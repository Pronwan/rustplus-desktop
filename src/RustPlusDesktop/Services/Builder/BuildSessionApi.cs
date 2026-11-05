using Newtonsoft.Json.Linq;
using RustPlusDesk.Services.Cloud;
using System.Net.Http;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.Builder;

/// <summary>
/// The platform side of co-op Base Builder sessions: who hosts, who is invited, who may build.
///
/// Calls throw <see cref="CloudApiException"/> on a refusal; its status says which: 403 not
/// premium, 404 not yours or gone, 409 full, 410 expired or ended, 503 the feature is off.
/// </summary>
public static class BuildSessionApi
{
    /// <summary>The admin switch on the platform. Read fail-closed: no flag, no feature.</summary>
    public const string FeatureFlag = "coop_builder";

    /// <summary>Whether co-op building is switched on for this platform at all.</summary>
    public static bool IsAvailable =>
        CloudBackend.UsePlatform && CloudAuthManager.IsAuthenticated && CloudFeatureFlags.IsWithinWindow(FeatureFlag);

    public static async Task<BuildSessionsState> CurrentAsync() =>
        BuildSessionsState.Parse(await CloudApiClient.CallApiAsync("build-sessions/current", HttpMethod.Get));

    /// <summary>
    /// Starts a session, or returns the one this host already has open in the same builder. One open in the other builder
    /// ends, and a new one starts: its guests would be in the wrong builder.
    /// </summary>
    public static async Task<BuildSessionInfo?> StartAsync(string? baseName, string workspace = BuildSessionInfo.BaseWorkspace) =>
        ParseSession(await CloudApiClient.CallApiAsync("build-sessions", HttpMethod.Post, payload: new { base_name = baseName, workspace }));

    public static Task EndAsync(string sessionId) =>
        CloudApiClient.CallApiAsync($"build-sessions/{sessionId}", HttpMethod.Delete);

    /// <returns>Whether the session is still running; false once the host lost premium.</returns>
    public static async Task<bool> HeartbeatAsync(string sessionId)
    {
        var body = await CloudApiClient.CallApiAsync($"build-sessions/{sessionId}/heartbeat", HttpMethod.Post);
        return JObject.Parse(body)["data"]?["active"]?.Value<bool>() == true;
    }

    /// <summary>
    /// Invites a player. Succeeds whether or not anybody was reached - the platform never says if
    /// a Steam id has an account or has blocked you - so the UI says "sent", never "invited".
    /// </summary>
    public static Task InviteAsync(string sessionId, string steamId) =>
        CloudApiClient.CallApiAsync($"build-sessions/{sessionId}/invites", HttpMethod.Post, payload: new { steam_id = steamId });

    public static async Task<BuildSessionInfo?> AcceptAsync(string sessionId) =>
        ParseSession(await CloudApiClient.CallApiAsync($"build-sessions/{sessionId}/accept", HttpMethod.Post));

    public static Task DeclineAsync(string sessionId) =>
        CloudApiClient.CallApiAsync($"build-sessions/{sessionId}/decline", HttpMethod.Post);

    /// <summary>Declines and blocks the host, who is only ever told "declined".</summary>
    public static Task IgnoreAsync(string sessionId) =>
        CloudApiClient.CallApiAsync($"build-sessions/{sessionId}/ignore", HttpMethod.Post);

    public static Task LeaveAsync(string sessionId) =>
        CloudApiClient.CallApiAsync($"build-sessions/{sessionId}/leave", HttpMethod.Post);

    public static async Task<BuildSessionInfo?> SetCanBuildAsync(string sessionId, string memberId, bool canBuild) =>
        ParseSession(await CloudApiClient.CallApiAsync(
            $"build-sessions/{sessionId}/members/{memberId}", HttpMethod.Patch, payload: new { can_build = canBuild }));

    /// <summary>Kicks a guest, or withdraws an invite they have not answered.</summary>
    public static async Task<BuildSessionInfo?> KickAsync(string sessionId, string memberId) =>
        ParseSession(await CloudApiClient.CallApiAsync($"build-sessions/{sessionId}/members/{memberId}", HttpMethod.Delete));

    // ---- the relay: the base itself, carried between host and guests ----

    /// <summary>The host's whole base. The platform refuses a version older than the one it holds.</summary>
    public static Task PutSnapshotAsync(string sessionId, long version, JToken snapshot) =>
        CloudApiClient.CallApiAsync($"build-sessions/{sessionId}/snapshot", HttpMethod.Put,
            payload: new { version, snapshot = AsJson(snapshot) });

    /// <summary>The last snapshot as the platform's <c>data</c> object ({ version, snapshot }), or null when there is none yet.</summary>
    public static async Task<JObject?> GetSnapshotAsync(string sessionId)
    {
        try
        {
            var body = await CloudApiClient.CallApiAsync($"build-sessions/{sessionId}/snapshot", HttpMethod.Get);
            return JObject.Parse(body)["data"] as JObject;
        }
        catch (CloudApiException ex) when (ex.StatusCode == 404 && ex.Reason.Contains("no_snapshot"))
        {
            return null;
        }
    }

    /// <summary>One batch of the host's changes ({ v, ops }), for everyone else in the session.</summary>
    public static Task SendOpsAsync(string sessionId, JObject batch) =>
        CloudApiClient.CallApiAsync($"build-sessions/{sessionId}/ops", HttpMethod.Post, payload: AsJson(batch));

    /// <summary>A guest's change for the host to check and apply.</summary>
    public static Task ProposeAsync(string sessionId, string kind, JToken payload) =>
        CloudApiClient.CallApiAsync($"build-sessions/{sessionId}/proposals", HttpMethod.Post,
            payload: new { kind, payload = AsJson(payload) });

    public static Task RequestSnapshotAsync(string sessionId) =>
        CloudApiClient.CallApiAsync($"build-sessions/{sessionId}/snapshot-request", HttpMethod.Post);

    /// <summary>
    /// The builder's JSON arrives as Newtonsoft tokens, but the client serializes payloads with System.Text.Json, which
    /// would write a JObject out as its properties' metadata. A JsonElement it writes as the JSON it holds.
    /// </summary>
    internal static System.Text.Json.JsonElement AsJson(JToken token) =>
        System.Text.Json.JsonDocument.Parse(token.ToString(Newtonsoft.Json.Formatting.None)).RootElement.Clone();

    private static BuildSessionInfo? ParseSession(string body) =>
        BuildSessionInfo.Parse(JObject.Parse(body)["data"]);
}
