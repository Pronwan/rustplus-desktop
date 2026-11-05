using System.IO;
using System.Windows.Media.Imaging;

namespace RustPlusDesk.Services.Map;

/// <summary>
/// Decodes map images straight from their file into a frozen bitmap.
///
/// Reading the file into a byte array first and decoding from a MemoryStream kept a second copy
/// of every file (30 MB for a render) on the large-object heap. Decoding from the file with
/// OnLoad caching reads it once and lets it go, so the file is not left locked either.
/// </summary>
internal static class MapImageLoader
{
    /// <param name="decodePixelWidth">
    /// Decodes at this width instead of the file's, keeping the aspect ratio; 0 for full size.
    /// A 4750 px render takes 90 MB decoded, 2048 px takes 16 MB.
    /// </param>
    public static BitmapImage Load(string path, int decodePixelWidth = 0)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (decodePixelWidth > 0) image.DecodePixelWidth = decodePixelWidth;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>An image file's pixel size, read from its header without decoding it. (0, 0) when unreadable.</summary>
    public static (int Width, int Height) ReadSize(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch
        {
            return (0, 0);
        }
    }
}
