using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using RustPlusDesk.Helpers;

namespace RustPlusDesk.Views
{
    /// <summary>
    /// Draw a box around the killer's name, once, on a picture of your own death screen.
    ///
    /// The measurements built into the defaults cover 16:9, 16:10 and 4:3 at both interface
    /// scales, and they will still be wrong for somebody — an ultrawide, a scale nobody tried,
    /// a future patch that moves the box. This is the way out that does not need a new build.
    ///
    /// It works on a still picture rather than on the live screen, and that is the point: the
    /// death screen is up for a few seconds and drawing a careful rectangle takes longer than
    /// that. The picture is taken while it is on screen and dragged on afterwards, with the
    /// game already respawned behind it.
    /// </summary>
    public sealed class DeathRegionPicker : Window
    {
        private readonly Image _shot = new() { Stretch = Stretch.Uniform };
        private readonly Canvas _canvas = new();
        private readonly Rectangle _box = new()
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x3F, 0xD7, 0xFF)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(0x22, 0x3F, 0xD7, 0xFF)),
            Visibility = Visibility.Collapsed,
        };

        private Point _from;
        private bool _dragging;

        /// <summary>The chosen band, as fractions of the screen's width. Null if cancelled.</summary>
        public (double Left, double Top, double Width, double Height)? Region { get; private set; }

        public DeathRegionPicker(string imagePath)
        {
            Title = Loc.Text("DeathRegionPickerTitle", "Select the killer's name");
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowState = WindowState.Maximized;
            Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0F, 0x16));

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            var hint = new TextBlock
            {
                Text = Loc.Text("DeathRegionPickerHint",
                    "Drag a box around the killer's name — and the weapon beside it, if you want that too."),
                Margin = new Thickness(16, 12, 16, 8),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.White,
            };
            Grid.SetRow(hint, 0);
            root.Children.Add(hint);

            // The picture and the box share one grid cell, and the box is drawn on a canvas the
            // same size as the picture — so a rectangle in canvas coordinates is a rectangle on
            // the screenshot, whatever the window has been resized to.
            var stage = new Grid { Margin = new Thickness(16, 0, 16, 0) };
            Grid.SetRow(stage, 1);
            root.Children.Add(stage);

            stage.Children.Add(_shot);

            _canvas.Background = Brushes.Transparent;
            _canvas.Children.Add(_box);
            stage.Children.Add(_canvas);

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                image.UriSource = new Uri(imagePath);
                image.EndInit();

                _shot.Source = image;
            }
            catch
            {
                hint.Text = Loc.Text("DeathRegionPickerNoShot", "The screenshot could not be read.");
            }

            _canvas.MouseLeftButtonDown += OnDown;
            _canvas.MouseMove += OnMove;
            _canvas.MouseLeftButtonUp += OnUp;

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(16, 10, 16, 14),
            };
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            var use = new Wpf.Ui.Controls.Button
            {
                Content = Loc.Text("DeathRegionPickerUse", "Use this box"),
                Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
                Margin = new Thickness(0, 0, 8, 0),
                IsEnabled = false,
            };

            var cancel = new Wpf.Ui.Controls.Button
            {
                Content = Loc.Text("Cancel", "Cancel"),
            };

            use.Click += (_, _) => { DialogResult = true; Close(); };
            cancel.Click += (_, _) => { Region = null; DialogResult = false; Close(); };

            buttons.Children.Add(use);
            buttons.Children.Add(cancel);

            _enableUse = () => use.IsEnabled = Region != null;

            // Escape leaves it alone, which is what somebody who opened this by mistake wants.
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;

                Region = null;
                DialogResult = false;
                Close();
            };
        }

        private readonly Action _enableUse;

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            _from = e.GetPosition(_canvas);
            _dragging = true;

            _box.Visibility = Visibility.Visible;
            Place(_from, _from);

            _canvas.CaptureMouse();
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (_dragging) Place(_from, e.GetPosition(_canvas));
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;

            _dragging = false;
            _canvas.ReleaseMouseCapture();

            Place(_from, e.GetPosition(_canvas));
            Region = ToFractions();

            _enableUse();
        }

        private void Place(Point a, Point b)
        {
            double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);

            Canvas.SetLeft(_box, x);
            Canvas.SetTop(_box, y);

            _box.Width = Math.Abs(a.X - b.X);
            _box.Height = Math.Abs(a.Y - b.Y);
        }

        /// <summary>
        /// The drawn box as fractions of the screen's width.
        ///
        /// Through the picture rather than through the window: the screenshot is shown scaled
        /// to fit and letterboxed, so canvas pixels and screen pixels differ by whatever that
        /// scale came out to. Everything divides by the picture's width, including the vertical
        /// numbers — the same unit the region is stored and captured in.
        /// </summary>
        private (double, double, double, double)? ToFractions()
        {
            if (_shot.Source is not BitmapSource source) return null;
            if (_box.Width < 4 || _box.Height < 4) return null;

            // Where the picture actually sits inside the canvas once Uniform has had its way.
            double scale = Math.Min(_canvas.ActualWidth / source.PixelWidth,
                                    _canvas.ActualHeight / source.PixelHeight);

            if (scale <= 0) return null;

            double offsetX = (_canvas.ActualWidth - source.PixelWidth * scale) / 2;
            double offsetY = (_canvas.ActualHeight - source.PixelHeight * scale) / 2;

            double left = (Canvas.GetLeft(_box) - offsetX) / scale;
            double top = (Canvas.GetTop(_box) - offsetY) / scale;

            double width = _box.Width / scale;
            double height = _box.Height / scale;

            double w = source.PixelWidth;

            return (Math.Clamp(left / w, 0, 1), Math.Clamp(top / w, 0, 1),
                    Math.Clamp(width / w, 0.005, 1), Math.Clamp(height / w, 0.005, 1));
        }
    }
}
