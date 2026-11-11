using System;
using System.Collections.Generic;
using System.Linq;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services
{
    /// <summary>
    /// Servers the user deleted here, so Facepunch's notification history cannot put them back.
    ///
    /// The history holds every pairing the account ever received. Without this, the launch-time
    /// history sync recreated a deleted server from its old pairing notifications. A pairing
    /// newer than the deletion is the user pairing the server again in game, and is let through
    /// (and clears the record).
    ///
    /// Keyed by both Facepunch's server id and host:port, because either may be all a pairing
    /// carries. Devices are tracked on the profile instead (<see cref="ServerProfile.RemovedDevices"/>).
    /// </summary>
    public static class RemovedServerStore
    {
        private const string CacheKey = "history_removed_servers";

        /// <summary>Records older than this are dropped; Facepunch's history does not reach that far back.</summary>
        private static readonly TimeSpan MaxAge = TimeSpan.FromDays(120);

        private static readonly object s_lock = new();

        public static void MarkRemoved(ServerProfile profile)
        {
            if (profile == null) return;
            lock (s_lock)
            {
                var map = Load();
                var now = DateTime.UtcNow;
                foreach (var key in KeysOf(profile.ServerId, profile.Host, profile.Port))
                    map[key] = now;
                Save(map);
            }
        }

        /// <summary>
        /// True when a pairing for this server made at <paramref name="pairedAtUtc"/> is no newer
        /// than the user's deletion of it.
        /// </summary>
        public static bool IsRemovedServerPairing(PairingPayload payload, DateTime pairedAtUtc)
        {
            if (payload == null) return false;
            lock (s_lock)
            {
                var map = Load();
                foreach (var key in KeysOf(payload.ServerId, payload.Host, payload.Port))
                {
                    if (map.TryGetValue(key, out var removedAt) && pairedAtUtc <= removedAt)
                        return true;
                }
                return false;
            }
        }

        /// <summary>The server was paired again: forget the deletion.</summary>
        public static void Clear(string? serverId, string? host, int port)
        {
            lock (s_lock)
            {
                var map = Load();
                bool changed = false;
                foreach (var key in KeysOf(serverId, host, port))
                    changed |= map.Remove(key);
                if (changed) Save(map);
            }
        }

        private static IEnumerable<string> KeysOf(string? serverId, string? host, int port)
        {
            if (!string.IsNullOrWhiteSpace(serverId))
                yield return "id:" + serverId.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(host) && host != "companion-socket" && port > 0)
                yield return $"addr:{host.Trim().ToLowerInvariant()}:{port}";
        }

        private static Dictionary<string, DateTime> Load()
        {
            Dictionary<string, DateTime>? stored = null;
            try { stored = StorageService.LoadCache<Dictionary<string, DateTime>>(CacheKey); }
            catch { /* unreadable cache: start empty */ }

            var map = new Dictionary<string, DateTime>(stored ?? new Dictionary<string, DateTime>(), StringComparer.Ordinal);
            var cutoff = DateTime.UtcNow - MaxAge;
            foreach (var stale in map.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList())
                map.Remove(stale);
            return map;
        }

        private static void Save(Dictionary<string, DateTime> map)
        {
            try { StorageService.SaveCache(CacheKey, map); }
            catch { /* best effort: worst case a deleted server can come back once */ }
        }
    }
}
