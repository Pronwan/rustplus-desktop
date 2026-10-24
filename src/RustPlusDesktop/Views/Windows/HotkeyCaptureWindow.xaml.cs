using System;
using System.Windows;
using System.Windows.Input;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views.Windows
{
    public partial class HotkeyCaptureWindow : Wpf.Ui.Controls.FluentWindow
    {
        public string? Gesture { get; private set; }

        /// <summary>
        /// Lets Escape remove the hotkey instead of cancelling.
        ///
        /// Off by default: for the device hotkeys this window is one step of a longer
        /// assignment, where Escape has always meant never mind. Where a single hotkey is
        /// being set, though, there is no other way to get rid of one — so the caller asks
        /// for it, and the footer says which of the two this window is doing.
        /// </summary>
        public bool AllowClear { get; set; }

        public HotkeyCaptureWindow()
        {
            InitializeComponent();
            PreviewKeyDown += OnPreviewKeyDown;

            Loaded += (_, __) =>
            {
                if (AllowClear)
                {
                    TxtFooter.Text = Helpers.Loc.Text(
                        "UiPressESCToClearEnterToSave",
                        "ESC removes the hotkey · Enter saves · close this window to cancel");
                }
            };
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.System) return;
            var key = (e.Key == Key.ImeProcessed) ? e.ImeProcessedKey : e.Key;
            
            if (key == Key.Escape)
            {
                // Applied as an empty gesture rather than refused: having no hotkey is a
                // real choice, and the caller stores it the way it stores any other.
                if (AllowClear)
                {
                    Gesture = "";
                    DialogResult = true;
                }
                else
                {
                    DialogResult = false;
                }

                Close();
                e.Handled = true;
                return;
            }

            // Skip modifier-only keypresses during processing
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }

            // Save on Enter/Return only if a gesture was set
            if (key == Key.Enter || key == Key.Return)
            {
                if (!string.IsNullOrEmpty(Gesture))
                {
                    DialogResult = true;
                    Close();
                }
                e.Handled = true;
                return;
            }

            bool ctrl = (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl));
            bool alt = (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt));
            bool shift = (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift));
            bool win = (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin));

            Gesture = GlobalHotkeyManager.Format(key, ctrl, alt, shift, win);
            TxtGesture.Text = Gesture;
            e.Handled = true;
        }
    }
}
