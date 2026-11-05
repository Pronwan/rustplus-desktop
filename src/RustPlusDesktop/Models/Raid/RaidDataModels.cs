using System;
using System.Collections.Generic;
using System.Linq;
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
    bool HasCraftCost)
{
    /// <summary>Raw sulfur required for this method — the community-standard raid-cost metric.</summary>
    public double SulfurCost => HasCraftCost
        ? Resources.Where(cost => cost.Shortname.Equals("sulfur", StringComparison.OrdinalIgnoreCase))
            .Sum(cost => cost.Amount)
        : 0;

    /// <summary>Workbench tier needed to craft this raid item, or null when it cannot be crafted.</summary>
    public int? WorkbenchLevel => Source.WorkbenchLevelRequired;

    /// <summary>True when this method has a craft cost that includes sulfur, so it can be sulfur-ranked.</summary>
    public bool IsSulfurRankable => HasCraftCost && SulfurCost > 0;

    /// <summary>
    /// True for the explosives players actually carry on a raid. Siege/situational tools
    /// (torpedo, cannonball, mortar, catapult, fire, bee, …) can be the lowest raw sulfur on
    /// paper but are impractical, so they never win the default recommendation.
    /// </summary>
    public bool IsStandardTool => RaidTools.IsStandard(Source.ItemShortname);

    /// <summary>Non-null for siege-delivered items (e.g. "Catapult"), so the UI can flag the delivery weapon.</summary>
    public string? DeliveryLabel => RaidTools.DeliveryLabel(Source.ItemShortname);
}

/// <summary>The mainstream raiding explosives, per the community raid-cost meta.</summary>
public static class RaidTools
{
    private static readonly HashSet<string> Standard = new(StringComparer.OrdinalIgnoreCase)
    {
        "explosive.timed",      // C4
        "ammo.rocket.basic",    // Rocket
        "ammo.rocket.hv",       // HV Rocket
        "explosive.satchel",    // Satchel Charge
        "ammo.rifle.explosive", // Explosive 5.56
        "grenade.beancan",      // Beancan Grenade
        "grenade.f1"            // F1 Grenade
    };

    public static bool IsStandard(string? shortname) => shortname is not null && Standard.Contains(shortname);

    /// <summary>
    /// How a raid item is delivered when it is not a hand-thrown/placed explosive. Siege deliveries
    /// (catapult, mortar, cannon, submarine, MLRS, grenade launcher) require a separate weapon, so the
    /// picker tags them — a catapult-launched propane bomb is a very different plan from placing C4.
    /// Returns null for the standard hand tools.
    /// </summary>
    public static string? DeliveryLabel(string? shortname)
    {
        if (string.IsNullOrEmpty(shortname)) return null;
        if (shortname.StartsWith("catapult.ammo", StringComparison.OrdinalIgnoreCase)) return "Catapult";
        if (shortname.StartsWith("ammo.mortar", StringComparison.OrdinalIgnoreCase)) return "Mortar";
        return shortname switch
        {
            "cannonball" => "Cannon",
            "submarine.torpedo.straight" => "Submarine",
            "ammo.rocket.mlrs" => "MLRS",
            "ammo.grenadelauncher.he" => "Launcher",
            _ => null
        };
    }
}

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
