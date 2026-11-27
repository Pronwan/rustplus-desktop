using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RustPlusDesk.Services.Camera
{
    /// <summary>One stretch of time a player was in view of a camera.</summary>
    public sealed class CameraSighting
    {
        public string Name { get; set; } = "";
        public string Server { get; set; } = "";
        public string CameraId { get; set; } = "";
        public DateTime FirstSeenUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public bool Team { get; set; }
    }

    /// <summary>
    /// The players cameras have seen by name, with when. Cameras only send the real name once a player is close;
    /// further away they send a placeholder, which is not kept. Sightings of the same name on the same camera less
    /// than <see cref="MergeGap"/> apart are one sighting. Stored in %APPDATA%\RustPlusDesk\camera-seen-players.json.
    /// </summary>
    public static class CameraSeenPlayers
    {
        public static readonly TimeSpan MergeGap = TimeSpan.FromMinutes(5);
        private const int MaxEntries = 5000;
        private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(10);

        private static readonly object Gate = new();
        private static List<CameraSighting>? _items;
        private static DateTime _lastSaveUtc = DateTime.MinValue;
        private static bool _dirty;

        /// <summary>Raised (on the recording thread) when a name was added or a sighting started.</summary>
        public static event Action? Changed;

        internal static string FilePath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustPlusDesk", "camera-seen-players.json");

        /// <summary>True for names that say nothing about who it is (empty, or the generic "player" sent from afar).</summary>
        public static bool IsPlaceholderName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            var n = name.Trim().Trim('(', ')', '[', ']').Trim();
            return n.Length == 0 || n.Equals("player", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Notes that these names are in view of <paramref name="cameraId"/> now.</summary>
        public static void Record(string server, string cameraId, IEnumerable<(string Name, bool Team)> names, DateTime nowUtc)
        {
            bool started = false;
            lock (Gate)
            {
                var items = Load();
                foreach (var (raw, team) in names)
                {
                    if (IsPlaceholderName(raw)) continue;
                    var name = raw.Trim();
                    var open = items.LastOrDefault(s => s.CameraId == cameraId && s.Server == server &&
                        s.Name.Equals(name, StringComparison.Ordinal) && nowUtc - s.LastSeenUtc <= MergeGap);
                    if (open != null)
                    {
                        if (nowUtc > open.LastSeenUtc) open.LastSeenUtc = nowUtc;
                        open.Team |= team;
                    }
                    else
                    {
                        items.Add(new CameraSighting { Name = name, Server = server, CameraId = cameraId, FirstSeenUtc = nowUtc, LastSeenUtc = nowUtc, Team = team });
                        if (items.Count > MaxEntries) items.RemoveRange(0, items.Count - MaxEntries);
                        started = true;
                    }
                    _dirty = true;
                }
                if (_dirty && (started || nowUtc - _lastSaveUtc >= SaveInterval)) SaveLocked(nowUtc);
            }
            if (started) Changed?.Invoke();
        }

        /// <summary>All sightings, newest first; only those of <paramref name="server"/> when it is given.</summary>
        public static IReadOnlyList<CameraSighting> Snapshot(string? server = null)
        {
            lock (Gate)
            {
                return Load()
                    .Where(s => server == null || s.Server == server)
                    .OrderByDescending(s => s.LastSeenUtc)
                    .Select(s => new CameraSighting { Name = s.Name, Server = s.Server, CameraId = s.CameraId, FirstSeenUtc = s.FirstSeenUtc, LastSeenUtc = s.LastSeenUtc, Team = s.Team })
                    .ToList();
            }
        }

        /// <summary>How many different names <paramref name="cameraId"/> has seen on <paramref name="server"/>.</summary>
        public static int DistinctNames(string? server, string cameraId)
        {
            lock (Gate)
                return Load().Where(s => s.CameraId == cameraId && (server == null || s.Server == server))
                    .Select(s => s.Name).Distinct(StringComparer.Ordinal).Count();
        }

        /// <summary>The servers that have sightings.</summary>
        public static IReadOnlyList<string> Servers()
        {
            lock (Gate) return Load().Select(s => s.Server).Distinct().ToList();
        }

        /// <summary>Forgets the sightings of <paramref name="server"/> (and <paramref name="cameraId"/>), or all of them.</summary>
        public static void Clear(string? server = null, string? cameraId = null)
        {
            lock (Gate)
            {
                Load().RemoveAll(s => (server == null || s.Server == server) && (cameraId == null || s.CameraId == cameraId));
                _dirty = true;
                SaveLocked(DateTime.UtcNow);
            }
            Changed?.Invoke();
        }

        /// <summary>Writes pending changes (the window calls this when a camera closes).</summary>
        public static void Flush()
        {
            lock (Gate) if (_dirty) SaveLocked(DateTime.UtcNow);
        }

        private static List<CameraSighting> Load()
        {
            if (_items != null) return _items;
            try
            {
                _items = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<List<CameraSighting>>(File.ReadAllText(FilePath)) ?? new()
                    : new();
            }
            catch { _items = new(); }
            return _items;
        }

        private static void SaveLocked(DateTime nowUtc)
        {
            _lastSaveUtc = nowUtc;
            _dirty = false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(_items ?? new()));
                File.Move(tmp, FilePath, overwrite: true);
            }
            catch { _dirty = true; /* try again with the next sighting */ }
        }

        /// <summary>For tests: drops the in-memory copy so the next call reads <see cref="FilePath"/> again.</summary>
        internal static void ResetForTests()
        {
            lock (Gate) { _items = null; _dirty = false; _lastSaveUtc = DateTime.MinValue; }
        }
    }
}
