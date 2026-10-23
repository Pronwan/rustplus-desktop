using System;
using System.Collections;

namespace RustPlusDesk.Services.Deaths
{
    /// <summary>
    /// Which parts of the world are water, worked out once per map image so that asking about
    /// a position is a single lookup. The wipe tracker classifies every teammate on every
    /// poll, so the per-question cost is what matters, not the one-off build.
    ///
    /// The Rust+ map paints the sea, rivers and lakes in one water colour and frames the
    /// island with a band of open ocean, so a corner pixel is always sea. A cell whose pixels
    /// sit close to that colour is water. Anything outside the world square is ocean by
    /// definition and needs no cell at all.
    /// </summary>
    public sealed class MapWaterMask
    {
        /// <summary>World units per cell: finer than a player can tell apart near a shoreline.</summary>
        public const double CellSize = 8;

        /// <summary>
        /// How far (RGB distance) a cell may sit from the sampled sea colour. Loose enough for
        /// JPEG noise and the lighter shallows, tight enough to keep beaches and snow out.
        /// </summary>
        public const double Tolerance = 36;

        private readonly BitArray _water;
        private readonly int _cells;
        private readonly double _worldSize;

        private MapWaterMask(BitArray water, int cells, double worldSize)
        {
            _water = water;
            _cells = cells;
            _worldSize = worldSize;
        }

        public bool IsWater(double x, double y)
        {
            if (_worldSize <= 0)
                return false;

            if (x < 0 || y < 0 || x > _worldSize || y > _worldSize)
                return true;

            int cx = Math.Min(_cells - 1, (int)(x / CellSize));
            int cy = Math.Min(_cells - 1, (int)(y / CellSize));
            return _water[(cy * _cells) + cx];
        }

        /// <summary>
        /// Builds the mask from 32-bit BGRA pixels of the map image. The pixels are only read
        /// here; the mask keeps one bit per cell.
        /// </summary>
        /// <param name="padWorld">World units of ocean the image adds around the world square, split evenly on both sides.</param>
        public static MapWaterMask Build(byte[] bgra, int width, int height, int stride, double worldSize, double padWorld)
        {
            if (worldSize <= 0 || width <= 0 || height <= 0)
                return new MapWaterMask(new BitArray(0), 0, 0);

            var sea = SeaColour(bgra, width, height, stride);
            int cells = Math.Max(1, (int)Math.Ceiling(worldSize / CellSize));
            var water = new BitArray(cells * cells);

            double total = worldSize + padWorld;
            double half = padWorld / 2;

            for (int cy = 0; cy < cells; cy++)
            {
                // Cell centre in world space; image rows run north to south.
                double y = Math.Min(worldSize, (cy + 0.5) * CellSize);
                int v = (int)Math.Round((worldSize - y + half) / total * (height - 1));

                for (int cx = 0; cx < cells; cx++)
                {
                    double x = Math.Min(worldSize, (cx + 0.5) * CellSize);
                    int u = (int)Math.Round((x + half) / total * (width - 1));

                    // A 3x3 average rides over JPEG blocks and single-pixel detail.
                    var p = Average(bgra, width, height, stride, u, v, 1);
                    double db = p.B - sea.B, dg = p.G - sea.G, dr = p.R - sea.R;
                    if ((db * db) + (dg * dg) + (dr * dr) <= Tolerance * Tolerance)
                        water[(cy * cells) + cx] = true;
                }
            }

            return new MapWaterMask(water, cells, worldSize);
        }

        /// <summary>Averaged over the four corners, so one stray pixel cannot set the reference.</summary>
        private static (double B, double G, double R) SeaColour(byte[] bgra, int width, int height, int stride)
        {
            int inset = Math.Min(4, Math.Max(0, (Math.Min(width, height) / 2) - 1));
            var a = Average(bgra, width, height, stride, inset, inset, 0);
            var b = Average(bgra, width, height, stride, width - 1 - inset, inset, 0);
            var c = Average(bgra, width, height, stride, inset, height - 1 - inset, 0);
            var d = Average(bgra, width, height, stride, width - 1 - inset, height - 1 - inset, 0);
            return ((a.B + b.B + c.B + d.B) / 4, (a.G + b.G + c.G + d.G) / 4, (a.R + b.R + c.R + d.R) / 4);
        }

        private static (double B, double G, double R) Average(byte[] bgra, int width, int height, int stride, int cx, int cy, int radius)
        {
            double b = 0, g = 0, r = 0;
            int n = 0;
            for (int y = cy - radius; y <= cy + radius; y++)
            {
                if (y < 0 || y >= height) continue;
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    if (x < 0 || x >= width) continue;
                    int i = (y * stride) + (x * 4);
                    b += bgra[i];
                    g += bgra[i + 1];
                    r += bgra[i + 2];
                    n++;
                }
            }

            return n == 0 ? (0, 0, 0) : (b / n, g / n, r / n);
        }
    }
}
