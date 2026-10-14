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

        // The key is auto-repeating into this while it is held, so a hold that has already
        // run out of time must not simply start again.
        if (_peekSpent) return;

        _peeking = true;
        _peekStartedUtc = DateTime.UtcNow;
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
        bool down = PeekKeyStillDown();

        // The key coming up is checked before anything else, and that is the whole fix for a
        // peek key that worked once and then never again. This used to begin by returning
        // when nothing was on screen — which is exactly the state the watch is left in after
        // the time limit runs out — so the flag saying "this hold is spent" was never
        // cleared, and every later press was refused.
        if (!down)
        {
            _peekSpent = false;

            if (_peeking) EndMiniMapPeek();
            else _peekReleaseTimer?.Stop();

            return;
        }

        // Still down with nothing showing: the limit has already run out on this hold, and
        // the watch is only here to see it released.
        if (!_peeking) return;

        if (DateTime.UtcNow - _peekStartedUtc < PeekLimit) return;

        _peekSpent = true;
        EndMiniMapPeek(keepWatching: true);
    }

    /// <param name="keepWatching">
    /// Leaves the timer running so the key coming up is still noticed. Used when the limit
    /// ran out while the key is held: without it nothing would ever clear the spent flag and
    /// the key would be dead until the next restart.
    /// </param>
    private void EndMiniMapPeek(bool keepWatching = false)
    {
        _peeking = false;
        if (!keepWatching) _peekReleaseTimer?.Stop();

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
