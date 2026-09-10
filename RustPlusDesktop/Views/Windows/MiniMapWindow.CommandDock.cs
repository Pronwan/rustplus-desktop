using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk
{
    /// <summary>
    /// The mini-map's command dock: the map is one tile on a cell grid, and the rest of the
    /// grid holds clocks, devices, events, chat and Logic Engine launchers.
    ///
    /// Tiles are built in code rather than through an ItemsControl because the dock is a
    /// positional layout, not a list: every tile needs its own pixel rect on the canvas, and the
    /// cell arithmetic that produces it is the same code that has to find a free spot when a new
    /// tile is added. Live values are pulled once a second — half the content is a countdown, so
    /// a timer has to run either way.
    /// </summary>
    public partial class MiniMapWindow
    {
        /// <summary>Set by the main window; null in design time and before a server is picked.</summary>
        public ICommandDockHost? DockHost { get; set; }

        private CommandDockLayout _dock = new();
        private readonly Dictionary<string, FrameworkElement> _tileElements = new();
        private readonly List<Action> _tileRefreshers = new();
        private DispatcherTimer? _dockTimer;
        private bool _isEditMode;
        private CommandDockTilePicker? _picker;

        private const string DockCacheKey = "minimap_dock";

        private void InitCommandDock()
        {
            _dock = StorageService.LoadCache<CommandDockLayout>(DockCacheKey) ?? new CommandDockLayout();

            _dockTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _dockTimer.Tick += (_, __) => RefreshTiles();

            Loaded += (_, __) => { RebuildTiles(); _dockTimer.Start(); };
            Closed += (_, __) => _dockTimer?.Stop();
        }

        private void SaveDock() => StorageService.SaveCache(DockCacheKey, _dock);

        // ── Geometry ────────────────────────────────────────────────────────────

        /// <summary>How many cells the map tile covers, so tiles never land underneath it.</summary>
        private (int Cols, int Rows) MapCellSpan() =>
            (CommandDockLayout.PixelsToCells(_mapWidth), CommandDockLayout.PixelsToCells(_mapHeight));

        /// <summary>
        /// A tile's pixel rect. Cell (0,0) is the map tile's top-left, and columns to the right
        /// of the map start after it — so the grid stays whole while the map changes size.
        /// </summary>
        private Rect CellRect(CommandDockTile tile)
        {
            var (mapCols, mapRows) = MapCellSpan();

            double x = tile.Col < mapCols
                ? CommandDockLayout.CellOffset(tile.Col)
                : _mapWidth + CommandDockLayout.CellGap + CommandDockLayout.CellOffset(tile.Col - mapCols);

            double y = tile.Row < mapRows
                ? CommandDockLayout.CellOffset(tile.Row)
                : _mapHeight + CommandDockLayout.CellGap + CommandDockLayout.CellOffset(tile.Row - mapRows);

            return new Rect(x, y,
                CommandDockLayout.CellsToPixels(tile.ColSpan),
                CommandDockLayout.CellsToPixels(tile.RowSpan));
        }

        /// <summary>Feeds <see cref="MeasureDockBounds"/>; the map tile is measured separately.</summary>
        private IEnumerable<Rect> TileBounds()
        {
            foreach (var tile in _dock.Tiles)
            {
                if (!_tileElements.TryGetValue(tile.Id, out var el)) continue;
                double x = Canvas.GetLeft(el), y = Canvas.GetTop(el);
                if (double.IsNaN(x) || double.IsNaN(y)) continue;
                yield return new Rect(x, y, el.Width, el.Height);
            }
        }

        /// <summary>
        /// Places a new tile like a desktop icon: first free run of cells, scanned down the
        /// column to the right of the map, then further columns, then below the map.
        /// </summary>
        private void AssignFreeCell(CommandDockTile tile)
        {
            var (mapCols, mapRows) = MapCellSpan();
            var taken = new HashSet<(int, int)>();

            for (int c = 0; c < mapCols; c++)
                for (int r = 0; r < mapRows; r++)
                    taken.Add((c, r));

            foreach (var other in _dock.Tiles)
            {
                if (other == tile) continue;
                for (int c = other.Col; c < other.Col + other.ColSpan; c++)
                    for (int r = other.Row; r < other.Row + other.RowSpan; r++)
                        taken.Add((c, r));
            }

            bool Fits(int col, int row)
            {
                for (int c = col; c < col + tile.ColSpan; c++)
                    for (int r = row; r < row + tile.RowSpan; r++)
                        if (taken.Contains((c, r))) return false;
                return true;
            }

            // Right of the map first — a vertical quickbar beside the map is the arrangement
            // this is meant to make easy — then the rows underneath it.
            for (int col = mapCols; col < mapCols + 6; col++)
                for (int row = 0; row < mapRows + 6; row++)
                    if (Fits(col, row)) { tile.Col = col; tile.Row = row; return; }

            for (int row = mapRows; row < mapRows + 12; row++)
                for (int col = 0; col < mapCols + 6; col++)
                    if (Fits(col, row)) { tile.Col = col; tile.Row = row; return; }

            tile.Col = mapCols;
            tile.Row = mapRows;
        }

        // ── Building ────────────────────────────────────────────────────────────

        private void RebuildTiles()
        {
            foreach (var el in _tileElements.Values)
                DockCanvas.Children.Remove(el);

            _tileElements.Clear();
            _tileRefreshers.Clear();
            _tileGrips.Clear();

            foreach (var tile in _dock.Tiles.ToList())
            {
                var el = BuildTile(tile);
                if (el == null) continue;   // unknown kind from a newer build

                var rect = CellRect(tile);
                el.Width = rect.Width;
                el.Height = rect.Height;
                Canvas.SetLeft(el, rect.X);
                Canvas.SetTop(el, rect.Y);

                DockCanvas.Children.Add(el);
                _tileElements[tile.Id] = el;

                // After the tile's own click handlers, so a device toggle still gets its
                // MouseUp: this one only swallows what is left, which is what keeps the
                // window's DragMove from eating the click.
                if (el is Border border)
                {
                    AddResizeGrip(border, tile);
                    AttachTileInteraction(border, tile.Id);
                }
            }

            ApplyEditModeChrome();
            RefreshTiles();
            LayoutDock();
        }

        /// <summary>
        /// Moves existing tiles to the cells they now sit in, without rebuilding them.
        /// Resizing the map changes how many cells it covers, and everything to its right and
        /// below has to follow — rebuilding would restart the chat scroll and the alarm pulse.
        /// </summary>
        private void RepositionTiles()
        {
            foreach (var tile in _dock.Tiles)
            {
                if (!_tileElements.TryGetValue(tile.Id, out var el)) continue;
                var rect = CellRect(tile);
                el.Width = rect.Width;
                el.Height = rect.Height;
                Canvas.SetLeft(el, rect.X);
                Canvas.SetTop(el, rect.Y);
            }
        }

        private void RefreshTiles()
        {
            foreach (var refresh in _tileRefreshers)
            {
                try { refresh(); }
                catch { /* one bad tile must not stop the rest from ticking */ }
            }
        }

        private static Border TileShell()
        {
            return new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Background = Brush("Surface", Color.FromArgb(0xD8, 0x16, 0x1B, 0x22)),
                BorderBrush = Brush("CardBorder", Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                Padding = new Thickness(6),
                SnapsToDevicePixels = true,
            };
        }

        private static Brush Brush(string resourceKey, Color fallback)
        {
            if (Application.Current?.TryFindResource(resourceKey) is Brush found) return found;
            return new SolidColorBrush(fallback);
        }

        private FrameworkElement? BuildTile(CommandDockTile tile) => tile.Kind switch
        {
            CommandDockTileKinds.Clock => BuildClockTile(tile),
            CommandDockTileKinds.Device => BuildDeviceTile(tile),
            CommandDockTileKinds.Event => BuildEventTile(tile),
            CommandDockTileKinds.Rule => BuildRuleTile(tile),
            CommandDockTileKinds.TeamChat => BuildChatTile(tile, clan: false),
            CommandDockTileKinds.ClanChat => BuildChatTile(tile, clan: true),
            _ => null,
        };

        // ── Clock ───────────────────────────────────────────────────────────────

        private FrameworkElement BuildClockTile(CommandDockTile tile)
        {
            var shell = TileShell();
            shell.Tag = tile;

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            shell.Child = stack;

            if (tile.ClockStyle == 1)
            {
                // Analog: a face plus two hands, redrawn from the server clock each tick.
                var face = new Grid { Width = 46, Height = 46, HorizontalAlignment = HorizontalAlignment.Center };
                var dial = new System.Windows.Shapes.Ellipse
                {
                    Stroke = Brush("TextSubtle", Colors.Gray),
                    StrokeThickness = 1.5,
                    Fill = System.Windows.Media.Brushes.Transparent,
                };
                var hourHand = new System.Windows.Shapes.Line
                {
                    X1 = 23, Y1 = 23, X2 = 23, Y2 = 11,
                    StrokeThickness = 2.5,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Stroke = System.Windows.Media.Brushes.White,
                };
                var minuteHand = new System.Windows.Shapes.Line
                {
                    X1 = 23, Y1 = 23, X2 = 23, Y2 = 6,
                    StrokeThickness = 1.5,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Stroke = Brush("Accent", Color.FromRgb(0x3F, 0xD7, 0xFF)),
                };
                var hourRotate = new RotateTransform(0, 23, 23);
                var minuteRotate = new RotateTransform(0, 23, 23);
                hourHand.RenderTransform = hourRotate;
                minuteHand.RenderTransform = minuteRotate;

                face.Children.Add(dial);
                face.Children.Add(hourHand);
                face.Children.Add(minuteHand);
                stack.Children.Add(face);

                var phase = SubtleText();
                stack.Children.Add(phase);

                _tileRefreshers.Add(() =>
                {
                    var (time, isDay, _) = DockHost?.DockServerTime ?? ("-", true, (TimeSpan?)null);
                    if (TryParseServerTime(time, out int h, out int m))
                    {
                        hourRotate.Angle = (h % 12) * 30 + m * 0.5;
                        minuteRotate.Angle = m * 6;
                    }
                    dial.Stroke = isDay
                        ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD1, 0x66))
                        : new SolidColorBrush(Color.FromRgb(0x90, 0xCA, 0xF9));
                    phase.Text = tile.ClockShowDayNight ? DayNightLine() : "";
                    phase.Visibility = string.IsNullOrEmpty(phase.Text) ? Visibility.Collapsed : Visibility.Visible;
                });

                return shell;
            }

            bool rustStyle = tile.ClockStyle == 2;

            var glyph = new TextBlock
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2),
            };
            var clock = new TextBlock
            {
                FontSize = rustStyle ? 20 : 19,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = rustStyle
                    ? new SolidColorBrush(Color.FromRgb(0xCD, 0x41, 0x2B))
                    : Brush("TextPrimary", Colors.White),
                FontFamily = rustStyle ? new FontFamily("Impact, Segoe UI") : new FontFamily("Consolas, Segoe UI"),
            };
            var phaseText = SubtleText();

            stack.Children.Add(glyph);
            stack.Children.Add(clock);
            stack.Children.Add(phaseText);

            _tileRefreshers.Add(() =>
            {
                var (time, isDay, _) = DockHost?.DockServerTime ?? ("-", true, (TimeSpan?)null);
                clock.Text = time;
                glyph.Text = isDay ? "\uE706" : "\uE708";   // sun / moon
                glyph.Foreground = isDay
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD1, 0x66))
                    : new SolidColorBrush(Color.FromRgb(0x90, 0xCA, 0xF9));
                phaseText.Text = tile.ClockShowDayNight ? DayNightLine() : "";
                phaseText.Visibility = string.IsNullOrEmpty(phaseText.Text) ? Visibility.Collapsed : Visibility.Visible;
            });

            return shell;
        }

        private string DayNightLine()
            => (Application.Current?.MainWindow as Views.MainWindow)?.DockTimeUntilNextPhase ?? "";

        private static bool TryParseServerTime(string text, out int hours, out int minutes)
        {
            hours = minutes = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = text.Split(':');
            return parts.Length >= 2
                && int.TryParse(parts[0].Trim(), out hours)
                && int.TryParse(parts[1].Trim(), out minutes);
        }

        private static TextBlock SubtleText() => new()
        {
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Brush("TextSubtle", Colors.Gray),
        };

        // ── Device ──────────────────────────────────────────────────────────────

        private FrameworkElement BuildDeviceTile(CommandDockTile tile)
        {
            var shell = TileShell();
            shell.Tag = tile;
            shell.Cursor = Cursors.Hand;

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            shell.Child = stack;

            var icon = new TextBlock
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 17,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 3),
            };
            var name = SubtleText();
            var state = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            stack.Children.Add(icon);
            stack.Children.Add(name);
            stack.Children.Add(state);

            // Alarms pulse instead of just turning red: a raid alarm that fired while you were
            // looking elsewhere has to be findable at a glance on a busy dock.
            var pulse = new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(550))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            };
            bool pulsing = false;

            shell.MouseLeftButtonUp += async (_, e) =>
            {
                if (_isEditMode) return;
                e.Handled = true;

                var device = FindDevice(tile.EntityId);
                if (device == null || DockHost == null) return;
                if (!IsSwitch(device)) return;

                await DockHost.ToggleDockSwitchAsync(device, !(device.IsOn == true));
            };

            _tileRefreshers.Add(() =>
            {
                var device = FindDevice(tile.EntityId);
                if (device == null)
                {
                    icon.Text = "\uE783";                  // error: the device is gone
                    name.Text = $"#{tile.EntityId}";
                    state.Text = Loc.Text("CommandDockDeviceMissing", "not paired");
                    state.Foreground = Brush("TextSubtle", Colors.Gray);
                    shell.Opacity = 0.5;
                    return;
                }

                shell.Opacity = 1.0;
                name.Text = Abbreviate(device.DisplayName, 12);
                ToolTipService.SetToolTip(shell, device.DisplayName);

                if (IsSwitch(device))
                {
                    bool on = device.IsOn == true;
                    icon.Text = "\uE7E8";                  // power button
                    icon.Foreground = on
                        ? new SolidColorBrush(Color.FromRgb(0x4C, 0xC9, 0x6A))
                        : Brush("TextSubtle", Colors.Gray);
                    state.Text = on ? Loc.Text("On", "On") : Loc.Text("Off", "Off");
                    state.Foreground = icon.Foreground;
                    shell.BorderBrush = on
                        ? new SolidColorBrush(Color.FromArgb(0x99, 0x4C, 0xC9, 0x6A))
                        : Brush("CardBorder", Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
                    StopPulse();
                }
                else if (IsAlarm(device))
                {
                    bool fired = device.IsOn == true;
                    icon.Text = "\uE7ED";                  // ringer
                    icon.Foreground = fired
                        ? new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35))
                        : Brush("TextSubtle", Colors.Gray);
                    state.Text = fired
                        ? Loc.Text("CommandDockAlarmTriggered", "Triggered")
                        : Loc.Text("CommandDockAlarmIdle", "Armed");
                    state.Foreground = icon.Foreground;
                    shell.BorderBrush = fired
                        ? new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35))
                        : Brush("CardBorder", Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));

                    if (fired) StartPulse(); else StopPulse();
                }
                else
                {
                    // Storage monitor, including a tool cupboard: upkeep is the number that
                    // matters, and only a TC ever reports one.
                    icon.Text = "\uE7B8";                  // package
                    icon.Foreground = Brush("TextSubtle", Colors.Gray);
                    var secs = device.UpkeepSeconds ?? 0;
                    if (secs > 0)
                    {
                        state.Text = FormatUpkeep(secs);
                        state.Foreground = secs < 3600
                            ? new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35))
                            : Brush("TextPrimary", Colors.White);
                    }
                    else
                    {
                        state.Text = "—";
                        state.Foreground = Brush("TextSubtle", Colors.Gray);
                    }
                    StopPulse();
                }

                void StartPulse()
                {
                    if (pulsing) return;
                    pulsing = true;
                    shell.BeginAnimation(UIElement.OpacityProperty, pulse);
                }

                void StopPulse()
                {
                    if (!pulsing) return;
                    pulsing = false;
                    shell.BeginAnimation(UIElement.OpacityProperty, null);
                    shell.Opacity = 1.0;
                }
            });

            return shell;
        }

        private SmartDevice? FindDevice(uint entityId)
            => DockHost?.DockDevices.FirstOrDefault(d => d.EntityId == entityId);

        private static bool IsSwitch(SmartDevice d)
            => string.Equals(d.Kind, "SmartSwitch", StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.Kind, "Smart Switch", StringComparison.OrdinalIgnoreCase);

        private static bool IsAlarm(SmartDevice d)
            => string.Equals(d.Kind, "SmartAlarm", StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.Kind, "Smart Alarm", StringComparison.OrdinalIgnoreCase);

        private static string FormatUpkeep(int seconds)
        {
            var span = TimeSpan.FromSeconds(seconds);
            if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
            if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
            return $"{span.Minutes}m";
        }

        private static string Abbreviate(string? text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= max ? text : text.Substring(0, Math.Max(1, max - 1)) + "…";
        }

        // ── Event ───────────────────────────────────────────────────────────────

        private FrameworkElement BuildEventTile(CommandDockTile tile)
        {
            var shell = TileShell();
            shell.Tag = tile;

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            shell.Child = stack;

            var image = new Image
            {
                Width = 26,
                Height = 26,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 3),
            };
            var timer = new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = Brush("TextPrimary", Colors.White),
            };
            var label = SubtleText();

            stack.Children.Add(image);
            stack.Children.Add(timer);
            stack.Children.Add(label);

            string? loadedIcon = null;

            _tileRefreshers.Add(() =>
            {
                // The Oil Rig crate countdown is not an event dock entry: it comes from the
                // Logic Engine's hack timers, and only exists when a rule can start one.
                if (tile.EventKey == "oilrig")
                {
                    var host = Application.Current?.MainWindow as Views.MainWindow;
                    var running = host?.DockOilRigTimers ?? Array.Empty<(string Rig, TimeSpan Left)>();
                    SetIcon("pack://application:,,,/Assets/icons/crate.png");
                    label.Text = Loc.Text("OilRigCrateStatus", "Oil Rig crate");

                    if (running.Count > 0)
                    {
                        var (rig, left) = running[0];
                        timer.Text = $"{(int)left.TotalMinutes}:{left.Seconds:D2}";
                        shell.Opacity = 1.0;
                        ToolTipService.SetToolTip(shell, $"{rig}: {timer.Text}");
                        return;
                    }
                }

                var ev = DockHost?.DockEvents.FirstOrDefault(e => e.Key == tile.EventKey);
                if (ev == null)
                {
                    timer.Text = "—";
                    shell.Opacity = 0.5;
                    if (tile.EventKey != "oilrig") label.Text = tile.EventKey ?? "";
                    return;
                }

                SetIcon(ev.Icon);
                label.Text = Abbreviate(ev.Name, 12);
                timer.Text = string.IsNullOrWhiteSpace(ev.TimerText) ? "—" : ev.TimerText;
                timer.Foreground = ev.Active
                    ? Brush("TextPrimary", Colors.White)
                    : Brush("TextSubtle", Colors.Gray);
                shell.Opacity = ev.Active ? 1.0 : 0.55;
                ToolTipService.SetToolTip(shell, ev.ToolTip ?? ev.Name);

                void SetIcon(string uri)
                {
                    if (loadedIcon == uri || string.IsNullOrEmpty(uri)) return;
                    try { image.Source = new BitmapImage(new Uri(uri)); loadedIcon = uri; }
                    catch { /* a missing pack icon must not kill the tick */ }
                }
            });

            return shell;
        }

        // ── Logic Engine rule ───────────────────────────────────────────────────

        private FrameworkElement BuildRuleTile(CommandDockTile tile)
        {
            var shell = TileShell();
            shell.Tag = tile;
            shell.Cursor = Cursors.Hand;

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            shell.Child = stack;

            var image = new Image
            {
                Width = 24,
                Height = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 3, 3),
                Visibility = Visibility.Collapsed,
            };
            var glyph = new TextBlock
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Text = "\uE945",              // lightning bolt: the rule launcher
                FontSize = 17,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 3),
                Foreground = Brush("Accent", Color.FromRgb(0x3F, 0xD7, 0xFF)),
            };
            var name = SubtleText();

            stack.Children.Add(image);
            stack.Children.Add(glyph);
            stack.Children.Add(name);

            shell.MouseLeftButtonUp += (_, e) =>
            {
                if (_isEditMode) return;
                e.Handled = true;
                if (!CanRunRule(tile, out string? _)) return;
                if (tile.RuleId != null) DockHost?.RunDockRule(tile.RuleId);
            };

            _tileRefreshers.Add(() =>
            {
                var rule = DockHost?.DockRules.FirstOrDefault(r => r.Id == tile.RuleId);
                name.Text = Abbreviate(rule?.Name ?? tile.RuleId, 12);

                if (rule?.CustomIcon != null)
                {
                    image.Source = rule.CustomIcon;
                    image.Visibility = Visibility.Visible;
                    glyph.Visibility = Visibility.Collapsed;
                }
                else
                {
                    image.Visibility = Visibility.Collapsed;
                    glyph.Visibility = Visibility.Visible;
                }

                bool runnable = CanRunRule(tile, out var why);
                shell.Opacity = runnable ? 1.0 : 0.4;
                shell.Cursor = runnable ? Cursors.Hand : Cursors.No;
                ToolTipService.SetToolTip(shell, why ?? rule?.Name ?? "");
            });

            return shell;
        }

        /// <summary>
        /// Whether the tile can start its rule, and if not, the reason in the user's language.
        /// A greyed tile that does not say why is a bug report waiting to happen.
        /// </summary>
        private bool CanRunRule(CommandDockTile tile, out string? reason)
        {
            reason = null;
            var rule = DockHost?.DockRules.FirstOrDefault(r => r.Id == tile.RuleId);

            if (rule == null)
            {
                reason = Loc.Text("CommandDockRuleMissing", "This rule no longer exists.");
                return false;
            }
            if (rule.TriggerType != "CommandDock")
            {
                reason = Loc.Text("CommandDockRuleWrongTrigger",
                    "This rule no longer uses the Command Dock trigger.");
                return false;
            }
            if (DockHost?.IsDockLogicEngineActive != true || !rule.IsEnabled)
            {
                reason = Loc.Text("CommandDockRuleInactive", "Activate Logic Engine Mechanics or Rule");
                return false;
            }
            return true;
        }

        // ── Chat ────────────────────────────────────────────────────────────────

        private FrameworkElement BuildChatTile(CommandDockTile tile, bool clan)
        {
            // Chat needs room for a line of text; one cell would only ever show an ellipsis.
            if (tile.ColSpan < 2) tile.ColSpan = 2;
            if (tile.RowSpan < 2) tile.RowSpan = 2;

            var shell = TileShell();
            shell.Tag = tile;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            shell.Child = grid;

            var header = new TextBlock
            {
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = Brush("Accent", Color.FromRgb(0x3F, 0xD7, 0xFF)),
                Text = clan ? Loc.Text("ClanChat", "Clan chat") : Loc.Text("TeamChat", "Team chat"),
            };
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var lines = new StackPanel();
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = lines,
            };
            Grid.SetRow(scroll, 1);
            grid.Children.Add(scroll);

            int lastCount = -1;
            string lastSignature = "";

            _tileRefreshers.Add(() =>
            {
                var feed = DockHost?.GetDockChat(clan, 30) ?? Array.Empty<CommandDockChatLine>();
                var signature = feed.Count == 0 ? "" : $"{feed.Count}|{feed[^1].Time}|{feed[^1].Message}";
                if (feed.Count == lastCount && signature == lastSignature) return;
                lastCount = feed.Count;
                lastSignature = signature;

                lines.Children.Clear();
                foreach (var line in feed)
                {
                    var para = new TextBlock
                    {
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 2),
                    };
                    para.Inlines.Add(new System.Windows.Documents.Run(
                        tile.ChatAbbreviateNames ? Abbreviate(line.Author, 10) : line.Author)
                    {
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Brush("Accent", Color.FromRgb(0x3F, 0xD7, 0xFF)),
                    });
                    para.Inlines.Add(new System.Windows.Documents.Run("  " + line.Message)
                    {
                        Foreground = Brush("TextPrimary", Colors.White),
                    });
                    ToolTipService.SetToolTip(para, $"{line.Author} · {line.Time}");
                    lines.Children.Add(para);
                }

                scroll.ScrollToEnd();
            });

            return shell;
        }

        // ── Edit mode ───────────────────────────────────────────────────────────

        private void BtnAddTile_Click(object sender, RoutedEventArgs e)
        {
            SetEditMode(true);
            ShowPicker();
        }

        private void SetEditMode(bool on)
        {
            if (_isEditMode == on) return;
            _isEditMode = on;
            ApplyEditModeChrome();
        }

        private void ApplyEditModeChrome()
        {
            foreach (var el in _tileElements.Values)
            {
                if (el is not Border border) continue;

                border.Cursor = _isEditMode ? Cursors.SizeAll : Cursors.Arrow;
                if (_isEditMode)
                    border.BorderBrush = Brush("Accent", Color.FromRgb(0x3F, 0xD7, 0xFF));
            }

            foreach (var grip in _tileGrips.Values)
                grip.Visibility = _isEditMode ? Visibility.Visible : Visibility.Collapsed;
        }

        private readonly Dictionary<string, FrameworkElement> _tileGrips = new();

        /// <summary>
        /// The corner handle that changes a tile's cell span in edit mode.
        ///
        /// It is wrapped into the tile after the tile built its own content, so every kind gets
        /// one without each builder having to make room for it. Chat keeps a floor of two cells
        /// wide — below that a message is nothing but an ellipsis.
        /// </summary>
        private void AddResizeGrip(Border shell, CommandDockTile tile)
        {
            var content = shell.Child;
            var host = new Grid();
            shell.Child = null;
            if (content != null) host.Children.Add(content);

            var grip = new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, -4, -4),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = Brush("Accent", Color.FromRgb(0x3F, 0xD7, 0xFF)),
                Cursor = Cursors.SizeNWSE,
                Visibility = _isEditMode ? Visibility.Visible : Visibility.Collapsed,
            };
            // The edit hints live on the grip, not the tile: the tile's own tooltip is live data
            // that its refresher rewrites every second, and would swallow anything set here.
            ToolTipService.SetToolTip(grip,
                Loc.Text("CommandDockResizeHint", "Drag to resize") + "\n" +
                Loc.Text("CommandDockEditHint", "Drag to move · right-click to remove"));

            bool sizing = false;
            Point start = default;
            int startCols = tile.ColSpan, startRows = tile.RowSpan;

            grip.MouseLeftButtonDown += (_, e) =>
            {
                sizing = true;
                start = e.GetPosition(DockCanvas);
                startCols = tile.ColSpan;
                startRows = tile.RowSpan;
                grip.CaptureMouse();
                e.Handled = true;
            };

            grip.MouseMove += (_, e) =>
            {
                if (!sizing) return;
                var now = e.GetPosition(DockCanvas);
                double step = CommandDockLayout.CellSize + CommandDockLayout.CellGap;

                int minCols = tile.Kind is CommandDockTileKinds.TeamChat or CommandDockTileKinds.ClanChat ? 2 : 1;
                tile.ColSpan = Math.Max(minCols, startCols + (int)Math.Round((now.X - start.X) / step));
                tile.RowSpan = Math.Max(1, startRows + (int)Math.Round((now.Y - start.Y) / step));

                var rect = CellRect(tile);
                shell.Width = rect.Width;
                shell.Height = rect.Height;
                e.Handled = true;
            };

            grip.MouseLeftButtonUp += (_, e) =>
            {
                if (!sizing) return;
                sizing = false;
                grip.ReleaseMouseCapture();
                e.Handled = true;

                if (Overlaps(tile)) AssignFreeCell(tile);
                SaveDock();
                RebuildTiles();
            };

            host.Children.Add(grip);
            shell.Child = host;
            _tileGrips[tile.Id] = grip;
        }

        /// <summary>
        /// The mouse handling every tile needs, attached once per tile after its own handlers.
        ///
        /// A press on a tile is always swallowed. The window drags itself on MouseLeftButtonDown
        /// and DragMove blocks until the button comes back up, eating the MouseUp with it — so a
        /// tile that did not stop the press would never see the click that toggles its switch.
        /// </summary>
        private void AttachTileInteraction(Border border, string tileId)
        {
            Point grabOffset = default;
            bool dragging = false;

            border.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;   // never let the press reach the window's DragMove
                if (!_isEditMode) return;

                grabOffset = e.GetPosition(border);
                dragging = true;
                border.CaptureMouse();
            };

            border.MouseMove += (_, e) =>
            {
                if (!dragging) return;
                var p = e.GetPosition(DockCanvas);
                Canvas.SetLeft(border, p.X - grabOffset.X);
                Canvas.SetTop(border, p.Y - grabOffset.Y);
            };

            border.MouseLeftButtonUp += (_, e) =>
            {
                // Also swallowed when not dragging: otherwise the release bubbles to the window
                // and re-centres the map behind the tile the user just pressed.
                e.Handled = true;
                if (!dragging) return;

                dragging = false;
                border.ReleaseMouseCapture();
                SnapTileToGrid(tileId, border);
            };

            border.MouseRightButtonUp += (_, e) =>
            {
                if (!_isEditMode) return;
                e.Handled = true;
                RemoveTile(tileId);
            };
        }

        /// <summary>Turns a dropped pixel position back into the nearest free cell.</summary>
        private void SnapTileToGrid(string tileId, FrameworkElement el)
        {
            var tile = _dock.Tiles.FirstOrDefault(t => t.Id == tileId);
            if (tile == null) return;

            var (mapCols, mapRows) = MapCellSpan();
            double x = Canvas.GetLeft(el), y = Canvas.GetTop(el);

            tile.Col = PixelToCell(x, _mapWidth, mapCols);
            tile.Row = PixelToCell(y, _mapHeight, mapRows);

            // A drop onto occupied cells or onto the map falls back to the first free spot,
            // which is what makes the dock behave like desktop icons rather than a free canvas.
            if (Overlaps(tile)) AssignFreeCell(tile);

            SaveDock();
            RebuildTiles();

            static int PixelToCell(double pixels, double mapExtent, int mapCells)
            {
                if (pixels < mapExtent)
                    return Math.Max(0, (int)Math.Round(pixels / (CommandDockLayout.CellSize + CommandDockLayout.CellGap)));

                double past = pixels - mapExtent - CommandDockLayout.CellGap;
                return mapCells + Math.Max(0, (int)Math.Round(past / (CommandDockLayout.CellSize + CommandDockLayout.CellGap)));
            }
        }

        private bool Overlaps(CommandDockTile tile)
        {
            var (mapCols, mapRows) = MapCellSpan();

            for (int c = tile.Col; c < tile.Col + tile.ColSpan; c++)
                for (int r = tile.Row; r < tile.Row + tile.RowSpan; r++)
                {
                    if (c < mapCols && r < mapRows) return true;

                    foreach (var other in _dock.Tiles)
                    {
                        if (other == tile) continue;
                        if (c >= other.Col && c < other.Col + other.ColSpan &&
                            r >= other.Row && r < other.Row + other.RowSpan) return true;
                    }
                }

            return false;
        }

        private void RemoveTile(string tileId)
        {
            _dock.Tiles.RemoveAll(t => t.Id == tileId);
            SaveDock();
            RebuildTiles();
        }

        public void AddTile(CommandDockTile tile)
        {
            AssignFreeCell(tile);
            _dock.Tiles.Add(tile);
            SaveDock();
            RebuildTiles();
        }

        private void ShowPicker()
        {
            // A closed Window cannot be shown again, so the old instance is dropped rather than
            // reused — Show() on it throws instead of reopening the picker.
            if (_picker == null)
            {
                _picker = new CommandDockTilePicker { Owner = this };
                _picker.OnPicked = AddTile;
                _picker.OnClosed = () => { _picker = null; SetEditMode(false); };
            }

            _picker.Host = DockHost;
            _picker.Refresh();
            _picker.Show();
            _picker.Activate();
        }
    }
}
