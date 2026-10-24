using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    // One density image for the death heatmap (see DeathHeatmapRaster), tracked so it can be
    // cleared and redrawn. It lives on the transformed Overlay, so it pans/zooms with the map
    // without needing a redraw.
    private Image? _deathHeatImage;
    private bool _showDeathHeatmap;

    // Influence radius of one death, in world units.
    private const double DeathHeatWorldRadius = 90.0;

    private System.Windows.Threading.DispatcherTimer? _deathHeatDensityDebounce;
    private bool _syncingDeathHeatDensity;

    private void ChkDeathHeatmap_Checked(object sender, RoutedEventArgs e)
    {
        _showDeathHeatmap = ChkDeathHeatmap.IsChecked == true;
        if (_vm != null && !_vm.IsInitializing)
            TrackingService.MapShowDeathHeatmap = _showDeathHeatmap;

        if (_showDeathHeatmap)
        {
            _syncingDeathHeatDensity = true;
            SliderDeathHeatmapDensity.Value = Math.Clamp(TrackingService.DeathHeatmapMinDensity,
                SliderDeathHeatmapDensity.Minimum, SliderDeathHeatmapDensity.Maximum);
            _syncingDeathHeatDensity = false;
        }

        RedrawDeathHeatmap();
    }

    private void SliderDeathHeatmapDensity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Fires once during InitializeComponent, before the rest of the window exists.
        if (TxtDeathHeatmapDensityValue == null) return;

        TxtDeathHeatmapDensityValue.Text = $"{Math.Round(e.NewValue * 100)}%";
        if (_syncingDeathHeatDensity) return;

        TrackingService.DeathHeatmapMinDensity = e.NewValue;

        // Redraw once the thumb settles rather than on every tick of a drag.
        if (_deathHeatDensityDebounce == null)
        {
            _deathHeatDensityDebounce = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _deathHeatDensityDebounce.Tick += (_, _) =>
            {
                _deathHeatDensityDebounce.Stop();
                RedrawDeathHeatmap();
            };
        }
        _deathHeatDensityDebounce.Stop();
        _deathHeatDensityDebounce.Start();
    }

    // The layer row's status line: how many deaths are behind the colour.
    private void UpdateDeathHeatmapStatus(int deaths)
    {
        if (DeathHeatmapOptions == null) return;

        DeathHeatmapOptions.Visibility = _showDeathHeatmap ? Visibility.Visible : Visibility.Collapsed;
        if (!_showDeathHeatmap) return;

        TxtDeathHeatmapStatus.Text = deaths == 0
            ? "On · no deaths logged this wipe yet"
            : $"On · {deaths} death{(deaths == 1 ? "" : "s")} this wipe";
    }

    private void ClearDeathHeatmap()
    {
        if (_deathHeatImage != null && Overlay != null)
            RemoveFromMapLayers(_deathHeatImage);
        _deathHeatImage = null;
    }

    public void RedrawDeathHeatmap()
    {
        ClearDeathHeatmap();
        UpdateDeathHeatmapStatus(0);

        // The Deep Sea view maps a different box onto the image; main-world deaths do not belong there.
        if (!_showDeathHeatmap || Overlay == null || _worldSizeS <= 0 || _worldRectPx.Width <= 0 || _isShowingDeepSeaMap)
            return;

        // The log keeps every death ever seen on this server, across wipes and therefore across
        // maps. Only this wipe's deaths say anything about this map.
        var wipeStart = (_vm?.Selected?.WipeTime ?? _vm?.Selected?.RustMapsWipeTime)?.ToUniversalTime();
        long? sinceUnix = wipeStart is { } start ? new DateTimeOffset(start).ToUnixTimeSeconds() : null;
        var positions = Services.Deaths.DeathLogStore.LoadPositions(GetServerKey(), sinceUnix);

        UpdateDeathHeatmapStatus(positions.Count);

        var bitmap = Services.Deaths.DeathHeatmapRaster.Render(positions, _worldSizeS, DeathHeatWorldRadius,
            TrackingService.DeathHeatmapMinDensity);
        if (bitmap == null)
            return;

        var northWest = WorldToImagePx(0, _worldSizeS);
        var southEast = WorldToImagePx(_worldSizeS, 0);
        var image = new Image
        {
            Source = bitmap,
            Width = Math.Abs(southEast.X - northWest.X),
            Height = Math.Abs(southEast.Y - northWest.Y),
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Linear);
        Canvas.SetLeft(image, Math.Min(northWest.X, southEast.X));
        Canvas.SetTop(image, Math.Min(northWest.Y, southEast.Y));
        // Above the map/grid, below markers and death pins.
        Panel.SetZIndex(image, 50);
        IconLayer.Children.Add(image);
        _deathHeatImage = image;
    }
}
