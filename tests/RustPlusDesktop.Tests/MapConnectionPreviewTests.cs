using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;
using RustPlusDesk.ViewModels;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Xml.Linq;

namespace RustPlusDesktop.Tests;

[TestClass]
public class MapConnectionPreviewTests
{
    [STATestMethod]
    public void CachedAndDevicesOnlyMaps_StayBlurredBehindConnectCard_UntilFullConnect()
    {
        var view = LoadPreview();
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        var profile = new ServerProfile { Port = 28082 };
        typeof(MainViewModel).GetField("_selected", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, profile);
        var preview = (Viewbox)view.FindName("Preview");
        var placeholder = (Border)view.FindName("MapPlaceholder");
        foreach (var mode in new[] { "empty", "cached", "devices", "opening", "soft-opening", "full", "failed", "offline" })
        {
            vm.HasMapImage = mode != "empty";
            profile.IsConnected = mode is "devices" or "soft-opening" or "full";
            profile.IsFullConnected = mode == "full";
            vm.IsFullConnectRequested = mode is "opening" or "soft-opening";
            profile.Port = mode == "offline" ? 0 : 28082;
            profile.ConnectionFailure = mode == "failed" ? ConnectionFailureKind.Unavailable : null;
            view.DataContext = null;
            view.DataContext = vm;
            view.Measure(new Size(800, 600));
            view.Arrange(new Rect(0, 0, 800, 600));
            view.UpdateLayout();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            bool blurred = mode is "cached" or "devices" or "failed";
            Assert.AreEqual(blurred, vm.IsMapPreviewVisible);
            Assert.AreEqual(blurred, preview.Effect is BlurEffect);
            Assert.AreEqual(!blurred, preview.IsHitTestVisible);
            Assert.AreEqual(mode is "full" or "offline" or "opening" or "soft-opening" ? Visibility.Collapsed : Visibility.Visible, placeholder.Visibility);
            if (mode == "devices")
                Assert.AreEqual(Visibility.Visible, ((FrameworkElement)view.FindName("MapUiBtnFullConnect")).Visibility);
            var recovery = (Wpf.Ui.Controls.Button)view.FindName("MapConnectionRecoveryButton");
            Assert.AreEqual(mode == "failed" ? Visibility.Visible : Visibility.Collapsed, recovery.Visibility);
            if (mode == "failed")
            {
                Assert.AreEqual(profile.ConnectionRecoveryAction, recovery.Content);
                Assert.IsFalse(((FrameworkElement)view.FindName("MapUiBtnConnect")).IsVisible);
            }
        }
        Assert.IsTrue(((SolidColorBrush)((Border)view.FindName("MapConnectCard")).Background).Color.A < 255,
            "The frosted card must let the blurred map show through.");
    }

    private static Grid LoadPreview()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = XDocument.Load(Path.Combine(root, "src/RustPlusDesktop/MainWindow.xaml"));
        var placeholder = new XElement(document.Descendants(wpf + "Border").Single(e => (string?)e.Attribute(x + "Name") == "MapPlaceholder"));
        foreach (var element in placeholder.DescendantsAndSelf()) element.Attribute("Click")?.Remove();
        foreach (var brush in placeholder.Descendants(wpf + "ImageBrush"))
            brush.SetAttributeValue("ImageSource", new Uri(Path.Combine(root, "src/RustPlusDesktop/Assets/Images/map-connect-backdrop.png")).AbsoluteUri);
        var theme = ServerDeletionConfirmationTests.ThemeResources();
        var dictionary = theme.Element(wpf + "ResourceDictionary")!;
        dictionary.Add(new XElement(document.Descendants(wpf + "Style").Single(e => (string?)e.Attribute(x + "Key") == "MapPreviewStyle")));
        var shared = XDocument.Load(Path.Combine(root, "src/RustPlusDesktop/Views/MainWindow/SharedResources.xaml"));
        dictionary.Add(shared.Root!.Elements().Where(e => (string?)e.Attribute(x + "Key") is "BoolToVisibility" or "BoolToVisibilityInv").Select(e => new XElement(e)));
        var host = new XElement(wpf + "Grid", new XAttribute("xmlns", wpf.NamespaceName), new XAttribute(XNamespace.Xmlns + "x", x.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "ui", "http://schemas.lepo.co/wpfui/2022/xaml"), theme,
            new XElement(wpf + "Viewbox", new XAttribute(x + "Name", "Preview"), new XAttribute("Style", "{StaticResource MapPreviewStyle}"),
                new XElement(wpf + "Border", new XAttribute("Width", 800), new XAttribute("Height", 600), new XAttribute("Background", "#0B3A4A"))), placeholder);
        return (Grid)XamlReader.Parse(host.ToString().Replace("http://schemas.lepo.co/wpfui/2022/xaml", "clr-namespace:Wpf.Ui.Controls;assembly=Wpf.Ui")
            .Replace("clr-namespace:RustPlusDesk.Converters\"", "clr-namespace:RustPlusDesk.Converters;assembly=RustPlusDesk\""));
    }
}
