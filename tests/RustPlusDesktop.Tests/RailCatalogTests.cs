using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services.Sidebar;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class RailCatalogTests
{
    [TestMethod]
    public void BaseBuilderStartsInToolsAndUpgradePreservesCustomTabs()
    {
        var layout = RailCatalog.BuildDefault("My tools");
        var folder = layout.Nodes.Single(n => n.FolderId == RailCatalog.DefaultFolderId);
        Assert.IsTrue(folder.Children.Contains("basebuilder"));
        folder.Children.Remove("basebuilder");
        layout.Nodes.Reverse();
        var first = layout.Nodes[0];
        Assert.IsTrue(RailCatalog.Reconcile(layout));
        Assert.AreSame(first, layout.Nodes[0]);
        Assert.AreEqual("My tools", folder.Name);
        Assert.AreEqual(1, folder.Children.Count(id => id == "basebuilder"));
        Assert.IsFalse(RailCatalog.Reconcile(layout));
    }

    [TestMethod]
    public void BaseBuilderKeepsItsCustomLocationAndMissingToolsCanBeRestored()
    {
        var layout = RailCatalog.BuildDefault("Tools");
        var folder = layout.Nodes.Single(n => n.IsFolder);
        folder.Children.Remove("basebuilder");
        layout.Nodes.Insert(0, RailNodeData.Tab("basebuilder"));
        Assert.IsFalse(RailCatalog.Reconcile(layout));
        Assert.AreEqual("basebuilder", layout.Nodes[0].TabId);
        layout.Nodes.RemoveAt(0);
        layout.Nodes.Remove(folder);
        RailCatalog.Reconcile(layout);
        Assert.IsTrue(layout.Nodes.Single(n => n.FolderId == RailCatalog.DefaultFolderId).Children.Contains("basebuilder"));
    }
}
