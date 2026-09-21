using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.Cloud
{
    /// <summary>
    /// Cloud 24/7: which servers the cloud watches while this app is closed.
    ///
    /// Talks to the same <c>me/cloud-sessions</c> contract the web dashboard uses.
    /// That is deliberate — two implementations of the same plan arithmetic would
    /// drift, and a user comparing the app to the website would be told two
    /// different things about what is covered.
    ///
    /// The handoff is two calls and a timeout. Takeover says "this app is driving
    /// the server now", release says it has stopped. Neither is required for
    /// correctness: the platform holds a short lease that expires on its own, which
    /// is what covers the case this app cannot report at all — being killed.
    /// </summary>
    public static class CloudSessionsApi
    {
        /// <summary>
        /// Identifies this run of the app to the platform.
        ///
        /// Regenerated per process on purpose: it exists to answer "is the client
        /// that took this lease still the one running", and a value that survived a
        /// restart would let a dead process look alive.
        /// </summary>
        public static readonly string DesktopSessionId = "desktop-" + Guid.NewGuid().ToString("N")[..12];

        /// <summary>One paired server and what the cloud is doing about it.</summary>
        public sealed record CloudServer(
            string UserServerId,
            string ServerId,
            string? ServerKey,
            string? Name,
            bool Enrolled,
            string? Mode,
            string? State,
            string Owner,
            DateTime? LastConnectedAt,
            string? LastError,
            bool NeedsRepair,
            bool IsPreferred,
            bool HasCloudOverrides);

        /// <summary>The plan's ceiling, so the UI can be honest about it.</summary>
        public sealed record CloudPlan(
            bool Access,
            int LiveUsed,
            int LiveLimit,
            int? EnrolledLimit,
            bool AutoEnroll,
            bool GlobalConsent = false,
            string? PreferredServerId = null,
            IReadOnlyList<string>? ActiveServerIds = null,
            DateTime? ConsentedAt = null);

        public sealed record CloudOverview(IReadOnlyList<CloudServer> Servers, CloudPlan Plan);

        public static bool GlobalConsentEnabled { get; private set; }
        public static string? PreferredServerId { get; private set; }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> CoveredServerKeys = new(StringComparer.OrdinalIgnoreCase);
        private static DateTime _lastOverviewUtc = DateTime.MinValue;
        private static readonly TimeSpan OverviewTtl = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Check whether a server key is currently consented and actively covered by Cloud 24/7.
        /// </summary>
        public static bool IsServerCovered(string? serverKey)
        {
            if (string.IsNullOrWhiteSpace(serverKey)) return false;
            if (!GlobalConsentEnabled) return false;
            return CoveredServerKeys.TryGetValue(serverKey, out var covered) && covered;
        }

        /// <summary>
        /// Asynchronously check whether a server is covered, refreshing the overview if stale or not yet loaded.
        /// </summary>
        public static async Task<bool> IsServerCoveredAsync(string? serverKey)
        {
            if (string.IsNullOrWhiteSpace(serverKey)) return false;

            if (DateTime.UtcNow - _lastOverviewUtc > OverviewTtl || CoveredServerKeys.IsEmpty)
            {
                await GetOverviewAsync().ConfigureAwait(false);
            }

            return IsServerCovered(serverKey);
        }

        /// <summary>
        /// Update the global Cloud 24/7 switch and the list of active chosen servers (up to the plan live limit).
        /// </summary>
        public static async Task<bool> UpdateGlobalSettingsAsync(bool enabled, IEnumerable<string>? activeServerIds = null)
        {
            if (!CloudBackend.UsePlatform || !CloudAuthManager.IsAuthenticated) return false;

            try
            {
                var payload = new
                {
                    enabled,
                    active_server_ids = activeServerIds ?? Array.Empty<string>()
                };

                await CloudApiClient.CallApiAsync("me/cloud-sessions/settings", HttpMethod.Post, payload: payload);
                GlobalConsentEnabled = enabled;
                await GetOverviewAsync().ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                Services.Auth.SupabaseAuthManager.AppendLog($"[Cloud 24/7] Failed to update global settings: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Everything this account has paired, enrolled or not.
        ///
        /// Returns null when the platform cannot be reached, which the UI shows as
        /// "unknown" rather than "nothing covered" — claiming a server is uncovered
        /// because a request failed would be worse than admitting we do not know.
        /// </summary>
        public static async Task<CloudOverview?> GetOverviewAsync()
        {
            if (!CloudBackend.UsePlatform || !CloudAuthManager.IsAuthenticated) return null;

            try
            {
                var body = await CloudApiClient.CallApiAsync("me/cloud-sessions", HttpMethod.Get);
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                var servers = new List<CloudServer>();
                CoveredServerKeys.Clear();

                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in data.EnumerateArray())
                    {
                        var s = ReadServer(item);
                        servers.Add(s);

                        if (!string.IsNullOrWhiteSpace(s.ServerKey) && (s.Enrolled || s.IsPreferred))
                        {
                            CoveredServerKeys[s.ServerKey] = true;
                        }
                    }
                }

                _lastOverviewUtc = DateTime.UtcNow;

                var plan = root.TryGetProperty("meta", out var meta)
                    ? ReadPlan(meta)
                    : new CloudPlan(false, 0, 0, null, false);

                return new CloudOverview(servers, plan);
            }
            catch (Exception ex)
            {
                Services.Auth.SupabaseAuthManager.AppendLog($"[Cloud 24/7] Could not read cover: {ex.Message}");
                return null;
            }
        }

        /// <summary>Start covering a server while the app is closed.</summary>
        public static async Task<bool> EnableAsync(string userServerId, string? serverKey = null)
        {
            var ok = await PostAsync($"me/cloud-sessions/{userServerId}/enable");
            if (ok && !string.IsNullOrWhiteSpace(serverKey))
            {
                CoveredServerKeys[serverKey] = true;
            }
            return ok;
        }

        /// <summary>Stop covering a server.</summary>
        public static async Task<bool> DisableAsync(string userServerId, string? serverKey = null)
        {
            var ok = await PostAsync($"me/cloud-sessions/{userServerId}/disable");
            if (ok && !string.IsNullOrWhiteSpace(serverKey))
            {
                CoveredServerKeys.TryRemove(serverKey, out _);
            }
            return ok;
        }

        /// <summary>
        /// Give this server the live cloud connection.
        ///
        /// The only decision there is. Raid alarms already reach every paired
        /// server whether or not anything is enrolled — the push listener is one
        /// socket per account, not per server — so what this picks is which server
        /// gets the live socket, and with it the map, chat commands and device
        /// control. It enrols as part of the same call, because asking the user to
        /// switch a server on and then choose it was two steps for one intent.
        /// </summary>
        public static async Task<bool> SetPreferredAsync(string userServerId, string? serverKey = null)
        {
            var ok = await PostAsync($"me/cloud-sessions/{userServerId}/preferred");
            if (ok && !string.IsNullOrWhiteSpace(serverKey))
            {
                CoveredServerKeys[serverKey] = true;
            }
            return ok;
        }

        /// <summary>
        /// Set which servers keep their live connection when the budget is full.
        ///
        /// Higher wins. This is the only lever the user has over the automatic
        /// promotion, so the UI has to expose it rather than leaving the choice
        /// looking arbitrary.
        /// </summary>
        public static Task<bool> SetPriorityAsync(string userServerId, int priority)
            => PostAsync($"me/cloud-sessions/{userServerId}", priority: priority);

        /// <summary>
        /// Tell the cloud this app is driving a server, so it stands down.
        ///
        /// Fire-and-forget: a failure here costs a duplicate connection for at most
        /// one heartbeat, which is not worth blocking a connect over.
        /// Only sends takeover if the server has been consented and enrolled in Cloud 24/7.
        /// </summary>
        public static async Task TakeoverAsync(string serverKey)
        {
            if (string.IsNullOrWhiteSpace(serverKey)) return;

            await PostAsync("client/cloud/takeover", serverKey: serverKey);
        }

        /// <summary>
        /// Hand a server back to the cloud on a clean exit.
        ///
        /// Skipping this is survivable — the lease expires by itself — but calling it
        /// turns a ninety-second gap into a couple of seconds.
        /// </summary>
        public static async Task ReleaseAsync(string serverKey)
        {
            if (string.IsNullOrWhiteSpace(serverKey)) return;

            await PostAsync("client/cloud/release", serverKey: serverKey);
        }

        private static async Task<bool> PostAsync(string route, string? serverKey = null, int? priority = null)
        {
            if (!CloudBackend.UsePlatform || !CloudAuthManager.IsAuthenticated) return false;

            try
            {
                object? payload = null;

                if (serverKey != null)
                    payload = new { server_key = serverKey, desktop_session_id = DesktopSessionId };
                else if (priority != null)
                    payload = new { priority };

                var method = priority != null ? HttpMethod.Patch : HttpMethod.Post;
                await CloudApiClient.CallApiAsync(route, method, payload: payload);

                return true;
            }
            catch (Exception ex)
            {
                Services.Auth.SupabaseAuthManager.AppendLog($"[Cloud 24/7] {route} failed: {ex.Message}");
                return false;
            }
        }

        private static CloudServer ReadServer(JsonElement item)
        {
            var userServerId = Str(item, "user_server_id") ?? string.Empty;
            var serverId = Str(item, "server_id") ?? userServerId;
            return new CloudServer(
                userServerId,
                serverId,
                Str(item, "server_key"),
                Str(item, "name") ?? Str(item, "server_name"),
                Bool(item, "enrolled"),
                Str(item, "mode"),
                Str(item, "state"),
                Str(item, "owner") ?? "none",
                Date(item, "last_connected_at"),
                Str(item, "last_error"),
                Bool(item, "needs_repair"),
                Bool(item, "is_preferred"),
                Bool(item, "has_cloud_overrides"));
        }

        private static CloudPlan ReadPlan(JsonElement meta)
        {
            int? enrolledLimit = null;

            if (meta.TryGetProperty("enrolled_limit", out var limit) && limit.ValueKind == JsonValueKind.Number)
                enrolledLimit = limit.GetInt32();

            var activeIds = new List<string>();
            if (meta.TryGetProperty("active_server_ids", out var idsElem) && idsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var id in idsElem.EnumerateArray())
                {
                    if (id.GetString() is { Length: > 0 } s) activeIds.Add(s);
                }
            }

            var globalConsent = Bool(meta, "global_consent");
            GlobalConsentEnabled = globalConsent;

            var preferredServerId = Str(meta, "preferred_server_id");
            PreferredServerId = preferredServerId;

            return new CloudPlan(
                Bool(meta, "access"),
                Int(meta, "live_used"),
                Int(meta, "live_limit"),
                enrolledLimit,
                Bool(meta, "auto_enroll"),
                globalConsent,
                preferredServerId,
                activeIds,
                Date(meta, "consented_at"));
        }

        private static string? Str(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static bool Bool(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        private static int Int(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

        private static DateTime? Date(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                && DateTime.TryParse(v.GetString(), out var parsed) ? parsed : null;
    }
}
