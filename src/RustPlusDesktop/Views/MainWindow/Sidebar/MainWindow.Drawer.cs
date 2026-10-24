using System.Windows;
using System.Windows.Controls;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private bool _sidebarUsesDrawer;

    private void SidebarViewport_Loaded(object sender, RoutedEventArgs e) => UpdateSidebarLayoutMode();

    private void SidebarViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged) UpdateSidebarLayoutMode();
    }

    private void UpdateSidebarLayoutMode()
    {
        if (LeftPanelBorder is null || SidebarSplitter is null || Root.ActualWidth <= 0) return;
        bool drawer = ShouldUseSidebarDrawer(Root.ActualWidth, _expandedSidebarWidth, _isSidebarPinnedExpanded);
        if (drawer == _sidebarUsesDrawer) return;

        _sidebarAnimationTimer?.Stop();
        _sidebarAnimationCompleted = null;
        _sidebarUsesDrawer = drawer;
        SetSidebarWidth(_isSidebarExpanded ? _expandedSidebarWidth : CompactSidebarWidth);
        if (_isSidebarExpanded && !drawer) ColSidebar.MinWidth = MinExpandedSidebarWidth;
    }

    internal static bool ShouldUseSidebarDrawer(double viewportWidth, double expandedWidth, bool pinned)
        => !pinned && viewportWidth > 0 && viewportWidth - expandedWidth - 6 < 720;

    internal static void ApplySidebarLayout(Grid root, Border sidebar, GridSplitter splitter,
        double railWidth, double sidebarWidth, bool drawer)
    {
        var column = root.ColumnDefinitions[0];
        column.MinWidth = railWidth;
        column.Width = new GridLength(drawer ? railWidth : sidebarWidth);
        Grid.SetColumnSpan(sidebar, drawer ? 3 : 1);
        sidebar.HorizontalAlignment = drawer ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        sidebar.Width = drawer ? sidebarWidth : double.NaN;
        Panel.SetZIndex(sidebar, drawer ? 10 : 0);
        splitter.Visibility = drawer ? Visibility.Collapsed : Visibility.Visible;
        root.InvalidateMeasure();
    }
}
