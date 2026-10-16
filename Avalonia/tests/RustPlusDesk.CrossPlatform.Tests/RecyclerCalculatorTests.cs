using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Core.Recycling;
using RustPlusDesk.Infrastructure.Items;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class RecyclerCalculatorTests
{
    [TestMethod]
    public void BundledCatalog_PreservesAll776RecyclableItems_AndDetailedWolfYields()
    {
        var catalog = new BundledRecyclerCatalog().Load();
        Assert.AreEqual(776, catalog.Items.Count);
        Assert.AreEqual(776, catalog.Items.Select(item => item.Resource.ShortName).Distinct().Count());
        Assert.IsTrue(catalog.Items.All(item => item.StackSize > 0));
        Assert.IsTrue(catalog.Items.SelectMany(item => item.Wild.Concat(item.Safe)).All(yield =>
            double.IsFinite(yield.Quantity) && yield.Quantity >= 0 && yield.Probability is >= 0 and <= 1 && catalog.Resources.ContainsKey(yield.ShortName)));
        var wolf = catalog.Items.Single(item => item.Resource.ShortName == "hat.wolf");
        var result = RecyclerCalculator.Calculate(catalog, [(wolf, 2)]);
        Assert.AreEqual(10, result.WildSeconds);
        Assert.AreEqual(16, result.SafeSeconds);
        var cloth = result.Outputs.Single(output => output.Resource.ShortName == "cloth");
        Assert.AreEqual(new RecyclerTotals(12, 12, 12), cloth.Wild);
        Assert.AreEqual(new RecyclerTotals(8, 8, 8), cloth.Safe);
        var skull = result.Outputs.Single(output => output.Resource.ShortName == "skull.wolf");
        Assert.AreEqual(new RecyclerTotals(1.2, 0, 2), skull.Wild);
        Assert.AreEqual(new RecyclerTotals(0.8, 0, 2), skull.Safe);
        Assert.AreEqual(60, skull.Wild.ChancePercent);
        Assert.AreEqual(40, skull.Safe.ChancePercent);
        Assert.AreEqual("Tech Parts", catalog.Resources["techparts"].Name);
        Assert.AreEqual("Tech Trash", catalog.Items.Single(item => item.Resource.ShortName == "techparts").Resource.Name);
    }

    [TestMethod]
    public void Aggregation_PreservesGuaranteedAndChanceRanges_AndTenPercentTickRounding()
    {
        var resource = new RecyclerResource("scrap", "Scrap");
        var first = new RecyclerItem(new("a", "A"), "Components", 11, [new("scrap", 2, 1), new("scrap", 1, 0.5)], []);
        var second = new RecyclerItem(new("b", "B"), "Components", 100, [new("scrap", 4, 0.25)], [new("scrap", 2, 1)]);
        var catalog = new RecyclerCatalog([first, second], new Dictionary<string, RecyclerResource> { ["scrap"] = resource });
        var result = RecyclerCalculator.Calculate(catalog, [(first, 3), (second, 11)]);
        Assert.AreEqual(20, result.WildSeconds); // ceil(3/2) + ceil(11/10) = four ticks.
        Assert.AreEqual(32, result.SafeSeconds);
        var output = result.Outputs.Single();
        Assert.AreEqual(new RecyclerTotals(18.5, 6, 53), output.Wild);
        Assert.AreEqual(new RecyclerTotals(22, 22, 22), output.Safe);
        Assert.AreEqual(47, output.Wild.ChanceQuantity);
        Assert.AreEqual(12.5 / 47 * 100, output.Wild.ChancePercent, 0.000001);
    }

    [TestMethod]
    public void ZeroAndNegativeQuantities_AndLargeCounts_DoNotWrapIntArithmetic()
    {
        var item = new RecyclerItem(new("a", "A"), "", 1, [new("scrap", 4, 1)], []);
        var catalog = new RecyclerCatalog([item], new Dictionary<string, RecyclerResource>());
        var empty = RecyclerCalculator.Calculate(catalog, [(item, 0)]);
        Assert.AreEqual(0, empty.Outputs.Count);
        Assert.AreEqual(0, empty.WildSeconds);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => RecyclerCalculator.Calculate(catalog, [(item, -1)]));
        var large = RecyclerCalculator.Calculate(catalog, [(item, int.MaxValue)]);
        Assert.AreEqual((long)int.MaxValue * 8, large.SafeSeconds);
        Assert.AreEqual((double)int.MaxValue * 4, large.Outputs.Single().Wild.Guaranteed);
    }
}
