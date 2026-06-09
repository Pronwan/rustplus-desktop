using System;
using System.Windows;
using System.Windows.Input;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views.Windows
{
    public partial class HotkeyCaptureWindow : Window
    {
        public string? Gesture { get; private set; }

        public HotkeyCaptureWindow()
        {
            InitializeComponent();
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.System) return;
            var key = (e.Key == Key.ImeProcessed) ? e.ImeProcessedKey : e.Key;
            
            // Allow closing/cancelling via Escape
            if (key == Key.Escape)
            {
                DialogResult = false;
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

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}
