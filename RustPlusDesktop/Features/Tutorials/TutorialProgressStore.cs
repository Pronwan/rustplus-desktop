using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Features.Tutorials;

public interface ITutorialProgressStore
{
    Task<TutorialProgress> GetAsync(TutorialDefinition definition, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, TutorialProgress>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(TutorialProgress progress, CancellationToken cancellationToken = default);
    Task ResetAsync(string tutorialId, CancellationToken cancellationToken = default);
    Task ResetAllAsync(CancellationToken cancellationToken = default);
    Task<TutorialPreferences> GetPreferencesAsync(CancellationToken cancellationToken = default);
    Task SavePreferencesAsync(TutorialPreferences preferences, CancellationToken cancellationToken = default);
}

public sealed class TutorialProgressStore : ITutorialProgressStore
{
    private sealed class StoreData
    {
        public Dictionary<string, TutorialProgress> Tutorials { get; set; } = new(StringComparer.Ordinal);
        public TutorialPreferences Preferences { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TutorialProgressStore(string? path = null) =>
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustPlusDesk", "tutorial-progress.json");

    public async Task<TutorialProgress> GetAsync(TutorialDefinition definition, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await ReadAsync(cancellationToken);
            if (!data.Tutorials.TryGetValue(definition.Id, out var progress))
                return NewProgress(definition);

            if (progress.TutorialVersion < definition.Version && progress.Status is TutorialStatus.Completed or TutorialStatus.Skipped)
                progress.Status = TutorialStatus.Updated;
            progress.TutorialVersion = definition.Version;
            return progress;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyDictionary<string, TutorialProgress>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return (await ReadAsync(cancellationToken)).Tutorials; }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(TutorialProgress progress, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await ReadAsync(cancellationToken);
            data.Tutorials[progress.TutorialId] = progress;
            await WriteAsync(data, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task ResetAsync(string tutorialId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await ReadAsync(cancellationToken);
            data.Tutorials.Remove(tutorialId);
            await WriteAsync(data, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task ResetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await ReadAsync(cancellationToken);
            data.Tutorials.Clear();
            data.Preferences.LastTutorialId = null;
            await WriteAsync(data, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<TutorialPreferences> GetPreferencesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return (await ReadAsync(cancellationToken)).Preferences; }
        finally { _gate.Release(); }
    }

    public async Task SavePreferencesAsync(TutorialPreferences preferences, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await ReadAsync(cancellationToken);
            data.Preferences = preferences;
            await WriteAsync(data, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private static TutorialProgress NewProgress(TutorialDefinition definition) => new()
    {
        TutorialId = definition.Id,
        TutorialVersion = definition.Version,
        Status = TutorialStatus.NotStarted
    };

    private async Task<StoreData> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_path)) return new();
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<StoreData>(stream, JsonOptions, cancellationToken) ?? new();
        }
        catch (JsonException) { return new(); }
        catch (IOException) { return new(); }
    }

    private async Task WriteAsync(StoreData data, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temp = _path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, data, JsonOptions, cancellationToken);
        File.Move(temp, _path, true);
    }
}
