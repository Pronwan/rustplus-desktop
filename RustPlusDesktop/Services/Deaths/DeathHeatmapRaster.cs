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
/// </summary>
public static class DeathHeatmapRaster
{
    /// <summary>Grid cell size in world units; the image is scaled up smoothly when shown.</summary>
    private const double CellWorldUnits = 8;
    private const int MaxImageSize = 1024;

    /// <summary>Below this share of the hottest spot a cell is left transparent.</summary>
    private const double VisibleFloor = 0.02;

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
    public static BitmapSource? Render(IReadOnlyList<(double X, double Y)> deaths, double worldSize, double radiusWorldUnits)
    {
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

        var pixels = new byte[size * size * 4];
        for (var i = 0; i < density.Length; i++)
        {
            if (density[i] <= 0)
                continue;
            // Square root keeps single deaths visible next to a hotspot dozens of deaths deep.
            var t = Math.Sqrt(density[i] / max);
            if (t < VisibleFloor)
                continue;
            var (r, g, b) = Colour(t);
            // Faint at the low end: a scatter of single deaths should tint the map, not cover it.
            var alpha = Math.Clamp(0.1 + t * 0.75, 0, 0.85);
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
