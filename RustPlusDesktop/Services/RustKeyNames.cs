using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace RustPlusDesk.Services;

/// <summary>
/// Translates a key press into the name Rust's bind command expects. Windows and Rust disagree
/// on nearly every special key - NumPad1 is "kp1", D1 is "1", OemPlus is "=" - so a captured key
/// cannot be written into a bind line as-is.
/// </summary>
public static class RustKeyNames
{
    /// <summary>
    /// Mouse buttons and the scroll wheel can never arrive through a keyboard event, so they are
    /// offered as a list instead. The names are Rust's own.
    /// </summary>
    public static readonly IReadOnlyList<string> MouseAndWheel = new[]
    {
        "mouse1", "mouse2", "mouse3", "mouse4", "mouse5",
        "mousewheelup", "mousewheeldown",
    };

    private static readonly Dictionary<Key, string> Map = new()
    {
        // Numpad - the single most common source of a wrong bind, because Windows and Rust
        // use completely different names for the same keys.
        [Key.NumPad0] = "kp0", [Key.NumPad1] = "kp1", [Key.NumPad2] = "kp2",
        [Key.NumPad3] = "kp3", [Key.NumPad4] = "kp4", [Key.NumPad5] = "kp5",
        [Key.NumPad6] = "kp6", [Key.NumPad7] = "kp7", [Key.NumPad8] = "kp8",
        [Key.NumPad9] = "kp9",
        [Key.Divide] = "kpdivide", [Key.Multiply] = "kpmultiply",
        [Key.Subtract] = "kpminus", [Key.Add] = "kpplus", [Key.Decimal] = "kpperiod",

        // Number row
        [Key.D0] = "0", [Key.D1] = "1", [Key.D2] = "2", [Key.D3] = "3", [Key.D4] = "4",
        [Key.D5] = "5", [Key.D6] = "6", [Key.D7] = "7", [Key.D8] = "8", [Key.D9] = "9",

        // Named keys
        [Key.Space] = "space",
        [Key.Enter] = "return",
        [Key.Tab] = "tab",
        [Key.Back] = "backspace",
        [Key.Escape] = "escape",
        [Key.CapsLock] = "capslock",
        [Key.Insert] = "insert",
        [Key.Delete] = "delete",
        [Key.Home] = "home",
        [Key.End] = "end",
        [Key.PageUp] = "pageup",
        [Key.PageDown] = "pagedown",
        [Key.Up] = "uparrow",
        [Key.Down] = "downarrow",
        [Key.Left] = "leftarrow",
        [Key.Right] = "rightarrow",

        // Modifiers as standalone keys
        [Key.LeftShift] = "leftshift", [Key.RightShift] = "rightshift",
        [Key.LeftCtrl] = "leftcontrol", [Key.RightCtrl] = "rightcontrol",
        [Key.LeftAlt] = "leftalt", [Key.RightAlt] = "rightalt",

        // Punctuation, where the WPF name says nothing about the printed character
        [Key.OemPlus] = "=", [Key.OemMinus] = "-",
        [Key.OemComma] = ",", [Key.OemPeriod] = ".",
        [Key.OemQuestion] = "/", [Key.OemTilde] = "`",
        [Key.OemOpenBrackets] = "[", [Key.OemCloseBrackets] = "]",
        [Key.OemPipe] = "\\", [Key.OemSemicolon] = ";", [Key.OemQuotes] = "'",
    };

    /// <summary>
    /// Keys we refuse to capture. Escape cancels the capture, and the Windows key never reaches
    /// the game anyway, so binding it would produce a line that silently does nothing.
    /// </summary>
    public static bool IsRejected(Key key)
        => key is Key.LWin or Key.RWin or Key.Apps or Key.System or Key.None;

    /// <summary>
    /// Rust's own name for a key, or null if we have no confident mapping. Returning null is
    /// deliberate - guessing a name produces a bind that looks right and does nothing.
    /// </summary>
    public static string? ToRustName(Key key)
    {
        if (IsRejected(key)) return null;
        if (Map.TryGetValue(key, out var mapped)) return mapped;

        // A-Z and F1-F24 share their WPF name with Rust's, just lower case.
        var name = key.ToString();
        if (name.Length == 1 && char.IsLetter(name[0])) return name.ToLowerInvariant();
        if (name.Length >= 2 && name[0] == 'F' && int.TryParse(name.AsSpan(1), out _)) return name.ToLowerInvariant();

        return null;
    }

    /// <summary>
    /// Adds the modifier prefix Rust uses for combinations, e.g. "[leftshift+f11]". Only applied
    /// when the pressed key is not itself the modifier.
    /// </summary>
    public static string? ToRustBind(Key key, ModifierKeys modifiers)
    {
        var baseName = ToRustName(key);
        if (baseName == null) return null;

        var isModifierItself = key is Key.LeftShift or Key.RightShift
            or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt;
        if (isModifierItself || modifiers == ModifierKeys.None) return baseName;

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("leftcontrol");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("leftshift");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("leftalt");
        if (parts.Count == 0) return baseName;

        parts.Add(baseName);
        return "[" + string.Join("+", parts) + "]";
    }
}
