using System;
using System.Collections.Generic;
using System.Linq;

namespace RustPlusDesk.Services.PlayerWipeTracker;

/// <summary>Pure derivation of glance-value intel from a player's recorded wipe data.</summary>
public static class TrackerInsightsBuilder
{
    public static TrackerInsights Build(
        IReadOnlyList<PlayerObservation> observations,
        IReadOnlyList<TrackerSegment> segments,
        TrackerSummary summary,
        DateTime nowUtc)
    {
        if (observations is null || observations.Count == 0)
            return TrackerInsights.Empty;

        var now = nowUtc.ToUniversalTime();
        var ordered = observations.OrderBy(o => o.TimestampUtc).ToArray();
        var first = ordered[0];
        var last = ordered[^1];
        var sessions = ordered.Select(o => o.SessionId).Distinct(StringComparer.Ordinal).Count();

        var topMonument = summary.MonumentVisits
            .GroupBy(visit => visit.Name, StringComparer.Ordinal)
            .Select(group => new
            {
                Name = group.Key,
                Duration = TimeSpan.FromTicks(group.Sum(visit => visit.EstimatedDuration.Ticks)),
                Visits = group.Count(),
            })
            .OrderByDescending(item => item.Duration)
            .FirstOrDefault();

        // Longest blind gap = the longest stretch we lost eyes on the player (Unknown segment).
        var longestGap = TimeSpan.Zero;
        DateTime? longestGapStart = null;
        foreach (var segment in segments)
        {
            if (segment.State != PlayerActivityState.Unknown)
                continue;
            var span = segment.EndUtc - segment.StartUtc;
            if (span > longestGap)
            {
                longestGap = span;
                longestGapStart = segment.StartUtc;
            }
        }

        // Peak hour = local hour of day with the most *active* time, splitting each
        // active segment across the hour boundaries it crosses.
        var hourBuckets = new double[24];
        foreach (var segment in segments)
        {
            if (segment.State is not (PlayerActivityState.Moving or PlayerActivityState.Stationary or PlayerActivityState.Afk))
                continue;
            var cursor = segment.StartUtc.ToLocalTime();
            var end = segment.EndUtc.ToLocalTime();
            while (cursor < end)
            {
                var nextHour = cursor.Date.AddHours(cursor.Hour + 1);
                var sliceEnd = nextHour < end ? nextHour : end;
                hourBuckets[cursor.Hour] += (sliceEnd - cursor).TotalSeconds;
                cursor = sliceEnd;
            }
        }
        int? peakHour = null;
        var peakSeconds = 0d;
        for (var hour = 0; hour < 24; hour++)
        {
            if (hourBuckets[hour] > peakSeconds)
            {
                peakSeconds = hourBuckets[hour];
                peakHour = hour;
            }
        }

        var currentState = PlayerWipeTrackerEngine.Classify(last);
        var likelyOnline = last.IsConnected && last.Online && !last.Dead &&
            (now - last.TimestampUtc) <= TimeSpan.FromMinutes(3);

        return new TrackerInsights(
            first.TimestampUtc,
            last.TimestampUtc,
            sessions,
            topMonument?.Name,
            topMonument?.Duration ?? TimeSpan.Zero,
            topMonument?.Visits ?? 0,
            longestGap,
            longestGapStart,
            peakHour,
            TimeSpan.FromSeconds(peakSeconds),
            currentState,
            last.LocationType,
            last.LocationName,
            last.Grid,
            last.TimestampUtc,
            likelyOnline);
    }
}
