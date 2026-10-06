using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace RustPlusDesk
{
    /// <summary>
    /// The check that the dock really ended up on screen once a layout has been applied.
    /// </summary>
    public partial class MiniMapWindow
    {
        private bool _onScreenCheckQueued;

        /// <summary>
        /// Once the window has actually taken its new size and place, makes sure all of it is
        /// inside the working area of the monitor it is on.
        ///
        /// <see cref="ClampToScreen"/> already does this, but in WPF's numbers - the Width and
        /// Left it has just written, against a monitor converted to device-independent pixels.
        /// Adding a widget could still leave its right-hand edge past the screen, so this asks
        /// Windows for the rectangle the window really occupies and the work area of the monitor
        /// it really is on, both in the same physical pixels, and moves by whatever is left over.
        /// </summary>
        private void QueueOnScreenCheck()
        {
            if (_onScreenCheckQueued) return;
            _onScreenCheckQueued = true;

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _onScreenCheckQueued = false;
                KeepFullyOnScreen();
            }));
        }

        private void KeepFullyOnScreen()
        {
            // Mid-drag only reachability matters, and pulling the dock back would make a monitor
            // boundary impossible to cross - see ClampToScreen.
            if (!IsVisible || _draggingTile != null) return;
            if (double.IsNaN(Left) || double.IsNaN(Top)) return;

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            if (!GetWindowRect(hwnd, out var win)) return;

            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return;

            var work = info.rcWork;
            int width = win.Right - win.Left;
            int height = win.Bottom - win.Top;

            int dx = 0;
            if (width > work.Right - work.Left) dx = work.Left - win.Left;   // too wide: left edge wins
            else if (win.Right > work.Right) dx = work.Right - win.Right;
            else if (win.Left < work.Left) dx = work.Left - win.Left;

            int dy = 0;
            if (height > work.Bottom - work.Top) dy = work.Top - win.Top;    // too tall: top wins
            else if (win.Bottom > work.Bottom) dy = work.Bottom - win.Bottom;
            else if (win.Top < work.Top) dy = work.Top - win.Top;

            if (dx == 0 && dy == 0) return;

            double scale = 1.0;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
                scale = source.CompositionTarget.TransformToDevice.M11;
            if (scale <= 0) scale = 1.0;

            double oldLeft = Left, oldTop = Top;
            Left += dx / scale;
            Top += dy / scale;

            AnchorOriginToWindow();
            SaveDockPosition();
            HoldSettingsPopupInPlace(Left - oldLeft, Top - oldTop);
            FollowAiAnswer();
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    }
}
