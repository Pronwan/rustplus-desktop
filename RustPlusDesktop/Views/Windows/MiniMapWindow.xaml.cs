using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace RustPlusDesk
{
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

        public MiniMapWindow(Visual mapVisual)
        {
            InitializeComponent();
            MirrorBrush.Visual = mapVisual;

            // Zoom nur für Mini-Map
            MouseWheel += MiniMapWindow_MouseWheel;

            // Panning mit rechter Maustaste
            MouseRightButtonDown += MiniMapWindow_MouseRightButtonDown;
            MouseRightButtonUp += MiniMapWindow_MouseRightButtonUp;
            MouseMove += MiniMapWindow_MouseMove;

            // Click detection for centering
            Point startDragPos = new Point();
            MouseLeftButtonDown += (s, e) => { startDragPos = e.GetPosition(this); DragMove(); };
            MouseLeftButtonUp += (s, e) =>
            {
                var endPos = e.GetPosition(this);
                if (Math.Abs(endPos.X - startDragPos.X) < 5 && Math.Abs(endPos.Y - startDragPos.Y) < 5)
                {
                    OnClicked?.Invoke();
                }
            };

            Loaded += (s, e) =>
            {
                var settings = RustPlusDesk.Services.StorageService.LoadCache<RustPlusDesk.Services.MiniMapSettings>("minimap_settings");
                if (settings != null)
                {
                    CmbShape.SelectedIndex = settings.ShapeIndex;
                    SliOpacity.Value = settings.Opacity;
                    ChkShowTime.IsChecked = settings.ShowTime;
                    UpdateSize(settings.Size, updateSlider: true);
                }
                else
                {
                    CmbShape.SelectedIndex = 0; // Standard Circle
                    SliOpacity.Value = 1.0;
                    ChkShowTime.IsChecked = false;
                    UpdateSize(260.0, updateSlider: true);
                }
            };
        }

        private int _viewboxId = 0;

        // wird vom MainWindow aufgerufen
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

            // Wenn der Sprung zu groß ist (z.B. Erster Start oder Teleport), direkt setzen
            double dist = Math.Sqrt(Math.Pow(targetPos.X - startPos.X, 2) + Math.Pow(targetPos.Y - startPos.Y, 2));
            if (dist > 500)
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
                double newW = Width * factor;
                UpdateSize(newW, updateSlider: true);

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
            MouseRightButtonDown += (s, e) =>
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
                // sonst normales panning wie oben
            };
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
            // (Window.Width/Height nimmst du aus dem tatsächlichen Fenster)
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

            // Pan addieren – wir verschieben einfach den Mittelpunkt
            double finalCx = cx - _panX;
            double finalCy = cy - _panY;

            var vb = new Rect(finalCx - w / 2.0, finalCy - h / 2.0, w, h);

            MirrorBrush.ViewboxUnits = BrushMappingMode.Absolute;
            MirrorBrush.Viewbox = vb;
            MirrorBrush.Stretch = Stretch.Uniform;
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsPopup.Visibility = Visibility.Visible;
            SettingsHoverBorder.Visibility = Visibility.Collapsed;
        }

        private void BtnSettingsClose_Click(object sender, RoutedEventArgs e)
        {
            SettingsPopup.Visibility = Visibility.Collapsed;
            SettingsHoverBorder.Visibility = Visibility.Visible;
        }

        private void CmbShape_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSize(Width, updateSlider: true);
        }

        private void SliOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MapShapeBorder != null)
                MapShapeBorder.Opacity = e.NewValue;

            int pct = (int)Math.Round(e.NewValue * 100);
            if (LblOpacity != null)
                LblOpacity.Text = $"Opacity: {pct}%";

            SaveSettings();
        }

        private void SliSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSize(e.NewValue, updateSlider: false);
        }

        private void SaveSettings()
        {
            if (CmbShape == null || SliOpacity == null || SliSize == null || ChkShowTime == null) return;

            var settings = new RustPlusDesk.Services.MiniMapSettings(
                CmbShape.SelectedIndex,
                SliSize.Value,
                SliOpacity.Value,
                ChkShowTime.IsChecked == true
            );

            RustPlusDesk.Services.StorageService.SaveCache("minimap_settings", settings);
        }

        private bool _isUpdatingSize = false;
        private void UpdateSize(double newSize, bool updateSlider = true)
        {
            if (_isUpdatingSize) return;
            _isUpdatingSize = true;
            try
            {
                newSize = Math.Max(160, Math.Min(newSize, 800));
                
                Width = newSize;
                Height = newSize;

                int idx = CmbShape?.SelectedIndex ?? 0;
                if (MapContainer != null && MapShapeBorder != null)
                {
                    if (idx == 0) // Circle
                    {
                        MapContainer.Width = newSize;
                        MapContainer.Height = newSize;
                        MapShapeBorder.CornerRadius = new CornerRadius(newSize / 2.0);
                    }
                    else if (idx == 1) // Square
                    {
                        MapContainer.Width = newSize;
                        MapContainer.Height = newSize;
                        MapShapeBorder.CornerRadius = new CornerRadius(12);
                    }
                    else if (idx == 2) // Rectangle (16:9)
                    {
                        MapContainer.Width = newSize;
                        MapContainer.Height = newSize * 9.0 / 16.0;
                        MapShapeBorder.CornerRadius = new CornerRadius(12);
                    }
                }

                int pct = (int)Math.Round((newSize / 260.0) * 100);
                if (LblSize != null)
                    LblSize.Text = $"Size: {pct}% (SHIFT+Mousewheel)";

                if (updateSlider && SliSize != null)
                    SliSize.Value = newSize;
            }
            finally
            {
                _isUpdatingSize = false;
            }

            SaveSettings();
        }

        private void ChkShowTime_Changed(object sender, RoutedEventArgs e)
        {
            if (TimeOverlayBorder != null)
                TimeOverlayBorder.Visibility = (ChkShowTime.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;

            SaveSettings();
        }
    }
}