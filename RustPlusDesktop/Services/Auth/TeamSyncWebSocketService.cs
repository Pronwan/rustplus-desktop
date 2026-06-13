using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json.Linq;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;
using Supabase.Realtime;
using Supabase.Realtime.Models;
using Supabase.Realtime.PostgresChanges;

namespace RustPlusDesk.Services.Auth
{
    public static class TeamSyncWebSocketService
    {
        private static RealtimeChannel? _broadcastChannel;
        private static RealtimeBroadcast<BaseBroadcast<JObject>>? _broadcast;
        private static RealtimeChannel? _presenceChannel;
        private static string _currentServerKey = "";
        private static string _currentTeamKey = "";
        private static bool _broadcastSubscribed;
        private static bool _initialized;

        public static bool IsActive => _broadcastSubscribed;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            _ = SubscribeToPresenceAsync();
            AppendLog("[TeamSyncWS] Service initialized (direct Supabase Realtime).");
        }

        public static void Shutdown()
        {
            _initialized = false;
            UnsubscribeAll();
            AppendLog("[TeamSyncWS] Service shut down.");
        }

        private static async Task SubscribeToPresenceAsync()
        {
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
            if (string.IsNullOrEmpty(serverKey) || string.IsNullOrEmpty(teamKey)) return;

            UnsubscribeBroadcast();

            try
            {
                var client = SupabaseAuthManager.Client;
                if (client?.Realtime == null) return;

                var channelName = $"team_sync:{serverKey}:{teamKey}";
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
                AppendLog($"[TeamSyncWS] Subscribed to broadcast channel: {channelName}");
            }
            catch (Exception ex)
            {
                AppendLog($"[TeamSyncWS/Error] Failed to subscribe to broadcast: {ex.Message}");
                _broadcastChannel = null;
                _broadcast = null;
                _broadcastSubscribed = false;
            }
        }

        private static void UnsubscribeBroadcast()
        {
            _broadcastSubscribed = false;
            if (_broadcastChannel != null)
            {
                try { _broadcastChannel.Unsubscribe(); } catch { }
                _broadcastChannel = null;
                _broadcast = null;
            }
        }

        private static void UnsubscribePresence()
        {
            if (_presenceChannel != null)
            {
                try { _presenceChannel.Unsubscribe(); } catch { }
                _presenceChannel = null;
            }
        }

        private static void UnsubscribeAll()
        {
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
                            long ovUpdatedAt = payload["updated_at"]?.Value<long>() ?? 0;

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

                        AppendLog($"[TeamSyncWS] Master changed event. Active Master: {state?.MasterSteamId}");
                        _ = ApplyMasterStateAsync(state);
                    }
                    break;

                case "presence_changed":
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
