using System;
using System.Collections.Generic;

namespace RustPlusDesk.Services.Deaths
{
    /// <summary>
    /// Classifies a death's map position into base / monument / open, plus a grid
    /// label. A base always wins over a monument (a base built at a monument is
    /// still "your base"); otherwise the nearest zone whose radius contains the
    /// point wins. The monument radius is what makes "died approaching the
    /// monument" count as a monument death.
    /// </summary>
    public sealed class DeathLocationClassifier
    {
        private readonly IReadOnlyList<DeathZone> _bases;
        private readonly IReadOnlyList<DeathZone> _monuments;
        private readonly double _mapSize;

        // Rust's companion-map grid cell size (world units per grid square).
        private const double GridCell = 146.28;

        public DeathLocationClassifier(
            IReadOnlyList<DeathZone> bases,
            IReadOnlyList<DeathZone> monuments,
            double mapSize)
        {
            _bases = bases;
            _monuments = monuments;
            _mapSize = mapSize;
        }

        public (string Type, string? Name) Classify(double? x, double? y)
        {
            if (x is null || y is null)
                return ("open", null);

            var baseHit = Nearest(_bases, x.Value, y.Value);
            if (baseHit is not null)
                return ("base", baseHit.Value.Name);

            var monHit = Nearest(_monuments, x.Value, y.Value);
            if (monHit is not null)
                return ("monument", monHit.Value.Name);

            return ("open", null);
        }

        /// <summary>Rust grid label (e.g. "D7"), or null when position/size is unknown.</summary>
        public string? Grid(double? x, double? y)
        {
            if (x is null || y is null || _mapSize <= 0)
                return null;

            int col = (int)Math.Floor(x.Value / GridCell);
            // Row 0 is the north (top) edge; y increases northward.
            int row = (int)Math.Floor((_mapSize - y.Value) / GridCell);
            if (col < 0 || row < 0)
                return null;

            return ColumnLabel(col) + row.ToString();
        }

        private static DeathZone? Nearest(IReadOnlyList<DeathZone> zones, double x, double y)
        {
            DeathZone? best = null;
            double bestDistance = double.MaxValue;

            foreach (var zone in zones)
            {
                double dx = x - zone.X;
                double dy = y - zone.Y;
                double distance = Math.Sqrt((dx * dx) + (dy * dy));
                if (distance <= zone.Radius && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = zone;
                }
            }

            return best;
        }

        /// <summary>0 => A, 25 => Z, 26 => AA, matching Rust's grid columns.</summary>
        private static string ColumnLabel(int col)
        {
            string label = string.Empty;
            col++; // 1-based for the base-26 conversion.
            while (col > 0)
            {
                int remainder = (col - 1) % 26;
                label = (char)('A' + remainder) + label;
                col = (col - 1) / 26;
            }

            return label;
        }
    }
}
