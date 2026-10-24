using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services;

public static class DeviceAutomationEvaluator
{
    public readonly record struct PlayerSnapshot(ulong SteamId, bool IsOnline, double? X, double? Y);

    static DeviceAutomationEvaluator() => Verify();

    public static bool IsProximityMatch(
        DeviceAutomationRule rule,
        double anchorX,
        double anchorY,
        IReadOnlyCollection<PlayerSnapshot> players)
    {
        var selected = rule.PlayerMatchMode.StartsWith("Specific", StringComparison.Ordinal)
            ? players.Where(player => player.SteamId == rule.SpecificPlayerSteamId).ToList()
            : players.ToList();

        if (rule.PlayerMatchMode == "AnyOffline") return selected.Any(player => !player.IsOnline);
        if (rule.PlayerMatchMode == "AllOffline") return selected.Count > 0 && selected.All(player => !player.IsOnline);
        if (rule.PlayerMatchMode == "SpecificOffline") return selected.Count == 1 && !selected[0].IsOnline;

        bool IsNear(PlayerSnapshot player) => player.IsOnline
            && player.X.HasValue
            && player.Y.HasValue
            && Math.Sqrt(Math.Pow(player.X.Value - anchorX, 2) + Math.Pow(player.Y.Value - anchorY, 2)) <= rule.DistanceMeters;

        return rule.PlayerMatchMode == "AllOnline"
            ? selected.Count > 0 && selected.All(IsNear)
            : selected.Any(IsNear);
    }

    public static bool IsTimeMatch(DeviceAutomationRule rule, string currentTime)
    {
        return TryGetTimeMatch(rule, currentTime, out bool matched) && matched;
    }

    public static bool TryGetTimeMatch(DeviceAutomationRule rule, string currentTime, out bool matched)
    {
        matched = false;
        if (!TryMinutes(rule.StartTime, out int start)
            || !TryMinutes(rule.EndTime, out int end)
            || !TryMinutes(currentTime, out int current))
            return false;

        matched = start == end || (start < end
            ? current >= start && current < end
            : current >= start || current < end);
        return true;
    }

    private static bool TryMinutes(string value, out int minutes)
    {
        minutes = 0;
        if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var time)) return false;
        minutes = ((int)time.TotalMinutes % 1440 + 1440) % 1440;
        return true;
    }

    [Conditional("DEBUG")]
    private static void Verify()
    {
        var rule = new DeviceAutomationRule { DistanceMeters = 250 };
        Debug.Assert(IsProximityMatch(rule, 1000, 1000, new[] { new PlayerSnapshot(1, true, 1100, 1000) }));
        Debug.Assert(!IsProximityMatch(rule, 1000, 1000, new[] { new PlayerSnapshot(1, false, 1000, 1000) }));
        rule.PlayerMatchMode = "AllOffline";
        Debug.Assert(IsProximityMatch(rule, 0, 0, new[] { new PlayerSnapshot(1, false, null, null) }));
        rule.StartTime = "20:00";
        rule.EndTime = "08:00";
        Debug.Assert(IsTimeMatch(rule, "23:30") && IsTimeMatch(rule, "07:59") && !IsTimeMatch(rule, "12:00"));
        Debug.Assert(!TryGetTimeMatch(rule, "–", out _));
    }
}
