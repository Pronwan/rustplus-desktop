using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RustPlusDesk.Services
{
    /// <summary>
    /// Persists the latest known state of each in-game event (oil rig, cargo, heli, players, ...)
    /// to a single JSON file, overwriting the entry for a key when a newer event of that type
    /// arrives. Survives app crashes/restarts and is loaded back on startup.
    /// Paired with the append-only timeline.log for full history.
    /// </summary>
    public static class EventStateService
    {
        public sealed record EventEntry(string State, string Detail, DateTime TimeUtc);

        private static readonly string _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RustPlusDesk", "logs", "events.json");

        private static readonly object _lock = new();
        private static Dictionary<string, EventEntry> _states = new(StringComparer.OrdinalIgnoreCase);
        private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

        /// <summary>Reads the persisted state into memory. Call once on startup.</summary>
        public static void Load()
        {
            try
            {
                lock (_lock)
                {
                    if (!File.Exists(_path)) return;
                    var data = JsonSerializer.Deserialize<Dictionary<string, EventEntry>>(File.ReadAllText(_path));
                    if (data != null) _states = new Dictionary<string, EventEntry>(data, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch { /* missing/corrupt -> start fresh */ }
        }

        /// <summary>Overwrites the latest state for <paramref name="key"/> and persists immediately.</summary>
        public static void Record(string key, string state, string detail = "")
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            lock (_lock)
            {
                _states[key] = new EventEntry(state, detail ?? string.Empty, DateTime.UtcNow);
                Save();
            }
        }

        public static bool TryGet(string key, out EventEntry entry)
        {
            lock (_lock) { return _states.TryGetValue(key, out entry!); }
        }

        public static IReadOnlyDictionary<string, EventEntry> Snapshot()
        {
            lock (_lock) { return new Dictionary<string, EventEntry>(_states, StringComparer.OrdinalIgnoreCase); }
        }

        private static void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (dir != null) Directory.CreateDirectory(dir);

                // write to a temp file then swap, so a crash mid-write can't corrupt the store
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(_states, _json));
                File.Copy(tmp, _path, overwrite: true);
                File.Delete(tmp);
            }
            catch { /* best effort */ }
        }
    }
}
