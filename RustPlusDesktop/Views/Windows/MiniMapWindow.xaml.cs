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

            MouseLeftButtonDown += (_, __) => DragMove();

            // Zoom nur für Mini-Map
            MouseWheel += MiniMapWindow_MouseWheel;

            // Panning mit rechter Maustaste
            MouseRightButtonDown += MiniMapWindow_MouseRightButtonDown;
            MouseRightButtonUp += MiniMapWindow_MouseRightButtonUp;
            MouseMove += MiniMapWindow_MouseMove;
        }

        // wird vom MainWindow aufgerufen
        public void SetViewbox(Rect viewbox)
        {
            _baseViewbox = viewbox;  // nur merken
            ApplyViewbox();          // Basis + User-Zoom + User-Pan anwenden
        }

        private void MiniMapWindow_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                // SHIFT gedrückt → Fenstergröße ändern
                double factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;

                // aktuelle Größe
                double newW = Width * factor;
                double newH = Height * factor;

                // Mindest- und Maximalwerte, damit’s nicht verschwindet oder riesig wird
                newW = Math.Max(160, Math.Min(newW, 600));
                newH = Math.Max(160, Math.Min(newH, 600));

                Width = newW;
                Height = newH;

                // Kreis und Rand anpassen
                Circle.Width = newW;
                Circle.Height = newH;

                if (Content is Grid g)
                {
                    foreach (var child in g.Children)
                    {
                        if (child is Border b)
                        {
                            b.CornerRadius = new CornerRadius(newW / 2.0);
                        }
                    }
                }

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
            double winW = Math.Max(1.0, this.ActualWidth);
            double winH = Math.Max(1.0, this.ActualHeight);

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

        // Falls du später eckig statt rund willst:
        public void SetSquare(bool square)
        {
            if (square)
            {
                Circle.Visibility = Visibility.Collapsed;
                Content = new Border
                {
                    Width = Width,
                    Height = Height,
                    CornerRadius = new CornerRadius(12),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(102, 0, 0, 0)),
                    BorderThickness = new Thickness(1),
                    Background = Brushes.Transparent,
                  //  Child = new Rectangle
                  //  {
                  //      Fill = MirrorBrush,
                  //      RadiusX = 12,
                  //      RadiusY = 12
                   // }
                };
            }
            else
            {
                // zurück auf rund
                InitializeComponent();
            }
        }
    }
}