using System;
using System.Collections.Generic;
using System.Linq;
using RustPlusDesk.Models.Raid;

namespace RustPlusDesk.Services.Raid;

public sealed class RaidCalculatorEngine(RaidDataSet data)
{
    private readonly Dictionary<long, RaidSource> _sources = data.Sources.ToDictionary(source => source.SourceId);

    public IReadOnlyList<RaidMethodResult> GetMethods(RaidTarget target, int targetQuantity = 1)
    {
        int quantity = Math.Max(1, targetQuantity);
        var methods = new List<RaidMethodResult>();
        foreach ((long sourceId, Dictionary<long, int> hitCounts) in data.Hits)
        {
            if (!_sources.TryGetValue(sourceId, out RaidSource? source) ||
                !hitCounts.TryGetValue(target.TargetId, out int hits) || hits <= 0)
                continue;

            // raid-data.json hit counts are authoritative and already rounded to whole raid items.
            // Multiply only after that rounding so a multi-target plan cannot under-count items.
            int requiredItems = checked(hits * quantity);
            double damage = data.DamagePerHit.GetValueOrDefault(sourceId)?.GetValueOrDefault(target.TargetId) ?? 0;
            IReadOnlyList<RaidResourceTotal> resources = source.CraftCost is null
                ? []
                : source.CraftCost.Select(cost => new RaidResourceTotal(
                    cost.Shortname, cost.ItemId, cost.DisplayName, cost.Amount * requiredItems)).ToList();
            double totalDamage = damage * requiredItems;
            methods.Add(new RaidMethodResult(
                source, requiredItems, damage, totalDamage, Math.Max(0, totalDamage - (target.StartHealth * quantity)), resources, source.CraftCost is not null));
        }
        return methods;
    }

    /// <summary>
    /// Orders the methods the way a raider reads a raid-cost chart: cheapest raw sulfur first,
    /// then the fewest explosives to deploy, then the lowest workbench tier. Craftable methods
    /// always rank above ones the dataset has no craft cost for (MLRS, 40mm, scattershot, …).
    /// </summary>
    public static IReadOnlyList<RaidMethodResult> RankBySulfur(IEnumerable<RaidMethodResult> methods) =>
        methods
            .OrderByDescending(method => method.IsSulfurRankable)
            .ThenBy(method => method.IsSulfurRankable ? method.SulfurCost : double.MaxValue)
            .ThenBy(method => method.RequiredItems)
            .ThenBy(method => method.WorkbenchLevel ?? int.MaxValue)
            .ThenBy(method => method.Source.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// The practical "best" method: cheapest raw sulfur among the mainstream raiding explosives
    /// (C4, rockets, satchels, explosive ammo, grenades). Siege/situational tools can be lower raw
    /// sulfur on paper but are never picked as the default. Falls back to any craftable method only
    /// when no standard tool can hit the target.
    /// </summary>
    public RaidMethodResult? BestMethod(RaidTarget target, int targetQuantity = 1)
    {
        List<RaidMethodResult> methods = GetMethods(target, targetQuantity)
            .Where(method => method.IsSulfurRankable).ToList();
        return methods
            .OrderByDescending(method => method.IsStandardTool)
            .ThenBy(method => method.SulfurCost)
            .ThenBy(method => method.RequiredItems)
            .ThenBy(method => method.WorkbenchLevel ?? int.MaxValue)
            .FirstOrDefault();
    }

    // Popular two-tool loadouts players actually combine on a raid. Each becomes one "mix" option
    // in the method picker, so the menu offers variety instead of a single blended result.
    private static readonly (string Big, string Small)[] CuratedPairs =
    {
        ("explosive.timed", "ammo.rocket.basic"),      // C4 + Rockets
        ("explosive.timed", "explosive.satchel"),      // C4 + Satchels
        ("ammo.rocket.basic", "explosive.satchel"),    // Rockets + Satchels
        ("explosive.timed", "ammo.rifle.explosive"),   // C4 + Explo ammo
        ("ammo.rocket.basic", "ammo.rifle.explosive"), // Rockets + Explo ammo
        ("explosive.satchel", "ammo.rifle.explosive")  // Satchels + Explo ammo
    };

    /// <summary>
    /// Builds a set of two-item mixes for a target — one per popular tool pair — each a balanced split
    /// that uses both explosives and wastes the least leftover damage. Gives the picker real variety
    /// beyond the single global smart mix.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<RaidMethodResult>> GetCuratedMixes(RaidTarget target, int targetQuantity = 1)
    {
        int quantity = Math.Max(1, targetQuantity);
        var mixes = new List<IReadOnlyList<RaidMethodResult>>();
        var seen = new HashSet<string>();

        foreach ((string bigShort, string smallShort) in CuratedPairs)
        {
            if (!TryTool(bigShort, target, out RaidSource big, out double bigDamage) ||
                !TryTool(smallShort, target, out RaidSource small, out double smallDamage))
                continue;

            // Always treat the higher-damage item as the "bulk" tool.
            if (smallDamage > bigDamage)
            {
                (big, small) = (small, big);
                (bigDamage, smallDamage) = (smallDamage, bigDamage);
            }

            (int bigCount, int smallCount)? split = BalancedSplit(target.StartHealth, bigDamage, smallDamage);
            if (split is null) continue;
            (int bigCount, int smallCount) = split.Value;

            string signature = $"{Math.Min(big.SourceId, small.SourceId)}:{Math.Max(big.SourceId, small.SourceId)}:{bigCount}:{smallCount}";
            if (!seen.Add(signature)) continue;

            double combinedDamage = ((bigCount * bigDamage) + (smallCount * smallDamage)) * quantity;
            double overkill = Math.Max(0, combinedDamage - (target.StartHealth * quantity));
            mixes.Add(
            [
                BuildMixPart(big, bigDamage, bigCount, quantity, overkill),
                BuildMixPart(small, smallDamage, smallCount, quantity, 0)
            ]);
        }
        return mixes;
    }

    private bool TryTool(string shortname, RaidTarget target, out RaidSource source, out double damagePerItem)
    {
        source = _sources.Values.FirstOrDefault(candidate =>
            string.Equals(candidate.ItemShortname, shortname, StringComparison.OrdinalIgnoreCase))!;
        damagePerItem = 0;
        return source is { CraftCost: not null }
            && data.DamagePerHit.TryGetValue(source.SourceId, out Dictionary<long, double>? row)
            && row.TryGetValue(target.TargetId, out damagePerItem)
            && damagePerItem > 0;
    }

    private static RaidMethodResult BuildMixPart(RaidSource source, double damagePerItem, int countPerTarget, int quantity, double overkill)
    {
        int requiredItems = countPerTarget * quantity;
        IReadOnlyList<RaidResourceTotal> resources = source.CraftCost is null
            ? []
            : source.CraftCost.Select(cost => new RaidResourceTotal(
                cost.Shortname, cost.ItemId, cost.DisplayName, cost.Amount * requiredItems)).ToList();
        return new RaidMethodResult(source, requiredItems, damagePerItem, damagePerItem * requiredItems,
            overkill, resources, source.CraftCost is not null);
    }

    /// <summary>
    /// Picks whole-item counts of a bulk item and a finisher item that reach the target's health using
    /// both, with the least leftover (overkill) damage, then the fewest total items. Both counts are ≥ 1
    /// so the result is always a genuine mix.
    /// </summary>
    private static (int Big, int Small)? BalancedSplit(double health, double bigDamage, double smallDamage)
    {
        if (bigDamage <= 0 || smallDamage <= 0) return null;
        int maxBig = Math.Max(1, (int)Math.Ceiling(health / bigDamage));
        (int Big, int Small)? best = null;
        double bestOverkill = double.MaxValue;
        int bestItems = int.MaxValue;

        for (int big = 1; big <= maxBig; big++)
        {
            double remaining = health - (big * bigDamage);
            int small = remaining <= 0 ? 1 : (int)Math.Ceiling(remaining / smallDamage);
            double overkill = (big * bigDamage) + (small * smallDamage) - health;
            int items = big + small;
            if (overkill < bestOverkill - 1e-9 || (Math.Abs(overkill - bestOverkill) < 1e-9 && items < bestItems))
            {
                best = (big, small);
                bestOverkill = overkill;
                bestItems = items;
            }
        }
        return best;
    }

    public static RaidMethodResult? Recommend(IEnumerable<RaidMethodResult> methods, RaidComparisonMode mode)
    {
        var available = methods.ToList();
        if (available.Count == 0 || mode == RaidComparisonMode.Custom)
            return null;

        // Every mode prefers the mainstream raiding explosives first, so the auto-pick never lands on a
        // siege/situational item (e.g. 400 torpedoes) that is only "cheapest" on a raw-sulfur technicality.
        return mode switch
        {
            RaidComparisonMode.LowestSulfur => available.Where(method => method.HasCraftCost)
                .OrderByDescending(method => method.IsStandardTool)
                .ThenBy(method => method.SulfurCost)
                .ThenBy(method => method.RequiredItems).FirstOrDefault(),
            RaidComparisonMode.LowestTotalResources => available.Where(method => method.HasCraftCost)
                .OrderByDescending(method => method.IsStandardTool)
                .ThenBy(method => method.Resources.Sum(cost => cost.Amount)).ThenBy(method => method.RequiredItems).FirstOrDefault(),
            RaidComparisonMode.FewestRaidItems => available
                .OrderByDescending(method => method.IsStandardTool)
                .ThenBy(method => method.RequiredItems)
                .ThenBy(method => method.HasCraftCost ? 0 : 1).First(),
            _ => null
        };
    }

    public IReadOnlyList<RaidMethodResult> GetBestCombination(
        RaidTarget target, IEnumerable<long> sourceIds, RaidComparisonMode mode, int targetQuantity = 1)
    {
        var methods = GetMethods(target)
            .Where(method => sourceIds.Contains(method.Source.SourceId))
            .Where(method => mode == RaidComparisonMode.FewestRaidItems || method.HasCraftCost)
            .ToList();
        if (methods.Count == 0) return [];

        int scale = 10_000;
        var scaledDamage = methods.Select(method => Math.Max(1, (int)Math.Round(method.DamagePerItem * scale))).ToArray();
        int divisor = scaledDamage.Aggregate(GreatestCommonDivisor);
        int health = Math.Max(1, (int)Math.Ceiling((target.StartHealth * scale) / divisor));
        if (health > 2_000_000)
        {
            // ponytail: hundredth-HP fallback caps memory; raise the cap if sub-cent raid damage enters the dataset.
            scale = 100;
            scaledDamage = methods.Select(method => Math.Max(1, (int)Math.Floor(method.DamagePerItem * scale))).ToArray();
            divisor = scaledDamage.Aggregate(GreatestCommonDivisor);
            health = Math.Max(1, (int)Math.Ceiling((target.StartHealth * scale) / divisor));
        }
        int[] damage = scaledDamage.Select(value => Math.Max(1, value / divisor)).ToArray();
        var best = new CombinationState?[health + 1];
        best[0] = new CombinationState(0, 0, 0, 0, -1, -1);

        for (int dealt = 0; dealt < health; dealt++)
        {
            CombinationState? current = best[dealt];
            if (current is null) continue;
            for (int methodIndex = 0; methodIndex < methods.Count; methodIndex++)
            {
                RaidMethodResult method = methods[methodIndex];
                int nextDamage = Math.Min(health, dealt + damage[methodIndex]);
                double sulfur = method.Source.CraftCost?.FirstOrDefault(resource =>
                    resource.Shortname.Equals("sulfur", StringComparison.OrdinalIgnoreCase))?.Amount ?? 0;
                double totalResources = method.Source.CraftCost?.Sum(resource => resource.Amount) ?? 0;
                (double first, double second) = mode switch
                {
                    RaidComparisonMode.LowestTotalResources => (totalResources, sulfur),
                    RaidComparisonMode.FewestRaidItems => (1, sulfur),
                    _ => (sulfur, totalResources)
                };
                var candidate = new CombinationState(
                    current.FirstCost + first, current.SecondCost + second, current.Items + 1,
                    current.ActualDamage + method.DamagePerItem, dealt, methodIndex);
                if (best[nextDamage] is null || candidate.IsBetterThan(best[nextDamage]!))
                    best[nextDamage] = candidate;
            }
        }

        if (best[health] is null) return [];
        var counts = new int[methods.Count];
        for (int state = health; state > 0;)
        {
            CombinationState step = best[state]!;
            counts[step.MethodIndex]++;
            state = step.PreviousDamage;
        }

        int quantity = Math.Max(1, targetQuantity);
        return methods.Select((method, index) => (method, count: counts[index] * quantity))
            .Where(entry => entry.count > 0)
            .Select(entry => CreateResult(entry.method, entry.count, target.StartHealth * quantity))
            .ToList();
    }

    private static RaidMethodResult CreateResult(RaidMethodResult method, int count, double targetHealth)
    {
        IReadOnlyList<RaidResourceTotal> resources = method.HasCraftCost
            ? method.Source.CraftCost!.Select(cost => new RaidResourceTotal(
                cost.Shortname, cost.ItemId, cost.DisplayName, cost.Amount * count)).ToList()
            : [];
        double totalDamage = method.DamagePerItem * count;
        return new RaidMethodResult(method.Source, count, method.DamagePerItem, totalDamage,
            Math.Max(0, totalDamage - targetHealth), resources, method.HasCraftCost);
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0) (left, right) = (right, left % right);
        return Math.Abs(left);
    }

    private sealed record CombinationState(
        double FirstCost, double SecondCost, int Items, double ActualDamage, int PreviousDamage, int MethodIndex)
    {
        public bool IsBetterThan(CombinationState other) =>
            FirstCost < other.FirstCost ||
            (FirstCost == other.FirstCost && (SecondCost < other.SecondCost ||
             (SecondCost == other.SecondCost && (Items < other.Items ||
              (Items == other.Items && ActualDamage < other.ActualDamage)))));
    }

    public static IReadOnlyList<RaidResourceTotal> Aggregate(IEnumerable<RaidMethodResult> methods) =>
        methods.SelectMany(method => method.Resources)
            .GroupBy(resource => resource.Shortname, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RaidResourceTotal(
                group.Key, group.First().ItemId, group.First().DisplayName, group.Sum(resource => resource.Amount)))
            .OrderByDescending(resource => resource.Shortname.Equals("sulfur", StringComparison.OrdinalIgnoreCase))
            .ThenBy(resource => resource.DisplayName)
            .ToList();

    public static IReadOnlyList<RaidItemTotal> AggregateItems(IEnumerable<RaidMethodResult> methods) =>
        methods.GroupBy(method => method.Source.SourceId)
            .Select(group => new RaidItemTotal(group.First().Source, group.Sum(method => method.RequiredItems)))
            .OrderByDescending(item => item.Amount)
            .ThenBy(item => item.Source.DisplayName)
            .ToList();
}
