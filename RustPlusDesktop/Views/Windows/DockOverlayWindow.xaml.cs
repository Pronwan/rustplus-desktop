using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace RustPlusDesk.Views.Windows
{
    /// <summary>
    /// The screen-wide half of the Widget Overlay: the cell grid a drag is measured against, and
    /// the bar that arranges the dock.
    ///
    /// Both used to live inside the dock window, which is sized to its own tiles. The grid was
    /// therefore cut off at the dock's edge, and to give a drag room the dock grew by a cell in
    /// every direction and moved itself the opposite way to compensate - except ClampToScreen
    /// then pushed it back, so the compensation failed and the dock crept down the screen one
    /// cell per drag. A window that already covers the screen needs none of that: the dock keeps
    /// its size, and the bar can sit at the top of the screen instead of on top of the dock.
    ///
    /// It never takes focus. The grid is not hit-testable at all, and the window is marked
    /// NOACTIVATE so pressing a button on the bar leaves the game in front.
    /// </summary>
    public partial class DockOverlayWindow
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        /// <summary>Raised by the bar's buttons, handled by the dock that owns this overlay.</summary>
        public Action? LockToggled { get; set; }
        public Action? TemplatesRequested { get; set; }
        public Action? AddTileRequested { get; set; }
        public Action? SettingsRequested { get; set; }
        public Action<double>? ZoomChanged { get; set; }

        /// <summary>Dragging the bar moves the dock, as dragging its old title bar did.</summary>
        public Action<Vector>? BarDragged { get; set; }

        private bool _suppressZoomWrite;

        public DockOverlayWindow()
        {
            InitializeComponent();
            WireBarDrag();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).Handle;
            var styles = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();

            // NOACTIVATE so the bar's buttons work without pulling the game out of focus;
            // TOOLWINDOW so a full-screen transparent window is not an alt-tab entry.
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)(styles | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        }

        // ── Placement ───────────────────────────────────────────────────────────

        /// <summary>Lays the overlay over one screen, in device-independent pixels.</summary>
        public void CoverScreen(Rect screen)
        {
            if (screen.Width <= 0 || screen.Height <= 0) return;

            Left = screen.Left;
            Top = screen.Top;
            Width = screen.Width;
            Height = screen.Height;
        }

        // ── The bar ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Fades the bar in and out, and switches hit testing with it.
        ///
        /// Opacity alone does not stop a WPF element taking the mouse, and an invisible bar
        /// across the top of the screen would swallow every click on that strip.
        /// </summary>
        public void ShowBar(bool show)
        {
            if (TitleBar == null) return;
            if (BarVisible == show) return;

            BarVisible = show;
            TitleBar.IsHitTestVisible = show;

            TitleBar.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(show ? 1.0 : 0.0, TimeSpan.FromMilliseconds(show ? 140 : 320))
                {
                    FillBehavior = FillBehavior.HoldEnd,
                });
        }

        public bool BarVisible { get; private set; }

        public void SetLocked(bool locked)
        {
            if (LockGlyph != null)
                LockGlyph.Text = locked ? "" : "";   // closed padlock / open padlock

            if (BtnLock != null)
                BtnLock.ToolTip = locked
                    ? Helpers.Loc.Text("CommandDockUnlock", "Unlock the overlay to arrange it")
                    : Helpers.Loc.Text("CommandDockLock", "Lock the overlay");
        }

        public void SetZoom(double zoom)
        {
            if (SliZoom == null) return;
            if (Math.Abs(SliZoom.Value - zoom) < 0.001) { ShowZoomLabel(zoom); return; }

            _suppressZoomWrite = true;
            try { SliZoom.Value = zoom; }
            finally { _suppressZoomWrite = false; }

            ShowZoomLabel(zoom);
        }

        private void ShowZoomLabel(double zoom)
        {
            if (LblZoom == null) return;
            LblZoom.Text = string.Format(CultureInfo.CurrentCulture, "{0}%", (int)Math.Round(zoom * 100));
        }

        private void SliZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ShowZoomLabel(e.NewValue);
            if (_suppressZoomWrite) return;
            ZoomChanged?.Invoke(e.NewValue);
        }

        private void BtnLock_Click(object sender, RoutedEventArgs e) => LockToggled?.Invoke();
        private void BtnTemplates_Click(object sender, RoutedEventArgs e) => TemplatesRequested?.Invoke();
        private void BtnAddTile_Click(object sender, RoutedEventArgs e) => AddTileRequested?.Invoke();
        private void BtnSettings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

        /// <summary>
        /// Dragging an empty part of the bar moves the dock, which is how it was moved before
        /// the bar left the dock window. Reported as a delta so the dock stays the one that
        /// knows where it is and what it may clamp to.
        /// </summary>
        private void WireBarDrag()
        {
            Point? from = null;

            TitleBar.MouseLeftButtonDown += (_, e) =>
            {
                // Buttons and the slider handle their own presses; only the bar itself drags.
                if (e.OriginalSource is DependencyObject src && IsInteractive(src)) return;

                from = PointToScreen(e.GetPosition(this));
                TitleBar.CaptureMouse();
            };

            TitleBar.MouseMove += (_, e) =>
            {
                if (from is not { } origin) return;

                var now = PointToScreen(e.GetPosition(this));
                var delta = now - origin;
                if (delta.Length < 0.5) return;

                from = now;
                BarDragged?.Invoke(delta);
            };

            TitleBar.MouseLeftButtonUp += (_, __) =>
            {
                from = null;
                TitleBar.ReleaseMouseCapture();
            };
        }

        private static bool IsInteractive(DependencyObject node)
        {
            for (var at = node; at != null; at = VisualTreeHelper.GetParent(at))
            {
                if (at is ButtonBase or Slider or Thumb) return true;
            }
            return false;
        }

        // ── The grid ────────────────────────────────────────────────────────────

        private Rectangle? _highlight;
        private FrameworkElement? _ghost;

        /// <summary>
        /// Paints the grid across the whole screen, aligned to the dock's own cell origin.
        ///
        /// <paramref name="origin"/> is where cell (0,0) of the dock sits in this window's
        /// coordinates. The grid is then extended outwards from there in whole cell steps until
        /// it covers the screen, so every line a drag can snap to is drawn - including the cells
        /// left of and above the dock, which is how a bar along an edge gets built.
        /// </summary>
        public void PaintGrid(Point origin, double cellSize, double gap, IEnumerable<Rect> occupied)
        {
            PaintLayer.Children.Clear();
            _highlight = null;
            _ghost = null;

            double pitch = cellSize + gap;
            if (pitch <= 1) return;

            var line = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            line.Freeze();

            // Back up from the origin to the first cell that is still on screen, rather than
            // starting at zero and drawing a lot of rectangles nobody sees.
            double startX = origin.X - Math.Ceiling(origin.X / pitch) * pitch;
            double startY = origin.Y - Math.Ceiling(origin.Y / pitch) * pitch;

            for (double x = startX; x < ActualWidth; x += pitch)
            {
                for (double y = startY; y < ActualHeight; y += pitch)
                {
                    var cell = new Rectangle
                    {
                        Width = cellSize,
                        Height = cellSize,
                        RadiusX = 6,
                        RadiusY = 6,
                        Stroke = line,
                        StrokeThickness = 1,
                        StrokeDashArray = new DoubleCollection { 3, 3 },
                        Fill = Brushes.Transparent,
                    };
                    Canvas.SetLeft(cell, x);
                    Canvas.SetTop(cell, y);
                    PaintLayer.Children.Add(cell);
                }
            }

            // A veil over what is already placed. The point is to read the arrangement as
            // occupied space at a glance, without having to tell tiles from background while
            // looking for somewhere to put the one in hand.
            var veilFill = new SolidColorBrush(Color.FromArgb(0x38, 0x3F, 0xA9, 0xFF));
            var veilEdge = new SolidColorBrush(Color.FromArgb(0x88, 0x5A, 0xC8, 0xFF));
            veilFill.Freeze();
            veilEdge.Freeze();

            foreach (var rect in occupied)
            {
                var veil = new Rectangle
                {
                    Width = Math.Max(1, rect.Width),
                    Height = Math.Max(1, rect.Height),
                    RadiusX = 8,
                    RadiusY = 8,
                    Fill = veilFill,
                    Stroke = veilEdge,
                    StrokeThickness = 1,
                };
                Canvas.SetLeft(veil, rect.X);
                Canvas.SetTop(veil, rect.Y);
                PaintLayer.Children.Add(veil);
            }

            // Built once and moved on the pointer, rather than rebuilt at pointer rate.
            _highlight = new Rectangle { RadiusX = 8, RadiusY = 8, StrokeThickness = 2 };
            PaintLayer.Children.Add(_highlight);
            _highlight.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// A stand-in for the tile being dragged, following the pointer across the screen.
        ///
        /// A brush of the real element rather than the element itself: reparenting a live tile
        /// into another window mid-drag would take its handlers and its timers with it, and it
        /// has to go back afterwards. A picture is enough for the time it is in flight.
        /// </summary>
        public void SetGhost(Visual? source, Size size)
        {
            if (_ghost != null) { PaintLayer.Children.Remove(_ghost); _ghost = null; }
            if (source == null || size.Width <= 0 || size.Height <= 0) return;

            var brush = new VisualBrush(source) { Stretch = Stretch.Fill };
            brush.Freeze();

            _ghost = new Rectangle
            {
                Width = size.Width,
                Height = size.Height,
                RadiusX = 8,
                RadiusY = 8,
                Fill = brush,
                Opacity = 0.75,
                IsHitTestVisible = false,
            };
            PaintLayer.Children.Add(_ghost);
        }

        public void MoveGhost(Point at)
        {
            if (_ghost == null) return;
            Canvas.SetLeft(_ghost, at.X);
            Canvas.SetTop(_ghost, at.Y);
        }

        /// <summary>
        /// The cell a release would use. Red when the drop will bounce to the next free spot
        /// instead of landing here, so that outcome is visible before the button comes up.
        /// </summary>
        public void SetDropTarget(Rect? rect, bool blocked)
        {
            if (_highlight == null) return;

            if (rect is not { } r)
            {
                _highlight.Visibility = Visibility.Collapsed;
                return;
            }

            _highlight.Visibility = Visibility.Visible;
            _highlight.Width = Math.Max(1, r.Width);
            _highlight.Height = Math.Max(1, r.Height);
            _highlight.Fill = new SolidColorBrush(blocked
                ? Color.FromArgb(0x33, 0xE5, 0x39, 0x35)
                : Color.FromArgb(0x33, 0x3F, 0xD7, 0xFF));
            _highlight.Stroke = new SolidColorBrush(blocked
                ? Color.FromArgb(0xAA, 0xE5, 0x39, 0x35)
                : Color.FromArgb(0xAA, 0x3F, 0xD7, 0xFF));

            Canvas.SetLeft(_highlight, r.X);
            Canvas.SetTop(_highlight, r.Y);
        }

        public void ClearGrid()
        {
            PaintLayer.Children.Clear();
            _highlight = null;
            _ghost = null;
        }
    }
}
