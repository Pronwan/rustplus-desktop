using System;

namespace RustPlusDesk.Services;

/// <summary>
/// Colours a parsed resource heatmap (512x512 bytes from map_data.json) for drawing over
/// the map. Each heatmap is scaled to its own range, since some only hold small values
/// (wood sits near 20 of 255 across every forest), and weak values fade to transparent so
/// the blurred edges do not wash the whole map in colour. The ramp runs yellow, orange,
/// red: no green, which would vanish against grass.
/// </summary>
public static class ResourceHeatmapRaster
{
    public const int Size = 512;
    private const int BlurRadius = 3; // 7x7 box blur
    private const float FadeStart = 0.12f;
    private const float FadeEnd = 0.55f;
    private const byte MaxAlpha = 210;

    /// <summary>Returns premultiplied BGRA pixels (Pbgra32), or null when the data is not 512x512.</summary>
    public static int[]? Colorize(byte[] raw)
    {
        if (raw == null || raw.Length != Size * Size) return null;

        float reference = ReferenceLevel(raw);
        var pixels = new int[Size * Size];
        if (reference <= 0) return pixels;

        float[] blurred = BoxBlur(raw);
        for (int i = 0; i < pixels.Length; i++)
        {
            float value = Math.Max(blurred[i], raw[i]);
            float t = Math.Min(1f, value / reference);
            byte a = (byte)(MaxAlpha * SmoothStep(FadeStart, FadeEnd, t));
            if (a < 8) continue;

            var (r, g, b) = Ramp(t);
            pixels[i] = (a << 24) | (r * a / 255 << 16) | (g * a / 255 << 8) | (b * a / 255);
        }

        return pixels;
    }

    /// <summary>
    /// The value treated as full heat: the 95th percentile of the cells that hold anything,
    /// so a few extreme cells do not flatten the rest. Never below 16, to keep noise faint.
    /// </summary>
    internal static float ReferenceLevel(byte[] raw)
    {
        var counts = new int[256];
        int total = 0;
        foreach (byte v in raw)
        {
            if (v <= 2) continue;
            counts[v]++;
            total++;
        }
        if (total == 0) return 0;

        int target = (int)Math.Ceiling(total * 0.95);
        int seen = 0;
        for (int v = 3; v < 256; v++)
        {
            seen += counts[v];
            if (seen >= target) return Math.Max(16, v);
        }
        return 255;
    }

    internal static (int R, int G, int B) Ramp(float t)
    {
        // yellow (255,215,0) -> orange (255,120,0) -> red (215,25,25)
        if (t < 0.5f)
        {
            float k = t / 0.5f;
            return (255, Lerp(215, 120, k), 0);
        }
        else
        {
            float k = (t - 0.5f) / 0.5f;
            return (Lerp(255, 215, k), Lerp(120, 25, k), Lerp(0, 25, k));
        }
    }

    private static int Lerp(int from, int to, float k) => (int)Math.Round(from + (to - from) * k);

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float k = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return k * k * (3 - 2 * k);
    }

    private static float[] BoxBlur(byte[] raw)
    {
        var blurred = new float[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float sum = 0;
                int count = 0;
                for (int dy = -BlurRadius; dy <= BlurRadius; dy++)
                {
                    int ny = y + dy;
                    if (ny < 0 || ny >= Size) continue;
                    for (int dx = -BlurRadius; dx <= BlurRadius; dx++)
                    {
                        int nx = x + dx;
                        if (nx < 0 || nx >= Size) continue;
                        sum += raw[ny * Size + nx];
                        count++;
                    }
                }
                blurred[y * Size + x] = sum / count;
            }
        }
        return blurred;
    }
}
