using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RustPlusDesk.Models.Raid;

public sealed class RaidDataSet
{
    public int SchemaVersion { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public List<RaidSource> Sources { get; init; } = [];
    public List<RaidTarget> Targets { get; init; } = [];
    public Dictionary<long, Dictionary<long, double>> DamagePerHit { get; init; } = [];
    public Dictionary<long, Dictionary<long, int>> Hits { get; init; } = [];
}

public sealed class RaidSource
{
    public long SourceId { get; init; }
    public string PrefabName { get; init; } = string.Empty;
    public int? ItemId { get; init; }
    public string ItemShortname { get; init; } = string.Empty;
    public string ItemSlug { get; init; } = string.Empty;
    public string ItemCategorySlug { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public double RawDamage { get; init; }
    public Dictionary<string, double> DamageTypes { get; init; } = [];
    public List<RaidResourceCost>? CraftCost { get; init; }
    public int? WorkbenchLevelRequired { get; init; }
}

public sealed class RaidTarget
{
    public long TargetId { get; init; }
    public string PrefabName { get; init; } = string.Empty;
    public int? ItemId { get; init; }
    public string? ItemShortname { get; init; }
    public string? ItemSlug { get; init; }
    public string? ItemCategorySlug { get; init; }
    public string? BuildingSlug { get; init; }
    public string? BuildingImage { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? BuildingTier { get; init; }
    public string ComponentType { get; init; } = string.Empty;
    public double StartHealth { get; init; }

    [JsonIgnore]
    public string Category => ComponentType switch
    {
        "Door" or "Gate" => "Doors & gates",
        "BuildingBlock" or "SimpleBuildingBlock" => "Building structures",
        "Barricade" when ItemCategorySlug == "traps" => "Traps",
        "Barricade" => "Barricades",
        "BaseOven" or "BoxStorage" => "Deployables",
        _ => "Other"
    };
}

public sealed class RaidResourceCost
{
    public string Shortname { get; init; } = string.Empty;
    public int ItemId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public double Amount { get; init; }
}

public sealed record RaidMethodResult(
    RaidSource Source,
    int RequiredItems,
    double DamagePerItem,
    double TotalDamage,
    double Overkill,
    IReadOnlyList<RaidResourceTotal> Resources,
    bool HasCraftCost);

public sealed record RaidResourceTotal(string Shortname, int ItemId, string DisplayName, double Amount);

public sealed record RaidItemTotal(RaidSource Source, int Amount);

public sealed record RaidPlanEntry(long TargetId, int Quantity, long SourceId);

public enum RaidComparisonMode
{
    LowestSulfur,
    LowestTotalResources,
    FewestRaidItems,
    Custom
}
