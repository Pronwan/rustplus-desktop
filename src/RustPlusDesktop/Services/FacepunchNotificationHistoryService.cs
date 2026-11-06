using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services
{
    public sealed class FacepunchNotificationItem
    {
        public long NotificationId { get; set; }
        public DateTime Date { get; set; }
        public string? ServerId { get; set; }
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? Data { get; set; }
        public int Channel { get; set; }
    }

    /// <summary>
    /// Communicates with Facepunch's companion notification history endpoint (/api/history/read)
    /// to retrieve notifications and pairing requests stored on Facepunch servers.
    /// This matches the web companion app (app.playrust.com) behavior where notifications and pairings
    /// are reconciled even if FCM push messages were dropped or missed.
    /// </summary>
    public static class FacepunchNotificationHistoryService
    {
        private const string HistoryEndpoint = "https://companion-rust.facepunch.com/api/history/read";
        private const string NativeUserAgent = "okhttp/4.9.2";
        private static readonly HttpClient s_httpClient = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", NativeUserAgent);
            return client;
        }

        /// <summary>
        /// Reads all stored notifications from Facepunch for the current or specified AuthToken.
        /// </summary>
        public static async Task<List<FacepunchNotificationItem>> FetchHistoryAsync(string? authToken = null, CancellationToken ct = default)
        {
            authToken ??= RustCompanionAuthService.ReadCurrentToken();
            if (string.IsNullOrWhiteSpace(authToken))
                return new List<FacepunchNotificationItem>();

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, HistoryEndpoint);
                req.Headers.Accept.Clear();
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                req.Headers.TryAddWithoutValidation("User-Agent", NativeUserAgent);

                var jsonPayload = JsonSerializer.Serialize(new { AuthToken = authToken });
                req.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                using var res = await s_httpClient.SendAsync(req, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                    return new List<FacepunchNotificationItem>();

                var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return new List<FacepunchNotificationItem>();

                var list = new List<FacepunchNotificationItem>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    long notifId = el.TryGetProperty("notificationId", out var idProp) && idProp.TryGetInt64(out var nId) ? nId : 0;
                    DateTime date = el.TryGetProperty("date", out var dProp) && dProp.TryGetDateTime(out var dt) ? dt : DateTime.UtcNow;
                    string? sId = el.TryGetProperty("serverId", out var sProp) ? sProp.GetString() : null;
                    string? title = el.TryGetProperty("title", out var tProp) ? tProp.GetString() : null;
                    string? msgBody = el.TryGetProperty("body", out var bProp) ? bProp.GetString() : null;
                    string? data = el.TryGetProperty("data", out var dataProp) ? dataProp.GetString() : null;
                    int channel = el.TryGetProperty("channel", out var cProp) && cProp.TryGetInt32(out var ch) ? ch : 0;

                    list.Add(new FacepunchNotificationItem
                    {
                        NotificationId = notifId,
                        Date = date,
                        ServerId = sId,
                        Title = title,
                        Body = msgBody,
                        Data = data,
                        Channel = channel
                    });
                }
                return list;
            }
            catch
            {
                return new List<FacepunchNotificationItem>();
            }
        }

        /// <summary>
        /// Reads pairing requests from notification history and dispatches them to the pairing handler.
        /// Returns the number of pairings processed.
        /// </summary>
        public static async Task<int> SyncHistoryPairingsAsync(
            Func<PairingPayload, Task> onPairingDispatched,
            Func<PairingPayload, bool>? isAlreadyPaired = null,
            Action<string>? log = null,
            string? authToken = null,
            CancellationToken ct = default)
        {
            var history = await FetchHistoryAsync(authToken, ct).ConfigureAwait(false);
            if (history.Count == 0)
                return 0;

            int pairedCount = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Iterate newest to oldest so newest state takes precedence and duplicates are skipped
            foreach (var item in history)
            {
                if (string.IsNullOrWhiteSpace(item.Data))
                    continue;

                try
                {
                    using var dataDoc = JsonDocument.Parse(item.Data);
                    var root = dataDoc.RootElement;

                    string? type = root.TryGetProperty("type", out var tProp) ? tProp.GetString() : null;
                    bool isPairing = item.Channel == 1001 ||
                                     string.Equals(type, "server", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(type, "entity", StringComparison.OrdinalIgnoreCase) ||
                                     (item.Body?.Contains("pair", StringComparison.OrdinalIgnoreCase) ?? false);

                    if (!isPairing)
                        continue;

                    string? playerToken = root.TryGetProperty("playerToken", out var ptProp)
                        ? (ptProp.ValueKind == JsonValueKind.String ? ptProp.GetString() : ptProp.ToString())
                        : null;
                    if (string.IsNullOrWhiteSpace(playerToken))
                        continue;

                    string? ip = root.TryGetProperty("ip", out var ipProp) ? ipProp.GetString() : null;
                    int port = 0;
                    if (root.TryGetProperty("port", out var pProp))
                    {
                        if (pProp.ValueKind == JsonValueKind.Number)
                            port = pProp.GetInt32();
                        else if (pProp.ValueKind == JsonValueKind.String)
                            int.TryParse(pProp.GetString(), out port);
                    }

                    string? id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : item.ServerId;
                    if (string.IsNullOrWhiteSpace(ip) && string.IsNullOrWhiteSpace(id))
                        continue;

                    string host = !string.IsNullOrWhiteSpace(ip)
                        ? ip
                        : (!string.IsNullOrWhiteSpace(id) ? "companion-socket" : string.Empty);
                    if (port <= 0)
                        port = 28082;

                    string? serverName = root.TryGetProperty("name", out var nProp) ? nProp.GetString() : item.Title;
                    string? desc = root.TryGetProperty("desc", out var descProp) ? descProp.GetString() : null;
                    string? img = root.TryGetProperty("img", out var imgProp) ? imgProp.GetString() : null;
                    string? logo = root.TryGetProperty("logo", out var logoProp) ? logoProp.GetString() : null;
                    string? playerId = root.TryGetProperty("playerId", out var pidProp)
                        ? (pidProp.ValueKind == JsonValueKind.String ? pidProp.GetString() : pidProp.ToString())
                        : null;

                    uint? entityId = null;
                    if (root.TryGetProperty("entityId", out var eidProp))
                    {
                        if (eidProp.ValueKind == JsonValueKind.Number)
                            entityId = eidProp.GetUInt32();
                        else if (eidProp.ValueKind == JsonValueKind.String && uint.TryParse(eidProp.GetString(), out var eidVal))
                            entityId = eidVal;
                    }

                    string? entityName = root.TryGetProperty("entityName", out var enProp) ? enProp.GetString() : null;
                    string? entityType = root.TryGetProperty("entityType", out var etProp)
                        ? (etProp.ValueKind == JsonValueKind.String ? etProp.GetString() : etProp.ToString())
                        : type;

                    // Deduplicate within the history batch: only process newest entry for a given server/entity
                    string dedupKey = $"{id}|{host}:{port}|{entityId}";
                    if (!seen.Add(dedupKey))
                    {
                        continue;
                    }

                    var payload = new PairingPayload
                    {
                        Host = host,
                        Port = port,
                        ServerId = id,
                        ServerName = serverName,
                        ServerDescription = desc,
                        ImageUrl = !string.IsNullOrWhiteSpace(logo) ? logo : img,
                        SteamId64 = playerId ?? string.Empty,
                        PlayerToken = playerToken,
                        EntityId = entityId,
                        EntityName = entityName,
                        EntityType = entityType,
                        NotificationId = item.NotificationId > 0 ? item.NotificationId.ToString(CultureInfo.InvariantCulture) : null,
                        Timestamp = item.Date
                    };

                    if (isAlreadyPaired != null && isAlreadyPaired(payload))
                    {
                        log?.Invoke($"[history-sync] Skipping already paired server/device: '{payload.ServerName ?? payload.Host}'");
                        continue;
                    }

                    log?.Invoke($"[history-sync] Discovered pairing from history: '{payload.ServerName ?? payload.Host}' (ServerId: {payload.ServerId ?? "none"})");
                    await onPairingDispatched(payload).ConfigureAwait(false);
                    pairedCount++;
                }
                catch (Exception ex)
                {
                    log?.Invoke($"[history-sync:err] Failed to parse history entry {item.NotificationId}: {ex.Message}");
                }
            }

            return pairedCount;
        }
    }
}

