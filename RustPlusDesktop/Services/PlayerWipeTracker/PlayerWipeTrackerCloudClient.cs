using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RustPlusDesk.Services.Auth;

namespace RustPlusDesk.Services.PlayerWipeTracker;

public sealed class PlayerWipeTrackerCloudClient
{
    private static readonly HttpClient Http = new(new TrafficTrackingHttpMessageHandler("Player Wipe Cloud")) { Timeout = TimeSpan.FromSeconds(15) };
    private const string BaseUrl = "https://rustplusdesktop.cloud/api/v1";
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task<JsonDocument?> GetBootstrapAsync(CancellationToken cancellationToken = default)
    {
        return await SendJsonAsync(HttpMethod.Get, "client/bootstrap", null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> PutDayAsync(object payload, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Put, "player-wipe-tracker/days", payload, cancellationToken).ConfigureAwait(false);
        return (int)response.StatusCode;
    }

    /// <summary>
    /// Sends a batch of new observations and returns the newest timestamp the server has stored.
    ///
    /// The acknowledged timestamp is what the caller records as its cursor. Taking it from the
    /// response rather than from the batch it sent means a partially merged batch — anything the
    /// server chose to reject or deduplicate — leaves the cursor where the server actually is.
    /// </summary>
    public async Task<(int Status, DateTime? LastObservedUtc)> AppendDayAsync(
        CloudDayAppendRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "player-wipe-tracker/days/append", request, cancellationToken).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        if (status is < 200 or >= 300)
            return (status, null);

        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("last_observed_at", out var last)
                && last.ValueKind == JsonValueKind.String
                && DateTime.TryParse(last.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var parsed))
            {
                return (status, parsed.ToUniversalTime());
            }
        }
        catch
        {
            // A stored batch with an unreadable acknowledgement is still stored. Leaving the
            // cursor put costs one repeated batch, and the server merges by timestamp.
        }

        return (status, null);
    }

    public async Task<JsonDocument?> GetWipesAsync(CancellationToken cancellationToken = default)
        => await SendJsonAsync(HttpMethod.Get, "player-wipe-tracker/wipes", null, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<CloudArchiveSummary>> GetArchiveSummariesAsync(CancellationToken cancellationToken = default)
    {
        using var document = await GetWipesAsync(cancellationToken).ConfigureAwait(false);
        if (document is null)
            return Array.Empty<CloudArchiveSummary>();

        var data = UnwrapData(document);
        if (data.ValueKind != JsonValueKind.Array)
            return Array.Empty<CloudArchiveSummary>();

        return data.EnumerateArray()
            .Select(ParseArchive)
            .Where(archive => archive is not null)
            .Cast<CloudArchiveSummary>()
            .ToArray();
    }

    public async Task<CloudArchiveSummary?> GetArchiveDetailsAsync(string archiveId, CancellationToken cancellationToken = default)
    {
        using var document = await SendJsonAsync(
            HttpMethod.Get,
            $"player-wipe-tracker/wipes/{Uri.EscapeDataString(archiveId)}",
            null,
            cancellationToken).ConfigureAwait(false);
        if (document is null)
            return null;

        var data = UnwrapData(document);
        return data.ValueKind == JsonValueKind.Object ? ParseArchive(data) : null;
    }

    public async Task<IReadOnlyList<CloudRestoreDay>> GetRestoreDaysAsync(
        string archiveId,
        string steamId,
        CancellationToken cancellationToken = default)
    {
        using var document = await GetPlayerDaysAsync(archiveId, steamId, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (document is null)
            return Array.Empty<CloudRestoreDay>();

        var data = UnwrapData(document);
        if (data.ValueKind != JsonValueKind.Array)
            return Array.Empty<CloudRestoreDay>();

        var result = new List<CloudRestoreDay>();
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("payload", out var payloadElement) || payloadElement.ValueKind != JsonValueKind.Object)
                continue;

            var payload = payloadElement.Deserialize<CloudTrackerDayPayload>(_json);
            var day = String(item, "day");
            var playerSteamId = String(item, "player_steam_id");
            if (payload is null || string.IsNullOrWhiteSpace(day) || string.IsNullOrWhiteSpace(playerSteamId))
                continue;

            result.Add(new CloudRestoreDay(playerSteamId, String(item, "player_name"), day, payload));
        }

        return result;
    }

    public async Task<JsonDocument?> GetPlayerDaysAsync(string archiveId, string steamId, DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default)
    {
        var query = from is null && to is null
            ? string.Empty
            : $"?{(from is null ? string.Empty : $"from={from.Value:yyyy-MM-dd}")}{(from is not null && to is not null ? "&" : string.Empty)}{(to is null ? string.Empty : $"to={to.Value:yyyy-MM-dd}")}";
        return await SendJsonAsync(HttpMethod.Get, $"player-wipe-tracker/wipes/{Uri.EscapeDataString(archiveId)}/players/{Uri.EscapeDataString(steamId)}{query}", null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> DeleteArchiveAsync(string archiveId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"player-wipe-tracker/wipes/{Uri.EscapeDataString(archiveId)}", null, cancellationToken).ConfigureAwait(false);
        return (int)response.StatusCode;
    }

    public async Task<int> DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, "player-wipe-tracker", null, cancellationToken).ConfigureAwait(false);
        return (int)response.StatusCode;
    }

    private async Task<JsonDocument?> SendJsonAsync(HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, payload, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, $"{BaseUrl.TrimEnd('/')}/{path}");
        var token = RustPlusDesk.Services.Cloud.CloudAuthManager.CurrentToken;
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Client-Version", Helpers.VersionHelper.GetClientVersion());
        if (payload is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(payload, _json), Encoding.UTF8, "application/json");
        return await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    private static JsonElement UnwrapData(JsonDocument document)
        => document.RootElement.TryGetProperty("data", out var data) ? data : document.RootElement;


    public async Task<TrackerWipeMap?> DownloadWipeMapAsync(string serverKey, string wipeKey, CancellationToken cancellationToken = default)
    {
        // Reads from the decoupled /server-wipe-maps API (the old
        // /player-wipe-tracker/maps routes were retired). Uploading lives in
        // ServerWipeMapCloudClient; this stays here only for the archive restore.
        using var document = await SendJsonAsync(
            HttpMethod.Get,
            $"server-wipe-maps/{Uri.EscapeDataString(serverKey)}/{Uri.EscapeDataString(wipeKey)}",
            null,
            cancellationToken).ConfigureAwait(false);
        if (document is null)
            return null;

        var data = UnwrapData(document);
        if (data.ValueKind != JsonValueKind.Object)
            return null;

        var worldSize = Integer(data, "world_size");
        var rx = Double(data, "world_rect_x");
        var ry = Double(data, "world_rect_y");
        var rw = Double(data, "world_rect_width");
        var rh = Double(data, "world_rect_height");
        var oceanMargin = Double(data, "ocean_margin");
        var monuments = ParseMonuments(data, "monuments");
        monuments.AddRange(ParseMonuments(data, "extra_monuments"));

        byte[]? pngBytes = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl.TrimEnd('/')}/server-wipe-maps/{Uri.EscapeDataString(serverKey)}/{Uri.EscapeDataString(wipeKey)}/image");
            var token = RustPlusDesk.Services.Cloud.CloudAuthManager.CurrentToken;
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Client-Version", Helpers.VersionHelper.GetClientVersion());

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                pngBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // Ignore map image download errors
        }

        return new TrackerWipeMap(
            pngBytes ?? Array.Empty<byte>(),
            worldSize,
            rx,
            ry,
            rw,
            rh,
            oceanMargin,
            monuments);
    }

    private static List<TrackerMonument> ParseMonuments(JsonElement data, string property)
    {
        var result = new List<TrackerMonument>();
        if (!data.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var name = String(item, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            result.Add(new TrackerMonument(name, Double(item, "x"), Double(item, "y"), String(item, "size")));
        }

        return result;
    }

    private static CloudArchiveSummary? ParseArchive(JsonElement item)
    {
        var id = String(item, "id");
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var serverKey = string.Empty;
        var serverName = "Unknown server";
        if (item.TryGetProperty("server", out var server) && server.ValueKind == JsonValueKind.Object)
        {
            serverKey = String(server, "server_key") ?? string.Empty;
            serverName = String(server, "name") ?? serverKey;
        }

        var players = new List<CloudArchivePlayer>();
        if (item.TryGetProperty("players", out var playerList) && playerList.ValueKind == JsonValueKind.Array)
        {
            foreach (var player in playerList.EnumerateArray())
            {
                var steamId = String(player, "player_steam_id");
                if (!string.IsNullOrWhiteSpace(steamId))
                {
                    players.Add(new CloudArchivePlayer(
                        steamId,
                        Integer(player, "day_count"),
                        String(player, "player_name"),
                        Boolean(player, "is_linked"),
                        String(player, "user_id"),
                        String(player, "display_name"),
                        String(player, "avatar_url")));
                }
            }
        }

        var hasMap = Boolean(item, "has_map");
        var mapUrl = String(item, "map_url");

        return new CloudArchiveSummary(
            id,
            serverKey,
            serverName,
            String(item, "wipe_key") ?? string.Empty,
            Date(item, "wipe_started_at"),
            Date(item, "first_observed_at"),
            Date(item, "last_observed_at"),
            NullableInteger(item, "player_count"),
            Long(item, "stored_bytes"),
            players,
            hasMap,
            mapUrl);
    }

    private static string? String(JsonElement item, string property)
        => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Boolean(JsonElement item, string property)
        => item.TryGetProperty(property, out var value) && (value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.False ? false : false));

    private static double Double(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var result))
            return 0;
        return result;
    }

    private static DateTime? Date(JsonElement item, string property)
        => DateTimeOffset.TryParse(String(item, property), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value.UtcDateTime
            : null;

    private static int Integer(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            return 0;
        return result;
    }

    private static int? NullableInteger(JsonElement item, string property)
        => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : null;

    private static long? Long(JsonElement item, string property)
        => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result)
            ? result
            : null;
}
