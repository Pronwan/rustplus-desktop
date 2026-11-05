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
        public bool IsDeviceOverlay { get; set; } = true;
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

        /// <summary>The pull tab was hovered or pressed: the bar should come out.</summary>
        public Action? PullTabActivated { get; set; }

        /// <summary>Esc while the overlay has focus, which it only can while arranging.</summary>
        public Action? EscapePressed { get; set; }

        private bool _suppressZoomWrite;

        public DockOverlayWindow()
        {
            InitializeComponent();
            WireBarDrag();

            // Hover as well as click: the tab is small and only there when the pointer is
            // already heading for the top of the screen, so reaching it is the intent.
            PullTab.MouseEnter += (_, __) => PullTabActivated?.Invoke();
            PullTab.MouseLeftButtonDown += (_, e) => { e.Handled = true; PullTabActivated?.Invoke(); };

            PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                EscapePressed?.Invoke();
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            if (!IsDeviceOverlay)
            {
                TitleText.Text = "Minimap";
                BtnTemplates.Visibility = Visibility.Collapsed;
                return;
            }

            var hwnd = new WindowInteropHelper(this).Handle;
            var styles = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();

            // NOACTIVATE so the bar's buttons work without pulling the game out of focus;
            // TOOLWINDOW so a full-screen transparent window is not an alt-tab entry.
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)(styles | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>
        /// Lets the overlay be activated while the dock is being arranged.
        ///
        /// NOACTIVATE is right for using the dock - a press on a widget should not take the game
        /// out of focus. It is wrong for arranging it: a window that is never active gets its
        /// input behind whatever is in front, and with the game running that reads as lag on
        /// every drag and every drag of the zoom slider. Arranging is a deliberate, short mode
        /// where the app is what the user is looking at, so for its duration the overlay becomes
        /// an ordinary window.
        /// </summary>
        public void SetEditable(bool editing)
        {
            if (!IsDeviceOverlay) return;
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            var styles = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            long next = editing
                ? styles & ~(long)WS_EX_NOACTIVATE
                : styles | WS_EX_NOACTIVATE;

            if (next != styles) SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)next);

            if (editing) SetForegroundWindow(hwnd);
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

            var duration = TimeSpan.FromMilliseconds(show ? 160 : 320);

            TitleBar.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(show ? 1.0 : 0.0, duration) { FillBehavior = FillBehavior.HoldEnd });

            BarSlide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(show ? 0.0 : -TitleBar.Height, duration)
                {
                    EasingFunction = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn },
                    FillBehavior = FillBehavior.HoldEnd,
                });

            // The tab stands in for the bar while it is away, never alongside it.
            if (show) SetPullTab(false);
        }

        public bool BarVisible { get; private set; }

        private bool _pullTabVisible;

        /// <summary>Shows or hides the tab that brings the bar back, hit testing with it.</summary>
        public void SetPullTab(bool show)
        {
            if (PullTab == null) return;
            if (BarVisible) show = false;
            if (_pullTabVisible == show) return;

            _pullTabVisible = show;
            PullTab.IsHitTestVisible = show;
            PullTab.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(show ? 1.0 : 0.0, TimeSpan.FromMilliseconds(show ? 180 : 260))
                {
                    FillBehavior = FillBehavior.HoldEnd,
                });
        }

        public void SetLocked(bool locked)
        {
            if (LockGlyph != null)
                LockGlyph.Text = locked ? "" : "";   // closed padlock / open padlock

            if (BtnLock != null)
            {
                BtnLock.ToolTip = locked
                    ? Helpers.Loc.Text("CommandDockUnlock", "Unlock the overlay to arrange it")
                    : Helpers.Loc.Text("CommandDockLock", "Lock the overlay");

                // While arranging, Done says the same thing in words.
                BtnLock.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
            }

            if (BtnDone != null)
            {
                BtnDone.Content = Helpers.Loc.Text("CommandDockDone", "Done");
                BtnDone.ToolTip = Helpers.Loc.Text("CommandDockDoneHint", "Lock the layout again (Esc)");
                BtnDone.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
            }

            if (EditText != null && TitleText != null)
            {
                EditText.Text = Helpers.Loc.Text("CommandDockEditingBanner",
                    "Editing layout · drag a widget to move it · hover it to resize, configure or remove it");
                EditText.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
                TitleText.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
            }

            if (TitleBar != null)
            {
                TitleBar.BorderBrush = new SolidColorBrush(locked
                    ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
                    : Color.FromArgb(0xCC, 0x3F, 0xD7, 0xFF));
                TitleBar.BorderThickness = new Thickness(0, 0, 0, locked ? 1 : 2);
            }
        }

        /// <summary>
        /// Flashes the padlock, for when a hint is pointing at it. The bar is wide and the lock
        /// is a 14-pixel glyph at its far end; told to "press the lock", people look for it.
        /// </summary>
        public void PulseLock()
        {
            if (LockGlyph == null) return;

            var brush = new SolidColorBrush(Colors.White);
            LockGlyph.Foreground = brush;
            brush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(Color.FromRgb(0x3F, 0xD7, 0xFF), TimeSpan.FromMilliseconds(280))
                {
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(4),
                });
        }

        // ── Hints ───────────────────────────────────────────────────────────────

        private System.Windows.Threading.DispatcherTimer? _hintTimer;

        private const double HintMargin = 8;
        private const double HintGap = 10;

        /// <summary>
        /// Shows a short explanation for a while, then fades it.
        ///
        /// Never on top of <paramref name="dock"/>: the dock is its own window and sits above
        /// this one, so a hint placed over it is hidden behind the very tiles it is talking
        /// about. Without <paramref name="near"/> the hint is about the bar and goes centred
        /// under it, if that spot is clear of the dock. Otherwise it goes beside the dock - on
        /// whichever side has the most room and still fits it whole - lined up with
        /// <paramref name="near"/>, so it reads as being about the widget that was grabbed.
        /// </summary>
        public void ShowHint(string text, string glyph, TimeSpan duration, Rect? dock = null, Point? near = null)
        {
            if (HintBubble == null) return;

            HintText.Text = text;
            HintGlyph.Text = glyph;

            // The window's own size rather than ActualWidth/Height: CoverScreen sets it, and the
            // measured size lags a layout pass behind right after the overlay has moved - which
            // is how a hint got clamped to the size of a screen it was no longer covering.
            double screenW = double.IsNaN(Width) || Width <= 0 ? ActualWidth : Width;
            double screenH = double.IsNaN(Height) || Height <= 0 ? ActualHeight : Height;

            // A screen narrower than the bubble's usual width still has to fit all of it.
            HintBubble.MaxWidth = Math.Max(160, Math.Min(360, screenW - 2 * HintMargin));

            // Measure short-circuits on an element that is not itself dirty, and a new text only
            // dirties the TextBlock - its parents are marked later, by the layout pass. Without
            // this the bubble answered with the size of the previous hint (or of no text at all,
            // the first time), was placed for that size, and drew off the edge of the screen.
            for (DependencyObject? d = HintText; d != null; d = VisualTreeHelper.GetParent(d))
            {
                if (d is UIElement el) el.InvalidateMeasure();
                if (ReferenceEquals(d, HintBubble)) break;
            }
            HintGlyph.InvalidateMeasure();

            HintBubble.Measure(new Size(HintBubble.MaxWidth, double.PositiveInfinity));
            var size = HintBubble.DesiredSize;

            // The bar is out with most hints, and a hint under it would be covered as well.
            double top = (BarVisible ? TitleBar.Height : 0) + HintMargin;
            double left = HintMargin;
            double right = Math.Max(left, screenW - HintMargin - size.Width);
            double bottom = Math.Max(top, screenH - HintMargin - size.Height);

            double ClampX(double x) => Math.Max(left, Math.Min(x, right));
            double ClampY(double y) => Math.Max(top, Math.Min(y, bottom));

            bool Fits(Point p) =>
                p.X >= left - 0.5 && p.X <= right + 0.5 &&
                p.Y >= top - 0.5 && p.Y <= bottom + 0.5;

            bool Clear(Point p) =>
                dock is not { } d || d.IsEmpty || !new Rect(p, size).IntersectsWith(d);

            var underBar = new Point(ClampX((screenW - size.Width) / 2), top + HintGap - HintMargin);
            Point? at = null;

            if (near == null && Clear(underBar)) at = underBar;

            if (at == null && dock is { } r && !r.IsEmpty)
            {
                var focus = near ?? new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
                double alongX = ClampX(focus.X - size.Width / 2);
                double alongY = ClampY(focus.Y - size.Height / 2);

                var sides = new (double Room, Point Spot)[]
                {
                    (screenH - r.Bottom, new Point(alongX, r.Bottom + HintGap)),
                    (r.Top - top,        new Point(alongX, r.Top - HintGap - size.Height)),
                    (r.Left,             new Point(r.Left - HintGap - size.Width, alongY)),
                    (screenW - r.Right,  new Point(r.Right + HintGap, alongY)),
                };

                // Most room first, so a dock parked in a corner gets its hint towards the middle
                // of the screen rather than squeezed against the edge.
                Array.Sort(sides, (a, b) => b.Room.CompareTo(a.Room));
                foreach (var side in sides)
                {
                    if (!Fits(side.Spot)) continue;
                    at = side.Spot;
                    break;
                }
            }

            // A dock that leaves no side free - one filling the screen - still gets its hint in
            // the one place it can always be read.
            var spot = at ?? underBar;
            Canvas.SetLeft(HintBubble, spot.X);
            Canvas.SetTop(HintBubble, spot.Y);

            // Last word goes to the size it was actually drawn at. Should the measurement above
            // still disagree with the render - a font fallback, a late layout - the bubble is
            // pulled back on screen rather than left hanging off it.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                double w = HintBubble.ActualWidth, h = HintBubble.ActualHeight;
                if (w <= 0 || h <= 0) return;

                double maxX = Math.Max(HintMargin, screenW - HintMargin - w);
                double maxY = Math.Max(top, screenH - HintMargin - h);
                double x = Math.Max(HintMargin, Math.Min(Canvas.GetLeft(HintBubble), maxX));
                double y = Math.Max(top, Math.Min(Canvas.GetTop(HintBubble), maxY));

                Canvas.SetLeft(HintBubble, x);
                Canvas.SetTop(HintBubble, y);
            }));

            HintBubble.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.HoldEnd });

            _hintTimer?.Stop();
            _hintTimer = new System.Windows.Threading.DispatcherTimer { Interval = duration };
            _hintTimer.Tick += (_, __) => HideHint();
            _hintTimer.Start();
        }

        public void HideHint()
        {
            _hintTimer?.Stop();
            _hintTimer = null;

            HintBubble?.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(320)) { FillBehavior = FillBehavior.HoldEnd });
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

            // Deliberately not frozen. The colour brushes above are, but this one points at a
            // live element in another window's tree, and a Freezable holding an unfrozen
            // reference cannot itself be frozen - Freeze() throws rather than returning false.
            // It also has to stay live: the brush keeps tracking the tile while it is dragged.
            var brush = new VisualBrush(source) { Stretch = Stretch.Fill };

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

        // ── The template preview ────────────────────────────────────────────────

        /// <summary>
        /// Outlines an arrangement where it would actually land, in this window's coordinates.
        ///
        /// Drawn rather than applied: swapping the live layout to show a preview would resize
        /// the window, move the tiles, and leave the dock half-changed if the pointer moved on
        /// mid-way. An outline says the same thing and can be dropped at any moment.
        ///
        /// It lives here rather than on the dock because the dock is only as big as its own
        /// tiles. An arrangement larger than the current one had to grow the dock window just to
        /// have somewhere to be drawn, and one that lands somewhere else entirely - the built-in
        /// default, which parks in a corner - could not be shown in the right place at all.
        /// </summary>
        public void ShowPreview(IEnumerable<(Rect Rect, bool IsMap)> shapes)
        {
            PreviewLayer.Children.Clear();

            var fill = new SolidColorBrush(Color.FromArgb(0x26, 0x3F, 0xD7, 0xFF));
            var edge = new SolidColorBrush(Color.FromArgb(0xCC, 0x3F, 0xD7, 0xFF));
            fill.Freeze();
            edge.Freeze();

            bool any = false;
            foreach (var (rect, isMap) in shapes)
            {
                if (rect.Width <= 0 || rect.Height <= 0) continue;
                any = true;

                var outline = new Rectangle
                {
                    Width = rect.Width,
                    Height = rect.Height,
                    RadiusX = isMap ? Math.Min(rect.Width, rect.Height) / 2 : 10,
                    RadiusY = isMap ? Math.Min(rect.Width, rect.Height) / 2 : 10,
                    Fill = fill,
                    Stroke = edge,
                    StrokeThickness = isMap ? 2 : 1.5,
                };
                Canvas.SetLeft(outline, rect.X);
                Canvas.SetTop(outline, rect.Y);
                PreviewLayer.Children.Add(outline);
            }

            if (!any) return;

            PreviewLayer.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.HoldEnd });
        }

        public void HidePreview()
        {
            if (PreviewLayer == null) return;

            PreviewLayer.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(220)) { FillBehavior = FillBehavior.HoldEnd });
        }
    }
}
