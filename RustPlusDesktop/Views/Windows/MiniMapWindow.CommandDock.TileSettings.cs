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

        private void OpenTileSettings(CommandDockTile tile, FrameworkElement anchor)
        {
            _settingsTile = tile;

            TileSettingsPanel.Content = BuildTileSettings(tile);
            TileSettingsPopup.IsOpen = false;   // reposition cleanly when moving between tiles

            // Beside the tile, then nudged back onto the screen the dock is on. Same rule as the
            // map's own settings: a panel on the neighbouring monitor is a panel nobody sees.
            double x = Canvas.GetLeft(anchor);
            double y = Canvas.GetTop(anchor);
            if (double.IsNaN(x)) x = 0;
            if (double.IsNaN(y)) y = 0;

            const double panelWidth = 236;
            const double panelHeight = 330;
            var screen = ScreenBoundsFor(this);

            double offsetX = x + anchor.ActualWidth + 8;
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

                // Rebuilt so the sliders show the inherited values they just fell back to.
                if (_dock.Tiles.Contains(tile) && _tileElements.TryGetValue(tile.Id, out var el))
                    OpenTileSettings(tile, el);
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
                    if (_tileElements.TryGetValue(tile.Id, out var el)) OpenTileSettings(tile, el);
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
            CommandDockTileKinds.TeamChat => Loc.Text("TeamChat", "Team chat"),
            CommandDockTileKinds.ClanChat => Loc.Text("ClanChat", "Clan chat"),
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
