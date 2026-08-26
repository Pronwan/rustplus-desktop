using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RustPlusDesk.Services.Auth;

namespace RustPlusDesk.Services.Cloud
{
    /// <summary>
    /// Alexa + FCM linking against the Cloud API.
    ///
    /// Two shape differences make this more than a route swap. Supabase keyed the
    /// active Alexa server by the client-side <c>"{host}-{port}"</c> string, whereas
    /// the platform references a <c>rust_servers</c> UUID — so the pairing list is used to
    /// translate between them. And the API never returns the stored FCM config (it is
    /// encrypted at rest), so the Alexa PIN cannot be read-modify-written remotely;
    /// the local credentials file is the source of truth and is re-uploaded whole.
    /// </summary>
    public static class CloudAlexaAdapter
    {
        private sealed record PairedServer(string ServerId, string Host, int Port);

        /// <summary>The client-side server key ("{host}-{port}") currently linked to Alexa, if any.</summary>
        public static async Task<string?> GetActiveServerKeyAsync()
        {
            var body = await CloudApiClient.CallApiAsync("me/alexa", HttpMethod.Get);

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("active_server_id", out var activeEl) ||
                activeEl.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var activeServerId = activeEl.GetString();
            if (string.IsNullOrEmpty(activeServerId)) return null;

            foreach (var server in await FetchPairedServersAsync())
            {
                if (server.ServerId == activeServerId)
                    return $"{server.Host}-{server.Port}";
            }

            return null;
        }

        /// <summary>
        /// Register (idempotently) the per-user pairing for a server so the platform
        /// holds its encrypted player token — this is what makes the server appear in
        /// the web dashboard and its smart devices controllable from the cloud.
        /// Returns the server id, or null when the response carried none.
        ///
        /// The <c>server_key</c> ("{host}-{port}") is sent so the pairing resolves to
        /// the same rust_servers row the device/overlay sync files under; sending only
        /// ip+port can create a second row and split the token from the devices.
        /// </summary>
        public static async Task<string?> PairServerAsync(string steamId, string host, int port, string? name, string playerToken)
        {
            var pairBody = await CloudApiClient.CallApiAsync("me/servers", HttpMethod.Post, payload: new
            {
                server_key = $"{host}-{port}",
                server_ip = host,
                server_port = port,
                name,
                player_token = playerToken,
                steam_id = steamId,
            });

            return ExtractServerId(pairBody);
        }

        /// <summary>
        /// Pair the server (idempotent) and point Alexa at it. Returns false when the
        /// pairing response carried no server id to link.
        /// </summary>
        public static async Task<bool> LinkServerAsync(string steamId, string host, int port, string? name, string playerToken)
        {
            var serverId = await PairServerAsync(steamId, host, port, name, playerToken);
            if (serverId == null) return false;

            await CloudApiClient.CallApiAsync("me/alexa", HttpMethod.Put, payload: new
            {
                steam_id = steamId,
                active_server_id = serverId,
            });

            return true;
        }

        public static Task RevokeAsync() =>
            CloudApiClient.CallApiAsync("me/alexa", HttpMethod.Delete);

        /// <summary>
        /// Stamp a short-lived Alexa PIN into the FCM config and re-upload it. The
        /// stored config is never returned by the API, so the local file is amended
        /// and sent in full rather than patched remotely.
        /// </summary>
        public static async Task<bool> SetAlexaPinAsync(string steamId, string pin, DateTime expiresUtc)
        {
            if (!File.Exists(FcmSyncService.ConfigPath))
                return false;

            var config = JObject.Parse(await File.ReadAllTextAsync(FcmSyncService.ConfigPath));
            config["alexa_pin"] = pin;
            config["alexa_pin_expires"] = expiresUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");

            // The config is a Newtonsoft JObject, but CloudApiClient serialises with
            // System.Text.Json. That combination silently destroys the payload: JObject looks
            // like an IDictionary<string, JToken>, so every value is written as the empty array
            // its JToken reflects to. The upload succeeded with 200 OK while the server stored
            // {"alexa_pin":[], ...} - which is why a generated PIN was always rejected.
            // Re-parsing into a JsonDocument hands System.Text.Json something it writes verbatim.
            using var configJson = System.Text.Json.JsonDocument.Parse(
                config.ToString(Newtonsoft.Json.Formatting.None));

            await CloudApiClient.CallApiAsync("me/fcm", HttpMethod.Put, payload: new
            {
                steam_id = steamId,
                fcm_config = configJson.RootElement,
            });

            return true;
        }

        /// <summary>True when FCM credentials have been uploaded for this account.</summary>
        public static async Task<bool> IsFcmConfiguredAsync()
        {
            try
            {
                var body = await CloudApiClient.CallApiAsync("me/fcm", HttpMethod.Get);
                using var doc = JsonDocument.Parse(body);

                return doc.RootElement.TryGetProperty("data", out var data)
                       && data.TryGetProperty("configured", out var configured)
                       && configured.ValueKind == JsonValueKind.True;
            }
            catch
            {
                return false;
            }
        }

        private static string? ExtractServerId(string body)
        {
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
            if (!data.TryGetProperty("server", out var server) || server.ValueKind != JsonValueKind.Object) return null;

            return server.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }

        private static async Task<List<PairedServer>> FetchPairedServersAsync()
        {
            var servers = new List<PairedServer>();

            var body = await CloudApiClient.CallApiAsync("me/servers", HttpMethod.Get);
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return servers;

            foreach (var entry in data.EnumerateArray())
            {
                if (!entry.TryGetProperty("server", out var server) || server.ValueKind != JsonValueKind.Object)
                    continue;

                var id = server.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                    ? idEl.GetString()
                    : null;
                var host = server.TryGetProperty("server_ip", out var ipEl) && ipEl.ValueKind == JsonValueKind.String
                    ? ipEl.GetString()
                    : null;
                var port = server.TryGetProperty("server_port", out var portEl) && portEl.ValueKind == JsonValueKind.Number
                    ? portEl.GetInt32()
                    : 0;

                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(host) && port > 0)
                    servers.Add(new PairedServer(id, host, port));
            }

            return servers;
        }
    }
}
