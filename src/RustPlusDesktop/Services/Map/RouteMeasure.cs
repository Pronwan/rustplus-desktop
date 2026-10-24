using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;

namespace RustPlusDesk.Services.Map;

/// <summary>
/// How long a drawn route is, and how long it takes to run.
///
/// The distance is not estimated from grid squares. The client already knows the world size in
/// metres and the pixel rectangle that world occupies, and overlay strokes live in that same
/// pixel space — so a route measures exactly, on any map size, without anybody having to agree on
/// what a grid is worth.
/// </summary>
public static class RouteMeasure
{
    /// <summary>
    /// Sprint speed on flat ground, in metres per second.
    ///
    /// Straight out of BasePlayer.GetSpeed(), which lerps 2.8 to 5.5 by how hard you are running
    /// and then down to 1.7 for ducking. 5.5 m/s is the top of that range, which is what somebody
    /// planning a route wants: 150 m of grid in 27 seconds, matching what players measure.
    ///
    /// It is a floor rather than an estimate. Hills, water and an empty stamina bar all cost time,
    /// and none of them are in the formula.
    /// </summary>
    public const double SprintMetresPerSecond = 5.5;

    private const double YardsPerMetre = 1.0936133;

    private const double MilesPerKilometre = 0.6213712;

    /// <summary>Total length of a polyline, in whatever unit its points are in.</summary>
    public static double PathLength(IReadOnlyList<Point> points)
    {
        double total = 0;

        for (int i = 1; i < points.Count; i++)
        {
            double dx = points[i].X - points[i - 1].X;
            double dy = points[i].Y - points[i - 1].Y;
            total += Math.Sqrt((dx * dx) + (dy * dy));
        }

        return total;
    }

    /// <summary>
    /// Metres, from a length measured in map pixels.
    ///
    /// <paramref name="worldSpan"/> is how many metres the map's pixel rectangle covers — the
    /// world size on a normal map, the box size on the deep sea one. Both are already known, and
    /// passing whichever applies is the only difference between the two maps.
    /// </summary>
    public static double PixelsToMetres(double pixels, double worldSpan, double rectWidthPx)
        => rectWidthPx <= 0 || worldSpan <= 0 ? 0 : pixels * (worldSpan / rectWidthPx);

    public static TimeSpan SprintTime(double metres)
        => TimeSpan.FromSeconds(metres / SprintMetresPerSecond);

    /// <summary>
    /// Distance for a legend row: metric first, imperial in brackets.
    ///
    /// Under a kilometre it stays in whole metres — nobody plans a route to the centimetre — and
    /// above it switches to two decimals, where the extra digit is the difference between two
    /// routes rather than noise.
    /// </summary>
    public static string FormatDistance(double metres, CultureInfo? culture = null)
    {
        culture = culture ?? CultureInfo.CurrentCulture;

        if (metres < 1000)
        {
            double yards = metres * YardsPerMetre;
            return string.Format(culture, "{0:0} m ({1:0} yd)", metres, yards);
        }

        double km = metres / 1000.0;
        return string.Format(culture, "{0:0.00} km ({1:0.00} mi)", km, km * MilesPerKilometre);
    }

    /// <summary>
    /// Duration for a legend row: seconds under a minute, minutes and seconds above it.
    ///
    /// No hours. A route that takes an hour to run is not a route anybody is reading off a legend.
    /// </summary>
    public static string FormatDuration(TimeSpan span, CultureInfo? culture = null)
    {
        culture = culture ?? CultureInfo.CurrentCulture;

        int seconds = (int)Math.Round(span.TotalSeconds);

        return seconds < 60
            ? string.Format(culture, "{0} s", seconds)
            : string.Format(culture, "{0}:{1:00}", seconds / 60, seconds % 60);
    }
}
