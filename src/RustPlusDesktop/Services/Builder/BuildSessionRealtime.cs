using Newtonsoft.Json.Linq;
using RustPlusDesk.Services.Cloud;
using RustPlusDesk.Services.Social;
using System;

namespace RustPlusDesk.Services.Builder;

/// <summary>
/// The co-op Base Builder broadcasts, turned into events. Raised on the UI thread.
///
/// Invites, answers, kicks and rekeys arrive on the account's own channel, so they reach the app
/// wherever the player is - the builder is usually not even open when an invite lands. Member
/// changes and the end of a session arrive on the session channel, which the builder bridge
/// subscribes to once there is a session to be in.
/// </summary>
public static class BuildSessionRealtime
{
    /// <summary>Somebody invited this account. Drives the app-wide popup.</summary>
    public static event Action<BuildInvite>? InviteReceived;

    /// <summary>A session this account held an unanswered invite for has ended; close its popup.</summary>
    public static event Action<string>? InviteWithdrawn;

    /// <summary>The host's side: an invite was answered. "Ignored" arrives as declined.</summary>
    public static event Action<string, string, bool>? InviteSettled;

    /// <summary>This account was kicked from a session.</summary>
    public static event Action<string>? Kicked;

    /// <summary>A session moved to a new channel after a kick: session id, new channel name.</summary>
    public static event Action<string, string>? Rekeyed;

    /// <summary>Someone joined, left or had build rights changed. Raw, for the builder bridge.</summary>
    public static event Action<JObject>? MemberChanged;

    /// <summary>A session ended: session id, reason (host_ended, host_timeout, premium_lost...).</summary>
    public static event Action<string, string?>? SessionEnded;

    /// <summary>A batch of the host's changes, on the session channel. Raw, for the builder.</summary>
    public static event Action<JObject>? OpsReceived;

    /// <summary>The host's side: a guest proposed a change. Raw, for the builder.</summary>
    public static event Action<JObject>? ProposalReceived;

    /// <summary>The host's side: a guest lost track and needs a fresh snapshot.</summary>
    public static event Action<string>? SnapshotRequested;

    private static readonly object Gate = new();
    private static bool _attached;

    /// <summary>Listens for builder broadcasts. Safe to call repeatedly.</summary>
    public static void EnsureStarted()
    {
        lock (Gate)
        {
            if (!_attached)
            {
                RealtimeClient.Shared.EventReceived += OnEvent;
                _attached = true;
            }
        }

        // The account channel belongs to the social layer's subscription; asking again is a no-op
        // once it runs, and starts it when no social panel has yet.
        SocialRealtime.EnsureStarted();
    }

    internal static void OnEvent(string channel, string eventName, JObject data)
    {
        var name = eventName?.TrimStart('.') ?? "";
        if (!name.StartsWith("builder.", StringComparison.OrdinalIgnoreCase)) return;

        var sessionId = data["session_id"]?.ToString();
        if (string.IsNullOrWhiteSpace(sessionId)) return;

        switch (name.ToLowerInvariant())
        {
            case "builder.invite":
                if (BuildInvite.FromBroadcast(data) is { } invite) Raise(() => InviteReceived?.Invoke(invite));
                break;

            case "builder.invite_settled":
                var answeredBy = data["user_id"]?.ToString() ?? "";
                var accepted = data["answer"]?.ToString() == "accepted";
                Raise(() => InviteSettled?.Invoke(sessionId!, answeredBy, accepted));
                break;

            case "builder.kicked":
                Raise(() => Kicked?.Invoke(sessionId!));
                break;

            case "builder.rekeyed":
                var newChannel = data["channel"]?.ToString();
                if (!string.IsNullOrWhiteSpace(newChannel)) Raise(() => Rekeyed?.Invoke(sessionId!, newChannel!));
                break;

            case "builder.member_changed":
                Raise(() => MemberChanged?.Invoke(data));
                break;

            case "builder.ops":
                Raise(() => OpsReceived?.Invoke(data));
                break;

            case "builder.proposal":
                Raise(() => ProposalReceived?.Invoke(data));
                break;

            case "builder.snapshot_requested":
                Raise(() => SnapshotRequested?.Invoke(sessionId!));
                break;

            case "builder.session_ended":
                // On the account channel it means "the session you were invited to is gone"; on the
                // session channel, "the session you are in is over". Both are worth hearing.
                if (channel.StartsWith("private-users.", StringComparison.OrdinalIgnoreCase))
                    Raise(() => InviteWithdrawn?.Invoke(sessionId!));
                else
                    Raise(() => SessionEnded?.Invoke(sessionId!, data["reason"]?.ToString()));
                break;
        }
    }

    /// <summary>Handlers touch controls, and this arrives on the socket's receive loop.</summary>
    private static void Raise(Action action)
    {
        var app = System.Windows.Application.Current;
        if (app == null) { action(); return; }

        if (app.Dispatcher.CheckAccess()) action();
        else app.Dispatcher.InvokeAsync(action);
    }
}
