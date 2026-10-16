using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Avalonia.Features.Recycling;
using RustPlusDesk.Core.Recycling;
using RustPlusDesk.Infrastructure.Items;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class RecyclerViewModelTests
{
    [TestMethod]
    public void FilteringAndPaging_KeepHiddenQuantities_AndClearResetsEverything()
    {
        var model = new RecyclerViewModel(new BundledRecyclerCatalog().Load());
        Assert.AreEqual(30, model.Items.Count);
        Assert.IsTrue(model.HasMore);
        model.Items[0].Quantity = 10;
        string originalName = model.Items[0].Name;
        Assert.IsTrue(model.HasOutputs);
        model.LoadMoreCommand.Execute(null);
        Assert.AreEqual(60, model.Items.Count);
        model.Search = "hat.wolf";
        Assert.AreEqual(1, model.Items.Count);
        Assert.IsTrue(model.HasOutputs, "Hidden inventory must remain in the calculation.");
        Assert.AreEqual("Wolf Headdress", model.Items.Single().Name);
        model.FillStacksCommand.Execute(null);
        Assert.AreEqual(1, model.Items.Single().Quantity);
        model.Search = originalName;
        Assert.AreEqual(10, model.Items.First(item => item.Name == originalName).Quantity);
        model.ClearCommand.Execute(null);
        Assert.IsFalse(model.HasOutputs);
        Assert.AreEqual("00:00:00", model.WildTime);
        model.Search = "hat.wolf";
        Assert.AreEqual(0, model.Items.Single().Quantity);
        model.Category = "not-a-category";
        Assert.AreEqual(0, model.Items.Count);
    }

    [TestMethod]
    public void QuantityAdjustment_ClampsAndPreventsOverflow_AndLongTimesDoNotWrapAfter24Hours()
    {
        var item = new RecyclerItem(new("a", "A"), "", 1, [new("scrap", 1, 1)], []);
        var model = new RecyclerViewModel(new([item], new Dictionary<string, RecyclerResource>()));
        model.Items[0].Adjust(-10);
        Assert.AreEqual(0, model.Items[0].Quantity);
        model.Items[0].Quantity = int.MaxValue;
        model.Items[0].Adjust(10);
        Assert.AreEqual(int.MaxValue, model.Items[0].Quantity);
        Assert.AreEqual(((long)int.MaxValue * 5 / 3600).ToString(), model.WildTime.Split(':')[0]);
        model.Items[0].Quantity = -1;
        Assert.AreEqual(0, model.Items[0].Quantity);
    }
}
