using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RustPlusDesk.Services.Deaths
{
    /// <summary>Who killed the player, how often, and when they last managed it.</summary>
    public sealed record KillerStat(string Name, int Kills, long LastAt, string? LastWeapon)
    {
        /// <summary>The last kill as a date, because the table shows it and a unix stamp is not one.</summary>
        public string LastAtText => KillerLogStore.When(LastAt);
    }

    /// <summary>
    /// Who was behind each death, written beside the death log rather than into it.
    ///
    /// The death itself is recorded the moment the game reports it; the name comes later, when
    /// the player has read their own death screen and pressed the button. Rewriting the line
    /// that was already written would mean editing a file another part of the app appends to,
    /// which is how log files lose entries. This is a second file keyed by the time of death,
    /// and the stats join the two — a death with nobody attached to it simply has no name yet,
    /// which is also the honest description of what happened.
    /// </summary>
    public static class KillerLogStore
    {
        private sealed class RawKiller
        {
            public long died_at { get; set; }
            public string? killer { get; set; }
            public string? weapon { get; set; }
            public long recorded_at { get; set; }
        }

        private static string PathFor(string serverKey) =>
            DeathReporter.LogPathFor(serverKey).Replace(".jsonl", ".killers.jsonl");

        /// <summary>
        /// Notes who was responsible for one death.
        ///
        /// Appended rather than replaced, and the reader takes the last line for a death, so
        /// pressing the button twice after correcting the region fixes the record instead of
        /// leaving two answers behind.
        /// </summary>
        public static void Record(string? serverKey, long diedAt, string killer, string? weapon)
        {
            if (string.IsNullOrEmpty(serverKey) || string.IsNullOrWhiteSpace(killer)) return;

            try
            {
                var path = PathFor(serverKey!);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var line = JsonSerializer.Serialize(new
                {
                    died_at = diedAt,
                    killer = killer.Trim(),
                    weapon = string.IsNullOrWhiteSpace(weapon) ? null : weapon!.Trim(),
                    recorded_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                });

                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch
            {
                // A death nobody could write down is a death without a name on it, which the
                // stats already know how to show.
            }
        }

        /// <summary>The name attached to each death, by the time that death happened.</summary>
        public static Dictionary<long, (string Killer, string? Weapon)> LoadByDeath(string? serverKey)
        {
            var byDeath = new Dictionary<long, (string, string?)>();
            if (string.IsNullOrEmpty(serverKey)) return byDeath;

            try
            {
                var path = PathFor(serverKey!);
                if (!File.Exists(path)) return byDeath;

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var raw = JsonSerializer.Deserialize<RawKiller>(line, options);
                        if (raw?.killer is not { Length: > 0 }) continue;

                        // Last one wins: a correction is written after what it corrects.
                        byDeath[raw.died_at] = (raw.killer, raw.weapon);
                    }
                    catch
                    {
                        // Skip a malformed line rather than lose the rest of the file.
                    }
                }
            }
            catch
            {
                // Unreadable file: no names, rather than no stats.
            }

            return byDeath;
        }

        /// <summary>
        /// Who has killed this player, most often first.
        ///
        /// Grouped case-insensitively, because a name read off a screen twice can differ in
        /// case where the recogniser was unsure, and two entries for one player would be a
        /// worse answer than one slightly misspelt. The spelling kept is the most recent.
        /// </summary>
        /// <param name="only">
        /// The deaths to count, when the view is showing a filtered set of them. Null counts
        /// every death there is a name for — a table that ignored the date filter above it
        /// would disagree with every other table on the page.
        /// </param>
        public static IReadOnlyList<KillerStat> Summarize(
            IReadOnlyDictionary<long, (string Killer, string? Weapon)> byDeath,
            IEnumerable<long>? only = null)
        {
            if (byDeath.Count == 0) return Array.Empty<KillerStat>();

            var entries = only == null
                ? byDeath
                : only.Where(byDeath.ContainsKey)
                      .Distinct()
                      .ToDictionary(at => at, at => byDeath[at]);

            if (entries.Count == 0) return Array.Empty<KillerStat>();

            return entries
                .Select(e => (Name: e.Value.Killer, Weapon: e.Value.Weapon, At: e.Key))
                .GroupBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(group =>
                {
                    var latest = group.OrderByDescending(e => e.At).First();
                    return new KillerStat(latest.Name, group.Count(), latest.At, latest.Weapon);
                })
                .OrderByDescending(k => k.Kills)
                .ThenByDescending(k => k.LastAt)
                .ToList();
        }

        /// <summary>
        /// Whether a name is the player themselves, which Rust shows for a suicide.
        ///
        /// Worth telling apart: a list of who has killed you, topped by you, is a list nobody
        /// asked for. The comparison is loose because one side came off a screen.
        /// </summary>
        public static bool IsSelf(string killer, string? ownName) =>
            !string.IsNullOrWhiteSpace(ownName) &&
            string.Equals(killer.Trim(), ownName!.Trim(), StringComparison.CurrentCultureIgnoreCase);

        /// <summary>A death's time as something readable, in the local zone.</summary>
        public static string When(long unixSeconds) =>
            DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime()
                .ToString("d MMM, HH:mm", CultureInfo.CurrentCulture);
    }
}
