using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RustPlusDesk.Services.Deaths
{
    /// <summary>Per-player death count + average survival.</summary>
    public sealed record VictimStat(string Victim, int Deaths, string AvgSurvival);

    /// <summary>Death count grouped by a specific location, with its type for colouring.</summary>
    public sealed record LocationStat(string Location, string Type, int Deaths);

    /// <summary>Death share for a broad area (monument / base / open).</summary>
    public sealed record AreaStat(string Name, string Type, int Deaths, int Percent);

    /// <summary>A recent death, formatted for display.</summary>
    public sealed record RecentDeath(string Victim, string Type, string Location, string Grid, string Died);

    /// <summary>Aggregated view of the local death log for one server.</summary>
    public sealed class DeathStatsSummary
    {
        public int Total { get; init; }
        public int Victims { get; init; }
        public string AvgSurvival { get; init; } = "—";
        public string DeadliestPlace { get; init; } = "—";
        public string DeadliestGrid { get; init; } = "—";
        public IReadOnlyList<AreaStat> ByArea { get; init; } = Array.Empty<AreaStat>();
        public IReadOnlyList<VictimStat> ByVictim { get; init; } = Array.Empty<VictimStat>();
        public IReadOnlyList<LocationStat> ByLocation { get; init; } = Array.Empty<LocationStat>();
        public IReadOnlyList<RecentDeath> Recent { get; init; } = Array.Empty<RecentDeath>();

        public bool HasData => Total > 0;

        public string Headline => Total == 0
            ? "No deaths logged yet for this server."
            : $"{Total} death(s) across {Victims} player(s).";
    }

    /// <summary>
    /// Reads the local JSON-lines death log written by <see cref="DeathReporter"/>
    /// and aggregates it for the in-app stats view. Local-only, so it works for
    /// free accounts and offline; premium's shared/cloud view is the dashboard.
    /// </summary>
    public static class DeathLogStore
    {
        private sealed class RawDeath
        {
            public string? name { get; set; }
            public long died_at { get; set; }
            public long? spawn_at { get; set; }
            public string? grid { get; set; }
            public string? location_type { get; set; }
            public string? location_name { get; set; }
        }

        /// <summary>Delete the local death log for a server.</summary>
        public static void Clear(string? serverKey)
        {
            if (string.IsNullOrEmpty(serverKey))
                return;

            try
            {
                var path = DeathReporter.LogPathFor(serverKey);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // A failed delete must not crash the stats view.
            }
        }

        public static DeathStatsSummary LoadForServer(string? serverKey)
        {
            var entries = ReadEntries(serverKey);
            if (entries.Count == 0)
                return new DeathStatsSummary();

            int total = entries.Count;

            var byVictim = entries
                .GroupBy(e => e.name ?? "Unknown")
                .Select(g => new VictimStat(g.Key, g.Count(), AverageSurvival(g)))
                .OrderByDescending(v => v.Deaths)
                .ToList();

            var byLocation = entries
                .GroupBy(e => LocationLabel(e.location_type, e.location_name))
                .Select(g => new LocationStat(g.Key, NormalizeType(g.First().location_type), g.Count()))
                .OrderByDescending(l => l.Deaths)
                .ToList();

            var byArea = entries
                .GroupBy(e => NormalizeType(e.location_type))
                .Select(g => new AreaStat(AreaName(g.Key), g.Key, g.Count(), (int)Math.Round(100.0 * g.Count() / total)))
                .OrderByDescending(a => a.Deaths)
                .ToList();

            var deadliestGrid = entries
                .Where(e => !string.IsNullOrEmpty(e.grid) && e.grid != "off-grid")
                .GroupBy(e => e.grid!)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key} ({g.Count()})")
                .FirstOrDefault() ?? "—";

            var recent = entries
                .OrderByDescending(e => e.died_at)
                .Take(50)
                .Select(e => new RecentDeath(
                    e.name ?? "Unknown",
                    NormalizeType(e.location_type),
                    LocationLabel(e.location_type, e.location_name),
                    e.grid ?? "—",
                    DateTimeOffset.FromUnixTimeSeconds(e.died_at).LocalDateTime.ToString("g", CultureInfo.CurrentCulture)))
                .ToList();

            return new DeathStatsSummary
            {
                Total = total,
                Victims = byVictim.Count,
                AvgSurvival = AverageSurvival(entries),
                DeadliestPlace = byLocation.FirstOrDefault()?.Location ?? "—",
                DeadliestGrid = deadliestGrid,
                ByArea = byArea,
                ByVictim = byVictim,
                ByLocation = byLocation,
                Recent = recent,
            };
        }

        private static List<RawDeath> ReadEntries(string? serverKey)
        {
            var entries = new List<RawDeath>();
            if (string.IsNullOrEmpty(serverKey))
                return entries;

            var path = DeathReporter.LogPathFor(serverKey);
            if (!File.Exists(path))
                return entries;

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    var raw = JsonSerializer.Deserialize<RawDeath>(line, options);
                    if (raw != null)
                        entries.Add(raw);
                }
                catch
                {
                    // Skip a malformed line rather than fail the whole view.
                }
            }

            return entries;
        }

        private static string AverageSurvival(IEnumerable<RawDeath> deaths)
        {
            var survivals = deaths
                .Where(e => e.spawn_at is > 0 && e.died_at > e.spawn_at!.Value)
                .Select(e => e.died_at - e.spawn_at!.Value)
                .ToList();
            return survivals.Count > 0 ? FormatDuration((long)survivals.Average()) : "—";
        }

        private static string NormalizeType(string? type) => type switch
        {
            "monument" => "monument",
            "base" => "base",
            _ => "open",
        };

        private static string AreaName(string type) => type switch
        {
            "monument" => "Monument",
            "base" => "Base",
            _ => "Open",
        };

        private static string LocationLabel(string? type, string? name)
        {
            if (!string.IsNullOrEmpty(name))
                return name!;
            return AreaName(NormalizeType(type));
        }

        private static string FormatDuration(long seconds)
        {
            if (seconds < 60)
                return $"{seconds}s";
            long m = seconds / 60;
            long s = seconds % 60;
            if (m < 60)
                return $"{m}m {s}s";
            long h = m / 60;
            m %= 60;
            return $"{h}h {m}m";
        }
    }
}
