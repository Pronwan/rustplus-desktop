using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace RustPlusDesk.Views
{
    public partial class ImageZoomWindow : Window
    {
        private Point _startPoint;
        private Point _origin;
        private bool _isDragging = false;
        private double _accumulatedScale = 1.0;

        public ImageZoomWindow(ImageSource imageSource)
        {
            InitializeComponent();
            ZoomedImage.Source = imageSource;
            
            Loaded += (s, e) => ResetZoom();
        }

        private void ResetZoom()
        {
            ImgScale.ScaleX = 1.0;
            ImgScale.ScaleY = 1.0;
            ImgTranslate.X = 0;
            ImgTranslate.Y = 0;
            _accumulatedScale = 1.0;
        }

        private void ZoomContainer_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double zoomFactor = e.Delta > 0 ? 1.15 : 1 / 1.15;
            double newScale = _accumulatedScale * zoomFactor;

            // Restrict zoom limits
            if (newScale < 0.8 || newScale > 10.0) return;

            Point mousePos = e.GetPosition(ZoomedImage);

            // Compute offset so zoom centers under the cursor location
            double absoluteX = mousePos.X * ImgScale.ScaleX + ImgTranslate.X;
            double absoluteY = mousePos.Y * ImgScale.ScaleY + ImgTranslate.Y;

            ImgScale.ScaleX = newScale;
            ImgScale.ScaleY = newScale;
            _accumulatedScale = newScale;

            ImgTranslate.X = absoluteX - (mousePos.X * ImgScale.ScaleX);
            ImgTranslate.Y = absoluteY - (mousePos.Y * ImgScale.ScaleY);
        }

        private void ZoomContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ResetZoom();
                return;
            }

            _startPoint = e.GetPosition(ZoomContainer);
            _origin = new Point(ImgTranslate.X, ImgTranslate.Y);
            _isDragging = true;
            ZoomContainer.CaptureMouse();
        }

        private void ZoomContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;

            _isDragging = false;
            ZoomContainer.ReleaseMouseCapture();

            // Detect clean click vs drag
            Point endPoint = e.GetPosition(ZoomContainer);
            double deltaX = Math.Abs(endPoint.X - _startPoint.X);
            double deltaY = Math.Abs(endPoint.Y - _startPoint.Y);

            if (deltaX < 4 && deltaY < 4)
            {
                // Simple click triggers closure
                Close();
            }
        }

        private void ZoomContainer_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || !ZoomContainer.IsMouseCaptured) return;

            Point currentPoint = e.GetPosition(ZoomContainer);
            Vector delta = currentPoint - _startPoint;

            ImgTranslate.X = _origin.X + delta.X;
            ImgTranslate.Y = _origin.Y + delta.Y;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
