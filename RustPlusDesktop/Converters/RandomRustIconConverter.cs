using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RustPlusDesk.Converters;

/// <summary>
/// A random Rust item icon, for decoration.
///
/// This used to do its whole job inside the binding, on the UI thread, on every single hover:
/// enumerate the icon cache, then open and fully decode PNG after PNG until one turned out to be
/// 40x40 with a usable background — worst case every file in the folder. That cache reaches two
/// thousand files and a hundred and sixty megabytes in normal use, and each open goes past the
/// virus scanner. Users reported the sidebar taking five to ten seconds to react to a hover, with
/// the delay varying per hover because the search started at a random offset each time.
///
/// It is a gimmick. So it now warms a small, fixed set once in the background and picks from that
/// afterwards, and the binding never touches the disk.
/// </summary>
public sealed class RandomRustIconConverter : IValueConverter
{
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RustPlusDesk",
        "icons");

    /// <summary>
    /// How many icons to keep. Enough that the same one rarely comes up twice in a row, small
    /// enough that warming it is quick and it costs about a megabyte of decoded pixels.
    /// </summary>
    private const int PoolSize = 200;

    /// <summary>
    /// Ceiling on files examined while warming, so a pathological cache — thousands of icons in
    /// the wrong size — cannot turn startup into the problem this replaced.
    /// </summary>
    private const int MaxFilesExamined = 600;

    private const int RequiredIconSize = 40;

    /// <summary>Frozen and therefore safe to hand to the UI thread from the warming task.</summary>
    private static IReadOnlyList<BitmapImage> _pool = Array.Empty<BitmapImage>();

    private static int _warmingStarted;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // First use kicks off the warm-up and draws nothing. The popover appears without its
        // decoration once, which is a far better trade than blocking it for seconds.
        if (Interlocked.CompareExchange(ref _warmingStarted, 1, 0) == 0)
        {
            _ = Task.Run(WarmPool);
        }

        var pool = _pool;
        return pool.Count == 0
            ? DependencyProperty.UnsetValue
            : pool[Random.Shared.Next(pool.Count)];
    }

    private static void WarmPool()
    {
        try
        {
            if (!Directory.Exists(CacheDirectory)) return;

            // Shuffled so the session's set is not always the same first two hundred files on
            // disk — the whole point of the thing is that it varies.
            var paths = Directory.GetFiles(CacheDirectory, "*.png")
                .OrderBy(_ => Random.Shared.Next())
                .Take(MaxFilesExamined)
                .ToArray();

            var pool = new List<BitmapImage>(PoolSize);
            foreach (var path in paths)
            {
                if (pool.Count >= PoolSize) break;

                var icon = TryLoad(path);
                if (icon is not null) pool.Add(icon);
            }

            _pool = pool;
        }
        catch
        {
            // A decoration that cannot be built is not worth reporting. The popover simply shows
            // no icon, and nothing else in the app depends on this.
        }
    }

    private static BitmapImage? TryLoad(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = stream;
            bitmap.EndInit();

            if (bitmap.PixelWidth != RequiredIconSize || bitmap.PixelHeight != RequiredIconSize) return null;
            if (HasOpaqueBlackBackground(bitmap)) return null;

            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Rejects icons that ship with a solid black backdrop, which reads as a hole on the panel.
    /// Judged by three of the four corners, since a legitimate icon rarely fills them all.
    /// </summary>
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
            pixels.Length - 4,
        };

        return offsets.Count(offset =>
            pixels[offset + 3] > 240 &&
            pixels[offset] < 25 &&
            pixels[offset + 1] < 25 &&
            pixels[offset + 2] < 25) >= 3;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
