using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace RustPlusDesk
{
    /// <summary>
    /// Keeps the dock out of the foreground.
    ///
    /// Pressing a widget used to pull the game out of focus, because the dock is an ordinary
    /// window and clicking one activates it. NOACTIVATE changes that: the window still receives
    /// the mouse, it simply never becomes the active window, so a switch flips and a timer is
    /// read with the game still taking the keyboard.
    ///
    /// The cost is that the window can never take keyboard focus either, and a WPF TextBox in a
    /// window that is not active cannot be typed into. So the style comes off for exactly as long
    /// as something needs typing - the Discord line, the translate box, and nothing else - and
    /// goes straight back on afterwards. Everything else on the dock is a button, a switch or a
    /// drag, none of which need focus at all; taking it was only ever a side effect.
    /// </summary>
    public partial class MiniMapWindow
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>How many things currently want the dock to be able to take the keyboard.</summary>
        private int _focusHolders;

        private void ApplyNoActivate(bool on)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            var styles = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();

            long next = on
                ? styles | WS_EX_NOACTIVATE
                : styles & ~(long)WS_EX_NOACTIVATE;

            if (next == styles) return;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)next);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            InitNoActivate();
        }

        private void InitNoActivate()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            var styles = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();

            // TOOLWINDOW as well, so the dock is not an alt-tab entry. It never was a place to
            // switch to, and with NOACTIVATE it cannot even be switched to.
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)(styles | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        }

        /// <summary>
        /// Lets the dock take the keyboard while a text box has it, and gives it back after.
        ///
        /// Counted rather than a flag: two text boxes on the same dock can be entered one after
        /// the other, and the first one losing focus must not take the keyboard away from the
        /// second one that just got it.
        /// </summary>
        internal void HoldKeyboardFocus(bool hold)
        {
            _focusHolders = Math.Max(0, _focusHolders + (hold ? 1 : -1));

            if (_focusHolders > 0)
            {
                ApplyNoActivate(false);
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero) SetForegroundWindow(hwnd);
            }
            else
            {
                ApplyNoActivate(true);
            }
        }

        /// <summary>Whether the edit mode is currently one of the things holding focus open.</summary>
        private bool _editFocusHeld;

        /// <summary>
        /// Unlocking the dock puts both windows into the foreground; locking gives it back.
        ///
        /// Arranging is a mode the user entered on purpose, and while they are in it the app is
        /// what they are looking at - so it behaves like an ordinary window and responds like
        /// one. Locked, both go back to never taking focus, which is what makes a widget usable
        /// mid-game without pulling the game out from under it.
        ///
        /// Idempotent, because it is called from ApplyLockState, which also runs on load: going
        /// through the same counter as the text boxes means an unbalanced call would leave the
        /// dock permanently activatable.
        /// </summary>
        internal void SetEditModeFocus(bool editing)
        {
            _overlay?.SetEditable(editing);

            if (editing == _editFocusHeld) return;
            _editFocusHeld = editing;

            HoldKeyboardFocus(editing);
        }

        /// <summary>
        /// Wires a text box so the dock can be typed into while it has focus.
        ///
        /// Called by the tiles that own one - the Discord line and the translate box. Anything
        /// that does not call this stays unable to take focus, which is the point.
        /// </summary>
        internal void AllowTypingIn(TextBox box)
        {
            box.GotKeyboardFocus += (_, __) => HoldKeyboardFocus(true);
            box.LostKeyboardFocus += (_, __) => HoldKeyboardFocus(false);

            // A NOACTIVATE window never gets focus from a click on its own, so the press has to
            // ask for it. Preview, because the TextBox's own handler runs after and would
            // otherwise try to place a caret in a window that cannot have one yet.
            box.PreviewMouseLeftButtonDown += (_, __) =>
            {
                if (box.IsKeyboardFocusWithin) return;

                HoldKeyboardFocus(true);
                box.Focus();

                // Balanced immediately: from here the GotKeyboardFocus above is what holds it,
                // and this one was only needed to make that focus possible at all.
                HoldKeyboardFocus(false);
            };
        }
    }
}
