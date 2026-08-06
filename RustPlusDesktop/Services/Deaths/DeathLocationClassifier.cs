using System;
using System.Collections.Generic;

namespace RustPlusDesk.Services.Deaths
{
    /// <summary>
    /// Classifies a death's map position into base / monument / open. A base
    /// always wins over a monument (a base built at a monument is still "your
    /// base"); otherwise the nearest monument whose radius contains the point
    /// wins. The monument radius is what makes "died approaching the monument"
    /// count as a monument death.
    ///
    /// Monuments compare in world coordinates directly. Bases are delegated to the
    /// app (which matches the death against the team's in-game base map notes, also
    /// in world coordinates). The grid label is likewise delegated to the app's
    /// shared GetGridLabel so the death log matches the chat/marker output.
    /// </summary>
    public sealed class DeathLocationClassifier
    {
        private readonly IReadOnlyList<DeathZone> _monuments;
        private readonly Func<double, double, string?> _baseResolver;
        private readonly Func<double, double, string?> _gridResolver;

        public DeathLocationClassifier(
            IReadOnlyList<DeathZone> monuments,
            Func<double, double, string?> baseResolver,
            Func<double, double, string?> gridResolver)
        {
            _monuments = monuments;
            _baseResolver = baseResolver;
            _gridResolver = gridResolver;
        }

        public (string Type, string? Name) Classify(double? x, double? y)
        {
            if (x is null || y is null)
                return ("open", null);

            var baseName = _baseResolver(x.Value, y.Value);
            if (!string.IsNullOrEmpty(baseName))
                return ("base", baseName);

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
