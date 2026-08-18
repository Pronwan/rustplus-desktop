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

public sealed class LaravelPlayerWipeTrackerClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
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
                    players.Add(new CloudArchivePlayer(steamId, Integer(player, "day_count")));
            }
        }

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
            players);
    }

    private static string? String(JsonElement item, string property)
        => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

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
