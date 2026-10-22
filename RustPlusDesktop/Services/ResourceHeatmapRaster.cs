using System;

namespace RustPlusDesk.Services;

/// <summary>
/// Colours a parsed resource heatmap (square bytes from map_data.json: the 512 grid, or the
/// parser's full-resolution 2048 layer) for drawing over the map. Each heatmap is scaled to
/// its own range, since some only hold small values (wood sits near 20 of 255 across every
/// forest), and values under half of that fade to transparent, so only the places where a
/// spawn is dense are coloured instead of every cell it can appear in. The ramp runs
/// yellow, orange, red: no green, which would vanish against grass.
/// </summary>
public static class ResourceHeatmapRaster
{
    public const int Size = 512;
    // Values below half the reference stay clear; 0.12 tinted most of the map.
    private const float FadeStart = 0.5f;
    private const float FadeEnd = 0.9f;
    private const byte MaxAlpha = 210;

    /// <summary>Returns premultiplied BGRA pixels (Pbgra32), or null when the data is not size x size.</summary>
    public static int[]? Colorize(byte[] raw, int size = Size)
    {
        if (raw == null || size <= 0 || raw.Length != size * size) return null;

        float reference = ReferenceLevel(raw);
        var pixels = new int[size * size];
        if (reference <= 0) return pixels;

        // Soften cell edges over a few metres: 7x7 cells on the 512 grid (~8 m cells), 3x3 on
        // the full-resolution layer (~2 m cells).
        float[] blurred = BoxBlur(raw, size, size >= 1024 ? 1 : 3);
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

    // Separable box blur: horizontal averages, then vertical averages of those.
    private static float[] BoxBlur(byte[] raw, int size, int radius)
    {
        var horizontal = new float[size * size];
        for (int y = 0; y < size; y++)
        {
            int row = y * size;
            for (int x = 0; x < size; x++)
            {
                float sum = 0;
                int count = 0;
                for (int nx = Math.Max(0, x - radius); nx <= Math.Min(size - 1, x + radius); nx++)
                {
                    sum += raw[row + nx];
                    count++;
                }
                horizontal[row + x] = sum / count;
            }
        }

        var blurred = new float[size * size];
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float sum = 0;
                int count = 0;
                for (int ny = Math.Max(0, y - radius); ny <= Math.Min(size - 1, y + radius); ny++)
                {
                    sum += horizontal[ny * size + x];
                    count++;
                }
                blurred[y * size + x] = sum / count;
            }
        }
        return blurred;
    }

    /// <summary>
    /// One heatmap category from map_data.json: the parser's full-resolution layer (deflated,
    /// ~2 m cells) when present, else the 512 grid. Null when the category is missing.
    /// </summary>
    public static (byte[] Data, int Size)? Read(System.Text.Json.JsonElement root, string category)
    {
        if (root.TryGetProperty("cellHeatmapsB64", out var fine) && fine.ValueKind == System.Text.Json.JsonValueKind.Object &&
            fine.TryGetProperty(category, out var fineEl) && fineEl.GetString() is { Length: > 0 } fineB64 &&
            root.TryGetProperty("cellLayerResolution", out var resEl) && resEl.TryGetInt32(out int fineSize) && fineSize > 0)
        {
            using var input = new System.IO.MemoryStream(Convert.FromBase64String(fineB64));
            using var deflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var output = new System.IO.MemoryStream();
            deflate.CopyTo(output);
            byte[] data = output.ToArray();
            if (data.Length == fineSize * fineSize) return (data, fineSize);
        }

        if (root.TryGetProperty("heatmaps", out var grid) && grid.ValueKind == System.Text.Json.JsonValueKind.Object &&
            grid.TryGetProperty(category, out var gridEl) && gridEl.GetString() is { Length: > 0 } gridB64)
        {
            byte[] data = Convert.FromBase64String(gridB64);
            if (data.Length == Size * Size) return (data, Size);
        }
        return null;
    }
}
