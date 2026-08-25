using System;
using System.Runtime.InteropServices;

namespace RustPlusDesk.Services;

/// <summary>
/// Lifts a window into the topmost band without activating it. Needed for WPF popups shown from
/// a Topmost window: the popup gets its own HWND, which WPF creates as an ordinary top-level
/// window, so it renders behind its own parent and looks like it never opened.
/// </summary>
internal static class NativeTopmost
{
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public static void Promote(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        // NOACTIVATE matters: the suggestion list must not steal focus from the text box that
        // opened it, or typing would stop working the moment the list appears.
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
