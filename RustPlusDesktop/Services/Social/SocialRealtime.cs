using Newtonsoft.Json.Linq;
using RustPlusDesk.Services.Cloud;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.Social;

/// <summary>
/// The live half of the social layer: the two channels the platform pushes on, turned into
/// events the panel can hang off.
///
/// Handles real-time global chat messages, message deletions, slow mode updates, and moderation sanction events.
/// </summary>
public static class SocialRealtime
{
    /// <summary>The public room gained a line. Carries nothing: the reader re-reads.</summary>
    public static event Action? ChatChanged;

    /// <summary>A new chat message was posted in global chat.</summary>
    public static event Action<Models.ChatLine>? ChatMessageReceived;

    /// <summary>A chat message was deleted in global chat.</summary>
    public static event Action<string>? ChatMessageDeleted;

    /// <summary>Slow mode cooldown was updated across all clients.</summary>
    public static event Action<Models.ChatSlowModeEvent>? SlowModeUpdated;

    /// <summary>A system sanction (timeout/ban/lifted) was broadcasted.</summary>
    public static event Action<Models.SystemSanctionEvent>? SanctionEventReceived;

    /// <summary>A message landed in a thread. The argument is the conversation it belongs to.</summary>
    public static event Action<string>? MessageArrived;

    /// <summary>Somebody wants to open a thread and is waiting to be let in.</summary>
    public static event Action? RequestArrived;

    private const string ChatChannel = "presence-chat.global";

    private static readonly object Gate = new();

    private static bool _handlerAttached;
    private static string? _userChannel;

    /// <summary>
    /// Subscribes both channels, once. Safe to call on every panel open — the second call and
    /// the two hundredth do nothing, and the client resubscribes across reconnects on its own.
    /// </summary>
    public static void EnsureStarted()
    {
        if (!CloudAuthManager.IsAuthenticated) return;

        var me = CloudAuthManager.CurrentUser?.Id;
        if (string.IsNullOrWhiteSpace(me)) return;

        var channel = $"private-users.{me}";

        lock (Gate)
        {
            if (_userChannel == channel && _handlerAttached) return;

            if (!_handlerAttached)
            {
                RealtimeClient.Shared.EventReceived += OnEvent;
                _handlerAttached = true;
            }

            // Signing in as somebody else mid-session would otherwise leave us listening on the
            // previous account's channel — which the server would refuse to re-authorise anyway,
            // but the desired-channel set would keep asking forever.
            var previous = _userChannel;
            _userChannel = channel;

            if (previous != null && previous != channel)
                _ = RealtimeClient.Shared.UnsubscribeAsync(previous);
        }

        _ = SubscribeAsync(channel);
    }

    /// <summary>Drops both subscriptions. Called when the account goes away.</summary>
    public static void Stop()
    {
        string? channel;
        lock (Gate)
        {
            channel = _userChannel;
            _userChannel = null;
        }

        if (channel != null)
            _ = RealtimeClient.Shared.UnsubscribeAsync(channel);

        _ = RealtimeClient.Shared.UnsubscribeAsync(ChatChannel);
    }

    private static async Task SubscribeAsync(string userChannel)
    {
        try
        {
            RealtimeClient.Shared.Start();

            await RealtimeClient.Shared.SubscribeAsync(userChannel).ConfigureAwait(false);

            // The room is joined while the app runs, not while the panel is open: whispers and
            // thread requests arrive on the same connection, and an occupant list that emptied
            // whenever somebody closed a panel would describe nothing useful.
            await RealtimeClient.Shared.SubscribeAsync(ChatChannel).ConfigureAwait(false);
        }
        catch
        {
            // The client logs and retries on its own; there is nothing to tell the user.
        }
    }

    private static void OnEvent(string channel, string eventName, JObject data)
    {
        var norm = eventName?.TrimStart('.') ?? "";

        if (norm.EndsWith("ChatMessagePosted", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("chat.message", StringComparison.OrdinalIgnoreCase))
        {
            var msg = ParseChatMessage(data);
            if (msg != null)
            {
                Raise(() => ChatMessageReceived?.Invoke(msg));
            }
            Raise(() => ChatChanged?.Invoke());
        }
        else if (norm.EndsWith("ChatMessageDeleted", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("chat.message_deleted", StringComparison.OrdinalIgnoreCase))
        {
            var id = ParseMessageDeleted(data);
            if (!string.IsNullOrWhiteSpace(id))
            {
                Raise(() => ChatMessageDeleted?.Invoke(id!));
            }
        }
        else if (norm.EndsWith("ChatSlowModeUpdated", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("chat.slow_mode", StringComparison.OrdinalIgnoreCase))
        {
            var sm = ParseSlowMode(data);
            if (sm != null)
            {
                Raise(() => SlowModeUpdated?.Invoke(sm));
            }
        }
        else if (norm.EndsWith("ChatSanctionBroadcasted", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("chat.sanction_event", StringComparison.OrdinalIgnoreCase))
        {
            var sanction = ParseSanctionEvent(data);
            if (sanction != null)
            {
                Raise(() => SanctionEventReceived?.Invoke(sanction));
            }
        }
        else if (norm.EndsWith("MessageArrived", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("social.message", StringComparison.OrdinalIgnoreCase))
        {
            var conversation = data["conversation_id"]?.ToString() ?? data["data"]?["conversation_id"]?.ToString();
            if (!string.IsNullOrWhiteSpace(conversation))
                Raise(() => MessageArrived?.Invoke(conversation!));
        }
        else if (norm.EndsWith("RequestArrived", StringComparison.OrdinalIgnoreCase)
            || norm.Equals("social.request", StringComparison.OrdinalIgnoreCase))
        {
            Raise(() => RequestArrived?.Invoke());
        }
    }

    private static Models.ChatLine? ParseChatMessage(JObject root)
    {
        try
        {
            var data = root["message"] as JObject ?? root["data"] as JObject ?? root;
            var id = data["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(id)) return null;

            var body = data["body"]?.ToString() ?? "";
            var senderId = data["sender_id"]?.ToString();
            var sender = data["sender"] as JObject;

            var senderName = sender?["display_name"]?.ToString()
                ?? sender?["name"]?.ToString()
                ?? data["sender_name"]?.ToString()
                ?? "—";

            var avatarUrl = sender?["avatar_url"]?.ToString() ?? data["avatar_url"]?.ToString();
            var steamId = sender?["steam_id"]?.ToString() ?? data["steam_id"]?.ToString();

            var rolesToken = sender?["roles"] as JArray ?? data["roles"] as JArray;
            var roles = rolesToken?.Select(r => r.ToString()).ToList() ?? new List<string>();

            var createdAtStr = data["created_at"]?.ToString();
            DateTime? sentAt = null;
            if (!string.IsNullOrWhiteSpace(createdAtStr) && DateTime.TryParse(createdAtStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            {
                sentAt = dt;
            }

            return new Models.ChatLine
            {
                Id = id,
                Body = body,
                SenderId = senderId ?? sender?["id"]?.ToString(),
                SenderName = senderName,
                AvatarUrl = avatarUrl,
                SteamId = steamId,
                Roles = roles,
                SentAt = sentAt,
                SentAtIso = createdAtStr,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ParseMessageDeleted(JObject root)
    {
        try
        {
            var data = root["data"] as JObject ?? root;
            return data["id"]?.ToString() ?? data["message_id"]?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static Models.ChatSlowModeEvent? ParseSlowMode(JObject root)
    {
        try
        {
            var data = root["data"] as JObject ?? root;
            var secondsToken = data["seconds"] ?? data["slow_mode"];
            if (secondsToken == null) return null;

            var seconds = secondsToken.Value<int>();
            var updatedBy = data["updated_by"] as JObject;

            return new Models.ChatSlowModeEvent
            {
                Seconds = seconds,
                UpdatedById = updatedBy?["id"]?.ToString(),
                UpdatedByName = updatedBy?["display_name"]?.ToString() ?? updatedBy?["name"]?.ToString(),
            };
        }
        catch
        {
            return null;
        }
    }

    private static Models.SystemSanctionEvent? ParseSanctionEvent(JObject root)
    {
        try
        {
            var data = root["sanction"] as JObject ?? root["event"] as JObject ?? root["data"] as JObject ?? root;
            var id = data["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(id)) return null;

            var action = data["action"]?.ToString() ?? "issued";
            var kind = data["kind"]?.ToString() ?? "timeout";
            var scope = data["scope"]?.ToString() ?? "chat";
            var reason = data["reason"]?.ToString() ?? "";
            var duration = data["duration"]?.ToString();
            var expiresAtStr = data["expires_at"]?.ToString();
            DateTime? expiresAt = null;
            if (!string.IsNullOrWhiteSpace(expiresAtStr) && DateTime.TryParse(expiresAtStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expDt))
            {
                expiresAt = expDt;
            }

            var createdAtStr = data["created_at"]?.ToString();
            var createdAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(createdAtStr) && DateTime.TryParse(createdAtStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var crDt))
            {
                createdAt = crDt;
            }

            Models.SanctionTarget? target = null;
            if (data["target"] is JObject t)
            {
                target = new Models.SanctionTarget
                {
                    Id = t["id"]?.ToString() ?? "",
                    Name = t["name"]?.ToString() ?? "",
                    DisplayName = t["display_name"]?.ToString() ?? t["name"]?.ToString() ?? "—",
                    AvatarUrl = t["avatar_url"]?.ToString(),
                    SteamId = t["steam_id"]?.ToString(),
                };
            }

            Models.SanctionModerator? moderator = null;
            if (data["moderator"] is JObject m)
            {
                var modRoles = (m["roles"] as JArray)?.Select(r => r.ToString()).ToList() ?? new List<string>();
                moderator = new Models.SanctionModerator
                {
                    Id = m["id"]?.ToString() ?? "",
                    Name = m["name"]?.ToString() ?? "",
                    DisplayName = m["display_name"]?.ToString() ?? m["name"]?.ToString() ?? "Moderator",
                    Roles = modRoles,
                };
            }

            return new Models.SystemSanctionEvent
            {
                Id = id,
                Type = data["type"]?.ToString() ?? "system_sanction",
                Action = action,
                Kind = kind,
                Scope = scope,
                Reason = reason,
                Duration = duration,
                ExpiresAt = expiresAt,
                ExpiresAtIso = expiresAtStr,
                Target = target,
                Moderator = moderator,
                CreatedAt = createdAt,
                CreatedAtIso = createdAtStr,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Handlers touch controls, and this arrives on the socket's receive loop. Marshalling here
    /// rather than in each handler means a subscriber cannot forget.
    /// </summary>
    private static void Raise(Action action)
    {
        var app = System.Windows.Application.Current;
        if (app == null) return;

        if (app.Dispatcher.CheckAccess()) action();
        else app.Dispatcher.InvokeAsync(action);
    }
}
