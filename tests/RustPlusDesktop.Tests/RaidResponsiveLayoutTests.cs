using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Views;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class RaidResponsiveLayoutTests
{
    [STATestMethod]
    [DataRow(576, 0, 0, true)]
    [DataRow(768, 0, 0, true)]
    [DataRow(900, 292, 272, false)]
    [DataRow(1080, 330, 300, false)]
    public void ArrangePanes_UsesFullWidthInCompactModeAndKeepsWideRails(int width, int catalogWidth, int shoppingWidth, bool compact)
    {
        var grid = new Grid();
        foreach (var column in Enumerable.Range(0, 5))
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        var catalog = new Border();
        var shopping = new Border();
        grid.Children.Add(catalog);
        grid.Children.Add(shopping);
        var tabs = new System.Windows.Controls.Primitives.UniformGrid();
        var columns = grid.ColumnDefinitions;

        Assert.AreEqual(compact, RaidCalculatorView.ArrangePanes(width, tabs, columns[0], columns[4], columns[1], columns[3], catalog, shopping));
        Assert.AreEqual(compact ? Visibility.Visible : Visibility.Collapsed, tabs.Visibility);
        var size = new Size(width, 700);
        grid.Measure(size);
        grid.Arrange(new Rect(size));
        Assert.AreEqual(compact ? (double)width : catalogWidth, catalog.ActualWidth, 1d);
        Assert.AreEqual(compact ? (double)width : shoppingWidth, shopping.ActualWidth, 1d);
        Assert.AreEqual(compact ? 0d : width - shoppingWidth, shopping.TranslatePoint(new Point(), grid).X, 1d);
    }

    [TestMethod]
    [DataRow(true, true, Visibility.Visible)]
    [DataRow(true, false, Visibility.Collapsed)]
    [DataRow(false, false, Visibility.Visible)]
    public void PaneVisibility_OnlyHidesInactiveCompactPanes(bool compact, bool selected, Visibility expected) =>
        Assert.AreEqual(expected, RaidCalculatorView.PaneVisibility(compact, selected));

    [STATestMethod]
    public void SelectTutorialPane_SelectsCatalogThenPlan()
    {
        var catalog = new RadioButton();
        var plan = new RadioButton();
        var tabs = new StackPanel();
        tabs.Children.Add(catalog);
        tabs.Children.Add(plan);

        RaidCalculatorView.SelectTutorialPane("Raid.Search", catalog, plan);
        Assert.IsTrue(catalog.IsChecked == true && plan.IsChecked != true);
        RaidCalculatorView.SelectTutorialPane("Raid.Plan", catalog, plan);
        Assert.IsTrue(plan.IsChecked == true && catalog.IsChecked != true);
    }

    [TestMethod]
    public void CompactSwitcher_ContainsAllThreePaneActions()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var view = XDocument.Load(Path.Combine(root, "src/RustPlusDesktop/Views/MainWindow/Raid/RaidCalculatorView.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var tabs = view.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "CompactPaneTabs");
        var actions = tabs.Elements().ToArray();

        CollectionAssert.AreEqual(new[] { "CatalogTab", "PlanTab", "ShoppingTab" },
            actions.Select(e => (string?)e.Attribute(x + "Name")).ToArray());
        Assert.IsTrue(actions.All(e => e.Name.LocalName == "RadioButton" && (string?)e.Attribute("Checked") == "CompactPane_Checked"));
    }
}
