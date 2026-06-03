using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services
{
    public class CheaterReportService
    {
        private readonly HttpClient _http = new();

        // ── CSV Export ────────────────────────────────────────────────────────
        public string BuildCsv(IEnumerable<CheaterRecord> records)
        {
            var sb = new StringBuilder();
            sb.AppendLine("SteamID,DisplayName,Confidence,Source,VAC,GameBan,DaysSinceBan,ReportCount,Confirmed,FlaggedAt,EvidenceNotes,EvidenceLink");
            foreach (var r in records)
            {
                sb.AppendLine(string.Join(",",
                    CsvCell(r.SteamId),
                    CsvCell(r.DisplayName),
                    r.Confidence.ToString(),
                    r.Source.ToString(),
                    r.HasVacBan ? "YES" : "NO",
                    r.HasGameBan ? "YES" : "NO",
                    r.DaysSinceLastBan.ToString(),
                    r.ReportCount.ToString(),
                    r.IsConfirmedBanned ? "YES" : "NO",
                    r.FlaggedAt.ToString("yyyy-MM-dd HH:mm"),
                    CsvCell(r.EvidenceNotes ?? ""),
                    CsvCell(r.EvidenceLink ?? "")
                ));
            }
            return sb.ToString();
        }

        public void SaveCsv(IEnumerable<CheaterRecord> records, string filePath)
        {
            File.WriteAllText(filePath, BuildCsv(records), Encoding.UTF8);
        }

        // ── F7 Report Text ────────────────────────────────────────────────────
        public string BuildF7ReportText(IEnumerable<CheaterRecord> records)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== CHEATER REPORT — Copy SteamIDs below into F7 Report ===");
            sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
            sb.AppendLine();
            foreach (var r in records)
            {
                sb.AppendLine($"Player:     {r.DisplayName}");
                sb.AppendLine($"SteamID:    {r.SteamId}");
                sb.AppendLine($"Confidence: {r.Confidence}  |  Source: {r.Source}");
                if (r.HasVacBan)  sb.AppendLine($"VAC Ban:    YES ({r.DaysSinceLastBan} days ago)");
                if (r.HasGameBan) sb.AppendLine($"Game Ban:   YES");
                if (!string.IsNullOrWhiteSpace(r.EvidenceNotes))
                    sb.AppendLine($"Notes:      {r.EvidenceNotes}");
                if (!string.IsNullOrWhiteSpace(r.EvidenceLink))
                    sb.AppendLine($"Evidence:   {r.EvidenceLink}");
                sb.AppendLine(new string('-', 50));
            }
            return sb.ToString();
        }

        // ── Discord Webhook ───────────────────────────────────────────────────
        public async Task SendDiscordReportAsync(
            string webhookUrl,
            string serverName,
            IEnumerable<CheaterRecord> records)
        {
            var list = records.ToList();
            var confirmed  = list.Count(r => r.IsConfirmedBanned || r.Confidence == ConfidenceLevel.Confirmed);
            var suspected  = list.Count(r => !r.IsConfirmedBanned && r.Confidence != ConfidenceLevel.Confirmed);

            // Build fields — Discord embed field limit is 25
            var fields = new List<object>();
            foreach (var r in list.Take(20))
            {
                var badges = new List<string>();
                if (r.HasVacBan)          badges.Add("🔴 VAC");
                if (r.HasGameBan)         badges.Add("🟠 GameBan");
                if (r.IsConfirmedBanned)  badges.Add("✅ Confirmed");
                var badgeStr = badges.Count > 0 ? string.Join(" ", badges) : "";

                var val = new StringBuilder();
                val.AppendLine($"`{r.SteamId}`");
                val.AppendLine($"Confidence: **{r.Confidence}** | Source: {r.Source}");
                if (!string.IsNullOrWhiteSpace(badgeStr)) val.AppendLine(badgeStr);
                if (!string.IsNullOrWhiteSpace(r.EvidenceNotes))
                    val.AppendLine($"_{TruncateStr(r.EvidenceNotes, 80)}_");

                fields.Add(new
                {
                    name   = $"⚠️ {r.DisplayName}",
                    value  = val.ToString().Trim(),
                    inline = false
                });
            }

            var embed = new
            {
                title       = $"🛡 Cheater Report — {serverName}",
                description = $"**{confirmed}** confirmed  |  **{suspected}** suspected  |  **{list.Count}** total flagged",
                color       = confirmed > 0 ? 15158332 : 15105570, // red or orange
                timestamp   = DateTime.UtcNow.ToString("o"),
                footer      = new { text = "Rust+ Desktop — Cheater Analytics" },
                fields
            };

            var payload = JsonSerializer.Serialize(new { embeds = new[] { embed } });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(webhookUrl, content);
            response.EnsureSuccessStatusCode();
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static string CsvCell(string val)
        {
            if (val.Contains(',') || val.Contains('"') || val.Contains('\n'))
                return $"\"{val.Replace("\"", "\"\"")}\"";
            return val;
        }

        private static string TruncateStr(string s, int max) =>
            s.Length <= max ? s : s[..max] + "…";
    }
}
