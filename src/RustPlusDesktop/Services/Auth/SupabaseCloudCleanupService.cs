using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services.Auth
{
    public class PurgeResult
    {
        public bool Success { get; set; }

        /// <summary>
        /// How many servers had their cloud data removed. The API reports this
        /// rather than a per-table breakdown: everything belonging to a server
        /// goes together, so the number of servers is the whole answer.
        /// </summary>
        public int RemovedServers { get; set; }

        public string? ErrorMessage { get; set; }
    }

    public static class SupabaseCloudCleanupService
    {
        /// <summary>
        /// Deletes all cloud data (map overlays, base markers, smart devices, user servers) for a single server key.
        /// Resets Alexa active_server_key if it matched this server (without unlinking the Alexa account).
        /// </summary>
        public static async Task<bool> DeleteCloudDataForServerAsync(string host, int port, string steamId)
        {
            if (string.IsNullOrWhiteSpace(host) || port <= 0 || string.IsNullOrWhiteSpace(steamId))
                return false;

            try
            {
                var serverKey = $"{host}-{port}";
                var queryParams = new Dictionary<string, string>
                {
                    { "server_key", serverKey },
                    { "steam_id", steamId }
                };

                var responseJson = await SupabaseAuthManager.CallEdgeFunctionAsync("overlay", HttpMethod.Delete, null, queryParams);
                return !string.IsNullOrWhiteSpace(responseJson) && responseJson.Contains("\"success\":true");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SupabaseCloudCleanupService] Failed to delete server data: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Scans Supabase database for all records belonging to the given Steam ID and purges
        /// any data for servers that no longer exist in the user's active client server list.
        /// </summary>
        public static async Task<PurgeResult> PurgeOrphanedCloudDataAsync(IEnumerable<ServerProfile> activeServers, string steamId)
        {
            var result = new PurgeResult();

            if (string.IsNullOrWhiteSpace(steamId))
            {
                result.ErrorMessage = "Steam ID is missing or invalid.";
                return result;
            }

            try
            {
                var activeKeys = activeServers
                    .Where(s => !string.IsNullOrWhiteSpace(s.Host) && s.Port > 0)
                    .Select(s => $"{s.Host}-{s.Port}")
                    .Distinct()
                    .ToList();

                var payload = new
                {
                    active_server_keys = activeKeys,
                    steam_id = steamId,
                    action = "purge-orphaned"
                };

                var responseJson = await SupabaseAuthManager.CallEdgeFunctionAsync("overlay/purge-orphaned", HttpMethod.Post, payload);
                if (string.IsNullOrWhiteSpace(responseJson))
                {
                    result.ErrorMessage = "Empty response from Edge Function.";
                    return result;
                }

                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("error", out var errEl) && !string.IsNullOrWhiteSpace(errEl.GetString()))
                {
                    result.ErrorMessage = errEl.GetString();
                    return result;
                }

                // The cloud answers {"status":"success","removed_servers":N}. This used
                // to read the old Supabase edge function's {"success":true,"purged":{...}},
                // so after the move to the Laravel API every purge reported
                // "Unexpected response structure" — while having worked.
                if (root.TryGetProperty("status", out var statusEl)
                    && string.Equals(statusEl.GetString(), "success", StringComparison.OrdinalIgnoreCase))
                {
                    result.Success = true;
                    if (root.TryGetProperty("removed_servers", out var rs) && rs.TryGetInt32(out var count))
                        result.RemovedServers = count;
                }
                else
                {
                    result.ErrorMessage = "Unexpected response structure.";
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
            }

            return result;
        }
    }
}
