using RustPlusDesk.Services.Cloud;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace RustPlusDesk.Services.Social;

/// <summary>
/// Cached friend lists and friend relationship checks for the account.
///
/// Keeps track of accepted friends, incoming requests, and outgoing requests so that UI
/// elements (such as chat and thread context menus) know immediately whether someone can be
/// friended or is already a friend without requiring redundant network requests.
/// </summary>
public static class SocialFriends
{
    public static event Action? Changed;

    private static readonly object Gate = new();

    private static readonly HashSet<string> _friendSteamIds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _friendUserIds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _pendingSteamIds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _pendingUserIds = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<Models.Friend> Friends { get; private set; } = Array.Empty<Models.Friend>();
    public static IReadOnlyList<Models.Friend> Incoming { get; private set; } = Array.Empty<Models.Friend>();
    public static IReadOnlyList<Models.Friend> Outgoing { get; private set; } = Array.Empty<Models.Friend>();

    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);
    private static DispatcherTimer? _timer;
    private static bool _started;
    private static bool _reading;
    private static bool _dirty;

    public static void Start()
    {
        lock (Gate)
        {
            if (_started) return;
            _started = true;
        }

        SocialRealtime.EnsureStarted();

        SocialRealtime.FriendRequestArrived += () => _ = RefreshAsync();
        SocialRealtime.FriendRequestSettled += () => _ = RefreshAsync();

        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += (_, __) => _ = RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
    }

    public static async Task RefreshAsync()
    {
        if (!CloudAuthManager.IsAuthenticated)
        {
            Report(new Models.FriendList(new(), new(), new(), false));
            return;
        }

        lock (Gate)
        {
            if (_reading)
            {
                _dirty = true;
                return;
            }

            _reading = true;
            _dirty = false;
        }

        try
        {
            var list = await SocialApi.GetFriendsAsync().ConfigureAwait(false);
            Report(list);
        }
        finally
        {
            bool runAgain;
            lock (Gate)
            {
                _reading = false;
                runAgain = _dirty;
                _dirty = false;
            }

            if (runAgain) _ = RefreshAsync();
        }
    }

    public static void Report(Models.FriendList list)
    {
        if (!list.Ok) return;

        lock (Gate)
        {
            Friends = list.Friends;
            Incoming = list.Incoming;
            Outgoing = list.Outgoing;

            _friendSteamIds.Clear();
            _friendUserIds.Clear();
            _pendingSteamIds.Clear();
            _pendingUserIds.Clear();

            foreach (var friend in list.Friends)
            {
                if (!string.IsNullOrWhiteSpace(friend.SteamId)) _friendSteamIds.Add(friend.SteamId.Trim());
                if (!string.IsNullOrWhiteSpace(friend.UserId)) _friendUserIds.Add(friend.UserId.Trim());
            }

            foreach (var inc in list.Incoming)
            {
                if (!string.IsNullOrWhiteSpace(inc.SteamId)) _pendingSteamIds.Add(inc.SteamId.Trim());
                if (!string.IsNullOrWhiteSpace(inc.UserId)) _pendingUserIds.Add(inc.UserId.Trim());
            }

            foreach (var outg in list.Outgoing)
            {
                if (!string.IsNullOrWhiteSpace(outg.SteamId)) _pendingSteamIds.Add(outg.SteamId.Trim());
                if (!string.IsNullOrWhiteSpace(outg.UserId)) _pendingUserIds.Add(outg.UserId.Trim());
            }
        }

        var app = System.Windows.Application.Current;
        if (app == null) return;

        if (app.Dispatcher.CheckAccess()) Changed?.Invoke();
        else app.Dispatcher.InvokeAsync(() => Changed?.Invoke());
    }

    public static bool IsFriend(string? steamId, string? userId = null)
    {
        lock (Gate)
        {
            if (!string.IsNullOrWhiteSpace(steamId) && _friendSteamIds.Contains(steamId.Trim())) return true;
            if (!string.IsNullOrWhiteSpace(userId) && _friendUserIds.Contains(userId.Trim())) return true;
            return false;
        }
    }

    public static bool HasPendingRequest(string? steamId, string? userId = null)
    {
        lock (Gate)
        {
            if (!string.IsNullOrWhiteSpace(steamId) && _pendingSteamIds.Contains(steamId.Trim())) return true;
            if (!string.IsNullOrWhiteSpace(userId) && _pendingUserIds.Contains(userId.Trim())) return true;
            return false;
        }
    }

    public static bool IsSelf(string? steamId, string? userId = null)
    {
        var mySteamId = Services.TrackingService.SteamId64;
        var myUserId = CloudAuthManager.CurrentUser?.Id;

        if (!string.IsNullOrWhiteSpace(steamId) && !string.IsNullOrWhiteSpace(mySteamId)
            && string.Equals(steamId.Trim(), mySteamId.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(myUserId)
            && string.Equals(userId.Trim(), myUserId.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public static bool CanBeFriended(string? steamId, string? userId = null)
    {
        if (string.IsNullOrWhiteSpace(steamId)) return false;
        if (IsSelf(steamId, userId)) return false;
        if (IsFriend(steamId, userId)) return false;
        if (HasPendingRequest(steamId, userId)) return false;
        return true;
    }
}
