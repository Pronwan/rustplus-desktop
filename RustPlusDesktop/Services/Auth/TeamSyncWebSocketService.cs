using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using RustPlusDesk.Helpers;
using RustPlusDesk.Services.Data;

namespace RustPlusDesk.Services.Auth
{
    public static class TeamSyncWebSocketService
    {
        private static ClientWebSocket? _webSocket;
        private static CancellationTokenSource? _cts;
        private static Task? _loopTask;
        private static int _reconnectDelaySeconds = 2;
        private static readonly object LockObj = new();
        private static System.Threading.Timer? _pingTimer;

        public static bool IsActive { get; private set; }

        public static void Initialize()
        {
            lock (LockObj)
            {
                if (_loopTask != null) return; // Already running
                _cts = new CancellationTokenSource();
                _loopTask = Task.Run(() => ConnectionLoopAsync(_cts.Token));
                AppendLog("[TeamSyncWS] Service initialized.");
            }
        }

        public static void Shutdown()
        {
            lock (LockObj)
            {
                if (_loopTask == null) return;
                _cts?.Cancel();
                _pingTimer?.Dispose();
                _pingTimer = null;
                CloseWebSocketAsync().Wait();
                _loopTask = null;
                IsActive = false;
                AppendLog("[TeamSyncWS] Service shut down.");
            }
        }

        private static async Task ConnectionLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    bool shouldBeConnected = SupabaseAuthManager.IsAuthenticated &&
                                             SupabaseAuthManager.Client?.Auth?.CurrentSession != null;

                    if (shouldBeConnected && (_webSocket == null || _webSocket.State != WebSocketState.Open))
                    {
                        if (_webSocket == null || _webSocket.State == WebSocketState.Aborted || _webSocket.State == WebSocketState.Closed)
                        {
                            await ConnectAsync(ct);
                        }
                    }
                    else if (!shouldBeConnected && _webSocket != null && _webSocket.State == WebSocketState.Open)
                    {
                        await CloseWebSocketAsync();
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"[TeamSyncWS/Error] Connection loop exception: {ex.Message}");
                }

                await Task.Delay(2000, ct);
            }
        }

        private static async Task ConnectAsync(CancellationToken ct)
        {
            string? token = SupabaseAuthManager.Client?.Auth?.CurrentSession?.AccessToken;
            if (string.IsNullOrEmpty(token)) return;

            string rawUrl = DataManager.SUPABASE_URL;
            if (string.IsNullOrEmpty(rawUrl)) return;

            string wsUrl = rawUrl.Replace("https://", "wss://").TrimEnd('/') +
                            "/functions/v1/team-sync?jwt=" + Uri.EscapeDataString(token) +
                            "&version=" + Uri.EscapeDataString(VersionHelper.GetClientVersion());

            AppendLog($"[TeamSyncWS] Connecting to {rawUrl.Replace("https://", "wss://").TrimEnd('/')}/functions/v1/team-sync...");
            
            try
            {
                _webSocket = new ClientWebSocket();
                _webSocket.Options.SetRequestHeader("x-client-version", VersionHelper.GetClientVersion());
                
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                
                await _webSocket.ConnectAsync(new Uri(wsUrl), linkedCts.Token);
                
                _reconnectDelaySeconds = 2; // Reset backoff on success
                IsActive = true;
                AppendLog("[TeamSyncWS] Connected successfully!");

                // Start ping timer every 30 seconds
                _pingTimer?.Dispose();
                _pingTimer = new System.Threading.Timer(async _ =>
                {
                    await SendPingAsync();
                }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

                // Start receiving messages
                _ = Task.Run(() => ReceiveLoopAsync(_webSocket, ct), ct);
            }
            catch (Exception ex)
            {
                AppendLog($"[TeamSyncWS/Error] Connection failed: {ex.Message}. Reconnecting in {_reconnectDelaySeconds}s...");
                IsActive = false;
                _webSocket?.Dispose();
                _webSocket = null;
                
                // Backoff delay
                await Task.Delay(TimeSpan.FromSeconds(_reconnectDelaySeconds), ct);
                _reconnectDelaySeconds = Math.Min(_reconnectDelaySeconds * 2, 30);
            }
        }

        private static async Task SendPingAsync()
        {
            if (_webSocket == null || _webSocket.State != WebSocketState.Open) return;
            try
            {
                var payload = new { @event = "ping" };
                string json = JsonSerializer.Serialize(payload);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                AppendLog($"[TeamSyncWS/Error] Failed to send ping: {ex.Message}");
            }
        }

        private static async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
        {
            var buffer = new byte[8192];
            try
            {
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        AppendLog("[TeamSyncWS] Server requested connection close.");
                        await CloseWebSocketAsync();
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        await HandleMessageAsync(message);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    AppendLog($"[TeamSyncWS/Error] Receive loop error: {ex.Message}");
                }
            }
            finally
            {
                IsActive = false;
            }
        }

        private static async Task HandleMessageAsync(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("event", out var evProp))
                {
                    string ev = evProp.GetString() ?? "";
                    if (ev == "overlay_changed")
                    {
                        if (root.TryGetProperty("steam_id", out var sidProp))
                        {
                            string sidStr = sidProp.GetString() ?? "";
                            if (ulong.TryParse(sidStr, out ulong steamId))
                            {
                                AppendLog($"[TeamSyncWS] Overlay changed event for teammate: {steamId}");
                                await RefreshOverlayAsync(steamId);
                            }
                        }
                    }
                    else if (ev == "master_changed")
                    {
                        if (root.TryGetProperty("state", out var stateProp))
                        {
                            RustPlusDesk.Models.TeamFeatureMasterState? state = null;
                            if (stateProp.ValueKind == JsonValueKind.Array)
                            {
                                var list = JsonSerializer.Deserialize<List<RustPlusDesk.Models.TeamFeatureMasterState>>(
                                    stateProp.GetRawText(),
                                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                                );
                                state = list?.FirstOrDefault();
                            }
                            else if (stateProp.ValueKind == JsonValueKind.Object)
                            {
                                state = JsonSerializer.Deserialize<RustPlusDesk.Models.TeamFeatureMasterState>(
                                    stateProp.GetRawText(),
                                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                                );
                            }

                            AppendLog($"[TeamSyncWS] Master changed event received. Active Master: {state?.MasterSteamId}");
                            await ApplyMasterStateAsync(state);
                        }
                    }
                    else if (ev == "subscribed")
                    {
                        string teamKey = root.TryGetProperty("team_key", out var tk) ? tk.GetString() ?? "" : "";
                        string serverKey = root.TryGetProperty("server_key", out var sk) ? sk.GetString() ?? "" : "";
                        AppendLog($"[TeamSyncWS] Subscribed to Realtime channel for team={teamKey} server={serverKey}");
                    }
                    else if (ev == "pong")
                    {
                        // Ping reply, do nothing
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[TeamSyncWS/Error] Failed to parse message: {ex.Message}");
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

        private static async Task ApplyMasterStateAsync(RustPlusDesk.Models.TeamFeatureMasterState? state)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (Application.Current.MainWindow is Views.MainWindow mainWin)
                {
                    // Call the public method in MainWindow
                    mainWin.ApplyTeamFeatureMasterState(state, mainWin.BuildTeamFeatureKey());
                }
            });
        }

        private static async Task CloseWebSocketAsync()
        {
            IsActive = false;
            _pingTimer?.Dispose();
            _pingTimer = null;

            if (_webSocket != null)
            {
                if (_webSocket.State == WebSocketState.Open || _webSocket.State == WebSocketState.CloseReceived)
                {
                    try
                    {
                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutting down", timeoutCts.Token);
                    }
                    catch { }
                }
                _webSocket.Dispose();
                _webSocket = null;
                AppendLog("[TeamSyncWS] Connection closed.");
            }
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
