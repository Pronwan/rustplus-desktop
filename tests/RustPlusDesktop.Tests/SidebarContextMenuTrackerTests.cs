using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services.Sidebar;

namespace RustPlusDesktop.Tests;

[TestClass]
public class SidebarContextMenuTrackerTests
{
    [STATestMethod]
    public void SidebarMenu_HoldsOpenUntilClosed_EvenWhenPlacementTargetIsCleared()
    {
        var button = new Button();
        var root = new Border { Child = new Grid { Children = { button } } };
        var tracker = new SidebarContextMenuTracker(root);
        var states = new List<bool>();
        tracker.Changed += (_, _) => states.Add(tracker.IsOpen);
        var menu = new ContextMenu { PlacementTarget = button, Style = new Style(typeof(ContextMenu)) };
        Assert.IsFalse(tracker.IsOpen);
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        Assert.IsTrue(tracker.IsOpen);
        CollectionAssert.AreEqual(new[] { true }, states);
        menu.PlacementTarget = null;
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
        Assert.IsFalse(tracker.IsOpen);
        CollectionAssert.AreEqual(new[] { true, false }, states);
    }

    [STATestMethod]
    public void OtherMenusDoNotHoldSidebar_AndClosingOneMenuDoesNotReleaseAnother()
    {
        var button = new Button();
        var tracker = new SidebarContextMenuTracker(new Border { Child = button });
        var outside = new ContextMenu { PlacementTarget = new Button(), Style = new Style(typeof(ContextMenu)) };
        outside.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        Assert.IsFalse(tracker.IsOpen);
        var first = new ContextMenu { PlacementTarget = button, Style = new Style(typeof(ContextMenu)) };
        var second = new ContextMenu { PlacementTarget = button, Style = new Style(typeof(ContextMenu)) };
        first.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        second.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        first.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
        Assert.IsTrue(tracker.IsOpen);
        second.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
        Assert.IsFalse(tracker.IsOpen);
        first.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        Assert.IsTrue(tracker.IsOpen);
        first.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
        Assert.IsFalse(tracker.IsOpen);
    }
}
