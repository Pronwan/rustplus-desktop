using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RustPlusDesk.Services.Sidebar;

internal sealed class SidebarContextMenuTracker
{
    private static readonly ConditionalWeakTable<DependencyObject, SidebarContextMenuTracker> Roots = new();
    private readonly HashSet<ContextMenu> _openMenus = new();

    static SidebarContextMenuTracker()
    {
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent,
            new RoutedEventHandler(MenuOpened), handledEventsToo: true);
    }

    public SidebarContextMenuTracker(FrameworkElement root) => Roots.Add(root, this);

    public bool IsOpen => _openMenus.Count != 0;
    public event EventHandler? Changed;

    private static void MenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        for (DependencyObject? current = menu.PlacementTarget; current != null;
             current = (current is Visual ? VisualTreeHelper.GetParent(current) : null) ?? LogicalTreeHelper.GetParent(current))
        {
            if (!Roots.TryGetValue(current, out var tracker)) continue;
            if (tracker._openMenus.Add(menu))
            {
                menu.Closed += tracker.MenuClosed;
                tracker.Changed?.Invoke(tracker, EventArgs.Empty);
            }
            return;
        }
    }

    private void MenuClosed(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || !_openMenus.Remove(menu)) return;
        menu.Closed -= MenuClosed;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
