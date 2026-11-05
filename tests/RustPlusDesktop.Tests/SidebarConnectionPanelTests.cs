using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;

namespace RustPlusDesktop.Tests;

[TestClass]
public class SidebarConnectionPanelTests
{
    [STATestMethod]
    public void ConnectionPanel_UsesConnectionStateAndKeepsActionsFixed()
    {
        var view = LoadPanels();
        var profile = new ServerProfile { Name = "Test server" };
        var status = (TextBlock)view.FindName("TxtServerConnectionState");
        var actions = (StackPanel)view.FindName("SidebarConnectionActions");
        Point? actionPosition = null;
        foreach (var mode in new[] { "disconnected", "busy", "devices", "full", "connecting", "initializing", "priming", "denied", "offline" })
        {
            profile.IsConnected = mode is "devices" or "full";
            profile.IsFullConnected = mode == "full";
            profile.IsAccessDenied = mode == "denied";
            profile.Port = mode == "offline" ? 0 : 28082;
            view.DataContext = new State { Selected = profile, Servers = new() { profile }, IsBusy = mode == "busy", IsConnectionLoading = mode == "connecting", IsInitializing = mode == "initializing", IsDeviceSubscribePriming = mode == "priming" };
            Refresh(view);
            var expected = mode switch
            {
                "devices" => "StatusDevicesActive", "full" => "StatusFullyConnected",
                "connecting" or "priming" => "StatusConnecting", "initializing" => "StatusInitializing", "denied" => "StatusAccessDenied", _ => "StatusDisconnected"
            };
            Assert.AreEqual(expected, status.Text);
            Assert.AreEqual(mode is not ("connecting" or "initializing" or "priming" or "offline"), actions.IsEnabled);
            var point = actions.TranslatePoint(new Point(), view);
            actionPosition ??= point;
            Assert.AreEqual(actionPosition.Value, point, "Connection states must not move actions.");
            var list = (ListBox)view.FindName("ListServers");
            Assert.AreEqual(mode is not ("connecting" or "initializing" or "priming"), list.IsEnabled);
        }
    }

    [STATestMethod]
    public void CompactPanels_FitNarrowSidebar_AndPairingWorksWithoutServers()
    {
        var view = LoadPanels();
        var profile = new ServerProfile { Name = "A long server name that must stay on one line" };
        view.DataContext = new State { Selected = profile, Servers = new() { profile } };
        Refresh(view);
        Console.WriteLine($"Collapsed server and pairing controls: {view.DesiredSize.Height:0}px at 264px width.");
        Assert.IsTrue(view.DesiredSize.Height <= 104, $"Collapsed controls use {view.DesiredSize.Height}px.");
        var start = (Wpf.Ui.Controls.Button)view.FindName("PairingStartButton");
        Assert.IsNull(view.FindName("PairingStopButton"), "Stopping the app-wide listener belongs in its menu, not beside the server controls.");
        foreach (var state in new[] { new State(), new State { IsPairingBusy = true }, new State { IsPairingRunning = true }, new State { IsBusy = true } })
        {
            view.DataContext = state;
            Refresh(view);
            Assert.AreEqual(state.CanStartPairing, start.IsEnabled);
            Assert.AreEqual(state.CanStopPairing ? Visibility.Collapsed : Visibility.Visible, start.Visibility);
        }
        Assert.AreEqual(Visibility.Collapsed, ((Grid)view.FindName("SidebarConnectionRow")).Visibility);
    }

    [STATestMethod]
    public void CardButtons_LineUpInOneIconColumn()
    {
        var view = LoadPanels();
        var profile = new ServerProfile { Name = "Test server", IsConnected = true };
        view.DataContext = new State { Selected = profile, Servers = new() { profile } };
        Refresh(view);
        foreach (var column in new[] { new[] { "BtnManageServers", "UiBtnServerInfo", "BtnManagePairing" }, new[] { "BtnToggleServerArea", "UiBtnDisconnect", "PairingStartButton" } })
        {
            var buttons = column.Select(name => (FrameworkElement)view.FindName(name)).ToArray();
            var left = buttons[0].TranslatePoint(new Point(), view).X;
            foreach (var button in buttons)
            {
                Assert.AreEqual(24, button.ActualWidth, $"{button.Name} width");
                Assert.AreEqual(left, button.TranslatePoint(new Point(), view).X, 0.5, $"{button.Name} must share a column with {buttons[0].Name}.");
            }
        }
    }

    [STATestMethod]
    public void ExpiryWarning_OnlyTakesSpaceWhenItNeedsAttention()
    {
        var view = LoadPanels();
        foreach (var days in new[] { -1, 60, 10 })
        {
            view.DataContext = new State { FcmExpiryDays = days };
            Refresh(view);
            var warning = (Wpf.Ui.Controls.InfoBar)view.FindName("FcmExpiryInfoBar");
            Assert.AreEqual(days >= 0 && days < 30 ? Visibility.Visible : Visibility.Collapsed, warning.Visibility);
        }
    }

    private static void Refresh(FrameworkElement view)
    {
        view.Measure(new Size(264, 700));
        view.Arrange(new Rect(0, 0, 264, view.DesiredSize.Height));
        view.UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private static StackPanel LoadPanels()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = XDocument.Load(Path.Combine(root, "src/RustPlusDesktop/MainWindow.xaml"));
        var cards = document.Descendants(wpf + "Border").Where(e => (string?)e.Attribute(x + "Name") == "ServerConnectionPanel").Select(e => new XElement(e)).ToArray();
        foreach (var card in cards)
        {
            card.Descendants(wpf + "Grid.Resources").Remove();
            card.Descendants(wpf + "Setter").Where(e => (string?)e.Attribute("Property") == "ContextMenu").Remove();
            foreach (var element in card.DescendantsAndSelf())
            {
                foreach (var name in new[] { "Click", "SelectionChanged", "ContextMenu" }) element.Attribute(name)?.Remove();
                element.Attributes().Where(a => a.Name.LocalName == "Tutorial.TargetId").Remove();
            }
        }
        var theme = ServerDeletionConfirmationTests.ThemeResources();
        theme.Name = wpf + "StackPanel.Resources";
        var shared = XDocument.Load(Path.Combine(root, "src/RustPlusDesktop/Views/MainWindow/SharedResources.xaml"));
        theme.Element(wpf + "ResourceDictionary")!.Add(shared.Root!.Elements().Where(e => (string?)e.Attribute(x + "Key") is "BoolToVisibility" or "BoolToVisibilityInv" or "LessThanConverter").Select(e => new XElement(e)));
        var host = new XElement(wpf + "StackPanel", new XAttribute("xmlns", wpf.NamespaceName), new XAttribute(XNamespace.Xmlns + "x", x.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "ui", "http://schemas.lepo.co/wpfui/2022/xaml"), theme, cards);
        var result = (StackPanel)XamlReader.Parse(host.ToString().Replace("http://schemas.lepo.co/wpfui/2022/xaml", "clr-namespace:Wpf.Ui.Controls;assembly=Wpf.Ui")
            .Replace("clr-namespace:RustPlusDesk.Converters\"", "clr-namespace:RustPlusDesk.Converters;assembly=RustPlusDesk\""));
        foreach (var key in new[] { "StatusDisconnected", "StatusDevicesActive", "StatusFullyConnected", "StatusConnecting", "StatusInitializing", "StatusAccessDenied", "UiPairingListener", "PairingIdle", "UiListenerRunning" }) result.Resources[key] = key;
        return result;
    }

    private sealed class State
    {
        public System.Collections.ObjectModel.ObservableCollection<ServerProfile> Servers { get; set; } = new();
        public ServerProfile? Selected { get; set; }
        public bool IsBusy { get; set; }
        public bool IsConnectionLoading { get; set; }
        public bool IsInitializing { get; set; }
        public bool IsDeviceSubscribePriming { get; set; }
        public int FcmExpiryDays { get; set; } = -1;
        public string FcmExpiryText => "";
        public bool IsPairingRunning { get; set; }
        public bool IsPairingBusy { get; set; }
        public bool IsPairingFaulted { get; set; }
        public bool CanStartPairing => !IsPairingRunning && !IsPairingBusy && !IsBusy;
        public bool CanStopPairing => IsPairingRunning || IsPairingBusy;
    }
}
