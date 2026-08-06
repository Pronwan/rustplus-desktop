using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    // Radial-gradient blobs drawn on the Overlay for the death heatmap; tracked so
    // they can be cleared and redrawn. They live on the transformed Overlay, so
    // they pan/zoom with the map without needing a redraw.
    private readonly List<FrameworkElement> _deathHeatEls = new();
    private bool _showDeathHeatmap;

    // Influence radius of one death, in world units. Overlapping blobs blend so
    // dense areas glow hotter.
    private const double DeathHeatWorldRadius = 90.0;

    private void ChkDeathHeatmap_Checked(object sender, RoutedEventArgs e)
    {
        _showDeathHeatmap = ChkDeathHeatmap.IsChecked == true;
        if (_vm != null && !_vm.IsInitializing)
            TrackingService.MapShowDeathHeatmap = _showDeathHeatmap;

        RedrawDeathHeatmap();
    }

    private void ClearDeathHeatmap()
    {
        if (Overlay != null)
        {
            foreach (var el in _deathHeatEls)
                Overlay.Children.Remove(el);
        }

        _deathHeatEls.Clear();
    }

    public void RedrawDeathHeatmap()
    {
        ClearDeathHeatmap();

        if (!_showDeathHeatmap || Overlay == null || _worldSizeS <= 0 || _worldRectPx.Width <= 0)
            return;

        var positions = Services.Deaths.DeathLogStore.LoadPositions(GetServerKey());
        if (positions.Count == 0)
            return;

        // World radius -> pixels at the base map scale.
        var origin = WorldToImagePx(0, 0);
        var edge = WorldToImagePx(DeathHeatWorldRadius, 0);
        double radiusPx = Math.Abs(edge.X - origin.X);
        if (radiusPx <= 0)
            radiusPx = 30;
        double diameter = radiusPx * 2;

        var brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(150, 255, 60, 0), 0.0),
                new GradientStop(Color.FromArgb(70, 255, 130, 0), 0.55),
                new GradientStop(Color.FromArgb(0, 255, 170, 0), 1.0),
            },
        };
        brush.Freeze();

        foreach (var (x, y) in positions)
        {
            var px = WorldToImagePx(x, y);
            var ellipse = new Ellipse
            {
                Width = diameter,
                Height = diameter,
                Fill = brush,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(ellipse, px.X - radiusPx);
            Canvas.SetTop(ellipse, px.Y - radiusPx);
            // Above the map/grid, below markers and death pins.
            Panel.SetZIndex(ellipse, 50);
            Overlay.Children.Add(ellipse);
            _deathHeatEls.Add(ellipse);
        }
    }
}
