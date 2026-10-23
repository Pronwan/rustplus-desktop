using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private void MapToolbar_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyMapToolbarLayout(MapToolbarLayout, MapToolbarFilters, MapQuickFilters, MapToolbarActions, MapLayersLabel);
        UpdateMapActiveLayerCount(sender, e);
        foreach (var toggle in new ToggleButton[] { ChkGrid, ChkPlayers, Monuments, ChkShops, ChkDeathHeatmap,
                     ChkNoBuildZones, ChkDeathMarkers, ChkCargoPath, ChkCardReaders, ChkKeycards })
        {
            toggle.Checked -= UpdateMapActiveLayerCount;
            toggle.Unchecked -= UpdateMapActiveLayerCount;
            toggle.Checked += UpdateMapActiveLayerCount;
            toggle.Unchecked += UpdateMapActiveLayerCount;
            toggle.IsEnabledChanged -= MapLayerAvailabilityChanged;
            toggle.IsVisibleChanged -= MapLayerAvailabilityChanged;
            toggle.IsEnabledChanged += MapLayerAvailabilityChanged;
            toggle.IsVisibleChanged += MapLayerAvailabilityChanged;
        }
    }

    private void MapLayerAvailabilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        => UpdateMapActiveLayerCount(sender, new RoutedEventArgs());

    private void MapToolbar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (MapToolbarActions is not null && MapToolbarLayout.IsLoaded)
            ApplyMapToolbarLayout(MapToolbarLayout, MapToolbarFilters, MapQuickFilters, MapToolbarActions, MapLayersLabel);
    }

    private void UpdateMapActiveLayerCount(object sender, RoutedEventArgs e)
    {
        int count = new ToggleButton[] { ChkGrid, ChkPlayers, Monuments, ChkShops, ChkDeathHeatmap,
                ChkNoBuildZones, ChkDeathMarkers, ChkCargoPath, ChkCardReaders, ChkKeycards }
            .Count(toggle => toggle.IsChecked == true && toggle.IsEnabled && toggle.Visibility == Visibility.Visible);
        MapActiveLayerCount.Text = $"({count})";
    }

    internal static void ApplyMapToolbarLayout(Grid layout, FrameworkElement filters,
        Border quickFilters, FrameworkElement actions, FrameworkElement? layersLabel = null)
    {
        if (layout.ActualWidth <= 0) return;

        // Measure the full labels so translations and sidebar resizing share the same breakpoints.
        var unconstrained = new Size(double.PositiveInfinity, double.PositiveInfinity);
        SetMapQuickFilterMode(quickFilters, false);
        if (layersLabel is not null) layersLabel.Visibility = Visibility.Visible;
        actions.Margin = new Thickness(0);
        filters.InvalidateMeasure();
        filters.Measure(unconstrained);
        actions.Measure(unconstrained);
        if (filters.DesiredSize.Width + actions.DesiredSize.Width + 8 > layout.ActualWidth)
        {
            SetMapQuickFilterMode(quickFilters, true);
            if (layersLabel is not null) layersLabel.Visibility = Visibility.Collapsed;
            filters.InvalidateMeasure();
            filters.Measure(unconstrained);
        }

        bool stacked = filters.DesiredSize.Width + actions.DesiredSize.Width + 8 > layout.ActualWidth;
        Grid.SetColumnSpan(filters, stacked ? 2 : 1);
        Grid.SetRow(actions, stacked ? 1 : 0);
        Grid.SetColumn(actions, stacked ? 0 : 1);
        Grid.SetColumnSpan(actions, stacked ? 2 : 1);
        actions.Margin = stacked ? new Thickness(0, 8, 0, 0) : new Thickness(8, 0, 0, 0);
    }

    internal static void SetMapQuickFilterMode(Border quickFilters, bool compact)
    {
        // Update presentation before measuring; binding triggers settle after this layout pass.
        foreach (CheckBox filter in ((Panel)quickFilters.Child).Children)
        {
            var content = (Panel)filter.Content;
            content.Children[0].Visibility = Visibility.Visible;
            ((FrameworkElement)content.Children[0]).Margin = compact ? new Thickness(0) : new Thickness(0, 0, 6, 0);
            content.Children[1].Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            filter.Padding = new Thickness(compact ? 8 : 14, 6, compact ? 8 : 14, 6);
            filter.Width = compact ? 34 : double.NaN;
            content.InvalidateMeasure();
            filter.InvalidateMeasure();
        }
        ((Panel)quickFilters.Child).InvalidateMeasure();
        quickFilters.InvalidateMeasure();
        quickFilters.Tag = compact;
    }
}
