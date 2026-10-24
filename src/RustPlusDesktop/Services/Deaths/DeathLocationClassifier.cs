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
    ///
    /// Deep Sea and water are places of their own, not "open" ground: dying at sea or
    /// in the Deep Sea instance is a different story from dying in a field.
    /// </summary>
    public sealed class DeathLocationClassifier
    {
        public const string WaterType = "water";
        public const string DeepSeaType = "deep_sea";

        private readonly IReadOnlyList<DeathZone> _monuments;
        private readonly Func<double, double, string?> _baseResolver;
        private readonly Func<double, double, string?> _gridResolver;
        private readonly Func<double, double, bool>? _waterResolver;

        public DeathLocationClassifier(
            IReadOnlyList<DeathZone> monuments,
            Func<double, double, string?> baseResolver,
            Func<double, double, string?> gridResolver,
            Func<double, double, bool>? waterResolver = null)
        {
            _monuments = monuments;
            _baseResolver = baseResolver;
            _gridResolver = gridResolver;
            _waterResolver = waterResolver;
        }

        public (string Type, string? Name) Classify(double? x, double? y)
        {
            if (x is null || y is null)
                return ("open", null);

            // A separate instance far off the map's west edge: nothing on the main map
            // (bases, monuments, the coast) applies there.
            if (PlayerWipeTracker.TrackerRouteContinuity.IsInDeepSea(x.Value))
                return (DeepSeaType, null);

            var baseName = _baseResolver(x.Value, y.Value);
            if (!string.IsNullOrEmpty(baseName))
                return ("base", baseName);

            // Before water: oil rigs, harbours and the like sit on it and keep their names.
            var monHit = Nearest(_monuments, x.Value, y.Value);
            if (monHit is not null)
                return ("monument", monHit.Value.Name);

            if (_waterResolver?.Invoke(x.Value, y.Value) == true)
                return (WaterType, null);

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
