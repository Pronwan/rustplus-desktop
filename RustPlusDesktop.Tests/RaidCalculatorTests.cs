using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models.Raid;
using RustPlusDesk.Services.Raid;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class RaidCalculatorTests
{
    private RaidDataSet _data = null!;
    private RaidCalculatorEngine _engine = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _data = await new RaidDataService().LoadAsync();
        _engine = new RaidCalculatorEngine(_data);
    }

    [TestMethod]
    public void SingleTargetUsesDatasetHitCountAndCraftCost()
    {
        RaidTarget target = _data.Targets.First(item => item.DisplayName == "Armored Door");
        RaidSource source = _data.Sources.First(item => item.DisplayName == "Timed Explosive Charge");

        RaidMethodResult result = _engine.GetMethods(target).Single(item => item.Source.SourceId == source.SourceId);

        Assert.AreEqual(_data.Hits[source.SourceId][target.TargetId], result.RequiredItems);
        foreach (RaidResourceCost cost in source.CraftCost!)
            Assert.AreEqual(cost.Amount * result.RequiredItems, result.Resources.Single(item => item.Shortname == cost.Shortname).Amount);
    }

    [TestMethod]
    public void TargetQuantityMultipliesWholeDatasetHitCount()
    {
        RaidTarget target = _data.Targets.First(item => item.DisplayName == "Armored Door");
        RaidSource source = _data.Sources.First(item => item.DisplayName == "Rocket");
        int datasetHits = _data.Hits[source.SourceId][target.TargetId];

        RaidMethodResult result = _engine.GetMethods(target, 3).Single(item => item.Source.SourceId == source.SourceId);

        Assert.AreEqual(datasetHits * 3, result.RequiredItems);
        Assert.AreEqual(Math.Ceiling(target.StartHealth / result.DamagePerItem), datasetHits);
    }

    [TestMethod]
    public void MultipleTargetsAndMethodsAggregateSharedResources()
    {
        RaidTarget firstTarget = _data.Targets.First(item => item.DisplayName == "Armored Door");
        RaidTarget secondTarget = _data.Targets.First(item => item.TargetId != firstTarget.TargetId);
        RaidSource firstSource = _data.Sources.First(item => item.DisplayName == "Timed Explosive Charge");
        RaidSource secondSource = _data.Sources.First(item => item.DisplayName == "Rocket");
        RaidMethodResult first = _engine.GetMethods(firstTarget, 2).Single(item => item.Source.SourceId == firstSource.SourceId);
        RaidMethodResult second = _engine.GetMethods(secondTarget).Single(item => item.Source.SourceId == secondSource.SourceId);

        IReadOnlyList<RaidResourceTotal> totals = RaidCalculatorEngine.Aggregate([first, second]);

        double expectedSulfur = first.Resources.Where(item => item.Shortname == "sulfur").Sum(item => item.Amount)
                               + second.Resources.Where(item => item.Shortname == "sulfur").Sum(item => item.Amount);
        Assert.AreEqual(expectedSulfur, totals.Single(item => item.Shortname == "sulfur").Amount);
        IReadOnlyList<RaidItemTotal> raidItems = RaidCalculatorEngine.AggregateItems([first, second, first]);
        Assert.AreEqual(first.RequiredItems * 2,
            raidItems.Single(item => item.Source.SourceId == first.Source.SourceId).Amount);
    }

    [TestMethod]
    public void SmartCombinationUsesWholeItemsAndReachesTargetHealth()
    {
        RaidTarget metalWall = _data.Targets.Single(target => target.DisplayName == "Wall (Metal)");
        long[] selectedSources = _data.Sources.Where(source => source.ItemShortname is
                "explosive.timed" or "ammo.rifle.explosive")
            .Select(source => source.SourceId).ToArray();
        IReadOnlyList<RaidMethodResult> mix = _engine.GetBestCombination(
            metalWall, selectedSources, RaidComparisonMode.LowestSulfur);

        Assert.AreEqual(2, mix.Count);
        Assert.AreEqual(3, mix.Single(part => part.Source.ItemShortname == "explosive.timed").RequiredItems);
        Assert.IsTrue(mix.Sum(part => part.TotalDamage) >= metalWall.StartHealth);
    }

    [TestMethod]
    public void InvalidQuantityIsClampedAndUnknownMethodIsUnavailable()
    {
        RaidTarget target = _data.Targets.First();
        IReadOnlyList<RaidMethodResult> zero = _engine.GetMethods(target, 0);

        CollectionAssert.AreEqual(
            _engine.GetMethods(target, 1).Select(item => item.RequiredItems).ToList(),
            zero.Select(item => item.RequiredItems).ToList());
        Assert.IsFalse(zero.Any(item => item.Source.SourceId == long.MaxValue));
    }

    [TestMethod]
    public void OptionalNullCraftCostIsAcceptedButMalformedNumbersAreRejected()
    {
        RaidDataService.Validate(_data);
        RaidSource source = _data.Sources[0];
        RaidDataSet malformed = new()
        {
            SchemaVersion = 1,
            Sources = [new RaidSource { SourceId = source.SourceId, DisplayName = source.DisplayName, RawDamage = double.NaN }],
            Targets = [_data.Targets[0]],
            DamagePerHit = new() { [source.SourceId] = new() { [_data.Targets[0].TargetId] = 1 } },
            Hits = new() { [source.SourceId] = new() { [_data.Targets[0].TargetId] = 1 } }
        };

        Assert.ThrowsException<InvalidDataException>(() => RaidDataService.Validate(malformed));
        Assert.IsTrue(_data.Sources.Any(item => item.CraftCost is null));
    }

    [TestMethod]
    public async Task PersistenceRoundTripsPlanEntries()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"RustPlusDesk.Tests-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "plan.json");
        try
        {
            RaidPlanEntry expected = new(_data.Targets[0].TargetId, 3, _data.Sources[0].SourceId);
            var store = new RaidPlanStore(path);

            await store.SaveAsync([expected]);
            IReadOnlyList<RaidPlanEntry> restored = await store.LoadAsync();

            Assert.AreEqual(expected, restored.Single());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void TargetCategoriesAndSearchTermsComeFromDatasetFields()
    {
        RaidTarget door = _data.Targets.First(item => item.ComponentType == "Door");
        IEnumerable<RaidTarget> matches = _data.Targets.Where(item =>
            $"{item.DisplayName} {item.ComponentType} {item.BuildingTier} {item.ItemCategorySlug}"
                .Contains(door.DisplayName, StringComparison.OrdinalIgnoreCase));

        Assert.AreEqual("Doors & gates", door.Category);
        Assert.IsTrue(matches.Any(item => item.TargetId == door.TargetId));
    }
}
