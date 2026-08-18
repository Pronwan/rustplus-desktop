using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json.Linq;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Cloud;
using Supabase.Realtime;
using Supabase.Realtime.Models;
using Supabase.Realtime.PostgresChanges;

namespace RustPlusDesk.Services.Auth
{
    /// <summary>
    /// Live team sync: teammates' overlay/marker/device changes and master election.
    ///
    /// Two transports sit behind the same event handlers. Supabase Realtime discovers
    /// the current team by subscribing to postgres changes on the caller's presence
    /// row, then joins <c>team_sync:{serverKey}:{teamKey}</c>. The platform has no
    /// database-change feed, so the team is instead learned from the heartbeat
    /// response (which returns the resolved team id) and the client subscribes to the
    /// private channel <c>team-sync.{teamId}</c>. Both deliver the same event names
    /// and payload keys, so <see cref="HandleBroadcastEvent"/> is shared.
    /// </summary>
    public static class TeamSyncWebSocketService
    {
        private static RealtimeChannel? _broadcastChannel;
        private static RealtimeBroadcast<BaseBroadcast<JObject>>? _broadcast;
        private static RealtimeChannel? _presenceChannel;
        private static string _currentServerKey = "";
        private static string _currentTeamKey = "";
        private static bool _broadcastSubscribed;
        private static string? _subscribedBroadcastChannel;
        private static bool _hasBroadcastMasterState;
        private static string? _lastBroadcastMasterSteamId;
        private static readonly SemaphoreSlim BroadcastSubscriptionLock = new(1, 1);
        private static bool _initialized;

        // Platform realtime state.
        private static string? _currentTeamId;
        private static string? _realtimeChannel;
        private static bool _realtimeHandlerAttached;

        public static bool IsActive => CloudBackend.UsePlatform
            ? _realtimeChannel != null && RealtimeClient.Shared.IsSubscribed(_realtimeChannel)
            : _broadcastSubscribed;

        public static void Initialize()
        {
            if (SupabaseAuthManager.IsUpgradeRequiredSnackbarShown) return;
            if (_initialized) return;
            _initialized = true;

            if (CloudBackend.UsePlatform)
            {
                AttachRealtimeHandler();
                RealtimeClient.Shared.Start();
                AppendLog("[TeamSyncWS] Service initialized (realtime). Awaiting team heartbeat.");
                return;
            }

            _ = SubscribeToPresenceAsync();
            AppendLog("[TeamSyncWS] Service initialized (direct Supabase Realtime).");
        }

        public static void Shutdown()
        {
            _initialized = false;
            UnsubscribeAll();
            AppendLog("[TeamSyncWS] Service shut down.");
        }

        /// <summary>
        /// Called from the team-feature heartbeat once the server has resolved which
        /// team the local player is on. On cloud this is the only source of team
        /// identity — the heartbeat returns the team id that names the realtime channel.
        /// A no-op when the team has not changed, so it is safe to call every beat.
        /// </summary>
        public static void NotifyTeamResolved(string? teamId)
        {
            if (!CloudBackend.UsePlatform) return;
            if (string.IsNullOrWhiteSpace(teamId)) return;
            if (_currentTeamId == teamId && IsActive) return;

            _ = SubscribeToTeamChannelAsync(teamId);
        }

        private static void AttachRealtimeHandler()
        {
            if (_realtimeHandlerAttached) return;
            _realtimeHandlerAttached = true;

            RealtimeClient.Shared.EventReceived += (channel, eventName, data) =>
            {
                // Ignore traffic for a channel we have since moved off of.
                if (_realtimeChannel != null && channel != _realtimeChannel) return;

                try
                {
                    HandleBroadcastEvent(eventName, data);
                }
                catch (Exception ex)
                {
                    AppendLog($"[TeamSyncWS/Error] Realtime handler error: {ex.Message}");
                }
            };
        }

        private static async Task SubscribeToTeamChannelAsync(string teamId)
        {
            await BroadcastSubscriptionLock.WaitAsync();
            try
            {
                var channel = $"private-team-sync.{teamId}";
                if (_realtimeChannel == channel && RealtimeClient.Shared.IsSubscribed(channel))
                    return;

                if (_realtimeChannel != null && _realtimeChannel != channel)
                {
                    var previous = _realtimeChannel;
                    _realtimeChannel = null;
                    await RealtimeClient.Shared.UnsubscribeAsync(previous);
                    AppendLog($"[TeamSyncWS] Left team channel: {previous}");
                }

                // Master state from the previous team must not leak into the new one.
                _hasBroadcastMasterState = false;
                _lastBroadcastMasterSteamId = null;

                _currentTeamId = teamId;
                _realtimeChannel = channel;

                AttachRealtimeHandler();
                await RealtimeClient.Shared.SubscribeAsync(channel);
                AppendLog($"[TeamSyncWS] Subscribing to team channel: {channel}");
            }
            catch (Exception ex)
            {
                AppendLog($"[TeamSyncWS/Error] Failed to subscribe to team channel: {ex.Message}");
                _realtimeChannel = null;
                _currentTeamId = null;
            }
            finally
            {
                BroadcastSubscriptionLock.Release();
            }
        }

        private static void UnsubscribeRealtime()
        {
            var channel = _realtimeChannel;
            _realtimeChannel = null;
            _currentTeamId = null;
            _hasBroadcastMasterState = false;
            _lastBroadcastMasterSteamId = null;

            if (channel != null)
            {
                try { _ = RealtimeClient.Shared.UnsubscribeAsync(channel); } catch { }
            }

            RealtimeClient.Shared.Stop();
        }

        private static async Task SubscribeToPresenceAsync()
        {
            if (SupabaseAuthManager.IsUpgradeRequiredSnackbarShown) return;

            try
            {
                var steamId = TrackingService.SteamId64;
                if (string.IsNullOrWhiteSpace(steamId) || steamId == "0")
                {
                    await Task.Delay(5000);
                    _ = SubscribeToPresenceAsync();
                    return;
                }

                var client = SupabaseAuthManager.Client;
                if (client?.Realtime == null) return;

                _presenceChannel = client.Realtime.Channel($"user_presence:{steamId}");

                var options = new PostgresChangesOptions(
                    "public",
                    "team_feature_presence",
                    PostgresChangesOptions.ListenType.Updates,
                    $"steam_id=eq.{steamId}");
                _presenceChannel.Register(options);

                _presenceChannel.AddPostgresChangeHandler(
                    PostgresChangesOptions.ListenType.Updates,
                    (sender, change) =>
                    {
                        try
                        {
                            var row = change.Model<TeamFeaturePresenceModel>();
                            if (row == null || row.SteamId != steamId) return;

                            string newServerKey = row.ServerKey ?? "";
                            string newTeamKey = row.TeamKey ?? "";

                            if (!string.IsNullOrEmpty(newServerKey) && !string.IsNullOrEmpty(newTeamKey))
                            {
                                if (newServerKey != _currentServerKey || newTeamKey != _currentTeamKey)
                                {
                                    AppendLog($"[TeamSyncWS] Team changed: {_currentServerKey}/{_currentTeamKey} -> {newServerKey}/{newTeamKey}");
                                    _currentServerKey = newServerKey;
                                    _currentTeamKey = newTeamKey;
                                    _ = SubscribeToBroadcastAsync(newServerKey, newTeamKey);
                                }
                                else if (!_broadcastSubscribed)
                                {
                                    _ = SubscribeToBroadcastAsync(newServerKey, newTeamKey);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            AppendLog($"[TeamSyncWS/Error] Presence change handler error: {ex.Message}");
                        }
                    });

                await _presenceChannel.Subscribe();
                AppendLog($"[TeamSyncWS] Subscribed to presence changes for SteamID: {steamId}");

                _ = TryInitialBroadcastSubscriptionAsync(steamId);
            }
            catch (Exception ex)
            {
                AppendLog($"[TeamSyncWS/Error] Failed to subscribe to presence: {ex.Message}");
                await Task.Delay(5000);
                _ = SubscribeToPresenceAsync();
            }
        }

        private static async Task TryInitialBroadcastSubscriptionAsync(string steamId)
        {
            try
            {
                var client = SupabaseAuthManager.Client;
                if (client == null) return;

                var response = await client
                    .From<TeamFeaturePresenceModel>()
                    .Where(x => x.SteamId == steamId)
                    .Get();

                var row = response?.Models?.FirstOrDefault();
                if (row != null &&
                    !string.IsNullOrEmpty(row.ServerKey) &&
                    !string.IsNullOrEmpty(row.TeamKey))
                {
                    _currentServerKey = row.ServerKey;
                    _currentTeamKey = row.TeamKey;
                    await SubscribeToBroadcastAsync(row.ServerKey, row.TeamKey);
                }
            }
            catch
            {
                // Not found or RLS blocked - wait for heartbeat-driven presence update
            }
        }

        private static async Task SubscribeToBroadcastAsync(string serverKey, string teamKey)
        {
            if (SupabaseAuthManager.IsUpgradeRequiredSnackbarShown) return;
            if (string.IsNullOrEmpty(serverKey) || string.IsNullOrEmpty(teamKey)) return;

            var channelName = $"team_sync:{serverKey}:{teamKey}";
            await BroadcastSubscriptionLock.WaitAsync();
            try
            {
                if (_broadcastSubscribed && _subscribedBroadcastChannel == channelName)
                    return;

                UnsubscribeBroadcast();

                var client = SupabaseAuthManager.Client;
                if (client?.Realtime == null) return;

                _broadcastChannel = client.Realtime.Channel(channelName);

                _broadcast = _broadcastChannel.Register<BaseBroadcast<JObject>>();
                _broadcast.AddBroadcastEventHandler((sender, args) =>
                {
                    try
                    {
                        var message = _broadcast?.Current();
                        if (message == null) return;

                        HandleBroadcastEvent(message.Event, message.Payload);
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"[TeamSyncWS/Error] Broadcast handler error: {ex.Message}");
                    }
                });

                await _broadcastChannel.Subscribe();
                _broadcastSubscribed = true;
                _subscribedBroadcastChannel = channelName;
                AppendLog($"[TeamSyncWS] Subscribed to broadcast channel: {channelName}");
            }
            catch (Exception ex)
            {
                AppendLog($"[TeamSyncWS/Error] Failed to subscribe to broadcast: {ex.Message}");

                // Drop it properly rather than only letting go of our reference. A channel left
                // behind in the client's registry is handed back to the next attempt, which
                // then fails the same way — which is how one failure turned into a dead clan
                // chat for the rest of the session.
                if (_broadcastChannel != null) DropChannel(_broadcastChannel);
                _broadcastChannel = null;
                _broadcast = null;
                _broadcastSubscribed = false;
                _subscribedBroadcastChannel = null;
            }
            finally
            {
                BroadcastSubscriptionLock.Release();
            }
        }

        private static void UnsubscribeBroadcast()
        {
            _broadcastSubscribed = false;
            _subscribedBroadcastChannel = null;
            _hasBroadcastMasterState = false;
            _lastBroadcastMasterSteamId = null;
            if (_broadcastChannel != null)
            {
                DropChannel(_broadcastChannel);
                _broadcastChannel = null;
                _broadcast = null;
            }
        }

        private static void UnsubscribePresence()
        {
            if (_presenceChannel != null)
            {
                DropChannel(_presenceChannel);
                _presenceChannel = null;
            }
        }

        /// <summary>
        /// Unsubscribes and then takes the channel out of the Realtime client entirely.
        ///
        /// Unsubscribe alone is not enough: the client keeps channels in a registry keyed by
        /// topic, so a later Channel(sameName) hands back the very same object — and Register
        /// may only be called on a channel once. Reconnecting to a server already used in this
        /// session then failed with "Register can only be called with broadcast options for a
        /// channel once", and stayed broken until the app was restarted, because every retry
        /// received the same stale channel.
        /// </summary>
        private static void DropChannel(RealtimeChannel channel)
        {
            try { channel.Unsubscribe(); } catch { }
            try { SupabaseAuthManager.Client?.Realtime?.Remove(channel); } catch { }
        }

        private static void UnsubscribeAll()
        {
            if (CloudBackend.UsePlatform)
            {
                UnsubscribeRealtime();
                return;
            }

            UnsubscribeBroadcast();
            UnsubscribePresence();
        }

        private static void HandleBroadcastEvent(string? eventName, JObject? payload)
        {
            if (string.IsNullOrEmpty(eventName) || payload == null) return;

            var mySteamId = TrackingService.SteamId64;

            switch (eventName)
            {
                case "overlay_changed":
                case "markers_changed":
                case "devices_changed":
                    string? senderSteamId = payload["steam_id"]?.ToString();
                    if (!string.IsNullOrEmpty(senderSteamId) && senderSteamId != mySteamId)
                    {
                        if (ulong.TryParse(senderSteamId, out ulong sid))
                        {
                            AppendLog($"[TeamSyncWS] {eventName} event for teammate: {sid}");
                            _ = RefreshOverlayAsync(sid);
                        }
                    }
                    break;

                case "overlay_data":
                    string? ovSteamId = payload["steam_id"]?.ToString();
                    if (!string.IsNullOrEmpty(ovSteamId) && ovSteamId != mySteamId)
                    {
                        if (ulong.TryParse(ovSteamId, out ulong ovSid))
                        {
                            string? ovServerKey = payload["server_key"]?.ToString();
                            string? ovData = payload["overlay_data"]?.ToString();
                            string? mkData = payload["marker_data"]?.ToString();
                            string? dvData = payload["device_data"]?.ToString();
                            long ovUpdatedAt = 0;
                            var updatedAtToken = payload["updated_at"];
                            if (updatedAtToken != null)
                            {
                                if (updatedAtToken.Type == JTokenType.Integer)
                                    ovUpdatedAt = updatedAtToken.Value<long>();
                                else if (updatedAtToken.Type == JTokenType.Date)
                                    ovUpdatedAt = new DateTimeOffset(updatedAtToken.Value<DateTime>()).ToUnixTimeMilliseconds();
                                else if (long.TryParse(updatedAtToken.ToString(), out long parsed))
                                    ovUpdatedAt = parsed;
                            }

                            AppendLog($"[TeamSyncWS] overlay_data inline event for teammate: {ovSid}");
                            _ = ApplyInlineOverlayAsync(ovSid, ovServerKey, ovData, mkData, dvData, ovUpdatedAt);
                        }
                    }
                    break;

                case "master_changed":
                    var statePayload = payload["state"];
                    if (statePayload != null)
                    {
                        TeamFeatureMasterState? state = null;
                        try
                        {
                            if (statePayload is JArray arr)
                            {
                                state = arr.FirstOrDefault()?.ToObject<TeamFeatureMasterState>();
                            }
                            else if (statePayload is JObject obj)
                            {
                                state = obj.ToObject<TeamFeatureMasterState>();
                            }
                        }
                        catch { }

                        if (_hasBroadcastMasterState && state?.MasterSteamId == _lastBroadcastMasterSteamId)
                            break;

                        // Guard against stale "no master" broadcast overwriting our own fresh heartbeat claim.
                        // This happens on full connect: the channel fires current DB state before our heartbeat
                        // has written the new master row. We skip it if WE are currently master and the broadcast
                        // says the slot is empty — the heartbeat timer will sync reality within ≤60 s.
                        var hasActiveMasterInBroadcast = state != null
                            && !string.IsNullOrWhiteSpace(state.MasterSteamId)
                            && (!state.ExpiresAt.HasValue || state.ExpiresAt.Value.ToUniversalTime() > DateTime.UtcNow);

                        if (!hasActiveMasterInBroadcast)
                        {
                            // Check if we currently hold master – if so, ignore this stale empty broadcast.
                            bool weAreMaster = false;
                            if (Application.Current != null)
                            {
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    if (Application.Current.MainWindow is Views.MainWindow mainWin)
                                        weAreMaster = mainWin.IsChatFeatureMasterPublic;
                                });
                            }
                            if (weAreMaster)
                            {
                                AppendLog($"[TeamSyncWS] Ignoring empty master_changed broadcast — we are active master (stale event on channel join).");
                                break;
                            }
                        }

                        _hasBroadcastMasterState = true;
                        _lastBroadcastMasterSteamId = state?.MasterSteamId;

                        AppendLog($"[TeamSyncWS] Master changed event. Active Master: {state?.MasterSteamId}");
                        _ = ApplyMasterStateAsync(state);
                    }
                    break;

                case "presence_changed":
                    // On cloud the channel follows the team id from the heartbeat, so
                    // there is nothing to re-derive here; only Supabase needs to switch
                    // channels off the presence row.
                    if (CloudBackend.UsePlatform) break;

                    string? presenceSteamId = payload["steam_id"]?.ToString();
                    if (presenceSteamId == mySteamId)
                    {
                        string? newServer = payload["server_key"]?.ToString();
                        string? newTeam = payload["team_key"]?.ToString();
                        if (!string.IsNullOrEmpty(newServer) && !string.IsNullOrEmpty(newTeam) &&
                            (newServer != _currentServerKey || newTeam != _currentTeamKey))
                        {
                            _currentServerKey = newServer;
                            _currentTeamKey = newTeam;
                            _ = SubscribeToBroadcastAsync(newServer, newTeam);
                        }
                    }
                    break;
            }
        }

        private static async Task RefreshOverlayAsync(ulong steamId)
        {
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                if (Application.Current.MainWindow is Views.MainWindow mainWin)
                {
                    await mainWin.RefreshTeammateOverlayAsync(steamId);
                }
            });
        }

        private static async Task ApplyInlineOverlayAsync(ulong steamId, string? serverKey, string? overlayDataJson, string? markerDataJson, string? deviceDataJson, long updatedAt)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (Application.Current.MainWindow is Views.MainWindow mainWin)
                {
                    mainWin.ApplyInlineOverlayData(
                        steamId,
                        serverKey ?? "",
                        overlayDataJson ?? "",
                        markerDataJson,
                        deviceDataJson,
                        updatedAt);
                }
            });
        }

        private static async Task ApplyMasterStateAsync(TeamFeatureMasterState? state)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (Application.Current.MainWindow is Views.MainWindow mainWin)
                {
                    mainWin.ApplyTeamFeatureMasterState(state, mainWin.BuildTeamFeatureKey());
                }
            });
        }

        private static void AppendLog(string msg)
        {
            if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Application.Current.MainWindow is Views.MainWindow mainWin)
                    {
                        mainWin.AppendLog(msg);
                    }
                });
            }
        }
    }
}
