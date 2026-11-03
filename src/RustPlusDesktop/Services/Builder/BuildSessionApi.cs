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

    /// <summary>Starts a session, or returns the one this host already has open.</summary>
    public static async Task<BuildSessionInfo?> StartAsync(string? baseName) =>
        ParseSession(await CloudApiClient.CallApiAsync("build-sessions", HttpMethod.Post, payload: new { base_name = baseName }));

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

    private static BuildSessionInfo? ParseSession(string body) =>
        BuildSessionInfo.Parse(JObject.Parse(body)["data"]);
}
