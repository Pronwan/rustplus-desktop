using RustPlusDesk.Services.Cloud;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.Social;

/// <summary>How the recipient's inbox handles a stranger's first message.</summary>
public enum AcceptMode { Auto, Approval, Off }

/// <summary>What somebody is advertising, or nothing at all.</summary>
public enum LfgMode { None, LookingForTeam, LookingForMembers }

/// <summary>Everything the panel needs to draw itself on open.</summary>
public sealed record SocialSettings(
    AcceptMode Accept,
    bool LfgConsent,
    bool DmConsent,
    string? NameColor);

/// <summary>
/// The social layer, from the client's side.
///
/// Every one of these can fail, and none of them is worth a dialog when it does — the panel shows
/// what it has and says so. That is why the reads return null on failure rather than throwing:
/// "we could not reach the cloud" and "you have not set anything" look identical to a user, and
/// both mean the panel should not pretend to know.
/// </summary>
public static class SocialApi
{
    public static async Task<SocialSettings?> GetSettingsAsync()
    {
        try
        {
            var body = await CloudApiClient.CallApiAsync("social/settings", HttpMethod.Get)
                .ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            var data = doc.RootElement.GetProperty("data");

            return new SocialSettings(
                ParseAccept(data.TryGetProperty("accept_mode", out var a) ? a.GetString() : null),
                data.TryGetProperty("lfg_consent", out var l) && l.GetBoolean(),
                data.TryGetProperty("dm_consent", out var d) && d.GetBoolean(),
                data.TryGetProperty("name_color", out var c) ? c.GetString() : null);
        }
        catch
        {
            return null;
        }
    }

    public static Task<bool> SetAcceptModeAsync(AcceptMode mode)
        => WriteAsync("social/settings", HttpMethod.Put, new { accept_mode = Serialize(mode) });

    /// <summary>Records that the current disclosure was read. Scope is "lfg" or "dm".</summary>
    public static Task<bool> ConsentAsync(string scope)
        => WriteAsync("social/consent", HttpMethod.Post, new { scope });

    /// <summary>The listing, or None when there is none — including when the cloud is unreachable.</summary>
    public static async Task<LfgMode> GetListingAsync()
    {
        try
        {
            var (status, body) = await CloudApiClient.TryCallApiAsync("social/lfg/me", HttpMethod.Get)
                .ConfigureAwait(false);

            // 404 is the ordinary answer for "not advertising", not a failure.
            if (status == 404) return LfgMode.None;
            if (status is < 200 or >= 300) return LfgMode.None;

            using var doc = JsonDocument.Parse(body);
            var mode = doc.RootElement.GetProperty("data").TryGetProperty("mode", out var m)
                ? m.GetString() : null;

            return mode switch
            {
                "lfg" => LfgMode.LookingForTeam,
                "lfm" => LfgMode.LookingForMembers,
                _ => LfgMode.None,
            };
        }
        catch
        {
            return LfgMode.None;
        }
    }

    /// <summary>
    /// Publishes or withdraws a listing. Returns false when the disclosure has not been accepted
    /// yet — the server answers 409 for that, which is a step missing rather than a refusal.
    /// </summary>
    public static async Task<bool> SetListingAsync(LfgMode mode)
    {
        if (mode == LfgMode.None)
            return await WriteAsync("social/lfg/me", HttpMethod.Delete).ConfigureAwait(false);

        var payload = new { mode = mode == LfgMode.LookingForTeam ? "lfg" : "lfm" };
        return await WriteAsync("social/lfg/me", HttpMethod.Put, payload).ConfigureAwait(false);
    }

    /// <summary>
    /// Pushes the listing's expiry out. Called while the client runs, because a listing that is
    /// never renewed disappears after two days — which is the point, but only for people who
    /// have actually gone.
    /// </summary>
    public static Task<bool> RenewListingAsync()
        => WriteAsync("social/lfg/me/renew", HttpMethod.Post);

    private static async Task<bool> WriteAsync(string route, HttpMethod method, object? payload = null)
    {
        try
        {
            var (status, _) = await CloudApiClient.TryCallApiAsync(route, method, payload: payload)
                .ConfigureAwait(false);

            return status is >= 200 and < 300;
        }
        catch
        {
            return false;
        }
    }

    private static AcceptMode ParseAccept(string? value) => value switch
    {
        "approval" => AcceptMode.Approval,
        "off" => AcceptMode.Off,
        _ => AcceptMode.Auto,
    };

    private static string Serialize(AcceptMode mode) => mode switch
    {
        AcceptMode.Approval => "approval",
        AcceptMode.Off => "off",
        _ => "auto",
    };
}
