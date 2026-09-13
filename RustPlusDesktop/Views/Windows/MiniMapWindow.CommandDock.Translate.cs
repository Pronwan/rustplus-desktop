using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using RustPlusDesk.Services.AiCompanion;

namespace RustPlusDesk
{
    /// <summary>
    /// Paste in what somebody wrote, read it back in your own language — or type your own and
    /// read it back in theirs.
    ///
    /// Built for the dock rather than for the app window because that is where it is useful: put
    /// it above the chat, copy a line out of the game, and the answer is one press away without
    /// alt-tabbing. The microphone beside the input is the same idea for voice, and comes with
    /// the same warning the AI companion's local path carries — Windows only understands the
    /// languages it has a recogniser for, and hands back confident nonsense for the rest, so the
    /// language it listened in is shown with the result.
    /// </summary>
    public partial class MiniMapWindow
    {
        /// <summary>
        /// What each translate tile is holding, kept out of the elements.
        ///
        /// The dock rebuilds every tile whenever anything about it changes — a resize, a style,
        /// a server reconnect — so a half-typed sentence living in the TextBox would be thrown
        /// away by an event that has nothing to do with this tile.
        /// </summary>
        private sealed class TranslateState
        {
            public string Input = "";
            public string Output = "";
            public bool IsError;
            public bool Busy;

            /// <summary>Set while this tile is the one holding the microphone.</summary>
            public bool Listening;
        }

        private readonly Dictionary<string, TranslateState> _translateStates = new();

        private TranslateState TranslateStateFor(string tileId)
        {
            if (!_translateStates.TryGetValue(tileId, out var state))
            {
                state = new TranslateState();
                _translateStates[tileId] = state;
            }

            return state;
        }

        /// <summary>The language a tile translates into: its own, or the app's.</summary>
        private static string TranslateTargetOf(CommandDockTile tile) =>
            string.IsNullOrWhiteSpace(tile.TranslateTarget)
                ? CultureInfo.CurrentUICulture.Name
                : tile.TranslateTarget!;

        private FrameworkElement BuildTranslateTile(CommandDockTile tile)
        {
            var style = StyleFor(tile);
            var shell = TileShell(style);
            shell.Tag = tile;

            var state = TranslateStateFor(tile.Id);

            // One cell high leaves about thirty pixels a row, which is a line of text and
            // nothing else. Taller, and both boxes are worth wrapping and scrolling.
            bool tall = tile.RowSpan >= 2;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            shell.Child = root;

            // ── what goes in ────────────────────────────────────────────────
            var top = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(top, 0);
            root.Children.Add(top);

            var mic = IconButton(
                GlyphMicrophone,
                style,
                Loc.Text("CommandDockTranslateListen",
                    "Listen to the game and the microphone, and write down what was said"));

            mic.Margin = new Thickness(0, 0, 4, 0);
            mic.MouseLeftButtonUp += (_, e) => { e.Handled = true; ToggleTranslateListening(tile); };
            Grid.SetColumn(mic, 0);
            top.Children.Add(mic);

            var input = new TextBox
            {
                Text = state.Input,
                FontSize = style.Size(11),
                Foreground = style.TextMain,
                CaretBrush = style.TextMain,
                Background = style.Chrome(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(5, 1, 5, 1),
                VerticalContentAlignment = VerticalAlignment.Center,
                AcceptsReturn = false,
                TextWrapping = tall ? TextWrapping.Wrap : TextWrapping.NoWrap,
                VerticalScrollBarVisibility = tall ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            };

            ToolTipService.SetToolTip(input, Loc.Text("CommandDockTranslateInputHint",
                "Paste or type something, then press send"));

            // Kept on the way out of the tile rather than on every keystroke: this runs on the
            // dock's own thread while the game has the screen, and the state only has to be
            // right by the time something reads it.
            input.TextChanged += (_, _) =>
            {
                state.Input = input.Text;

                // Clearing the box clears the answer under it. It was the answer to what used
                // to be there, and left up it reads as the answer to whatever is typed next.
                if (input.Text.Trim().Length > 0 || state.Output.Length == 0) return;

                state.Output = "";
                state.IsError = false;
                RefreshTiles();
            };
            input.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;

                e.Handled = true;
                state.Input = input.Text;
                _ = TranslateTileAsync(tile);
            };

            Grid.SetColumn(input, 1);
            top.Children.Add(input);

            var send = IconButton(GlyphSend, style, Loc.Text("CommandDockTranslateSend", "Translate"));
            send.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                state.Input = input.Text;
                _ = TranslateTileAsync(tile);
            };
            Grid.SetColumn(send, 2);
            top.Children.Add(send);

            // ── what comes back ─────────────────────────────────────────────
            var bottom = new Grid();
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(bottom, 1);
            root.Children.Add(bottom);

            var flag = BuildTranslateFlag(tile, style);
            ApplyTranslateFlag(flag, tile, style);
            Grid.SetColumn(flag, 0);
            bottom.Children.Add(flag);

            var normal = style.TextMain;
            var failed = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A));

            var output = new TextBlock
            {
                FontSize = style.Size(11),

                Effect = style.TextShadow,
                VerticalAlignment = tall ? VerticalAlignment.Top : VerticalAlignment.Center,
                TextWrapping = tall ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextTrimming = tall ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            };

            var scroller = new ScrollViewer
            {
                Content = output,
                VerticalScrollBarVisibility = tall ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(5, 1, 5, 1),
                Background = style.Chrome(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            };

            Grid.SetColumn(scroller, 1);
            bottom.Children.Add(scroller);

            // The tile swallows presses so the window does not drag itself. The text box is
            // exempt by type; these three are Borders and have to be registered, or the mic
            // cannot be pressed and the flag cannot be opened.
            KeepPresses(mic, send, flag);

            // The dock refreshes tiles rather than rebuilding them, which is what keeps a
            // half-typed sentence alive through everything else that ticks on this window.
            _tileRefreshers.Add(() =>
            {
                bool listening = TranslateOwnsRecorder(tile);

                Glyph(mic, listening ? GlyphStop : GlyphMicrophone);
                mic.Background = listening
                    ? RecordRed
                    : Brush("SurfaceAlt", Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));

                ToolTipService.SetToolTip(mic, listening
                    ? Loc.Text("CommandDockTranslateStop", "Stop and write down what was said")
                    : Loc.Text("CommandDockTranslateListen",
                        "Listen to the game and the microphone, and write down what was said"));

                send.Opacity = state.Busy ? 0.4 : 1;

                // Never while it is being typed in: the state is only ahead of the box when
                // something else put words there, which is the microphone.
                if (!input.IsFocused && input.Text != state.Input) input.Text = state.Input;

                string text = state.Busy && state.Output.Length == 0
                    ? Loc.Text("CommandDockTranslateWorking", "Translating…")
                    : state.Output;

                if (output.Text != text) output.Text = text;

                // Red is the only colour here that is not the tile's own: a failure styled
                // like an answer reads as an answer.
                output.Foreground = state.IsError ? failed : normal;

                ToolTipService.SetToolTip(output, text.Length > 0 ? text : null);

                // Changing it from the grid only writes it to the tile; this is what puts it
                // on screen.
                ApplyTranslateFlag(flag, tile, style);
            });

            return shell;
        }

        /// <summary>
        /// The flag of the language the answer comes back in, and the way to change it.
        ///
        /// A flag rather than a code because it is read at a glance from across a desk while the
        /// game has the screen, and because the same picture is already what the settings picker
        /// and the LFG list use for a language.
        /// </summary>
        private Border BuildTranslateFlag(CommandDockTile tile, TileStyle style)
        {
            var button = new Border
            {
                Width = 24,
                Height = 18,
                Margin = new Thickness(0, 0, 4, 0),
                CornerRadius = new CornerRadius(3),
                Background = Brush("SurfaceAlt", Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ClipToBounds = true,
            };

            button.MouseLeftButtonUp += (_, e) => { e.Handled = true; OpenTranslateLanguages(tile, button); };
            return button;
        }

        /// <summary>
        /// Puts the current language on the flag. Separate from building it because the tile is
        /// refreshed rather than rebuilt, so this is the only thing that ever changes about it.
        /// </summary>
        private static void ApplyTranslateFlag(Border button, CommandDockTile tile, TileStyle style)
        {
            string target = TranslateTargetOf(tile);
            var language = AppLanguages.All.FirstOrDefault(l => l.Code == target)
                        ?? AppLanguages.All.FirstOrDefault(l =>
                               l.Code.StartsWith(target.Split('-')[0], StringComparison.OrdinalIgnoreCase));

            // Nothing to redraw, and redrawing it every tick would throw away a freshly
            // decoded image several times a second.
            if (Equals(button.Tag, target)) return;
            button.Tag = target;

            ToolTipService.SetToolTip(button, string.Format(
                Loc.Text("CommandDockTranslateInto", "Translating into {0} — click to change"),
                language?.Name ?? target));

            if (language != null && FlagImage(language) is { } image)
            {
                button.Child = new Image { Source = image, Stretch = Stretch.UniformToFill };
                return;
            }

            button.Child = new TextBlock
            {
                Text = target.Split('-')[0].ToUpperInvariant(),
                FontSize = style.Size(9),
                Foreground = style.TextMain,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        private static BitmapImage? FlagImage(AppLanguage language)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(language.FlagPath);
                image.EndInit();
                if (image.CanFreeze) image.Freeze();

                return image;
            }
            catch
            {
                // A missing flag leaves the code showing instead; the tile still works.
                return null;
            }
        }

        /// <summary>Every language the app ships, as a grid of flags under the one on the tile.</summary>
        private void OpenTranslateLanguages(CommandDockTile tile, UIElement anchor)
        {
            var grid = new WrapPanel { MaxWidth = 232, Orientation = Orientation.Horizontal };

            var popup = new Popup
            {
                PlacementTarget = anchor,
                Placement = PlacementMode.Top,
                StaysOpen = false,
                AllowsTransparency = true,
                Child = new Border
                {
                    Padding = new Thickness(6),
                    CornerRadius = new CornerRadius(8),
                    Background = Brush("Surface", Color.FromArgb(0xF2, 0x16, 0x1B, 0x22)),
                    BorderBrush = Brush("CardBorder", Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1),
                    Child = grid,
                },
            };

            string current = TranslateTargetOf(tile);

            foreach (var language in AppLanguages.All)
            {
                var cell = new Border
                {
                    Width = 32,
                    Height = 24,
                    Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(3),
                    Cursor = Cursors.Hand,
                    ClipToBounds = true,
                    BorderThickness = new Thickness(language.Code == current ? 2 : 0),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0xD7, 0xFF)),
                };

                if (FlagImage(language) is { } image)
                {
                    cell.Child = new Image { Source = image, Stretch = Stretch.UniformToFill };
                }
                else
                {
                    cell.Background = Brush("SurfaceAlt", Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
                    cell.Child = new TextBlock
                    {
                        Text = language.Code.Split('-')[0].ToUpperInvariant(),
                        FontSize = 9,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                }

                ToolTipService.SetToolTip(cell, language.Name);

                var chosen = language.Code;
                cell.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    popup.IsOpen = false;

                    tile.TranslateTarget = chosen;
                    SaveDock();

                    // The text on screen was translated into the old language, so it is no
                    // longer an answer to anything. The input stays: the usual reason to change
                    // the flag is to send the same sentence somewhere else.
                    var state = TranslateStateFor(tile.Id);
                    state.Output = "";
                    state.IsError = false;

                    RefreshTiles();
                    if (state.Input.Trim().Length > 0) _ = TranslateTileAsync(tile);
                };

                grid.Children.Add(cell);
            }

            popup.IsOpen = true;
        }

        // ── translating ─────────────────────────────────────────────────────

        /// <summary>
        /// Sends what is in the box to Google and puts the answer underneath.
        ///
        /// Consent is asked for once and shared with the patch notes and the chat, because it is
        /// the same question about the same service: this leaves the machine and goes to Google.
        /// Saying no here leaves the tile as it was rather than reporting a failure — a refusal
        /// the user just made is not news to them.
        /// </summary>
        private async Task TranslateTileAsync(CommandDockTile tile)
        {
            var state = TranslateStateFor(tile.Id);

            string text = state.Input.Trim();
            if (text.Length == 0 || state.Busy) return;

            if (!TrackingService.TranslationConsentGiven && !await AskTranslateConsentAsync()) return;

            state.Busy = true;
            state.IsError = false;
            state.Output = "";
            RefreshTiles();

            try
            {
                var result = await TranslationService
                    .TranslateAsync(text, TranslateTargetOf(tile))
                    .ConfigureAwait(true);

                if (!result.Ok)
                {
                    // The usual failure by a distance is Google's rate limit, which is a 429 on
                    // an endpoint with no key behind it. Saying so is more use than "failed",
                    // because the answer is to wait rather than to change anything.
                    state.IsError = true;
                    state.Output = Loc.Text("CommandDockTranslateFailed",
                        "Translation service unavailable — it may be rate-limited. Try again shortly.");
                }
                else
                {
                    state.Output = result.Text;
                    state.IsError = false;
                }
            }
            catch (Exception ex)
            {
                state.IsError = true;
                state.Output = ex.Message;
            }
            finally
            {
                state.Busy = false;
                RefreshTiles();
            }
        }

        /// <summary>The one question, asked the way the rest of the app asks it.</summary>
        private async Task<bool> AskTranslateConsentAsync()
        {
            var box = new Wpf.Ui.Controls.MessageBox
            {
                Title = Loc.Text("ChatTranslateConsentTitle", "Translate message"),
                Content = Loc.Text("ChatTranslateConsentBody",
                    "Translating sends the message text to Google Translate. Continue?"),
                PrimaryButtonText = Loc.Text("ChatTranslateConsentAccept", "Translate"),
                CloseButtonText = Loc.Text("Cancel", "Cancel"),
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };

            if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return false;

            TrackingService.TranslationConsentGiven = true;
            return true;
        }

        // ── listening ───────────────────────────────────────────────────────

        /// <summary>
        /// Records the game and the microphone together, then writes down what it heard.
        ///
        /// The recorder is one object for the whole app, because there is one microphone. If the
        /// AI companion is already using it, this says so rather than cutting its recording
        /// short — a question half recorded is worse than a translation not started.
        /// </summary>
        private void ToggleTranslateListening(CommandDockTile tile)
        {
            var state = TranslateStateFor(tile.Id);
            var recorder = AiRecorder.Instance;

            if (TranslateOwnsRecorder(tile))
            {
                recorder.Stop();
                state.Listening = false;
                _ = TranscribeTranslateAsync(tile, recorder.MicPath, recorder.GamePath);
                RefreshTiles();
                return;
            }

            // Somebody else is holding it — the companion, or the tile next to this one.
            if (recorder.State != AiRecorderState.Idle)
            {
                state.IsError = true;
                state.Output = Loc.Text("CommandDockTranslateMicBusy",
                    "The microphone is in use by another tile.");
                RefreshTiles();
                return;
            }

            state.IsError = false;
            state.Output = "";

            // Both tracks: the point is to overhear the game, and the microphone comes along so
            // a teammate on voice is picked up in the same pass.
            if (!recorder.Start(captureGameAudio: true))
            {
                state.IsError = true;
                state.Output = recorder.LastError ?? Loc.Text("CommandDockTranslateNoMic",
                    "Recording could not be started.");
            }
            else
            {
                state.Listening = true;
                RecorderOwner = tile.Id;
            }

            RefreshTiles();
        }

        /// <summary>Whether this tile is the one currently recording.</summary>
        private bool TranslateOwnsRecorder(CommandDockTile tile) =>
            RecorderOwner == tile.Id && AiRecorder.Instance.State == AiRecorderState.Recording;

        /// <summary>
        /// Turns the recording into words with Windows' own recogniser, then translates them.
        ///
        /// The language it listened in is put in front of the transcript, because a recogniser
        /// given a language it does not know returns the nearest words it does know rather than
        /// nothing at all. Without that line, "Windows only had English installed" and "they
        /// really did say that" look identical.
        /// </summary>
        private async Task TranscribeTranslateAsync(CommandDockTile tile, string? micPath, string? gamePath)
        {
            var state = TranslateStateFor(tile.Id);

            state.Busy = true;
            state.IsError = false;
            state.Output = Loc.Text("CommandDockTranslateHearing", "Writing down what was said…");
            RefreshTiles();

            // The model works the language out for itself, so there is no caveat to add to
            // its transcript. Windows has one, and it is the difference between the two.
            bool useAi = tile.TranslateUseAi && AiTranscription.Available;

            string heard;
            string? language = useAi ? null : LocalTranscriber.RecognizerLanguage?.NativeName;

            try
            {
                heard = useAi
                    ? await AiTranscription.TranscribeAsync(micPath, gamePath, default).ConfigureAwait(true)
                    : await Task.Run(() =>
                      {
                          var parts = new[]
                              {
                                  LocalTranscriber.Transcribe(gamePath),
                                  LocalTranscriber.Transcribe(micPath),
                              }
                              .Where(p => !string.IsNullOrWhiteSpace(p));

                          return string.Join(" ", parts).Trim();
                      }).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                state.Busy = false;
                state.IsError = true;
                state.Output = ex.Message;
                AiRecorder.Instance.Discard();
                RecorderOwner = null;
                RefreshTiles();
                return;
            }

            AiRecorder.Instance.Discard();
            RecorderOwner = null;
            state.Busy = false;

            if (heard.Length == 0)
            {
                state.IsError = true;
                state.Output = useAi
                    ? Loc.Text("CommandDockTranslateNothingHeardAi",
                        "Nothing intelligible was heard in the recording.")
                    : language == null
                        ? Loc.Text("CommandDockTranslateNoRecognizer",
                            "Windows has no speech recognition installed. Add a speech language in Windows settings.")
                        : string.Format(
                            Loc.Text("CommandDockTranslateNothingHeard",
                                "Nothing was recognised. Windows was listening in {0}."),
                            language);

                RefreshTiles();
                return;
            }

            state.Input = heard;
            RefreshTiles();

            await TranslateTileAsync(tile).ConfigureAwait(true);

            // Appended rather than replacing the translation: the translation is what was asked
            // for, and this is the caveat that belongs with it.
            if (!state.IsError && language != null)
            {
                state.Output += string.Format(
                    Loc.Text("CommandDockTranslateHeardIn", "\n(heard in {0})"), language);

                RefreshTiles();
            }
        }
    }
}
