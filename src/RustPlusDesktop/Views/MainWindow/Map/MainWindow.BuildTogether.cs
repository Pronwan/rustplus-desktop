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

    /// <summary>A guest's session ended with its base still open; the panel offers to keep a copy until answered.</summary>
    private bool _buildGuestEnded;

    /// <summary>Reverb allows ten client events a second; the builder asks for two, this makes sure of it.</summary>
    private static readonly TimeSpan BuildPoseMinInterval = TimeSpan.FromMilliseconds(400);
    private DateTime _buildPoseSentAt;
    private string? _buildSessionChannel;
    private DispatcherTimer? _buildHeartbeat;
    private (string Text, string Tone)? _buildTogetherNotice;

    private BuildSessionInfo? ActiveBuildSession => _buildHostSession ?? BuildGuestSession;

    /// <summary>Entry point for the builder's <c>coop_*</c> messages. Always ends by pushing state.</summary>
    private async Task HandleBuildTogetherMessageAsync(JObject message)
    {
        var type = message["type"]?.ToString();
        if (type == "coop_pose")
        {
            WhisperBuildPose(message);
            return;
        }
        if (type is "coop_ops" or "coop_snapshot" or "coop_propose" or "coop_fetch_snapshot" or "coop_request_snapshot")
        {
            await RelayBuildMessageAsync(type, message);
            return;
        }

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
                    // Walking out leaves the base on screen, like the host ending it: the guest may keep a copy.
                    _buildGuestEnded = true;
                    await BuildSessionApi.LeaveAsync(left);
                    break;

                case "coop_upgrade":
                    OpenUrl(BuildTogetherPricingUrl);
                    break;

                case "coop_dismiss_ended":
                    // Kept or left behind: either way the offer is answered.
                    _buildGuestEnded = false;
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
        BuildSessionRealtime.MemberChanged += change =>
        {
            // Our own build rights take effect at once; the refresh that follows only fills in the rest.
            if (BuildGuestSession is { } joined && joined.Id == change["session_id"]?.ToString()
                && change["user_id"]?.ToString() == CloudAuthManager.CurrentUser?.Id
                && change["can_build"]?.Type == JTokenType.Boolean)
            {
                BuildGuestSession = joined with { CanBuild = change["can_build"]!.Value<bool>() };
                PushBuildTogetherState();
            }
            _ = RefreshAndPushBuildTogetherAsync();
        };
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
        AttachBuildRelay();
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
            ended = session == null && _buildGuestEnded,
        };

        try { _ = core.ExecuteScriptAsync($"window.setCoopState && window.setCoopState({JsonSerializer.Serialize(state)});"); }
        catch (Exception ex) { AppendLog($"[Build Together] Could not update the panel: {ex.Message}"); }
    }

    // ---------------------------------------------------------------- the relay (coopSync.js)

    /// <summary>
    /// One at a time and in the order the builder sent them: batch 41 must not overtake batch 40, and a snapshot must be
    /// stored before the resync op that tells guests to fetch it.
    /// </summary>
    private readonly SemaphoreSlim _buildRelayGate = new(1, 1);

    /// <summary>The base moving between apps. Quiet: no panel state is pushed, these happen on every edit.</summary>
    private async Task RelayBuildMessageAsync(string type, JObject message)
    {
        await _buildRelayGate.WaitAsync();
        try
        {
            switch (type)
            {
                case "coop_ops" when _buildHostSession != null:
                    var batch = new JObject { ["v"] = message["v"], ["ops"] = message["ops"] };
                    await BuildSessionApi.SendOpsAsync(_buildHostSession.Id, batch);
                    break;

                case "coop_snapshot" when _buildHostSession != null && message["snapshot"] is JObject snapshot:
                    await BuildSessionApi.PutSnapshotAsync(_buildHostSession.Id, message["version"]?.Value<long>() ?? 0, snapshot);
                    break;

                case "coop_propose" when BuildGuestSession != null:
                    await BuildSessionApi.ProposeAsync(BuildGuestSession.Id, message["kind"]?.ToString() ?? "edit",
                        message["payload"] ?? new JObject());
                    break;

                case "coop_request_snapshot" when BuildGuestSession != null:
                    // The host answers with a fresh snapshot and a resync op, which brings us back here as a fetch.
                    await BuildSessionApi.RequestSnapshotAsync(BuildGuestSession.Id);
                    break;

                case "coop_fetch_snapshot" when BuildGuestSession != null:
                    var data = await BuildSessionApi.GetSnapshotAsync(BuildGuestSession.Id);
                    RunInBuilder($"window.loadCoopSnapshot && window.loadCoopSnapshot({data?.ToString(Newtonsoft.Json.Formatting.None) ?? "null"});");
                    break;
            }
        }
        catch (CloudApiException ex) when (ex.StatusCode == 413 && type == "coop_ops")
        {
            // Too big for one broadcast after all: the snapshot path carries any size.
            RunInBuilder("window.coopSnapshotRequested && window.coopSnapshotRequested();");
        }
        catch (CloudApiException ex) when (ex.StatusCode is 403 or 404 or 410)
        {
            // Rights taken away, or the session is gone: the panel needs to know.
            AppendLog($"[Build Together] {type} refused ({ex.StatusCode}); refreshing the session.");
            _ = RefreshAndPushBuildTogetherAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"[Build Together] {type} failed: {ex.Message}");
            // A guest that missed a fetch would wait forever; let it try again on the next batch.
            if (type == "coop_fetch_snapshot") RunInBuilder("window.loadCoopSnapshot && window.loadCoopSnapshot(null);");
        }
        finally
        {
            _buildRelayGate.Release();
        }
    }

    /// <summary>Broadcasts from the platform that belong to the builder: the host's batches, guests' proposals, resync asks.</summary>
    private void AttachBuildRelay()
    {
        BuildSessionRealtime.OpsReceived += data =>
        {
            if (BuildGuestSession?.Id == data["session_id"]?.ToString())
                RunInBuilder($"window.applyCoopOps && window.applyCoopOps({data.ToString(Newtonsoft.Json.Formatting.None)});");
        };
        BuildSessionRealtime.ProposalReceived += data =>
        {
            if (_buildHostSession?.Id == data["session_id"]?.ToString())
                RunInBuilder($"window.applyCoopProposal && window.applyCoopProposal({data.ToString(Newtonsoft.Json.Formatting.None)});");
        };
        BuildSessionRealtime.SnapshotRequested += sessionId =>
        {
            if (_buildHostSession?.Id == sessionId)
                RunInBuilder("window.coopSnapshotRequested && window.coopSnapshotRequested();");
        };
        BuildSessionRealtime.PoseReceived += pose =>
        {
            if (ActiveBuildSession != null)
                RunInBuilder($"window.applyCoopPose && window.applyCoopPose({pose.ToString(Newtonsoft.Json.Formatting.None)});");
        };
    }

    /// <summary>
    /// Where this player stands, to everyone else on the session channel. A whisper rather than an API call: it is
    /// frequent, worthless a second later, and Reverb relays it without the platform doing any work.
    /// </summary>
    private void WhisperBuildPose(JObject message)
    {
        var me = CloudAuthManager.CurrentUser?.Id;
        if (_buildSessionChannel == null || string.IsNullOrEmpty(me)) return;
        if (DateTime.UtcNow - _buildPoseSentAt < BuildPoseMinInterval) return;
        if (message["p"] is not JArray { Count: 3 } position || message["yaw"]?.Type is not (JTokenType.Float or JTokenType.Integer)) return;

        _buildPoseSentAt = DateTime.UtcNow;
        _ = RealtimeClient.Shared.WhisperAsync(_buildSessionChannel, "pose",
            new JObject { ["u"] = me, ["p"] = position, ["yaw"] = message["yaw"] });
    }

    private void RunInBuilder(string script)
    {
        try { _ = _baseBuilderWebView?.CoreWebView2?.ExecuteScriptAsync(script); }
        catch (Exception ex) { AppendLog($"[Build Together] Could not reach the builder: {ex.Message}"); }
    }

    /// <summary>The workshop is going away, and the session cannot outlive it.</summary>
    private async Task LeaveBuildTogetherOnCloseAsync()
    {
        var hosted = _buildHostSession;
        var joined = BuildGuestSession;
        _buildHostSession = null;
        BuildGuestSession = null;
        _buildTogetherNotice = null;
        _buildGuestEnded = false;
        SyncBuildTogetherConnection();

        try
        {
            if (hosted != null) await BuildSessionApi.EndAsync(hosted.Id);
            else if (joined != null) await BuildSessionApi.LeaveAsync(joined.Id);
        }
        catch (Exception ex) { AppendLog($"[Build Together] Could not leave the session on close: {ex.Message}"); }
    }
}
