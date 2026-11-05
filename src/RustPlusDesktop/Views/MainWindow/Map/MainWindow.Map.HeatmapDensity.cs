using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    // The last heatmap read from the map's heatmap slice. That still runs to tens of MB, so a
    // density change re-colours this instead of parsing it again.
    private sealed record HeatmapRasterCache(string DataPath, DateTime WrittenUtc, string Category, byte[] Data, int Size);

    private HeatmapRasterCache? _heatmapRasterCache;
    private DispatcherTimer? _heatmapDensityDebounce;
    private bool _syncingHeatmapDensity;

    private async Task<(byte[] Data, int Size)?> ReadHeatmapRasterAsync(string dataPath, string category)
    {
        DateTime written = File.GetLastWriteTimeUtc(dataPath);
        if (_heatmapRasterCache is { } c && c.DataPath == dataPath && c.WrittenUtc == written && c.Category == category)
        {
            return (c.Data, c.Size);
        }

        // The heatmap slice instead of the whole map_data.json: ~20 MB rather than ~90 MB.
        string? slice = await Task.Run(() => Services.Map.MapDataSlices.EnsureHeatmaps(Path.GetDirectoryName(dataPath) ?? string.Empty));
        if (slice == null) return null;

        using var fs = new FileStream(slice, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var doc = await JsonDocument.ParseAsync(fs);
        if (ResourceHeatmapRaster.Read(doc.RootElement, category) is not { } heatmap)
        {
            return null;
        }

        _heatmapRasterCache = new HeatmapRasterCache(dataPath, written, category, heatmap.Data, heatmap.Size);
        return heatmap;
    }

    private async Task RenderHeatmapRasterAsync(byte[] data, int size)
    {
        float minDensity = (float)TrackingService.HeatmapMinDensity;
        int[]? pixels = await Task.Run(() => ResourceHeatmapRaster.Colorize(data, size, minDensity));
        if (pixels == null) return;

        // Apply the scale and margin
        var ptTopLeft = WorldToImagePx(0, _worldSizeS);
        var ptBotRight = WorldToImagePx(_worldSizeS, 0);
        if (ptBotRight.X > ptTopLeft.X && ptBotRight.Y > ptTopLeft.Y)
        {
            ImgHeatmap.Width = ptBotRight.X - ptTopLeft.X;
            ImgHeatmap.Height = ptBotRight.Y - ptTopLeft.Y;
            ImgHeatmap.Margin = new Thickness(ptTopLeft.X, ptTopLeft.Y, 0, 0);
        }

        var writeableBmp = new System.Windows.Media.Imaging.WriteableBitmap(
            size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null);

        writeableBmp.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);

        ImgHeatmap.Source = writeableBmp;
    }

    private void SyncHeatmapDensitySlider()
    {
        _syncingHeatmapDensity = true;
        try
        {
            SliderHeatmapDensity.Value = Math.Clamp(TrackingService.HeatmapMinDensity,
                SliderHeatmapDensity.Minimum, SliderHeatmapDensity.Maximum);
            TxtHeatmapDensityValue.Text = $"{Math.Round(SliderHeatmapDensity.Value * 100)}%";
        }
        finally
        {
            _syncingHeatmapDensity = false;
        }
    }

    private void SliderHeatmapDensity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Fires once during InitializeComponent, before the rest of the window exists.
        if (TxtHeatmapDensityValue == null) return;

        TxtHeatmapDensityValue.Text = $"{Math.Round(e.NewValue * 100)}%";
        if (_syncingHeatmapDensity) return;

        TrackingService.HeatmapMinDensity = e.NewValue;

        // Redraw once the thumb settles rather than on every tick of a drag.
        _heatmapDensityDebounce ??= CreateHeatmapDensityDebounce();
        _heatmapDensityDebounce.Stop();
        _heatmapDensityDebounce.Start();
    }

    private DispatcherTimer CreateHeatmapDensityDebounce()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            await ReapplyHeatmapDensityAsync();
        };
        return timer;
    }

    private async Task ReapplyHeatmapDensityAsync()
    {
        string? category = _currentActiveHeatmap;
        if (category == null) return;

        UpdateBentoActiveStates();

        if (_isMap3DActive && _map3DWebView?.CoreWebView2 != null)
        {
            string json = JsonSerializer.Serialize(new
            {
                type = "SET_HEATMAP_DENSITY",
                minDensity = TrackingService.HeatmapMinDensity
            });
            try
            {
                await _map3DWebView.CoreWebView2.ExecuteScriptAsync($"if (window.handleHeatmapRequest) window.handleHeatmapRequest({json});");
            }
            catch (Exception ex)
            {
                AppendLog($"[Heatmap] Error sending density to viewer: {ex.Message}");
            }
            return;
        }

        if (_heatmapRasterCache is { } c && c.Category == category)
        {
            await RenderHeatmapRasterAsync(c.Data, c.Size);
        }
        else
        {
            await DrawHeatmapOn2DMapAsync(category);
        }
    }
}
