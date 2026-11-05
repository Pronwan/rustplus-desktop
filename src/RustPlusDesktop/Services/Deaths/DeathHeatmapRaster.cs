using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RustPlusDesk.Services.Deaths;

/// <summary>
/// Renders logged deaths as one density image covering the playable world.
///
/// The heatmap used to be one translucent blob per death. Blobs add up, so three or four
/// overlapping deaths already reached full colour, and on a long wipe with hundreds of deaths
/// the whole inhabited part of the map turned into one orange sheet with nothing standing out.
/// Here the deaths are summed into a density grid first and coloured relative to the densest
/// spot, so the hotspots stay hot and a lone death stays a faint mark however many there are.
/// It is also a single image instead of thousands of elements on the map canvas.
///
/// The densest spot alone is not a fair yardstick early in a wipe: with a handful of scattered
/// deaths every lone death is the densest spot, and each one came out full red. So the scale
/// never drops below <see cref="HotDeaths"/> deaths stacked on one spot; a single death sits
/// low on it and only repeated deaths in one place build up to the hot colours.
/// </summary>
public static class DeathHeatmapRaster
{
    /// <summary>Grid cell size in world units; the image is scaled up smoothly when shown.</summary>
    private const double CellWorldUnits = 8;
    private const int MaxImageSize = 1024;

    /// <summary>
    /// How many deaths stacked on one spot it takes to reach full red while no spot on the map is
    /// denser. Counted in deaths: the kernel adds exactly 1 at a death's own position, so this is
    /// independent of the radius. Deaths a little apart overlap only partly and add up to less.
    /// </summary>
    public const double HotDeaths = 8;

    private const double MaxAlpha = 0.85;

    /// <summary>
    /// Below this intensity a cell is left transparent by default. The layer's density slider
    /// raises it to keep only the hotspots; around 0.35 it hides lone deaths.
    /// </summary>
    public const double DefaultMinShare = 0.02;

    /// <summary>
    /// The density the colours are measured against: the densest spot, but never less than
    /// <see cref="HotDeaths"/> stacked deaths.
    /// </summary>
    public static double Scale(double peakDensity) => Math.Max(peakDensity, HotDeaths);

    /// <summary>
    /// Where a cell's density sits on the colour ramp, 0 to 1. Logarithmic because deaths are
    /// counts: the step from one to three deaths in a spot matters as much as from thirty to
    /// ninety, and a long wipe's big hotspot doesn't wash every smaller one out.
    /// </summary>
    public static double Intensity(double density, double scale)
    {
        if (density <= 0 || scale <= 0)
            return 0;
        return Math.Clamp(Math.Log(1 + density) / Math.Log(1 + scale), 0, 1);
    }

    /// <summary>
    /// Opacity for an intensity. Steeper than linear, with no floor, so a lone death is a faint
    /// smudge whose edges fade out entirely, and the visible size of a spot grows with its count.
    /// </summary>
    public static double Opacity(double intensity) => MaxAlpha * Math.Pow(Math.Clamp(intensity, 0, 1), 1.5);

    private static readonly (double At, byte R, byte G, byte B)[] Ramp =
    {
        (0.00, 40, 110, 255),
        (0.35, 0, 210, 200),
        (0.60, 255, 225, 0),
        (0.80, 255, 130, 0),
        (1.00, 255, 35, 0),
    };

    /// <summary>
    /// The density image for world [0, worldSize] on both axes, north up; null when no death
    /// falls inside the world.
    /// </summary>
    public static BitmapSource? Render(IReadOnlyList<(double X, double Y)> deaths, double worldSize, double radiusWorldUnits,
        double minShare = DefaultMinShare)
    {
        var visibleFloor = Math.Clamp(minShare, 0, 0.95);
        if (deaths.Count == 0 || worldSize <= 0)
            return null;

        var size = Math.Clamp((int)Math.Ceiling(worldSize / CellWorldUnits), 64, MaxImageSize);
        var cell = worldSize / size;
        var radius = Math.Max(1.5, radiusWorldUnits / cell);
        var reach = (int)Math.Ceiling(radius);
        var density = new double[size * size];

        foreach (var (x, y) in deaths)
        {
            // Deep Sea deaths sit far outside the world square and have no place on this map.
            if (x < 0 || y < 0 || x > worldSize || y > worldSize)
                continue;
            var cx = x / cell;
            var cy = (worldSize - y) / cell;
            var left = Math.Max(0, (int)(cx - reach));
            var right = Math.Min(size - 1, (int)(cx + reach));
            var top = Math.Max(0, (int)(cy - reach));
            var bottom = Math.Min(size - 1, (int)(cy + reach));
            for (var row = top; row <= bottom; row++)
            {
                var dy = (row + 0.5 - cy) / radius;
                for (var column = left; column <= right; column++)
                {
                    var dx = (column + 0.5 - cx) / radius;
                    var d2 = dx * dx + dy * dy;
                    if (d2 >= 1)
                        continue;
                    // Quartic kernel: smooth falloff to exactly zero at the radius.
                    var w = 1 - d2;
                    density[row * size + column] += w * w;
                }
            }
        }

        var max = 0d;
        foreach (var value in density)
            max = Math.Max(max, value);
        if (max <= 0)
            return null;
        var scale = Scale(max);

        var pixels = new byte[size * size * 4];
        for (var i = 0; i < density.Length; i++)
        {
            if (density[i] <= 0)
                continue;
            var t = Intensity(density[i], scale);
            if (t < visibleFloor)
                continue;
            var (r, g, b) = Colour(t);
            var alpha = Opacity(t);
            // Pbgra32 wants premultiplied colour.
            pixels[i * 4] = (byte)(b * alpha);
            pixels[i * 4 + 1] = (byte)(g * alpha);
            pixels[i * 4 + 2] = (byte)(r * alpha);
            pixels[i * 4 + 3] = (byte)(255 * alpha);
        }

        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static (double R, double G, double B) Colour(double t)
    {
        for (var i = 1; i < Ramp.Length; i++)
        {
            if (t > Ramp[i].At)
                continue;
            var (fromAt, fromR, fromG, fromB) = Ramp[i - 1];
            var (toAt, toR, toG, toB) = Ramp[i];
            var f = (t - fromAt) / (toAt - fromAt);
            return (fromR + (toR - fromR) * f, fromG + (toG - fromG) * f, fromB + (toB - fromB) * f);
        }
        var last = Ramp[^1];
        return (last.R, last.G, last.B);
    }
}
