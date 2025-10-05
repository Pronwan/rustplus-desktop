using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace RustPlusDesk
{
    public partial class MiniMapWindow : Window
    {
        public MiniMapWindow(Visual mapVisual)
        {
            InitializeComponent();
            MirrorBrush.Visual = mapVisual;        // <- Live-Spiegel der bestehenden Karte
            MouseLeftButtonDown += (_, __) => DragMove();  // Fenster per Maus ziehen
        }

        public void SetViewbox(Rect viewbox)
        {
            MirrorBrush.ViewboxUnits = BrushMappingMode.Absolute;
            MirrorBrush.Viewbox = viewbox;

            // isotrope Skalierung, keine Verzerrung
            MirrorBrush.Stretch = Stretch.Uniform;      // <— statt Fill
                                                        // (bei einem runden, quadratischen Fenster ist Uniform perfekt)
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