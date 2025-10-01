using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace RustPlusDesk
{
    public enum CrosshairStyle
    {
        GreenDot,
        MiniGreen,
        OpenCrossRG,
        ThinRedCircle
    }

    public partial class CrosshairWindow : Window
    {
        private CrosshairStyle _style;

        public CrosshairWindow()
        {
            InitializeComponent();
            SetStyle(CrosshairStyle.GreenDot);
        }

        public void SetStyle(CrosshairStyle style)
        {
            _style = style;

            // Fenstergröße je Stil
            switch (_style)
            {
                case CrosshairStyle.MiniGreen:
                    Width = Height = 24; // sehr klein
                    break;
                case CrosshairStyle.OpenCrossRG:
                    Width = Height = 32; // deutlich kleiner als vorher
                    break;
                case CrosshairStyle.ThinRedCircle:
                    Width = Height = 36;
                    break;
                case CrosshairStyle.GreenDot:
                default:
                    Width = Height = 32;
                    break;
            }

            RenderStyle();
        }

        private void RenderStyle()
        {
            if (Presenter != null) Presenter.Content = null;

            switch (_style)
            {
                case CrosshairStyle.GreenDot:
                    Presenter.Content = BuildGreenDot();
                    break;
                case CrosshairStyle.MiniGreen:
                    Presenter.Content = BuildMiniGreen();
                    break;
                case CrosshairStyle.OpenCrossRG:
                    Presenter.Content = BuildOpenCrossRG_Small();
                    break;
                case CrosshairStyle.ThinRedCircle:
                    Presenter.Content = BuildThinRedCircle();
                    break;
            }
        }

        private UIElement BuildGreenDot()
        {
            var g = new Grid();
            g.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Brushes.Lime });
            g.Children.Add(new Ellipse { Width = 14, Height = 14, Stroke = Brushes.Lime, StrokeThickness = 1, Opacity = 0.35 });
            return g;
        }

        private UIElement BuildMiniGreen()
        {
            var g = new Grid();
            // winziger Punkt ohne Glow/Schnickschnack
            g.Children.Add(new Ellipse { Width = 3, Height = 3, Fill = Brushes.Lime });
            return g;
        }

        private UIElement BuildOpenCrossRG_Small()
        {
            // kleines offenes Kreuz
            var canvas = new Canvas
            {
                Width = Width,
                Height = Height,
                IsHitTestVisible = false
            };

            double cx = Width / 2.0, cy = Height / 2.0;
            double len = 6;   // vorher ~14
            double gap = 3;   // vorher ~8
            double th = 1.2; // dünner

            canvas.Children.Add(LineV(cx, cy - gap - len, cy - gap, Brushes.Red, th));   // oben (rot)
            canvas.Children.Add(LineV(cx, cy + gap, cy + gap + len, Brushes.Lime, th)); // unten (grün)
            canvas.Children.Add(LineH(cx - gap - len, cx - gap, cy, Brushes.Lime, th)); // links (grün)
            canvas.Children.Add(LineH(cx + gap, cx + gap + len, cy, Brushes.Red, th));  // rechts (rot)

            return canvas;

            static UIElement LineH(double x1, double x2, double y, Brush b, double t) =>
                new Line { X1 = x1, X2 = x2, Y1 = y, Y2 = y, Stroke = b, StrokeThickness = t, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };

            static UIElement LineV(double x, double y1, double y2, Brush b, double t) =>
                new Line { X1 = x, X2 = x, Y1 = y1, Y2 = y2, Stroke = b, StrokeThickness = t, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        }

        private UIElement BuildThinRedCircle()
        {
            var g = new Grid();
            g.Children.Add(new Ellipse
            {
                Width = 14,
                Height = 14,
                Stroke = new SolidColorBrush(Color.FromRgb(255, 120, 120)),
                StrokeThickness = 1
            });
            g.Children.Add(new Ellipse { Width = 2, Height = 2, Fill = new SolidColorBrush(Color.FromRgb(255, 170, 170)) });
            return g;
        }
    }
}