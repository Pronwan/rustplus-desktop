using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RustPlusDesk.Models.Raid;

namespace RustPlusDesk.Services.Raid;

public sealed class RaidDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<RaidDataSet> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using Stream stream = OpenDataStream();
        var data = await JsonSerializer.DeserializeAsync<RaidDataSet>(stream, JsonOptions, cancellationToken)
                   ?? throw new InvalidDataException("Raid data is empty.");
        RemoveUnsupportedTargets(data);
        Validate(data);
        return data;
    }

    private static void RemoveUnsupportedTargets(RaidDataSet data)
    {
        HashSet<long> targetIds = data.Targets
            .Where(target => target.PrefabName.Contains("/building boat/", StringComparison.OrdinalIgnoreCase))
            .Select(target => target.TargetId)
            .ToHashSet();
        if (targetIds.Count == 0) return;

        data.Targets.RemoveAll(target => targetIds.Contains(target.TargetId));
        foreach (Dictionary<long, double> values in data.DamagePerHit.Values)
            foreach (long targetId in targetIds) values.Remove(targetId);
        foreach (Dictionary<long, int> values in data.Hits.Values)
            foreach (long targetId in targetIds) values.Remove(targetId);
    }

    private static Stream OpenDataStream()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Data", "raid-data.json");
        if (File.Exists(filePath))
            return File.OpenRead(filePath);

        Assembly assembly = typeof(RaidDataService).Assembly;
        string? resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("Assets.Data.raid-data.json", StringComparison.OrdinalIgnoreCase));
        return resourceName is not null
            ? assembly.GetManifestResourceStream(resourceName)!
            : throw new FileNotFoundException("The packaged raid-data.json asset is missing.", filePath);
    }

    public static void Validate(RaidDataSet data)
    {
        if (data.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported raid data schema version {data.SchemaVersion}.");
        if (data.Sources.Count == 0 || data.Targets.Count == 0 || data.Hits.Count == 0 || data.DamagePerHit.Count == 0)
            throw new InvalidDataException("Raid data must contain sources, targets, hit counts, and damage values.");

        var sourceIds = new HashSet<long>();
        foreach (RaidSource source in data.Sources)
        {
            if (source.SourceId <= 0 || !sourceIds.Add(source.SourceId) || string.IsNullOrWhiteSpace(source.DisplayName))
                throw new InvalidDataException("Raid data contains an invalid or duplicate source.");
            if (!double.IsFinite(source.RawDamage) || source.RawDamage < 0 ||
                source.DamageTypes.Any(value => !double.IsFinite(value.Value) || value.Value < 0) ||
                source.CraftCost?.Any(cost => string.IsNullOrWhiteSpace(cost.Shortname) || !double.IsFinite(cost.Amount) || cost.Amount < 0) == true)
                throw new InvalidDataException($"Raid source '{source.DisplayName}' contains malformed numeric data.");
        }

        var targetIds = new HashSet<long>();
        foreach (RaidTarget target in data.Targets)
        {
            if (target.TargetId <= 0 || !targetIds.Add(target.TargetId) || string.IsNullOrWhiteSpace(target.DisplayName) ||
                !double.IsFinite(target.StartHealth) || target.StartHealth <= 0)
                throw new InvalidDataException("Raid data contains an invalid or duplicate target.");
        }

        ValidateMatrix(data.DamagePerHit, sourceIds, targetIds, value => double.IsFinite(value) && value > 0, "damage");
        ValidateMatrix(data.Hits, sourceIds, targetIds, value => value > 0, "hit count");
    }

    private static void ValidateMatrix<T>(
        Dictionary<long, Dictionary<long, T>> matrix,
        HashSet<long> sourceIds,
        HashSet<long> targetIds,
        Func<T, bool> validValue,
        string valueName)
    {
        foreach ((long sourceId, Dictionary<long, T> values) in matrix)
        {
            if (!sourceIds.Contains(sourceId))
                throw new InvalidDataException($"Raid matrix references unknown source {sourceId}.");
            foreach ((long targetId, T value) in values)
            {
                if (!targetIds.Contains(targetId) || !validValue(value))
                    throw new InvalidDataException($"Raid matrix contains an invalid {valueName} for target {targetId}.");
            }
        }
    }
}
