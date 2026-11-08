using System;
using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace RustPlusDesk.Services.Map;

/// <summary>
/// One decoded, size-capped bitmap per icon file, shared by every map element that draws it.
///
/// A parsed map places hundreds of extra monuments, and each used to decode its own copy of a
/// 700 px PNG at full size: 1.9 MB of native memory apiece, 1.2 GB for one map, none of it
/// visible on the managed heap. Icons are drawn at 64 px or less, so a shared copy at
/// <see cref="MaxDecodePx"/> keeps them sharp and costs at most 256 KB per distinct file.
/// </summary>
internal static class MapIconCache
{
    /// <summary>Longest side an icon is decoded at; four times the largest size it is drawn at.</summary>
    internal const int MaxDecodePx = 256;

    private const string PackPrefix = "pack://application:,,,/";

    private static readonly ConcurrentDictionary<string, BitmapImage> Icons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The shared bitmap for <paramref name="uri"/>, frozen. Throws if the file cannot be decoded.</summary>
    public static BitmapImage Get(string uri)
    {
        if (Icons.TryGetValue(uri, out var cached)) return cached;

        // Two threads asking at once decode twice, and one result is dropped; nothing is held.
        return Icons.GetOrAdd(uri, Load(uri));
    }

    /// <summary>
    /// The sizes to give the decoder for an image of this size: (width, 0) or (0, height) to
    /// shrink it by its longer side, (0, 0) to leave it alone. Asking for more than the file
    /// holds would scale it up, so small icons are never given a size.
    /// </summary>
    internal static (int Width, int Height) DecodeSize(int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= MaxDecodePx && pixelHeight <= MaxDecodePx) return (0, 0);
        return pixelWidth >= pixelHeight ? (MaxDecodePx, 0) : (0, MaxDecodePx);
    }

    private static BitmapImage Load(string uri)
    {
        try
        {
            return Decode(new Uri(uri, UriKind.Absolute));
        }
        catch when (uri.StartsWith(PackPrefix + "Assets/", StringComparison.OrdinalIgnoreCase))
        {
            // Not every asset is embedded; some ship as files beside the executable.
            string relative = uri[PackPrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            return Decode(new Uri(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relative), UriKind.Absolute));
        }
    }

    private static BitmapImage Decode(Uri uri)
    {
        var (decodeWidth, decodeHeight) = ReadDecodeSize(uri);

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = uri;
        // WPF's own image cache is keyed by URI alone, so it would ignore the decode size.
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = decodeWidth;
        image.DecodePixelHeight = decodeHeight;
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>Reads the header through a stream of our own: a decoder given the URI keeps the file open.</summary>
    private static (int Width, int Height) ReadDecodeSize(Uri uri)
    {
        using Stream stream = uri.IsFile
            ? File.OpenRead(uri.LocalPath)
            : Application.GetResourceStream(uri)?.Stream ?? throw new IOException($"Cannot locate resource: {uri}");
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        return DecodeSize(frame.PixelWidth, frame.PixelHeight);
    }
}
