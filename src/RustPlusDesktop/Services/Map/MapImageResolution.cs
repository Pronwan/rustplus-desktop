using System;

namespace RustPlusDesk.Services.Map;

/// <summary>How large the 2D map image is decoded, traded against RAM.</summary>
public enum MapImageResolution
{
    /// <summary>At most <see cref="MapImagePolicy.EconomyWidth"/> px, server image and HD map alike.</summary>
    Economy = 0,

    /// <summary>The server image as it comes; the HD map at the server image's size.</summary>
    Standard = 1,

    /// <summary>Both at their full size.</summary>
    High = 2,
}

/// <summary>
/// The sizes behind <see cref="MapImageResolution"/>, and what they cost.
///
/// Markers, drawings, labels and grid are drawn over the image rather than into it, so a smaller
/// image only softens the terrain underneath; everything placed on it stays sharp.
/// </summary>
internal static class MapImagePolicy
{
    public const int EconomyWidth = 2048;

    /// <summary>Standard for an HD map with no server image to match, such as an offline map.</summary>
    public const int StandardFallbackWidth = 3072;

    /// <summary>The width to decode the server's image at; 0 keeps it as it is.</summary>
    public static int ServerDecodeWidth(MapImageResolution resolution, int nativeWidth)
        => resolution == MapImageResolution.Economy && nativeWidth > EconomyWidth ? EconomyWidth : 0;

    /// <summary>The width to decode MapParser's render at; 0 decodes it whole.</summary>
    public static int RenderDecodeWidth(MapImageResolution resolution, int renderWidth, int serverWidth)
    {
        int target = resolution switch
        {
            MapImageResolution.Economy => EconomyWidth,
            MapImageResolution.Standard => serverWidth > 0 ? serverWidth : StandardFallbackWidth,
            _ => 0,
        };
        return target > 0 && target < renderWidth ? target : 0;
    }

    /// <summary>
    /// RAM an image of this size takes on the map: 4 bytes a pixel decoded, and as much again for
    /// the copy WPF keeps to draw it. Matches what was measured for a 4750 px render.
    /// </summary>
    public static double EstimateMb(int width, int height)
        => width <= 0 || height <= 0 ? 0 : width * (double)height * 8 / (1024 * 1024);

    /// <summary>The size an image of <paramref name="width"/>×<paramref name="height"/> decodes to at <paramref name="decodeWidth"/>.</summary>
    public static (int Width, int Height) Scaled(int width, int height, int decodeWidth)
        => decodeWidth <= 0 || width <= 0 || decodeWidth >= width
            ? (width, height)
            : (decodeWidth, (int)Math.Round(height * (double)decodeWidth / width));

    /// <summary>Rounded for showing to people: to 10 MB, and never claiming less than 10.</summary>
    public static int RoundForDisplay(double mb) => Math.Max(10, (int)Math.Round(mb / 10.0) * 10);
}
