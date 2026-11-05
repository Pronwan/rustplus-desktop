using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Views;

namespace RustPlusDesktop.Tests;

[TestClass]
public class MapToolbarLayoutTests
{
    private static Border LoadQuickFilters()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var document = XDocument.Load(Path.Combine(root, "src/RustPlusDesktop/MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var quickFilters = new XElement(document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "MapQuickFilters"));
        quickFilters.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
        quickFilters.SetAttributeValue(XNamespace.Xmlns + "ui", "http://schemas.lepo.co/wpfui/2022/xaml");
        foreach (var toggle in quickFilters.Descendants(wpf + "CheckBox"))
        {
            toggle.Attribute("Checked")?.Remove();
            toggle.Attribute("Unchecked")?.Remove();
        }
        var shared = XDocument.Load(Path.Combine(root, "src/RustPlusDesktop/Views/MainWindow/SharedResources.xaml"));
        quickFilters.Element(wpf + "Border.Resources")!.AddFirst(
            new XElement(shared.Descendants(wpf + "Style").Single(element => (string?)element.Attribute(x + "Key") == "PillToggleButton")));
        return (Border)XamlReader.Parse(quickFilters.ToString().Replace(
            "http://schemas.lepo.co/wpfui/2022/xaml", "clr-namespace:Wpf.Ui.Controls;assembly=Wpf.Ui"));
    }

    [STATestMethod]
    public void ActualQuickFilters_SwitchToIcons_WithoutHidingOrChangingSelection()
    {
        var border = LoadQuickFilters();
        var toggles = ((WrapPanel)border.Child).Children.Cast<CheckBox>().ToArray();
        var selected = toggles.Select(toggle => toggle.IsChecked).ToArray();
        var visibility = toggles.Select(toggle => toggle.Visibility).ToArray();
        Assert.AreEqual(Visibility.Collapsed, toggles.Single(toggle => toggle.Name == "ChkShops").Visibility);
        // Standalone markup has no application language dictionary.
        foreach (var toggle in toggles)
            ((TextBlock)((StackPanel)toggle.Content).Children[1]).Text = "Long translated filter label";

        foreach (bool compact in new[] { false, true, false })
        {
            MainWindow.SetMapQuickFilterMode(border, compact);
            border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.AreEqual(6, toggles.Length);
            foreach (var toggle in toggles)
            {
                var content = (StackPanel)toggle.Content;
                Assert.AreEqual(Visibility.Visible, content.Children[0].Visibility);
                Assert.AreEqual(compact ? Visibility.Collapsed : Visibility.Visible, content.Children[1].Visibility);
            }
            CollectionAssert.AreEqual(selected, toggles.Select(toggle => toggle.IsChecked).ToArray());
            CollectionAssert.AreEqual(visibility, toggles.Select(toggle => toggle.Visibility).ToArray());
            if (compact) Assert.IsTrue(border.DesiredSize.Width <= 220,
                $"Icon strip width {border.DesiredSize.Width}; buttons {string.Join(", ", toggles.Select(toggle => toggle.DesiredSize.Width))}.");
        }
    }

    [STATestMethod]
    public void Resizing_KeepsFiltersVisible_AndRestoresLabels()
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var quickFilters = LoadQuickFilters();
        var quickToggles = ((Panel)quickFilters.Child).Children.Cast<CheckBox>().ToArray();
        quickToggles.Single(toggle => toggle.Name == "ChkShops").Visibility = Visibility.Visible;
        var selectedFilter = quickToggles[0];
        selectedFilter.IsChecked = true;
        var filters = new WrapPanel();
        filters.Children.Add(quickFilters);
        var layersLabel = new Border { Width = 60, Height = 32 };
        var commands = new StackPanel { Orientation = Orientation.Horizontal };
        commands.Children.Add(layersLabel);
        commands.Children.Add(new Border { Width = 120, Height = 32 });
        filters.Children.Add(commands);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        for (int i = 0; i < 8; i++)
            actions.Children.Add(new Border { Width = 40, Height = 32 });
        layout.Children.Add(filters);
        layout.Children.Add(actions);
        Grid.SetColumn(actions, 1);

        foreach (var (width, labelWidth, compact, stacked) in new[]
                 {
                     (1100d, 440d, false, false), (900d, 440d, true, false), (800d, 440d, true, false),
                     (540d, 440d, true, true), (400d, 440d, true, true), (240d, 440d, true, true),
                     (800d, 440d, true, false), (1100d, 440d, false, false),
                     (1100d, 900d, true, false), (1100d, 1400d, true, false), (1100d, 440d, false, false)
                 })
        {
            foreach (var toggle in quickToggles)
                ((TextBlock)((Panel)toggle.Content).Children[1]).Width = labelWidth / 6 - 30;
            layout.Width = width;
            layout.Measure(new Size(width, 500));
            layout.Arrange(new Rect(0, 0, width, 500));
            MainWindow.ApplyMapToolbarLayout(layout, filters, quickFilters, actions, layersLabel);
            layout.Measure(new Size(width, 500));
            layout.Arrange(new Rect(0, 0, width, 500));

            Assert.AreEqual(compact, quickFilters.Tag);
            Assert.AreEqual(compact ? Visibility.Collapsed : Visibility.Visible, layersLabel.Visibility);
            Assert.AreEqual(Visibility.Visible, quickFilters.Visibility, "Quick filters must remain directly accessible.");
            Assert.AreEqual(stacked ? 1 : 0, Grid.GetRow(actions),
                $"At {width}px with labels {labelWidth}: filters {filters.DesiredSize}, actions {actions.DesiredSize}.");
            Assert.IsTrue(selectedFilter.IsChecked, "Reflow must preserve the selected layers.");
            var filterBounds = new Rect(filters.TranslatePoint(new Point(), layout), filters.RenderSize);
            var actionBounds = new Rect(actions.TranslatePoint(new Point(), layout), actions.RenderSize);
            Assert.IsTrue(stacked ? filterBounds.Bottom <= actionBounds.Top : filterBounds.Right <= actionBounds.Left,
                $"Groups overlap at {width}px: filters {filterBounds}, actions {actionBounds}.");
            Assert.IsTrue(actionBounds.Right <= width, $"Actions overflow at {width}px.");
            Assert.IsTrue(filterBounds.Right <= width, $"Filters overflow at {width}px.");
            foreach (FrameworkElement action in actions.Children)
            {
                var bounds = new Rect(action.TranslatePoint(new Point(), layout), action.RenderSize);
                Assert.IsTrue(bounds.Left >= 0 && bounds.Right <= width,
                    $"An individual action overflows at {width}px.");
            }
        }
    }
}
