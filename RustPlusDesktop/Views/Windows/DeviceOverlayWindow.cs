using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace RustPlusDesk;

/// <summary>Device and command widgets that remain accessible above the game, without a map.</summary>
public sealed class DeviceOverlayWindow : MiniMapWindow
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private int _focusHolders;

    public DeviceOverlayWindow() : base(new MiniMapLayers(null, null, null, null, null, null, null, null))
    {
        Title = "RustPlus Desktop widgets";
        Topmost = true;
        ShowInTaskbar = false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var styles = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)(styles | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
    }

    internal override void HoldKeyboardFocus(bool hold)
    {
        _focusHolders = Math.Max(0, _focusHolders + (hold ? 1 : -1));
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var styles = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        long next = _focusHolders > 0 ? styles & ~WS_EX_NOACTIVATE : styles | WS_EX_NOACTIVATE;
        if (next != styles) SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)next);
        if (_focusHolders > 0) SetForegroundWindow(hwnd);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
