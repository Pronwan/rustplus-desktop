using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using RustPlusDesk.Services.Cloud;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    // Admin feature-flag key. The backend row carries enabled + an optional
    // starts_at / ends_at window, so the event switches itself on and off
    // without a client release.
    private const string HalloweenFlagKey = "halloween_event";

    // Spawn-filter heatmaps only: none of these are placed in the map file.
    // The server spawns them at runtime once the event convars are raised.
    private static readonly string[] HalloweenHeatmapCategories =
        { "halloween_portal", "scarecrow", "murderer", "zombie" };

    private static readonly TimeSpan HalloweenFlagRefreshInterval = TimeSpan.FromMinutes(10);

    private DispatcherTimer? _halloweenFlagTimer;
    private DateTime _halloweenFlagsFetchedUtc = DateTime.MinValue;

    private void StartHalloweenEventGate()
    {
        _ = RefreshHalloweenEventGateAsync(force: true);

        // Re-checked every minute so the card closes on time even when the popup
        // never reopens; the feed itself is only fetched every ten.
        _halloweenFlagTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _halloweenFlagTimer.Tick += (_, _) => _ = RefreshHalloweenEventGateAsync(force: false);
        _halloweenFlagTimer.Start();
    }

    private async Task RefreshHalloweenEventGateAsync(bool force)
    {
        if (force || DateTime.UtcNow - _halloweenFlagsFetchedUtc >= HalloweenFlagRefreshInterval)
        {
            _halloweenFlagsFetchedUtc = DateTime.UtcNow;
            try { await CloudFeatureFlags.RefreshAsync(); }
            catch (Exception ex) { AppendLog($"[Halloween] Feature flag refresh failed: {ex.Message}"); }
        }

        ApplyHalloweenEventGate();
    }

    private void ApplyHalloweenEventGate()
    {
        bool active = CloudFeatureFlags.IsWithinWindow(HalloweenFlagKey);
        HalloweenHeatmapCard.Visibility = active ? Visibility.Visible : Visibility.Collapsed;

        // The event ended (or was switched off) while one of its layers was showing.
        if (!active && _currentActiveHeatmap != null && HalloweenHeatmapCategories.Contains(_currentActiveHeatmap))
        {
            _currentActiveHeatmap = null;
            ClearActiveHeatmap();
            UpdateBentoActiveStates();
        }
    }

    private void ClearActiveHeatmap()
    {
        ImgHeatmap.Source = null;
        // Its raw raster too: kept only so the density slider can recolour a heatmap that is shown.
        _heatmapRasterCache = null;
        if (_isMap3DActive && _map3DWebView?.CoreWebView2 != null)
        {
            string json = JsonSerializer.Serialize(new { type = "CLEAR_HEATMAP" });
            _ = _map3DWebView.CoreWebView2.ExecuteScriptAsync($"if (window.handleHeatmapRequest) window.handleHeatmapRequest({json});");
        }
    }
}
