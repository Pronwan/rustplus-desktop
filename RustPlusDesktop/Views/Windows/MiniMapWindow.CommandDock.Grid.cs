using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using RustPlusDesk.Models;

namespace RustPlusDesk
{
    /// <summary>
    /// The grid the dock is arranged on: one lattice over the whole monitor, with cell (0,0) in
    /// its top-left corner.
    ///
    /// It used to be anchored to the dock instead, and the dock's own corner was whichever tile
    /// happened to be furthest up and left. That origin moved on its own - a tile hidden because
    /// the player was alive, or because a server changed, took the origin with it and dragged
    /// every other tile along. Everything downstream then had to compensate: the window was
    /// sized from the occupied cells, moved the opposite way to cancel the re-basing, and
    /// clamped back onto a screen afterwards. Each of those was somewhere a rounding or an edge
    /// case could and did go wrong.
    ///
    /// With a fixed origin none of it is needed. A cell is a place on the monitor, a tile's
    /// cell is where it is, and the window is simply drawn around whatever is currently visible.
    /// </summary>
    public partial class MiniMapWindow
    {
        /// <summary>
        /// The monitor the grid is laid over, in device-independent pixels.
        ///
        /// Held rather than derived, because deriving it from the window would be circular: the
        /// window's position now comes from the cells, and the cells are measured from here. It
        /// changes only when the dock is deliberately moved to another screen.
        /// </summary>
        private Rect _gridScreen;

        private bool _gridScreenKnown;

        private Rect GridScreen
        {
            get
            {
                if (!_gridScreenKnown) AnchorGridFromSavedPosition();
                return _gridScreen;
            }
        }

        /// <summary>
        /// Picks the grid's monitor from the saved dock corner, before anything is drawn.
        ///
        /// Asking the window would be too late and wrong: at that point it still has whatever
        /// geometry the XAML gave it, which can straddle both screens - so the first layout was
        /// measured against a monitor the dock was not on, and the map flashed up on the wrong
        /// screen at the wrong size before the next pass corrected it.
        /// </summary>
        private void AnchorGridFromSavedPosition()
        {
            _gridScreenKnown = true;

            _gridScreen = _dock?.WindowLeft is { } left && _dock.WindowTop is { } top
                ? ScreenBoundsForPoint(left, top)
                : ScreenBoundsFor(this);
        }

        /// <summary>
        /// Re-anchors the grid to whichever monitor the dock is on now.
        ///
        /// Called when the dock has been dragged, which is the only way it changes screens.
        /// </summary>
        private void ReanchorGrid()
        {
            var now = ScreenBoundsFor(this);
            if (_gridScreenKnown && now == _gridScreen) return;

            _gridScreen = now;
            _gridScreenKnown = true;
        }

        /// <summary>
        /// The distance from one cell's left edge to the next, including the gap.
        ///
        /// Stretched so a whole number of columns fills the monitor's width exactly: at 100% the
        /// grid is flush left and right, which is what makes a bar along either edge line up
        /// with the screen rather than stopping a few pixels short. The same pitch is used
        /// vertically, so cells stay square and the bottom row is allowed to run past the edge -
        /// a grid that divides both axes evenly needs a screen whose sides happen to agree, and
        /// a square cell matters more than a flush bottom.
        /// </summary>
        private double CellPitch => PitchFor(DockZoom);

        /// <summary>
        /// The same, at a zoom other than the live one.
        ///
        /// The template preview needs it: it draws an arrangement at the zoom it was saved with,
        /// and has to stretch that pitch the same way, or the outline sits a few pixels off the
        /// grid the load will actually produce.
        /// </summary>
        internal double PitchFor(double zoom)
        {
            double basePitch = CommandDockLayout.CellSizeAt(zoom) + CommandDockLayout.CellGapAt(zoom);
            double width = GridScreen.Width;

            if (basePitch <= 1 || width <= basePitch) return Math.Max(1, basePitch);

            int columns = Math.Max(1, (int)Math.Round(width / basePitch));
            return width / columns;
        }

        /// <summary>A cell's size, the pitch less the gap it carries with it.</summary>
        private double CellSize => CellSizeFor(DockZoom);

        internal double CellSizeFor(double zoom) =>
            Math.Max(1, PitchFor(zoom) - CommandDockLayout.CellGapAt(zoom));

        /// <summary>How many pixels a run of cells covers at a given zoom.</summary>
        internal double CellsToPixelsFor(int cells, double zoom) =>
            cells <= 0 ? 0 : cells * PitchFor(zoom) - CommandDockLayout.CellGapAt(zoom);

        /// <summary>Where a column's left edge sits on screen.</summary>
        private double CellScreenX(int col) => GridScreen.Left + col * CellPitch;

        /// <summary>Where a row's top edge sits on screen.</summary>
        private double CellScreenY(int row) => GridScreen.Top + row * CellPitch;

        /// <summary>How many pixels a run of cells covers, gaps between them included.</summary>
        private double CellsToPixels(int cells) =>
            cells <= 0 ? 0 : cells * CellPitch - CommandDockLayout.CellGapAt(DockZoom);

        /// <summary>How many cells a free-size element needs to cover a pixel width.</summary>
        private int PixelsToCells(double pixels) =>
            Math.Max(1, (int)Math.Ceiling((pixels + CommandDockLayout.CellGapAt(DockZoom)) / CellPitch));

        /// <summary>
        /// The cell containing a screen position, snapped to the nearest edge and kept on the
        /// monitor.
        ///
        /// Clamping here rather than after the drop is what replaces the old window clamp: a
        /// cell that cannot be off the screen means a window derived from cells cannot be
        /// either, so there is nothing left to pull back afterwards.
        /// </summary>
        private (int Col, int Row) CellAtScreen(Point at)
        {
            int col = (int)Math.Round((at.X - GridScreen.Left) / CellPitch, MidpointRounding.AwayFromZero);
            int row = (int)Math.Round((at.Y - GridScreen.Top) / CellPitch, MidpointRounding.AwayFromZero);

            return (
                Math.Max(0, Math.Min(col, LastColumn)),
                Math.Max(0, Math.Min(row, LastRow)));
        }

        /// <summary>The rightmost column that still starts on the monitor.</summary>
        private int LastColumn => Math.Max(0, (int)Math.Floor((GridScreen.Width - CellSize) / CellPitch));

        /// <summary>The lowest row that still starts on the monitor.</summary>
        private int LastRow => Math.Max(0, (int)Math.Floor((GridScreen.Height - CellSize) / CellPitch));

        /// <summary>A tile's rectangle on screen, in device-independent pixels.</summary>
        private Rect TileScreenRect(CommandDockTile tile)
        {
            if (tile.Kind == CommandDockTileKinds.Map) return MapRect();

            return new Rect(
                CellScreenX(tile.Col), CellScreenY(tile.Row),
                CellsToPixels(tile.ColSpan), CellsToPixels(tile.RowSpan));
        }

        // ── The map, which is not made of cells ─────────────────────────────────

        /// <summary>
        /// Where the map is, in screen pixels.
        ///
        /// Falls back to its old cell the first time, which is how an arrangement saved before
        /// the map was freed keeps it roughly where it was.
        /// </summary>
        private Rect MapRect()
        {
            var map = MapTile;
            if (map == null) return Rect.Empty;

            double x = _dock.MapX ?? CellScreenX(map.Col);
            double y = _dock.MapY ?? CellScreenY(map.Row);

            return new Rect(x, y, _mapWidth, _mapHeight);
        }

        /// <summary>Whether a press landed inside the map rather than on the dock around it.</summary>
        private bool PressedOnMap(object? originalSource)
        {
            if (MapContainer == null || originalSource is not DependencyObject node) return false;

            for (var at = node; at != null; at = System.Windows.Media.VisualTreeHelper.GetParent(at))
                if (ReferenceEquals(at, MapContainer)) return true;

            return false;
        }

        /// <summary>Gives the map a position of its own the first time one is needed.</summary>
        private void EnsureMapPlaced()
        {
            if (MapTile == null || (_dock.MapX.HasValue && _dock.MapY.HasValue)) return;

            var rect = MapRect();
            _dock.MapX = rect.X;
            _dock.MapY = rect.Y;
        }

        /// <summary>
        /// Moves the map by a pointer delta, in pixels and without touching a single cell.
        ///
        /// Kept on the monitor rather than snapped to it: the map is free, and the only thing
        /// it may not do is leave the screen entirely.
        /// </summary>
        internal void MoveMapByPixels(double dx, double dy)
        {
            if (MapTile == null) return;
            if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) return;

            EnsureMapPlaced();

            var screen = GridScreen;
            _dock.MapX = Math.Max(screen.Left, Math.Min((_dock.MapX ?? 0) + dx, screen.Right - _mapWidth));
            _dock.MapY = Math.Max(screen.Top, Math.Min((_dock.MapY ?? 0) + dy, screen.Bottom - _mapHeight));

            SaveDock();
            LayoutDock();
        }

        /// <summary>
        /// The cells the map's rectangle covers, so a widget cannot be dropped underneath it.
        ///
        /// This is all the grid knows about the map now. It is asked when something is being
        /// placed and never otherwise - which is the difference that stops a nudge of the size
        /// slider from rearranging widgets several columns away.
        /// </summary>
        private IEnumerable<(int, int)> MapCoveredCells()
        {
            if (!MapOccupiesCells) yield break;

            var rect = MapRect();
            if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) yield break;

            int fromCol = (int)Math.Floor((rect.X - GridScreen.Left) / CellPitch);
            int toCol = (int)Math.Ceiling((rect.Right - GridScreen.Left) / CellPitch) - 1;
            int fromRow = (int)Math.Floor((rect.Y - GridScreen.Top) / CellPitch);
            int toRow = (int)Math.Ceiling((rect.Bottom - GridScreen.Top) / CellPitch) - 1;

            for (int c = fromCol; c <= toCol; c++)
                for (int r = fromRow; r <= toRow; r++)
                    yield return (c, r);
        }

        /// <summary>
        /// The cell the window's own top-left corner sits on: the furthest up and left of
        /// everything currently visible.
        ///
        /// Derived per layout and never written back to a tile. That is the whole difference
        /// from the normalisation this replaced - the origin still moves when tiles come and go,
        /// but it moves the window, not the arrangement.
        /// </summary>
        private (int Col, int Row) VisibleOrigin()
        {
            var visible = VisibleTiles().ToList();
            if (visible.Count == 0) return (0, 0);

            return (visible.Min(t => t.Col), visible.Min(t => t.Row));
        }

        /// <summary>
        /// Converts an arrangement saved with dock-relative cells to absolute ones.
        ///
        /// The saved window position is what the cells were relative to, so it is what they are
        /// offset by. A layout from a different screen size lands approximately rather than
        /// exactly, which is the accepted cost of the change: the widgets are all still there
        /// and near where they were, and an arrangement is quick to nudge back into shape.
        /// </summary>
        private void MigrateCellsToAbsolute()
        {
            if (_dock.CellsAreAbsolute) return;

            _dock.CellsAreAbsolute = true;

            double left = _dock.WindowLeft ?? GridScreen.Left;
            double top = _dock.WindowTop ?? GridScreen.Top;

            int shiftCol = (int)Math.Round((left - GridScreen.Left) / CellPitch, MidpointRounding.AwayFromZero);
            int shiftRow = (int)Math.Round((top - GridScreen.Top) / CellPitch, MidpointRounding.AwayFromZero);

            foreach (var tile in _dock.Tiles)
            {
                tile.Col = Math.Max(0, tile.Col + shiftCol);
                tile.Row = Math.Max(0, tile.Row + shiftRow);
            }

            SaveDock();
        }

        /// <summary>
        /// Turns a window move into a move of the arrangement.
        ///
        /// Dragging the dock still drags a window, because that is what the pointer is over -
        /// but the window's position is derived from the cells, so on its own the drag would be
        /// undone by the next layout. The distance is converted to whole cells and applied to
        /// the tiles, which is the state that actually holds it.
        ///
        /// Crossing onto another monitor re-anchors the grid, so an arrangement carried to the
        /// second screen is measured from that screen's corner from then on.
        /// </summary>
        internal void MoveDockByPixels(double dx, double dy)
        {
            if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) return;

            ReanchorGrid();

            int cols = (int)Math.Round(dx / CellPitch, MidpointRounding.AwayFromZero);
            int rows = (int)Math.Round(dy / CellPitch, MidpointRounding.AwayFromZero);

            ShiftAllTiles(cols, rows);
            SaveDock();

            LayoutDock();
        }

        /// <summary>
        /// Moves every tile by a whole number of cells, which is how the dock as a whole moves
        /// now: there is no window position to set any more, only cells to shift.
        /// </summary>
        private void ShiftAllTiles(int cols, int rows)
        {
            if (cols == 0 && rows == 0) return;

            // Nothing may be pushed off the top or the left, where it could not be reached.
            var visible = VisibleTiles().ToList();
            if (visible.Count > 0)
            {
                cols = Math.Max(cols, -visible.Min(t => t.Col));
                rows = Math.Max(rows, -visible.Min(t => t.Row));
            }

            foreach (var tile in _dock.Tiles)
            {
                tile.Col += cols;
                tile.Row += rows;
            }
        }
    }
}
