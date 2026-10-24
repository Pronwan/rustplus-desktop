using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace RustPlusDesk.Services;

/// <summary>
/// Keeps the on-disk icon cache small and uniform: every cached icon is re-encoded to an exact
/// 40x40 PNG (the only size the app renders). This both saves space — the source art is often
/// 256/512px — and lets the sidebar hover decoration (which requires 40x40 files) find icons to use.
///
/// Lives in its own file because ImageSharp's Image/Color/Size types collide with the System.Windows
/// types used across MainWindow.
/// </summary>
public static class IconNormalizer
{
    public const int Size = 40;

    /// <summary>
    /// Re-encodes icon bytes to an exact 40x40, aspect-preserving, transparently padded PNG.
    /// Returns the original bytes unchanged if decoding/encoding fails.
    /// </summary>
    public static byte[] To40(byte[] input)
    {
        try
        {
            using var image = Image.Load(input);
            if (image.Width == Size && image.Height == Size) return input; // already correct

            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(Size, Size),
                Mode = ResizeMode.Pad,
                PadColor = SixLabors.ImageSharp.Color.Transparent,
            }));

            using var ms = new MemoryStream();
            image.Save(ms, new PngEncoder());
            return ms.ToArray();
        }
        catch
        {
            return input;
        }
    }

    /// <summary>
    /// Deletes cached PNGs that are not 40x40 (old 256/512px downloads). Uses a header-only size
    /// read so it does not decode the images. Returns the number of files removed. Idempotent.
    /// </summary>
    public static int CleanupNon40(string directory)
    {
        int removed = 0;
        try
        {
            if (!Directory.Exists(directory)) return 0;
            foreach (var path in Directory.EnumerateFiles(directory, "*.png"))
            {
                try
                {
                    if (!IsExactly40(path))
                    {
                        File.Delete(path);
                        removed++;
                    }
                }
                catch { /* skip locked/partial files */ }
            }
        }
        catch { /* best effort */ }
        return removed;
    }

    private static bool IsExactly40(string path)
    {
        Span<byte> header = stackalloc byte[24];
        using var stream = File.OpenRead(path);
        if (stream.Read(header) < header.Length) return false;
        if (header[1] != 'P' || header[2] != 'N' || header[3] != 'G') return false;

        uint width = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
        uint height = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
        return width == Size && height == Size;
    }
}
