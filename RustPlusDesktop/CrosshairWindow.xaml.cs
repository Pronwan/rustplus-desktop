using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
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
        ThinRedCircle,
        SquareDot,         // neu
        MagentaDot,        // neu
        MagentaOpenCross,  // neu
        RangeLine          // neu
    }

    public partial class CrosshairWindow : Window
    {
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x00000020;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).Handle;
            var styles = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            var newStyles = (IntPtr)((styles.ToInt64())
                                     | WS_EX_TRANSPARENT
                                     | WS_EX_NOACTIVATE
                                     | WS_EX_TOOLWINDOW);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, newStyles);
        }

        
        private CrosshairStyle _style;

        public CrosshairWindow()
        {
            InitializeComponent();
            Presenter.IsHitTestVisible = false;
            SetStyle(CrosshairStyle.GreenDot);
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased); // Linien wirken bei 1px knackiger
            Presenter.IsHitTestVisible = false;
        }

        public void SetStyle(CrosshairStyle style)
        {
            _style = style;

            switch (_style)
            {
                case CrosshairStyle.MiniGreen: Width = Height = 24; break;
                case CrosshairStyle.OpenCrossRG: Width = Height = 32; break;
                case CrosshairStyle.ThinRedCircle: Width = Height = 36; break;
                case CrosshairStyle.SquareDot: Width = Height = 28; break;      // kompakt
                case CrosshairStyle.MagentaDot: Width = Height = 32; break;
                case CrosshairStyle.MagentaOpenCross: Width = Height = 32; break;
                case CrosshairStyle.RangeLine: Width = 36; Height = 72; break;   // höher für Skala
                case CrosshairStyle.GreenDot:
                default: Width = Height = 32; break;
            }

            RenderStyle();
        }

        private void RenderStyle()
        {
            if (Presenter != null) Presenter.Content = null;

            UIElement el = _style switch
            {
                CrosshairStyle.GreenDot => BuildGreenDot(),
                CrosshairStyle.MiniGreen => BuildMiniGreen(),
                CrosshairStyle.OpenCrossRG => BuildOpenCrossRG_Small(),
                CrosshairStyle.ThinRedCircle => BuildThinRedCircle(),
                CrosshairStyle.SquareDot => BuildSquareDot(),
                CrosshairStyle.MagentaDot => BuildMagentaDot(),
                CrosshairStyle.MagentaOpenCross => BuildMagentaOpenCross(),
                CrosshairStyle.RangeLine => BuildRangeLine(),
                _ => BuildGreenDot()
            };

            Presenter.Content = el;
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

        private static readonly Brush Magenta = new SolidColorBrush(Color.FromRgb(255, 0, 255));
        private static readonly Brush MagentaSoft = new SolidColorBrush(Color.FromRgb(255, 120, 255));

        private UIElement BuildSquareDot()
        {
            var canvas = new Canvas
            {
                Width = Width,
                Height = Height,
                IsHitTestVisible = false,
                SnapsToDevicePixels = true
            };

            double side = Math.Min(Width, Height) * 0.90;  // Gesamtgröße des Quadrats
            double cx = Width / 2.0, cy = Height / 2.0;
            double half = side / 2.0;

            // Ecklängen (wie weit die Corner-Linien in X/Y reinragen)
            double cornerLen = side * 0.18;    // ~18% der Seitenlänge
            double th = 1.4;                   // Linienstärke

            Brush stroke = Brushes.White;
            double x1 = cx - half, x2 = cx + half;
            double y1 = cy - half, y2 = cy + half;

            // Oben links
            canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x1 + cornerLen, Y2 = y1, Stroke = stroke, StrokeThickness = th });
            canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x1, Y2 = y1 + cornerLen, Stroke = stroke, StrokeThickness = th });

            // Oben rechts
            canvas.Children.Add(new Line { X1 = x2 - cornerLen, Y1 = y1, X2 = x2, Y2 = y1, Stroke = stroke, StrokeThickness = th });
            canvas.Children.Add(new Line { X1 = x2, Y1 = y1, X2 = x2, Y2 = y1 + cornerLen, Stroke = stroke, StrokeThickness = th });

            // Unten links
            canvas.Children.Add(new Line { X1 = x1, Y1 = y2, X2 = x1 + cornerLen, Y2 = y2, Stroke = stroke, StrokeThickness = th });
            canvas.Children.Add(new Line { X1 = x1, Y1 = y2 - cornerLen, X2 = x1, Y2 = y2, Stroke = stroke, StrokeThickness = th });

            // Unten rechts
            canvas.Children.Add(new Line { X1 = x2 - cornerLen, Y1 = y2, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = th });
            canvas.Children.Add(new Line { X1 = x2, Y1 = y2 - cornerLen, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = th });

            // Mittelpunkt
            var dot = new Ellipse { Width = 2.8, Height = 2.8, Fill = Brushes.White };
            Canvas.SetLeft(dot, cx - 1.4);
            Canvas.SetTop(dot, cy - 1.4);
            canvas.Children.Add(dot);

            return canvas;
        }

        private UIElement BuildMagentaDot()
        {
            var g = new Grid { IsHitTestVisible = false };

            // Außenring (soft / glow)
            g.Children.Add(new Ellipse
            {
                Width = 14,
                Height = 14,
                Stroke = MagentaSoft,
                StrokeThickness = 1.0,
                Fill = Brushes.Transparent,
                Opacity = 0.35
            });

            // Innenring (kräftiger, Mitte transparent)
            g.Children.Add(new Ellipse
            {
                Width = 8,
                Height = 8,
                Stroke = Magenta,          // kräftiger Ton
                StrokeThickness = 2.0,     // „dicker“
                Fill = Brushes.Transparent // Mitte bleibt transparent
            });

            return g;
        }

        private UIElement BuildMagentaOpenCross()
        {
            var canvas = new Canvas { Width = Width, Height = Height, IsHitTestVisible = false, SnapsToDevicePixels = true };
            double cx = Width / 2.0, cy = Height / 2.0;
            double len = 6, gap = 3, th = 1.2;

            canvas.Children.Add(LineV(cx, cy - gap - len, cy - gap, Magenta, th));   // oben
            canvas.Children.Add(LineV(cx, cy + gap, cy + gap + len, Magenta, th)); // unten
            canvas.Children.Add(LineH(cx - gap - len, cx - gap, cy, Magenta, th));       // links
            canvas.Children.Add(LineH(cx + gap, cx + gap + len, cy, Magenta, th)); // rechts
            return canvas;
        }

        private UIElement BuildRangeLine()
        {
            var canvas = new Canvas
            {
                Width = Width,
                Height = Height,
                IsHitTestVisible = false,
                SnapsToDevicePixels = true
            };

            double cx = Width / 2.0, cy = Height / 2.0;
            double lineLen = Height * 0.45;
            double th = 1.2;

            // zentraler Punkt
            var dot = new Ellipse
            {
                Width = 3,
                Height = 3,
                Fill = Brushes.Yellow
            };
            Canvas.SetLeft(dot, cx - 1.5);
            Canvas.SetTop(dot, cy - 1.5);
            canvas.Children.Add(dot);

            // Hauptlinie (nach unten)
            var mainLine = new Line
            {
                X1 = cx,
                Y1 = cy + 3,
                X2 = cx,
                Y2 = cy + lineLen,
                Stroke = Brushes.Yellow,
                StrokeThickness = th,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            canvas.Children.Add(mainLine);

            // Ticks: alle 10 px kurz, alle 50 px lang
            for (double dy = 10; dy <= lineLen; dy += 10)
            {
                bool major = Math.Abs(dy % 50) < 0.0001;
                double tick = major ? 8 : 4;
                double y = cy + dy;

                var tickLine = new Line
                {
                    X1 = cx - tick,
                    Y1 = y,
                    X2 = cx + tick,
                    Y2 = y,
                    Stroke = Brushes.Yellow,
                    StrokeThickness = major ? 1.4 : 1.0
                };
                canvas.Children.Add(tickLine);
            }

            return canvas;
        }

        // kleine Helfer:
        static UIElement LineH(double x1, double x2, double y, Brush b, double t) =>
            new Line { X1 = x1, X2 = x2, Y1 = y, Y2 = y, Stroke = b, StrokeThickness = t, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };

        static UIElement LineV(double x, double y1, double y2, Brush b, double t) =>
            new Line { X1 = x, X2 = x, Y1 = y1, Y2 = y2, Stroke = b, StrokeThickness = t, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };

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