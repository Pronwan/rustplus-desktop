using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RustPlusDesk.Services;

/// <summary>
/// Remembers which alarms have already been shown, across restarts.
///
/// Google keeps pushes for a listener that is away and delivers the backlog the
/// moment it reconnects, so starting the app replays whatever happened while it
/// was closed. The in-memory duplicate check cannot help with that: it is empty
/// at start, which is precisely when the backlog arrives.
///
/// An entry is an alarm's identity and the time it happened. The same alarm at
/// the same time is one the player has already seen. The same alarm at a later
/// time is a new one and is shown — a base attacked twice is two raids, not a
/// repeat.
///
/// Every failure here resolves towards showing the notification. A missing file,
/// an unreadable one, a disk that will not take the write: none of them are worth
/// swallowing a raid alarm over.
/// </summary>
internal static class SeenAlarmStore
{
    private static readonly object Gate = new();
    private static Dictionary<string, DateTime>? _entries;

    /// Long enough to cover a holiday away from the game, short enough that the
    /// file stays a few kilobytes.
    private static readonly TimeSpan Retention = TimeSpan.FromDays(14);

    private const int MaxEntries = 500;

    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RustPlusDesk", "seen-alarms.json");

    /// <summary>
    /// Records an alarm as shown and reports whether it was new.
    /// Returns false only when this same alarm, at this same time, was recorded before.
    /// </summary>
    public static bool MarkIfNew(string? key, DateTime eventTime)
    {
        if (string.IsNullOrWhiteSpace(key)) return true;

        try
        {
            lock (Gate)
            {
                var entries = Load();

                // Seconds, because the two halves of one alarm can be timestamped a
                // few ticks apart and no alarm is distinguished by its milliseconds.
                if (entries.TryGetValue(key!, out var previous)
                    && Truncate(previous) == Truncate(eventTime))
                {
                    return false;
                }

                entries[key!] = eventTime;
                Prune(entries);
                Save(entries);
                return true;
            }
        }
        catch
        {
            return true;
        }
    }

    private static DateTime Truncate(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, value.Kind);

    private static Dictionary<string, DateTime> Load()
    {
        if (_entries != null) return _entries;

        try
        {
            if (File.Exists(StorePath))
            {
                _entries = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(
                    File.ReadAllText(StorePath)) ?? new Dictionary<string, DateTime>();
            }
        }
        catch
        {
            // A file we cannot read is a file we replace on the next write.
        }

        return _entries ??= new Dictionary<string, DateTime>();
    }

    private static void Prune(Dictionary<string, DateTime> entries)
    {
        var cutoff = DateTime.Now - Retention;
        foreach (var stale in entries.Where(e => e.Value < cutoff).Select(e => e.Key).ToList())
            entries.Remove(stale);

        if (entries.Count <= MaxEntries) return;

        foreach (var oldest in entries.OrderBy(e => e.Value)
                     .Take(entries.Count - MaxEntries).Select(e => e.Key).ToList())
        {
            entries.Remove(oldest);
        }
    }

    private static void Save(Dictionary<string, DateTime> entries)
    {
        var dir = Path.GetDirectoryName(StorePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(StorePath, JsonSerializer.Serialize(entries));
    }
}
