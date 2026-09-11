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

            InitArming();

            Loaded += (_, __) => { RebuildTiles(); _dockTimer.Start(); };
            Closed += (_, __) =>
            {
                _dockTimer?.Stop();
                _armTimer?.Stop();
                _disarmTimer?.Stop();
                SaveDockPosition();
            };
        }

        private void SaveDock() => StorageService.SaveCache(DockCacheKey, _dock);

        // ── Dock position ───────────────────────────────────────────────────────

        private void SaveDockPosition()
        {
            if (double.IsNaN(Left) || double.IsNaN(Top)) return;

            _dock.WindowLeft = Left;
            _dock.WindowTop = Top;
            SaveDock();
        }

        /// <summary>
        /// Puts the dock back where it was left. Silently skipped the first time, so a fresh
        /// install still gets the top-right corner the main window picks for it.
        /// </summary>
        private void RestoreDockPosition()
        {
            if (_dock.WindowLeft is not { } left || _dock.WindowTop is not { } top) return;

            double dLeft = left - Left, dTop = top - Top;
            Left = left;
            Top = top;
            ClampToScreen();
            HoldSettingsPopupInPlace(dLeft, dTop);
        }

        // ── Arming ──────────────────────────────────────────────────────────────

        // Long enough that crossing a tile on the way somewhere else does not arm it, short
        // enough that deliberately resting on one does.
        private static readonly TimeSpan ArmDelay = TimeSpan.FromMilliseconds(600);

        // Kept armed after the pointer leaves, so several tiles can be changed in a row without
        // waiting out the delay between each.
        private static readonly TimeSpan DisarmGrace = TimeSpan.FromSeconds(2.5);

        private DispatcherTimer? _armTimer;
        private DispatcherTimer? _disarmTimer;
        private bool _armed;
        private string? _hoveredTileId;

        private void InitArming()
        {
            _armTimer = new DispatcherTimer { Interval = ArmDelay };
            _armTimer.Tick += (_, __) => { _armTimer!.Stop(); SetArmed(true); };

            _disarmTimer = new DispatcherTimer { Interval = DisarmGrace };
            _disarmTimer.Tick += (_, __) => { _disarmTimer!.Stop(); SetArmed(false); };

            MouseEnter += (_, __) =>
            {
                _disarmTimer?.Stop();
                if (!_armed) _armTimer?.Start();
            };

            MouseLeave += (_, __) =>
            {
                _armTimer?.Stop();
                _hoveredTileId = null;
                UpdateTileHandles();
                if (_armed) _disarmTimer?.Start();
            };
        }

        private void SetArmed(bool armed)
        {
            if (_armed == armed) return;
            _armed = armed;

            FadeTitleBar(armed);
            UpdateTileHandles();
        }

        private void FadeTitleBar(bool show)
        {
            if (DockTitleBar == null) return;

            // Opacity 0 does not stop a WPF element from taking the mouse. Left hit-testable,
            // the invisible bar would swallow every click on the top 30 pixels of whatever tile
            // sits under it — so the two are switched together.
            DockTitleBar.IsHitTestVisible = show;

            var fade = new DoubleAnimation(show ? 1.0 : 0.0, TimeSpan.FromMilliseconds(show ? 120 : 450))
            {
                FillBehavior = FillBehavior.HoldEnd,
            };
            DockTitleBar.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        /// <summary>
        /// Handles belong to the hovered tile, and only while the dock is armed. Both conditions
        /// change independently, so one place decides and everything else just calls it.
        /// </summary>
        private void UpdateTileHandles()
        {
            foreach (var (tileId, handles) in _tileHandles)
            {
                var show = _armed && tileId == _hoveredTileId;
                foreach (var handle in handles)
                    handle.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // ── Geometry ────────────────────────────────────────────────────────────

        /// <summary>How many cells the map tile covers, so tiles never land underneath it.</summary>
        private (int Cols, int Rows) MapCellSpan() =>
            (CommandDockLayout.PixelsToCells(_mapWidth), CommandDockLayout.PixelsToCells(_mapHeight));

        /// <summary>
        /// A tile's pixel rect. Cell (0,0) is the map tile's top-left; cells past the map's own
        /// span continue after it, and negative cells run left of and above it. So the grid
        /// stays whole while the map changes size, and a bar can be built on any side.
        /// </summary>
        private Rect CellRect(CommandDockTile tile)
        {
            var (mapCols, mapRows) = MapCellSpan();

            double x = tile.Col >= mapCols
                ? _mapWidth + CommandDockLayout.CellGap + CommandDockLayout.CellOffset(tile.Col - mapCols)
                : CommandDockLayout.CellOffset(tile.Col);

            double y = tile.Row >= mapRows
                ? _mapHeight + CommandDockLayout.CellGap + CommandDockLayout.CellOffset(tile.Row - mapRows)
                : CommandDockLayout.CellOffset(tile.Row);

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
        /// Places a new tile like a desktop icon: the first free run of cells, scanned in the
        /// direction the dock is set to grow, with the other direction as the fallback once that
        /// side is full. Only auto-placement follows the setting — a drag reaches any cell,
        /// including the ones left of and above the map.
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

            // Below the map by default: it keeps the dock as narrow as the map, which is what
            // sits well beside a game. Growing to the right is the opt-in, for a quickbar.
            bool right = _dock.GrowRight;

            if (right)
            {
                for (int col = mapCols; col < mapCols + 8; col++)
                    for (int row = 0; row < mapRows + 8; row++)
                        if (Fits(col, row)) { tile.Col = col; tile.Row = row; return; }
            }

            for (int row = mapRows; row < mapRows + 16; row++)
                for (int col = 0; col < Math.Max(mapCols, 1) + 8; col++)
                    if (Fits(col, row)) { tile.Col = col; tile.Row = row; return; }

            if (!right)
            {
                for (int col = mapCols; col < mapCols + 8; col++)
                    for (int row = 0; row < mapRows + 8; row++)
                        if (Fits(col, row)) { tile.Col = col; tile.Row = row; return; }
            }

            tile.Col = right ? mapCols : 0;
            tile.Row = right ? 0 : mapRows;
        }

        // ── Building ────────────────────────────────────────────────────────────

        private void RebuildTiles()
        {
            foreach (var el in _tileElements.Values)
                DockCanvas.Children.Remove(el);

            _tileElements.Clear();
            _tileRefreshers.Clear();
            _tileHandles.Clear();
            _hoveredTileId = null;

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

        // ── Adding and arranging ────────────────────────────────────────────────

        private void BtnAddTile_Click(object sender, RoutedEventArgs e) => ShowPicker();

        /// <summary>
        /// The corner handle that changes a tile's cell span, and the button that removes the
        /// tile. Both appear on the hovered tile once the dock has armed — see the arming timers
        /// above: the delay is what stops a cursor passing over a switch from putting a delete
        /// button under it.
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
                Visibility = Visibility.Collapsed,
            };

            // Removing is a button, not a right-click. Right-click still pans the map, and a
            // tile that vanished because the cursor happened to be over it would be worse than
            // any amount of saved pixels.
            var remove = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, -6, -6, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)),
                Cursor = Cursors.Hand,
                Visibility = Visibility.Collapsed,
                Child = new TextBlock
                {
                    Text = "\uE711",  // cancel
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 9,
                    Foreground = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            ToolTipService.SetToolTip(remove, Loc.Text("CommandDockRemoveTile", "Remove tile"));
            remove.PreviewMouseLeftButtonDown += (_, e) => e.Handled = true;
            remove.MouseLeftButtonUp += (_, e) => { e.Handled = true; RemoveTile(tile.Id); };

            // A veil rather than a border tint: the device and alarm refreshers rewrite the
            // shell's BorderBrush every second and would wipe a hover colour straight off again.
            // It is also what shows a press landed, which a tile that only toggles a switch
            // somewhere else on screen otherwise never acknowledges.
            var veil = new Border
            {
                CornerRadius = shell.CornerRadius,
                Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                Opacity = 0,
                IsHitTestVisible = false,
            };

            void Veil(double to, int ms) => veil.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { FillBehavior = FillBehavior.HoldEnd });

            shell.MouseEnter += (_, __) =>
            {
                _hoveredTileId = tile.Id;
                UpdateTileHandles();
                Veil(0.07, 120);
            };

            shell.MouseLeave += (_, __) =>
            {
                if (_hoveredTileId == tile.Id) _hoveredTileId = null;
                UpdateTileHandles();
                Veil(0, 160);
            };

            shell.PreviewMouseLeftButtonDown += (_, __) => Veil(0.18, 40);
            shell.PreviewMouseLeftButtonUp += (_, __) => Veil(shell.IsMouseOver ? 0.07 : 0, 220);
            // The edit hints live on the grip, not the tile: the tile's own tooltip is live data
            // that its refresher rewrites every second, and would swallow anything set here.
            ToolTipService.SetToolTip(grip,
                Loc.Text("CommandDockResizeHint", "Drag to resize") + "\n" +
                Loc.Text("CommandDockEditHint", "Drag to move · × to remove"));

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

            // Veil under the handles, so the grip and the × stay at full strength on a pressed
            // tile; both above the content, so they are never hidden behind a chat line.
            host.Children.Add(veil);
            host.Children.Add(grip);
            host.Children.Add(remove);
            shell.Child = host;

            // The tile's drag handler consults these so a press that started on one of them is
            // not turned into a tile drag.
            _tileHandles[tile.Id] = new[] { (FrameworkElement)grip, remove };
        }

        private readonly Dictionary<string, FrameworkElement[]> _tileHandles = new();

        private bool PressedOnHandle(string tileId, object? originalSource)
        {
            if (!_tileHandles.TryGetValue(tileId, out var handles)) return false;

            for (var node = originalSource as DependencyObject; node != null;)
            {
                if (node is FrameworkElement fe && Array.IndexOf(handles, fe) >= 0) return true;
                node = node is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(node)
                    : (node as FrameworkContentElement)?.Parent;
            }

            return false;
        }

        /// <summary>Below this many pixels a press is a click, above it a drag.</summary>
        private const double DragThreshold = 5;

        /// <summary>
        /// Moving, resizing and removing work whenever the tile is hovered — there is no mode to
        /// enter. Which means one press has to serve both a click and a drag, so it is only a
        /// drag once the mouse has actually travelled: a switch tile still toggles on a plain
        /// click, and nudging it a few pixels no longer swallows the toggle.
        ///
        /// The press is always swallowed either way. The window drags itself on
        /// MouseLeftButtonDown and DragMove blocks until the button comes back up, eating the
        /// MouseUp with it — a tile that let the press through would never see its own click.
        /// </summary>
        private void AttachTileInteraction(Border border, string tileId)
        {
            Point grabOffset = default;
            bool pressed = false;
            bool dragging = false;

            border.PreviewMouseLeftButtonDown += (_, e) =>
            {
                // The resize grip and the remove button are children of this border, so this
                // tunnelling handler sees their presses first. Handling one here would suppress
                // their own bubbling handlers entirely and neither would ever work.
                if (PressedOnHandle(tileId, e.OriginalSource)) return;

                pressed = true;
                dragging = false;
                grabOffset = e.GetPosition(border);
                border.CaptureMouse();
                e.Handled = true;   // never let the press reach the window's DragMove
            };

            border.PreviewMouseMove += (_, e) =>
            {
                if (!pressed) return;

                var p = e.GetPosition(DockCanvas);

                if (!dragging)
                {
                    var moved = e.GetPosition(border) - grabOffset;
                    if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold) return;
                    dragging = true;
                    Panel.SetZIndex(border, 1000);   // over its neighbours while it travels
                }

                Canvas.SetLeft(border, p.X - grabOffset.X);
                Canvas.SetTop(border, p.Y - grabOffset.Y);
            };

            border.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (!pressed) return;
                pressed = false;
                border.ReleaseMouseCapture();

                if (!dragging) return;   // a click: let the tile's own handler have it

                dragging = false;
                Panel.SetZIndex(border, 0);
                e.Handled = true;        // a drag must not also toggle the switch it landed on
                SnapTileToGrid(tileId, border);
            };

            // Registered with handledEventsToo: the tile's own click handler has already marked
            // the release handled, and this still has to stop it reaching the window, where it
            // would re-centre the map behind the tile.
            border.AddHandler(UIElement.MouseLeftButtonUpEvent,
                new MouseButtonEventHandler((_, e) => e.Handled = true), true);

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
                double step = CommandDockLayout.CellSize + CommandDockLayout.CellGap;

                // Negative cells are allowed: they are how a bar gets built along the left edge
                // or across the top, which the map's own footprint would otherwise block.
                if (pixels < mapExtent)
                    return (int)Math.Round(pixels / step);

                double past = pixels - mapExtent - CommandDockLayout.CellGap;
                return mapCells + Math.Max(0, (int)Math.Round(past / step));
            }
        }

        private bool Overlaps(CommandDockTile tile)
        {
            var (mapCols, mapRows) = MapCellSpan();

            for (int c = tile.Col; c < tile.Col + tile.ColSpan; c++)
                for (int r = tile.Row; r < tile.Row + tile.RowSpan; r++)
                {
                    // The map's own footprint. Both bounds matter: a negative cell is left of
                    // or above the map, not on it, and dropping there has to be allowed.
                    if (c >= 0 && c < mapCols && r >= 0 && r < mapRows) return true;

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

        /// <summary>Which way auto-placement grows. Down is the default; right is the quickbar.</summary>
        public bool DockGrowsRight
        {
            get => _dock.GrowRight;
            set
            {
                if (_dock.GrowRight == value) return;
                _dock.GrowRight = value;
                SaveDock();
            }
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
                _picker.OnClosed = () => _picker = null;
            }

            _picker.Host = DockHost;
            _picker.Refresh();
            _picker.Show();
            _picker.Activate();
        }
    }
}
