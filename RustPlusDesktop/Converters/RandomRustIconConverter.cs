using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RustPlusDesk.Converters;

public sealed class RandomRustIconConverter : IValueConverter
{
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RustPlusDesk",
        "icons");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (!Directory.Exists(CacheDirectory)) return DependencyProperty.UnsetValue;

        var paths = Directory.GetFiles(CacheDirectory, "*.png");
        if (paths.Length == 0) return DependencyProperty.UnsetValue;

        var start = Random.Shared.Next(paths.Length);
        for (var i = 0; i < paths.Length; i++)
        {
            try
            {
                using var stream = File.OpenRead(paths[(start + i) % paths.Length]);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                if (bitmap.PixelWidth != 40 || bitmap.PixelHeight != 40) continue;
                if (HasOpaqueBlackBackground(bitmap)) continue;

                bitmap.Freeze();
                return bitmap;
            }
            catch { }
        }

        return DependencyProperty.UnsetValue;
    }

    private static bool HasOpaqueBlackBackground(BitmapSource bitmap)
    {
        var bgra = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);

        var offsets = new[]
        {
            0,
            (bgra.PixelWidth - 1) * 4,
            (bgra.PixelHeight - 1) * stride,
            pixels.Length - 4
        };

        return offsets.Count(offset =>
            pixels[offset + 3] > 240 &&
            pixels[offset] < 25 &&
            pixels[offset + 1] < 25 &&
            pixels[offset + 2] < 25) >= 3;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
