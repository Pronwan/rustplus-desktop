using System;
using System.Collections.Generic;
using System.Linq;

namespace RustPlusDesk.Services;

/// <summary>
/// Decides when a tool cupboard's remaining upkeep has dropped far enough to warn about.
///
/// Each cupboard is warned once per threshold it falls under (by default 2 hours, then 30
/// minutes), however many times it is checked. If the cupboard is refilled the warnings are
/// re-armed, with a 10% margin so a value that wobbles around a threshold between snapshots
/// does not repeat the same warning.
/// </summary>
public sealed class UpkeepAlertTracker
{
    public static readonly IReadOnlyList<int> DefaultThresholdsSeconds = new[] { 2 * 60 * 60, 30 * 60 };

    private const double ReArmMargin = 1.1;

    private readonly int[] _thresholds; // largest first
    private readonly Dictionary<string, int> _alertedLevel = new();

    public UpkeepAlertTracker(IEnumerable<int>? thresholdsSeconds = null)
    {
        _thresholds = (thresholdsSeconds ?? DefaultThresholdsSeconds)
            .Where(t => t > 0)
            .Distinct()
            .OrderByDescending(t => t)
            .ToArray();

        if (_thresholds.Length == 0)
            throw new ArgumentException("At least one positive threshold is required.", nameof(thresholdsSeconds));
    }

    /// <summary>
    /// Returns the threshold (in seconds) that was just crossed and should be announced, or
    /// null when there is nothing new to say. Unknown upkeep (null) never announces and never
    /// resets state. If several thresholds were crossed since the last check only the lowest
    /// is returned, so there is one message, not a burst.
    /// </summary>
    public int? Evaluate(string key, int? remainingSeconds)
    {
        if (remainingSeconds is not int remaining)
            return null;

        int level = LevelFor(remaining, 1.0);
        _alertedLevel.TryGetValue(key, out int alerted);

        if (level > alerted)
        {
            _alertedLevel[key] = level;
            return _thresholds[level - 1];
        }

        if (level < alerted)
        {
            int relaxed = LevelFor(remaining, ReArmMargin);
            if (relaxed < alerted)
                _alertedLevel[key] = relaxed;
        }

        return null;
    }

    public void Forget(string key) => _alertedLevel.Remove(key);

    public static string FormatDuration(int seconds)
    {
        if (seconds < 60) return "under 1m";

        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes}m"
            : $"{span.Minutes}m";
    }

    private int LevelFor(int remaining, double margin) =>
        _thresholds.Count(t => remaining <= t * margin);
}
