using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace RustPlusDesk.Views
{
    public class GalleryItem
    {
        public string ImagePath { get; set; } = "";
        public string Description { get; set; } = "";
        internal ImageGallery ParentGallery { get; set; }
    }

    [ContentProperty("Items")]
    public partial class ImageGallery : UserControl
    {
        private int _currentIndex = 0;

        public List<GalleryItem> Items { get; } = new List<GalleryItem>();

        public ImageGallery()
        {
            InitializeComponent();
            Loaded += ImageGallery_Loaded;
        }

        private void ImageGallery_Loaded(object sender, RoutedEventArgs e)
        {
            if (Items == null || Items.Count == 0)
            {
                Visibility = Visibility.Collapsed;
                return;
            }

            foreach (var item in Items)
            {
                if (item != null)
                {
                    item.ParentGallery = this;
                }
            }

            _currentIndex = 0;
            UpdateGallery();
        }

        public void UpdateGallery()
        {
            if (Items.Count == 0) return;

            var activeItem = Items[_currentIndex];

            // Update Image source
            try
            {
                if (!string.IsNullOrEmpty(activeItem.ImagePath))
                {
                    ImgPreview.Source = new BitmapImage(new Uri(activeItem.ImagePath, UriKind.RelativeOrAbsolute));
                }
            }
            catch
            {
                // Fallback / ignore invalid image paths gracefully
            }

            // Update Description
            TxtDescription.Text = activeItem.Description;
            TxtDescription.Visibility = string.IsNullOrEmpty(activeItem.Description) ? Visibility.Collapsed : Visibility.Visible;

            // Show/Hide Next/Prev Buttons if only 1 image
            if (Items.Count <= 1)
            {
                NavigationOverlay.Visibility = Visibility.Collapsed;
                IndicatorPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                NavigationOverlay.Visibility = Visibility.Visible;
                IndicatorPanel.Visibility = Visibility.Visible;
                BuildIndicators();
            }
        }

        private void BuildIndicators()
        {
            IndicatorPanel.Children.Clear();
            for (int i = 0; i < Items.Count; i++)
            {
                int index = i;
                var dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Margin = new Thickness(4, 0, 4, 0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Fill = (Brush)FindResource(i == _currentIndex ? "Accent" : "TextSubtle"),
                    Opacity = i == _currentIndex ? 1.0 : 0.4
                };
                
                dot.MouseDown += (s, e) =>
                {
                    _currentIndex = index;
                    UpdateGallery();
                };

                IndicatorPanel.Children.Add(dot);
            }
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (Items.Count == 0) return;
            _currentIndex = (_currentIndex - 1 + Items.Count) % Items.Count;
            UpdateGallery();
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (Items.Count == 0) return;
            _currentIndex = (_currentIndex + 1) % Items.Count;
            UpdateGallery();
        }

        private void ImgPreview_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ImgPreview.Source == null) return;
            try
            {
                var parentWindow = Window.GetWindow(this);
                var zoomWin = new ImageZoomWindow(ImgPreview.Source)
                {
                    Owner = parentWindow
                };
                zoomWin.ShowDialog();
            }
            catch
            {
                try
                {
                    var zoomWin = new ImageZoomWindow(ImgPreview.Source);
                    zoomWin.Show();
                }
                catch { }
            }
        }
    }
}
