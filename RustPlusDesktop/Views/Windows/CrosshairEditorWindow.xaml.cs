using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using RustPlusDesk.Services;

namespace RustPlusDesk;

    public partial class CrosshairEditorWindow : Window
    {
        private enum ToolType { Pixel, Pen, Line, Rectangle, Ellipse }
        
        private ToolType _currentTool = ToolType.Pixel;
        private Brush _currentBrush = Brushes.White;
        private double CurrentThickness => ThicknessSlider.Value;
        private double CurrentOpacity => OpacitySlider.Value;

        private Point _startPoint;
        private UIElement? _currentShape;
        private Polyline? _currentPolyline;
        private bool _isErasing = false;
        private System.Collections.Generic.HashSet<Point> _currentPixelStroke = new();
        
        private System.Collections.Generic.Stack<System.Collections.Generic.List<UIElement>> _undoStack = new();
        private System.Collections.Generic.List<UIElement> _currentStrokeElements = new();

        private WriteableBitmap? _bgBitmap;

        public CustomCrosshair? SavedCrosshair { get; private set; }
        private CustomCrosshair? _editingCrosshair;

        public CrosshairEditorWindow(CustomCrosshair? cc = null)
        {
            InitializeComponent();
            _editingCrosshair = cc;
            if (cc != null && !string.IsNullOrEmpty(cc.Base64Image))
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(cc.Base64Image);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = new MemoryStream(bytes);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    
                    _bgBitmap = new WriteableBitmap(new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0));
                    BackgroundImage.Source = _bgBitmap;
                }
                catch { }
            }
        }

        private void Tool_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                if (Enum.TryParse<ToolType>(tag, out var tool))
                {
                    _currentTool = tool;
                }
            }
        }

        private void Color_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                try
                {
                    var converter = new BrushConverter();
                    _currentBrush = (Brush?)converter.ConvertFromString(tag) ?? Brushes.White;
                }
                catch
                {
                    _currentBrush = Brushes.White;
                }
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            DrawCanvas.Children.Clear();
            BackgroundImage.Source = null;
            _bgBitmap = null;
            _undoStack.Clear();
        }

        private void BtnUpload_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PNG Image (*.png)|*.png|All files (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var bitmap = new BitmapImage(new Uri(dlg.FileName));
                    var rtb = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
                    var visual = new DrawingVisual();
                    using (var context = visual.RenderOpen())
                    {
                        context.DrawImage(bitmap, new Rect(0, 0, 64, 64));
                    }
                    rtb.Render(visual);
                    
                    _bgBitmap = new WriteableBitmap(rtb);
                    BackgroundImage.Source = _bgBitmap;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not load image: {ex.Message}");
                }
            }
        }

        private void DrawCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed && e.RightButton != MouseButtonState.Pressed) return;

            _startPoint = e.GetPosition(DrawCanvas);
            DrawCanvas.CaptureMouse();
            _currentStrokeElements = new System.Collections.Generic.List<UIElement>();

            if (_currentTool == ToolType.Pixel)
            {
                _isErasing = e.RightButton == MouseButtonState.Pressed;
                _currentPixelStroke.Clear();
                if (_isErasing)
                    ErasePixelAt(_startPoint);
                else
                    DrawPixelAt(_startPoint);
            }
            else
            {
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    DrawCanvas.ReleaseMouseCapture();
                    return;
                }
                
                switch (_currentTool)
                {
                case ToolType.Pen:
                    _currentPolyline = new Polyline
                    {
                        Stroke = _currentBrush,
                        StrokeThickness = CurrentThickness,
                        Opacity = CurrentOpacity,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        StrokeLineJoin = PenLineJoin.Round
                    };
                    _currentPolyline.Points.Add(_startPoint);
                    _currentShape = _currentPolyline;
                    DrawCanvas.Children.Add(_currentShape);
                    _currentStrokeElements.Add(_currentShape);
                    break;
                case ToolType.Line:
                    _currentShape = new Line
                    {
                        Stroke = _currentBrush,
                        StrokeThickness = CurrentThickness,
                        Opacity = CurrentOpacity,
                        X1 = _startPoint.X,
                        Y1 = _startPoint.Y,
                        X2 = _startPoint.X,
                        Y2 = _startPoint.Y,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round
                    };
                    DrawCanvas.Children.Add(_currentShape);
                    _currentStrokeElements.Add(_currentShape);
                    break;
                case ToolType.Rectangle:
                    _currentShape = new Rectangle
                    {
                        Stroke = _currentBrush,
                        StrokeThickness = CurrentThickness,
                        Opacity = CurrentOpacity,
                        Width = 0,
                        Height = 0
                    };
                    Canvas.SetLeft(_currentShape, _startPoint.X);
                    Canvas.SetTop(_currentShape, _startPoint.Y);
                    DrawCanvas.Children.Add(_currentShape);
                    _currentStrokeElements.Add(_currentShape);
                    break;
                case ToolType.Ellipse:
                    _currentShape = new Ellipse
                    {
                        Stroke = _currentBrush,
                        StrokeThickness = CurrentThickness,
                        Opacity = CurrentOpacity,
                        Width = 0,
                        Height = 0
                    };
                    Canvas.SetLeft(_currentShape, _startPoint.X);
                    Canvas.SetTop(_currentShape, _startPoint.Y);
                    DrawCanvas.Children.Add(_currentShape);
                    _currentStrokeElements.Add(_currentShape);
                    break;
                }
            }
        }

        private void DrawCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!DrawCanvas.IsMouseCaptured) return;
            if (_currentTool != ToolType.Pixel && _currentShape == null) return;

            var pos = e.GetPosition(DrawCanvas);

            switch (_currentTool)
            {
                case ToolType.Pixel:
                    if (_isErasing)
                        ErasePixelAt(pos);
                    else
                        DrawPixelAt(pos);
                    break;
                case ToolType.Pen:
                    if (_currentPolyline != null)
                    {
                        _currentPolyline.Points.Add(pos);
                    }
                    break;
                case ToolType.Line:
                    if (_currentShape is Line line)
                    {
                        line.X2 = pos.X;
                        line.Y2 = pos.Y;
                    }
                    break;
                case ToolType.Rectangle:
                    if (_currentShape is Rectangle rect)
                    {
                        var x = Math.Min(pos.X, _startPoint.X);
                        var y = Math.Min(pos.Y, _startPoint.Y);
                        var w = Math.Abs(pos.X - _startPoint.X);
                        var h = Math.Abs(pos.Y - _startPoint.Y);

                        // Hold shift for square
                        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                        {
                            var side = Math.Max(w, h);
                            w = h = side;
                            x = _startPoint.X < pos.X ? _startPoint.X : _startPoint.X - side;
                            y = _startPoint.Y < pos.Y ? _startPoint.Y : _startPoint.Y - side;
                        }

                        Canvas.SetLeft(rect, x);
                        Canvas.SetTop(rect, y);
                        rect.Width = w;
                        rect.Height = h;
                    }
                    break;
                case ToolType.Ellipse:
                    if (_currentShape is Ellipse ellipse)
                    {
                        var x = Math.Min(pos.X, _startPoint.X);
                        var y = Math.Min(pos.Y, _startPoint.Y);
                        var w = Math.Abs(pos.X - _startPoint.X);
                        var h = Math.Abs(pos.Y - _startPoint.Y);

                        // Hold shift for perfect circle
                        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                        {
                            var side = Math.Max(w, h);
                            w = h = side;
                            x = _startPoint.X < pos.X ? _startPoint.X : _startPoint.X - side;
                            y = _startPoint.Y < pos.Y ? _startPoint.Y : _startPoint.Y - side;
                        }

                        Canvas.SetLeft(ellipse, x);
                        Canvas.SetTop(ellipse, y);
                        ellipse.Width = w;
                        ellipse.Height = h;
                    }
                    break;
            }
        }

        private void DrawCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (DrawCanvas.IsMouseCaptured)
            {
                DrawCanvas.ReleaseMouseCapture();
            }
            if (_currentStrokeElements.Count > 0)
            {
                _undoStack.Push(new System.Collections.Generic.List<UIElement>(_currentStrokeElements));
                // Optionally limit undo stack size
                if (_undoStack.Count > 50)
                {
                    // keep simple, no strict removal needed or just let it grow.
                }
            }
            _currentShape = null;
            _currentPolyline = null;
            _currentPixelStroke.Clear();
            _currentStrokeElements.Clear();
            _isErasing = false;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (_undoStack.Count > 0)
                {
                    var lastStroke = _undoStack.Pop();
                    foreach (var el in lastStroke)
                    {
                        DrawCanvas.Children.Remove(el);
                    }
                }
            }
        }

        private void DrawPixelAt(Point pos)
        {
            double x = Math.Floor(pos.X / 8.0) * 8.0;
            double y = Math.Floor(pos.Y / 8.0) * 8.0;
            var pt = new Point(x, y);

            if (_currentPixelStroke.Add(pt))
            {
                var rect = new Rectangle
                {
                    Fill = _currentBrush,
                    Width = 8,
                    Height = 8,
                    Opacity = CurrentOpacity
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                DrawCanvas.Children.Add(rect);
                _currentStrokeElements.Add(rect);
            }
        }

        private void ErasePixelAt(Point pos)
        {
            double x = Math.Floor(pos.X / 8.0) * 8.0;
            double y = Math.Floor(pos.Y / 8.0) * 8.0;
            var pt = new Point(x, y);

            if (_currentPixelStroke.Add(pt))
            {
                var toRemove = new System.Collections.Generic.List<UIElement>();
                foreach (UIElement child in DrawCanvas.Children)
                {
                    if (child is Rectangle r && Canvas.GetLeft(r) == x && Canvas.GetTop(r) == y)
                    {
                        toRemove.Add(child);
                    }
                }
                foreach (var el in toRemove)
                {
                    DrawCanvas.Children.Remove(el);
                }

                if (_bgBitmap != null)
                {
                    int bx = (int)(x / 8.0);
                    int by = (int)(y / 8.0);
                    if (bx >= 0 && bx < _bgBitmap.PixelWidth && by >= 0 && by < _bgBitmap.PixelHeight)
                    {
                        byte[] emptyPixels = new byte[4];
                        _bgBitmap.WritePixels(new Int32Rect(bx, by, 1, 1), emptyPixels, 4, 0);
                    }
                }
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Render Canvas to 64x64 image
                var targetWidth = 64;
                var targetHeight = 64;
                
                var visual = new DrawingVisual();
                using (var context = visual.RenderOpen())
                {
                    var brush = new VisualBrush(RenderGrid);
                    context.DrawRectangle(brush, null, new Rect(0, 0, targetWidth, targetHeight));
                }

                var rtb = new RenderTargetBitmap(targetWidth, targetHeight, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(visual);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));

                string base64;
                using (var ms = new MemoryStream())
                {
                    encoder.Save(ms);
                    base64 = Convert.ToBase64String(ms.ToArray());
                }

                var list = CustomCrosshairManager.LoadCrosshairs();
                var newIndex = list.Count + 1;
                
                var ch = new CustomCrosshair
                {
                    Id = _editingCrosshair?.Id ?? Guid.NewGuid().ToString(),
                    Name = _editingCrosshair?.Name ?? $"CUSTOM {newIndex}",
                    Base64Image = base64
                };
                
                if (_editingCrosshair != null)
                {
                    var existingIdx = list.FindIndex(c => c.Id == _editingCrosshair.Id);
                    if (existingIdx >= 0)
                        list[existingIdx] = ch;
                    else
                        list.Add(ch);
                }
                else
                {
                    list.Add(ch);
                }
                
                CustomCrosshairManager.SaveCrosshairs(list);

                SavedCrosshair = ch;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving crosshair: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

