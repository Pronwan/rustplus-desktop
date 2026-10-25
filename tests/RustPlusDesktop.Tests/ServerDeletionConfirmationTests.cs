using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RustPlusDesktop.Tests;

[TestClass]
public class ServerDeletionConfirmationTests
{
    [STATestMethod]
    public void Manager_DeletingInactiveRowDoesNotChangeActiveSelection()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = XDocument.Load(Path.Combine(root, "src/RustPlusDesktop/MainWindow.xaml"));
        var manager = new XElement(document.Descendants(wpf + "Grid").Single(e => (string?)e.Attribute(x + "Name") == "ServerManagerOverlay"));
        manager.SetAttributeValue("Visibility", "Visible");
        manager.SetAttributeValue("xmlns", wpf.NamespaceName);
        manager.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
        manager.SetAttributeValue(XNamespace.Xmlns + "ui", "http://schemas.lepo.co/wpfui/2022/xaml");
        foreach (var element in manager.DescendantsAndSelf())
        {
            element.Attribute("Click")?.Remove();
            element.Attribute("MouseLeftButtonDown")?.Remove();
            element.Attribute("ContextMenu")?.Remove();
        }
        manager.AddFirst(ThemeResources());
        var view = (Grid)XamlReader.Parse(manager.ToString().Replace("http://schemas.lepo.co/wpfui/2022/xaml", "clr-namespace:Wpf.Ui.Controls;assembly=Wpf.Ui"));
        var active = new RustPlusDesk.Models.ServerProfile { Name = "Active", IsConnected = true };
        var inactive = new RustPlusDesk.Models.ServerProfile { Name = "Inactive" };
        var state = new ManagementState { Selected = active, Servers = new() { active, inactive } };
        view.DataContext = state;
        view.Measure(new Size(800, 700));
        view.Arrange(new Rect(0, 0, 800, 700));
        view.UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var list = (ItemsControl)view.FindName("ServerManagerList");
        var row = (ContentPresenter)list.ItemContainerGenerator.ContainerFromItem(inactive);
        var delete = VisualChildren(row).OfType<Wpf.Ui.Controls.Button>().Single(button => button.Name == "ServerManagerDeleteButton");
        var edit = VisualChildren(row).OfType<Wpf.Ui.Controls.Button>().Single(button => button.Name == "ServerManagerEditButton");
        var more = VisualChildren(row).OfType<Wpf.Ui.Controls.Button>().Single(button => button.Name == "ServerManagerMoreButton");
        Assert.AreSame(inactive, edit.Tag);
        Assert.AreSame(inactive, more.DataContext);
        Assert.AreSame(inactive, delete.Tag, "Deletion targets the row profile, not the active selection.");
        Assert.AreSame(active, state.Selected);
        // Detached rows must keep local styles instead of resolving another test thread's application styles.
        foreach (var element in VisualChildren(row).OfType<FrameworkElement>()) element.Style = element.Style;
        state.Servers.Remove((RustPlusDesk.Models.ServerProfile)delete.Tag);
        Assert.AreSame(active, state.Selected);
        Assert.IsTrue(active.IsConnected);
        Assert.AreEqual(1, list.Items.Count);
    }

    private sealed class ManagementState
    {
        public System.Collections.ObjectModel.ObservableCollection<RustPlusDesk.Models.ServerProfile> Servers { get; set; } = new();
        public RustPlusDesk.Models.ServerProfile? Selected { get; set; }
    }

    private static XElement ThemeResources()
    {
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace markup = "clr-namespace:Wpf.Ui.Markup;assembly=Wpf.Ui";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var palette = new[] { "Surface", "SurfaceAlt", "CardBorder", "AccentDark", "Accent", "TextPrimary", "TextSubtle", "DangerBrush" };
        return new XElement(wpf + "Grid.Resources", new XElement(wpf + "ResourceDictionary",
            new XElement(wpf + "ResourceDictionary.MergedDictionaries",
                new XElement(markup + "ThemesDictionary", new XAttribute("Theme", "Dark")),
                new XElement(markup + "ControlsDictionary")),
            palette.Select(key => new XElement(wpf + "SolidColorBrush", new XAttribute(x + "Key", key), new XAttribute("Color", "#8090A0")))));
    }

    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject parent)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in VisualChildren(child)) yield return descendant;
        }
    }

    [STATestMethod]
    public void Confirmation_IsCenteredAcrossWindowIncludingHeaderAndSidebar()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = XDocument.Load(Path.Combine(root, "src/RustPlusDesktop/MainWindow.xaml"));
        var overlay = document.Descendants(wpf + "Grid").Single(e => (string?)e.Attribute(x + "Name") == "DeleteConfirmationOverlay");
        var windowGrid = document.Root!.Element(wpf + "Grid")!;
        Assert.AreSame(windowGrid, overlay.Parent, "Confirmation must cover the full window, including the header.");
        var markup = new XElement(overlay);
        markup.SetAttributeValue("Visibility", "Visible");
        foreach (var element in markup.DescendantsAndSelf())
        {
            element.Attribute("Click")?.Remove();
            element.Attribute("MouseLeftButtonDown")?.Remove();
        }
        var hostMarkup = new XElement(wpf + "Grid", new XAttribute("xmlns", wpf.NamespaceName), new XAttribute(XNamespace.Xmlns + "x", x.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "ui", "http://schemas.lepo.co/wpfui/2022/xaml"),
            ThemeResources(),
            new XElement(windowGrid.Element(wpf + "Grid.RowDefinitions")!),
            new XElement(wpf + "Border", new XAttribute("Grid.Row", "0"), new XAttribute("Height", "100")),
            new XElement(wpf + "Border", new XAttribute("Grid.Row", "2"), new XAttribute("Height", "24")), markup);
        var host = (Grid)XamlReader.Parse(hostMarkup.ToString().Replace("http://schemas.lepo.co/wpfui/2022/xaml", "clr-namespace:Wpf.Ui.Controls;assembly=Wpf.Ui"));
        var actualOverlay = (Grid)host.FindName("DeleteConfirmationOverlay");
        var card = (Border)actualOverlay.Children[0];
        foreach (var size in new[] { new Size(800, 700), new Size(1750, 814) })
        {
            host.Measure(size);
            host.Arrange(new Rect(size));
            host.UpdateLayout();
            var center = card.TranslatePoint(new Point(card.ActualWidth / 2, card.ActualHeight / 2), host);
            Assert.AreEqual(size.Width / 2, center.X, 1.0);
            Assert.AreEqual(size.Height / 2, center.Y, 1.0);
            Assert.AreEqual(size, actualOverlay.RenderSize);
        }
    }
}
