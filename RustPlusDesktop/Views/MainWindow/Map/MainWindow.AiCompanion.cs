using System;
using RustPlusDesk.Services.AiCompanion;

namespace RustPlusDesk.Views;

/// <summary>
/// Push to talk for the AI companion.
///
/// The gesture lives here rather than in the dock because the hotkey manager is owned by this
/// window — and because it must keep working while Rust has the foreground, which is the whole
/// reason the companion is worth having.
/// </summary>
public partial class MainWindow
{
    /// <summary>The gesture currently registered, so a changed one can replace the old.</summary>
    private string? _aiHotkeyGesture;

    /// <summary>
    /// Registers the configured push-to-talk gesture, replacing whatever was registered before.
    ///
    /// Called after every sweep of the device hotkeys as well as on a settings change: those
    /// unregister everything when the server changes or when hotkeys are switched off entirely,
    /// and push to talk is not part of that switch — it belongs to the companion, not to a
    /// server's device bindings.
    /// </summary>
    internal void ApplyAiHotkey()
    {
        if (_hotkeyMgr == null) return;

        var wanted = AiCompanionStore.Current.Hotkey?.Trim();
        if (string.IsNullOrEmpty(wanted)) wanted = null;

        // A gesture that is being replaced or cleared has to be given back, or the old one
        // keeps firing and Windows keeps refusing it to whatever else wants it.
        if (_aiHotkeyGesture != null &&
            !string.Equals(_aiHotkeyGesture, wanted, StringComparison.OrdinalIgnoreCase))
            _hotkeyMgr.Unregister(_aiHotkeyGesture);

        _aiHotkeyGesture = wanted;

        if (_aiHotkeyGesture != null) _hotkeyMgr.Register(_aiHotkeyGesture);
    }

    /// <summary>
    /// Whether this gesture was the companion's. Checked before the device bindings, so a
    /// gesture used for both records rather than toggling a switch — the recording is the one
    /// the user pressed it for while looking at the game.
    /// </summary>
    private bool HandledByAiCompanion(string gesture)
    {
        if (_aiHotkeyGesture == null ||
            !string.Equals(gesture, _aiHotkeyGesture, StringComparison.OrdinalIgnoreCase))
            return false;

        // Nothing to record into without the dock open. Silently, because a hotkey that pops up
        // a dialog over a full-screen game is worse than one that does nothing.
        _miniMap?.ToggleAiRecording();
        return true;
    }
}
