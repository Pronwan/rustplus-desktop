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

    /// <summary>Death count grouped by location.</summary>
    public sealed record LocationStat(string Location, int Deaths);

    /// <summary>A recent death, formatted for display.</summary>
    public sealed record RecentDeath(string Victim, string Location, string Grid, string Died);

    /// <summary>Aggregated view of the local death log for one server.</summary>
    public sealed class DeathStatsSummary
    {
        public int Total { get; init; }
        public int Victims { get; init; }
        public IReadOnlyList<VictimStat> ByVictim { get; init; } = Array.Empty<VictimStat>();
        public IReadOnlyList<LocationStat> ByLocation { get; init; } = Array.Empty<LocationStat>();
        public IReadOnlyList<RecentDeath> Recent { get; init; } = Array.Empty<RecentDeath>();
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

        public static DeathStatsSummary LoadForServer(string? serverKey)
        {
            var entries = ReadEntries(serverKey);
            if (entries.Count == 0)
                return new DeathStatsSummary();

            var byVictim = entries
                .GroupBy(e => e.name ?? "Unknown")
                .Select(g =>
                {
                    var survivals = g
                        .Where(e => e.spawn_at is > 0 && e.died_at > e.spawn_at!.Value)
                        .Select(e => e.died_at - e.spawn_at!.Value)
                        .ToList();
                    var avg = survivals.Count > 0 ? FormatDuration((long)survivals.Average()) : "—";
                    return new VictimStat(g.Key, g.Count(), avg);
                })
                .OrderByDescending(v => v.Deaths)
                .ToList();

            var byLocation = entries
                .GroupBy(e => LocationLabel(e.location_type, e.location_name))
                .Select(g => new LocationStat(g.Key, g.Count()))
                .OrderByDescending(l => l.Deaths)
                .ToList();

            var recent = entries
                .OrderByDescending(e => e.died_at)
                .Take(50)
                .Select(e => new RecentDeath(
                    e.name ?? "Unknown",
                    LocationLabel(e.location_type, e.location_name),
                    e.grid ?? "—",
                    DateTimeOffset.FromUnixTimeSeconds(e.died_at).LocalDateTime.ToString("g", CultureInfo.CurrentCulture)))
                .ToList();

            return new DeathStatsSummary
            {
                Total = entries.Count,
                Victims = byVictim.Count,
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

        private static string LocationLabel(string? type, string? name)
        {
            if (!string.IsNullOrEmpty(name))
                return name!;
            return type switch
            {
                "monument" => "Monument",
                "base" => "Base",
                _ => "Open",
            };
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
