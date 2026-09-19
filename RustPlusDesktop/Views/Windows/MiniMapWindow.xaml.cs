using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RustPlusDesk
{
    /// <summary>
    /// The map layers the mini-map mirrors, each as its own visual.
    ///
    /// They are siblings in the main map's scene grid, so they share one coordinate space and
    /// one viewbox drives every brush. Any of them may be null before a map is loaded.
    /// </summary>
    public sealed record MiniMapLayers(
        Visual? Texture,
        Visual? Heatmap,
        Visual? Grid,
        Visual? Drawings,
        Visual? Icons,
        Visual? Players,
        Visual? Deaths,
        Visual? NoBuild);

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
                if (!IsFromWindowContent(e.OriginalSource)) return;

                // A press a tile let through on purpose, so a control inside it could have it.
                // DragMove blocks until the button comes back up and would eat that press too.
                if (PressedOnTileControl(e.OriginalSource)) return;

                startDragPos = e.GetPosition(this);

                double fromLeft = Left, fromTop = Top;

                DragMove();

                // Dragging the map moves the map, not the dock. It has a position of its own
                // now, so the window Windows just moved has to be handed back and the distance
                // applied to the thing the pointer was actually on. Everything else - a press
                // on the dock's background - still moves the arrangement.
                bool onMap = PressedOnMap(e.OriginalSource);

                double dx = Left - fromLeft, dy = Top - fromTop;

                if (onMap)
                {
                    Left = fromLeft;
                    Top = fromTop;
                    MoveMapByPixels(dx, dy);
                }
                else
                {
                    MoveDockByPixels(dx, dy);
                }
            };
            MouseLeftButtonUp += (s, e) =>
            {
                if (!IsFromWindowContent(e.OriginalSource)) return;

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

                double fromLeft = Left, fromTop = Top;
                DragMove();
                MoveDockByPixels(Left - fromLeft, Top - fromTop);
            };

            // No clamping on LocationChanged any more. The window's position is derived from the
            // cells, and a cell cannot leave the screen - so a clamp could only ever disagree
            // with the next layout, which would put it straight back.

            // Runs after every Loaded handler, so the saved position is applied on top of
            // whatever the initial layout and the loaded settings worked out.
            ContentRendered += (_, __) => RestoreDockPosition();

            // The clip depends on the layers' measured size, which arrives after the layout pass
            // that UpdateSize triggers — so it is reapplied whenever that size settles.
            MapClipHost.SizeChanged += (_, __) => ApplyMapClip();

            SettingsOverlay.ParentWindow = this;
            InitCommandDock();
        }


        /// <summary>
        /// Whether the mini-map is asking the main map to keep building the grid.
        ///
        /// The main map skips the work entirely when its own grid is off, and the mini-map
        /// mirrors that same canvas — so its switch cannot be a filter over something already
        /// drawn. It has to ask for the layer to exist in the first place.
        /// </summary>
        public bool WantsGridLayer { get; private set; } = true;

        /// <summary>The same for death markers, which the main map also builds only on demand.</summary>
        public bool WantsDeathLayer { get; private set; } = true;

        /// <summary>
        /// The same for the building-blocked zones. Starts false, because the layer starts off:
        /// the zones do not exist until the map has been parsed.
        /// </summary>
        public bool WantsNoBuildLayer { get; private set; }

        /// <summary>
        /// Whether a mouse event came from this window's own content rather than from one of its
        /// popups.
        ///
        /// A popup renders in its own HWND with its own visual root, but its events still bubble
        /// into the window through the logical tree. Without this check, pressing anything in a
        /// popup that does not handle the press itself — a colour swatch, say, as opposed to a
        /// slider or a button — reached the window's DragMove, which blocks until the button
        /// comes back up and swallows the release. The swatch never saw its own click.
        /// </summary>
        private bool IsFromWindowContent(object? originalSource)
        {
            if (originalSource is not DependencyObject node) return true;

            DependencyObject root = node;
            while (true)
            {
                var parent = root is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(root)
                    : (root as FrameworkContentElement)?.Parent;

                if (parent == null) break;
                root = parent;
            }

            return ReferenceEquals(root, this);
        }

        /// <summary>
        /// Points every layer brush at its source. Called again whenever the main window
        /// rebuilds its map scene, since a fresh map may hand out fresh visuals.
        /// </summary>
        public void SetLayers(MiniMapLayers layers)
        {
            BrushTexture.Visual = layers.Texture;
            BrushHeatmap.Visual = layers.Heatmap;
            BrushGrid.Visual = layers.Grid;
            BrushDrawings.Visual = layers.Drawings;
            BrushIcons.Visual = layers.Icons;
            BrushPlayers.Visual = layers.Players;
            BrushDeaths.Visual = layers.Deaths;
            BrushNoBuild.Visual = layers.NoBuild;
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
            // A wheel over a popup scrolls that popup, it does not zoom the map behind it.
            if (!IsFromWindowContent(e.OriginalSource)) return;

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
            if (!IsFromWindowContent(e.OriginalSource)) return;

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

            foreach (var brush in new[] { BrushTexture, BrushHeatmap, BrushGrid, BrushDrawings, BrushIcons, BrushPlayers, BrushDeaths, BrushNoBuild })
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

            // Asked again on every open: the panel reads its settings once, but whether the
            // no-build layer has anything to show changes whenever a parse finishes - which
            // happens while this panel is closed.
            SettingsOverlay?.RefreshNoBuildAvailability();
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
        /// <summary>
        /// The working area of the monitor containing one point, in device-independent pixels.
        ///
        /// Used where the window cannot be asked yet - the grid has to know its monitor before
        /// the first layout, and the window's geometry at that moment is whatever the XAML said.
        /// </summary>
        private Rect ScreenBoundsForPoint(double x, double y)
        {
            try
            {
                double scale = 1.0;
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                    scale = source.CompositionTarget.TransformToDevice.M11;
                if (scale <= 0) scale = 1.0;

                var at = new System.Drawing.Point((int)Math.Round(x * scale), (int)Math.Round(y * scale));
                var area = System.Windows.Forms.Screen.FromPoint(at).WorkingArea;

                return new Rect(area.Left / scale, area.Top / scale, area.Width / scale, area.Height / scale);
            }
            catch
            {
                return ScreenBoundsFor(this);
            }
        }

        private static Rect ScreenBoundsFor(Window window)
        {
            try
            {
                // Screen speaks physical pixels; Window.Left is device-independent. The scale
                // has to be applied on the way in as well as on the way out - passing raw DIPs
                // to Screen picked the wrong monitor on any display that is not at 100%.
                double scale = 1.0;
                var source = PresentationSource.FromVisual(window);
                if (source?.CompositionTarget != null)
                    scale = source.CompositionTarget.TransformToDevice.M11;
                if (scale <= 0) scale = 1.0;

                double left = double.IsNaN(window.Left) ? 0 : window.Left;
                double top = double.IsNaN(window.Top) ? 0 : window.Top;
                double width = double.IsNaN(window.Width) || window.Width <= 0 ? 1 : window.Width;
                double height = double.IsNaN(window.Height) || window.Height <= 0 ? 1 : window.Height;

                // By the whole window rather than its top-left corner. FromRectangle picks the
                // monitor the window mostly covers, which is the one somebody would point at.
                //
                // The corner was actively wrong: growing the map holds its centre still, so the
                // dock's left edge travels outwards as it gets bigger. Cross a monitor boundary
                // with that one pixel and the overlay - the grid and the bar with it - moved to
                // the other screen, while the dock stayed where it was.
                var rect = new System.Drawing.Rectangle(
                    (int)Math.Round(left * scale), (int)Math.Round(top * scale),
                    (int)Math.Round(width * scale), (int)Math.Round(height * scale));

                var area = System.Windows.Forms.Screen.FromRectangle(rect).WorkingArea;

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
            // Nothing to show unless a heatmap is active on the main map, in which
            // case ImgHeatmap carries it and the brush picks it up on its own.
            Vis(LayerHeatmap, settings.ShowHeatmap);
            Vis(LayerGrid, settings.ShowGrid);
            Vis(LayerDrawings, settings.ShowDrawings);
            Vis(LayerIcons, settings.ShowIcons);
            Vis(LayerPlayers, settings.ShowPlayers);
            Vis(LayerDeaths, settings.ShowDeaths);
            Vis(LayerNoBuild, settings.ShowNoBuild);

            // The main map only builds the grid and the death pins when something wants them,
            // so the mini-map has to say so — its switch is not a filter over something that is
            // always there.
            bool wantsGrid = settings.ShowGrid;
            bool wantsDeaths = settings.ShowDeaths;
            bool wantsNoBuild = settings.ShowNoBuild;

            if (wantsGrid != WantsGridLayer || wantsDeaths != WantsDeathLayer || wantsNoBuild != WantsNoBuildLayer)
            {
                WantsGridLayer = wantsGrid;
                WantsDeathLayer = wantsDeaths;
                WantsNoBuildLayer = wantsNoBuild;
                (Application.Current?.MainWindow as Views.MainWindow)?.RefreshIndependentLayers();
            }

            if (MapBackdrop != null)
                MapBackdrop.Visibility = settings.ShowTexture ? Visibility.Visible : Visibility.Collapsed;

            if (MapShapeBorder != null)
                MapShapeBorder.BorderThickness = new Thickness(settings.ShowTexture ? 1 : 0);

            // With nothing left to draw, the map stops holding cells and the dock closes up over
            // it. Turning a layer back on brings the space back.
            bool anyLayer = settings.ShowTexture || settings.ShowGrid || settings.ShowDrawings
                         || settings.ShowIcons || settings.ShowPlayers || settings.ShowDeaths
                         || settings.ShowNoBuild;

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

        private double _mapCornerRadius = 130;

        /// <summary>
        /// Rounds off the mirrored layers. A Border does not clip its child to its own rounded
        /// corners, so the geometry has to be applied by hand or the circle shows a square.
        ///
        /// Measured from what is actually being clipped rather than from the tile's size: the
        /// border sits inside the tile, so the layers are a pixel or two smaller. Using the tile
        /// size made the clip circle wider than its content, and the content's own straight edge
        /// showed through as a flat cut on the right and the bottom.
        /// </summary>
        private void ApplyMapClip()
        {
            if (MapClipHost == null) return;

            double w = MapClipHost.ActualWidth, h = MapClipHost.ActualHeight;
            if (w <= 0 || h <= 0) return;

            double radius = Math.Min(_mapCornerRadius, Math.Min(w, h) / 2.0);
            MapClipHost.Clip = new RectangleGeometry(new Rect(0, 0, w, h), radius, radius);
        }

        /// <summary>
        /// Sets the map's shape - 0 circle, 1 square, 2 16:9 - and makes it stick.
        ///
        /// <see cref="_shapeIndex"/> is the shape; the settings panel's ComboBox only shows it.
        /// It used to be the other way round, with UpdateSize reading the control, which meant
        /// anything that resized the map while the panel had never been opened - applying a
        /// saved arrangement, most visibly - quietly took the control's default and put the map
        /// back to a circle.
        ///
        /// The write goes to the settings file directly rather than through the panel, because
        /// the panel's own save is a no-op until its popup has been opened once: its controls do
        /// not exist before that, and CurrentSettings returns null when they are missing.
        /// </summary>
        /// <summary>
        /// Writes the shape to the settings file directly.
        ///
        /// Not through the settings panel: its own save is a no-op until its popup has been
        /// opened once, because its controls do not exist before that.
        /// </summary>
        internal void PersistMapShape(int shapeIndex)
        {
            var stored = RustPlusDesk.Services.StorageService.LoadCache<RustPlusDesk.Services.MiniMapSettings>("minimap_settings");
            if (stored != null)
                RustPlusDesk.Services.StorageService.SaveCache("minimap_settings", stored with { ShapeIndex = shapeIndex });
        }

        /// <summary>The map's height for a given width, which only the 16:9 shape changes.</summary>
        internal double MapHeightFor(double width, int shapeIndex) =>
            shapeIndex == 2 ? width * 9.0 / 16.0 : width;

        public void SetMapShape(int shapeIndex, bool persist = true)
        {
            shapeIndex = Math.Max(0, Math.Min(2, shapeIndex));
            if (_shapeIndex == shapeIndex) return;

            _shapeIndex = shapeIndex;

            if (persist) PersistMapShape(shapeIndex);

            SettingsOverlay?.SyncShapeSelection(shapeIndex);

            // Re-applies the geometry at the current size: the corner radius and, at 16:9, the
            // height both come out of the shape.
            UpdateSize(_mapWidth, updateSlider: false);
        }

        private bool _isUpdatingSize = false;

        public void UpdateSize(double newSize, bool updateSlider = true)
        {
            if (_isUpdatingSize) return;
            _isUpdatingSize = true;
            try
            {
                newSize = Math.Max(160, Math.Min(newSize, 800));

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

                _mapCornerRadius = cornerRadius;
                ApplyMapClip();

                // LayoutDock re-derives every tile's position from its cell, which is all a
                // resize needs: the map's cell span changed, so the seam moved with it.
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
            double oldLeft = Left, oldTop = Top;

            // The monitor the dock is on *now*, before its size changes.
            //
            // Applying a wide arrangement grows the window rightwards, and once it reaches far
            // enough the monitor it mostly covers becomes the next one along - so a clamp that
            // re-measured afterwards would pull the whole dock onto a screen it was never on,
            // following a move it had caused itself. An arrangement is a shape, not a place: it
            // belongs on whichever screen the dock was already sitting on.
            // The map's cell span follows its free size, so a resize — or simply loading the
            // saved size after the tiles were placed — can leave neighbours underneath it.
            SyncMapCellSpan();
            ResolveOverlaps();

            // Cells are places on the monitor, so the window is drawn around whatever is
            // visible rather than the other way round. Nothing here writes to a tile: the
            // origin moves the window, never the arrangement.
            var bounds = CellBounds();

            ApplyTilePositions();

            Width = Math.Max(1, bounds.Width);
            Height = Math.Max(1, bounds.Height);

            Left = bounds.X;
            Top = bounds.Y;

            if (!double.IsNaN(oldLeft) && !double.IsNaN(oldTop))
                HoldSettingsPopupInPlace(Left - oldLeft, Top - oldTop);
            FollowAiAnswer();

            PositionChrome();

            // The dock moved or resized, so the grid the overlay painted is measured against
            // the wrong origin. Only while a drag is in flight - otherwise there is none.
            if (_draggingTile != null)
            {
                PaintOverlayGrid();
                ShowOverlayDropTarget();
            }
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
        /// Keeps the dock on the screen.
        ///
        /// A dock that fits is pulled fully into view, in both directions. It used to be lenient
        /// horizontally — a strip of 120 pixels was enough, and the rest could hang off the edge
        /// — because the title bar was the only way to move the dock and sat on the dock itself,
        /// so what mattered was that some of it stayed grabbable.
        ///
        /// The bar is on the overlay now, pinned to the top of the screen and reachable wherever
        /// the dock happens to be, so that reason is gone. What the leniency left behind was a
        /// real problem: loading a wide arrangement while the dock was anchored near the right
        /// edge — which is exactly where the built-in default parks it — pushed the right-hand
        /// widgets off the screen, and nothing brought them back.
        ///
        /// A dock genuinely larger than the screen still hangs off, because it has to; the strip
        /// is what stays reachable then.
        /// </summary>
        /// <summary>
        /// Keeps the dock somewhere it can be reached and, when asked, fully in view.
        ///
        /// <paramref name="pullIntoView"/> separates two jobs that were one and must not be.
        ///
        /// While the dock is being dragged, only reachability matters, and a strip of 120 pixels
        /// is the whole rule. Pulling it fully onto a monitor mid-drag makes a monitor boundary
        /// impossible to cross: the dock is still mostly on the screen it is leaving, so that is
        /// the screen the clamp measures against, and it hauls it back on every mouse move. The
        /// dock ends up stuck on one monitor with no way off it.
        ///
        /// After a structural change - a template applied, the map resized - there is no drag to
        /// fight, and reachability is not enough. That is when a dock that fits gets pulled fully
        /// into view, so a wide arrangement loaded while the dock sat near an edge cannot leave
        /// its right-hand widgets off the screen.
        /// </summary>
        /// <param name="screen">
        /// The monitor to clamp against, when the caller knows better than the current geometry
        /// does - a layout change measures the screen before it resizes the window, because
        /// growing the window can move it onto the next monitor and the clamp must not chase it
        /// there. Null asks for whichever monitor the dock mostly covers right now.
        /// </param>
        private void ClampToScreen(bool pullIntoView = false, Rect? screen = null)
        {
            if (double.IsNaN(Left) || double.IsNaN(Top)) return;

            const double grabbable = 120;
            var bounds = screen ?? ScreenBoundsFor(this);

            double top = Math.Max(bounds.Top, Top);

            // Nothing below the bottom edge either, unless the dock is taller than the screen —
            // then the top wins, because that is where the handle is.
            if (top + Height > bounds.Bottom)
                top = Math.Max(bounds.Top, bounds.Bottom - Height);

            double left;
            if (pullIntoView && Width <= bounds.Width)
            {
                left = Math.Min(Left, bounds.Right - Width);
                left = Math.Max(left, bounds.Left);
            }
            else
            {
                left = Math.Min(Left, bounds.Right - grabbable);
                left = Math.Max(left, bounds.Left - Math.Max(0, Width - grabbable));
            }

            if (Math.Abs(left - Left) > 0.01) Left = left;
            if (Math.Abs(top - Top) > 0.01) Top = top;
        }

        // Just the title bar, wide enough to hold its buttons: what is left when the map is off
        // and no tile has been added yet.
        private const double EmptyDockWidth = 200;
        private const double EmptyDockHeight = 30;

    }
}
