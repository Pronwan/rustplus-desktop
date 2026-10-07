using System;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using RustPlusDesk.Services;

namespace RustPlusDesk;

public partial class MiniMapWindow
{
    private const string MapPositionCacheKey = "minimap_position";
    private sealed record MapPosition(int Left, int Top);

    private void InitMiniMap()
    {
        ChromeLayer.Visibility = Visibility.Collapsed;
        SettingsOverlay.HideWidgetSettings();
        if (StorageService.LoadCache<MiniMapSettings>("minimap_settings") is { } settings)
            ApplyLoadedSettings(settings);
        SourceInitialized += (_, _) => RestoreMapPosition();
        Closing += (_, _) => SaveMapPosition();
    }

    private void SaveMapPosition()
    {
        if (double.IsNaN(Left) || double.IsNaN(Top)) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero && GetWindowRect(handle, out var rect))
            StorageService.SaveCache(MapPositionCacheKey, new MapPosition(rect.Left, rect.Top));
    }

    private void RestoreMapPosition()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (!GetWindowRect(handle, out var rect)) return;

        var screens = System.Windows.Forms.Screen.AllScreens;
        var areas = screens.Select(s => new Rect(s.WorkingArea.X, s.WorkingArea.Y,
            s.WorkingArea.Width, s.WorkingArea.Height)).ToArray();
        int preferred = Array.FindIndex(screens, s => !s.Primary);
        var saved = StorageService.LoadCache<MapPosition>(MapPositionCacheKey);
        var position = GetMapPosition(saved == null ? null : new Point(saved.Left, saved.Top),
            new Size(rect.Right - rect.Left, rect.Bottom - rect.Top), areas, Math.Max(0, preferred));

        // Both the saved position and monitor areas use physical pixels for mixed DPI displays.
        SetWindowPos(handle, IntPtr.Zero, (int)position.X, (int)position.Y, 0, 0, 0x0015);
    }

    /// <summary>Restores a reachable map position, or starts on the preferred monitor.</summary>
    internal static Point GetMapPosition(Point? saved, Size size, Rect[] areas, int preferred)
    {
        var area = areas[preferred];
        if (saved is { } point && double.IsFinite(point.X) && double.IsFinite(point.Y))
        {
            var window = new Rect(point, size);
            var match = areas.OrderByDescending(a =>
            {
                var overlap = Rect.Intersect(a, window);
                return overlap.IsEmpty ? 0 : overlap.Width * overlap.Height;
            }).First();
            if (match.IntersectsWith(window)) area = match;
            else saved = null;
        }
        else saved = null;

        var desired = saved ?? new Point(area.Right - size.Width - 20, area.Top + 20);
        return new Point(Math.Clamp(desired.X, area.Left, Math.Max(area.Left, area.Right - size.Width)),
            Math.Clamp(desired.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));
    }
}
