using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.Cloud
{
    /// <summary>
    /// Platform-agnostic smart-home linking (Google Home) against the Cloud API.
    ///
    /// Mirrors <see cref="CloudAlexaAdapter"/> but targets the generic
    /// <c>me/smart-home</c> active server rather than the Alexa setting, so Google
    /// Home is independent of Alexa. Server pairing is shared — it reuses
    /// <see cref="CloudAlexaAdapter.PairServerAsync"/>, which just registers the
    /// per-user server credentials the worker needs.
    /// </summary>
    public static class CloudSmartHomeAdapter
    {
        /// <summary>The client-side server key ("{host}-{port}") currently linked, if any.</summary>
        public static async Task<string?> GetActiveServerKeyAsync()
        {
            var body = await CloudApiClient.CallApiAsync("me/smart-home", HttpMethod.Get);

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

            foreach (var (serverId, host, port) in await FetchPairedServersAsync())
            {
                if (serverId == activeServerId)
                    return $"{host}-{port}";
            }

            return null;
        }

        /// <summary>
        /// Pair the server (idempotent) and point the smart-home active server at it.
        /// Returns false when the pairing response carried no server id to link.
        /// </summary>
        public static async Task<bool> LinkServerAsync(string steamId, string host, int port, string? name, string playerToken)
        {
            var serverId = await CloudAlexaAdapter.PairServerAsync(steamId, host, port, name, playerToken);
            if (serverId == null) return false;

            await CloudApiClient.CallApiAsync("me/smart-home", HttpMethod.Put, payload: new
            {
                active_server_id = serverId,
            });

            return true;
        }

        public static Task RevokeAsync() =>
            CloudApiClient.CallApiAsync("me/smart-home", HttpMethod.Delete);

        private static async Task<List<(string ServerId, string Host, int Port)>> FetchPairedServersAsync()
        {
            var servers = new List<(string, string, int)>();

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
                    servers.Add((id, host, port));
            }

            return servers;
        }
    }
}
