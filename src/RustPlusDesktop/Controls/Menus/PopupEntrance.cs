using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Media;

namespace RustPlusDesk.Controls.Menus;

/// <summary>
/// Slides a popup window from an offset into its resting place, paced by the
/// compositor instead of the WPF dispatcher.
///
/// A menu is its own top-level window, and the acrylic behind it belongs to
/// that window rather than to anything WPF draws. Clipping or translating
/// content leaves the blur at full size, so the whole window has to move to
/// get a believable entrance. Moving it through <c>Popup.VerticalOffset</c>
/// re-runs WPF's placement logic every frame, which costs up to ~6 ms on the
/// UI thread. That's a whole frame on a 144–180 Hz display, and the cost
/// grows the busier the UI thread is.
///
/// This class moves the window with a bare <c>SetWindowPos</c> from a short
/// lived background thread that waits on <c>DwmFlush</c>, so every composed
/// frame gets exactly one position computed for the moment it's shown. The
/// clock starts at the first rendered frame, not when the popup is created,
/// so a menu that takes a while to lay out still shows its whole entrance.
/// </summary>
internal sealed class PopupEntrance
{
    private const int WM_WINDOWPOSCHANGING = 0x0046;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;

    /// <summary>
    /// Our own moves skip WM_WINDOWPOSCHANGING, so the hook can treat every
    /// move it does see as WPF repositioning the popup.
    /// </summary>
    private const uint SWP_NOSENDCHANGING = 0x0400;

    private const uint MoveFlags =
        SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING;

    /// <summary>How long the thread waits for WPF to render the first frame before starting anyway.</summary>
    private static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromMilliseconds(250);

    private readonly HwndSource _source;
    private readonly IntPtr _hwnd;
    private readonly int _fromX;
    private readonly int _fromY;
    private readonly double _durationTicks;
    private readonly HwndSourceHook _hook;
    private readonly object _gate = new();

    // Guarded by _gate. Rest is where WPF wants the window; offset is how far
    // from it the window currently sits.
    private int _restX;
    private int _restY;
    private int _offsetX;
    private int _offsetY;

    private volatile bool _firstFrameRendered;
    private volatile bool _cancelled;
    private int _hookRemoved;

    private PopupEntrance(HwndSource source, int fromX, int fromY, TimeSpan duration)
    {
        _source = source;
        _hwnd = source.Handle;
        _fromX = fromX;
        _fromY = fromY;
        _durationTicks = duration.TotalSeconds * Stopwatch.Frequency;
        _hook = Hook;
    }

    /// <summary>
    /// Parks the window at its resting position plus (<paramref name="fromX"/>,
    /// <paramref name="fromY"/>) physical pixels and slides it home. Call on
    /// the UI thread after the popup is shown and before its first render, e.g.
    /// from Popup.Opened.
    /// </summary>
    public static PopupEntrance? Begin(HwndSource source, int fromX, int fromY, TimeSpan duration)
    {
        if (source.IsDisposed || source.Handle == IntPtr.Zero) return null;
        if ((fromX == 0 && fromY == 0) || duration <= TimeSpan.Zero) return null;
        if (!GetWindowRect(source.Handle, out var rect)) return null;

        var entrance = new PopupEntrance(source, fromX, fromY, duration);
        entrance._restX = rect.Left;
        entrance._restY = rect.Top;
        entrance._offsetX = fromX;
        entrance._offsetY = fromY;

        source.AddHook(entrance._hook);

        // Synchronously, so the first frame WPF presents is already at the
        // starting point instead of flashing at the resting one.
        SetWindowPos(source.Handle, IntPtr.Zero, rect.Left + fromX, rect.Top + fromY, 0, 0, MoveFlags);

        CompositionTarget.Rendering += entrance.OnFirstRendering;

        var thread = new Thread(entrance.Run)
        {
            IsBackground = true,
            Name = "Popup entrance",
            Priority = ThreadPriority.AboveNormal,
        };
        thread.Start();

        return entrance;
    }

    /// <summary>Stops the slide where it is. The popup is closing, so there is nothing to settle.</summary>
    public void Cancel()
    {
        _cancelled = true;
        CompositionTarget.Rendering -= OnFirstRendering;
        RemoveHook();
    }

    private void OnFirstRendering(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnFirstRendering;
        _firstFrameRendered = true;
    }

    private void Run()
    {
        try
        {
            // Rendering fires just before WPF draws the frame; the flush after
            // it is the frame that frame lands in. Nothing has been visible yet.
            var waitStart = Stopwatch.GetTimestamp();
            var waitLimit = FirstFrameTimeout.TotalSeconds * Stopwatch.Frequency;

            while (!_firstFrameRendered && !_cancelled
                   && Stopwatch.GetTimestamp() - waitStart < waitLimit)
            {
                WaitForVBlank();
            }

            if (_cancelled) return;

            // WPF presents a transparent window with UpdateLayeredWindow, and its
            // render thread passes along the window position it last heard of.
            // That first present can land a frame after Rendering, carrying
            // the parked position, and would yank the window back by however
            // far the slide had got. Two flushes see it through before the
            // slide starts moving.
            WaitForVBlank();
            WaitForVBlank();
            var start = Stopwatch.GetTimestamp();
            var lastVBlank = start;

            // Seeded from the wait above, which already spanned whole frames.
            double framePeriod = MeasureFramePeriod();

            while (!_cancelled)
            {
                var now = Stopwatch.GetTimestamp();

                // A move issued now appears at the next composition, so place
                // the window where it should be at that moment.
                var t = (now + framePeriod - start) / _durationTicks;
                if (t >= 1) break;

                var remaining = 1 - FluentEase.Decelerate(t);
                MoveTo((int)Math.Round(_fromX * remaining), (int)Math.Round(_fromY * remaining));

                WaitForVBlank();

                var after = Stopwatch.GetTimestamp();
                var measured = after - lastVBlank;
                lastVBlank = after;

                // Track the real refresh rate, ignoring frames the scheduler
                // made us miss, so the prediction stays one frame ahead.
                if (measured > Stopwatch.Frequency / 500.0 && measured < Stopwatch.Frequency / 20.0)
                    framePeriod += (measured - framePeriod) * 0.25;
            }
        }
        catch
        {
            // Animation is cosmetic. Whatever happened, the window still has to land.
        }
        finally
        {
            if (!_cancelled) MoveTo(0, 0);
            _source.Dispatcher.BeginInvoke(new Action(() =>
            {
                CompositionTarget.Rendering -= OnFirstRendering;
                RemoveHook();
            }));
        }
    }

    private void MoveTo(int offsetX, int offsetY)
    {
        int x, y;

        lock (_gate)
        {
            if (offsetX == _offsetX && offsetY == _offsetY) return;
            _offsetX = offsetX;
            _offsetY = offsetY;
            x = _restX + offsetX;
            y = _restY + offsetY;
        }

        if (_cancelled || !IsWindowVisible(_hwnd)) return;

        // Cross-thread, this waits for the UI thread to process the move,
        // which coalesces frames naturally if it's busy, unlike
        // SWP_ASYNCWINDOWPOS, whose queued moves would replay in a burst.
        SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, MoveFlags);
    }

    /// <summary>
    /// WPF repositions a popup whenever its size changes, which can happen
    /// mid-slide as items finish loading. Take that position as the new resting
    /// place and keep the current offset on top of it, instead of letting it
    /// snap the window home early.
    /// </summary>
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_WINDOWPOSCHANGING || lParam == IntPtr.Zero) return IntPtr.Zero;

        var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
        if ((pos.flags & SWP_NOMOVE) != 0) return IntPtr.Zero;

        lock (_gate)
        {
            _restX = pos.x;
            _restY = pos.y;
            pos.x += _offsetX;
            pos.y += _offsetY;
        }

        Marshal.StructureToPtr(pos, lParam, false);
        return IntPtr.Zero;
    }

    private void RemoveHook()
    {
        if (Interlocked.Exchange(ref _hookRemoved, 1) != 0) return;
        if (!_source.IsDisposed) _source.RemoveHook(_hook);
    }

    /// <summary>
    /// The display's frame period in Stopwatch ticks, taken from the
    /// compositor's own timing so a 144 Hz screen is not paced like a 60 Hz one.
    /// </summary>
    private static double MeasureFramePeriod()
    {
        var timing = new DWM_TIMING_INFO { cbSize = Marshal.SizeOf<DWM_TIMING_INFO>() };

        if (DwmGetCompositionTimingInfo(IntPtr.Zero, ref timing) >= 0
            && timing.rateRefresh.uiNumerator > 0 && timing.rateRefresh.uiDenominator > 0)
        {
            var hz = (double)timing.rateRefresh.uiNumerator / timing.rateRefresh.uiDenominator;
            if (hz is >= 20 and <= 500) return Stopwatch.Frequency / hz;
        }

        return Stopwatch.Frequency / 60.0;
    }

    /// <summary>
    /// Blocks until the compositor presents its next frame. DwmFlush can return
    /// straight away when there is nothing left to compose, so a short sleep
    /// keeps that case from spinning.
    /// </summary>
    private static void WaitForVBlank()
    {
        var before = Stopwatch.GetTimestamp();
        var flushed = DwmFlush() >= 0;

        if (!flushed || Stopwatch.GetTimestamp() - before < Stopwatch.Frequency / 2000)
            Thread.Sleep(1);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [StructLayout(LayoutKind.Sequential)]
    private struct UNSIGNED_RATIO
    {
        public uint uiNumerator;
        public uint uiDenominator;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct DWM_TIMING_INFO
    {
        public int cbSize;
        public UNSIGNED_RATIO rateRefresh;
        public ulong qpcRefreshPeriod;
        public UNSIGNED_RATIO rateCompose;
        public ulong qpcVBlank;
        public ulong cRefresh;
        public uint cDXRefresh;
        public ulong qpcCompose;
        public ulong cFrame;
        public uint cDXPresent;
        public ulong cRefreshFrame;
        public ulong cFrameSubmitted;
        public uint cDXPresentSubmitted;
        public ulong cFrameConfirmed;
        public uint cDXPresentConfirmed;
        public ulong cRefreshConfirmed;
        public uint cDXRefreshConfirmed;
        public ulong cFramesLate;
        public uint cFramesOutstanding;
        public ulong cFrameDisplayed;
        public ulong qpcFrameDisplayed;
        public ulong cRefreshFrameDisplayed;
        public ulong cFrameComplete;
        public ulong qpcFrameComplete;
        public ulong cFramePending;
        public ulong qpcFramePending;
        public ulong cFramesDisplayed;
        public ulong cFramesComplete;
        public ulong cFramesPending;
        public ulong cFramesAvailable;
        public ulong cFramesDropped;
        public ulong cFramesMissed;
        public ulong cRefreshNextDisplayed;
        public ulong cRefreshNextPresented;
        public ulong cRefreshesDisplayed;
        public ulong cRefreshesPresented;
        public ulong cRefreshStarted;
        public ulong cPixelsReceived;
        public ulong cPixelsDrawn;
        public ulong cBuffersEmpty;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetCompositionTimingInfo(IntPtr hwnd, ref DWM_TIMING_INFO timingInfo);
}

/// <summary>Fluent motion curves.</summary>
internal static class FluentEase
{
    /// <summary>
    /// Fluent "fast out, slow in", <c>cubic-bezier(0, 0, 0, 1)</c>: the curve
    /// WinUI uses for elements entering the screen.
    /// </summary>
    public static double Decelerate(double t) => CubicBezier(0, 0, 0, 1, t);

    /// <summary>Evaluates a CSS-style cubic Bézier timing function at progress <paramref name="x"/>.</summary>
    public static double CubicBezier(double x1, double y1, double x2, double y2, double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;

        // Solve curveX(s) = x for s, then return curveY(s). Bisection: the
        // curve is monotonic in x for control points inside [0, 1], and 20
        // halvings is far below a pixel.
        double lo = 0, hi = 1, s = x;
        for (int i = 0; i < 20; i++)
        {
            s = (lo + hi) / 2;
            if (Bezier(x1, x2, s) < x) lo = s; else hi = s;
        }

        return Bezier(y1, y2, s);
    }

    private static double Bezier(double p1, double p2, double s)
    {
        var inv = 1 - s;
        return 3 * inv * inv * s * p1 + 3 * inv * s * s * p2 + s * s * s;
    }
}
