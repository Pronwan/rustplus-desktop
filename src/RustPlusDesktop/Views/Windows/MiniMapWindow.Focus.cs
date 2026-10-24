using System.Windows.Controls;

namespace RustPlusDesk;

public partial class MiniMapWindow
{
    // A regular map window uses normal Windows keyboard focus.
    internal virtual void HoldKeyboardFocus(bool hold) { }
    private bool _editFocusHeld;

    internal void SetEditModeFocus(bool editing)
    {
        _overlay?.SetEditable(editing);
        if (editing == _editFocusHeld) return;
        _editFocusHeld = editing;
        HoldKeyboardFocus(editing);
    }

    internal void AllowTypingIn(TextBox box)
    {
        box.GotKeyboardFocus += (_, __) => HoldKeyboardFocus(true);
        box.LostKeyboardFocus += (_, __) => HoldKeyboardFocus(false);
        box.PreviewMouseLeftButtonDown += (_, __) =>
        {
            if (box.IsKeyboardFocusWithin) return;
            HoldKeyboardFocus(true);
            box.Focus();
            HoldKeyboardFocus(false);
        };
    }
}
