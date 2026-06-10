using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services
{
    public class CheaterAnalyticsService
    {
        private readonly string _dataDir;

        public CheaterAnalyticsService(string appDataDir)
        {
            _dataDir = appDataDir;
        }

        // ── persistence ───────────────────────────────────────────────────────

        private string RecordPath(string serverId) =>
            Path.Combine(_dataDir, $"cheater_records_{serverId}.json");

        public List<CheaterRecord> LoadRecords(string serverId)
        {
            var path = RecordPath(serverId);
            if (!File.Exists(path)) return new();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<CheaterRecord>>(json) ?? new();
        }

        public void SaveRecords(string serverId, List<CheaterRecord> records)
        {
            var json = JsonSerializer.Serialize(records,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(RecordPath(serverId), json);
        }

        // ── analytics ─────────────────────────────────────────────────────────

        public CheaterAnalyticsSnapshot BuildSnapshot(
            string serverId,
            int activePlayers,
            string wipeId,
            List<CheaterRecord> records)
        {
            var wipeRecords = records.Where(r => r.WipeId == wipeId).ToList();
            return new CheaterAnalyticsSnapshot
            {
                ServerId              = serverId,
                Timestamp             = DateTime.UtcNow,
                WipeId                = wipeId,
                ActivePlayerCount     = activePlayers,
                ConfirmedCheaterCount = wipeRecords.Count(r => r.IsConfirmedBanned ||
                                            r.Confidence == ConfidenceLevel.Confirmed),
                SuspectedFlaggedCount = wipeRecords.Count(r => !r.IsConfirmedBanned &&
                                            r.Confidence != ConfidenceLevel.Confirmed)
            };
        }

        public List<CheaterAnalyticsSnapshot> LoadSnapshotHistory(string serverId)
        {
            var histPath = Path.Combine(_dataDir, $"cheater_snapshots_{serverId}.json");
            if (!File.Exists(histPath)) return new();
            var json = File.ReadAllText(histPath);
            return JsonSerializer.Deserialize<List<CheaterAnalyticsSnapshot>>(json) ?? new();
        }

        public void AppendSnapshot(string serverId, CheaterAnalyticsSnapshot snap)
        {
            var history = LoadSnapshotHistory(serverId);
            history.Add(snap);
            var histPath = Path.Combine(_dataDir, $"cheater_snapshots_{serverId}.json");
            File.WriteAllText(histPath,
                JsonSerializer.Serialize(history,
                    new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
