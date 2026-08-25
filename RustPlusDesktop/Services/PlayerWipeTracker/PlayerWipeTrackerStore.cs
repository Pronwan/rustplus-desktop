using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.PlayerWipeTracker;

/// <summary>Append-only JSON-lines storage isolated by server, wipe, and player.</summary>
public sealed class PlayerWipeTrackerStore : IAsyncDisposable
{
    private const int QueueCapacity = 2048;
    private readonly string _root;
    private readonly Channel<QueueItem> _queue = Channel.CreateBounded<QueueItem>(new BoundedChannelOptions(QueueCapacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
    });
    private readonly Task _writer;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record WriteItem(string Path, string Line) : QueueItem;
    private sealed record FlushItem(TaskCompletionSource<bool> Completion) : QueueItem;
    private sealed record StopItem(TaskCompletionSource<bool> Completion) : QueueItem;
    private abstract record QueueItem;

    public PlayerWipeTrackerStore(string rootDirectory)
    {
        _root = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_root);
        _writer = Task.Run(WriteLoopAsync);
    }

    public bool Append(string serverKey, string wipeKey, ulong steamId, TrackerPersistedObservation item)
    {
        var path = FilePath(serverKey, wipeKey, steamId);
        var line = JsonSerializer.Serialize(item, _json);
        var queued = new WriteItem(path, line);
        if (_queue.Writer.TryWrite(queued))
            return true;

        // ponytail: bounded queue drops the oldest pending line under sustained I/O pressure; replace with durable batching if this becomes observable.
        _queue.Reader.TryRead(out _);
        return _queue.Writer.TryWrite(queued);
    }

    public async Task AppendAsync(
        string serverKey,
        string wipeKey,
        ulong steamId,
        TrackerPersistedObservation item,
        CancellationToken cancellationToken = default)
    {
        var path = FilePath(serverKey, wipeKey, steamId);
        var line = JsonSerializer.Serialize(item, _json);
        await _queue.Writer.WriteAsync(new WriteItem(path, line), cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<TrackerPersistedObservation> Load(string serverKey, string wipeKey, ulong steamId)
    {
        var path = FilePath(serverKey, wipeKey, steamId);
        if (!File.Exists(path))
            return Array.Empty<TrackerPersistedObservation>();

        var result = new List<TrackerPersistedObservation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                var item = JsonSerializer.Deserialize<TrackerPersistedObservation>(line, _json);
                if (item is null)
                    continue;
                var key = $"{item.Kind}|{item.Observation.SessionId}|{item.Observation.TimestampUtc:O}";
                if (seen.Add(key))
                    result.Add(item);
            }
            catch (JsonException)
            {
                // One malformed JSONL line must not hide the remaining history.
            }
        }

        return result.OrderBy(x => x.Observation.TimestampUtc).ToArray();
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _queue.Writer.WriteAsync(new FlushItem(completion), cancellationToken).ConfigureAwait(false);
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public long StorageBytes => Directory.Exists(_root)
        ? Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length)
        : 0;

    public bool HasWipeMap(string serverKey, string wipeKey)
        => File.Exists(Path.Combine(WipeDirectory(serverKey, wipeKey), "map.png"))
            && File.Exists(Path.Combine(WipeDirectory(serverKey, wipeKey), "map.json"));

    public void SaveWipeMap(string serverKey, string wipeKey, TrackerWipeMap map)
    {
        var directory = WipeDirectory(serverKey, wipeKey);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "map.png"), map.PngBytes);
        File.WriteAllText(Path.Combine(directory, "map.json"), JsonSerializer.Serialize(map with { PngBytes = Array.Empty<byte>() }, _json));
    }

    public TrackerWipeMap? LoadWipeMap(string serverKey, string wipeKey)
    {
        var directory = WipeDirectory(serverKey, wipeKey);
        var imagePath = Path.Combine(directory, "map.png");
        var metadataPath = Path.Combine(directory, "map.json");
        if (!File.Exists(imagePath) || !File.Exists(metadataPath))
            return null;
        try
        {
            var metadata = JsonSerializer.Deserialize<TrackerWipeMap>(File.ReadAllText(metadataPath), _json);
            return metadata is null ? null : metadata with { PngBytes = File.ReadAllBytes(imagePath) };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public IReadOnlyList<ulong> LoadPlayerIds(string serverKey, string wipeKey)
    {
        var directory = Path.Combine(_root, Safe(serverKey), Safe(wipeKey));
        if (!Directory.Exists(directory))
            return Array.Empty<ulong>();
        return Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(value => ulong.TryParse(value, out _))
            .Select(ulong.Parse)
            .Distinct()
            .ToArray();
    }

    public void SaveWipeMetadata(string serverKey, string wipeKey, StoredWipeMetadata metadata)
    {
        var directory = WipeDirectory(serverKey, wipeKey);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "wipe_info.json"), JsonSerializer.Serialize(metadata, _json));
    }

    public StoredWipeMetadata? LoadWipeMetadata(string serverKey, string wipeKey)
    {
        var path = Path.Combine(WipeDirectory(serverKey, wipeKey), "wipe_info.json");
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<StoredWipeMetadata>(File.ReadAllText(path), _json);
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<StoredWipeSummary> GetStoredWipes()
    {
        if (!Directory.Exists(_root))
            return Array.Empty<StoredWipeSummary>();

        var results = new List<StoredWipeSummary>();
        foreach (var serverDir in Directory.EnumerateDirectories(_root))
        {
            var serverKey = Path.GetFileName(serverDir);
            foreach (var wipeDir in Directory.EnumerateDirectories(serverDir))
            {
                var wipeKey = Path.GetFileName(wipeDir);
                var meta = LoadWipeMetadata(serverKey, wipeKey);
                var jsonlFiles = Directory.EnumerateFiles(wipeDir, "*.jsonl").ToArray();
                if (jsonlFiles.Length == 0 && !File.Exists(Path.Combine(wipeDir, "map.png")))
                    continue;

                var serverName = meta?.ServerName ?? serverKey;
                var wipeStarted = meta?.WipeStartedAtUtc;
                var playerCount = jsonlFiles.Length;
                var totalBytes = Directory.EnumerateFiles(wipeDir, "*").Sum(f => new FileInfo(f).Length);
                DateTime? lastObserved = null;

                if (jsonlFiles.Length > 0)
                {
                    lastObserved = jsonlFiles.Max(f => new FileInfo(f).LastWriteTimeUtc);
                }

                results.Add(new StoredWipeSummary(
                    serverKey,
                    serverName,
                    wipeKey,
                    wipeStarted,
                    lastObserved,
                    playerCount,
                    0,
                    totalBytes,
                    HasWipeMap(serverKey, wipeKey)));
            }
        }

        return results
            .OrderByDescending(w => w.LastObservedAtUtc ?? w.WipeStartedAtUtc ?? DateTime.MinValue)
            .ToArray();
    }

    public void DeleteWipe(string serverKey, string wipeKey)
    {
        var directory = Path.Combine(_root, Safe(serverKey), Safe(wipeKey));
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    public void DeleteAll()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        Directory.CreateDirectory(_root);
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                switch (item)
                {
                    case WriteItem write:
                        Directory.CreateDirectory(Path.GetDirectoryName(write.Path)!);
                        await File.AppendAllTextAsync(write.Path, write.Line + Environment.NewLine, _shutdown.Token).ConfigureAwait(false);
                        break;
                    case FlushItem flush:
                        flush.Completion.TrySetResult(true);
                        break;
                    case StopItem stop:
                        stop.Completion.TrySetResult(true);
                        return;
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch { }
    }

    private string FilePath(string serverKey, string wipeKey, ulong steamId)
        => Path.Combine(WipeDirectory(serverKey, wipeKey), $"{steamId}.jsonl");

    private string WipeDirectory(string serverKey, string wipeKey)
        => Path.Combine(_root, Safe(serverKey), Safe(wipeKey));

    private static string Safe(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe[..Math.Min(safe.Length, 120)];
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await FlushAsync().ConfigureAwait(false);
        }
        catch { }
        _queue.Writer.TryComplete();
        _shutdown.Cancel();
        try { await _writer.ConfigureAwait(false); } catch { }
        _shutdown.Dispose();
    }
}
