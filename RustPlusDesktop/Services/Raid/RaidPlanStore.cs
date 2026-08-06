using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RustPlusDesk.Models.Raid;

namespace RustPlusDesk.Services.Raid;

public sealed class RaidPlanStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public RaidPlanStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RustPlusDesk", "raid-plan.json");
    }

    public async Task<IReadOnlyList<RaidPlanEntry>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return [];
        try
        {
            await using FileStream stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<List<RaidPlanEntry>>(stream, JsonOptions, cancellationToken) ?? [];
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public async Task SaveAsync(IEnumerable<RaidPlanEntry> entries, CancellationToken cancellationToken = default)
    {
        string directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = _path + ".tmp";
        await using (FileStream stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, entries, JsonOptions, cancellationToken);
        File.Move(temporaryPath, _path, true);
    }
}
