using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RustPlusDesktop.Tests;

[TestClass]
public class PairingMenuTests
{
    [STATestMethod]
    public void ManageMenu_TracksListenerState_IndependentlyOfServerConnection()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var document = XDocument.Load(Path.Combine(root, "src/RustPlusDesktop/MainWindow.xaml"));
        var markup = new XElement(document.Descendants(wpf + "ContextMenu")
            .Single(element => (string?)element.Attribute(x + "Key") == "PairingRecoveryMenu"));
        markup.Attribute(x + "Key")!.Remove();
        markup.Attribute("Style")!.Remove();
        markup.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
        markup.SetAttributeValue(XNamespace.Xmlns + "ui", "http://schemas.lepo.co/wpfui/2022/xaml");
        markup.SetAttributeValue(XNamespace.Xmlns + "conv", "clr-namespace:RustPlusDesk.Converters;assembly=RustPlusDesk");
        foreach (var element in markup.Descendants()) element.Attribute("Click")?.Remove();
        var shared = XDocument.Load(Path.Combine(root, "src/RustPlusDesktop/Views/MainWindow/SharedResources.xaml"));
        markup.AddFirst(new XElement(wpf + "ContextMenu.Resources",
            shared.Root!.Elements().Where(element => (string?)element.Attribute(x + "Key") is "BoolToVisibility" or "BoolToVisibilityInv")
                .Select(element => new XElement(element))));
        string source = markup.ToString()
            .Replace("http://schemas.lepo.co/wpfui/2022/xaml", "clr-namespace:Wpf.Ui.Controls;assembly=Wpf.Ui")
            .Replace("clr-namespace:RustPlusDesk.Converters\"", "clr-namespace:RustPlusDesk.Converters;assembly=RustPlusDesk\"");
        var menu = (ContextMenu)XamlReader.Parse(source);
        var button = new Button();
        menu.PlacementTarget = button;
        var start = menu.Items.OfType<MenuItem>().Single(item => item.Name == "BtnListenPairing");
        var stop = menu.Items.OfType<MenuItem>().Single(item => item.Name == "BtnStopPairing");

        foreach (var (connected, canStart, canStop) in new[]
                 { (false, true, false), (false, false, true), (true, false, true), (true, false, false), (false, true, false) })
        {
            button.DataContext = new { Selected = new { IsConnected = connected }, CanStartPairing = canStart, CanStopPairing = canStop };
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.AreSame(button.DataContext, menu.DataContext);
            Assert.AreEqual(canStart, start.IsEnabled);
            Assert.AreEqual(canStop ? Visibility.Collapsed : Visibility.Visible, start.Visibility);
            Assert.AreEqual(canStop ? Visibility.Visible : Visibility.Collapsed, stop.Visibility);
        }
    }
}
