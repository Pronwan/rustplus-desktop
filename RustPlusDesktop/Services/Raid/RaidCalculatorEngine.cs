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

    public static RaidMethodResult? Recommend(IEnumerable<RaidMethodResult> methods, RaidComparisonMode mode)
    {
        var available = methods.ToList();
        if (available.Count == 0 || mode == RaidComparisonMode.Custom)
            return null;

        return mode switch
        {
            RaidComparisonMode.LowestSulfur => available.Where(method => method.HasCraftCost)
                .OrderBy(method => method.Resources.FirstOrDefault(cost => cost.Shortname == "sulfur")?.Amount ?? 0)
                .ThenBy(method => method.RequiredItems).FirstOrDefault(),
            RaidComparisonMode.LowestTotalResources => available.Where(method => method.HasCraftCost)
                .OrderBy(method => method.Resources.Sum(cost => cost.Amount)).ThenBy(method => method.RequiredItems).FirstOrDefault(),
            RaidComparisonMode.FewestRaidItems => available.OrderBy(method => method.RequiredItems)
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
