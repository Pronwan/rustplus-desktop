using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RustPlusDesktop.Tests;

[TestClass]
public class DevicesPanelTests
{
    private static readonly string[] ActionButtons = { "BtnHotkeys", "BtnDevicesImport", "BtnDevicesExport", "BtnLogicEngine", "BtnDeviceAutomation", "BtnDeviceRefresh", "BtnDeleteDevice" };

    [STATestMethod]
    public void ActionBar_FitsNarrowSidebar_WithEqualIconButtons()
    {
        var view = LoadPanel();
        view.DataContext = new State();
        Refresh(view, 280);
        var bar = (Border)view.FindName("DevicesActionBar");
        Assert.IsTrue(bar.DesiredSize.Width <= 280, $"Action bar needs {bar.DesiredSize.Width}px at 280px.");
        double top = ((FrameworkElement)view.FindName(ActionButtons[0])).TranslatePoint(new Point(), view).Y;
        foreach (var name in ActionButtons)
        {
            var button = (FrameworkElement)view.FindName(name);
            Assert.AreEqual(34, button.ActualWidth, $"{name} width");
            Assert.AreEqual(top, button.TranslatePoint(new Point(), view).Y, 0.5, $"{name} must stay on the one action row.");
        }
    }

    [STATestMethod]
    public void ActiveButtons_ShareOneTreatment()
    {
        var view = LoadPanel();
        view.DataContext = new State { Selected = new Selection { IsLogicEngineActive = true, IsDeviceAutomationActive = true } };
        Refresh(view, 300);
        var logic = (Control)view.FindName("BtnLogicEngine");
        var automation = (Control)view.FindName("BtnDeviceAutomation");
        Assert.IsNull(logic.Effect, "Active buttons use a tint, not a glow.");
        Assert.AreEqual(logic.Background.ToString(), automation.Background.ToString());
        Assert.AreEqual(logic.BorderBrush.ToString(), automation.BorderBrush.ToString());
        Assert.AreEqual(logic.Foreground.ToString(), automation.Foreground.ToString());
    }

    [STATestMethod]
    public void FilterRow_SearchAndTypeShareHeightAndCentre()
    {
        var view = LoadPanel();
        view.DataContext = new State();
        Refresh(view, 300);
        var search = (Control)view.FindName("TxtDeviceFilter");
        var type = (Control)view.FindName("CmbDeviceTypeFilter");
        Assert.AreEqual(search.ActualHeight, type.ActualHeight);
        Assert.AreEqual(VerticalAlignment.Center, search.VerticalContentAlignment);
        Assert.AreEqual(VerticalAlignment.Center, type.VerticalContentAlignment);
        Assert.IsNull(search.Resources[typeof(TextBlock)], "No nudge transforms on the search text.");
    }

    [STATestMethod]
    public void DeviceSetupProgress_IsInDevices_AndVisibleOnlyWhileItsPhaseRuns()
    {
        var view = LoadPanel();
        foreach (var (subscribing, checking) in new[] { (false, false), (true, false), (false, true) })
        {
            view.DataContext = new State { IsDeviceSubscribePriming = subscribing, IsDeviceStatusChecking = checking };
            Refresh(view, 280);
            Assert.AreEqual(subscribing ? Visibility.Visible : Visibility.Collapsed,
                ((Border)view.FindName("DeviceSubscriptionProgress")).Visibility);
            Assert.AreEqual(checking ? Visibility.Visible : Visibility.Collapsed,
                ((Border)view.FindName("DeviceStatusProgressPanel")).Visibility);
        }
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var map = XDocument.Load(Path.Combine(root, "src/RustPlusDesktop/MainWindow.xaml"))
            .Descendants().Single(e => (string?)e.Attribute(x + "Name") == "MapPlaceholder");
        Assert.IsFalse(map.Descendants().Attributes().Any(a => a.Value.Contains("IsDeviceSubscribePriming") || a.Value.Contains("IsDeviceStatusChecking")));
    }

    internal static void Refresh(FrameworkElement view, double width)
    {
        view.Measure(new Size(width, 700));
        view.Arrange(new Rect(0, 0, width, view.DesiredSize.Height));
        view.UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    // The Devices tab header, filter row and action bar, parsed straight from MainWindow.xaml
    // with the real App.xaml palette so state colours can be compared.
    internal static StackPanel LoadPanel()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = XDocument.Load(Path.Combine(root, "src/RustPlusDesktop/MainWindow.xaml"));
        var dock = document.Descendants(wpf + "TabItem").Single(e => (string?)e.Attribute(x + "Name") == "DevicesTabItem").Element(wpf + "DockPanel")!;
        string? Name(XElement e) => (string?)e.Attribute(x + "Name");
        var parts = new[]
        {
            dock.Elements(wpf + "Grid").First(),
            dock.Descendants(wpf + "Grid").First(g => g.Elements().Any(e => Name(e) == "TxtDeviceFilter")),
            dock.Elements(wpf + "Border").Single(e => Name(e) == "DevicesActionBar"),
            dock.Elements(wpf + "Border").Single(e => Name(e) == "DeviceSubscriptionProgress"),
            dock.Elements(wpf + "Border").Single(e => Name(e) == "DeviceStatusProgressPanel"),
        }.Select(e => new XElement(e)).ToArray();
        foreach (var part in parts)
        {
            part.Descendants().Where(e => e.Name.LocalName.EndsWith(".ContextMenu")).Remove();
            foreach (var element in part.DescendantsAndSelf())
                element.Attributes().Where(a => a.Name.LocalName is "Click" or "MouseEnter" or "MouseLeave" or "Tutorial.TargetId" or "DockPanel.Dock").Remove();
        }

        var app = XDocument.Load(Path.Combine(root, "src/RustPlusDesktop/App.xaml"));
        var tokens = app.Root!.Descendants().Where(e => (e.Name == wpf + "Color" || e.Name == wpf + "SolidColorBrush") && e.Attribute(x + "Key") != null)
            .GroupBy(e => (string)e.Attribute(x + "Key")!).Select(g => new XElement(g.First())).ToArray();
        var theme = ServerDeletionConfirmationTests.ThemeResources();
        theme.Name = wpf + "StackPanel.Resources";
        var dictionary = theme.Element(wpf + "ResourceDictionary")!;
        dictionary.Elements(wpf + "SolidColorBrush").Remove();
        dictionary.Add(tokens);
        XNamespace converters = "clr-namespace:RustPlusDesk.Converters;assembly=RustPlusDesk";
        dictionary.Add(new XElement(converters + "BoolToVisibilityConverter", new XAttribute(x + "Key", "BoolToVisibility")));
        var host = new XElement(wpf + "StackPanel", new XAttribute("xmlns", wpf.NamespaceName), new XAttribute(XNamespace.Xmlns + "x", x.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "ui", "http://schemas.lepo.co/wpfui/2022/xaml"), new XAttribute("Background", "#181B1F"), theme, parts);
        var view = (StackPanel)XamlReader.Parse(host.ToString().Replace("http://schemas.lepo.co/wpfui/2022/xaml", "clr-namespace:Wpf.Ui.Controls;assembly=Wpf.Ui"));
        var text = new Dictionary<string, string> { ["DevicesTab"] = "Devices", ["UiSearchDevices"] = "Search devices...", ["NotificationsAll"] = "All" };
        foreach (var key in new[] { "Hotkeys", "ImportDevices", "CloudSync", "ExportDevices", "UiLogicEngine", "UiDeviceAutomation", "UiRefreshDeviceStates", "Refresh", "UiDeleteMissingDevice", "DeleteMissingOnly", "PremiumFeatures", "UiSwitch", "UiAlarm", "UiStorage" })
            text[key] = key;
        foreach (var pair in text) view.Resources[pair.Key] = pair.Value;
        return view;
    }

    internal sealed class Selection
    {
        public bool IsLogicEngineActive { get; set; }
        public bool IsDeviceAutomationActive { get; set; }
        public bool HasMissingDevices { get; set; }
    }

    internal sealed class State
    {
        public Selection Selected { get; set; } = new();
        public System.Collections.ObjectModel.ObservableCollection<object> CurrentDevices { get; } = new();
        public bool IsDeviceSubscribePriming { get; set; }
        public bool IsDeviceStatusChecking { get; set; }
        public int DeviceSubscribeMax => 9;
        public int DeviceSubscribeProgress { get; set; } = 4;
        public string DeviceSubscribeProgressText => "4 / 9";
        public string DeviceSubscribeText => "Subscribing device #2400320...";
        public int DeviceStatusMax => 9;
        public int DeviceStatusProgress { get; set; } = 4;
        public string DeviceStatusProgressText => "4 / 9";
        public string DeviceStatusText => "Checking device #2400320...";
    }
}
