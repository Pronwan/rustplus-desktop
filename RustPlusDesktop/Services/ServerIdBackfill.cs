using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RustPlusApi;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services;

/// <summary>
/// Fills <see cref="ServerProfile.ServerId"/> for servers paired before the app kept it, so they can
/// use the companion socket instead of the direct connection Facepunch is removing.
///
/// Two of Facepunch's companion endpoints know the id, both read with the companion login token:
/// <list type="number">
/// <item><c>/api/history/read</c> — recent notifications with their full push payload, including
/// <c>ip</c>, <c>port</c> and <c>id</c>. An exact address match, so it is trusted as is. It only
/// reaches back a handful of entries.</item>
/// <item><c>/api/syncData</c> — the server list app.playrust.com saved for this account. It has
/// names but no address, so a name match is only taken once the companion socket accepts our player
/// token for that id. It is the web app's own state: read here, never written.</item>
/// </list>
/// Servers neither knows keep connecting directly until a push from them (or a re-pair) brings the id.
/// The push payloads carry player tokens, so nothing read here is ever logged.
/// </summary>
public static class ServerIdBackfill
{
    private const string CompanionBase = "https://companion-rust.facepunch.com";
    private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>A server id and what a source says about the server it belongs to.</summary>
    internal sealed record Candidate(Guid ServerId, string? Name, string? Ip, int Port, string? SteamId);

    /// <summary>
    /// Looks up ids for every profile without one and assigns those it can prove. Returns how many
    /// profiles got an id; the caller saves. Never throws for network or format problems.
    /// </summary>
    public static async Task<int> RunAsync(IReadOnlyList<ServerProfile> profiles, Action<string> log, CancellationToken ct = default)
    {
        var missing = profiles.Where(p => string.IsNullOrWhiteSpace(p.ServerId)).ToList();
        if (missing.Count == 0) return 0;

        var authToken = RustCompanionAuthService.ReadCurrentToken();
        if (string.IsNullOrWhiteSpace(authToken)) return 0;

        int filled = 0;

        var history = ParseHistory(await PostAsync("/api/history/read", authToken, log, ct).ConfigureAwait(false));
        foreach (var profile in missing.ToList())
        {
            if (MatchByAddress(profile, history) is not { } id) continue;
            profile.ServerId = id.ToString("D");
            missing.Remove(profile);
            filled++;
            log($"[server-id] '{profile.Name}' ← notification history.");
        }

        if (missing.Count == 0) return filled;

        var synced = ParseSyncData(await PostAsync("/api/syncData", authToken, log, ct).ConfigureAwait(false));
        foreach (var profile in missing)
        {
            if (MatchByName(profile, synced) is not { } id) continue;
            if (!await AcceptsPlayerAsync(profile, id, authToken, ct).ConfigureAwait(false))
            {
                log($"[server-id] '{profile.Name}' matched by name, but the companion socket did not accept it — skipped.");
                continue;
            }
            profile.ServerId = id.ToString("D");
            filled++;
            log($"[server-id] '{profile.Name}' ← web app server list (verified).");
        }

        return filled;
    }

    /// <summary>The one id whose notifications came from this profile's address and player, if exactly one did.</summary>
    internal static Guid? MatchByAddress(ServerProfile profile, IEnumerable<Candidate> candidates) =>
        Single(candidates.Where(c =>
            c.Port == profile.Port &&
            string.Equals(c.Ip?.Trim(), profile.Host?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            SamePlayer(c, profile)));

    /// <summary>The one id saved under this profile's exact server name and player, if exactly one is.</summary>
    internal static Guid? MatchByName(ServerProfile profile, IEnumerable<Candidate> candidates) =>
        string.IsNullOrWhiteSpace(profile.Name)
            ? null
            : Single(candidates.Where(c =>
                string.Equals(c.Name?.Trim(), profile.Name.Trim(), StringComparison.OrdinalIgnoreCase) &&
                SamePlayer(c, profile)));

    // A source without a player id doesn't rule a match out; a different one does.
    private static bool SamePlayer(Candidate c, ServerProfile profile) =>
        string.IsNullOrEmpty(c.SteamId) || string.IsNullOrEmpty(profile.SteamId64) || c.SteamId == profile.SteamId64;

    // Two different ids for one server means the source is ambiguous; take neither.
    private static Guid? Single(IEnumerable<Candidate> matches)
    {
        var ids = matches.Select(c => c.ServerId).Distinct().Take(2).ToList();
        return ids.Count == 1 ? ids[0] : null;
    }

    /// <summary>Reads <c>/api/history/read</c>: an array of notifications whose <c>data</c> is the push payload as a JSON string.</summary>
    internal static List<Candidate> ParseHistory(string? json)
    {
        var result = new List<Candidate>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("data", out var dataProp) || dataProp.ValueKind != JsonValueKind.String) continue;
                try
                {
                    using var data = JsonDocument.Parse(dataProp.GetString()!);
                    var d = data.RootElement;
                    if (!Guid.TryParse(Str(d, "id"), out var id) || id == Guid.Empty) continue;
                    if (!int.TryParse(Str(d, "port"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)) continue;
                    result.Add(new Candidate(id, Str(d, "name"), Str(d, "ip"), port, Str(d, "playerId")));
                }
                catch (JsonException) { /* one malformed payload doesn't spoil the rest */ }
            }
        }
        catch (JsonException) { }
        return result;
    }

    /// <summary>
    /// Reads <c>/api/syncData</c>: <c>{ payload: "&lt;json&gt;", version }</c>, the payload holding
    /// <c>servers[]</c> with <c>id</c>, <c>name</c> and <c>playerId</c> as a JavaScript Long (<c>low</c>/<c>high</c>).
    /// </summary>
    internal static List<Candidate> ParseSyncData(string? json)
    {
        var result = new List<Candidate>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var outer = JsonDocument.Parse(json);
            if (!outer.RootElement.TryGetProperty("payload", out var payloadProp) || payloadProp.ValueKind != JsonValueKind.String) return result;
            using var payload = JsonDocument.Parse(payloadProp.GetString()!);
            if (!payload.RootElement.TryGetProperty("servers", out var servers) || servers.ValueKind != JsonValueKind.Array) return result;
            foreach (var s in servers.EnumerateArray())
            {
                if (!Guid.TryParse(Str(s, "id"), out var id) || id == Guid.Empty) continue;
                result.Add(new Candidate(id, Str(s, "name"), null, 0, SteamIdOf(s)));
            }
        }
        catch (JsonException) { }
        return result;
    }

    // The web app stores 64-bit ids as { low, high } 32-bit halves, each possibly negative.
    private static string? SteamIdOf(JsonElement server)
    {
        if (!server.TryGetProperty("playerId", out var pid)) return null;
        if (pid.ValueKind == JsonValueKind.String) return pid.GetString();
        if (pid.ValueKind != JsonValueKind.Object ||
            !pid.TryGetProperty("low", out var lo) || !lo.TryGetInt64(out var low) ||
            !pid.TryGetProperty("high", out var hi) || !hi.TryGetInt64(out var high))
            return null;
        var value = ((ulong)unchecked((uint)high) << 32) | unchecked((uint)low);
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            }
            : null;

    private static async Task<string?> PostAsync(string path, string authToken, Action<string> log, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(new { AuthToken = authToken }), Encoding.UTF8, "application/json");
            using var response = await s_http.PostAsync(CompanionBase + path, content, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                log($"[server-id] {path}: HTTP {(int)response.StatusCode}.");
                return null;
            }
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log($"[server-id] {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Whether the companion socket for <paramref name="serverId"/> answers getInfo for this profile's player.</summary>
    private static async Task<bool> AcceptsPlayerAsync(ServerProfile profile, Guid serverId, string authToken, CancellationToken ct)
    {
        if (!ulong.TryParse(profile.SteamId64, NumberStyles.Integer, CultureInfo.InvariantCulture, out var steamId) ||
            !int.TryParse(profile.PlayerToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var playerToken))
            return false;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await using var api = new RustPlus(new RustPlusConnection(profile.Host, profile.Port, steamId, playerToken)
            {
                ServerId = serverId,
                AuthToken = authToken,
            });
            await api.ConnectAsync(cts.Token).ConfigureAwait(false);
            var info = await api.GetInfoAsync(cts.Token).ConfigureAwait(false);
            return info.IsSuccess;
        }
        catch
        {
            return false;
        }
    }
}
