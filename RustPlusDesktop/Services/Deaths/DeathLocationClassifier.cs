using System;
using System.Collections.Generic;

namespace RustPlusDesk.Services.Deaths
{
    /// <summary>
    /// Classifies a death's map position into base / monument / open. A base
    /// always wins over a monument (a base built at a monument is still "your
    /// base"); otherwise the nearest zone whose radius contains the point wins.
    /// The monument radius is what makes "died approaching the monument" count as
    /// a monument death.
    ///
    /// The grid label is delegated to the app's shared coordinate→grid math
    /// (the same GetGridLabel the chat notifications and map markers use), so the
    /// death log matches what the client already shows for a death/spawn.
    /// </summary>
    public sealed class DeathLocationClassifier
    {
        private readonly IReadOnlyList<DeathZone> _bases;
        private readonly IReadOnlyList<DeathZone> _monuments;
        private readonly Func<double, double, string?> _gridResolver;

        public DeathLocationClassifier(
            IReadOnlyList<DeathZone> bases,
            IReadOnlyList<DeathZone> monuments,
            Func<double, double, string?> gridResolver)
        {
            _bases = bases;
            _monuments = monuments;
            _gridResolver = gridResolver;
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

        /// <summary>Grid label via the app's shared math; null when position is unknown.</summary>
        public string? Grid(double? x, double? y)
            => (x is null || y is null) ? null : _gridResolver(x.Value, y.Value);

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
    }
}
