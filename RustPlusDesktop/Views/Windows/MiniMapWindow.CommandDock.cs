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
    /// The mini-map's Widget Overlay: the map is one tile on a cell grid, and the rest of the
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

            // Layouts written before the map became a tile have none, and their tiles were
            // positioned relative to a map pinned at the origin — so putting it at (0,0) leaves
            // every one of them exactly where it was.
            if (MapTile == null && !_dock.MapRemoved)
            {
                _dock.Tiles.Insert(0, new CommandDockTile
                {
                    Id = CommandDockTileKinds.MapTileId,
                    Kind = CommandDockTileKinds.Map,
                    Col = 0,
                    Row = 0,
                });
            }

            _dockTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _dockTimer.Tick += (_, __) => RefreshTiles();

            // The overlay is the dock's, and only exists while the dock does: it draws the grid
            // the dock is measured against and carries the bar that arranges it.
            IsVisibleChanged += (_, __) =>
            {
                if (IsVisible)
                {
                    Overlay();
                    StartEdgeWatch();
                }
                else
                {
                    StopEdgeWatch();
                    _overlay?.ShowBar(false);
                    _overlay?.ClearGrid();
                }
            };

            // A full-screen transparent window left behind would sit over the game forever.
            Closed += (_, __) => CloseOverlay();

            // The dock can be dragged to another monitor, and the overlay has to follow it -
            // a grid painted on the screen the dock used to be on describes nothing.
            //
            // Size as well as position: which monitor the dock is mostly on can change by it
            // growing, without its corner moving at all.
            LocationChanged += (_, __) => PositionOverlay();
            SizeChanged += (_, __) => PositionOverlay();

            InitArming();

            Loaded += (_, __) => { ApplyLockState(); RebuildTiles(); _dockTimer.Start(); };
            Closed += (_, __) =>
            {
                _dockTimer?.Stop();
                _armTimer?.Stop();
                _disarmTimer?.Stop();
                _hoverHideTimer?.Stop();
                _settingsApplyTimer?.Stop();
                CloseAiAnswer();
                SaveDockPosition();
            };
        }

        private void SaveDock() => StorageService.SaveCache(DockCacheKey, _dock);

        /// <summary>
        /// Re-reads the layout from disk and redraws. Called when the main settings change the
        /// dock's defaults, so the two views of one file cannot disagree.
        /// </summary>
        public void ReloadDockLayout()
        {
            var reloaded = StorageService.LoadCache<CommandDockLayout>(DockCacheKey);
            if (reloaded == null) return;

            _dock = reloaded;

            if (MapTile == null && !_dock.MapRemoved)
                _dock.Tiles.Insert(0, new CommandDockTile { Id = CommandDockTileKinds.MapTileId, Kind = CommandDockTileKinds.Map });

            CloseTileSettings();
            ApplyLockState();
            RebuildTiles();
        }

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
            // Coming back from a saved position: a monitor may have been unplugged or the
            // resolution changed since, so the dock has to be pulled somewhere visible.
            ClampToScreen(pullIntoView: true);
            HoldSettingsPopupInPlace(dLeft, dTop);
            FollowAiAnswer();
        }

        // ── Arming ──────────────────────────────────────────────────────────────

        // Long enough that crossing a tile on the way somewhere else does not arm it, short
        // enough that deliberately resting on one does.
        private static readonly TimeSpan ArmDelay = TimeSpan.FromMilliseconds(600);

        // Kept armed after the pointer leaves, so several tiles can be changed in a row without
        // waiting out the delay between each.
        private static readonly TimeSpan DisarmGrace = TimeSpan.FromSeconds(2.5);

        // The handles sit on the tile's bounding box, which for a round map is a square whose
        // corners are not part of the map and take no mouse. Reaching the gear therefore means
        // crossing dead space, and hiding the moment the pointer left would take the button away
        // before it could be pressed.
        private static readonly TimeSpan HoverHideDelay = TimeSpan.FromMilliseconds(700);

        private DispatcherTimer? _armTimer;
        private DispatcherTimer? _disarmTimer;
        private DispatcherTimer? _hoverHideTimer;
        private bool _armed;
        private string? _hoveredTileId;

        /// <summary>Marks a tile hovered and cancels any pending hide.</summary>
        private void HoverTile(string tileId)
        {
            _hoverHideTimer?.Stop();
            _hoveredTileId = tileId;
            UpdateTileHandles();
        }

        /// <summary>Starts the grace period before the hovered tile gives up its handles.</summary>
        private void UnhoverTile(string? tileId = null)
        {
            if (tileId != null && _hoveredTileId != tileId) return;

            _hoverHideTimer ??= CreateHoverHideTimer();
            _hoverHideTimer.Stop();
            _hoverHideTimer.Start();
        }

        private DispatcherTimer CreateHoverHideTimer()
        {
            var timer = new DispatcherTimer { Interval = HoverHideDelay };
            timer.Tick += (_, __) =>
            {
                timer.Stop();
                _hoveredTileId = null;
                UpdateTileHandles();
            };
            return timer;
        }

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
                UnhoverTile();
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

        /// <summary>
        /// The dock's own title bar is gone - the overlay carries it, at the top of the screen.
        ///
        /// The element is still in the XAML and still holds nothing: kept so the drag handler
        /// and the tutorial anchors that name it keep resolving, and permanently hidden here
        /// rather than deleted in a dozen places. Hit testing goes off with it, because an
        /// invisible bar would otherwise swallow every click on the dock's top 30 pixels.
        /// </summary>
        private void FadeTitleBar(bool show)
        {
            if (DockTitleBar == null) return;

            DockTitleBar.IsHitTestVisible = false;
            DockTitleBar.Opacity = 0;
        }

        /// <summary>
        /// Handles belong to the hovered tile, while the dock is armed, and only when it is
        /// unlocked. Three conditions that change independently, so one place decides and
        /// everything else just calls it.
        /// </summary>
        private void UpdateTileHandles()
        {
            foreach (var (tileId, handles) in _tileHandles)
            {
                var show = _armed && !_dock.Locked && tileId == _hoveredTileId;
                foreach (var handle in handles)
                    handle.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // ── Lock ────────────────────────────────────────────────────────────────

        private void BtnLockDock_Click(object sender, RoutedEventArgs e)
        {
            _dock.Locked = !_dock.Locked;
            SaveDock();

            if (_dock.Locked) CloseTileSettings();

            ApplyLockState();

            // Unlocking shows the tiles that only exist in certain states — the death tile is
            // hidden while alive, and hidden exactly while somebody is trying to place it is
            // no use to them. Locking hides them again.
            RebuildTiles();
            UpdateTileHandles();
        }

        /// <summary>Puts the lock button into the state it is actually in.</summary>
        private void ApplyLockState()
        {
            bool locked = _dock.Locked;

            // The bar on the overlay carries the same lock, so it is repainted here rather than
            // by whoever happened to flip it.
            _overlay?.SetLocked(locked);

            // Unlocked means somebody is arranging: the bar stays out for as long as that lasts,
            // because it is the only way back to locking it.
            if (!locked) _overlay?.ShowBar(true);

            // ...and while arranging, both windows take focus like ordinary ones. Never taking
            // it is right for using the dock over a running game and wrong for building it.
            SetEditModeFocus(!locked);

            if (LockGlyph == null || BtnLockDock == null) return;

            LockGlyph.Text = locked ? "\uE72E" : "\uE785";   // closed / open padlock
            LockGlyph.Foreground = locked
                ? System.Windows.Media.Brushes.White
                : Brush("Accent", Color.FromRgb(0x3F, 0xD7, 0xFF));

            ToolTipService.SetToolTip(BtnLockDock, locked
                ? Loc.Text("CommandDockUnlockHint", "Unlock to move, resize and remove tiles")
                : Loc.Text("CommandDockLockHint", "Lock the layout"));
        }

        // ── Geometry ────────────────────────────────────────────────────────────

        /// <summary>The map's entry in the layout, or null when it has been removed.</summary>
        private CommandDockTile? MapTile =>
            _dock.Tiles.FirstOrDefault(t => t.Kind == CommandDockTileKinds.Map);

        /// <summary>
        /// Whether the map takes up space in the grid.
        ///
        /// A map with every layer switched off draws nothing at all, so leaving a hole in the
        /// dock the size of a map nobody can see would be absurd — it counts as removed until a
        /// layer comes back on.
        /// </summary>
        private bool MapOccupiesCells => MapTile != null && _mapLayersOn;

        private bool _mapLayersOn = true;
        private string? _lastServerKey;

        /// <summary>How many cells the map's free pixel size needs.</summary>
        private (int Cols, int Rows) MapCellSpan() => MapOccupiesCells
            ? (CommandDockLayout.PixelsToCells(_mapWidth, DockZoom), CommandDockLayout.PixelsToCells(_mapHeight, DockZoom))
            : (0, 0);

        /// <summary>
        /// The left edge of a column. The grid is uniform, everywhere, always.
        ///
        /// It used to have a seam. The map keeps a free pixel size but reserves whole cells, and
        /// its allotment is a little bigger than it is, so every column past the map shifted by
        /// that difference to close the gap - at every row, not just beside the map, or a tile
        /// underneath would have fallen out of line with the one above it.
        ///
        /// That made the grid depend on a pixel value the size slider changes continuously: the
        /// snap points moved as the map was resized, and the grid drawn during a drag could not
        /// show where a tile would actually land. The map now sits over the grid instead of
        /// displacing it - it still snaps its own corner to a cell and still reserves cells so
        /// nothing lands underneath it, but it no longer moves anything else.
        ///
        /// The cost is the gap the seam used to close: up to one cell between the map's edge and
        /// the next tile, since the reservation rounds up. That one is predictable and can be
        /// closed by choosing the map's size; a grid that moved could not be.
        /// </summary>
        private double CellX(int col) => CommandDockLayout.CellOffset(col, DockZoom);

        private double CellY(int row) => CommandDockLayout.CellOffset(row, DockZoom);

        /// <summary>A tile's pixel rect. The map is the one tile whose size is not cell-derived.</summary>
        private Rect CellRect(CommandDockTile tile)
        {
            double x = CellX(tile.Col);
            double y = CellY(tile.Row);

            if (tile.Kind == CommandDockTileKinds.Map)
                return new Rect(x, y, _mapWidth, _mapHeight);

            return new Rect(x, y,
                CommandDockLayout.CellsToPixels(tile.ColSpan, DockZoom),
                CommandDockLayout.CellsToPixels(tile.RowSpan, DockZoom));
        }

        /// <summary>
        /// The tiles that currently take up space: everything but a switched-off map and the
        /// device tiles belonging to a server other than the one in front of us.
        /// </summary>
        private IEnumerable<CommandDockTile> VisibleTiles() => _dock.Tiles.Where(BelongsHere);

        /// <summary>
        /// Gives a server to device tiles saved before they had one.
        ///
        /// Every one of them was created on some server, and the one in front of us is by far the
        /// best guess. Leaving them unstamped would mean they stay global and keep reading "not
        /// paired" everywhere else, which is the thing this was meant to stop.
        /// </summary>
        private void StampLegacyDeviceTiles()
        {
            var key = DockHost?.DockServerKey;
            if (key == null) return;

            bool changed = false;
            foreach (var tile in _dock.Tiles)
            {
                if (tile.Kind != CommandDockTileKinds.Device || tile.ServerKey != null) continue;
                tile.ServerKey = key;
                changed = true;
            }

            if (changed) SaveDock();
        }

        private bool BelongsHere(CommandDockTile tile)
        {
            // Collapsed: the button that did it stays, and the map stays unless that button
            // was set to take it too. Everything else is hidden where it stands — no cell is
            // touched, so expanding puts the arrangement back rather than re-flowing it.
            if (IsCollapsed && tile.Kind != CommandDockTileKinds.Collapse)
            {
                if (tile.Kind != CommandDockTileKinds.Map) return false;
                if (CollapseTakesMap) return false;
            }

            // Nothing to read while alive, and a button offering to read it would be a
            // button that does nothing. It takes its cells back on respawn.
            //
            // Unless the dock is unlocked, which means somebody is arranging it: a tile that
            // is invisible exactly while it is being placed cannot be placed at all.
            if (tile.Kind == CommandDockTileKinds.DeathTrack &&
                !tile.DeathTrackAlwaysVisible && !PlayerIsDead && _dock.Locked)
            {
                return false;
            }

            // Nothing on the map to clear, same rule: the cells go back until there is, and
            // unlocking brings the tile out so it can be placed.
            if (tile.Kind == CommandDockTileKinds.DeathWipe &&
                !tile.DeathWipeAlwaysVisible && DeathMarkerCount == 0 && _dock.Locked)
            {
                return false;
            }

            if (tile.Kind == CommandDockTileKinds.Map) return MapOccupiesCells;
            if (tile.ServerKey == null) return true;

            return string.Equals(tile.ServerKey, DockHost?.DockServerKey, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether the dock is collapsed.
        ///
        /// A saved collapsed state with no collapse tile left on the dock is ignored: the tile
        /// is the only way back, and without it every other tile would be hidden for good.
        /// </summary>
        private bool IsCollapsed =>
            _dock.Collapsed && _dock.Tiles.Any(t => t.Kind == CommandDockTileKinds.Collapse);

        /// <summary>Whether the collapse tile in charge was told to take the map with it.</summary>
        private bool CollapseTakesMap =>
            _dock.Tiles.FirstOrDefault(t => t.Kind == CommandDockTileKinds.Collapse)?.CollapseIncludesMap == true;

        /// <summary>
        /// The rectangle the dock's tiles fit into, derived from their cells rather than read
        /// back off the canvas. Reading the canvas was how the layout and the cell grid drifted
        /// apart; now nothing writes a pixel position that is not computed here first.
        /// </summary>
        private Rect CellBounds()
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var tile in VisibleTiles())
            {
                var r = CellRect(tile);
                minX = Math.Min(minX, r.X);
                minY = Math.Min(minY, r.Y);
                maxX = Math.Max(maxX, r.Right);
                maxY = Math.Max(maxY, r.Bottom);
            }

            if (minX > maxX || minY > maxY)
                return new Rect(0, 0, EmptyDockWidth, EmptyDockHeight);

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// Slides every tile so the top-left occupied cell is (0,0).
        ///
        /// Without this the grid can grow into negative cells, and then a pixel position and a
        /// cell coordinate stop being convertible into one another — which is exactly the drift
        /// that made dropped tiles land somewhere other than where they were let go. Hidden tiles
        /// move along so they stay consistent, but do not get a vote on where the corner is.
        /// </summary>
        private void NormaliseCells()
        {
            var visible = VisibleTiles().ToList();
            if (visible.Count == 0) return;

            int minCol = visible.Min(t => t.Col);
            int minRow = visible.Min(t => t.Row);
            if (minCol == 0 && minRow == 0) return;

            foreach (var tile in _dock.Tiles)
            {
                tile.Col -= minCol;
                tile.Row -= minRow;
            }
        }

        /// <summary>Writes every tile's derived pixel position onto the canvas.</summary>
        private void ApplyTilePositions()
        {
            SyncMapCellSpan();

            foreach (var tile in _dock.Tiles)
            {
                if (!_tileElements.TryGetValue(tile.Id, out var el)) continue;

                var rect = CellRect(tile);

                // The map's size comes from the slider, not from its cell span — UpdateSize has
                // already applied it, and writing the cell width here would undo that.
                if (tile.Kind != CommandDockTileKinds.Map)
                {
                    el.Width = rect.Width;
                    el.Height = rect.Height;
                }

                Canvas.SetLeft(el, rect.X);
                Canvas.SetTop(el, rect.Y);
            }
        }

        // ── Drag preview ────────────────────────────────────────────────────────

        private CommandDockTile? _draggingTile;
        private (int Col, int Row)? _dropTarget;

        private void BeginDragPreview(CommandDockTile tile)
        {
            _draggingTile = tile;
            _dropTarget = (tile.Col, tile.Row);

            // _dragPad stays zero now. It used to grow the dock by a cell in every direction so
            // the grid had somewhere to be drawn, with the window moving the opposite way to
            // compensate - and ClampToScreen then undid the compensation at the top of the
            // screen, which is why the dock crept downwards one cell per drag. The grid lives on
            // a window that already covers the screen, so none of that is needed.
            PaintOverlayGrid();
            ShowOverlayDropTarget();
        }

        private void EndDragPreview()
        {
            _draggingTile = null;
            _dropTarget = null;
            ClearOverlayGrid();
        }


        /// <summary>Keeps the map tile's cell span in step with the size the slider gave it.</summary>
        private void SyncMapCellSpan()
        {
            var map = MapTile;
            if (map == null) return;

            var (cols, rows) = MapCellSpan();
            map.ColSpan = Math.Max(1, cols);
            map.RowSpan = Math.Max(1, rows);
        }

        /// <summary>
        /// Places a new tile like a desktop icon: the first free run of cells, scanned in the
        /// direction the dock is set to grow, with the other direction as the fallback once that
        /// side is full. Only auto-placement follows the setting — a drag reaches any cell,
        /// including the ones left of and above the map.
        /// </summary>
        private void AssignFreeCell(CommandDockTile tile) =>
            PlaceInFirstFreeCell(tile, OccupiedCells(except: tile));

        private void PlaceInFirstFreeCell(CommandDockTile tile, HashSet<(int, int)> taken)
        {
            bool Fits(int col, int row)
            {
                for (int c = col; c < col + tile.ColSpan; c++)
                    for (int r = row; r < row + tile.RowSpan; r++)
                        if (taken.Contains((c, r))) return false;
                return true;
            }

            // The map is no longer pinned to the origin, so the search starts from wherever it
            // happens to be — that is the corner everything else is arranged around.
            var map = MapOccupiesCells ? MapTile : null;
            var (mapCols, mapRows) = MapCellSpan();

            int originCol = map?.Col ?? 0;
            int originRow = map?.Row ?? 0;
            int afterCol = originCol + mapCols;
            int afterRow = originRow + mapRows;

            // Below the map by default: it keeps the dock as narrow as the map, which is what
            // sits well beside a game. Growing to the right is the opt-in, for a quickbar.
            bool right = _dock.GrowRight;

            bool TryBeside()
            {
                for (int col = afterCol; col < afterCol + 8; col++)
                    for (int row = originRow; row < afterRow + 8; row++)
                        if (Fits(col, row)) { tile.Col = col; tile.Row = row; return true; }
                return false;
            }

            bool TryBelow()
            {
                for (int row = afterRow; row < afterRow + 16; row++)
                    for (int col = originCol; col < afterCol + 8; col++)
                        if (Fits(col, row)) { tile.Col = col; tile.Row = row; return true; }
                return false;
            }

            if (right ? (TryBeside() || TryBelow()) : (TryBelow() || TryBeside())) return;

            // Both windows full. Walk down until something fits rather than dropping the tile on
            // top of another: past the lowest occupied row every cell is free, so this ends.
            int floor = taken.Count == 0 ? afterRow : taken.Max(cell => cell.Item2) + 1;
            for (int row = floor; row < floor + tile.RowSpan + 2; row++)
            {
                if (!Fits(originCol, row)) continue;
                tile.Col = originCol;
                tile.Row = row;
                return;
            }

            tile.Col = originCol;
            tile.Row = floor;
        }

        /// <summary>
        /// Pushes apart any tiles sitting on top of one another.
        ///
        /// Needed because the map's footprint is not fixed: making it bigger claims cells that
        /// were free when the tiles beside it were put there, and loading a layout applies the
        /// saved size after the tiles are already placed. Without this the map simply drew over
        /// them.
        ///
        /// The map is placed first — it is what the rest of the dock is arranged around — and
        /// everything else keeps its reading order, so a collision feels like the neighbours
        /// shuffling down rather than the layout being redealt.
        /// </summary>
        private void ResolveOverlaps()
        {
            var placed = new HashSet<(int, int)>();

            var ordered = VisibleTiles()
                .OrderBy(t => t.Kind == CommandDockTileKinds.Map ? 0 : 1)
                .ThenBy(t => t.Row)
                .ThenBy(t => t.Col)
                .ToList();

            foreach (var tile in ordered)
            {
                bool clear = true;
                for (int c = tile.Col; c < tile.Col + tile.ColSpan && clear; c++)
                    for (int r = tile.Row; r < tile.Row + tile.RowSpan && clear; r++)
                        if (placed.Contains((c, r))) clear = false;

                if (!clear) PlaceInFirstFreeCell(tile, placed);

                for (int c = tile.Col; c < tile.Col + tile.ColSpan; c++)
                    for (int r = tile.Row; r < tile.Row + tile.RowSpan; r++)
                        placed.Add((c, r));
            }
        }

        /// <summary>
        /// Every cell some tile already holds. The map is in the list like everything else now,
        /// so it needs no special case here — only the check that it is actually on screen.
        /// </summary>
        private HashSet<(int, int)> OccupiedCells(CommandDockTile? except)
        {
            var taken = new HashSet<(int, int)>();

            foreach (var other in _dock.Tiles)
            {
                // By id, not by reference: the drop preview asks with a throwaway copy of the
                // tile at a candidate cell. Compared by reference that copy never matched the
                // real tile, so every tile collided with itself and the highlight was always
                // red — most visibly on the map, which is large enough to always overlap.
                if (except != null && other.Id == except.Id) continue;
                if (!BelongsHere(other)) continue;

                for (int c = other.Col; c < other.Col + other.ColSpan; c++)
                    for (int r = other.Row; r < other.Row + other.RowSpan; r++)
                        taken.Add((c, r));
            }

            return taken;
        }

        // ── Building ────────────────────────────────────────────────────────────

        private void RebuildTiles()
        {
            foreach (var el in _tileElements.Values)
            {
                // Never the map: its element is declared in XAML and stays a child of the canvas
                // for the life of the window.
                if (ReferenceEquals(el, MapContainer)) continue;
                DockCanvas.Children.Remove(el);
            }

            _tileElements.Clear();
            _tileRefreshers.Clear();
            _tileHandles.Clear();
            _tilePassThrough.Clear();

            // _hoveredTileId survives on purpose. Tile ids are stable, and a settings change
            // rebuilds the dock — clearing it here would make the handles vanish under the
            // pointer every time a swatch was clicked.

            StampLegacyDeviceTiles();
            SyncMapCellSpan();

            foreach (var tile in _dock.Tiles.ToList())
            {
                // The map's element is the one declared in XAML — it carries the mirror brushes
                // and the whole viewbox machinery, and rebuilding it every time a tile moved
                // would throw that away. It is placed like any other tile, just not created.
                if (!BelongsHere(tile) && tile.Kind != CommandDockTileKinds.Map) continue;

                bool isMap = tile.Kind == CommandDockTileKinds.Map;

                if (isMap)
                {
                    MapContainer.Visibility = MapOccupiesCells ? Visibility.Visible : Visibility.Collapsed;
                    if (!MapOccupiesCells) continue;
                }

                var el = isMap ? MapContainer : BuildTile(tile);
                if (el == null) continue;   // unknown kind from a newer build

                // Size and position come from ApplyTilePositions at the end, so there is exactly
                // one place that turns a cell into pixels.
                if (!isMap) DockCanvas.Children.Add(el);
                _tileElements[tile.Id] = el;

                // Handles after the tile's own click handlers, so a device toggle still gets its
                // MouseUp: the interaction handler only swallows what is left, which is what
                // keeps the window's DragMove from eating the click.
                //
                // Every tile but the map is a fresh element each rebuild, so its handlers go
                // with it. The map's element is not — attaching again would stack another drag
                // and another hover handler on it every single time the dock redraws.
                bool wireMouse = !isMap || !_mapMouseWired;
                AddTileHandles(el, tile, resizable: IsResizable(tile), wireMouse: wireMouse);

                if (wireMouse)
                {
                    AttachTileInteraction(el, tile.Id,
                        onClick: isMap ? () => OnClicked?.Invoke()
                            : tile.Kind == CommandDockTileKinds.Collapse ? ToggleDockCollapsed
                            : tile.Kind == CommandDockTileKinds.DeathWipe ? () => WipeDeathMarkers(tile)
                            : null);
                    if (isMap) _mapMouseWired = true;
                }
            }

            if (MapTile == null) MapContainer.Visibility = Visibility.Collapsed;

            // The handles are rebuilt collapsed; this gives them back to the tile still under
            // the pointer.
            UpdateTileHandles();

            RefreshTiles();
            LayoutDock();
        }

        /// <summary>
        /// Whether a tile offers a resize grip.
        ///
        /// A device is an icon and one word — stretching it produces a mostly empty rectangle,
        /// so it stays one cell. The map is free-size through its own slider, not through cells.
        /// </summary>
        private static bool IsResizable(CommandDockTile tile) => tile.Kind switch
        {
            CommandDockTileKinds.Map => false,
            CommandDockTileKinds.Device => false,
            CommandDockTileKinds.Session => false,
            CommandDockTileKinds.Discord => false,
            CommandDockTileKinds.Collapse => false,
            CommandDockTileKinds.DeathWipe => false,
            _ => true,
        };

        /// <summary>What the dock was last built for, so a change of state is noticed.</summary>
        /// <summary>
        /// How much bigger than its base size the grid is drawn right now.
        ///
        /// Read through a property rather than the field so every measurement goes through the
        /// same clamp: a layout file edited by hand, or written by a build that allowed a wider
        /// range, must not be able to produce a cell of zero pixels.
        /// </summary>
        internal double DockZoom => CommandDockLayout.ClampZoom(_dock.GridZoom);

        /// <summary>
        /// Changes the grid's pitch, and everything measured against it.
        ///
        /// A full rebuild rather than a re-layout: the tiles' contents are sized from the zoom
        /// too - see StyleFor - so their elements have to be built again, not just moved. The
        /// map keeps its own size, which is set in pixels by its slider and is not a number of
        /// cells; only how many cells it covers changes, which is what the auto-arrange needs.
        /// </summary>
        internal void SetGridZoom(double zoom)
        {
            zoom = CommandDockLayout.ClampZoom(zoom);
            if (Math.Abs(zoom - _dock.GridZoom) < 0.001) return;

            _dock.GridZoom = zoom;
            SaveDock();

            SettingsOverlay?.SyncGridZoom(zoom);
            RebuildTiles();
        }

        private bool _lastPlayerDead;

        /// <summary>Whether the map had any death markers last tick, so the crossing is noticed.</summary>
        private bool _lastHadDeathMarkers;

        private int DeathMarkerCount => DockHost?.DockDeathMarkerCount ?? 0;

        private void RefreshTiles()
        {
            // Cheaper than wiring an event through the host: the server changes rarely, and a
            // second of lag on a dock that has just reconnected is not worth the coupling.
            var serverKey = DockHost?.DockServerKey;
            if (!string.Equals(serverKey, _lastServerKey, StringComparison.OrdinalIgnoreCase))
            {
                _lastServerKey = serverKey;
                RebuildTiles();
                return;
            }

            // Dying and respawning change which tiles exist, not just what they say, and that
            // is a rebuild rather than a refresh. Checked here for the same reason as the
            // server above: this runs anyway, and one comparison a tick is cheaper than an
            // event routed from the team list into a window that opens and closes freely.
            bool dead = PlayerIsDead;
            if (dead != _lastPlayerDead)
            {
                _lastPlayerDead = dead;

                if (_dock.Tiles.Any(t => t.Kind == CommandDockTileKinds.DeathTrack))
                {
                    RebuildTiles();
                    return;
                }
            }

            // Markers appearing or all of them going is the same kind of change for the wipe
            // tile: it holds cells in one state and not the other. Only the crossing matters,
            // not the count - that is the tile's own refresher.
            bool hasMarkers = DeathMarkerCount > 0;
            if (hasMarkers != _lastHadDeathMarkers)
            {
                _lastHadDeathMarkers = hasMarkers;

                if (_dock.Tiles.Any(t => t.Kind == CommandDockTileKinds.DeathWipe && !t.DeathWipeAlwaysVisible))
                {
                    RebuildTiles();
                    return;
                }
            }

            foreach (var refresh in _tileRefreshers)
            {
                try { refresh(); }
                catch { /* one bad tile must not stop the rest from ticking */ }
            }
        }

        /// <summary>
        /// The tile's frame, already faded to its chosen opacity. Only the background and the
        /// border go through the style — text is added by the builders and stays fully opaque,
        /// which is the point of a tile that can be turned invisible.
        /// </summary>
        private static Border TileShell(TileStyle style)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Background = style.Chrome(Brush("Surface", Color.FromArgb(0xD8, 0x16, 0x1B, 0x22))),
                BorderBrush = style.Chrome(Brush("CardBorder", Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF))),
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
            CommandDockTileKinds.Session => BuildSessionTile(tile),
            CommandDockTileKinds.Discord => BuildDiscordTile(tile),
            CommandDockTileKinds.ServerInfo => BuildServerInfoTile(tile),
            CommandDockTileKinds.AiCompanion => BuildAiTile(tile),
            CommandDockTileKinds.Translate => BuildTranslateTile(tile),
            CommandDockTileKinds.Collapse => BuildCollapseTile(tile),
            CommandDockTileKinds.DeathWipe => BuildDeathWipeTile(tile),
            CommandDockTileKinds.DeathTrack => BuildDeathTrackTile(tile),
            CommandDockTileKinds.TeamChat => BuildChatTile(tile, clan: false),
            CommandDockTileKinds.ClanChat => BuildChatTile(tile, clan: true),
            _ => null,
        };

        // ── Clock ───────────────────────────────────────────────────────────────

        private FrameworkElement BuildClockTile(CommandDockTile tile)
        {
            var style = StyleFor(tile);
            var shell = TileShell(style);
            shell.Tag = tile;

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            shell.Child = stack;

            // A bigger clock tile gets a bigger clock. Driven by the smaller of the two spans,
            // because a clock three cells wide and one tall has no room to grow — which is what
            // "as far as the height allows" means.
            //
            // Not a Viewbox around the content: the day-night line is much wider than the time,
            // so scaling to fit would have shrunk the reading to make room for its own caption.
            double grow = Math.Min(3.0, 1 + (Math.Min(tile.ColSpan, tile.RowSpan) - 1) * 0.8);
            double Sized(double baseSize) => style.Size(baseSize * grow);

            if (tile.ClockStyle == 1)
            {
                // Analog: a face, hour marks and two hands, redrawn from the server clock.
                //
                // The face keeps its own 46-unit space — every coordinate below, including the
                // hands' rotation centre, is written in it — and a LayoutTransform scales the
                // whole thing. Layout, not render: the panel above has to reserve the larger
                // size, or a grown clock would overlap the line under it.
                var face = new Grid
                {
                    Width = 46,
                    Height = 46,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    LayoutTransform = new ScaleTransform(grow, grow),
                };
                var dial = new System.Windows.Shapes.Ellipse
                {
                    Stroke = Brush("TextSubtle", Colors.Gray),
                    StrokeThickness = 1.5,
                    Fill = System.Windows.Media.Brushes.Transparent,
                };

                face.Children.Add(dial);

                // Twelve marks, the quarters longer. Without them a bare ring gives the hands
                // nothing to be read against.
                for (int hour = 0; hour < 12; hour++)
                {
                    bool quarter = hour % 3 == 0;
                    var mark = new System.Windows.Shapes.Line
                    {
                        X1 = 23, Y1 = quarter ? 3.5 : 4.5,
                        X2 = 23, Y2 = quarter ? 8.5 : 7.0,
                        StrokeThickness = quarter ? 1.6 : 1.0,
                        Stroke = style.TextSub,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        RenderTransform = new RotateTransform(hour * 30, 23, 23),
                    };
                    face.Children.Add(mark);
                }

                var hourHand = new System.Windows.Shapes.Line
                {
                    X1 = 23, Y1 = 23, X2 = 23, Y2 = 11,
                    StrokeThickness = 2.5,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Stroke = style.TextMain,
                    Effect = style.TextShadow,
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

                face.Children.Add(hourHand);
                face.Children.Add(minuteHand);
                stack.Children.Add(face);

                var phase = SubtleText(style);
                phase.FontSize = Sized(10);
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
                FontSize = Sized(14),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2),
                Effect = style.TextShadow,
            };
            var clock = new TextBlock
            {
                FontSize = Sized(rustStyle ? 20 : 19),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Effect = style.TextShadow,
                // The Rust clock keeps the game's own red unless a colour was picked; anything
                // else would make "Rust style" just mean a different typeface.
                Foreground = rustStyle && (tile.TextColorKey ?? _dock.DefaultTextColorKey) == CommandDockTextColors.Auto
                    ? new SolidColorBrush(Color.FromRgb(0xCD, 0x41, 0x2B))
                    : style.TextMain,
                FontFamily = rustStyle ? new FontFamily("Impact, Segoe UI") : new FontFamily("Consolas, Segoe UI"),
            };
            var phaseText = SubtleText(style);
            phaseText.FontSize = Sized(10);

            stack.Children.Add(glyph);
            stack.Children.Add(clock);
            stack.Children.Add(phaseText);

            _tileRefreshers.Add(() =>
            {
                var (time, isDay, _) = DockHost?.DockServerTime ?? ("-", true, (TimeSpan?)null);
                clock.Text = tile.Clock12Hour ? To12Hour(time) : time;
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

        /// <summary>
        /// "15:20" as "3:20 PM". Returns the input untouched when it is not a clock reading —
        /// the server time is a dash until the first status arrives.
        /// </summary>
        private static string To12Hour(string time)
        {
            if (!TryParseServerTime(time, out int h, out int m)) return time;

            string suffix = h >= 12 ? "PM" : "AM";
            int hour12 = h % 12;
            if (hour12 == 0) hour12 = 12;

            return $"{hour12}:{m:D2} {suffix}";
        }

        private static bool TryParseServerTime(string text, out int hours, out int minutes)
        {
            hours = minutes = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = text.Split(':');
            return parts.Length >= 2
                && int.TryParse(parts[0].Trim(), out hours)
                && int.TryParse(parts[1].Trim(), out minutes);
        }

        private static TextBlock SubtleText(TileStyle style) => new()
        {
            FontSize = style.Size(10),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = style.TextSub,
            Effect = style.TextShadow,
        };

        // ── Device ──────────────────────────────────────────────────────────────

        private FrameworkElement BuildDeviceTile(CommandDockTile tile)
        {
            var style = StyleFor(tile);
            var shell = TileShell(style);
            shell.Tag = tile;
            shell.Cursor = Cursors.Hand;

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            shell.Child = stack;

            // The picture from the device list, when the user gave the device one. It is what
            // the device is recognised by everywhere else in the app, and a glyph that says
            // "some switch" carries none of that.
            var picture = new Image
            {
                Width = style.Size(24),
                Height = style.Size(24),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 3),
                Visibility = Visibility.Collapsed,
                Effect = style.TextShadow,
            };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);

            var icon = new TextBlock
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = style.Size(17),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 3),
                Effect = style.TextShadow,
            };
            var name = SubtleText(style);
            var state = new TextBlock
            {
                FontSize = style.Size(11),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Effect = style.TextShadow,
            };

            stack.Children.Add(picture);
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

            // An alarm stays on until someone resets it in game, so pulsing while it is on would
            // pulse for hours. Ten seconds from the moment it went off is long enough to catch
            // the eye and short enough to stop being noise.
            var pulseFor = TimeSpan.FromSeconds(10);
            bool wasFired = false;
            DateTime firedAt = DateTime.MinValue;

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
                    state.Foreground = style.TextSub;
                    shell.Opacity = 0.5;
                    return;
                }

                shell.Opacity = 1.0;
                ToolTipService.SetToolTip(shell, device.DisplayName);

                // Icon mode is the default: the picture plus one state word. Turning it off puts
                // the name back, which is what a dock full of identical-looking switches needs.
                var art = DeviceArt(device);
                bool showPicture = tile.ShowDeviceIcon && art != null;
                picture.Source = showPicture ? art : null;
                picture.Visibility = showPicture ? Visibility.Visible : Visibility.Collapsed;
                icon.Visibility = showPicture ? Visibility.Collapsed : Visibility.Visible;

                name.Text = Abbreviate(device.DisplayName, 12);
                name.Visibility = tile.ShowDeviceIcon ? Visibility.Collapsed : Visibility.Visible;

                if (IsSwitch(device))
                {
                    bool on = device.IsOn == true;
                    icon.Text = "\uE7E8";                  // power button
                    // Green means on and is left alone. Off follows the chosen text colour —
                    // it used to be the theme's grey, which is the one state that stops being
                    // readable the moment the tile goes transparent.
                    icon.Foreground = on
                        ? new SolidColorBrush(Color.FromRgb(0x4C, 0xC9, 0x6A))
                        : style.TextSub;
                    state.Text = on
                        ? Loc.Text("CommandDockSwitchOn", "ON")
                        : Loc.Text("CommandDockSwitchOff", "OFF");
                    state.Foreground = icon.Foreground;
                    shell.BorderBrush = on
                        ? style.Chrome(Color.FromArgb(0x99, 0x4C, 0xC9, 0x6A))
                        : style.Chrome(Brush("CardBorder", Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)));
                    StopPulse();
                }
                else if (IsAlarm(device))
                {
                    bool fired = device.IsOn == true;
                    icon.Text = "\uE7ED";                  // ringer
                    // Red means it went off; armed is an ordinary resting state and follows the
                    // tile's text colour like everything else.
                    icon.Foreground = fired
                        ? new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35))
                        : style.TextSub;
                    state.Text = fired
                        ? Loc.Text("CommandDockAlarmActive", "ACTIVE")
                        : Loc.Text("CommandDockAlarmInactive", "INACTIVE");
                    state.Foreground = icon.Foreground;
                    shell.BorderBrush = fired
                        ? style.Chrome(Color.FromRgb(0xE5, 0x39, 0x35))
                        : style.Chrome(Brush("CardBorder", Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)));

                    if (fired && !wasFired) firedAt = DateTime.UtcNow;
                    wasFired = fired;

                    if (fired && DateTime.UtcNow - firedAt < pulseFor) StartPulse();
                    else StopPulse();
                }
                else
                {
                    // Storage monitor, including a tool cupboard: upkeep is the number that
                    // matters, and only a TC ever reports one.
                    icon.Text = "\uE7B8";                  // package
                    icon.Foreground = style.TextSub;
                    var secs = device.UpkeepSeconds ?? 0;
                    if (secs > 0)
                    {
                        state.Text = FormatUpkeep(secs);
                        state.Foreground = secs < 3600
                            ? new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35))
                            : style.TextMain;
                    }
                    else
                    {
                        state.Text = "—";
                        state.Foreground = style.TextSub;
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

        /// <summary>
        /// The picture the device list shows for a device: the icon the user picked for it, or
        /// the default for its kind.
        ///
        /// The list falls back by kind through a stack of XAML triggers, so a device nobody gave
        /// an icon to still has one everywhere else in the app — which is why using CustomIcon
        /// alone left every untouched alarm, switch and monitor on the dock with a generic glyph.
        /// </summary>
        private static ImageSource? DeviceArt(SmartDevice device)
        {
            if (device.CustomIcon != null) return device.CustomIcon;

            string key =
                device.IsGroup ? "IconGroup" :
                IsAlarm(device) ? "IconSmartAlarm" :
                IsSwitch(device) ? "IconSmartSwitch" :
                "IconStorageMonitor";

            // SharedResources.xaml is merged into the main window, not into the application, so
            // the application-wide lookup that every other brush here uses finds nothing. Ask
            // the window that actually holds the dictionary first.
            if (Application.Current?.MainWindow?.TryFindResource(key) is ImageSource fromWindow)
                return fromWindow;

            return Application.Current?.TryFindResource(key) as ImageSource;
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

        /// <summary>A remaining time as m:ss, the way every other countdown on the dock reads.</summary>
        private static string Countdown(TimeSpan left)
            => $"{(int)left.TotalMinutes}:{left.Seconds:D2}";

        private static string Abbreviate(string? text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= max ? text : text.Substring(0, Math.Max(1, max - 1)) + "…";
        }

        // ── Event ───────────────────────────────────────────────────────────────

        private FrameworkElement BuildEventTile(CommandDockTile tile)
        {
            var style = StyleFor(tile);
            var shell = TileShell(style);
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
                FontSize = style.Size(12),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = style.TextMain,
                Effect = style.TextShadow,
            };
            // A second countdown, for when both rigs are being hacked at once. Only ever shown
            // on a tile at least two cells tall — at one cell the first timer already fills the
            // space under the icon, and a line that does not fit is a line nobody can read.
            var timer2 = new TextBlock
            {
                FontSize = style.Size(12),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = style.TextMain,
                Effect = style.TextShadow,
                Visibility = Visibility.Collapsed,
            };

            var label = SubtleText(style);

            stack.Children.Add(image);
            stack.Children.Add(timer);
            stack.Children.Add(timer2);
            stack.Children.Add(label);

            // A green glow behind the crate while a real countdown is running, so the two Oil Rig
            // states are told apart at a glance and not only by reading the tooltip.
            //
            // A shadow with no depth rather than a shape behind the icon: it follows the crate's
            // own alpha, so it reads as the icon glowing rather than as a blob it sits on.
            var glow = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0x4C, 0xC9, 0x6A),
                ShadowDepth = 0,
                BlurRadius = 14,
                Opacity = 0,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance,
            };

            var breathe = new DoubleAnimation(0.25, 0.85, TimeSpan.FromMilliseconds(1400))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };

            bool glowing = false;

            void SetGlow(bool on)
            {
                if (on == glowing) return;
                glowing = on;

                if (on)
                {
                    image.Effect = glow;
                    glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, breathe);
                }
                else
                {
                    glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, null);
                    glow.Opacity = 0;
                    image.Effect = null;   // the sound-detected view looks exactly as it did
                }
            }

            string? loadedIcon = null;

            _tileRefreshers.Add(() =>
            {
                // The Oil Rig crate countdown is not an event dock entry: it comes from the
                // Logic Engine's hack timers, and only exists when a rule can start one.
                timer2.Visibility = Visibility.Collapsed;

                // A rig wired to an RF receiver gives a real countdown from the moment the hack
                // starts. The crowd-sourced cue below only says "a crate went up somewhere" and
                // cannot tell the two rigs apart, so a real trigger always wins the display.
                if (tile.EventKey == "oilrig")
                {
                    var host = Application.Current?.MainWindow as Views.MainWindow;
                    var running = host?.DockOilRigTimers ?? Array.Empty<(string Rig, string Short, TimeSpan Left)>();
                    SetIcon("pack://application:,,,/Assets/icons/crate.png");
                    label.Text = Loc.Text("OilRigCrateStatus", "Oil Rig crate");

                    if (running.Count > 0)
                    {
                        shell.Opacity = 1.0;
                        SetGlow(true);

                        // Both rigs at once needs two lines, and two lines need the height. On a
                        // single cell the soonest one is shown with a count of what is hidden,
                        // so the tile never pretends the other rig is not running.
                        bool roomForTwo = tile.RowSpan >= 2 && running.Count > 1;

                        timer.Text = $"{running[0].Short} {Countdown(running[0].Left)}";

                        if (roomForTwo)
                        {
                            timer2.Text = $"{running[1].Short} {Countdown(running[1].Left)}";
                            timer2.Visibility = Visibility.Visible;
                        }
                        else if (running.Count > 1)
                        {
                            timer.Text += $"  +{running.Count - 1}";
                        }

                        ToolTipService.SetToolTip(shell,
                            Loc.Text("CommandDockOilRigTrigger", "From your Oil Rig trigger") + "\n" +
                            string.Join("\n", running.Select(r => $"{r.Rig}: {Countdown(r.Left)}")));
                        return;
                    }

                    // No live trigger: the crowd-sourced reading below, and no glow with it.
                    SetGlow(false);
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
                timer.Foreground = ev.Active ? style.TextMain : style.TextSub;
                shell.Opacity = ev.Active ? 1.0 : 0.55;
                // Named as heard rather than measured, so an Oil Rig reading from the crowd cue
                // is not mistaken for the real countdown a trigger gives.
                var source = tile.EventKey == "oilrig"
                    ? Loc.Text("CommandDockOilRigHeard", "Heard by other players") + "\n"
                    : "";

                ToolTipService.SetToolTip(shell, source + (ev.ToolTip ?? ev.Name));

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
            var style = StyleFor(tile);
            var shell = TileShell(style);
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
                FontSize = style.Size(17),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 3),
                Effect = style.TextShadow,
                Foreground = Brush("Accent", Color.FromRgb(0x3F, 0xD7, 0xFF)),
            };
            var name = SubtleText(style);

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
                    "This rule no longer uses the Widget Overlay trigger.");
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
            // Height is free — a single row still shows the last message, which is often all
            // anyone wants from it.
            if (tile.ColSpan < 2) tile.ColSpan = 2;

            var style = StyleFor(tile);
            var shell = TileShell(style);
            shell.Tag = tile;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            shell.Child = grid;

            var header = new TextBlock
            {
                FontSize = style.Size(10),
                Effect = style.TextShadow,
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
                        FontSize = style.Size(11),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 2),
                        Effect = style.TextShadow,
                    };
                    para.Inlines.Add(new System.Windows.Documents.Run(
                        tile.ChatAbbreviateNames ? Abbreviate(line.Author, 10) : line.Author)
                    {
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Brush("Accent", Color.FromRgb(0x3F, 0xD7, 0xFF)),
                    });
                    para.Inlines.Add(new System.Windows.Documents.Run("  " + line.Message)
                    {
                        Foreground = style.TextMain,
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
        private bool _mapMouseWired;

        private void AddTileHandles(FrameworkElement shell, CommandDockTile tile, bool resizable, bool wireMouse)
        {
            var host = EnsureHandleHost(shell);

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
            //
            // Bottom-left, opposite the grip: the title bar overlays the dock's top edge, and a
            // × in the top-right corner of a first-row tile would sit underneath it.
            var remove = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(-6, 0, 0, -6),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
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

            // Next to the ×, along the bottom edge, for the same reason: the title bar owns the
            // top of the dock.
            var gear = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(14, 0, 0, -6),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = Brush("Accent", Color.FromRgb(0x3F, 0xD7, 0xFF)),
                Cursor = Cursors.Hand,
                Visibility = Visibility.Collapsed,
                Child = new TextBlock
                {
                    Text = "\uE713",  // settings
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 9,
                    Foreground = System.Windows.Media.Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            ToolTipService.SetToolTip(gear, Loc.Text("CommandDockTileSettings", "Tile settings"));
            gear.PreviewMouseLeftButtonDown += (_, e) => e.Handled = true;
            gear.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;

                // The map has no text to colour and no background to fade — it has a shape, a
                // size and five layers, and those already live in their own panel. Its gear goes
                // straight there rather than to a tile panel of settings that do not apply.
                if (tile.Kind == CommandDockTileKinds.Map) OpenSettings();
                else OpenTileSettings(tile);
            };

            // A veil rather than a border tint: the device and alarm refreshers rewrite the
            // shell's BorderBrush every second and would wipe a hover colour straight off again.
            // It is also what shows a press landed, which a tile that only toggles a switch
            // somewhere else on screen otherwise never acknowledges.
            //
            // The map gets none: it is round, a rectangular veil over it would look like a bug,
            // and it answers a click by recentring, which is feedback enough.
            Border? veil = null;
            if (shell is Border tileBorder)
            {
                veil = new Border
                {
                    CornerRadius = tileBorder.CornerRadius,
                    Background = new SolidColorBrush(Colors.White),
                    Opacity = 0,
                    IsHitTestVisible = false,
                };
            }

            void Veil(double to, int ms) => veil?.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { FillBehavior = FillBehavior.HoldEnd });

            // The tile id rather than the tile object: the map's handlers outlive the layout they
            // were attached under, and its id is fixed precisely so they stay valid.
            var tileId = tile.Id;

            if (wireMouse)
            {
                shell.MouseEnter += (_, __) =>
                {
                    HoverTile(tileId);
                    Veil(0.07, 120);
                };

                shell.MouseLeave += (_, __) =>
                {
                    UnhoverTile(tileId);
                    Veil(0, 160);
                };

                shell.PreviewMouseLeftButtonDown += (_, __) => Veil(0.18, 40);
                shell.PreviewMouseLeftButtonUp += (_, __) => Veil(shell.IsMouseOver ? 0.07 : 0, 220);
            }
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
                double step = CommandDockLayout.CellSizeAt(DockZoom) + CommandDockLayout.CellGapAt(DockZoom);

                int minCols = tile.Kind is CommandDockTileKinds.TeamChat
                                         or CommandDockTileKinds.ClanChat
                                         or CommandDockTileKinds.Translate ? 2 : 1;
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
            if (veil != null) host.Children.Add(veil);
            if (resizable) host.Children.Add(grip);
            host.Children.Add(remove);
            host.Children.Add(gear);

            // The tile's drag handler consults these so a press that started on one of them is
            // not turned into a tile drag.
            _tileHandles[tile.Id] = resizable
                ? new FrameworkElement[] { grip, remove, gear }
                : new FrameworkElement[] { remove, gear };
        }

        /// <summary>
        /// The layer a tile's handles are drawn into.
        ///
        /// A built tile is a Border with one child, so its content is wrapped in a grid that the
        /// handles can share. The map is a panel that already holds the mirror layers and its
        /// overlays, so it gets an overlay grid instead — wrapping its children would take them
        /// out of the tree the viewbox machinery expects them in.
        /// </summary>
        private static Grid EnsureHandleHost(FrameworkElement shell)
        {
            const string marker = "dock-handles";

            if (shell is Border border)
            {
                // Built fresh on every rebuild, so there is never an old host to reuse.
                var content = border.Child;
                var host = new Grid { Tag = marker };
                border.Child = null;
                if (content != null) host.Children.Add(content);
                border.Child = host;
                return host;
            }

            var panel = (Panel)shell;
            var overlay = panel.Children.OfType<Grid>().FirstOrDefault(g => (g.Tag as string) == marker);
            if (overlay == null)
            {
                overlay = new Grid { Tag = marker };
                Panel.SetZIndex(overlay, 50);
                panel.Children.Add(overlay);
            }

            overlay.Children.Clear();
            return overlay;
        }

        private readonly Dictionary<string, FrameworkElement[]> _tileHandles = new();

        private bool PressedOnHandle(string tileId, object? originalSource)
        {
            if (!_tileHandles.TryGetValue(tileId, out var handles)) return false;

            for (var node = originalSource as DependencyObject; node != null; node = ParentOf(node))
                if (node is FrameworkElement fe && Array.IndexOf(handles, fe) >= 0) return true;

            return false;
        }

        /// <summary>
        /// Whether a press landed on something inside a tile that wants it — a text box, a
        /// button, a dropdown, or an element a builder registered.
        ///
        /// Tunnelling runs outside in, so the tile's own press handler sees the event before any
        /// control inside it does. Left to swallow everything, it made the Discord tile's text
        /// box impossible to focus and its two buttons impossible to press. The window's drag
        /// handler consults this for the same reason: it is the next thing the press would reach.
        /// </summary>
        private bool PressedOnTileControl(object? originalSource)
        {
            for (var node = originalSource as DependencyObject; node != null; node = ParentOf(node))
            {
                if (node is System.Windows.Controls.Primitives.TextBoxBase
                         or System.Windows.Controls.Primitives.ButtonBase
                         or ComboBox
                         or Slider
                         or System.Windows.Controls.Primitives.ScrollBar)
                    return true;

                if (node is FrameworkElement fe && _tilePassThrough.Contains(fe)) return true;
            }

            return false;
        }

        private static DependencyObject? ParentOf(DependencyObject node)
            => node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : (node as FrameworkContentElement)?.Parent;

        /// <summary>
        /// Elements a tile builder wants to keep its own presses. Needed for the ones that are
        /// not standard controls — the Discord tile's two icon buttons are Borders.
        /// </summary>
        private readonly HashSet<FrameworkElement> _tilePassThrough = new();

        internal void KeepPresses(params FrameworkElement[] elements)
        {
            foreach (var element in elements) _tilePassThrough.Add(element);
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
        private void AttachTileInteraction(FrameworkElement border, string tileId, Action? onClick = null)
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

                // Same for a control inside the tile. Swallowed here, the Discord tiles text
                // box could never take focus and its buttons could never be pressed.
                if (PressedOnTileControl(e.OriginalSource)) return;

                pressed = true;
                dragging = false;
                grabOffset = e.GetPosition(border);
                border.CaptureMouse();
                e.Handled = true;   // never let the press reach the window's DragMove
            };

            border.PreviewMouseMove += (_, e) =>
            {
                if (!pressed) return;

                // A locked dock is used, not rearranged. The press is still swallowed above, so
                // a switch tile toggles as usual — it simply cannot be dragged off its cell.
                if (_dock.Locked) return;

                if (!dragging)
                {
                    var moved = e.GetPosition(border) - grabOffset;
                    if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold) return;

                    dragging = true;

                    var dragged = _dock.Tiles.FirstOrDefault(t => t.Id == tileId);
                    if (dragged != null) BeginDragPreview(dragged);

                    // The tile itself stays on its cell and dims; what follows the pointer is a
                    // picture of it on the overlay. It has to be, because the overlay covers the
                    // screen and this window does not - a tile dragged past the dock's own edge
                    // would simply be clipped away.
                    _overlay?.SetGhost(border, new Size(border.ActualWidth, border.ActualHeight));
                    border.Opacity = 0.35;
                }

                var p = e.GetPosition(DockCanvas);
                var corner = new Point(p.X - grabOffset.X, p.Y - grabOffset.Y);

                var ghostAt = ToOverlay(corner.X, corner.Y);
                _overlay?.MoveGhost(ghostAt);

                // Recomputed on every move so the highlight is always the cell a release would
                // actually use — the drop reads this, it does not work it out again.
                var next = CellUnderPoint(corner);
                if (next != _dropTarget)
                {
                    _dropTarget = next;
                    ShowOverlayDropTarget();
                }
            };

            border.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (!pressed) return;
                pressed = false;
                border.ReleaseMouseCapture();

                if (!dragging)
                {
                    // A click. Tiles that do something on click carry their own handler and get
                    // the event; the map cannot, because its click action lives on the window,
                    // and the swallower below would eat the release before it arrived.
                    onClick?.Invoke();
                    return;
                }

                dragging = false;
                Panel.SetZIndex(border, 0);

                // Put back what the drag dimmed. The drop only re-positions the existing
                // elements, so nothing else would restore it and the tile would stay faded.
                border.Opacity = 1.0;

                e.Handled = true;        // a drag must not also toggle the switch it landed on
                DropTile(tileId);
            };

            // Registered with handledEventsToo: the tile's own click handler has already marked
            // the release handled, and this still has to stop it reaching the window, where it
            // would re-centre the map behind the tile.
            border.AddHandler(UIElement.MouseLeftButtonUpEvent,
                new MouseButtonEventHandler((_, e) => e.Handled = true), true);

        }

        /// <summary>
        /// The cell a top-left corner sits over, in dock canvas coordinates.
        ///
        /// Takes the corner rather than the element, because the element no longer moves during
        /// a drag: it stays on its cell while a ghost on the overlay follows the pointer, so the
        /// corner being asked about is where the tile *would* be, not where it is.
        /// </summary>
        private (int Col, int Row) CellUnderPoint(Point corner) =>
        (
            NearestCell(corner.X),
            NearestCell(corner.Y)
        );

        /// <summary>
        /// Commits a drag to the cell the highlight was showing. It is not recomputed here: the
        /// tile lands where the preview said it would, or the preview was lying.
        /// </summary>
        private void DropTile(string tileId)
        {
            var tile = _dock.Tiles.FirstOrDefault(t => t.Id == tileId);
            var target = _dropTarget;

            EndDragPreview();

            if (tile == null) { LayoutDock(); return; }

            if (target is { } cell)
            {
                tile.Col = cell.Col;
                tile.Row = cell.Row;
            }

            // A drop onto occupied cells falls back to the first free spot, which is what makes
            // the dock behave like desktop icons rather than a free canvas. The highlight turned
            // red on the way in, so this is not a surprise.
            if (Overlaps(tile)) AssignFreeCell(tile);

            SaveDock();
            RebuildTiles();
        }

        /// <summary>
        /// The cell index whose edge sits closest to a dropped pixel position.
        ///
        /// Solved rather than searched. It used to walk a fixed range of indices, because the
        /// grid had a seam at the map and the mapping was piecewise - but a fixed range of cells
        /// is a shrinking range of pixels as the zoom comes down, so below 100% the last columns
        /// before the screen's right edge could not be reached at all. Only tiles wide enough to
        /// start further left and reach over could get there.
        ///
        /// With the grid uniform the mapping is linear and inverts in one line, which has no
        /// range to run out of. Negative indices fall out of it for free, and they matter - they
        /// are how a bar gets built along the top or the left edge.
        /// </summary>
        private int NearestCell(double pixels)
        {
            if (double.IsNaN(pixels)) return 0;

            double pitch = CommandDockLayout.CellSizeAt(DockZoom) + CommandDockLayout.CellGapAt(DockZoom);
            if (pitch <= 0) return 0;

            return (int)Math.Round(pixels / pitch, MidpointRounding.AwayFromZero);
        }

        private bool Overlaps(CommandDockTile tile)
        {
            var taken = OccupiedCells(except: tile);

            for (int c = tile.Col; c < tile.Col + tile.ColSpan; c++)
                for (int r = tile.Row; r < tile.Row + tile.RowSpan; r++)
                    if (taken.Contains((c, r))) return true;

            return false;
        }

        private void RemoveTile(string tileId)
        {
            if (_dock.Tiles.FirstOrDefault(t => t.Id == tileId) is { Kind: CommandDockTileKinds.Map })
                _dock.MapRemoved = true;

            _dock.Tiles.RemoveAll(t => t.Id == tileId);
            SaveDock();
            RebuildTiles();
        }

        /// <summary>
        /// True while no map is visible on the dock — whether it was removed outright or every
        /// one of its layers was switched off. Both look the same to the user, so the picker
        /// offers to bring it back in both cases.
        /// </summary>
        public bool CanAddMap => !MapOccupiesCells;

        /// <summary>
        /// Puts the map back on the dock, with every layer on.
        ///
        /// Coming back through the picker means the user asked for a mini-map, and handing them
        /// the invisible one they had switched off would look like the button did nothing.
        /// </summary>
        public void AddMapTile()
        {
            if (MapTile == null)
            {
                _dock.MapRemoved = false;

                var tile = new CommandDockTile { Id = CommandDockTileKinds.MapTileId, Kind = CommandDockTileKinds.Map };
                AssignFreeCell(tile);
                _dock.Tiles.Insert(0, tile);
                SaveDock();
            }

            // Also the path back from "every layer off", which rebuilds the dock by itself.
            SettingsOverlay?.TurnAllLayersOn();
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
            Ach.Unlock(Ach.Widget);
            if (tile.Kind == CommandDockTileKinds.AiCompanion) Ach.Unlock(Ach.AiCompanion);

            // The map is never a second copy of itself — the picker's map entry means "bring the
            // one back", and it has its own path because it may only need its layers switched on.
            if (tile.Kind == CommandDockTileKinds.Map) { AddMapTile(); return; }

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
