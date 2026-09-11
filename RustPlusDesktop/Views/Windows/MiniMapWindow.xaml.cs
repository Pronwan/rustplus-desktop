using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RustPlusDesk
{
    /// <summary>
    /// The five map layers the mini-map mirrors, each as its own visual.
    ///
    /// They are siblings in the main map's scene grid, so they share one coordinate space and
    /// one viewbox drives all five brushes. Any of them may be null before a map is loaded.
    /// </summary>
    public sealed record MiniMapLayers(
        Visual? Texture,
        Visual? Grid,
        Visual? Drawings,
        Visual? Icons,
        Visual? Players);

    public partial class MiniMapWindow : Window
    {
        public Action? OnClicked { get; set; }

        // Basis-Ausschnitt vom MainWindow (wo der Spieler ist)
        private Rect _baseViewbox;

        // zusätzlicher User-Zoom nur für die Mini-Map
        private double _userZoom = 1.1;
        private const double USER_ZOOM_MIN = 0.4;
        private const double USER_ZOOM_MAX = 3.0;

        // zusätzlicher User-Pan (in ABSOLUTEN Koordinaten, wie der Viewbox auch)
        private double _panX = 0.0;
        private double _panY = 0.0;

        // Drag-Status für Rechtsklick-Panning
        private bool _isPanning = false;
        private Point _panStartMouse;     // Mauspos im Fenster
        private double _panStartX;        // panX beim Down
        private double _panStartY;        // panY beim Down

        // Current map tile geometry, the single source of truth for the window's size.
        private double _mapWidth = 260;
        private double _mapHeight = 260;
        private int _shapeIndex = 0;      // 0 = circle, 1 = square, 2 = 16:9

        public MiniMapWindow(MiniMapLayers layers)
        {
            InitializeComponent();
            SetLayers(layers);

            // Zoom nur für Mini-Map
            MouseWheel += MiniMapWindow_MouseWheel;

            // Panning mit rechter Maustaste
            MouseRightButtonDown += MiniMapWindow_MouseRightButtonDown;
            MouseRightButtonUp += MiniMapWindow_MouseRightButtonUp;
            MouseMove += MiniMapWindow_MouseMove;

            // Click detection for centering
            Point startDragPos = new Point();
            MouseLeftButtonDown += (s, e) =>
            {
                startDragPos = e.GetPosition(this);
                DragMove();
                ClampToScreen();
            };
            MouseLeftButtonUp += (s, e) =>
            {
                var endPos = e.GetPosition(this);
                if (Math.Abs(endPos.X - startDragPos.X) < 5 && Math.Abs(endPos.Y - startDragPos.Y) < 5)
                {
                    OnClicked?.Invoke();
                }
                else
                {
                    // Dragged by the map rather than the title bar — the dock still ends up
                    // somewhere new, and the position has to survive the next restart either way.
                    SaveDockPosition();
                }
            };

            // The title bar drags the whole dock, and is the only handle left once the map is
            // switched off. DragMove blocks until the button is released, so the clamp runs
            // afterwards rather than during — LocationChanged catches the in-between frames.
            DockTitleBar.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                DragMove();
                ClampToScreen();
                SaveDockPosition();
            };

            LocationChanged += (_, __) =>
            {
                if (_clamping) return;
                _clamping = true;
                try { ClampToScreen(); }
                finally { _clamping = false; }
            };

            // Runs after every Loaded handler, so the saved position is applied on top of
            // whatever the initial layout and the loaded settings worked out.
            ContentRendered += (_, __) => RestoreDockPosition();

            SettingsOverlay.ParentWindow = this;
            InitCommandDock();
        }

        private bool _clamping;

        /// <summary>
        /// Points every layer brush at its source. Called again whenever the main window
        /// rebuilds its map scene, since a fresh map may hand out fresh visuals.
        /// </summary>
        public void SetLayers(MiniMapLayers layers)
        {
            BrushTexture.Visual = layers.Texture;
            BrushGrid.Visual = layers.Grid;
            BrushDrawings.Visual = layers.Drawings;
            BrushIcons.Visual = layers.Icons;
            BrushPlayers.Visual = layers.Players;
            ApplyViewbox();
        }

        private int _viewboxId = 0;

        // wird vom MainWindow aufgerufen — Koordinaten sind Karten-Pixel (Scene-Space)
        public void SetViewbox(Rect viewbox, bool instant = false)
        {
            if (_baseViewbox.Width <= 0 || _baseViewbox.Height <= 0 || instant)
            {
                _baseViewbox = viewbox;
                ApplyViewbox();
                _viewboxId++; // Cancel any running interpolation
                return;
            }

            // Interpolation starten
            int myId = ++_viewboxId;
            var startPos = new Point(_baseViewbox.X, _baseViewbox.Y);
            var startSize = new Size(_baseViewbox.Width, _baseViewbox.Height);
            var targetPos = new Point(viewbox.X, viewbox.Y);
            var targetSize = new Size(viewbox.Width, viewbox.Height);

            // Wenn der Sprung zu groß ist (z.B. Erster Start oder Teleport), direkt setzen.
            // Schwelle relativ zur Ausschnittbreite: der Viewbox lebt jetzt in Karten-Pixeln,
            // und deren Maßstab hängt von der Kartengröße des Servers ab.
            double dist = Math.Sqrt(Math.Pow(targetPos.X - startPos.X, 2) + Math.Pow(targetPos.Y - startPos.Y, 2));
            if (dist > Math.Max(1.0, targetSize.Width) * 2.0)
            {
                _baseViewbox = viewbox;
                ApplyViewbox();
                return;
            }

            Dispatcher.InvokeAsync(async () =>
            {
                int steps = 120; // ca. 2 Sekunden bei 16ms (passend zum Polling/Marker-Animation)
                for (int i = 1; i <= steps; i++)
                {
                    if (myId != _viewboxId) break;

                    double t = i / (double)steps;
                    // Linear lerp
                    double curX = startPos.X + (targetPos.X - startPos.X) * t;
                    double curY = startPos.Y + (targetPos.Y - startPos.Y) * t;
                    double curW = startSize.Width + (targetSize.Width - startSize.Width) * t;
                    double curH = startSize.Height + (targetSize.Height - startSize.Height) * t;

                    _baseViewbox = new Rect(curX, curY, curW, curH);
                    ApplyViewbox();

                    await System.Threading.Tasks.Task.Delay(16);
                }
            });
        }

        private void MiniMapWindow_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                // SHIFT gedrückt → Fenstergröße ändern
                double factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
                UpdateSize(_mapWidth * factor, updateSlider: true);

                e.Handled = true;
                return;
            }

            // Kein SHIFT → normaler Karten-Zoom
            double zoomFactor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
            _userZoom *= zoomFactor;
            if (_userZoom < USER_ZOOM_MIN) _userZoom = USER_ZOOM_MIN;
            if (_userZoom > USER_ZOOM_MAX) _userZoom = USER_ZOOM_MAX;

            ApplyViewbox();
            e.Handled = true;
        }

        private void MiniMapWindow_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                _panX = 0;
                _panY = 0;
                _userZoom = 1.0;
                ApplyViewbox();
                e.Handled = true;
                return;
            }

            _isPanning = true;
            _panStartMouse = e.GetPosition(this);
            _panStartX = _panX;
            _panStartY = _panY;
            CaptureMouse();
        }

        private void MiniMapWindow_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isPanning = false;
            ReleaseMouseCapture();
        }

        private void MiniMapWindow_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanning) return;

            var cur = e.GetPosition(this);
            var dxWindow = cur.X - _panStartMouse.X;
            var dyWindow = cur.Y - _panStartMouse.Y;

            // aktuelle angezeigte Viewbox (nach Zoom) herausfinden,
            // um Window-Pixel in Karten-Pixel zu übersetzen
            if (_baseViewbox.Width <= 0 || _baseViewbox.Height <= 0)
                return;

            // “angezeigte” Größe nach Zoom:
            double shownW = _baseViewbox.Width / _userZoom;
            double shownH = _baseViewbox.Height / _userZoom;

            // Verhältnis: wieviel Karten-Pixel steckt in 1 Fenster-Pixel?
            double winW = Math.Max(1.0, MapContainer.ActualWidth);
            double winH = Math.Max(1.0, MapContainer.ActualHeight);

            double scaleX = shownW / winW;
            double scaleY = shownH / winH;

            // jetzt können wir Window-Delta in Viewbox-Delta umrechnen
            double dxView = dxWindow * scaleX;
            double dyView = dyWindow * scaleY;

            _panX = _panStartX + dxView;
            _panY = _panStartY + dyView;

            ApplyViewbox();
        }

        private void ApplyViewbox()
        {
            if (_baseViewbox.Width <= 0 || _baseViewbox.Height <= 0)
                return;

            // Mittelpunkt der Basis
            double cx = _baseViewbox.X + _baseViewbox.Width / 2.0;
            double cy = _baseViewbox.Y + _baseViewbox.Height / 2.0;

            // Größe nach User-Zoom
            double w = _baseViewbox.Width / _userZoom;
            double h = _baseViewbox.Height / _userZoom;

            // Ein nicht-quadratischer Ausschnitt würde die Karte verzerren, weil alle fünf
            // Brushes denselben Viewbox teilen: die Höhe folgt dem Seitenverhältnis der Kachel.
            if (_mapWidth > 0 && _mapHeight > 0)
                h = w * (_mapHeight / _mapWidth);

            // Pan addieren – wir verschieben einfach den Mittelpunkt
            double finalCx = cx - _panX;
            double finalCy = cy - _panY;

            var vb = new Rect(finalCx - w / 2.0, finalCy - h / 2.0, w, h);

            foreach (var brush in new[] { BrushTexture, BrushGrid, BrushDrawings, BrushIcons, BrushPlayers })
            {
                if (brush == null) continue;
                brush.ViewboxUnits = BrushMappingMode.Absolute;
                brush.Viewbox = vb;
                brush.Stretch = Stretch.Fill;
            }
        }

        // ── Settings popup ──────────────────────────────────────────────────────

        private void BtnSettings_Click(object sender, RoutedEventArgs e) => OpenSettings();

        /// <summary>
        /// Opens the settings popup beside the map and pins it there.
        ///
        /// The offsets are relative to this window, so dragging the mini-map carries the panel
        /// along, which is what you want. Resizing does not: <see cref="UpdateSize"/> corrects
        /// the offsets by however far it moved the window, so the panel holds its place on
        /// screen while the map grows out from under it.
        /// </summary>
        public void OpenSettings()
        {
            if (SettingsPopup.IsOpen) return;

            const double gap = 12;
            const double panelWidth = 220;    // overlay is 180 wide plus its padding and border
            const double panelHeight = 460;   // tall enough now that the layer switches are in

            double mapLeft = Canvas.GetLeft(MapContainer);
            if (double.IsNaN(mapLeft)) mapLeft = 0;

            var screen = ScreenBoundsFor(this);

            // Beside the map, on the mini-map's own monitor. Spilling onto the next screen was
            // the old behaviour and it put the panel on a display the user was not looking at.
            double offsetX = mapLeft + _mapWidth + gap;
            if (Left + offsetX + panelWidth > screen.Right)
                offsetX = mapLeft - panelWidth - gap;
            if (Left + offsetX < screen.Left)
                offsetX = Math.Max(screen.Left - Left, mapLeft + _mapWidth + gap);

            double offsetY = 0;
            if (Top + offsetY + panelHeight > screen.Bottom)
                offsetY = Math.Min(0, screen.Bottom - panelHeight - Top);
            if (Top + offsetY < screen.Top)
                offsetY = screen.Top - Top;

            SettingsPopup.HorizontalOffset = offsetX;
            SettingsPopup.VerticalOffset = offsetY;
            SettingsPopup.IsOpen = true;
        }

        public void CloseSettings() => SettingsPopup.IsOpen = false;

        /// <summary>
        /// The working area of the monitor the window sits on, in the same device-independent
        /// units as <see cref="Window.Left"/>.
        ///
        /// SystemParameters describes the whole virtual desktop, which is why the panel used to
        /// open on the neighbouring screen: "does it fit before the right edge" was asking about
        /// the far edge of the last monitor, not the one the mini-map is on.
        /// </summary>
        private static Rect ScreenBoundsFor(Window window)
        {
            try
            {
                var origin = new System.Drawing.Point(
                    (int)(double.IsNaN(window.Left) ? 0 : window.Left),
                    (int)(double.IsNaN(window.Top) ? 0 : window.Top));

                var area = System.Windows.Forms.Screen.FromPoint(origin).WorkingArea;

                // Screen reports physical pixels; Window.Left is device-independent.
                double scale = 1.0;
                var source = PresentationSource.FromVisual(window);
                if (source?.CompositionTarget != null)
                    scale = source.CompositionTarget.TransformToDevice.M11;
                if (scale <= 0) scale = 1.0;

                return new Rect(area.Left / scale, area.Top / scale, area.Width / scale, area.Height / scale);
            }
            catch
            {
                return new Rect(
                    SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            }
        }

        /// <summary>
        /// Keeps the popup where it is on screen after the window's own position changed.
        /// </summary>
        private void HoldSettingsPopupInPlace(double dLeft, double dTop)
        {
            if (!SettingsPopup.IsOpen) return;
            if (Math.Abs(dLeft) < 0.01 && Math.Abs(dTop) < 0.01) return;

            SettingsPopup.HorizontalOffset -= dLeft;
            SettingsPopup.VerticalOffset -= dTop;
        }

        // ── Settings application ────────────────────────────────────────────────

        public void ApplyLoadedSettings(RustPlusDesk.Services.MiniMapSettings settings)
        {
            _shapeIndex = settings.ShapeIndex;

            if (MapShapeBorder != null)
                MapShapeBorder.Opacity = settings.Opacity;

            if (TimeOverlayBorder != null)
                TimeOverlayBorder.Visibility = settings.ShowTime ? Visibility.Visible : Visibility.Collapsed;

            if (PopOverlayBorder != null)
                PopOverlayBorder.Visibility = settings.ShowPop ? Visibility.Visible : Visibility.Collapsed;

            ApplyLayerVisibility(settings);
            UpdateSize(settings.Size, updateSlider: false);
        }

        /// <summary>
        /// Switches the mirrored layers on and off.
        ///
        /// With the texture gone the frame and the backdrop go too: what is left is a fully
        /// transparent window showing only the layers still enabled — the point of turning the
        /// texture off is to see teammates over the game, not to stare into a dark disc.
        /// </summary>
        public void ApplyLayerVisibility(RustPlusDesk.Services.MiniMapSettings settings)
        {
            Vis(LayerTexture, settings.ShowTexture);
            Vis(LayerGrid, settings.ShowGrid);
            Vis(LayerDrawings, settings.ShowDrawings);
            Vis(LayerIcons, settings.ShowIcons);
            Vis(LayerPlayers, settings.ShowPlayers);

            if (MapBackdrop != null)
                MapBackdrop.Visibility = settings.ShowTexture ? Visibility.Visible : Visibility.Collapsed;

            if (MapShapeBorder != null)
                MapShapeBorder.BorderThickness = new Thickness(settings.ShowTexture ? 1 : 0);

            // With nothing left to draw, the map stops holding cells and the dock closes up over
            // it. Turning a layer back on brings the space back.
            bool anyLayer = settings.ShowTexture || settings.ShowGrid
                         || settings.ShowDrawings || settings.ShowIcons || settings.ShowPlayers;

            if (anyLayer != _mapLayersOn)
            {
                _mapLayersOn = anyLayer;
                RebuildTiles();
            }

            static void Vis(UIElement? el, bool on)
            {
                if (el != null) el.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private bool _isUpdatingSize = false;

        public void UpdateSize(double newSize, bool updateSlider = true)
        {
            if (_isUpdatingSize) return;
            _isUpdatingSize = true;
            try
            {
                newSize = Math.Max(160, Math.Min(newSize, 800));
                _shapeIndex = SettingsOverlay?.CmbShape?.SelectedIndex ?? _shapeIndex;

                // Where the map's middle sits on screen right now. Read before the new size is
                // applied, because that is the point the resize has to leave alone.
                Point? anchor = null;
                if (!double.IsNaN(Left) && !double.IsNaN(Top))
                {
                    double mx = Canvas.GetLeft(MapContainer);
                    double my = Canvas.GetTop(MapContainer);
                    anchor = new Point(
                        Left + (double.IsNaN(mx) ? 0 : mx) + _mapWidth / 2.0,
                        Top + (double.IsNaN(my) ? 0 : my) + _mapHeight / 2.0);
                }

                _mapWidth = newSize;
                _mapHeight = _shapeIndex == 2 ? newSize * 9.0 / 16.0 : newSize;

                double cornerRadius = _shapeIndex == 0 ? newSize / 2.0 : 12;

                if (MapContainer != null)
                {
                    MapContainer.Width = _mapWidth;
                    MapContainer.Height = _mapHeight;
                }
                if (MapShapeBorder != null)
                    MapShapeBorder.CornerRadius = new CornerRadius(cornerRadius);
                if (MapClipHost != null)
                {
                    // A Border does not clip its child to its own rounded corners, so the
                    // mirrors need the geometry applied by hand or the circle shows a square.
                    MapClipHost.Clip = new RectangleGeometry(
                        new Rect(0, 0, _mapWidth, _mapHeight), cornerRadius, cornerRadius);
                }

                RepositionTiles();
                LayoutDock(anchor);

                if (updateSlider && SettingsOverlay != null)
                    SettingsOverlay.UpdateSliderValue(newSize);

                // The viewbox aspect follows the tile, so a shape change has to reapply it.
                ApplyViewbox();
            }
            finally
            {
                _isUpdatingSize = false;
            }
        }

        /// <summary>
        /// Sizes the window to the bounding box of the map tile and every command tile.
        ///
        /// <paramref name="mapCentreAnchor"/> is the screen point the map's middle held before
        /// the change; the window is placed so it still holds it. Anything that moves the window
        /// runs through here, so the settings popup can be held still at the same time.
        /// </summary>
        private void LayoutDock(Point? mapCentreAnchor = null)
        {
            var bounds = MeasureDockBounds();

            // Tiles may sit left of or above the map; shift everything so the canvas origin is
            // the window origin, and the window keeps a positive size.
            double shiftX = -bounds.X;
            double shiftY = -bounds.Y;
            ShiftDockChildren(shiftX, shiftY);

            double oldLeft = Left, oldTop = Top;

            Width = Math.Max(1, bounds.Width);
            Height = Math.Max(1, bounds.Height);

            // Only meaningful while the map is on the dock; with it gone there is no centre to
            // hold and the dock simply keeps its own top-left corner.
            if (mapCentreAnchor is { } anchor && MapContainer.Visibility == Visibility.Visible)
            {
                double mx = Canvas.GetLeft(MapContainer);
                double my = Canvas.GetTop(MapContainer);
                if (!double.IsNaN(mx) && !double.IsNaN(my))
                {
                    Left = anchor.X - (mx + _mapWidth / 2.0);
                    Top = anchor.Y - (my + _mapHeight / 2.0);
                }
            }

            ClampToScreen();

            if (!double.IsNaN(oldLeft) && !double.IsNaN(oldTop))
                HoldSettingsPopupInPlace(Left - oldLeft, Top - oldTop);

            PositionChrome();
        }

        /// <summary>Stretches the title bar across the dock and parks it on the top edge.</summary>
        private void PositionChrome()
        {
            if (DockTitleBar == null) return;

            DockTitleBar.Width = Math.Max(120, Width);
            Canvas.SetLeft(DockTitleBar, 0);
            Canvas.SetTop(DockTitleBar, 0);
        }

        /// <summary>
        /// Keeps the dock reachable.
        ///
        /// The title bar is the only way to move the dock once the map is off, and it sits on the
        /// window's top edge — so that edge may never leave the screen. Horizontally a strip is
        /// enough: the dock can hang off either side as long as some of the bar can be grabbed.
        /// </summary>
        private void ClampToScreen()
        {
            if (double.IsNaN(Left) || double.IsNaN(Top)) return;

            const double grabbable = 120;
            var screen = ScreenBoundsFor(this);

            double top = Math.Max(screen.Top, Top);

            // Nothing below the bottom edge either, unless the dock is taller than the screen —
            // then the top wins, because that is where the handle is.
            if (top + Height > screen.Bottom)
                top = Math.Max(screen.Top, screen.Bottom - Height);

            double left = Math.Min(Left, screen.Right - grabbable);
            left = Math.Max(left, screen.Left - Math.Max(0, Width - grabbable));

            if (Math.Abs(left - Left) > 0.01) Left = left;
            if (Math.Abs(top - Top) > 0.01) Top = top;
        }

        /// <summary>
        /// The rectangle every placed tile fits into. The map is one of those tiles now, so it
        /// needs no separate term — and a dock with no tiles at all still gets a usable size, or
        /// the window would collapse to nothing and take the title bar with it.
        /// </summary>
        private Rect MeasureDockBounds()
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var rect in TileBounds())
            {
                minX = Math.Min(minX, rect.X);
                minY = Math.Min(minY, rect.Y);
                maxX = Math.Max(maxX, rect.Right);
                maxY = Math.Max(maxY, rect.Bottom);
            }

            if (minX > maxX || minY > maxY)
                return new Rect(0, 0, EmptyDockWidth, EmptyDockHeight);

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        // Just the title bar, wide enough to hold its buttons: what is left when the map is off
        // and no tile has been added yet.
        private const double EmptyDockWidth = 200;
        private const double EmptyDockHeight = 30;

        private void ShiftDockChildren(double dx, double dy)
        {
            if (Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01) return;

            foreach (UIElement child in DockCanvas.Children)
            {
                double x = Canvas.GetLeft(child);
                double y = Canvas.GetTop(child);
                Canvas.SetLeft(child, (double.IsNaN(x) ? 0 : x) + dx);
                Canvas.SetTop(child, (double.IsNaN(y) ? 0 : y) + dy);
            }
        }
    }
}
