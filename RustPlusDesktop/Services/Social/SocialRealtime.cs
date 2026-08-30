using Newtonsoft.Json.Linq;
using RustPlusDesk.Services.Cloud;
using System;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.Social;

/// <summary>
/// The live half of the social layer: the two channels the platform pushes on, turned into
/// events the panel can hang off.
///
/// Everything that arrives here is a nudge, never a delivery. The payloads say "something
/// changed", and what the user may actually read is decided by the endpoint that knows about
/// blocks and sanctions - so a missed frame costs nothing beyond the panel being a moment
/// behind, and the panel is still correct after a reload. That property is what makes this
/// worth having: the fallback is the state it already loads on open.
///
/// Static because the connection is one per process, like <see cref="RealtimeClient"/> itself.
/// A second subscription to the public room would show the same account twice in its
/// occupant list.
/// </summary>
public static class SocialRealtime
{
    /// <summary>The public room gained a line. Carries nothing: the reader re-reads.</summary>
    public static event Action? ChatChanged;

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
        switch (eventName)
        {
            case "chat.message":
                Raise(() => ChatChanged?.Invoke());
                break;

            case "social.message":
                var conversation = data["conversation_id"]?.ToString();
                if (!string.IsNullOrWhiteSpace(conversation))
                    Raise(() => MessageArrived?.Invoke(conversation!));
                break;

            case "social.request":
                Raise(() => RequestArrived?.Invoke());
                break;
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
