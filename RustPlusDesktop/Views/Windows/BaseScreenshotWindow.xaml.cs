using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RustPlusDesk.Services.Auth;

namespace RustPlusDesk.Views.Windows
{
    public partial class BaseScreenshotWindow : Window
    {
        public string? Base64Result { get; private set; }

        public BaseScreenshotWindow()
        {
            InitializeComponent();
            ApplyLocalizedText();
        }

        private static string T(string key, string fallback)
        {
            return RustPlusDesk.Properties.Resources.ResourceManager.GetString(key) ?? fallback;
        }

        private void ApplyLocalizedText()
        {
            Title = T("BaseAddScreenshot", "Add Screenshot");
            TxtTitle.Text = T("BaseAddScreenshot", "Add Screenshot");
            TxtDropHint.Text = T("BaseScreenshotDragDrop", "Drag & Drop an image here");
            TxtPasteHint.Text = T("BaseScreenshotPasteHint", "or press CTRL+V to paste a screenshot");
            BtnSave.Content = T("Save", "Save");
            BtnCancel.Content = T("Cancel", "Cancel");
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            try { DragMove(); } catch { }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(Base64Result))
            {
                DialogResult = true;
                Close();
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                TryPasteFromClipboard();
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    TryLoadFile(files[0]);
                }
            }
            e.Handled = true;
        }

        private void TryPasteFromClipboard()
        {
            try
            {
                if (Clipboard.ContainsImage())
                {
                    BitmapSource source = Clipboard.GetImage();
                    ProcessAndSetImage(source);
                }
                else
                {
                    MessageBox.Show(
                        T("BaseScreenshotNoClipboardImage", "No image found in clipboard."),
                        T("BaseScreenshotPasteErrorTitle", "Paste Error"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(T("BaseScreenshotPasteFailedFormat", "Failed to paste image: {0}"), ex.Message),
                    T("ErrorPrefix", "Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void TryLoadFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;

                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(filePath, UriKind.Absolute);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();

                ProcessAndSetImage(bi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(T("BaseScreenshotLoadFailedFormat", "Failed to load image file: {0}"), ex.Message),
                    T("ErrorPrefix", "Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ProcessAndSetImage(BitmapSource source)
        {
            try
            {
                bool isPremium = SupabaseAuthManager.IsPremium;
                int maxWidth = isPremium ? 1920 : 800;
                int maxHeight = isPremium ? 1080 : 600;

                double scaleX = (double)maxWidth / source.PixelWidth;
                double scaleY = (double)maxHeight / source.PixelHeight;
                double scale = Math.Min(scaleX, scaleY);

                BitmapSource processedSource = source;
                if (scale < 1.0)
                {
                    processedSource = new TransformedBitmap(source, new ScaleTransform(scale, scale));
                }

                // Compress to JPEG with 25% quality
                var encoder = new JpegBitmapEncoder
                {
                    QualityLevel = 25
                };
                encoder.Frames.Add(BitmapFrame.Create(processedSource));

                byte[] compressedBytes;
                using (var ms = new MemoryStream())
                {
                    encoder.Save(ms);
                    compressedBytes = ms.ToArray();
                }

                Base64Result = Convert.ToBase64String(compressedBytes);

                // Show preview of compressed image
                var biPreview = new BitmapImage();
                biPreview.BeginInit();
                biPreview.StreamSource = new MemoryStream(compressedBytes);
                biPreview.EndInit();
                biPreview.Freeze();

                ImgPreview.Source = biPreview;
                ImgPreview.Visibility = Visibility.Visible;
                PlaceholderPanel.Visibility = Visibility.Collapsed;

                double kbSize = Math.Round(compressedBytes.Length / 1024.0, 1);
                TxtImageInfo.Text = string.Format(
                    T("BaseScreenshotImageInfoFormat", "Resolution: {0}x{1} | Size: {2} KB (Quality 25%)"),
                    biPreview.PixelWidth,
                    biPreview.PixelHeight,
                    kbSize);
                BtnSave.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(T("BaseScreenshotCompressFailedFormat", "Error compressing image: {0}"), ex.Message),
                    T("BaseScreenshotCompressionErrorTitle", "Compression Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
