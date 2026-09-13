using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Deaths;

namespace RustPlusDesk
{
    /// <summary>
    /// The panel behind a tile's gear: how see-through it is, how big and what colour its text
    /// is, plus whatever that kind of tile has of its own.
    ///
    /// Built in code rather than as a UserControl because the kind-specific half differs for
    /// every tile, and a control with six mutually exclusive sections in it is harder to follow
    /// than six short builders.
    /// </summary>
    public partial class MiniMapWindow
    {
        private CommandDockTile? _settingsTile;

        private void OpenTileSettings(CommandDockTile tile)
        {
            _settingsTile = tile;

            TileSettingsPanel.Content = BuildTileSettings(tile);
            TileSettingsPopup.IsOpen = false;   // reposition cleanly when moving between tiles

            // Measured from the tile's cell rather than from the element.
            //
            // Changing a setting rebuilds the dock and then reopens this panel, and at that
            // moment the tile's element is brand new and has not been through a layout pass —
            // its ActualWidth is zero, so the panel opened on top of the tile instead of beside
            // it. The cell knows the width before anything is drawn.
            var rect = CellRect(tile);
            double x = rect.X + _dragPad;
            double y = rect.Y + _dragPad;

            const double panelWidth = 236;
            const double panelHeight = 330;
            var screen = ScreenBoundsFor(this);

            double offsetX = x + rect.Width + 8;
            if (Left + offsetX + panelWidth > screen.Right) offsetX = x - panelWidth - 8;
            if (Left + offsetX < screen.Left) offsetX = screen.Left - Left;

            double offsetY = y;
            if (Top + offsetY + panelHeight > screen.Bottom)
                offsetY = Math.Max(screen.Top - Top, screen.Bottom - panelHeight - Top);

            TileSettingsPopup.HorizontalOffset = offsetX;
            TileSettingsPopup.VerticalOffset = offsetY;
            TileSettingsPopup.IsOpen = true;
        }

        private void CloseTileSettings()
        {
            TileSettingsPopup.IsOpen = false;
            _settingsTile = null;
        }

        private void TileSettingsClose_Click(object sender, RoutedEventArgs e) => CloseTileSettings();

        private System.Windows.Threading.DispatcherTimer? _settingsApplyTimer;

        /// <summary>
        /// Redraws the dock so the change is visible while the slider is still under the thumb,
        /// but not on every one of the twenty values a drag passes through.
        ///
        /// A rebuild throws away each tile's live state — where the chat was scrolled, how far
        /// into its pulse an alarm is — so doing it per tick made dragging the opacity slider
        /// look like the dock was flickering. Coalescing to roughly eight redraws a second is
        /// still continuous to the eye and leaves that state alone in between.
        /// </summary>
        private void TileSettingChanged(bool immediate = false)
        {
            SaveDock();

            // A click has one value, not twenty — coalescing it only delays the feedback.
            if (immediate)
            {
                _settingsApplyTimer?.Stop();
                RebuildTiles();
                return;
            }

            _settingsApplyTimer ??= CreateApplyTimer();
            _settingsApplyTimer.Stop();
            _settingsApplyTimer.Start();
        }

        private System.Windows.Threading.DispatcherTimer CreateApplyTimer()
        {
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120),
            };
            timer.Tick += (_, __) => { timer.Stop(); RebuildTiles(); };
            return timer;
        }

        private FrameworkElement BuildTileSettings(CommandDockTile tile)
        {
            var stack = new StackPanel { Width = 200 };

            stack.Children.Add(SettingsHeading(TileKindLabel(tile)));

            // ── Appearance ──────────────────────────────────────────────────
            var opacityLabel = SettingsLabel("");
            stack.Children.Add(opacityLabel);

            var opacity = new Slider
            {
                Minimum = 0,
                Maximum = 1,
                TickFrequency = 0.05,
                IsSnapToTickEnabled = true,
                Value = tile.Opacity ?? _dock.DefaultOpacity,
                Margin = new Thickness(0, 0, 0, 10),
            };
            void ShowOpacity() => opacityLabel.Text = string.Format(
                Loc.Text("CommandDockTileOpacity", "Opacity {0}%"), (int)Math.Round(opacity.Value * 100));
            ShowOpacity();
            opacity.ValueChanged += (_, __) =>
            {
                ShowOpacity();
                tile.Opacity = opacity.Value;
                TileSettingChanged();
            };
            stack.Children.Add(opacity);

            var fontLabel = SettingsLabel("");
            stack.Children.Add(fontLabel);

            var font = new Slider
            {
                Minimum = 0.7,
                Maximum = 2.0,
                TickFrequency = 0.05,
                IsSnapToTickEnabled = true,
                Value = tile.FontScale ?? _dock.DefaultFontScale,
                Margin = new Thickness(0, 0, 0, 10),
            };
            void ShowFont() => fontLabel.Text = string.Format(
                Loc.Text("CommandDockTileFontSize", "Text size {0}%"), (int)Math.Round(font.Value * 100));
            ShowFont();
            font.ValueChanged += (_, __) =>
            {
                ShowFont();
                tile.FontScale = font.Value;
                TileSettingChanged();
            };
            stack.Children.Add(font);

            stack.Children.Add(SettingsLabel(Loc.Text("CommandDockTileTextColor", "Text colour")));
            stack.Children.Add(BuildColorSwatches(tile));

            // ── Kind-specific ───────────────────────────────────────────────
            var extras = BuildKindSettings(tile);
            if (extras != null)
            {
                stack.Children.Add(new Separator
                {
                    Background = Brush("CardBorder", Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                    Margin = new Thickness(0, 12, 0, 8),
                });
                stack.Children.Add(extras);
            }

            // ── Back to global ──────────────────────────────────────────────
            bool overridden = tile.Opacity != null || tile.FontScale != null || tile.TextColorKey != null;

            var reset = new Button
            {
                Content = Loc.Text("CommandDockTileResetToGlobal", "Back to the dock's defaults"),
                Margin = new Thickness(0, 12, 0, 0),
                Padding = new Thickness(8, 5, 8, 5),
                IsEnabled = overridden,
                Cursor = Cursors.Hand,
            };
            reset.Click += (_, __) =>
            {
                tile.Opacity = null;
                tile.FontScale = null;
                tile.TextColorKey = null;
                TileSettingChanged(immediate: true);

                // Reopened so the sliders show the inherited values they just fell back to.
                if (_dock.Tiles.Contains(tile)) OpenTileSettings(tile);
            };
            stack.Children.Add(reset);

            return stack;
        }

        private UIElement BuildColorSwatches(CommandDockTile tile)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            string current = tile.TextColorKey ?? _dock.DefaultTextColorKey ?? CommandDockTextColors.Auto;

            foreach (var key in CommandDockTextColors.All)
            {
                bool selected = key == current;

                var swatch = new Border
                {
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(0, 0, 5, 5),
                    CornerRadius = new CornerRadius(5),
                    Background = Brush("SurfaceAlt", Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(selected ? 2 : 1),
                    BorderBrush = selected
                        ? Brush("Accent", Color.FromRgb(0x3F, 0xD7, 0xFF))
                        : Brush("CardBorder", Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                    Cursor = Cursors.Hand,
                    Child = new TextBlock
                    {
                        // "A" in the colour itself: the swatch shows what the text will look
                        // like, which a plain block of colour does not.
                        Text = key == CommandDockTextColors.Auto ? "—" : "A",
                        FontWeight = FontWeights.Bold,
                        FontSize = 12,
                        Foreground = TextColorSwatch(key),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };

                ToolTipService.SetToolTip(swatch, TextColorLabel(key));

                var chosen = key;
                swatch.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;

                    // "Theme colour" is the absence of a choice, so it stores null and the tile
                    // goes back to inheriting — picking it should not count as an override.
                    tile.TextColorKey = chosen == CommandDockTextColors.Auto ? null : chosen;

                    TileSettingChanged(immediate: true);

                    // After the rebuild, so the panel re-anchors to the tile that now exists.
                    OpenTileSettings(tile);
                };

                row.Children.Add(swatch);
            }

            return row;
        }

        /// <summary>The options only one kind of tile has. Null when it has none.</summary>
        private UIElement? BuildKindSettings(CommandDockTile tile)
        {
            switch (tile.Kind)
            {
                case CommandDockTileKinds.Clock:
                {
                    var box = new StackPanel();
                    box.Children.Add(SettingsCheck(
                        Loc.Text("CommandDockClockDayNight", "Show time until day or night"),
                        tile.ClockShowDayNight,
                        on => { tile.ClockShowDayNight = on; TileSettingChanged(immediate: true); }));

                    // The analogue face is a dial either way, so the option is only offered
                    // where it changes something.
                    if (tile.ClockStyle != 1)
                    {
                        box.Children.Add(SettingsCheck(
                            Loc.Text("CommandDockClock12Hour", "12-hour clock (AM/PM)"),
                            tile.Clock12Hour,
                            on => { tile.Clock12Hour = on; TileSettingChanged(immediate: true); }));
                    }

                    return box;
                }

                case CommandDockTileKinds.AiCompanion:
                {
                    // These belong to the companion, not to this tile, and are mirrored here
                    // because the dock is where you are standing when you want them off — in
                    // the game, with no reason to go looking through Connected Services.
                    var box = new StackPanel();
                    var settings = Services.AiCompanion.AiCompanionStore.Current;

                    box.Children.Add(SettingsCheck(
                        Loc.Text("CommandDockAiAutoSend", "Send as soon as recording stops"),
                        settings.AutoSendAfterRecording,
                        on =>
                        {
                            var current = Services.AiCompanion.AiCompanionStore.Current;
                            current.AutoSendAfterRecording = on;
                            Services.AiCompanion.AiCompanionStore.Save(current);
                            TileSettingChanged(immediate: true);
                        }));

                    box.Children.Add(SettingsCheck(
                        Loc.Text("CommandDockAiAttachShot", "Attach a screenshot every time"),
                        settings.AttachScreenshotByDefault,
                        on =>
                        {
                            var current = Services.AiCompanion.AiCompanionStore.Current;
                            current.AttachScreenshotByDefault = on;
                            Services.AiCompanion.AiCompanionStore.Save(current);
                            TileSettingChanged(immediate: true);
                        }));

                    box.Children.Add(SettingsCheck(
                        Loc.Text("CommandDockAiGameAudioSetting", "Also record the game's sound"),
                        settings.CaptureGameAudio,
                        on =>
                        {
                            var current = Services.AiCompanion.AiCompanionStore.Current;
                            current.CaptureGameAudio = on;
                            Services.AiCompanion.AiCompanionStore.Save(current);
                            TileSettingChanged(immediate: true);
                        }));

                    box.Children.Add(SettingsCheck(
                        Loc.Text("CommandDockAiTextAnswers", "Show the answer under the tile"),
                        settings.TextAnswers,
                        on =>
                        {
                            var current = Services.AiCompanion.AiCompanionStore.Current;
                            current.TextAnswers = on;

                            // One of the two has to carry the answer, exactly as in the main
                            // settings — an answer with nowhere to go is a wasted request.
                            if (!on) current.AudioAnswers = true;

                            Services.AiCompanion.AiCompanionStore.Save(current);
                            TileSettingChanged(immediate: true);
                        }));

                    return box;
                }

                case CommandDockTileKinds.ServerInfo:
                {
                    var box = new StackPanel();
                    box.Children.Add(SettingsCheck(
                        Loc.Text("CommandDockServerShowGraph", "Show the population graph"),
                        tile.ShowGraph,
                        on => { tile.ShowGraph = on; TileSettingChanged(immediate: true); }));
                    return box;
                }

                case CommandDockTileKinds.Discord:
                {
                    var box = new StackPanel();

                    box.Children.Add(SettingsLabel(Loc.Text("CommandDockDiscordChannel", "Channel")));

                    var channels = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
                    foreach (var type in _discordChannels)
                        channels.Items.Add(new ComboBoxItem { Content = type, Tag = type });

                    channels.SelectedIndex = Math.Max(0, _discordChannels.IndexOf(EffectiveChannel(tile) ?? ""));
                    channels.IsEnabled = _discordChannels.Count > 0;
                    channels.SelectionChanged += (_, __) =>
                    {
                        tile.DiscordChannel = (channels.SelectedItem as ComboBoxItem)?.Tag as string;
                        TileSettingChanged(immediate: true);
                    };
                    box.Children.Add(channels);

                    box.Children.Add(SettingsLabel(Loc.Text("CommandDockDiscordMention", "Mention")));

                    var mention = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
                    mention.Items.Add(new ComboBoxItem { Content = Loc.Text("CommandDockDiscordMentionNone", "None") });
                    mention.Items.Add(new ComboBoxItem { Content = "@here" });
                    mention.Items.Add(new ComboBoxItem { Content = "@everyone" });
                    mention.SelectedIndex = Math.Clamp(tile.DiscordMention, 0, 2);
                    mention.SelectionChanged += (_, __) =>
                    {
                        tile.DiscordMention = mention.SelectedIndex;
                        TileSettingChanged(immediate: true);
                    };
                    box.Children.Add(mention);

                    // No text-to-speech switch here on purpose: the sender reads it from the
                    // channel's own configuration, and a switch on the tile that never reached
                    // it would be a lie in a settings panel.
                    box.Children.Add(new TextBlock
                    {
                        Text = Loc.Text("CommandDockDiscordTtsHint",
                            "Text-to-speech follows this channel's own setting under Connected Services."),
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Brush("TextSubtle", Colors.Gray),
                    });

                    return box;
                }

                case CommandDockTileKinds.Session:
                {
                    var box = new StackPanel();
                    var wipe = new Button
                    {
                        Content = Loc.Text("CommandDockSessionWipe", "Start a new session"),
                        Padding = new Thickness(8, 5, 8, 5),
                        Cursor = Cursors.Hand,
                    };
                    wipe.Click += (_, __) =>
                    {
                        Services.SessionTracker.Instance.Wipe(DockHost?.DockServerKey);
                        TileSettingChanged(immediate: true);
                    };
                    box.Children.Add(wipe);
                    box.Children.Add(new TextBlock
                    {
                        Text = Loc.Text("CommandDockSessionWipeHint",
                            "Resets the figures for this server. Counting continues from zero."),
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 6, 0, 0),
                        Foreground = Brush("TextSubtle", Colors.Gray),
                    });
                    return box;
                }

                case CommandDockTileKinds.Device:
                {
                    var box = new StackPanel();
                    box.Children.Add(SettingsCheck(
                        Loc.Text("CommandDockDeviceShowIcon", "Show the device's icon"),
                        tile.ShowDeviceIcon,
                        on => { tile.ShowDeviceIcon = on; TileSettingChanged(immediate: true); }));
                    box.Children.Add(new TextBlock
                    {
                        Text = Loc.Text("CommandDockDeviceShowIconHint",
                            "Off shows the device name instead — useful when several switches share an icon."),
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Brush("TextSubtle", Colors.Gray),
                    });
                    return box;
                }

                case CommandDockTileKinds.TeamChat:
                case CommandDockTileKinds.ClanChat:
                {
                    var box = new StackPanel();
                    box.Children.Add(SettingsCheck(
                        Loc.Text("CommandDockChatAbbreviate", "Shorten long names"),
                        tile.ChatAbbreviateNames,
                        on => { tile.ChatAbbreviateNames = on; TileSettingChanged(immediate: true); }));
                    return box;
                }

                case CommandDockTileKinds.DeathTrack:
                {
                    var box = new StackPanel();

                    box.Children.Add(SettingsCheck(
                        Loc.Text("CommandDockDeathTrackAlways", "Keep the tile on the dock while alive"),
                        tile.DeathTrackAlwaysVisible,
                        on => { tile.DeathTrackAlwaysVisible = on; TileSettingChanged(immediate: true); }));

                    // Windows reads one script; players come with several. This is the way
                    // round that, and it is the user's own credit being spent, so it is off
                    // until they say otherwise.
                    bool canReadWithAi = AiDeathScreen.Available;

                    var readWithAi = SettingsCheck(
                        Loc.Text("CommandDockDeathTrackWithAi", "Read the name with the AI model"),
                        tile.DeathReadWithAi && canReadWithAi,
                        on => { tile.DeathReadWithAi = on; TileSettingChanged(immediate: true); });

                    readWithAi.IsEnabled = canReadWithAi;
                    box.Children.Add(readWithAi);

                    box.Children.Add(new TextBlock
                    {
                        Text = canReadWithAi
                            ? Loc.Text("CommandDockDeathTrackWithAiHint",
                                "Reads names in any alphabet, and uses your own API credit. " +
                                "Windows only reads the scripts it has a language installed for.")
                            : Loc.Text("CommandDockDeathTrackWithAiUnavailable",
                                "Needs an AI Companion key under Connected Services. Any provider will do."),
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 10),
                        Foreground = Brush("TextSubtle", Colors.Gray),
                    });

                    box.Children.Add(new TextBlock
                    {
                        Text = DeathScreenReader.Available
                            ? string.Format(
                                Loc.Text("CommandDockDeathTrackReader", "Windows reads text in {0}."),
                                DeathScreenReader.RecognizerLanguage)
                            : Loc.Text("CommandDockDeathTrackNoOcr",
                                "Windows has no text recognition installed."),
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 10),
                        Foreground = Brush("TextSubtle", Colors.Gray),
                    });

                    // The region, as a share of the screen rather than in pixels — see the
                    // tile model. Four sliders and a preview: the numbers mean nothing on
                    // their own, and the only way to know a region is right is to look at
                    // what came out of it.
                    box.Children.Add(SettingsHeading(
                        Loc.Text("CommandDockDeathTrackRegion", "Where the name is")));

                    AddRegionSlider(box, tile, Loc.Text("CommandDockDeathTrackLeft", "From the left {0}%"),
                        () => tile.DeathRegionLeft, v => tile.DeathRegionLeft = v, 0, 0.9);

                    AddRegionSlider(box, tile, Loc.Text("CommandDockDeathTrackTop", "From the top {0}%"),
                        () => tile.DeathRegionTop, v => tile.DeathRegionTop = v, 0, 0.9);

                    AddRegionSlider(box, tile, Loc.Text("CommandDockDeathTrackWide", "Width {0}%"),
                        () => tile.DeathRegionWidth, v => tile.DeathRegionWidth = v, 0.05, 1);

                    AddRegionSlider(box, tile, Loc.Text("CommandDockDeathTrackHigh", "Height {0}%"),
                        () => tile.DeathRegionHeight, v => tile.DeathRegionHeight = v, 0.02, 0.5);

                    var preview = new Image
                    {
                        MaxHeight = 90,
                        Stretch = Stretch.Uniform,
                        StretchDirection = StretchDirection.DownOnly,
                        Margin = new Thickness(0, 4, 0, 4),
                        Visibility = Visibility.Collapsed,
                    };

                    var readBack = new TextBlock
                    {
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 8),
                        Foreground = Brush("TextSubtle", Colors.Gray),
                    };

                    // The way out for a screen nobody measured. Takes a picture of the
                    // whole screen and lets the region be drawn on it afterwards, because
                    // the death screen is up for seconds and drawing a careful box is not a
                    // thing anybody does in seconds.
                    var pick = new Wpf.Ui.Controls.Button
                    {
                        Content = Loc.Text("CommandDockDeathTrackPick", "Pick the area from a screenshot"),
                        Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
                        FontSize = 11,
                        Margin = new Thickness(0, 0, 0, 6),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                    };

                    pick.Click += async (_, __) =>
                    {
                        pick.IsEnabled = false;

                        try
                        {
                            var shot = await Services.AiCompanion.GameScreenshot
                                .CaptureWholeScreenAsync(OverlayWindows());

                            if (shot == null) return;

                            var picker = new Views.DeathRegionPicker(shot) { Owner = this };
                            if (picker.ShowDialog() != true || picker.Region is not { } region) return;

                            tile.DeathRegionLeft = region.Left;
                            tile.DeathRegionTop = region.Top;
                            tile.DeathRegionWidth = region.Width;
                            tile.DeathRegionHeight = region.Height;

                            SaveDock();

                            // Reopened rather than refreshed: the four sliders were built
                            // with the old numbers and have no idea these changed.
                            OpenTileSettings(tile);
                        }
                        finally
                        {
                            pick.IsEnabled = true;
                        }
                    };

                    box.Children.Add(pick);
                    var test = new Wpf.Ui.Controls.Button
                    {
                        Content = Loc.Text("CommandDockDeathTrackTest", "Test on the screen now"),
                        FontSize = 11,
                        Margin = new Thickness(0, 0, 0, 6),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                    };

                    test.Click += async (_, __) =>
                    {
                        test.IsEnabled = false;

                        try
                        {
                            var read = await DeathScreenReader.ReadAsync(
                                tile.DeathRegionLeft, tile.DeathRegionTop,
                                tile.DeathRegionWidth, tile.DeathRegionHeight,
                                OverlayWindows());

                            if (read.ImagePath != null)
                            {
                                // Loaded rather than referenced: the same file is overwritten
                                // on the next test, and a referenced one stays locked.
                                var image = new System.Windows.Media.Imaging.BitmapImage();
                                image.BeginInit();
                                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                                image.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
                                image.UriSource = new Uri(read.ImagePath);
                                image.EndInit();

                                preview.Source = image;
                                preview.Visibility = Visibility.Visible;
                            }

                            readBack.Text = read.Lines.Count > 0
                                ? string.Join("  ·  ", read.Lines)
                                : Loc.Text("CommandDockDeathTrackNothing",
                                    "Nothing readable there — check the region in this tile's settings.");
                        }
                        catch (Exception ex)
                        {
                            readBack.Text = ex.Message;
                        }
                        finally
                        {
                            test.IsEnabled = true;
                        }
                    };

                    box.Children.Add(test);
                    box.Children.Add(preview);
                    box.Children.Add(readBack);

                    return box;
                }

                case CommandDockTileKinds.Collapse:
                {
                    var box = new StackPanel();

                    box.Children.Add(SettingsCheck(
                        Loc.Text("CommandDockCollapseWithMap", "Hide the mini-map too"),
                        tile.CollapseIncludesMap,
                        on =>
                        {
                            tile.CollapseIncludesMap = on;

                            // Applied at once when the dock is already collapsed, or the
                            // setting would only take effect after collapsing twice.
                            TileSettingChanged(immediate: true);
                        }));

                    box.Children.Add(new TextBlock
                    {
                        Text = Loc.Text("CommandDockCollapseHint",
                            "One press hides every other tile where it stands; the next press " +
                            "brings the arrangement back unchanged."),
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 4),
                        Foreground = Brush("TextSubtle", Colors.Gray),
                    });

                    return box;
                }

                case CommandDockTileKinds.Translate:
                {
                    var box = new StackPanel();

                    // Which service translates. Two of them, so radio buttons: a pair of
                    // checkboxes would let both be off, and there is no such state.
                    bool canAskAi = Services.AiCompanion.AiTranslation.Available;
                    string group = "translator-" + tile.Id;

                    box.Children.Add(SettingsRadio(
                        Loc.Text("CommandDockTranslateWithGoogle", "Use Google Translate"),
                        group,
                        !tile.TranslateTextWithAi || !canAskAi,
                        on => { if (on) { tile.TranslateTextWithAi = false; TileSettingChanged(immediate: true); } }));

                    var withAi = SettingsRadio(
                        Loc.Text("CommandDockTranslateWithAi", "Use the AI model to translate"),
                        group,
                        tile.TranslateTextWithAi && canAskAi,
                        on => { if (on) { tile.TranslateTextWithAi = true; TileSettingChanged(immediate: true); } });

                    withAi.IsEnabled = canAskAi;
                    box.Children.Add(withAi);

                    box.Children.Add(new TextBlock
                    {
                        Text = canAskAi
                            ? Loc.Text("CommandDockTranslateWithAiHint",
                                "Better on chat — slang, abbreviations and typos — and uses your own API credit.")
                            : Loc.Text("CommandDockTranslateWithAiUnavailable",
                                "Needs an AI Companion key under Connected Services. Any provider will do."),
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 8),
                        Foreground = Brush("TextSubtle", Colors.Gray),
                    });

                    // Off unless there is a provider that takes audio and a key for it. The
                    // policy behind it was read when that key was entered, so there is
                    // nothing further to agree to here.
                    bool canUseAi = Services.AiCompanion.AiTranscription.Available;

                    var useAi = SettingsCheck(
                        Loc.Text("CommandDockTranslateUseAi", "Use the AI model for speech"),
                        tile.TranslateUseAi && canUseAi,
                        on => { tile.TranslateUseAi = on; TileSettingChanged(immediate: true); });

                    useAi.IsEnabled = canUseAi;
                    box.Children.Add(useAi);

                    box.Children.Add(new TextBlock
                    {
                        Text = canUseAi
                            ? Loc.Text("CommandDockTranslateUseAiHint",
                                "Works out the spoken language itself, and uses your own API credit. " +
                                "Windows only understands the languages it has a recogniser installed for.")
                            : Loc.Text("CommandDockTranslateUseAiUnavailable",
                                "Needs an AI Companion provider that accepts audio, with a key stored for it. " +
                                "Speech is written down by Windows until then."),
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 4),
                        Foreground = Brush("TextSubtle", Colors.Gray),
                    });

                    return box;
                }

                // The map never reaches this panel — its gear opens the mini-map settings, where
                // its shape, size and layers already live.

                default:
                    return null;
            }
        }

        private string TileKindLabel(CommandDockTile tile) => tile.Kind switch
        {
            CommandDockTileKinds.Map => Loc.Text("MiniMap", "Mini-map"),
            CommandDockTileKinds.Clock => Loc.Text("CommandDockSectionClock", "Clock"),
            CommandDockTileKinds.Device => FindDevice(tile.EntityId)?.DisplayName
                                           ?? Loc.Text("CommandDockSectionDevices", "Devices"),
            CommandDockTileKinds.Event => Loc.Text("CommandDockSectionEvents", "Events"),
            CommandDockTileKinds.Rule => DockHost?.DockRules.FirstOrDefault(r => r.Id == tile.RuleId)?.Name
                                         ?? Loc.Text("CommandDockSectionRules", "Logic Engine"),
            CommandDockTileKinds.Session => Loc.Text("CommandDockSessionTitle", "Session"),
            CommandDockTileKinds.Discord => "Discord",
            CommandDockTileKinds.ServerInfo => Loc.Text("CommandDockServerInfoTitle", "Server"),
            CommandDockTileKinds.TeamChat => Loc.Text("TeamChat", "Team chat"),
            CommandDockTileKinds.ClanChat => Loc.Text("ClanChat", "Clan chat"),
            CommandDockTileKinds.Translate => Loc.Text("CommandDockTranslateTitle", "Translate"),
            CommandDockTileKinds.Collapse => Loc.Text("CommandDockCollapseTitle", "Collapse"),
            _ => tile.Kind,
        };

        private static string TextColorLabel(string key) => key switch
        {
            CommandDockTextColors.Auto => Loc.Text("CommandDockColorAuto", "Theme colour"),
            CommandDockTextColors.White => Loc.Text("CommandDockColorWhite", "White"),
            CommandDockTextColors.Black => Loc.Text("CommandDockColorBlack", "Black"),
            CommandDockTextColors.Cyan => Loc.Text("CommandDockColorCyan", "Cyan"),
            CommandDockTextColors.Amber => Loc.Text("CommandDockColorAmber", "Amber"),
            CommandDockTextColors.Red => Loc.Text("CommandDockColorRed", "Red"),
            CommandDockTextColors.Green => Loc.Text("CommandDockColorGreen", "Green"),
            _ => key,
        };

        // ── Small builders shared by the panel ──────────────────────────────

        /// <summary>
        /// One edge of the death-screen region, as a share of the screen.
        ///
        /// Shown as a percentage because that is what it is — the region has to hold its
        /// meaning across resolutions, and a pixel offset does not.
        /// </summary>
        private void AddRegionSlider(
            Panel box, CommandDockTile tile, string format,
            Func<double> read, Action<double> write, double min, double max)
        {
            var label = SettingsLabel("");
            box.Children.Add(label);

            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                TickFrequency = 0.005,
                IsSnapToTickEnabled = true,
                Value = Math.Clamp(read(), min, max),
                Margin = new Thickness(0, 0, 0, 8),
            };

            void Show() => label.Text = string.Format(format, Math.Round(slider.Value * 100, 1));
            Show();

            slider.ValueChanged += (_, __) =>
            {
                Show();
                write(slider.Value);

                // Nothing on screen changes with these, so there is no rebuild to coalesce —
                // only the value to keep.
                SaveDock();
            };

            box.Children.Add(slider);
        }

        private static TextBlock SettingsHeading(string text) => new()
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 12),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Brush("TextPrimary", Colors.White),
        };

        private static TextBlock SettingsLabel(string text) => new()
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = Brush("TextSubtle", Colors.Gray),
        };

        private static RadioButton SettingsRadio(
            string text, string group, bool isChecked, Action<bool> onChanged)
        {
            var button = new RadioButton
            {
                Content = text,
                GroupName = group,
                IsChecked = isChecked,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4),
            };

            button.Checked += (_, __) => onChanged(true);
            button.Unchecked += (_, __) => onChanged(false);
            return button;
        }

        private static CheckBox SettingsCheck(string text, bool isChecked, Action<bool> onChanged)
        {
            var box = new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4),
            };
            box.Checked += (_, __) => onChanged(true);
            box.Unchecked += (_, __) => onChanged(false);
            return box;
        }
    }
}
