using System;
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
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    /// <summary>The gesture currently registered for this, so it can be given back.</summary>
    private string? _peekGesture;

    private DispatcherTimer? _peekReleaseTimer;
    private bool _peeking;

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
    /// Windows only tells us the key went down — RegisterHotKey has no matching message for it
    /// coming back up — so the release is found by asking. Polling at this rate costs nothing
    /// measurable and is only running while a key is actually held.
    /// </summary>
    private void BeginMiniMapPeek()
    {
        if (_miniMap == null || _peeking) return;

        _peeking = true;
        _miniMap.ShowForPeek();

        _peekReleaseTimer ??= new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(40),
        };

        _peekReleaseTimer.Tick -= OnPeekTick;
        _peekReleaseTimer.Tick += OnPeekTick;
        _peekReleaseTimer.Start();
    }

    private void OnPeekTick(object? sender, EventArgs e)
    {
        if (!_peeking || PeekKeyStillDown()) return;

        EndMiniMapPeek();
    }

    private void EndMiniMapPeek()
    {
        _peeking = false;
        _peekReleaseTimer?.Stop();

        _miniMap?.HideAfterPeek();
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
