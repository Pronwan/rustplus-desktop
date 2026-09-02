using RustPlusDesk.Services.Cloud;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.Social;

/// <summary>How the recipient's inbox handles a stranger's first message.</summary>
public enum AcceptMode { Auto, Approval, Off }

/// <summary>What somebody is advertising, or nothing at all.</summary>
public enum LfgMode { None, LookingForTeam, LookingForMembers }

/// <summary>Everything the panel needs to draw itself on open.</summary>
/// <summary>Why a line was refused, in the words the panel needs to pick a sentence.</summary>
public enum ChatPostResult
{
    Ok,

    /// <summary>The rules of the room have not been read yet. The panel shows them.</summary>
    ConsentRequired,

    /// <summary>Timed out or banned. The bar above the box says until when.</summary>
    Sanctioned,

    /// <summary>The account is too new to post. Costs a spammer time, which is the point.</summary>
    TooNew,

    /// <summary>The same line, again, within the window.</summary>
    Duplicate,

    /// <summary>The public room carries no links.</summary>
    LinkNotAllowed,

    /// <summary>Nothing usable was left after cleaning.</summary>
    Empty,

    /// <summary>Anything else, including not reaching the platform at all.</summary>
    Failed,
}

public sealed record SocialSettings(
    AcceptMode Accept,
    bool LfgConsent,
    bool DmConsent,
    bool ChatConsent,
    string? NameColor,
    // Whether the layer is open to this account at all. The platform rolls it out in stages, and
    // this is the one read that still answers while it is closed - everything else refuses, so
    // without this the app could not tell "not yet" from "you are banned".
    bool Enabled = true);

/// <summary>
/// The social layer, from the client's side.
///
/// Every one of these can fail, and none of them is worth a dialog when it does — the panel shows
/// what it has and says so. That is why the reads return null on failure rather than throwing:
/// "we could not reach the cloud" and "you have not set anything" look identical to a user, and
/// both mean the panel should not pretend to know.
/// </summary>
/// <summary>The two rooms, named as the platform names them.</summary>
public static class ChatRooms
{
    public const string Public = "public";

    public const string Supporter = "supporter";
}

public static class SocialApi
{
    /// <summary>
    /// The quoted line above a reply, or null when the message answers nothing.
    ///
    /// A reference with no quote is kept rather than discarded: it means the original was
    /// deleted, and the view says so instead of quietly showing an ordinary message.
    /// </summary>
    internal static Models.ChatReplyReference? ParseReply(JsonElement row)
    {
        if (!row.TryGetProperty("reply_to", out var reply) || reply.ValueKind != JsonValueKind.Object)
            return null;

        var id = Str(reply, "id");
        if (string.IsNullOrEmpty(id)) return null;

        var quote = reply.TryGetProperty("quote", out var q) && q.ValueKind == JsonValueKind.Object ? q : default;

        return new Models.ChatReplyReference
        {
            Id = id!,
            SenderName = Str(quote, "sender_name"),
            Excerpt = Str(quote, "excerpt"),
        };
    }

    /// <summary>
    /// Whether a sender id is the signed-in account. Shared by both paths that build chat lines,
    /// so a message looks the same whether it arrived by poll or over the socket.
    /// </summary>
    internal static bool IsOwnSender(string? senderId)
    {
        var me = CloudAuthManager.CurrentUser?.Id;
        return !string.IsNullOrEmpty(senderId)
            && !string.IsNullOrEmpty(me)
            && string.Equals(senderId, me, StringComparison.Ordinal);
    }

    private static SocialSettings? _cachedSettings;
    private static DateTime _settingsCachedAt = DateTime.MinValue;
    private static readonly TimeSpan SettingsCacheDuration = TimeSpan.FromMinutes(5);

    public static void InvalidateSettingsCache()
    {
        _cachedSettings = null;
        _settingsCachedAt = DateTime.MinValue;
    }

    public static async Task<SocialSettings?> GetSettingsAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedSettings != null && DateTime.UtcNow - _settingsCachedAt < SettingsCacheDuration)
        {
            return _cachedSettings;
        }

        try
        {
            var body = await CloudApiClient.CallApiAsync("social/settings", HttpMethod.Get)
                .ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            var data = doc.RootElement.GetProperty("data");

            var settings = new SocialSettings(
                ParseAccept(data.TryGetProperty("accept_mode", out var a) ? a.GetString() : null),
                data.TryGetProperty("lfg_consent", out var l) && l.GetBoolean(),
                data.TryGetProperty("dm_consent", out var d) && d.GetBoolean(),
                data.TryGetProperty("chat_consent", out var cc) && cc.GetBoolean(),
                data.TryGetProperty("name_color", out var c) ? c.GetString() : null,
                !doc.RootElement.TryGetProperty("meta", out var meta)
                    || !meta.TryGetProperty("enabled", out var enabled)
                    || enabled.ValueKind != System.Text.Json.JsonValueKind.False);

            _cachedSettings = settings;
            _settingsCachedAt = DateTime.UtcNow;

            return settings;
        }
        catch
        {
            return _cachedSettings;
        }
    }

    public static async Task<bool> SetAcceptModeAsync(AcceptMode mode)
    {
        InvalidateSettingsCache();
        return await WriteAsync("social/settings", HttpMethod.Put, new { accept_mode = Serialize(mode) }).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets the supporter name colour, or clears it with null.
    ///
    /// The platform validates the name against its own palette and refuses without a plan, so a
    /// false here means "not allowed" as often as "did not reach" — both leave the old colour.
    /// </summary>
    public static async Task<bool> SetNameColorAsync(string? colorKey)
    {
        InvalidateSettingsCache();
        return await WriteAsync("social/settings", HttpMethod.Put, new { name_color = colorKey }).ConfigureAwait(false);
    }

    /// <summary>Records that the current disclosure was read. Scope is "lfg" or "dm" or "chat".</summary>
    public static async Task<bool> ConsentAsync(string scope)
    {
        InvalidateSettingsCache();
        return await WriteAsync("social/consent", HttpMethod.Post, new { scope }).ConfigureAwait(false);
    }

    public sealed record MyLfgListing(LfgMode Mode, string? Blurb, string? Region, string? ServerName);

    /// <summary>The full listing details, or None when there is none.</summary>
    public static async Task<MyLfgListing> GetListingDetailsAsync()
    {
        try
        {
            var (status, body) = await CloudApiClient.TryCallApiAsync("social/lfg/me", HttpMethod.Get)
                .ConfigureAwait(false);

            if (status == 404 || status is < 200 or >= 300) return new MyLfgListing(LfgMode.None, null, null, null);

            using var doc = JsonDocument.Parse(body);
            var data = doc.RootElement.GetProperty("data");
            var modeStr = data.TryGetProperty("mode", out var m) ? m.GetString() : null;
            var blurb = data.TryGetProperty("blurb", out var b) ? b.GetString() : null;
            var region = data.TryGetProperty("region", out var r) ? r.GetString() : null;
            var serverName = data.TryGetProperty("server_name", out var s) ? s.GetString() : null;

            var mode = modeStr switch
            {
                "lfg" => LfgMode.LookingForTeam,
                "lfm" => LfgMode.LookingForMembers,
                _ => LfgMode.None,
            };

            return new MyLfgListing(mode, blurb, region, serverName);
        }
        catch
        {
            return new MyLfgListing(LfgMode.None, null, null, null);
        }
    }

    /// <summary>The listing, or None when there is none — including when the cloud is unreachable.</summary>
    public static async Task<LfgMode> GetListingAsync()
    {
        var details = await GetListingDetailsAsync().ConfigureAwait(false);
        return details.Mode;
    }

    /// <summary>
    /// Publishes or withdraws a listing. Returns false when the disclosure has not been accepted
    /// yet — the server answers 409 for that, which is a step missing rather than a refusal.
    /// </summary>
    public static async Task<bool> SetListingAsync(LfgMode mode, string? blurb = null, string? region = null, string? serverName = null)
    {
        if (mode == LfgMode.None)
            return await WriteAsync("social/lfg/me", HttpMethod.Delete).ConfigureAwait(false);

        bool isPremium = Auth.SupabaseAuthManager.IsPremium;
        var payload = new
        {
            mode = mode == LfgMode.LookingForTeam ? "lfg" : "lfm",
            blurb = string.IsNullOrWhiteSpace(blurb) ? null : blurb.Trim(),
            region = string.IsNullOrWhiteSpace(region) ? Helpers.AppLanguages.Current() : region.Trim(),
            server_name = string.IsNullOrWhiteSpace(serverName) ? null : serverName.Trim(),
            is_supporter = isPremium,
            is_premium = isPremium,
        };
        return await WriteAsync("social/lfg/me", HttpMethod.Put, payload).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates the connected server on an active listing, without disturbing mode or blurb.
    /// </summary>
    public static async Task UpdateActiveListingServerAsync(string? serverName)
    {
        try
        {
            var current = await GetListingDetailsAsync().ConfigureAwait(false);
            if (current.Mode == LfgMode.None) return;

            await SetListingAsync(current.Mode, current.Blurb, Helpers.AppLanguages.Current(), serverName)
                .ConfigureAwait(false);
        }
        catch { }
    }

    /// <summary>
    /// Updates language/region on an active listing when user changes UI language.
    /// </summary>
    public static async Task UpdateActiveListingLanguageAsync(string? language)
    {
        try
        {
            var current = await GetListingDetailsAsync().ConfigureAwait(false);
            if (current.Mode == LfgMode.None) return;

            await SetListingAsync(current.Mode, current.Blurb, language ?? Helpers.AppLanguages.Current(), current.ServerName)
                .ConfigureAwait(false);
        }
        catch { }
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
    {
        bool isPremium = Auth.SupabaseAuthManager.IsPremium;
        return WriteAsync("social/conversations", HttpMethod.Post, new { user_id = userId, body, origin, is_supporter = isPremium, is_premium = isPremium });
    }

    public static Task<bool> ReplyAsync(string conversationId, string body)
    {
        bool isPremium = Auth.SupabaseAuthManager.IsPremium;
        return WriteAsync($"social/conversations/{conversationId}/messages", HttpMethod.Post, new { body, is_supporter = isPremium, is_premium = isPremium });
    }

    public static Task<bool> AcceptThreadAsync(string conversationId)
        => WriteAsync($"social/conversations/{conversationId}/accept", HttpMethod.Post);

    public static Task<bool> DeclineThreadAsync(string conversationId)
        => WriteAsync($"social/conversations/{conversationId}/decline", HttpMethod.Post);

    public static Task<bool> MarkReadAsync(string conversationId)
        => WriteAsync($"social/conversations/{conversationId}/read", HttpMethod.Post);

    /// <summary>
    /// The block list, so it can be undone. A list you cannot see is a list you cannot leave, and
    /// blocking somebody in the heat of an argument is exactly the kind of thing people want back.
    ///
    /// Empty on failure, like the other reads: an empty list and an unreachable one both mean
    /// there is nothing to show right now.
    /// </summary>
    public static async Task<System.Collections.Generic.List<Models.BlockedPlayer>> GetBlocksAsync()
    {
        var result = new System.Collections.Generic.List<Models.BlockedPlayer>();

        try
        {
            var body = await CloudApiClient.CallApiAsync("social/blocks", HttpMethod.Get)
                .ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var row in rows.EnumerateArray())
            {
                var blocked = row.TryGetProperty("blocked", out var b) && b.ValueKind == JsonValueKind.Object ? b : default;

                var id = Str(blocked, "id") ?? Str(row, "blocked_id");
                if (string.IsNullOrWhiteSpace(id)) continue;

                result.Add(new Models.BlockedPlayer
                {
                    UserId = id!,
                    DisplayName = Str(blocked, "display_name") ?? Str(blocked, "name") ?? "—",
                    AvatarUrl = Str(blocked, "avatar_url"),
                    SteamId = Str(blocked, "steam_id"),
                    BlockedAt = Date(row, "created_at"),
                });
            }
        }
        catch
        {
            // Deliberately quiet.
        }

        return result;
    }

    /// <summary>Takes somebody off the list. The server treats a repeat as already done.</summary>
    public static Task<bool> UnblockAsync(string userId)
        => WriteAsync($"social/blocks/{userId}", HttpMethod.Delete);

    // ── Friends ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The friends list and the requests on both sides.
    ///
    /// Empty on failure like the other reads, with Ok saying which kind of empty it is — an
    /// account with no friends and an unreachable platform should not both offer to add one.
    /// </summary>
    public static async Task<Models.FriendList> GetFriendsAsync()
    {
        var friends = new System.Collections.Generic.List<Models.Friend>();
        var incoming = new System.Collections.Generic.List<Models.Friend>();
        var outgoing = new System.Collections.Generic.List<Models.Friend>();

        try
        {
            var body = await CloudApiClient.CallApiAsync("social/friends", HttpMethod.Get)
                .ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return new Models.FriendList(friends, incoming, outgoing, false);

            Fill(data, "friends", friends);
            Fill(data, "incoming", incoming);
            Fill(data, "outgoing", outgoing);

            return new Models.FriendList(friends, incoming, outgoing);
        }
        catch
        {
            return new Models.FriendList(friends, incoming, outgoing, false);
        }
    }

    private static void Fill(JsonElement data, string name, System.Collections.Generic.List<Models.Friend> into)
    {
        if (!data.TryGetProperty(name, out var rows) || rows.ValueKind != JsonValueKind.Array) return;

        foreach (var row in rows.EnumerateArray())
            into.Add(ParseFriend(row));
    }

    private static Models.Friend ParseFriend(JsonElement row)
    {
        var user = row.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object ? u : default;

        return new Models.Friend
        {
            Id = Str(row, "id") ?? "",
            State = Str(row, "state") ?? "pending",
            RequestedByMe = row.TryGetProperty("requested_by_me", out var mine) && mine.ValueKind == JsonValueKind.True,
            UserId = Str(user, "id") ?? "",
            DisplayName = Str(user, "display_name") ?? "—",
            AvatarUrl = Str(user, "avatar_url"),
            SteamId = Str(user, "steam_id"),
            IsOnline = user.ValueKind == JsonValueKind.Object
                && user.TryGetProperty("is_online", out var on) && on.ValueKind == JsonValueKind.True,
            Server = Str(row, "server"),
            TeamSize = row.TryGetProperty("team_size", out var ts) && ts.TryGetInt32(out var n) ? n : 0,
        };
    }

    /// <summary>Why a friend request was refused, in the words the panel needs.</summary>
    public enum FriendRequestResult
    {
        Ok,

        /// <summary>No account carries that Steam id — or the person behind it blocked you.</summary>
        NotFound,

        /// <summary>Already asked, and still waiting for an answer.</summary>
        Pending,

        AlreadyFriends,

        /// <summary>They said no. The answer stands.</summary>
        Declined,

        /// <summary>Sanctioned, or blocked in one direction or the other.</summary>
        Refused,

        Failed,
    }

    /// <summary>
    /// Asks somebody to be a friend, with the one message that goes with it.
    ///
    /// Addressed by Steam id because a name is not unique, and because a search that answers with
    /// near-matches would be a directory of everybody who plays.
    /// </summary>
    public static async Task<FriendRequestResult> AddFriendAsync(string steamId, string message)
    {
        try
        {
            await CloudApiClient.CallApiAsync("social/friends", HttpMethod.Post,
                payload: new { steam_id = steamId, body = message }).ConfigureAwait(false);

            return FriendRequestResult.Ok;
        }
        catch (CloudApiException ex)
        {
            var reason = ex.Reason ?? "";

            if (ex.StatusCode == 404) return FriendRequestResult.NotFound;
            if (reason.Contains("already_friends", StringComparison.Ordinal)) return FriendRequestResult.AlreadyFriends;
            if (reason.Contains("declined", StringComparison.Ordinal)) return FriendRequestResult.Declined;
            if (reason.Contains("pending", StringComparison.Ordinal)) return FriendRequestResult.Pending;
            if (ex.StatusCode == 403) return FriendRequestResult.Refused;

            return FriendRequestResult.Failed;
        }
        catch
        {
            return FriendRequestResult.Failed;
        }
    }

    public static Task<bool> AcceptFriendAsync(string friendshipId)
        => WriteAsync($"social/friends/{friendshipId}/accept", HttpMethod.Post);

    public static Task<bool> DeclineFriendAsync(string friendshipId)
        => WriteAsync($"social/friends/{friendshipId}/decline", HttpMethod.Post);

    /// <summary>Removes a friend, or withdraws a request that has not been answered.</summary>
    public static Task<bool> RemoveFriendAsync(string friendshipId)
        => WriteAsync($"social/friends/{friendshipId}", HttpMethod.Delete);

    public static Task<bool> BlockAsync(string userId)
        => WriteAsync("social/blocks", HttpMethod.Post, new { user_id = userId });

    public static Task<bool> ReportAsync(string userId, string reason, string? messageId = null, string? note = null)
        => WriteAsync("social/reports", HttpMethod.Post, new { user_id = userId, reason, message_id = messageId, note });

    // ── The public room ─────────────────────────────────────────────────────

    /// <summary>
    /// Reads the room. Returns an empty snapshot on failure rather than throwing — an empty room
    /// and an unreachable one both mean there is nothing to read yet.
    ///
    /// With <paramref name="since"/> it returns only what was written after that timestamp,
    /// which is what a live nudge asks for: the server still decides what the reader may see,
    /// so blocks and sanctions keep working without the client holding a block list of its own.
    /// </summary>
    public static async Task<Models.ChatSnapshot> GetChatAsync(string? since = null, int limit = 50, string room = ChatRooms.Public)
    {
        var lines = new System.Collections.Generic.List<Models.ChatLine>();
        Models.ChatSanction? sanction = null;
        var slowMode = 0;
        var ok = false;
        var supporterRoom = false;
        var maxLength = 128;

        try
        {
            var query = new System.Collections.Generic.Dictionary<string, string>
            {
                ["limit"] = limit.ToString(),
                ["room"] = room,
            };
            if (!string.IsNullOrWhiteSpace(since))
            {
                query["since"] = since!;
            }

            var body = await CloudApiClient.CallApiAsync("social/chat", HttpMethod.Get, queryParams: query)
                .ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in rows.EnumerateArray())
                {
                    var sender = row.TryGetProperty("sender", out var s) && s.ValueKind == JsonValueKind.Object ? s : default;

                    var roles = new System.Collections.Generic.List<string>();
                    if (sender.ValueKind == JsonValueKind.Object && sender.TryGetProperty("roles", out var rArray) && rArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var r in rArray.EnumerateArray())
                        {
                            string? roleStr = null;
                            if (r.ValueKind == JsonValueKind.String)
                            {
                                roleStr = r.GetString();
                            }
                            else if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("name", out var nameProp))
                            {
                                roleStr = nameProp.GetString();
                            }
                            if (!string.IsNullOrWhiteSpace(roleStr)) roles.Add(roleStr!);
                        }
                    }

                    bool isSupporter = (sender.ValueKind == JsonValueKind.Object && sender.TryGetProperty("is_supporter", out var sup1) && (sup1.ValueKind == JsonValueKind.True || (sup1.ValueKind == JsonValueKind.Number && sup1.GetInt32() == 1)))
                        || (sender.ValueKind == JsonValueKind.Object && sender.TryGetProperty("is_premium", out var prem1) && (prem1.ValueKind == JsonValueKind.True || (prem1.ValueKind == JsonValueKind.Number && prem1.GetInt32() == 1)))
                        || (row.TryGetProperty("is_supporter", out var sup2) && (sup2.ValueKind == JsonValueKind.True || (sup2.ValueKind == JsonValueKind.Number && sup2.GetInt32() == 1)))
                        || (row.TryGetProperty("is_premium", out var prem2) && (prem2.ValueKind == JsonValueKind.True || (prem2.ValueKind == JsonValueKind.Number && prem2.GetInt32() == 1)))
                        || roles.Any(r => string.Equals(r, "supporter", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "premium", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "vip", StringComparison.OrdinalIgnoreCase));

                    lines.Add(new Models.ChatLine
                    {
                        Id = Str(row, "id") ?? "",
                        Body = Str(row, "body") ?? "",
                        // Fall back to the nested sender object (like the realtime path) — the server
                        // returns the id under "sender.id", not always a top-level "sender_id", so
                        // without this block/report bail out on a null SenderId.
                        SenderId = Str(row, "sender_id") ?? Str(sender, "id"),
                        SenderName = Str(sender, "display_name") ?? Str(sender, "name") ?? "—",
                        AvatarUrl = Str(sender, "avatar_url"),
                        SteamId = Str(sender, "steam_id"),
                        IsSupporter = isSupporter,
                        NameColor = Str(sender, "name_color"),
                        IsMine = IsOwnSender(Str(row, "sender_id") ?? Str(sender, "id")),
                        ReplyTo = ParseReply(row),
                        Roles = roles,
                        SentAt = Date(row, "created_at"),
                        SentAtIso = Str(row, "created_at"),
                    });
                }
            }

            if (root.TryGetProperty("meta", out var meta))
            {
                if (meta.TryGetProperty("sanction", out var s2)
                    && s2.ValueKind == JsonValueKind.Object)
                {
                    sanction = new Models.ChatSanction(
                        Str(s2, "kind") ?? "timeout",
                        Str(s2, "reason") ?? "",
                        Date(s2, "expires_at"));
                }

                supporterRoom = meta.TryGetProperty("supporter_room", out var sr)
                    && sr.ValueKind == JsonValueKind.True;

                if (meta.TryGetProperty("slow_mode", out var sm) && sm.TryGetInt32(out var smVal))
                {
                    slowMode = smVal;
                }
                else if (meta.TryGetProperty("slow_mode_seconds", out var sms) && sms.TryGetInt32(out var smsVal))
                {
                    slowMode = smsVal;
                }

                if (meta.TryGetProperty("max_length", out var ml) && ml.TryGetInt32(out var mlVal))
                {
                    maxLength = mlVal;
                }
            }

            ok = true;
        }
        catch
        {
            // Deliberately quiet.
        }

        return new Models.ChatSnapshot(lines, sanction, slowMode, ok, supporterRoom, maxLength);
    }

    /// <summary>
    /// Posts a line, and says which refusal it was when it does not go through.
    ///
    /// The reasons are not interchangeable: one of them is answered by reading a notice, one by
    /// waiting, one by rewriting the message. Collapsing them into "it did not work" leaves the
    /// user guessing which, and the guess is usually "the app is broken".
    /// </summary>
    public static async Task<ChatPostResult> PostChatAsync(string body, string room = ChatRooms.Public, string? replyToId = null)
    {
        try
        {
            bool isPremium = Auth.SupabaseAuthManager.IsPremium;
            await CloudApiClient.CallApiAsync("social/chat", HttpMethod.Post, payload: new { body, room, reply_to = replyToId, is_supporter = isPremium, is_premium = isPremium })
                .ConfigureAwait(false);

            return ChatPostResult.Ok;
        }
        catch (CloudApiException ex)
        {
            // The server sends the reason as the message, and it is matched rather than compared
            // because the platform may wrap it.
            var reason = ex.Reason ?? "";

            if (reason.Contains("consent_required", StringComparison.Ordinal)) return ChatPostResult.ConsentRequired;
            if (reason.Contains("sanctioned", StringComparison.Ordinal)) return ChatPostResult.Sanctioned;
            if (reason.Contains("account_too_new", StringComparison.Ordinal)) return ChatPostResult.TooNew;
            if (reason.Contains("link_not_allowed", StringComparison.Ordinal)) return ChatPostResult.LinkNotAllowed;
            if (reason.Contains("duplicate", StringComparison.Ordinal)) return ChatPostResult.Duplicate;
            if (reason.Contains("empty", StringComparison.Ordinal)) return ChatPostResult.Empty;

            return ChatPostResult.Failed;
        }
        catch
        {
            return ChatPostResult.Failed;
        }
    }

    /// <summary>
    /// Picks the other participant out of a thread's members.
    ///
    /// The API returns both sides rather than "the other one", because who that is depends on who
    /// is asking — and an endpoint that answers differently per caller is one that caches wrong.
    /// </summary>
    private static Models.SocialThread ParseThread(JsonElement row, string? me)
    {
        JsonElement other = default;
        var createdBy = Str(row, "created_by");
        var isCreatedByMe = !string.IsNullOrEmpty(createdBy) && !string.IsNullOrEmpty(me) && createdBy == me;

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
            CreatedBy = createdBy,
            IsCreatedByMe = isCreatedByMe,
            CounterpartId = Str(other, "id"),
            CounterpartName = Str(other, "display_name") ?? Str(other, "name") ?? "—",
            CounterpartSteamId = Str(other, "steam_id"),
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

        var roles = new System.Collections.Generic.List<string>();
        if (user.ValueKind == JsonValueKind.Object && user.TryGetProperty("roles", out var rArray) && rArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in rArray.EnumerateArray())
            {
                string? roleStr = null;
                if (r.ValueKind == JsonValueKind.String)
                {
                    roleStr = r.GetString();
                }
                else if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("name", out var nameProp))
                {
                    roleStr = nameProp.GetString();
                }
                if (!string.IsNullOrWhiteSpace(roleStr)) roles.Add(roleStr!);
            }
        }

        return new Models.LfgEntry
        {
            UserId = Str(user, "id") ?? "",
            // display_name is what a player set; name is what Steam or Discord gave us. Falling
            // through keeps the row from being blank for accounts that never chose one.
            DisplayName = Str(user, "display_name") ?? Str(user, "name") ?? "—",
            AvatarUrl = Str(user, "avatar_url"),
            SteamId = Str(user, "steam_id"),
            Roles = roles,
            Language = Str(presence, "language"),
            IsOnline = presence.ValueKind == JsonValueKind.Object
                && presence.TryGetProperty("is_online", out var on) && on.ValueKind == JsonValueKind.True,
            IsSupporter = row.TryGetProperty("is_supporter", out var s) && s.ValueKind == JsonValueKind.True,
            TeamName = Str(team, "name"),
            TeamSize = team.ValueKind == JsonValueKind.Object
                && team.TryGetProperty("members_count", out var c) && c.TryGetInt32(out var n) ? n : 0,
            Blurb = Str(row, "blurb"),
            ServerName = Str(row, "server_name"),
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
