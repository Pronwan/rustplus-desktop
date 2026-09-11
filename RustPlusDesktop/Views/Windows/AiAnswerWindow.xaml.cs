using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using RustPlusDesk.Helpers;
using RustPlusDesk.Services.AiCompanion;

namespace RustPlusDesk.Views.Windows
{
    /// <summary>
    /// The answer, on its own small window docked under the AI tile.
    ///
    /// A window rather than another row in the dock, because an answer is any length at all and
    /// the dock is a grid of fixed cells — growing the dock to fit a paragraph would push every
    /// tile under it off the screen. This follows the tile instead, and goes away when dismissed.
    ///
    /// It never takes focus. The player is in a full-screen game; a window that activates itself
    /// would minimise Rust to show them a sentence.
    /// </summary>
    public partial class AiAnswerWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private readonly AiCompanionService _service = AiCompanionService.Instance;

        public AiAnswerWindow()
        {
            InitializeComponent();

            _service.Changed += OnServiceChanged;
            Closed += (_, __) => _service.Changed -= OnServiceChanged;

            Render();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).Handle;
            var styles = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();

            // NOACTIVATE keeps the game in front; TOOLWINDOW keeps this out of alt-tab, where a
            // sentence-sized window is only noise.
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)(styles | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        }

        private void OnServiceChanged()
        {
            // Deltas arrive on whichever thread the provider's stream is being read on.
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(Render);
                return;
            }

            Render();
        }

        private void Render()
        {
            switch (_service.State)
            {
                case AiAnswerState.Sending:
                    TxtState.Text = Loc.Text("AiAnswerThinking", "Thinking…");
                    TxtState.Foreground = Brush("Accent", Colors.SkyBlue);
                    TxtAnswer.Text = Loc.Text("AiAnswerWaiting", "Sent. Waiting for the first words…");
                    TxtAnswer.Foreground = Brush("TextSubtle", Colors.Gray);
                    break;

                case AiAnswerState.Streaming:
                case AiAnswerState.Answered:
                    TxtState.Text = _service.State == AiAnswerState.Streaming
                        ? Loc.Text("AiAnswerWriting", "Answering…")
                        : Loc.Text("AiAnswerDone", "Answer");
                    TxtState.Foreground = Brush("Accent", Colors.SkyBlue);
                    TxtAnswer.Text = _service.Answer;
                    TxtAnswer.Foreground = Brush("TextPrimary", Colors.White);
                    break;

                case AiAnswerState.Failed:
                    TxtState.Text = Loc.Text("AiAnswerFailed", "Failed");
                    TxtState.Foreground = Brush("DangerBrush", Color.FromRgb(0xE5, 0x39, 0x35));
                    TxtAnswer.Text = _service.Error ?? "";
                    TxtAnswer.Foreground = Brush("TextPrimary", Colors.White);
                    break;

                default:
                    Hide();
                    return;
            }

            TxtProvider.Text = AiProviders.DisplayName(AiCompanionStore.Current.Provider);

            // Copying half an answer is not useful, and a button that does nothing is worse than
            // one that is plainly not ready yet.
            BtnCopy.Visibility = _service.State is AiAnswerState.Answered or AiAnswerState.Failed
                ? Visibility.Visible
                : Visibility.Collapsed;

            // Follows the text down while it is being written, so the newest line is the one in
            // view without the player having to reach for a scrollbar mid-game.
            if (_service.State == AiAnswerState.Streaming) Scroller.ScrollToEnd();
        }

        private static Brush Brush(string key, Color fallback)
        {
            if (Application.Current?.TryFindResource(key) is Brush found) return found;
            return new SolidColorBrush(fallback);
        }

        /// <summary>
        /// Puts the window directly under the tile, and keeps it on that tile's own screen.
        ///
        /// Both rectangles are in the same device-independent units as <see cref="Window.Left"/>,
        /// because the caller already knows which monitor the dock is on and working it out
        /// twice is how a panel ends up on the neighbouring screen.
        /// </summary>
        public void DockUnder(Rect tile, Rect screen)
        {
            const double Gap = 6;

            Width = Math.Max(300, tile.Width);

            // SizeToContent only settles after a layout pass, and the height decides whether
            // there is room below the tile at all.
            UpdateLayout();
            double height = ActualHeight > 0 ? ActualHeight : 120;

            double top = tile.Bottom + Gap;

            // Above the tile instead when there is no room below it — a dock along the bottom
            // edge of the screen is a normal place to put one.
            if (top + height > screen.Bottom)
            {
                double above = tile.Top - Gap - height;
                top = above >= screen.Top ? above : Math.Max(screen.Top, screen.Bottom - height);
            }

            Left = Math.Clamp(tile.Left, screen.Left, Math.Max(screen.Left, screen.Right - Width));
            Top = top;
        }

        private void BtnCopy_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            try { Clipboard.SetText(TxtAnswer.Text); }
            catch { /* another process holds the clipboard; nothing here is worth a dialog */ }
        }

        private void BtnClose_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            _service.ClearAnswer();
            Hide();
        }
    }
}
