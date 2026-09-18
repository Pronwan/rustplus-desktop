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
            string? ServerKey,
            string? Name,
            bool Enrolled,
            string? Mode,
            string? State,
            string Owner,
            DateTime? LastConnectedAt,
            string? LastError,
            bool NeedsRepair);

        /// <summary>The plan's ceiling, so the UI can be honest about it.</summary>
        public sealed record CloudPlan(
            bool Access,
            int LiveUsed,
            int LiveLimit,
            int? EnrolledLimit,
            bool AutoEnroll);

        public sealed record CloudOverview(IReadOnlyList<CloudServer> Servers, CloudPlan Plan);

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

                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in data.EnumerateArray())
                        servers.Add(ReadServer(item));
                }

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
        public static Task<bool> EnableAsync(string userServerId)
            => PostAsync($"me/cloud-sessions/{userServerId}/enable");

        /// <summary>Stop covering a server.</summary>
        public static Task<bool> DisableAsync(string userServerId)
            => PostAsync($"me/cloud-sessions/{userServerId}/disable");

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
            return new CloudServer(
                Str(item, "user_server_id") ?? string.Empty,
                Str(item, "server_key"),
                Str(item, "name") ?? Str(item, "server_name"),
                Bool(item, "enrolled"),
                Str(item, "mode"),
                Str(item, "state"),
                Str(item, "owner") ?? "none",
                Date(item, "last_connected_at"),
                Str(item, "last_error"),
                Bool(item, "needs_repair"));
        }

        private static CloudPlan ReadPlan(JsonElement meta)
        {
            int? enrolledLimit = null;

            if (meta.TryGetProperty("enrolled_limit", out var limit) && limit.ValueKind == JsonValueKind.Number)
                enrolledLimit = limit.GetInt32();

            return new CloudPlan(
                Bool(meta, "access"),
                Int(meta, "live_used"),
                Int(meta, "live_limit"),
                // Null means unlimited, which is the normal case: cover costs nothing
                // because a dormant server holds no connection and still sends alarms.
                enrolledLimit,
                Bool(meta, "auto_enroll"));
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
