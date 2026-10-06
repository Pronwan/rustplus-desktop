using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using RustPlusDesk.Views.Windows;

namespace RustPlusDesk
{
    /// <summary>
    /// The dock's side of the screen-wide overlay: where the grid is painted, where the bar
    /// gets its presses from, and the watch that brings the bar out at the top of the screen.
    /// </summary>
    public partial class MiniMapWindow
    {
        private DockOverlayWindow? _overlay;

        /// <summary>
        /// How long the pointer has to rest against the top edge before the bar appears.
        ///
        /// The edge is the gesture rather than hovering a tile, for one reason: hovering a tile
        /// is something you do by accident while using the dock - reading a timer, holding over
        /// a switch - and the bar coming out on its own brought the delete handles with it. The
        /// top of the screen is somewhere the pointer only goes on purpose.
        /// </summary>
        private static readonly TimeSpan BarRevealDelay = TimeSpan.FromMilliseconds(500);

        /// <summary>How close to the top edge counts, in device-independent pixels.</summary>
        private const double BarRevealBand = 3.0;

        /// <summary>
        /// How close to the top of the screen the pointer has to come for the pull tab to show.
        /// Far wider than the reveal band: the tab is there to be aimed at, so it has to be on
        /// screen before the pointer arrives rather than once it already has.
        /// </summary>
        private const double PullTabBand = 80.0;

        private DispatcherTimer? _edgeWatch;
        private DateTime? _atEdgeSince;

        /// <summary>
        /// Until when the bar stays out regardless of where the pointer is - set while a hint is
        /// pointing at something on it, so it does not slide away mid-sentence.
        /// </summary>
        private DateTime _barHeldUntil;

        /// <summary>Until when the pull tab shows on its own, after the bar has demonstrated itself.</summary>
        private DateTime _pullTabHeldUntil;

        private DockOverlayWindow Overlay()
        {
            if (_overlay != null) return _overlay;

            var overlay = new DockOverlayWindow
            {
                LockToggled = ToggleDockLock,
                TemplatesRequested = () => BtnTemplates_Click(this, new RoutedEventArgs()),
                AddTileRequested = () => BtnAddTile_Click(this, new RoutedEventArgs()),
                SettingsRequested = OpenSettings,
                ZoomChanged = SetGridZoom,
                BarDragged = MoveDockBy,
                PullTabActivated = () => _overlay?.ShowBar(true),
                EscapePressed = FinishArranging,
            };

            _overlay = overlay;
            overlay.Closed += (_, __) => { if (ReferenceEquals(_overlay, overlay)) _overlay = null; };

            PositionOverlay();
            overlay.Show();

            overlay.SetLocked(_dock.Locked);
            overlay.SetZoom(DockZoom);

            return overlay;
        }

        /// <summary>Puts the overlay over whichever screen the dock is currently on.</summary>
        private void PositionOverlay()
        {
            if (_overlay == null) return;
            _overlay.CoverScreen(ScreenBoundsFor(this));
        }

        /// <summary>
        /// Moves the dock by a pointer delta from the bar, then lets it clamp itself.
        ///
        /// The bar is on another window now, so it cannot move the dock by moving itself. It
        /// reports how far it was dragged and the dock stays the one that decides where it is
        /// allowed to end up.
        /// </summary>
        private void MoveDockBy(Vector delta)
        {
            if (double.IsNaN(Left) || double.IsNaN(Top)) return;

            double oldLeft = Left, oldTop = Top;

            Left += delta.X;
            Top += delta.Y;

            ClampToScreen();
            AnchorOriginToWindow();
            SaveDockPosition();
            HoldSettingsPopupInPlace(Left - oldLeft, Top - oldTop);
            FollowAiAnswer();
        }

        /// <summary>
        /// The bar's lock button, routed into the one that was already there.
        ///
        /// Deliberately not its own copy of the logic: locking closes the tile settings, rebuilds
        /// the tiles that only exist unlocked, and repaints the button - three things that were
        /// already written once and would drift the moment there were two of them.
        /// </summary>
        internal void ToggleDockLock() => BtnLockDock_Click(this, new RoutedEventArgs());

        // ── The bar at the top of the screen ────────────────────────────────────

        /// <summary>
        /// Watches for the pointer resting against the top of the screen, and brings the bar out.
        ///
        /// Polled rather than hooked: a window that could receive the hover would have to be
        /// hit-testable, and a hit-testable strip across the top of the screen takes clicks away
        /// from the game. Reading the cursor position ten times a second intercepts nothing at
        /// all, and at that rate costs nothing worth measuring.
        /// </summary>
        private void StartEdgeWatch()
        {
            if (_edgeWatch != null) return;

            _edgeWatch = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(100),
            };
            _edgeWatch.Tick += (_, __) => CheckTopEdge();
            _edgeWatch.Start();
        }

        private void StopEdgeWatch()
        {
            _edgeWatch?.Stop();
            _edgeWatch = null;
            _atEdgeSince = null;
        }

        private void CheckTopEdge()
        {
            if (_overlay == null || !IsVisible) return;

            var screen = ScreenBoundsFor(this);

            Point cursor;
            try { cursor = CursorPositionDip(); }
            catch { return; }

            bool onThisScreen = cursor.X >= screen.Left && cursor.X <= screen.Right;
            bool atEdge = onThisScreen && cursor.Y <= screen.Top + BarRevealBand;

            // Inside the bar itself counts as still at the edge, or it would fade out from
            // under the pointer the moment somebody reached for a button on it.
            double barHeight = _overlay.TitleBar?.ActualHeight ?? 0;
            bool overBar = _overlay.BarVisible
                        && onThisScreen
                        && cursor.Y <= screen.Top + barHeight;

            var now = DateTime.UtcNow;

            if (atEdge)
            {
                _atEdgeSince ??= now;
                if (now - _atEdgeSince >= BarRevealDelay)
                    _overlay.ShowBar(true);
            }
            else
            {
                _atEdgeSince = null;

                // An unlocked dock keeps the bar: it is the only way back to locking it, and it
                // holds the zoom somebody is in the middle of adjusting.
                bool keep = overBar || !_dock.Locked || _draggingTile != null || now < _barHeldUntil;
                if (!keep) _overlay.ShowBar(false);
            }

            // The tab is the visible half of the gesture: near the top of the screen, or over
            // the dock it belongs to, it shows where the bar went.
            bool nearTop = onThisScreen && cursor.Y <= screen.Top + PullTabBand;
            bool overDock = !double.IsNaN(Left) && !double.IsNaN(Top)
                         && new Rect(Left, Top, ActualWidth, ActualHeight).Contains(cursor);

            _overlay.SetPullTab(nearTop || overDock || now < _pullTabHeldUntil);
        }

        // ── Teaching the bar ────────────────────────────────────────────────────

        private const string HintsCacheKey = "minimap_dock_hints";

        private static readonly TimeSpan BarIntroHold = TimeSpan.FromSeconds(6);
        private static readonly TimeSpan PullTabAfterIntro = TimeSpan.FromSeconds(4);

        /// <summary>
        /// Plays the bar's introduction the first time the dock is ever shown. A gesture that
        /// leaves nothing on screen cannot be discovered, only told about - so it is told about
        /// once, by the bar itself, while it is out.
        /// </summary>
        private void MaybeIntroduceBar()
        {
            var hints = StorageService.LoadCache<CommandDockHints>(HintsCacheKey) ?? new CommandDockHints();
            if (hints.BarIntroSeen) return;

            hints.BarIntroSeen = true;
            StorageService.SaveCache(HintsCacheKey, hints);

            // After the dock has had a moment on screen: a bar that arrives with the window is
            // one more thing appearing at once, and is the part that gets missed.
            var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            delay.Tick += (_, __) =>
            {
                delay.Stop();
                if (IsVisible) DemonstrateBar();
            };
            delay.Start();
        }

        /// <summary>
        /// Brings the bar out, says what it is and how to get it back, then lets it slide away
        /// and leaves the pull tab showing where it went.
        ///
        /// Public because the dock tutorial plays it too: being shown the bar leave is what
        /// makes "it is at the top of the screen" mean something.
        /// </summary>
        public void DemonstrateBar()
        {
            var overlay = Overlay();
            PositionOverlay();

            var now = DateTime.UtcNow;
            _barHeldUntil = now + BarIntroHold;
            _pullTabHeldUntil = now + BarIntroHold + PullTabAfterIntro;

            overlay.ShowBar(true);
            overlay.ShowHint(
                Loc.Text("CommandDockBarIntro",
                    "This bar arranges your widgets: unlock to move them, + to add more, ⚙ for settings. " +
                    "It hides while you play - rest the mouse on the top edge of the screen, or use the small tab there, to bring it back."),
                "",   // up arrow
                BarIntroHold,
                DockRectOnOverlay());
        }

        private DateTime _lastLockedHint;

        /// <summary>
        /// Someone tried to drag a widget on a locked dock - the moment they need to know about
        /// the lock, so that is when they are told. The bar comes out with the lock flashing,
        /// and the hint sits beside the dock, level with the widget they were pulling at.
        /// </summary>
        private void ExplainLockedDrag(Point canvasPoint)
        {
            var now = DateTime.UtcNow;
            if (now - _lastLockedHint < TimeSpan.FromSeconds(8)) return;
            _lastLockedHint = now;

            var overlay = Overlay();
            PositionOverlay();

            var hold = TimeSpan.FromSeconds(4.5);
            _barHeldUntil = now + hold;

            overlay.ShowBar(true);
            overlay.PulseLock();
            overlay.ShowHint(
                Loc.Text("CommandDockLockedDragHint",
                    "The layout is locked. Press the padlock in the bar at the top of the screen to move, resize or remove widgets."),
                "",   // padlock
                hold,
                DockRectOnOverlay(),
                ToOverlay(canvasPoint.X, canvasPoint.Y));
        }

        /// <summary>
        /// The dock window's rectangle in the overlay's coordinates - the area a hint must stay
        /// out of, because the dock is drawn above the overlay and would cover it.
        /// </summary>
        private Rect DockRectOnOverlay()
        {
            if (double.IsNaN(Left) || double.IsNaN(Top)) return Rect.Empty;
            return new Rect(ToOverlay(0, 0), new Size(ActualWidth, ActualHeight));
        }

        /// <summary>Esc while arranging: the same as pressing Done.</summary>
        private void FinishArranging()
        {
            if (_dock.Locked || _draggingTile != null) return;
            ToggleDockLock();
        }

        /// <summary>The pointer, in the same device-independent pixels as Window.Left.</summary>
        private Point CursorPositionDip()
        {
            var p = System.Windows.Forms.Control.MousePosition;

            double scale = 1.0;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
                scale = source.CompositionTarget.TransformToDevice.M11;
            if (scale <= 0) scale = 1.0;

            return new Point(p.X / scale, p.Y / scale);
        }

        // ── The grid ────────────────────────────────────────────────────────────

        /// <summary>A point on the dock's canvas, in the overlay window's coordinates.</summary>
        private Point ToOverlay(double canvasX, double canvasY)
        {
            var overlay = _overlay;
            if (overlay == null) return new Point(canvasX, canvasY);

            return new Point(
                Left + canvasX - overlay.Left,
                Top + canvasY - overlay.Top);
        }

        /// <summary>
        /// Paints the grid for a drag that has just started: the cells across the screen, and a
        /// veil over everything already placed.
        /// </summary>
        private void PaintOverlayGrid()
        {
            var overlay = Overlay();
            PositionOverlay();

            var occupied = new List<Rect>();
            foreach (var tile in _dock.Tiles)
            {
                if (_draggingTile != null && tile.Id == _draggingTile.Id) continue;
                if (!BelongsHere(tile)) continue;

                var rect = TileCanvasRect(tile);
                var at = ToOverlay(rect.X, rect.Y);
                occupied.Add(new Rect(at.X, at.Y, rect.Width, rect.Height));
            }

            overlay.PaintGrid(
                ToOverlay(CellX(0) - CellBounds().X, CellY(0) - CellBounds().Y),
                CommandDockLayout.CellSizeAt(DockZoom),
                CommandDockLayout.CellGapAt(DockZoom),
                occupied);

            overlay.ShowBar(true);
        }

        /// <summary>Shows where a release would put the tile, and in what colour.</summary>
        private void ShowOverlayDropTarget()
        {
            if (_overlay == null || _draggingTile == null) return;

            if (_dropTarget is not { } target)
            {
                _overlay.SetDropTarget(null, false);
                return;
            }

            var probe = new CommandDockTile
            {
                Id = _draggingTile.Id,
                Kind = _draggingTile.Kind,
                Col = target.Col,
                Row = target.Row,
                ColSpan = _draggingTile.ColSpan,
                RowSpan = _draggingTile.RowSpan,
            };

            var rect = TileCanvasRect(probe);
            var at = ToOverlay(rect.X, rect.Y);

            _overlay.SetDropTarget(new Rect(at.X, at.Y, rect.Width, rect.Height), Overlaps(probe));
        }

        private void ClearOverlayGrid()
        {
            _overlay?.ClearGrid();

            // The bar stays out while the dock is unlocked - arranging is not over just because
            // one tile was put down.
            if (_dock.Locked) _overlay?.ShowBar(false);
        }

        /// <summary>Takes the overlay down with the dock, so it cannot outlive what it describes.</summary>
        private void CloseOverlay()
        {
            StopEdgeWatch();

            var overlay = _overlay;
            _overlay = null;
            try { overlay?.Close(); } catch { }
        }
    }
}
