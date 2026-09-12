using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;
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

        /// <summary>
        /// A preview shows the settings, not an answer.
        ///
        /// Without it the preview would be wiped by the first thing the service did, and the
        /// only way to see what the colour and size settings look like would be to ask a
        /// question and hope it was long enough.
        /// </summary>
        private bool _preview;

        public AiAnswerWindow()
        {
            InitializeComponent();

            _service.Changed += OnServiceChanged;
            Closed += (_, __) => _service.Changed -= OnServiceChanged;

            ApplyAppearance();
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

        /// <summary>
        /// Colour, transparency and size, from the companion's settings.
        ///
        /// Only the panel's own surface fades, never its text — that is the same rule the dock's
        /// tiles follow, and the reason a panel turned nearly invisible is still readable over
        /// whatever the game is drawing behind it.
        /// </summary>
        public void ApplyAppearance()
        {
            var settings = AiCompanionStore.Current;

            // All the way to nothing. At zero only the text and the two buttons are left,
            // floating over the game — which is the point, and the same thing the dock's
            // tiles already allow.
            double opacity = Math.Clamp(settings.AnswerOpacity, 0.0, 1.0);

            Shell.Background = Fade(Resource("Surface", Color.FromArgb(0xD8, 0x16, 0x1B, 0x22)), opacity);
            Shell.BorderBrush = Fade(Resource("CardBorder", Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), opacity);

            Width = Math.Clamp(settings.AnswerWidth, 220, 900);
            Scroller.MaxHeight = Math.Clamp(settings.AnswerHeight, 80, 800);

            var (main, sub) = MiniMapWindow.TextBrushes(settings.AnswerTextColorKey ?? CommandDockTextColors.Auto);
            TxtAnswer.Foreground = main;
            TxtProvider.Foreground = sub;

            // A panel that can be seen through needs the same shadow the tiles use, for the same
            // reason: white on snow and black on water are both invisible without it.
            TxtAnswer.Effect = opacity < 0.85 ? TextShadow : null;
            TxtState.Effect = TxtAnswer.Effect;
            TxtProvider.Effect = TxtAnswer.Effect;
        }

        private static readonly System.Windows.Media.Effects.Effect TextShadow = CreateShadow();

        private static System.Windows.Media.Effects.Effect CreateShadow()
        {
            var shadow = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 4,
                ShadowDepth = 1,
                Direction = 270,
                Opacity = 0.85,
                Color = Colors.Black,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance,
            };
            shadow.Freeze();
            return shadow;
        }

        private static Brush Fade(Brush brush, double opacity)
        {
            if (brush is not SolidColorBrush solid) return brush;

            var color = solid.Color;
            var faded = new SolidColorBrush(Color.FromArgb(
                (byte)Math.Round(color.A * opacity), color.R, color.G, color.B));
            faded.Freeze();
            return faded;
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
            if (_preview) return;

            switch (_service.State)
            {
                case AiAnswerState.Sending:
                    TxtState.Text = Loc.Text("AiAnswerThinking", "Thinking…");
                    TxtState.Foreground = Resource("Accent", Colors.SkyBlue);
                    TxtAnswer.Text = Loc.Text("AiAnswerWaiting", "Sent. Waiting for the first words…");
                    break;

                case AiAnswerState.Streaming:
                case AiAnswerState.Answered:
                    TxtState.Text = _service.State == AiAnswerState.Streaming
                        ? Loc.Text("AiAnswerWriting", "Answering…")
                        : Loc.Text("AiAnswerDone", "Answer");
                    TxtState.Foreground = Resource("Accent", Colors.SkyBlue);
                    TxtAnswer.Text = _service.Answer;
                    break;

                case AiAnswerState.Failed:
                    TxtState.Text = Loc.Text("AiAnswerFailed", "Failed");
                    TxtState.Foreground = Resource("DangerBrush", Color.FromRgb(0xE5, 0x39, 0x35));
                    TxtAnswer.Text = _service.Error ?? "";
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

        private static Brush Resource(string key, Color fallback)
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

        /// <summary>
        /// Opens a sample panel so the colour, transparency and size settings can be judged
        /// without asking a question first.
        ///
        /// It is the real window with real settings applied, not a mock-up — a preview that
        /// renders differently from the thing it previews is worse than none.
        /// </summary>
        public static void ShowPreview(Window? owner)
        {
            var preview = new AiAnswerWindow { _preview = true, ShowActivated = false };

            preview.TxtState.Text = Loc.Text("AiAnswerPreviewLabel", "Preview");
            preview.TxtState.Foreground = Resource("Accent", Colors.SkyBlue);
            preview.TxtProvider.Text = AiProviders.DisplayName(AiCompanionStore.Current.Provider);
            preview.TxtAnswer.Text = Loc.Text("AiAnswerPreviewText",
                "Sulfur first — a rocket needs 1400, and you are short of that before you are " +
                "short of anything else. The crate on the screenshot is a normal military crate, " +
                "so it will not have one in it.\n\nClose this preview when the panel looks right.");
            preview.BtnCopy.Visibility = Visibility.Collapsed;

            // Beside its owner rather than under a tile: the settings window is what is being
            // looked at, and the dock may not even be open.
            preview.Show();
            preview.UpdateLayout();

            if (owner != null && !double.IsNaN(owner.Left))
            {
                preview.Left = owner.Left + Math.Max(0, (owner.ActualWidth - preview.Width) / 2);
                preview.Top = owner.Top + Math.Max(0, (owner.ActualHeight - preview.ActualHeight) / 2);
            }
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

            if (_preview)
            {
                Close();
                return;
            }

            _service.ClearAnswer();
            Hide();
        }
    }
}
