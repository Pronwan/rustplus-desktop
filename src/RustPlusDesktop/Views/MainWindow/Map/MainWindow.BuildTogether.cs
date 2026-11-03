using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using RustPlusDesk.Services.Auth;
using RustPlusDesk.Services.Builder;
using RustPlusDesk.Services.Cloud;
using RustPlusDesk.Services.Social;

namespace RustPlusDesk.Views;

/// <summary>
/// The desktop half of the Base Builder's "Together" panel.
///
/// The builder never talks to the platform: it posts what the player wants (<c>coop_*</c>) and
/// this answers every one of them with the whole state through <c>window.setCoopState</c> - even
/// after a failure, so the panel is never left waiting with its buttons disabled. The token, the
/// premium check and the live socket stay out of the web page.
///
/// Leaving the workshop leaves the session: a host who closes it ends it for everyone, a guest
/// who closes it walks out. There is no builder left to keep either of them in it.
/// </summary>
public partial class MainWindow
{
    private const string BuildTogetherPricingUrl = "https://rustplusdesktop.cloud/pricing";

    /// <summary>Well inside the platform's sixty seconds, so one lost beat does not end the session.</summary>
    private static readonly TimeSpan BuildHeartbeatInterval = TimeSpan.FromSeconds(20);

    private static readonly Regex SteamIdPattern = new(@"^\d{17}$", RegexOptions.Compiled);

    private int _buildTogetherAttached;
    private BuildSessionInfo? _buildHostSession;
    private string? _buildSessionChannel;
    private DispatcherTimer? _buildHeartbeat;
    private (string Text, string Tone)? _buildTogetherNotice;

    private BuildSessionInfo? ActiveBuildSession => _buildHostSession ?? BuildGuestSession;

    /// <summary>Entry point for the builder's <c>coop_*</c> messages. Always ends by pushing state.</summary>
    private async Task HandleBuildTogetherMessageAsync(JObject message)
    {
        var type = message["type"]?.ToString();
        _buildTogetherNotice = null;

        try
        {
            switch (type)
            {
                case "coop_ready":
                    AttachBuildTogether();
                    await CloudFeatureFlags.RefreshAsync();
                    await RefreshBuildTogetherAsync();
                    try { await SocialFriends.RefreshAsync(); } catch { /* no social layer: invite by Steam id only */ }
                    break;

                case "coop_start":
                    _buildHostSession = await BuildSessionApi.StartAsync(message["baseName"]?.ToString());
                    break;

                case "coop_invite" when _buildHostSession != null:
                    var steamId = message["steamId"]?.ToString()?.Trim() ?? "";
                    if (!SteamIdPattern.IsMatch(steamId))
                    {
                        _buildTogetherNotice = ("A Steam ID is 17 digits, like 76561198012345678.", "error");
                        break;
                    }
                    await BuildSessionApi.InviteAsync(_buildHostSession.Id, steamId);
                    // Never "invited": the platform does not say whether anybody was reached.
                    _buildTogetherNotice = ("Invite sent. If that player can be reached, they have two minutes to answer.", "info");
                    await RefreshBuildTogetherAsync();
                    break;

                case "coop_set_can_build" when _buildHostSession != null:
                    _buildHostSession = await BuildSessionApi.SetCanBuildAsync(
                        _buildHostSession.Id, message["memberId"]?.ToString() ?? "", message["canBuild"]?.Value<bool>() == true);
                    break;

                case "coop_kick" when _buildHostSession != null:
                    _buildHostSession = await BuildSessionApi.KickAsync(_buildHostSession.Id, message["memberId"]?.ToString() ?? "");
                    break;

                case "coop_end" when _buildHostSession != null:
                    var ended = _buildHostSession.Id;
                    _buildHostSession = null;
                    await BuildSessionApi.EndAsync(ended);
                    break;

                case "coop_leave" when BuildGuestSession != null:
                    var left = BuildGuestSession.Id;
                    BuildGuestSession = null;
                    await BuildSessionApi.LeaveAsync(left);
                    break;

                case "coop_upgrade":
                    OpenUrl(BuildTogetherPricingUrl);
                    break;
            }
        }
        catch (CloudApiException ex)
        {
            _buildTogetherNotice = (DescribeBuildTogetherError(ex), "error");
            if (ex.StatusCode is 404 or 410) await RefreshBuildTogetherQuietlyAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"[Build Together] {type} failed: {ex.Message}");
            _buildTogetherNotice = ("That did not work. Check your connection and try again.", "error");
        }

        SyncBuildTogetherConnection();
        PushBuildTogetherState();
    }

    private static string DescribeBuildTogetherError(CloudApiException ex) => ex.StatusCode switch
    {
        403 => "Hosting a session needs the Cloud package.",
        409 => "The session is full: up to eight players can join.",
        410 => "This session has ended.",
        429 => "That was a lot at once. Wait a moment, then try again.",
        503 => "Build together is switched off on the platform right now.",
        _ => "That did not work. Try again in a moment.",
    };

    /// <summary>The sessions this account hosts or joined, read fresh from the platform.</summary>
    private async Task RefreshBuildTogetherAsync()
    {
        if (!BuildSessionApi.IsAvailable)
        {
            _buildHostSession = null;
            BuildGuestSession = null;
            return;
        }

        var state = await BuildSessionApi.CurrentAsync();
        _buildHostSession = state.Hosting;
        BuildGuestSession = state.Joined;
    }

    private async Task RefreshBuildTogetherQuietlyAsync()
    {
        try { await RefreshBuildTogetherAsync(); }
        catch (Exception ex) { AppendLog($"[Build Together] Could not refresh the session: {ex.Message}"); }
    }

    /// <summary>Someone joined, answered, left, or the channel moved: read the session again.</summary>
    private void AttachBuildTogether()
    {
        if (Interlocked.Exchange(ref _buildTogetherAttached, 1) == 1) return;

        BuildSessionRealtime.InviteSettled += (_, _, _) => _ = RefreshAndPushBuildTogetherAsync();
        BuildSessionRealtime.MemberChanged += change => _ = RefreshAndPushBuildTogetherAsync();
        BuildSessionRealtime.Rekeyed += (_, _) => _ = RefreshAndPushBuildTogetherAsync();
        BuildSessionRealtime.SessionEnded += (sessionId, reason) =>
        {
            if (_buildHostSession?.Id != sessionId) return;
            _buildHostSession = null;
            _buildTogetherNotice = (reason == "premium_lost"
                ? "The session ended because the Cloud package is no longer active."
                : "The session has ended.", "info");
            SyncBuildTogetherConnection();
            PushBuildTogetherState();
        };
        BuildSessionRealtime.EnsureStarted();
    }

    private async Task RefreshAndPushBuildTogetherAsync()
    {
        await RefreshBuildTogetherQuietlyAsync();
        SyncBuildTogetherConnection();
        PushBuildTogetherState();
    }

    /// <summary>Subscribes the current session's channel and keeps a hosted session's heartbeat running.</summary>
    private void SyncBuildTogetherConnection()
    {
        var wanted = ActiveBuildSession?.Channel is { Length: > 0 } channel ? $"private-{channel}" : null;
        if (wanted != _buildSessionChannel)
        {
            if (_buildSessionChannel != null) _ = RealtimeClient.Shared.UnsubscribeAsync(_buildSessionChannel);
            _buildSessionChannel = wanted;
            if (wanted != null)
            {
                RealtimeClient.Shared.Start();
                _ = RealtimeClient.Shared.SubscribeAsync(wanted);
            }
        }

        if (_buildHostSession == null)
        {
            _buildHeartbeat?.Stop();
            return;
        }

        if (_buildHeartbeat == null)
        {
            _buildHeartbeat = new DispatcherTimer { Interval = BuildHeartbeatInterval };
            _buildHeartbeat.Tick += async (_, _) => await SendBuildHeartbeatAsync();
        }
        _buildHeartbeat.Start();
    }

    private async Task SendBuildHeartbeatAsync()
    {
        var session = _buildHostSession;
        if (session == null) return;

        try
        {
            if (await BuildSessionApi.HeartbeatAsync(session.Id)) return;
            _buildTogetherNotice = ("The session ended because the Cloud package is no longer active.", "info");
        }
        catch (CloudApiException ex) when (ex.StatusCode is 404 or 410)
        {
            _buildTogetherNotice = ("This session has ended.", "info");
        }
        catch (Exception ex)
        {
            // One missed beat is within the grace; the next one may well get through.
            AppendLog($"[Build Together] Heartbeat failed: {ex.Message}");
            return;
        }

        if (_buildHostSession?.Id == session.Id) _buildHostSession = null;
        SyncBuildTogetherConnection();
        PushBuildTogetherState();
    }

    /// <summary>The whole state, as the panel draws it. A no-op while the workshop is closed.</summary>
    private void PushBuildTogetherState()
    {
        var core = _baseBuilderWebView?.CoreWebView2;
        if (core == null) return;

        var me = CloudAuthManager.CurrentUser?.Id;
        var session = ActiveBuildSession;
        static object? Person(BuildSessionPerson? p) => p == null ? null : new { id = p.Id, name = p.DisplayName, steamId = p.SteamId };

        var state = new
        {
            available = BuildSessionApi.IsAvailable,
            premium = SupabaseAuthManager.IsPremium,
            session = session == null ? null : new
            {
                id = session.Id,
                role = session.IsHost ? "host" : "guest",
                canBuild = session.CanBuild,
                baseName = session.BaseName,
                host = Person(session.Host),
                // A guest's own row is not one of the "others".
                members = session.Members
                    .Where(m => session.IsHost || m.User?.Id != me)
                    .Select(m => new { id = m.Id, status = m.Status, canBuild = m.CanBuild, user = Person(m.User) }),
            },
            friends = SocialFriends.Friends
                .Where(f => f.IsAccepted && !string.IsNullOrEmpty(f.SteamId))
                .OrderByDescending(f => f.IsOnline)
                .Take(50)
                .Select(f => new { steamId = f.SteamId, name = f.DisplayName, online = f.IsOnline }),
            notice = _buildTogetherNotice is { } n ? new { text = n.Text, tone = n.Tone } : null,
        };

        try { _ = core.ExecuteScriptAsync($"window.setCoopState && window.setCoopState({JsonSerializer.Serialize(state)});"); }
        catch (Exception ex) { AppendLog($"[Build Together] Could not update the panel: {ex.Message}"); }
    }

    /// <summary>The workshop is going away, and the session cannot outlive it.</summary>
    private async Task LeaveBuildTogetherOnCloseAsync()
    {
        var hosted = _buildHostSession;
        var joined = BuildGuestSession;
        _buildHostSession = null;
        BuildGuestSession = null;
        _buildTogetherNotice = null;
        SyncBuildTogetherConnection();

        try
        {
            if (hosted != null) await BuildSessionApi.EndAsync(hosted.Id);
            else if (joined != null) await BuildSessionApi.LeaveAsync(joined.Id);
        }
        catch (Exception ex) { AppendLog($"[Build Together] Could not leave the session on close: {ex.Message}"); }
    }
}
