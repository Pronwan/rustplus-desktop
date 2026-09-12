using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;
using RustPlusDesk.Services.AiCompanion;

namespace RustPlusDesk
{
    /// <summary>
    /// Ask the companion a question without leaving the game: press to record, press to stop,
    /// press send.
    ///
    /// This is the one tile that has to be usable while Rust has the screen, so every action on
    /// it is a single press and every state it can be in is readable at a glance — a glance is
    /// all it will get.
    /// </summary>
    public partial class MiniMapWindow
    {
        private const string GlyphMicrophone = "\uE720";
        private const string GlyphStop = "\uE71A";
        private const string GlyphCamera = "\uE722";
        private const string GlyphDelete = "\uE74D";
        private const string GlyphVolume = "\uE767";
        private const string GlyphSend = "\uE724";

        private FrameworkElement BuildAiTile(CommandDockTile tile)
        {
            var style = StyleFor(tile);
            var shell = TileShell(style);
            shell.Tag = tile;

            var root = new Grid();
            shell.Child = root;

            var row = new Grid { VerticalAlignment = VerticalAlignment.Center };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.Children.Add(row);

            // The app's own mark, so the tile is recognisable among a dock of glyphs.
            var logo = new Image
            {
                Width = style.Size(20),
                Height = style.Size(20),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            try { logo.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/icons/app-icons/logo-dark-mode.png")); }
            catch { /* the tile works without it */ }
            Grid.SetColumn(logo, 0);
            row.Children.Add(logo);

            var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            lines.Children.Add(new TextBlock
            {
                Text = Loc.Text("CommandDockAiTitle", "Ask AI"),
                FontSize = style.Size(12),
                FontWeight = FontWeights.SemiBold,
                Foreground = style.TextMain,
                Effect = style.TextShadow,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            var status = new TextBlock
            {
                FontSize = style.Size(10),
                Foreground = style.TextSub,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Effect = style.TextShadow,
            };
            lines.Children.Add(status);
            Grid.SetColumn(lines, 1);
            row.Children.Add(lines);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(buttons, 2);
            row.Children.Add(buttons);

            var record = IconButton(GlyphMicrophone, style, Loc.Text("CommandDockAiRecord", "Record a question"));
            var send = IconButton(GlyphSend, style, Loc.Text("CommandDockAiSend", "Send the question"));
            var camera = IconButton(GlyphCamera, style, Loc.Text("CommandDockAiScreenshot", "Attach a screenshot"));
            var sound = IconButton(GlyphVolume, style, Loc.Text("CommandDockAiGameAudio", "Also record the game's sound"));
            var discard = IconButton(GlyphDelete, style, Loc.Text("CommandDockAiDiscard", "Discard"));

            buttons.Children.Add(record);
            buttons.Children.Add(send);
            buttons.Children.Add(camera);
            buttons.Children.Add(sound);
            buttons.Children.Add(discard);

            // The tile swallows presses so the window does not drag itself; Borders are not
            // controls, so each one has to be exempted by hand.
            KeepPresses(record, send, camera, sound, discard);

            // The countdown and the flash cover the whole tile and are only ever seen during a
            // capture.
            var countdown = new TextBlock
            {
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Effect = SharedTextShadow,
                IsHitTestVisible = false,
            };
            var flash = new Border
            {
                CornerRadius = shell.CornerRadius,
                Background = Brushes.White,
                Opacity = 0,
                IsHitTestVisible = false,
            };
            root.Children.Add(flash);
            root.Children.Add(countdown);

            // Kept on the window: the push-to-talk hotkey can start a capture with auto-send
            // on, and it has no tile in hand to find these from.
            _aiCountdown = countdown;
            _aiFlash = flash;

            var recorder = AiRecorder.Instance;
            var service = AiCompanionService.Instance;

            record.MouseLeftButtonUp += (_, e) => { e.Handled = true; ToggleAiRecording(); };

            send.MouseLeftButtonUp += (_, e) => { e.Handled = true; _ = SendAiQuestion(); };

            camera.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _ = CaptureAiScreenshot();
            };

            sound.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;

                var settings = AiCompanionStore.Current;
                settings.CaptureGameAudio = !settings.CaptureGameAudio;
                AiCompanionStore.Save(settings);
                RefreshTiles();
            };

            discard.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                DiscardAiQuestion();
            };

            var pulse = new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(650))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            };
            bool pulsing = false;

            _tileRefreshers.Add(() =>
            {
                bool ready = AiCompanionStore.HasKey;
                bool recording = recorder.State == AiRecorderState.Recording;
                bool hasScreenshot = service.Screenshot != null;
                bool sendable = !service.IsBusy &&
                                (recorder.State == AiRecorderState.Ready || hasScreenshot);

                shell.Opacity = ready ? 1.0 : 0.45;
                ToolTipService.SetToolTip(shell, ready
                    ? Loc.Text("CommandDockAiHint", "Press to record your question, press again to stop.")
                    : Loc.Text("CommandDockAiNoKey",
                        "No AI key stored yet. Add one under Connected Services first."));

                Glyph(record, recording ? GlyphStop : GlyphMicrophone);
                ToolTipService.SetToolTip(record, recording
                    ? Loc.Text("CommandDockAiStop", "Stop recording")
                    : Loc.Text("CommandDockAiRecord", "Record a question"));

                // Send appears only when there is something to send, so the row of buttons says
                // what the tile is waiting for rather than offering five equal choices.
                send.Visibility = sendable ? Visibility.Visible : Visibility.Collapsed;

                // The game-audio button carries its own state: lit when on, faded when off, so
                // the tile says what the next recording will contain without opening settings.
                sound.Opacity = AiCompanionStore.Current.CaptureGameAudio ? 1.0 : 0.4;
                camera.Opacity = hasScreenshot ? 1.0 : 0.7;

                if (service.IsBusy)
                {
                    status.Text = service.State == AiAnswerState.Sending
                        ? Loc.Text("CommandDockAiSending", "Sending…")
                        : Loc.Text("CommandDockAiAnswering", "Answering…");
                    status.Foreground = style.TextMain;
                    SetPulse(false);
                }
                else
                {
                    switch (recorder.State)
                    {
                        case AiRecorderState.Recording:
                            status.Text = string.Format(
                                Loc.Text("CommandDockAiRecording", "Recording {0}"), Countdown(recorder.Elapsed));
                            status.Foreground = RecordRed;
                            SetPulse(true);
                            break;

                        case AiRecorderState.Ready:
                            status.Text = string.Format(
                                Loc.Text("CommandDockAiReady", "{0} recorded"), Countdown(recorder.Elapsed));
                            status.Foreground = style.TextMain;
                            SetPulse(false);
                            break;

                        default:
                            // Where the answer panel is switched off there is no panel to
                            // look at, and "failed" with no reason anywhere is the complaint
                            // that put the history in the settings. So it points there.
                            status.Text = service.State == AiAnswerState.Failed
                                ? (AiCompanionStore.Current.TextAnswers
                                    ? Loc.Text("CommandDockAiFailed", "Failed — see the panel")
                                    : Loc.Text("CommandDockAiFailedHistory", "Failed — see recent questions"))
                                : recorder.LastError switch
                                {
                                    "no-microphone" => Loc.Text("CommandDockAiNoMic", "No microphone found"),
                                    "cutoff" => Loc.Text("CommandDockAiCutoff", "Stopped after five minutes"),
                                    null or "" => hasScreenshot
                                        ? Loc.Text("CommandDockAiShotAttached", "Screenshot attached")
                                        : Loc.Text("CommandDockAiIdle", "Ready"),
                                    _ => recorder.LastError,
                                };
                            status.Foreground = service.State == AiAnswerState.Failed
                                ? RecordRed
                                : style.TextSub;
                            SetPulse(false);
                            break;
                    }
                }

                // Nothing to throw away means no button to throw it away with.
                discard.Visibility = recorder.State != AiRecorderState.Idle || hasScreenshot
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                void SetPulse(bool on)
                {
                    if (on == pulsing) return;
                    pulsing = on;

                    if (on)
                    {
                        record.BeginAnimation(UIElement.OpacityProperty, pulse);
                    }
                    else
                    {
                        record.BeginAnimation(UIElement.OpacityProperty, null);
                        record.Opacity = 1.0;
                    }
                }
            });

            return shell;
        }

        private static readonly Brush RecordRed = Frozen(Color.FromRgb(0xE5, 0x39, 0x35));

        private static void Glyph(Border button, string glyph)
        {
            if (button.Child is TextBlock text && text.Text != glyph) text.Text = glyph;
        }

        /// <summary>
        /// Starts or ends a recording. Also what the push-to-talk hotkey calls, which is why it
        /// takes no arguments and finds everything it needs itself.
        /// </summary>
        internal void ToggleAiRecording()
        {
            var recorder = AiRecorder.Instance;

            if (recorder.State == AiRecorderState.Recording)
            {
                recorder.Stop();
                RefreshTiles();

                // The whole point of auto-send is that stopping is the last thing you do
                // before going back to the game — reaching for a second button defeats it.
                if (AiCompanionStore.Current.AutoSendAfterRecording) _ = AutoSendAsync();
                return;
            }

            // Without a key there is nowhere to send it, so recording would only fill the disk.
            if (!AiCompanionStore.HasKey) return;

            // A new question clears the last answer. Leaving it up would make it ambiguous which
            // question the panel under the tile is answering.
            AiCompanionService.Instance.ClearAnswer();
            HideAiAnswer();

            recorder.Start(AiCompanionStore.Current.CaptureGameAudio);
            RefreshTiles();
        }

        /// <summary>Throws away the recording and any screenshot taken for it.</summary>
        internal void DiscardAiQuestion()
        {
            AiRecorder.Instance.Discard();

            var service = AiCompanionService.Instance;
            service.AttachScreenshot(null);
            service.ClearAnswer();

            HideAiAnswer();
            RefreshTiles();
        }

        /// <summary>Sends what has been recorded and attached, and opens the answer panel.</summary>
        private async Task SendAiQuestion()
        {
            var service = AiCompanionService.Instance;
            if (service.IsBusy) return;

            // Still running: pressing send without stopping first is the obvious thing to do, and
            // it means "that is the question, take it".
            if (AiRecorder.Instance.State == AiRecorderState.Recording) AiRecorder.Instance.Stop();

            if (AiCompanionStore.Current.TextAnswers) ShowAiAnswer();
            RefreshTiles();

            await service.AskAsync(DockContext());

            RefreshTiles();
        }

        /// <summary>
        /// Stopping the recording is the whole interaction: take the screenshot if one was
        /// asked for, then send.
        ///
        /// The screenshot still counts three down first. That delay is not politeness — it is
        /// the time it takes to get back into the game after pressing anything at all, and
        /// skipping it here would attach a picture of the chat box every time.
        /// </summary>
        private async Task AutoSendAsync()
        {
            if (AiCompanionStore.Current.AttachScreenshotByDefault) await CaptureAiScreenshot();

            await SendAiQuestion();
        }

        /// <summary>
        /// What the app already knows about the situation, so the companion does not have to be
        /// told the time of day or how full the server is.
        /// </summary>
        private string? DockContext()
        {
            var host = DockHost;
            if (host == null) return null;

            try
            {
                var (time, isDay, _) = host.DockServerTime;
                var (current, max, _) = host.DockPopulation;

                var parts = new System.Collections.Generic.List<string>();

                if (!string.IsNullOrEmpty(time))
                    parts.Add($"in-game time {time} ({(isDay ? "day" : "night")})");
                if (max > 0)
                    parts.Add($"{current} of {max} players on the server");

                return parts.Count == 0 ? null : string.Join(", ", parts) + ".";
            }
            catch
            {
                return null;
            }
        }

        private bool _capturingAiShot;

        /// <summary>
        /// Counts three down, takes the picture, then flashes and clicks.
        ///
        /// The delay is the whole point. Pressing anything in this app means Rust no longer has
        /// the screen — the chat is open, or the crafting menu, or the game is not even focused —
        /// so a shot taken the instant the button goes down is a picture of a menu. Three seconds
        /// is about what it takes to get back in. The flash and the shutter are then the only way
        /// to know it happened, because by that point the user is looking at the game.
        /// </summary>
        private async Task CaptureAiScreenshot()
        {
            if (_capturingAiShot) return;
            _capturingAiShot = true;

            var countdown = _aiCountdown;
            var flash = _aiFlash;

            try
            {
                if (countdown != null) countdown.Visibility = Visibility.Visible;

                for (int i = 3; i > 0; i--)
                {
                    if (countdown != null) countdown.Text = i.ToString();
                    await Task.Delay(1000);
                }

                if (countdown != null) countdown.Visibility = Visibility.Collapsed;

                // Only one screenshot rides along with a question; a second press replaces the
                // first rather than leaving it behind in the temp folder.
                var path = await GameScreenshot.CaptureAsync(OurWindowHandles());
                AiCompanionService.Instance.AttachScreenshot(path);

                if (path != null)
                {
                    PlayShutter();
                    flash?.BeginAnimation(UIElement.OpacityProperty,
                        new DoubleAnimation(0.85, 0.0, TimeSpan.FromMilliseconds(420)));
                }

                RefreshTiles();
            }
            finally
            {
                if (countdown != null) countdown.Visibility = Visibility.Collapsed;
                _capturingAiShot = false;
            }
        }

        private TextBlock? _aiCountdown;
        private Border? _aiFlash;

        /// <summary>
        /// Every window of ours that sits over the game, so the screenshot is of the game.
        ///
        /// The dock and the answer panel are the two that are deliberately on top of Rust at
        /// all times; anything else of ours that happens to be open is on the screen too, and
        /// equally not what the question is about.
        /// </summary>
        private System.Collections.Generic.List<IntPtr> OurWindowHandles()
        {
            var handles = new System.Collections.Generic.List<IntPtr>();
            var windows = Application.Current?.Windows.OfType<Window>().ToList();
            if (windows == null) return handles;

            foreach (var window in windows)
            {
                if (!window.IsVisible) continue;

                try
                {
                    var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                    if (handle != IntPtr.Zero) handles.Add(handle);
                }
                catch { }
            }

            return handles;
        }

        private MediaPlayer? _shutter;

        /// <summary>
        /// The shutter click. Opened once and rewound, the way the ticket chime is, because
        /// reopening the file on every press costs a delay the effect cannot afford.
        /// </summary>
        private void PlayShutter()
        {
            try
            {
                if (_shutter == null)
                {
                    var path = System.IO.Path.Combine(
                        AppContext.BaseDirectory, "Assets", "sounds", "screenshot.mp3");
                    if (!System.IO.File.Exists(path)) return;

                    _shutter = new MediaPlayer();
                    _shutter.Open(new Uri(path, UriKind.Absolute));
                }

                _shutter.Position = TimeSpan.Zero;
                _shutter.Play();
            }
            catch
            {
                // The flash already said it happened; a sound that will not play is not worth
                // more than that.
            }
        }
    }
}
