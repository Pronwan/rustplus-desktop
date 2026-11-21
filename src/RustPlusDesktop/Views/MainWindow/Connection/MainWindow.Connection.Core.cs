using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private bool _isSoftConnecting = false;
    private Task<bool>? _softConnectTask;
    private Task _serverSelectionResetTask = Task.CompletedTask;

    /// <summary>
    /// The server this app told the cloud it is driving, so it can hand it back.
    ///
    /// Releasing is an optimisation, not a requirement: the platform holds a short
    /// lease that expires on its own, which is what covers the case this app cannot
    /// report at all - being killed. Calling it turns a ninety-second gap in cover
    /// into a couple of seconds.
    /// </summary>
    private string? _cloudHeldServerKey;

    /// <summary>
    /// The profile the Rust+ socket actually connected to when the hold was taken.
    ///
    /// The lease is renewed only while this very server is still connected. Asking
    /// whether the *selected* server is connected is not the same question: the app
    /// can be online and connected to another server, and renewing then kept the
    /// cloud silent on a server this app had already left.
    /// </summary>
    private ServerProfile? _cloudHeldProfile;
    private DispatcherTimer? _cloudLeaseHeartbeatTimer;

    private static string CloudServerKeyOf(ServerProfile profile) => profile.ServerKey;

    private bool IsStillConnectedToHeldServer()
    {
        var profile = _cloudHeldProfile;
        var key = _cloudHeldServerKey;

        return profile != null
            && profile.IsConnected
            && !string.IsNullOrWhiteSpace(key)
            && string.Equals(CloudServerKeyOf(profile), key, StringComparison.OrdinalIgnoreCase);
    }

    private void StartCloudLeaseHeartbeatTimer()
    {
        _cloudLeaseHeartbeatTimer?.Stop();
        _cloudLeaseHeartbeatTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _cloudHeldMissedChecks = 0;
        _cloudLeaseHeartbeatTimer.Tick += (s, e) =>
        {
            if (IsStillConnectedToHeldServer())
            {
                _cloudHeldMissedChecks = 0;
                RenewCloudHold();
                return;
            }

            // A reconnect looks exactly like a disconnect for a few seconds. Handing the
            // server back on the first miss let the cloud answer during every blip, and both
            // answered once the socket came back. Two misses is still inside the lease
            // (2 x 30s < 90s), so the cloud never waits on a server nobody here is driving
            // for longer than the lease would have made it wait anyway.
            if (++_cloudHeldMissedChecks >= CloudHoldMissesBeforeRelease)
            {
                ReleaseCloudHold();
            }
        };
        _cloudLeaseHeartbeatTimer.Start();
    }

    private const int CloudHoldMissesBeforeRelease = 2;
    private int _cloudHeldMissedChecks;
    private int _cloudTakeoverFailures;
    private bool _cloudTakeoverRetryPending;

    /// <summary>
    /// Tell the cloud this app still drives the held server, retrying soon if that fails.
    ///
    /// A takeover that was skipped (cloud sign-in not ready yet) or refused leaves the cloud
    /// free to answer, and waiting for the next thirty-second tick is exactly the window in
    /// which both answered. So a failure retries after 5s, then 10s, then 20s, capped at the
    /// tick itself.
    /// </summary>
    private async void RenewCloudHold()
    {
        var key = _cloudHeldServerKey;
        if (string.IsNullOrWhiteSpace(key)) return;

        bool ok;
        try
        {
            ok = await Services.Cloud.CloudSessionsApi.TakeoverAsync(key);
        }
        catch
        {
            ok = false;
        }

        if (!string.Equals(key, _cloudHeldServerKey, StringComparison.OrdinalIgnoreCase)) return;

        if (ok)
        {
            _cloudTakeoverFailures = 0;
            return;
        }

        if (_cloudTakeoverRetryPending) return;

        var delaySeconds = Math.Min(5 * (1 << Math.Min(_cloudTakeoverFailures, 3)), 30);
        _cloudTakeoverFailures++;
        _cloudTakeoverRetryPending = true;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
        }
        finally
        {
            _cloudTakeoverRetryPending = false;
        }

        if (IsStillConnectedToHeldServer()
            && string.Equals(key, _cloudHeldServerKey, StringComparison.OrdinalIgnoreCase))
        {
            RenewCloudHold();
        }
    }

    private void StopCloudLeaseHeartbeatTimer()
    {
        _cloudLeaseHeartbeatTimer?.Stop();
        _cloudLeaseHeartbeatTimer = null;
    }

    /// <summary>Tell the cloud this app is now driving the server it just connected to.</summary>
    private void ClaimCloudHold(ServerProfile? connected)
    {
        var serverKey = connected == null ? null : CloudServerKeyOf(connected);

        if (connected == null || string.IsNullOrWhiteSpace(connected.Host) || connected.Port <= 0)
        {
            ReleaseCloudHold();
            return;
        }

        if (!string.Equals(_cloudHeldServerKey, serverKey, StringComparison.OrdinalIgnoreCase))
        {
            ReleaseCloudHold();
        }

        _cloudHeldServerKey = serverKey;
        _cloudHeldProfile = connected;
        _cloudTakeoverFailures = 0;
        RenewCloudHold();
        StartCloudLeaseHeartbeatTimer();
    }

    /// <summary>Hand the current server back to the cloud, if we claimed one.</summary>
    private void ReleaseCloudHold()
    {
        StopCloudLeaseHeartbeatTimer();
        var key = _cloudHeldServerKey;
        _cloudHeldProfile = null;
        if (string.IsNullOrWhiteSpace(key)) return;

        _cloudHeldServerKey = null;
        _ = Services.Cloud.CloudSessionsApi.ReleaseAsync(key);
    }

    /// <summary>
    /// The same hand-back, but awaited, for the one caller that cannot fire and
    /// forget: shutdown.
    ///
    /// Everywhere else the process carries on and an unawaited call completes on
    /// its own. On exit it does not — the process is gone before the request
    /// leaves — so closing the app would cost the full ninety-second lease at
    /// exactly the moment cover matters most, which is the moment the user walked
    /// away.
    ///
    /// Bounded, because a hand-back is an optimisation and the lease already
    /// covers us: an unreachable platform must not hold the window open.
    /// </summary>
    internal async Task ReleaseCloudHoldOnExitAsync()
    {
        StopCloudLeaseHeartbeatTimer();
        var key = _cloudHeldServerKey;
        if (string.IsNullOrWhiteSpace(key)) return;

        _cloudHeldServerKey = null;

        try
        {
            await Services.Cloud.CloudSessionsApi.ReleaseAsync(key)
                .WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // Timed out or refused. The lease expiring is the fallback this was
            // only ever shortening.
        }
    }

    private void UpdateFullConnectButtonsEnabled()
    {
        void Apply()
        {
            bool canFullConnect = !_vm.IsFullConnectRequested
                                  && _vm.Selected?.IsConnected == true
                                  && _vm.Selected?.IsFullConnected != true;
            UiBtnFullConnect.IsEnabled = canFullConnect;
            MapUiBtnFullConnect.IsEnabled = canFullConnect;
        }

        if (Dispatcher.CheckAccess()) Apply();
        else Dispatcher.Invoke(Apply);
    }

    private async Task EnsureWebView2Async()
    {
        try
        {
            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RustPlusDesk", "WebView2");
            Directory.CreateDirectory(dataFolder);

            // Pre-warm the CoreWebView2 environment in the background so that features
            // using embedded web views (such as the 3D Map and Genetics Lab) initialize
            // quickly when opened, without attaching a dummy control to the visual tree.
            await CoreWebView2Environment.CreateAsync(userDataFolder: dataFolder);
        }
        catch (Exception ex)
        {
            // Debug.WriteLine is compiled away in a release build, so a shipped app
            // would have said nothing at all. This is the earliest moment the
            // machine can tell us the embedded browser is unusable, and it is worth
            // hearing: every web view in the app fails afterwards, and the errors
            // they raise then are far less specific than this one.
            AppendLog($"[WebView2] Environment pre-warm failed: {Services.WebView2Diagnostics.Explain(ex)}");
        }
    }



    private async Task UpdateServerStatusAsync()
    {
        try
        {
            if (_rust is RustPlusClientReal real && _vm.Selected?.IsConnected == true)
            {
                var st = await real.GetServerStatusAsync();
                if (st != null && st.Players >= 0)
                {
                    _vm.ServerPlayers = $"{st.Players}/{st.MaxPlayers}";
                    _vm.ServerQueue = (st.Queue >= 0) ? st.Queue.ToString() : "0";
                    
                    if (!string.IsNullOrWhiteSpace(st.TimeString))
                        _vm.UpdateServerTime(st.TimeString, st.GameHours, st.Sunrise, st.Sunset, st.DayLengthMinutes, st.TimeScale);
                }
            }
        }
        catch
        {
            // leise weiter - der Poll laeuft einfach erneut
        }
    }



    private void BtnAddServer_Click(object sender, RoutedEventArgs e)
    {
        var host = Microsoft.VisualBasic.Interaction.InputBox("Server IP/Host:", "Server hinzufügen", "127.0.0.1");
        var portStr = Microsoft.VisualBasic.Interaction.InputBox("Companion-Port:", "Server hinzufügen", "28082");
        var token = Microsoft.VisualBasic.Interaction.InputBox("Player-Token (Rust+):", "Server hinzufügen", "");
        var proxy = Microsoft.VisualBasic.Interaction.InputBox("Facepunch-Proxy verwenden? (y/n)", "Server hinzufügen", "n");

        if (int.TryParse(portStr, out var port) && !string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(token))
        {
            var prof = new ServerProfile
            {
                Name = $"{host}:{port}",
                Host = host,
                Port = port,
                SteamId64 = _vm.SteamId64,
                PlayerToken = token,
                UseFacepunchProxy = proxy.Trim().ToLowerInvariant().StartsWith("y")
            };
            _vm.AddServer(prof);
            _vm.Save();
        }
        else
        {
            MessageBox.Show(RustPlusDesk.Properties.Resources.GetString("CodeUiUngültigeEingaben"), RustPlusDesk.Properties.Resources.GetString("CodeUiFehler"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ListServers_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        int selectionVersion = ++_mapSelectionVersion;
        if (_isDeletingServer) return;
        if (_isMap3DActive) CloseMap3DView();
        _lastChatTsForCurrentServer = null;
        BeginServerMapTransition();
        _vm.IsFullConnectRequested = false;
        CancelConnectionPolling();
        ResetMapDisplay();
        Task resetTask = _serverSelectionResetTask = ResetServerSelectionConnectionAsync(_serverSelectionResetTask, _softConnectTask);

        var cachedMapTask = LoadMapAsync(cacheOnly: true);
        await cachedMapTask;
        if (selectionVersion != _mapSelectionVersion) return;
        bool transitionCompleted = HasMapImage;
        if (transitionCompleted) await CompleteServerMapTransitionAsync(selectionVersion);
        await resetTask;
        if (selectionVersion != _mapSelectionVersion) return;

        if (_vm.Selected is { } prof)
        {
            if (!string.IsNullOrWhiteSpace(prof.SteamId64))
                _vm.SteamId64 = prof.SteamId64;

            bool isOfflineOnly = prof.Port == 0 || prof.PlayerToken == "offline";
            bool hasLocalMap = !string.IsNullOrEmpty(prof.LocalMapFilePath);

            if (hasLocalMap && isOfflineOnly)
            {
                await ShowOfflineMapAsync(prof);
            }
            else if (hasLocalMap && !HasMapImage)
            {
                if (!string.IsNullOrEmpty(prof.LocalMapImagePath) && File.Exists(prof.LocalMapImagePath))
                {
                    try
                    {
                        var bitmap = LoadLocalMapImage(prof.LocalMapImagePath);
                        ShowMapBasic(bitmap);

                        // Try to restore _worldSizeS from a previously-parsed map_data.json.
                        // Without this, _worldSizeS stays 0 and the heatmap/UV rect is wrong.
                        TryRestoreWorldSizeFromCachedMapData(prof, bitmap);
                        RedrawMapReferenceLayers();
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"[Offline Map] Failed to load local map image: {ex.Message}");
                    }
                }
                else
                {
                    ResetMapDisplay();
                }
            }

        }

        if (!transitionCompleted) await CompleteServerMapTransitionAsync(selectionVersion);
        if (selectionVersion != _mapSelectionVersion) return;
        HydrateSteamUiFromStorage();
        RegisterAllHotkeys();
        ActivateHotkeysForCurrentServer();
        UpdateRustMapsUi();

        if (!_vm.IsFullConnectRequested && _vm.Selected is { Port: > 0, IsConnected: false } selected
            && selected.PlayerToken != "offline")
            await PerformConnectDevicesOnlyAsync(selected);
    }

    private async Task ResetServerSelectionConnectionAsync(Task previousReset, Task<bool>? previousSoftConnect)
    {
        await previousReset;
        if (previousSoftConnect != null) await previousSoftConnect;
        await HardResetAsync(reconnect: false, keepMap: true);
    }

    private Task<bool> PerformConnectDevicesOnlyAsync(ServerProfile profile) =>
        _softConnectTask is { IsCompleted: false } ? _softConnectTask : _softConnectTask = RunConnectionAttemptsAsync(full: false);

    private async Task<bool> ConnectDevicesOnlyCoreAsync(ServerProfile profile)
    {
        if (profile == null) return false;
        StopTeamPolling();
        _statusCts?.Cancel();
        _statusTimer.Stop();
        _isSoftConnecting = true;
        _vm.IsSoftConnecting = true;
        RenewConnectionPolling();
        var ct = _connectionPollingCts.Token;
        void CheckConnection()
        {
            ct.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_vm.Selected, profile)) throw new OperationCanceledException();
        }
        try
        {
            AppendLog($"Soft-connecting to {profile.Name} (Devices Only)...");
            await _rust.ConnectAsync(profile, ct);
            CheckConnection();
            _connectedProfile = profile;
            profile.IsConnected = true;
            profile.IsFullConnected = false;
            profile.IsAccessDenied = false;
            UpdateFullConnectButtonsEnabled();

            if (_rust is RustPlusClientReal real)
            {
                real.EnsureEventsHooked();
                real.FeatureUnsupported -= OnFeatureUnsupported;
                real.FeatureUnsupported += OnFeatureUnsupported;
                real.StorageSnapshotReceived -= OnStorageSnapshot;
                real.StorageSnapshotReceived += OnStorageSnapshot;
                
                // Hook local handlers
                real.ConnectionLost -= OnConnectionLost;
                real.ConnectionLost += OnConnectionLost;
                real.TeamChatReceived -= Real_TeamChatReceived;
                real.TeamChatReceived += Real_TeamChatReceived;
                real.ClanChatReceived -= Real_ClanChatReceived;
                real.ClanChatReceived += Real_ClanChatReceived;
                real.ClanChanged -= OnClanChangedReal;
                real.ClanChanged += OnClanChangedReal;
                try { await real.PrimeTeamChatAsync(); }
                catch (Exception ex) { AppendLog("[chat] prime error: " + ex.Message); }
                CheckConnection();
                try { await real.PrimeClanChatAsync(); }
                catch (Exception ex) { AppendLog("[clan] prime error: " + ex.Message); }
                CheckConnection();

                // Discord interactions depend on team-master state, even during soft connect.
                await LoadTeamAsync();
                CheckConnection();
                await InitializeDevicesAsync(profile, reuseConnection: false);
                CheckConnection();
                StartTeamPolling();
            }
            
            // Start the server status loop (players/time) even for soft connect
            _statusCts?.Cancel();
            _statusCts = new CancellationTokenSource();
            _ = PollServerStatusLoopAsync(_statusCts.Token);

            // Start A2S player polling so the Players tab works on soft connect too
            TrackingService.StartPolling(profile.Host ?? "", profile.Port, profile.Name ?? "", profile.BattleMetricsId);
            _ = TrackingService.FetchOnlinePlayersNowAsync();

            _ = SearchRustMapsAsync(false);

            // A soft connection is still the desktop owner. Without this takeover
            // the cloud worker keeps its lease and both clients answer the same chat.
            var connectedKey = GetServerKey();
            ClaimCloudHold(profile);
            if (!string.IsNullOrWhiteSpace(connectedKey))
            {
                _ = Services.Cloud.CloudChatCommandSync.SyncAsync(profile, connectedKey);
                _ = Services.Cloud.CloudAutomationSync.SyncAsync(profile, connectedKey);
            }

            AppendLog("Soft-connect complete.");
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested || !ReferenceEquals(_vm.Selected, profile)) return false;
            RecordConnectionFailure(profile, ex);
            AppendLog($"Soft-connect failed: {ex.Message}");
            return false;
        }
        finally
        {
            _isSoftConnecting = false;
            _vm.IsSoftConnecting = false;
            _vm.IsDeviceSubscribePriming = false;
            Dispatcher.Invoke(() => {
                UiBtnConnect.IsEnabled = true;
                MapUiBtnConnect.IsEnabled = true;
            });
            UpdateFullConnectButtonsEnabled();
        }
    }

    private async Task PollServerStatusLoopAsync(CancellationToken ct)
    {
        int serverStatusFailCount = 0;
        while (!ct.IsCancellationRequested)
        {
            bool success = false;
            try
            {
                var vm = _vm!;
                var selected = vm.Selected;
                if (_rust is RustPlusClientReal real && selected != null && selected.IsConnected)
                {
                    var st = await real.GetServerStatusAsync(ct);
                    if (st != null && st.Players >= 0)
                    {
                        success = true;
                        serverStatusFailCount = 0;
                        vm.ServerPlayers = $"{st.Players}/{st.MaxPlayers}";
                        vm.ServerQueue = (st.Queue >= 0) ? st.Queue.ToString() : "0";
                        
                        if (!string.IsNullOrWhiteSpace(st.TimeString))
                            vm.UpdateServerTime(st.TimeString, st.GameHours, st.Sunrise, st.Sunset, st.DayLengthMinutes, st.TimeScale);
                    }
                }
            }
            catch { /* Keep last known values on error */ }

            var currentSelected = _vm!.Selected;
            if (currentSelected != null && currentSelected.IsConnected)
            {
                if (!success)
                {
                    serverStatusFailCount++;
                    AppendLog($"[status-poll] Failed to get server status ({serverStatusFailCount}/5).");

                    if (serverStatusFailCount >= 5)
                    {
                        serverStatusFailCount = 0;
                        if (!_isReconnecting && _connectionAttemptsActive == 0 && !_isSoftConnecting && !(_vm?.IsBusy == true) && !currentSelected.HasConnectionIssue)
                        {
                            AppendLog("[status-poll] Connection seems dead (status failed 5 times). Refreshing connection silently...");
                            
                            _ = Dispatcher.InvokeAsync(async () =>
                            {
                                try
                                {
                                    if (ct.IsCancellationRequested || !ReferenceEquals(_vm?.Selected, currentSelected)) return;
                                    bool recovered = currentSelected.IsFullConnected
                                        ? await PerformConnectAsync(true, showBusy: false)
                                        : await PerformConnectDevicesOnlyAsync(currentSelected);
                                    AppendLog(recovered ? "[status-poll] Silent connection refresh complete." : "[status-poll] Silent connection refresh failed.");
                                }
                                catch (Exception ex)
                                {
                                    AppendLog($"[status-poll] Silent connection refresh failed: {ex.Message}");
                                }
                            });
                        }
                    }
                }
            }
            else
            {
                serverStatusFailCount = 0;
            }

            try { await Task.Delay(TimeSpan.FromSeconds(10), ct); } catch { }
        }
    }

    /// <summary>
    /// Gives this server's notification rows that have no picture the server's own logo
    /// (its banner if it has none), the image Rust+ itself shows for the server.
    /// </summary>
    private async Task BackfillNotificationImagesAsync(string? host, int port)
    {
        if (string.IsNullOrWhiteSpace(host)) return;
        try
        {
            var info = await _rust.GetServerInfoAsync();
            var url = !string.IsNullOrWhiteSpace(info?.LogoImage) ? info!.LogoImage : info?.HeaderImage;
            if (!string.IsNullOrWhiteSpace(url))
            {
                NotificationCenterService.BackfillServerImage(host, port, url!);
            }
        }
        catch
        {
            // A missing picture is cosmetic; the rows keep their icon tile.
        }
    }

    private async Task PrimeChatAsync(Func<CancellationToken, Task> prime, string logTag)
    {
        try { await prime(default); }
        catch (Exception ex) { AppendLog($"{logTag} prime error: {ex.Message}"); }
    }

    internal Task<bool> PerformConnectAsync(bool silent, bool showBusy = true) =>
        RunConnectionAttemptsAsync(full: true, silent, showBusy);

    private async Task<bool> PerformConnectOnceAsync(bool silent, bool showBusy = true)
    {
        Services.Map.MemoryProbe.Mark("connect: start", restart: true);
        _ownCloudRestoreReady = false;
        var requestedProfile = _vm.Selected;
        int requestedSelectionVersion = _mapSelectionVersion;
        if (requestedProfile == null) return false;
        _vm.IsFullConnectRequested = true;
        if (showBusy && HasMapImage) _vm.IsConnectionLoading = false;

        await _serverSelectionResetTask;
        if (requestedSelectionVersion != _mapSelectionVersion || !ReferenceEquals(_vm.Selected, requestedProfile)) return false;

        if (_softConnectTask is { IsCompleted: false })
        {
            AppendLog("[connect] Soft-connect in progress, waiting for completion to prevent socket conflict...");
            if (_softConnectTask != null) await _softConnectTask;
            if (requestedSelectionVersion != _mapSelectionVersion || !ReferenceEquals(_vm.Selected, requestedProfile)) return false;
            AppendLog("[connect] Soft-connect finished. Proceeding with full connect.");
        }

        bool alreadySoftConnected = _vm.Selected != null && _vm.Selected.IsConnected && !_vm.Selected.IsFullConnected;
        _statusCts?.Cancel();
        _statusTimer.Stop();

        if (!silent)
        {
            if (alreadySoftConnected)
            {
                // Soft connect exists: Reset UI/polling states but do NOT call HardResetAsync (which disconnects)
                _shopTimer?.Stop();
                _shopTimer = null;
                StopDynPolling();
                StopTeamPolling();
                ReleaseCloudHold();
                TeamMembers.Clear();
                ClanMembers.Clear();
                _lastClanPoll = DateTime.MinValue;

                _avatarCache.Clear();
                _lastPresence.Clear();
                ClearAllDeathPins();
                ClearAllToggleBusy();
                _lastShops.Clear();
                _shopLifetimes.Clear();
                _knownShopIds.Clear();
                _initialShopSnapshotTimeUtc = DateTime.MinValue;
                _alertsNeedRebaseline = true;
                _lastChatSendUtc = DateTime.MinValue;

                foreach (var el in _shopEls.Values)
                    RemoveFromMapLayers(el);
                _shopEls.Clear();
            }
            else
            {
                await HardResetAsync(reconnect: false, keepMap: true);
                _shopTimer?.Stop();
                _shopTimer = null;
                StopDynPolling();
                StopTeamPolling();
                ReleaseCloudHold();
                TeamMembers.Clear();
                ClanMembers.Clear();
                _lastClanPoll = DateTime.MinValue;

                _avatarCache.Clear();
                _lastPresence.Clear();
                ClearAllDeathPins();
                ClearAllToggleBusy();
                _lastShops.Clear();
                _shopLifetimes.Clear();
                _knownShopIds.Clear();
                _initialShopSnapshotTimeUtc = DateTime.MinValue;
                _alertsNeedRebaseline = true;
                _lastChatSendUtc = DateTime.MinValue;

                foreach (var el in _shopEls.Values)
                    RemoveFromMapLayers(el);
                _shopEls.Clear();
            }
        }
        else
        {
            if (!alreadySoftConnected)
            {
                // Ensure the interface disconnects before recreating
                try { await _rust.DisconnectAsync(); } catch { }

                _shopTimer?.Stop();
                StopDynPolling(clearKnown: false);
                StopTeamPolling();
                ReleaseCloudHold();
                _alertsNeedRebaseline = true;
            }
        }

        if (requestedSelectionVersion != _mapSelectionVersion || !ReferenceEquals(_vm.Selected, requestedProfile)) return false;
        if (_vm.Selected is null)
        {
            if (!silent) ShowInfoSnackbar(Properties.Resources.SnackbarTitleConnection, Properties.Resources.PleaseSelectServerFirst, WpfUi.ControlAppearance.Info);
            return false;
        }

        RenewConnectionPolling();
        var connectingProfile = _vm.Selected!;
        var connectionToken = _connectionPollingCts.Token;
        void CheckConnection()
        {
            connectionToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_vm?.Selected, connectingProfile)) throw new OperationCanceledException();
        }
        ResetBuildingBlockedZonesForServerChange();
        ResetCargoPathForServerChange();
        ResetKeycardsForServerChange();

        try
        {
            if (showBusy)
            {
                _vm.IsConnectionLoading = !HasMapImage;
                _vm.IsBusy = !HasMapImage;
                _vm.BusyText = "Connecting …";
            }

            if (!alreadySoftConnected)
            {
                AppendLog($"Connecting to {_vm.Selected.Name} via Rust+ companion …");
                await _rust.ConnectAsync(connectingProfile, connectionToken);
                AppendLog("Connected.");
                Services.Map.MemoryProbe.Mark("connect: socket open");
            }
            else
            {
                AppendLog("Reusing existing soft-connection for full connect.");
            }
            CheckConnection();
            _connectedProfile = connectingProfile;
            _connectedProfile.IsConnected = true;
            _connectedProfile.IsFullConnected = false;

            // ConnectAsync returns once the server has answered a request, so the socket is ready.
            var real = _rust as RustPlusClientReal;
            if (real != null)
            {
                real.StorageSnapshotReceived -= OnStorageSnapshot;
                real.StorageSnapshotReceived += OnStorageSnapshot;
                real.ConnectionLost -= OnConnectionLost;
                real.ConnectionLost += OnConnectionLost;
                
                // Add global chat subscription for background bot commands
                real.TeamChatReceived -= Real_TeamChatReceived;
                real.TeamChatReceived += Real_TeamChatReceived;
                real.ClanChatReceived -= Real_ClanChatReceived;
                real.ClanChatReceived += Real_ClanChatReceived;
                real.ClanChanged -= OnClanChangedReal;
                real.ClanChanged += OnClanChangedReal;

                real.EnsureEventsHooked();
                real.FeatureUnsupported -= OnFeatureUnsupported;
                real.FeatureUnsupported += OnFeatureUnsupported;
            }

            _lastChatTsForCurrentServer = null;


            if (showBusy)
            {
                _vm.IsBusy = false;
                _vm.BusyText = "Loading map and team…";
                _vm.IsInitializing = true;
            }

            var mapTask = HasMapImage ? Task.CompletedTask : LoadMapAsync(cacheOnly: true);
            var teamTask = LoadTeamAsync();
            
            // Rehydrate local cache immediately (sync)
            if (!alreadySoftConnected) RehydrateDevicesFromStorageInto(_vm.Selected);

            RehydrateCamerasFromStorageInto(_vm.Selected);
            SwitchCameraSourceTo(_vm.Selected);
            
            ClearUserOverlayElements();
            _visibleOverlayOwners.Add(_mySteamId);

            // Restore saved subscriptions for this server profile
            if (_vm?.Selected != null)
            {
                var savedSubs = _vm.Selected.SubscribedTeammateSteamIds;
                if (savedSubs != null)
                {
                    foreach (var sid in savedSubs)
                    {
                        if (sid != _mySteamId)
                        {
                            _visibleOverlayOwners.Add(sid);
                        }
                    }
                }
            }
            // Async init: merge local + cloud overlays intelligently (no wipe risk)
            _ownCloudRestoreReady = false;
            _ = InitOwnOverlayAsync();


            await mapTask;
            Services.Map.MemoryProbe.Mark("connect: map loaded");
            if (showBusy)
            {
                _vm!.IsConnectionLoading = !HasMapImage;
                _vm.BusyText = "Synchronizing server data…";
            }

            await teamTask;
            CheckConnection();
            ShowInitialTeamPositions();
            if (showBusy && HasMapImage)
            {
                _vm!.IsConnectionLoading = false;
                _vm.BusyText = "";
            }
            await InitializeDevicesAsync(connectingProfile, reuseConnection: alreadySoftConnected);
            CheckConnection();
            await LoadMapAsync();
            CheckConnection();
            if (!HasMapImage) throw new InvalidOperationException("The server did not provide a map. Try connecting again.");
            if (!_showingInitialTeamPositions) ShowInitialTeamPositions();
            if (real != null)
            {
                await Task.WhenAll(
                    PrimeChatAsync(real.PrimeTeamChatAsync, "[chat]"),
                    PrimeChatAsync(real.PrimeClanChatAsync, "[clan]"));
            }
            CheckConnection();
            Services.Map.MemoryProbe.Mark("connect: server data loaded");
            
            // Rebuild team bar and subscription dock with loaded team data, and fetch restored teammate overlays
            await Dispatcher.InvokeAsync(() =>
            {
                RebuildOverlayTeamBar();
                UpdateSubscriptionDock();

                var restoredTeammates = _visibleOverlayOwners.Where(id => id != _mySteamId).ToList();
                foreach (var sid in restoredTeammates)
                {
                    _ = TryFetchOverlayForPlayerFromServerAsync(sid);
                }
            });
            
            if (TrackingService.AutoLoadShops && !Services.RustApiFeatures.EventsAndShopsRemoved)
                Dispatcher.Invoke(() => ChkShops.IsChecked = true);

            if (showBusy)
            {
                _vm!.IsInitializing = false;
                _vm.IsConnectionLoading = false;
                _vm.BusyText = "";
            }
            var connectedProfile = _vm!.Selected;
            if (connectedProfile == null)
                throw new InvalidOperationException("No selected server profile after connection initialization.");

            connectedProfile.IsConnected = true;
            connectedProfile.IsFullConnected = true;
            connectedProfile.IsAccessDenied = false;
            TrackingService.StartPolling(connectedProfile.Host ?? "", connectedProfile.Port, connectedProfile.Name ?? "", connectedProfile.BattleMetricsId);

            _vm.NotifyDevicesChanged();
            _ = SearchRustMapsAsync(false, connectedProfile.WipeTime);
            AppendLog($"Connection initialization complete. Server: {connectedProfile.Name}");
            Services.Map.MemoryProbe.Mark("connect: marked connected");

            _ = StartServerEventTrackingAsync();
            _ = Services.Social.SocialApi.UpdateActiveListingServerAsync(connectedProfile.Name);

            // Notification rows saved before pushes kept their picture show this server's logo.
            _ = BackfillNotificationImagesAsync(connectedProfile.Host, connectedProfile.Port);



            // Report what this server said about itself, so its cloud record carries
            // a name and map details rather than just the key it is filed under.
            _ = Services.Cloud.CloudServerInfo.ReportOnceAsync(
                GetServerKey(),
                connectedProfile.Name,
                _worldSizeS,
                connectedProfile.WipeTime);

            // Register the per-user pairing (encrypted player token) for this server so
            // it appears in the web dashboard and its smart devices are controllable
            // from the cloud. Previously this only happened via the Alexa link flow, so
            // servers paired after the cloud migration never became controllable.
            _ = Services.Cloud.CloudServerInfo.EnsurePairedOnceAsync(
                GetServerKey(),
                connectedProfile.Host,
                connectedProfile.Port,
                connectedProfile.Name,
                connectedProfile.PlayerToken,
                _mySteamId);

            // Cloud 24/7: this app is driving the server now, so the cloud should
            // stand down. Fire-and-forget on purpose - a failure costs at most one
            // duplicate connection until the next heartbeat, which is not worth
            // holding up a connect over, and the lease expires on its own anyway.
            var connectedKey = GetServerKey();
            ClaimCloudHold(connectedProfile);

            // Send the command words up so the cloud answers to exactly what this
            // user configured. Without it a teammate would get different replies
            // depending on whether this app happened to be running.
            if (!string.IsNullOrWhiteSpace(connectedKey))
            {
                _ = Services.Cloud.CloudChatCommandSync.SyncAsync(connectedProfile, connectedKey);
                // The rules the cloud runs while this app is closed (see CloudAutomationSync).
                _ = Services.Cloud.CloudAutomationSync.SyncAsync(connectedProfile, connectedKey);
            }

            _statusCts?.Cancel();
            _statusCts = new CancellationTokenSource();
            _ = PollServerStatusLoopAsync(_statusCts.Token);
            _statusTimer.Start();

            StartTeamPolling();
            StartDynPolling();
            Services.Map.MemoryProbe.Mark("connect: finished");
            if (_overlayToolsVisible)
            {
                RebuildOverlayTeamBar();
            }

        }
        catch (OperationCanceledException) when (connectionToken.IsCancellationRequested || !ReferenceEquals(_vm.Selected, connectingProfile))
        {
            if (showBusy && connectionToken == _connectionPollingCts.Token && ReferenceEquals(_vm.Selected, connectingProfile))
            {
                _vm.IsFullConnectRequested = false;
                _vm.IsInitializing = false;
                _vm.IsBusy = false;
                _vm.IsConnectionLoading = false;
                _vm.BusyText = "";
            }
            return false;
        }
        catch (Exception ex)
        {
            if (connectionToken.IsCancellationRequested || !ReferenceEquals(_vm.Selected, connectingProfile)) return false;
            _vm.IsFullConnectRequested = false;
            RecordConnectionFailure(connectingProfile, ex);
            if (showBusy)
            {
                _vm.IsInitializing = false;
                _vm.IsBusy = false;
                _vm.IsConnectionLoading = false;
                _vm.BusyText = "";
            }
            AppendLog("Error: " + ex.Message);
            return false;
        }

        return true;
    }

    private async Task<bool> EnsureConnectedAsync()
    {
        if (_vm.Selected is null) { AppendLog("No server selected."); return false; }
        if (_vm.Selected.IsConnected) return true;

        AppendLog($"Connecting to ws://{_vm.Selected.Host}:{_vm.Selected.Port} …");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        try
        {
            await _rust.ConnectAsync(_vm.Selected, cts.Token);
            _vm.Selected.IsConnected = true;
            _vm.Selected.IsAccessDenied = false;
            AppendLog("Connected.");
            return true;
        }
        catch (Exception ex)
        {
            RecordConnectionFailure(_vm.Selected, ex);
            AppendLog("Connect failed: " + ex.Message);
            return false;
        }
    }

    private void OnClanChangedReal(object? sender, EventArgs e)
    {
        _ = Dispatcher.InvokeAsync(LoadClanAsync);
    }
}
