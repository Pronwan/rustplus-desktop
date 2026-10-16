using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.PlayerWipeTracker;

/// <summary>
/// Append-only storage isolated by server, wipe, and player: one compact binary file per player
/// (see <see cref="TrackerRecordFile"/>). Older JSON-lines files are converted, and thinned, the
/// first time they are touched or by the background sweep shortly after start.
/// </summary>
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

    private sealed record WriteItem(string Path, ulong SteamId, PlayerObservation Observation) : QueueItem;
    private sealed record FlushItem(TaskCompletionSource<bool> Completion) : QueueItem;
    private sealed record StopItem(TaskCompletionSource<bool> Completion) : QueueItem;
    private abstract record QueueItem;

    /// <summary>
    /// Per-file lock and the string table needed to append to it. The table is rebuilt from the
    /// file on first use and then kept, so an append never has to read the history again.
    /// </summary>
    private sealed class FileEntry
    {
        public readonly object Gate = new();
        public TrackerRecordFile? State;
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, FileEntry> _files = new(StringComparer.OrdinalIgnoreCase);

    private const string RecordExtension = ".pwt";
    private const string LegacyExtension = ".jsonl";

    public PlayerWipeTrackerStore(string rootDirectory)
    {
        _root = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_root);
        _writer = Task.Run(WriteLoopAsync);
        _ = Task.Run(MigrateLegacyFilesAsync);
    }

    public bool Append(string serverKey, string wipeKey, ulong steamId, TrackerPersistedObservation item)
    {
        var queued = new WriteItem(FilePath(serverKey, wipeKey, steamId), steamId, item.Observation);
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
        await _queue.Writer.WriteAsync(new WriteItem(FilePath(serverKey, wipeKey, steamId), steamId, item.Observation), cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<TrackerPersistedObservation> Load(string serverKey, string wipeKey, ulong steamId)
    {
        var path = FilePath(serverKey, wipeKey, steamId);
        var entry = Entry(path);
        var observations = new List<PlayerObservation>();
        lock (entry.Gate)
        {
            if (!MigrateLegacy(path, steamId, entry))
            {
                // Could not convert (locked, disk full): read the old file as it is.
                observations.AddRange(ReadLegacy(LegacyPath(path)));
            }

            if (File.Exists(path))
            {
                var bytes = ReadAllBytesShared(path);
                var state = TrackerRecordFile.Parse(bytes, steamId, observations);
                if (state is not null)
                    entry.State = state;
            }
        }

        return Distinct(observations)
            .Select(observation => new TrackerPersistedObservation(1, "observation", observation))
            .ToArray();
    }

    /// <summary>
    /// Time order with exact repeats dropped. A cloud restore of a wipe that is already on disk
    /// appends the same observations again; they are identical by session and timestamp.
    /// </summary>
    private static List<PlayerObservation> Distinct(List<PlayerObservation> observations)
    {
        var seen = new HashSet<(string, long)>();
        var result = new List<PlayerObservation>(observations.Count);
        foreach (var observation in observations)
        {
            if (seen.Add((observation.SessionId, observation.TimestampUtc.Ticks)))
                result.Add(observation);
        }

        // Stable, so observations sharing a timestamp keep the order they were written in.
        var ordered = result.OrderBy(observation => observation.TimestampUtc).ToList();
        return ordered;
    }

    private FileEntry Entry(string path) => _files.GetOrAdd(path, _ => new FileEntry());

    private static byte[] ReadAllBytesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>The string table and readable length of a file, read once and then kept.</summary>
    private TrackerRecordFile? EnsureState(string path, ulong steamId, FileEntry entry)
    {
        if (entry.State is not null)
            return entry.State;
        if (!File.Exists(path))
            return null;
        entry.State = TrackerRecordFile.Parse(ReadAllBytesShared(path), steamId, sink: null);
        return entry.State;
    }

    private void AppendRecord(string path, ulong steamId, PlayerObservation observation)
    {
        var entry = Entry(path);
        lock (entry.Gate)
        {
            MigrateLegacy(path, steamId, entry);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var state = EnsureState(path, steamId, entry);
            if (state is null && File.Exists(path))
            {
                // Not ours (or a header torn by a crash on the very first write). Keep the bytes
                // aside instead of appending records nobody could read back.
                File.Move(path, path + ".unreadable", overwrite: true);
            }

            try
            {
                using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
                if (state is null)
                {
                    state = TrackerRecordFile.CreateNew(observation.TimestampUtc);
                    stream.SetLength(0);
                    stream.Write(TrackerRecordFile.Header(state.BaseTicks));
                    entry.State = state;
                }
                else if (stream.Length != state.ValidLength)
                {
                    // A record cut short by a crash would swallow everything after it.
                    stream.SetLength(state.ValidLength);
                }

                stream.Seek(0, SeekOrigin.End);
                var bytes = state.Encode(observation);
                stream.Write(bytes);
                state.Appended(bytes.Length);
            }
            catch
            {
                // The table may now name strings the file never received; rebuild it from disk.
                entry.State = null;
                throw;
            }
        }
    }

    /// <summary>
    /// Converts a player's JSON-lines history into the record format, thinned the way the
    /// engine now stores live data. Returns false only when a legacy file exists but could not
    /// be converted.
    ///
    /// Thinning is what reclaims the space: the old writer stored almost every five-second poll,
    /// so a stationary or offline hour cost 720 records where one per heartbeat says the same.
    /// Anything already in the record file (from a cloud restore, say) is merged in, so the
    /// conversion never loses observations whichever order the two were written in.
    /// </summary>
    private bool MigrateLegacy(string path, ulong steamId, FileEntry entry)
    {
        var legacyPath = LegacyPath(path);
        if (!File.Exists(legacyPath))
            return true;

        var temporary = path + ".tmp";
        try
        {
            var observations = ReadLegacy(legacyPath);
            if (File.Exists(path))
                TrackerRecordFile.Parse(ReadAllBytesShared(path), steamId, observations);

            var kept = PlayerWipeTrackerEngine.Compact(Distinct(observations));
            var state = TrackerRecordFile.CreateNew(kept.Count > 0 ? kept[0].TimestampUtc : DateTime.UtcNow);
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(TrackerRecordFile.Header(state.BaseTicks));
                foreach (var observation in kept)
                {
                    var bytes = state.Encode(observation);
                    stream.Write(bytes);
                    state.Appended(bytes.Length);
                }
            }

            File.Move(temporary, path, overwrite: true);
            File.Delete(legacyPath);
            entry.State = state;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporary); } catch { }
            return false;
        }
    }

    private List<PlayerObservation> ReadLegacy(string legacyPath)
    {
        var result = new List<PlayerObservation>();
        if (!File.Exists(legacyPath))
            return result;

        foreach (var line in File.ReadLines(legacyPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                var item = JsonSerializer.Deserialize<TrackerPersistedObservation>(line, _json);
                if (item?.Observation is not null && item.Kind == "observation")
                    result.Add(item.Observation with { TimestampUtc = item.Observation.TimestampUtc.ToUniversalTime() });
            }
            catch (JsonException)
            {
                // One malformed JSONL line must not hide the remaining history.
            }
        }
        return result;
    }

    /// <summary>
    /// Converts every remaining JSON-lines history in the background, so the disk space comes
    /// back without each wipe having to be opened first. Waits a little so it does not compete
    /// with start-up, and goes one file at a time.
    /// </summary>
    private async Task MigrateLegacyFilesAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), _shutdown.Token).ConfigureAwait(false);
            foreach (var legacyPath in Directory.EnumerateFiles(_root, "*" + LegacyExtension, SearchOption.AllDirectories).ToArray())
            {
                if (_shutdown.IsCancellationRequested)
                    return;
                if (!ulong.TryParse(Path.GetFileNameWithoutExtension(legacyPath), NumberStyles.None, CultureInfo.InvariantCulture, out var steamId))
                    continue;
                var path = Path.ChangeExtension(legacyPath, RecordExtension);
                var entry = Entry(path);
                lock (entry.Gate)
                    MigrateLegacy(path, steamId, entry);
                await Task.Yield();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
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

    public bool IsWipeMapUploaded(string serverKey, string wipeKey)
        => File.Exists(Path.Combine(WipeDirectory(serverKey, wipeKey), ".map_uploaded"));

    public void MarkWipeMapUploaded(string serverKey, string wipeKey)
    {
        try
        {
            var directory = WipeDirectory(serverKey, wipeKey);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, ".map_uploaded"), DateTime.UtcNow.ToString("o"));
        }
        catch
        {
            // Ignore marker write error
        }
    }

    public bool IsExtraMonumentsUploaded(string serverKey, string wipeKey)
        => File.Exists(Path.Combine(WipeDirectory(serverKey, wipeKey), ".extra_monuments_uploaded"));

    public void MarkExtraMonumentsUploaded(string serverKey, string wipeKey)
    {
        try
        {
            var directory = WipeDirectory(serverKey, wipeKey);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, ".extra_monuments_uploaded"), DateTime.UtcNow.ToString("o"));
        }
        catch
        {
            // Ignore marker write error
        }
    }

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
        return PlayerFiles(directory)
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

        var profiles = LoadProfilesSafe();
        var results = new List<StoredWipeSummary>();
        foreach (var serverDir in Directory.EnumerateDirectories(_root))
        {
            var serverKey = Path.GetFileName(serverDir);
            foreach (var wipeDir in Directory.EnumerateDirectories(serverDir))
            {
                var wipeKey = Path.GetFileName(wipeDir);
                var playerFiles = PlayerFiles(wipeDir)
                    .Where(f => new FileInfo(f).Length > TrackerRecordFile.HeaderLength)
                    .ToArray();

                // Only show folders that have actual wipe tracking records
                if (playerFiles.Length == 0)
                    continue;

                var meta = LoadWipeMetadata(serverKey, wipeKey);
                var serverName = meta?.ServerName;
                if (string.IsNullOrWhiteSpace(serverName) || IsRawServerKey(serverName))
                {
                    serverName = ResolveServerName(serverKey, profiles);
                    if (!string.IsNullOrWhiteSpace(serverName) && !IsRawServerKey(serverName))
                    {
                        SaveWipeMetadata(serverKey, wipeKey, new StoredWipeMetadata(
                            serverKey,
                            serverName,
                            wipeKey,
                            meta?.WipeStartedAtUtc,
                            meta?.CreatedAtUtc ?? DateTime.UtcNow));
                    }
                }

                var wipeStarted = meta?.WipeStartedAtUtc;
                var playerCount = playerFiles.Select(Path.GetFileNameWithoutExtension).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                var totalBytes = Directory.EnumerateFiles(wipeDir, "*").Sum(f => new FileInfo(f).Length);
                var lastObserved = playerFiles.Max(f => new FileInfo(f).LastWriteTimeUtc);

                results.Add(new StoredWipeSummary(
                    serverKey,
                    serverName ?? serverKey,
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

    public static bool IsRawServerKey(string? name)
        => string.IsNullOrWhiteSpace(name) ||
           name.All(c => char.IsDigit(c) || c == '.' || c == ':' || c == '-' || c == '_') ||
           name.StartsWith("unknown", StringComparison.OrdinalIgnoreCase);

    public static List<RustPlusDesk.Models.ServerProfile> LoadProfilesSafe()
    {
        try { return RustPlusDesk.Services.Data.ProfileDataModule.LoadProfiles(); }
        catch { return new List<RustPlusDesk.Models.ServerProfile>(); }
    }

    public static string ResolveServerName(string serverKey, List<RustPlusDesk.Models.ServerProfile>? profiles = null)
    {
        if (string.IsNullOrWhiteSpace(serverKey))
            return "Server";

        profiles ??= LoadProfilesSafe();
        var cleanKey = serverKey.Replace(":", "-").Replace("_", "-").Trim();
        foreach (var p in profiles)
        {
            if (string.IsNullOrWhiteSpace(p.Host))
                continue;

            var pHostKey = $"{p.Host}-{p.Port}".Replace(":", "-").Replace("_", "-").Trim();
            if (string.Equals(cleanKey, pHostKey, StringComparison.OrdinalIgnoreCase) ||
                cleanKey.StartsWith(p.Host, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(p.Name))
                    return p.Name;
            }
        }

        return serverKey;
    }

    public void DeleteWipe(string serverKey, string wipeKey)
    {
        var directory = Path.Combine(_root, Safe(serverKey), Safe(wipeKey));
        ForgetFiles(directory);
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    public void DeleteAll()
    {
        ForgetFiles(_root);
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        Directory.CreateDirectory(_root);
    }

    private void ForgetFiles(string directory)
    {
        var prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var path in _files.Keys.Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray())
            _files.TryRemove(path, out _);
    }

    /// <summary>Player history files in one wipe folder, in either format.</summary>
    private static IEnumerable<string> PlayerFiles(string directory)
        => Directory.EnumerateFiles(directory, "*" + RecordExtension, SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(directory, "*" + LegacyExtension, SearchOption.TopDirectoryOnly));

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                switch (item)
                {
                    case WriteItem write:
                        try
                        {
                            AppendRecord(write.Path, write.SteamId, write.Observation);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            // One failed write must not stop the loop for every later one.
                        }
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
        => Path.Combine(WipeDirectory(serverKey, wipeKey), $"{steamId}{RecordExtension}");

    private static string LegacyPath(string path) => Path.ChangeExtension(path, LegacyExtension);

    /// <summary>
    /// One day's observations, without reading the days before it.
    ///
    /// These files reach tens of megabytes over a wipe, and the cloud upload only ever wants the
    /// current day — so parsing the whole history once a minute, per player, was most of the
    /// tracker's cost after the network traffic was dealt with. The file is append-only and
    /// written in time order, so the byte offset where a day begins never moves once found:
    /// callers keep it and hand it back, and the read starts there.
    ///
    /// <paramref name="fromOffset"/> means "everything before this byte is already dealt with".
    /// The returned NextOffset points just past the last complete record read, so a caller that
    /// consumed everything can hand it back next time and read only what was appended since.
    /// A stale or impossible offset costs a larger read, never a missed observation.
    /// </summary>
    public (IReadOnlyList<TrackerPersistedObservation> Items, long NextOffset) LoadDay(
        string serverKey,
        string wipeKey,
        ulong steamId,
        DateOnly day,
        long fromOffset)
    {
        var path = FilePath(serverKey, wipeKey, steamId);
        var entry = Entry(path);
        try
        {
            lock (entry.Gate)
            {
                MigrateLegacy(path, steamId, entry);
                var state = EnsureState(path, steamId, entry);
                if (state is null)
                    return (Array.Empty<TrackerPersistedObservation>(), 0);

                // A hint outside the readable part means the file was rebuilt underneath it;
                // start over rather than silently reading nothing.
                var start = fromOffset >= TrackerRecordFile.HeaderLength && fromOffset <= state.ValidLength
                    ? fromOffset
                    : TrackerRecordFile.HeaderLength;
                var remaining = (int)(state.ValidLength - start);
                if (remaining <= 0)
                    return (Array.Empty<TrackerPersistedObservation>(), start);

                var buffer = new byte[remaining];
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    stream.Seek(start, SeekOrigin.Begin);
                    stream.ReadExactly(buffer, 0, remaining);
                }

                var observations = new List<PlayerObservation>();
                var consumed = state.Read(buffer, steamId, observations);
                var items = Distinct(observations
                        .Where(observation => DateOnly.FromDateTime(observation.TimestampUtc) == day)
                        .ToList())
                    .Select(observation => new TrackerPersistedObservation(1, "observation", observation))
                    .ToArray();
                return (items, start + consumed);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (Array.Empty<TrackerPersistedObservation>(), fromOffset);
        }
    }

    /// <summary>
    /// The newest observation timestamp the cloud has confirmed for this player-day, or null
    /// when nothing has been acknowledged yet.
    ///
    /// Persisted rather than held in memory: without it a restart would have no idea what the
    /// server already holds and would send the day from the beginning again, which is the very
    /// cost the incremental upload exists to avoid.
    /// </summary>
    /// <summary>
    /// What the cloud has confirmed for this player-day, plus where that day starts in the
    /// player's file. The offset is only a read hint — see <see cref="LoadDay"/>.
    /// </summary>
    public (DateTime? LastObservedUtc, long DayStartOffset) GetCloudCursor(
        string serverKey, string wipeKey, ulong steamId, DateOnly day)
    {
        try
        {
            var path = CloudCursorFile(serverKey, wipeKey, steamId, day);
            if (!File.Exists(path)) return (null, 0);

            // "<iso>|b<offset>". Older files hold the timestamp alone, or a byte offset into the
            // JSON-lines file that no longer exists; both read back with a zero offset, which
            // costs one full scan and then writes the current form.
            var parts = File.ReadAllText(path).Trim().Split('|');
            var timestamp = DateTime.TryParse(parts[0], CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var value)
                ? value.ToUniversalTime()
                : (DateTime?)null;
            var offset = parts.Length > 1 && parts[1].StartsWith(RecordOffsetPrefix, StringComparison.Ordinal) &&
                long.TryParse(parts[1].AsSpan(RecordOffsetPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
            return (timestamp, offset);
        }
        catch
        {
            return (null, 0);
        }
    }

    public void SetCloudCursor(string serverKey, string wipeKey, ulong steamId, DateOnly day, DateTime lastObservedUtc, long dayStartOffset)
    {
        try
        {
            var path = CloudCursorFile(serverKey, wipeKey, steamId, day);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,
                lastObservedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                + "|" + RecordOffsetPrefix + dayStartOffset.ToString(CultureInfo.InvariantCulture));
        }
        catch
        {
            // A lost cursor costs one repeated batch, never correctness: the server merges
            // by timestamp, so re-sending what it already has changes nothing.
        }
    }

    /// <summary>Marks an offset into the record file, as opposed to one into the old JSON lines.</summary>
    private const string RecordOffsetPrefix = "b";

    private string CloudCursorFile(string serverKey, string wipeKey, ulong steamId, DateOnly day)
        => Path.Combine(WipeDirectory(serverKey, wipeKey), $".cloud_cursor_{steamId}_{day:yyyyMMdd}");

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
