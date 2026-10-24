using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private (IntPtr Handle, int Left, int Width, int Height)? _map3DViewportClip;

    private void UpdateMap3DViewportClip(object? sender, EventArgs e)
    {
        var view = _map3DWebView;
        if (view == null || !view.IsVisible || view.Handle == IntPtr.Zero || PresentationSource.FromVisual(view) == null) return;
        var topLeft = view.PointToScreen(new Point());
        var bottomRight = view.PointToScreen(new Point(view.ActualWidth, view.ActualHeight));
        int width = (int)Math.Ceiling(bottomRight.X - topLeft.X);
        int height = (int)Math.Ceiling(bottomRight.Y - topLeft.Y);
        int left = 0;
        if (Window.GetWindow(view) == this && _sidebarUsesDrawer && LeftPanelBorder.IsVisible)
        {
            double sidebarRight = LeftPanelBorder.PointToScreen(new Point(LeftPanelBorder.ActualWidth, 0)).X;
            left = GetMap3DLeftClip(topLeft.X, width, sidebarRight);
        }
        var clip = (view.Handle, left, width, height);
        if (_map3DViewportClip == clip) return;
        if (ApplyMap3DWindowRegion(view.Handle, left, width, height)) _map3DViewportClip = clip;
    }

    internal static int GetMap3DLeftClip(double viewportLeft, int width, double sidebarRight)
        => (int)Math.Clamp(Math.Ceiling(sidebarRight - viewportLeft), 0, Math.Max(0, width));

    // Clip the covered HWND pixels instead of resizing the camera or capturing every browser frame.
    internal static bool ApplyMap3DWindowRegion(IntPtr handle, int left, int width, int height)
    {
        if (left == 0) return SetWindowRgn(handle, IntPtr.Zero, true) != 0;
        IntPtr region = CreateRectRgn(left, 0, Math.Max(left, width), Math.Max(0, height));
        if (region == IntPtr.Zero) return false;
        if (SetWindowRgn(handle, region, true) != 0) return true; // Windows owns the region after success.
        DeleteObject(region);
        return false;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr region);
}
