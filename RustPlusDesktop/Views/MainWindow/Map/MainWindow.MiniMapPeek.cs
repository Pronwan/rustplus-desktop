using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

/// <summary>
/// Hold a key, see the map; let go, it is gone.
///
/// A second monitor is the thing that makes a mini-map comfortable, and plenty of people have
/// one screen. Leaving it up over the game on that screen is an advantage the game does not
/// give anyone — Rust's own map takes a keypress and covers what you were looking at, and that
/// cost is the point of it.
///
/// So this gives the same bargain: the map is only on screen while the key is held, over the
/// game, the way the in-game map is. Nobody gets to watch the map and the world at once.
/// </summary>
public partial class MainWindow
{
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    /// <summary>The gesture currently registered for this, so it can be given back.</summary>
    private string? _peekGesture;

    private DispatcherTimer? _peekReleaseTimer;
    private bool _peeking;
    private int _consecutiveUpTicks;
    private bool _asyncKeySawDown;

    private LowLevelKeyboardProc? _peekHookProc;
    private IntPtr _peekHookId = IntPtr.Zero;
    private uint _peekHookVk;

    /// <summary>When the current peek started, for the limit below.</summary>
    private DateTime _peekStartedUtc;

    /// <summary>
    /// How long the map can be held up for.
    ///
    /// The bargain this makes is that seeing the map costs the view, the way the game's own
    /// map does. Taping the key down and playing on with the map in the corner is not that
    /// bargain, so after this it goes away whether the key is still down or not, and stays
    /// away until the key is released and pressed again.
    /// </summary>
    private static readonly TimeSpan PeekLimit = TimeSpan.FromSeconds(20);

    /// <summary>Set when the limit ran out, so the same hold cannot start a new peek.</summary>
    private bool _peekSpent;

    /// <summary>
    /// Registers the configured peek gesture, replacing whatever was registered before.
    ///
    /// Alongside the companion's push to talk rather than part of the device bindings: like
    /// that one, it belongs to a window rather than to a server, and must survive the sweep
    /// that unregisters everything when the server changes.
    /// </summary>
    internal void ApplyMiniMapPeekHotkey()
    {
        if (_hotkeyMgr == null) return;

        // If widget overlay is not open, disable/unregister the hotkey
        bool overlayOpen = _deviceOverlay != null && _deviceOverlay.IsLoaded;
        if (!overlayOpen)
        {
            if (_peekGesture != null)
            {
                _hotkeyMgr.Unregister(_peekGesture);
                _peekGesture = null;
            }
            if (_peeking) EndMiniMapPeek();
            return;
        }

        var wanted = TrackingService.MiniMapPeekHotkey?.Trim();
        if (string.IsNullOrEmpty(wanted)) wanted = null;

        if (_peekGesture != null &&
            !string.Equals(_peekGesture, wanted, StringComparison.OrdinalIgnoreCase))
            _hotkeyMgr.Unregister(_peekGesture);

        _peekGesture = wanted;

        if (_peekGesture != null) _hotkeyMgr.Register(_peekGesture);
    }

    /// <summary>Whether this gesture was the map's. Checked before the device bindings.</summary>
    private bool HandledByMiniMapPeek(string gesture)
    {
        if (_peekGesture == null ||
            !string.Equals(gesture, _peekGesture, StringComparison.OrdinalIgnoreCase))
            return false;

        BeginMiniMapPeek();
        return true;
    }

    /// <summary>
    /// Shows the dock for as long as the key is held.
    ///
    /// Windows only tells us the key went down via RegisterHotKey, so the release is tracked
    /// using a low-level keyboard hook active only while peeking, plus a backup GetAsyncKeyState
    /// poll. Polling and hook are only active while the key is physically held.
    /// </summary>
    private void BeginMiniMapPeek()
    {
        // When the widget overlay or the minimap is not open, hotkey must not bring them back
        if (_deviceOverlay == null || !_deviceOverlay.IsLoaded || _miniMap == null || !_miniMap.IsLoaded || _peeking)
            return;

        // The key is auto-repeating into this while it is held, so a hold that has already
        // run out of time must not simply start again.
        if (_peekSpent) return;

        var key = KeyOfGesture(_peekGesture);
        if (key == Key.None) return;

        uint vk;
        try
        {
            vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        }
        catch
        {
            return;
        }

        _peeking = true;
        _consecutiveUpTicks = 0;
        _asyncKeySawDown = false;
        _peekStartedUtc = DateTime.UtcNow;
        _peekHookVk = vk;

        _miniMap.ShowForPeek();

        // Install temporary low-level hook to catch key release with zero latency across all windows
        InstallPeekHook();

        // Always create a clean, fresh timer so no stale pending ticks can cause instant hide
        StopPeekTimer();

        _peekReleaseTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(40),
        };
        _peekReleaseTimer.Tick += OnPeekTick;
        _peekReleaseTimer.Start();
    }

    private void InstallPeekHook()
    {
        if (_peekHookId != IntPtr.Zero) return;

        try
        {
            _peekHookProc = PeekHookCallback;
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            IntPtr modHandle = GetModuleHandle(curModule?.ModuleName);

            _peekHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _peekHookProc, modHandle, 0);
        }
        catch
        {
            // Fall back to GetAsyncKeyState timer if hook fails
        }
    }

    private void UninstallPeekHook()
    {
        if (_peekHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_peekHookId);
            _peekHookId = IntPtr.Zero;
            _peekHookProc = null;
        }
    }

    private IntPtr PeekHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
            {
                var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if (kbd.vkCode == _peekHookVk)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_peeking)
                        {
                            EndMiniMapPeek();
                        }
                        else
                        {
                            _peekSpent = false;
                            StopPeekTimer();
                            UninstallPeekHook();
                        }
                    }));
                }
            }
        }
        return CallNextHookEx(_peekHookId, nCode, wParam, lParam);
    }

    private void OnPeekTick(object? sender, EventArgs e)
    {
        bool down = PeekKeyStillDown();

        // Still down with nothing showing: the limit has already run out on this hold, and
        // the watch is only here to see it released.
        if (!_peeking)
        {
            if (!down)
            {
                _peekSpent = false;
                StopPeekTimer();
                UninstallPeekHook();
            }
            return;
        }

        // 20-second safety limit reached: hide map, keep timer/hook running until key is released
        if (DateTime.UtcNow - _peekStartedUtc >= PeekLimit)
        {
            _peekSpent = true;
            EndMiniMapPeek(keepWatching: true);
            return;
        }

        if (down)
        {
            _asyncKeySawDown = true;
            _consecutiveUpTicks = 0;
            return;
        }

        // Only trust GetAsyncKeyState for key-up if it was previously able to confirm the key down.
        // If it was never detected down (e.g. game is running elevated with EAC / UIPI),
        // we do NOT kill the peek prematurely; the WH_KEYBOARD_LL hook handles the release.
        if (_asyncKeySawDown)
        {
            _consecutiveUpTicks++;

            // Require 3 consecutive ticks (~120ms) of key-up to prevent key bounce from prematurely killing the peek.
            if (_consecutiveUpTicks >= 3)
            {
                _peekSpent = false;
                EndMiniMapPeek();
            }
        }
    }

    /// <param name="keepWatching">
    /// Leaves the timer/hook running so the key coming up is still noticed. Used when the limit
    /// ran out while the key is held: without it nothing would ever clear the spent flag and
    /// the key would be dead until the next restart.
    /// </param>
    private void EndMiniMapPeek(bool keepWatching = false)
    {
        _peeking = false;
        _consecutiveUpTicks = 0;
        _asyncKeySawDown = false;

        if (!keepWatching)
        {
            _peekSpent = false;
            StopPeekTimer();
            UninstallPeekHook();
        }

        _miniMap?.HideAfterPeek();
    }

    private void StopPeekTimer()
    {
        if (_peekReleaseTimer != null)
        {
            _peekReleaseTimer.Stop();
            _peekReleaseTimer.Tick -= OnPeekTick;
            _peekReleaseTimer = null;
        }
    }

    /// <summary>
    /// Whether the gesture's own key is still down. The modifiers are deliberately not checked:
    /// letting go of Shift while still holding the key is not letting go of the key.
    /// </summary>
    private bool PeekKeyStillDown()
    {
        var key = KeyOfGesture(_peekGesture);
        if (key == Key.None) return false;

        try
        {
            int vk = KeyInterop.VirtualKeyFromKey(key);
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }
        catch
        {
            // Nothing to hold on to; better to put the map away than to leave it up.
            return false;
        }
    }

    /// <summary>The key at the end of a gesture such as "Ctrl+Shift+M".</summary>
    private static Key KeyOfGesture(string? gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return Key.None;

        var last = gesture.Split('+')[^1].Trim();
        return Enum.TryParse<Key>(last, ignoreCase: true, out var key) ? key : Key.None;
    }
}
