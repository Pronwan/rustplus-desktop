using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RustPlusDesk.Helpers;

/// <summary>
/// Applies Windows 11 system backdrops (Mica / MicaAlt / Acrylic / None)
/// via direct DWM P/Invoke — no external packages required.
/// Falls back silently on Windows 10 or older.
/// </summary>
public static class WindowBackdropHelper
{
    // DWM attributes
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE     = 38;
    private const int DWMWA_MICA_EFFECT             = 1029; // Win11 21H2 fallback

    public enum BackdropType
    {
        None     = 1,
        Mica     = 2,
        MicaAlt  = 4,
        Acrylic  = 3,
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>
    /// Apply the Mica (or other) backdrop to a WPF Window.
    /// Must be called after the window handle is created (e.g. in Loaded or constructor after InitializeComponent).
    /// </summary>
    public static bool Apply(Window window, BackdropType backdrop = BackdropType.Mica)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();

            // 1. Force dark mode title bar
            int dark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

            // 2. Try Windows 11 22H2+ systemBackdropType
            int type = (int)backdrop;
            int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));

            if (hr != 0)
            {
                // Fallback: Windows 11 21H2 Mica-only attribute
                int mica = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_MICA_EFFECT, ref mica, sizeof(int));
            }

            // Make the WPF window background transparent so Mica shows through
            window.Background = System.Windows.Media.Brushes.Transparent;

            // Hook into HwndSource for extended frame
            var source = HwndSource.FromHwnd(hwnd);
            source?.CompositionTarget?.Let(ct => ct.BackgroundColor = System.Windows.Media.Colors.Transparent);

            return true;
        }
        catch
        {
            return false; // Windows 10 fallback — silent
        }
    }
}

// Small helper to avoid null-check verbosity
internal static class ObjectExtensions
{
    public static void Let<T>(this T? obj, Action<T> action) where T : class
    {
        if (obj != null) action(obj);
    }
}
