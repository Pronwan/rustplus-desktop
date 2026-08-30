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

    /// <summary>
    /// One page of listings. Returns an empty list rather than null on failure: an empty board and
    /// an unreachable one look the same to somebody scrolling, and the panel says which above the
    /// list instead of in place of it.
    /// </summary>
    public static async Task<System.Collections.Generic.List<Models.LfgEntry>> GetListingsAsync(
        LfgMode mode, string? language = null, bool onlineOnly = false)
    {
        var result = new System.Collections.Generic.List<Models.LfgEntry>();

        var query = new System.Collections.Generic.Dictionary<string, string>
        {
            ["mode"] = mode == LfgMode.LookingForMembers ? "lfm" : "lfg",
        };
        if (!string.IsNullOrWhiteSpace(language)) query["language"] = language!;
        if (onlineOnly) query["online"] = "1";

        try
        {
            var body = await CloudApiClient.CallApiAsync(
                "social/lfg/listings", HttpMethod.Get, queryParams: query).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);

            // Laravel wraps a paginator as data.data; the outer "data" is the envelope every
            // endpoint here uses, the inner one is the page.
            if (!doc.RootElement.TryGetProperty("data", out var envelope)) return result;
            if (!envelope.TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var row in rows.EnumerateArray())
                result.Add(ParseEntry(row));
        }
        catch
        {
            // Swallowed deliberately — see the summary above.
        }

        return result;
    }

    // ── Inbox ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The inbox. Empty on failure for the same reason the listings are: an empty inbox and an
    /// unreachable one lead to the same next step.
    /// </summary>
    public static async Task<System.Collections.Generic.List<Models.SocialThread>> GetThreadsAsync()
    {
        var result = new System.Collections.Generic.List<Models.SocialThread>();
        var me = CloudAuthManager.CurrentUser?.Id;

        try
        {
            var body = await CloudApiClient.CallApiAsync("social/conversations", HttpMethod.Get)
                .ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var envelope)) return result;
            if (!envelope.TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var row in rows.EnumerateArray())
                result.Add(ParseThread(row, me));
        }
        catch
        {
            // Deliberately quiet — see the summary above.
        }

        return result;
    }

    /// <summary>The messages in one thread, oldest first so it reads like a conversation.</summary>
    public static async Task<System.Collections.Generic.List<Models.SocialMessage>> GetMessagesAsync(string conversationId)
    {
        var result = new System.Collections.Generic.List<Models.SocialMessage>();
        var me = CloudAuthManager.CurrentUser?.Id;

        try
        {
            var body = await CloudApiClient.CallApiAsync(
                $"social/conversations/{conversationId}", HttpMethod.Get).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return result;
            if (!data.TryGetProperty("messages", out var page)) return result;
            if (!page.TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var row in rows.EnumerateArray())
            {
                var sender = row.TryGetProperty("sender", out var s) && s.ValueKind == JsonValueKind.Object ? s : default;
                var senderId = Str(row, "sender_id");

                result.Add(new Models.SocialMessage
                {
                    Id = Str(row, "id") ?? "",
                    Body = Str(row, "body") ?? "",
                    SenderName = Str(sender, "display_name") ?? Str(sender, "name") ?? "—",
                    IsMine = me is not null && senderId == me,
                    SentAt = Date(row, "created_at"),
                });
            }
        }
        catch
        {
            // Deliberately quiet.
        }

        return result;
    }

    public static Task<bool> OpenThreadAsync(string userId, string body, string origin = "lfg")
        => WriteAsync("social/conversations", HttpMethod.Post, new { user_id = userId, body, origin });

    public static Task<bool> ReplyAsync(string conversationId, string body)
        => WriteAsync($"social/conversations/{conversationId}/messages", HttpMethod.Post, new { body });

    public static Task<bool> AcceptThreadAsync(string conversationId)
        => WriteAsync($"social/conversations/{conversationId}/accept", HttpMethod.Post);

    public static Task<bool> DeclineThreadAsync(string conversationId)
        => WriteAsync($"social/conversations/{conversationId}/decline", HttpMethod.Post);

    public static Task<bool> MarkReadAsync(string conversationId)
        => WriteAsync($"social/conversations/{conversationId}/read", HttpMethod.Post);

    public static Task<bool> BlockAsync(string userId)
        => WriteAsync("social/blocks", HttpMethod.Post, new { user_id = userId });

    public static Task<bool> ReportAsync(string userId, string reason, string? messageId = null, string? note = null)
        => WriteAsync("social/reports", HttpMethod.Post, new { user_id = userId, reason, message_id = messageId, note });

    /// <summary>
    /// Picks the other participant out of a thread's members.
    ///
    /// The API returns both sides rather than "the other one", because who that is depends on who
    /// is asking — and an endpoint that answers differently per caller is one that caches wrong.
    /// </summary>
    private static Models.SocialThread ParseThread(JsonElement row, string? me)
    {
        JsonElement other = default;

        if (row.TryGetProperty("members", out var members) && members.ValueKind == JsonValueKind.Array)
        {
            foreach (var member in members.EnumerateArray())
            {
                if (Str(member, "user_id") == me) continue;
                if (member.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object)
                {
                    other = u;
                    break;
                }
            }
        }

        var presence = other.ValueKind == JsonValueKind.Object
            && other.TryGetProperty("presence", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;

        return new Models.SocialThread
        {
            Id = Str(row, "id") ?? "",
            State = Str(row, "state") ?? "accepted",
            CounterpartId = Str(other, "id"),
            CounterpartName = Str(other, "display_name") ?? Str(other, "name") ?? "—",
            AvatarUrl = Str(other, "avatar_url"),
            IsOnline = presence.ValueKind == JsonValueKind.Object
                && presence.TryGetProperty("is_online", out var on) && on.ValueKind == JsonValueKind.True,
            UnreadCount = row.TryGetProperty("unread_count", out var uc) && uc.TryGetInt32(out var n) ? n : 0,
            LastMessageAt = Date(row, "last_message_at"),
        };
    }

    private static DateTime? Date(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var v)
           && v.ValueKind == JsonValueKind.String
           && DateTime.TryParse(v.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static Models.LfgEntry ParseEntry(JsonElement row)
    {
        var user = row.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object ? u : default;
        var team = row.TryGetProperty("team", out var t) && t.ValueKind == JsonValueKind.Object ? t : default;
        var presence = user.ValueKind == JsonValueKind.Object
            && user.TryGetProperty("presence", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;

        return new Models.LfgEntry
        {
            UserId = Str(user, "id") ?? "",
            // display_name is what a player set; name is what Steam or Discord gave us. Falling
            // through keeps the row from being blank for accounts that never chose one.
            DisplayName = Str(user, "display_name") ?? Str(user, "name") ?? "—",
            AvatarUrl = Str(user, "avatar_url"),
            SteamId = Str(user, "steam_id"),
            Language = Str(presence, "language"),
            IsOnline = presence.ValueKind == JsonValueKind.Object
                && presence.TryGetProperty("is_online", out var on) && on.ValueKind == JsonValueKind.True,
            IsSupporter = row.TryGetProperty("is_supporter", out var s) && s.ValueKind == JsonValueKind.True,
            TeamName = Str(team, "name"),
            TeamSize = team.ValueKind == JsonValueKind.Object
                && team.TryGetProperty("members_count", out var c) && c.TryGetInt32(out var n) ? n : 0,
            Blurb = Str(row, "blurb"),
        };
    }

    private static string? Str(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var v)
           && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

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
