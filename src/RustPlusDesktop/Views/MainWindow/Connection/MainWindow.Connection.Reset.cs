using RustPlusDesk.Models;
using RustPlusDesk.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private ServerProfile? _connectedProfile;
    private DispatcherTimer? _storageTimer;
    private bool _isReconnecting = false;
    private CancellationTokenSource _connectionPollingCts = new();

    private void CancelConnectionPolling() => _connectionPollingCts.Cancel();

    private void RenewConnectionPolling()
    {
        _connectionPollingCts.Cancel();
        _connectionPollingCts.Dispose();
        _connectionPollingCts = new CancellationTokenSource();
    }

    private async Task HardResetAsync(bool reconnect = false, bool keepMap = false)
    {
        Services.Map.MemoryProbe.Mark("disconnect: start", restart: true);
        _connectedProfile = null;

        // Leaving the server for good (e.g. picking another one in the list) means
        // this app no longer drives it, so the cloud gets it back now. A reconnect
        // keeps the hold: the lease timer releases it if the reconnect never lands.
        if (!reconnect)
            ReleaseCloudHold();
        if (!keepMap)
        {
            _connectionRecoveryVersion++;
            _vm.IsFullConnectRequested = false;
            foreach (var profile in _vm.Servers)
            {
                profile.ConnectionFailure = null;
                profile.IsReconnecting = false;
                profile.WantsFullConnection = false;
            }
        }

        // 1) Laufende Polls/Tokens abbrechen
        CancelConnectionPolling();
        try { StopDynPolling(clearKnown: !reconnect); } catch { }
        try { StopTeamPolling(); } catch { }

        // Falls du eigene CTS für Status hast:
        try
        {
            _statusCts?.Cancel();
            _statusCts = null;
        }
        catch { }

        // 2) Timer stoppen
        try { StopCloudLeaseHeartbeatTimer(); } catch { }
        try { _statusTimer?.Stop(); } catch { }
        try { _shopTimer?.Stop(); _shopTimer = null; } catch { }
        try { _storageTimer?.Stop(); _storageTimer = null; } catch { }
        try { Dispatcher.Invoke(() => ChkShops.IsChecked = false); } catch { }

        // 3) UI-/In-Memory-State leeren
        try { TeamMembers.Clear(); } catch { }
        try { ClanMembers.Clear(); } catch { }
        try { _lastClanPoll = DateTime.MinValue; } catch { }
        try { _avatarCache.Clear(); } catch { }
        try { _lastPresence.Clear(); } catch { }
        try { ClearAllDeathPins(); } catch { }
        try { ClearAllToggleBusy(); } catch { }
        try { ResetAllBusyStates(); } catch { }

        // Shopspezifisch, wie du es im Connect auch machst
        try { _lastShops.Clear(); } catch { }
        try { _shopLifetimes.Clear(); } catch { }
        try { _knownShopIds.Clear(); } catch { }
        _firstShopPollDone = false;
        try { ResetShopDataAvailability(); } catch { }
        _initialShopSnapshotTimeUtc = DateTime.MinValue;
        _alertsNeedRebaseline = true;
        _lastChatSendUtc = DateTime.MinValue;

        // 4) Overlay-Elemente wirklich vom Canvas runternehmen
        try
        {
            foreach (var el in _shopEls.Values)
                RemoveFromMapLayers(el);
            _shopEls.Clear();
        }
        catch { }

        // Wenn du noch player-overlays / team-overlays hast:
        try
        {
            ClearUserOverlayElements();   // du hast das im Connect schon – nutzen!
            StopOverlayPollTimer();        // Live-Polling Timer stoppen
        }
        catch { }


        // 6) ViewModel "entkoppeln"
        if (_vm != null)
        {
            foreach (var profile in _vm.Servers)
            {
                profile.IsConnected = false;
                profile.IsFullConnected = false;
            }
            _vm.IsBusy = false;
            _vm.BusyText = "";
        }

        if (!keepMap) ResetMapDisplay();
        Services.Map.MemoryProbe.Mark(keepMap ? "disconnect: map retained" : "disconnect: map cleared");

        // After the UI on purpose: a socket can take seconds to close, and nothing above needs it.
        // 5) WIRKLICH vom Rust-Server trennen
        try
        {
            if (_rust != null)
                await _rust.DisconnectAsync();   // RustPlusClientReal trennt hier sauber und setzt _api = null
                Services.Map.MemoryProbe.Mark("disconnect: socket closed");
        }
        catch { }

        AppendLog("Hard reset completed.");

        // 7) Optional: direkt wieder verbinden
        if (reconnect && _vm?.Selected != null)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                // wir rufen deine bestehende Logik wieder auf
                BtnConnect_Click(this, new RoutedEventArgs());
            });
        }
    }

    public async Task PerformGranularResetAsync(
        bool connection, 
        bool profiles, 
        bool steam, 
        bool pairing, 
        bool crosshairs, 
        bool cache)
    {
        AppendLog(Properties.Resources.WipeLogStart);

        // 1. Connection reset
        if (connection)
        {
            AppendLog(Properties.Resources.WipeLogConnStart);
            await HardResetAsync(reconnect: false);
            AppendLog(Properties.Resources.WipeLogConnEnd);
        }

        // 2. Clear servers
        if (profiles)
        {
            AppendLog(Properties.Resources.WipeLogProfilesStart);
            _vm.Servers.Clear();
            _vm.Save();
            AppendLog(Properties.Resources.WipeLogProfilesEnd);
        }

        // 3. Clear Steam Credentials
        if (steam)
        {
            AppendLog(Properties.Resources.WipeLogSteamStart);
            _vm.SteamId64 = "";
            TrackingService.SteamId64 = "";
            HydrateSteamUiFromStorage();
            AppendLog(Properties.Resources.WipeLogSteamEnd);
        }

        // 4. Wipe Pairing Config
        if (pairing)
        {
            AppendLog(Properties.Resources.WipeLogPairingStart);
            await ResetPairingConfigAsync(stopListenerFirst: true);
            AppendLog(Properties.Resources.WipeLogPairingEnd);
        }

        // 5. Custom Crosshairs
        if (crosshairs)
        {
            AppendLog(Properties.Resources.WipeLogCrosshairsStart);
            try
            {
                RustPlusDesk.Services.Data.CrosshairDataModule.SaveCrosshairs(new System.Collections.Generic.List<CustomCrosshair>());
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(Properties.Resources.WipeLogCrosshairsError, ex.Message));
            }
            AppendLog(Properties.Resources.WipeLogCrosshairsEnd);
        }

        // 6. Local Cache & Drawings
        if (cache)
        {
            AppendLog(Properties.Resources.WipeLogCacheStart);
            try
            {
                if (System.IO.Directory.Exists(RustPlusDesk.Services.Data.DataManager.CacheDir))
                {
                    System.IO.Directory.Delete(RustPlusDesk.Services.Data.DataManager.CacheDir, true);
                }
                
                var overlaysDir = System.IO.Path.Combine(RustPlusDesk.Services.Data.DataManager.AppDir, "Overlays");
                if (System.IO.Directory.Exists(overlaysDir))
                {
                    System.IO.Directory.Delete(overlaysDir, true);
                }
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(Properties.Resources.WipeLogCacheError, ex.Message));
            }
            AppendLog(Properties.Resources.WipeLogCacheEnd);
        }

        // If pairing config was wiped, restart the listener to re-register/hydrate UI cleanly
        if (pairing)
        {
            HydrateSteamUiFromStorage();
            AppendLog(Properties.Resources.WipeLogPairingRestart);
            _ = StartPairingListenerUiAsync();
        }

        AppendLog(Properties.Resources.WipeLogComplete);
    }

    private bool _connectionActionPending;

    private void BtnConnectionOptions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private async void BtnConnectDevicesOnly_Click(object sender, RoutedEventArgs e)
    {
        if (_connectionActionPending || _isSoftConnecting || _vm.IsConnectionLoading || _vm.IsInitializing
            || _vm.Selected is not { IsConnected: false, Port: > 0 } profile || profile.PlayerToken == "offline") return;
        _connectionActionPending = true;
        try { await PerformConnectDevicesOnlyAsync(profile); }
        finally { _connectionActionPending = false; }
    }

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_connectionActionPending || _vm.IsConnectionLoading || _vm.IsInitializing) return;
        _connectionActionPending = true;
        _vm.IsFullConnectRequested = true;
        _vm.IsConnectionLoading = !HasMapImage;
        try
        {
            if (_isSoftConnecting)
            {
                AppendLog("[connect] Soft-connect is currently active. Automatically waiting for it to finish before starting full connect...");
            }
            await PerformConnectAsync(false);
        }
        finally
        {
            _vm.IsConnectionLoading = false;
            _connectionActionPending = false;
        }
    }

    private async void BtnDisconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_connectionActionPending) return;
        _connectionActionPending = true;
        try { await HardResetAsync(reconnect: false); }
        finally { _connectionActionPending = false; }
    }

    private void BtnShowServerInfo_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected == null) return;

        ServerInfoBackdrop.Visibility = Visibility.Visible;

        var modal = new Views.ServerInfoModal(_vm.Selected, _vm, _rust) { Owner = this };

        EventHandler locChanged = (s, args) => CenterServerInfoModal(modal);
        SizeChangedEventHandler sizeChanged = (s, args) => CenterServerInfoModal(modal);

        this.LocationChanged += locChanged;
        this.SizeChanged += sizeChanged;

        modal.Loaded += (s, args) => CenterServerInfoModal(modal);

        modal.Closed += (s, args) =>
        {
            this.LocationChanged -= locChanged;
            this.SizeChanged -= sizeChanged;
            ServerInfoBackdrop.Visibility = Visibility.Collapsed;
        };

        modal.Show();
    }

    private void CenterServerInfoModal(Window modal)
    {
        if (modal == null) return;
        double w = double.IsNaN(modal.Width) ? modal.ActualWidth : modal.Width;
        double h = double.IsNaN(modal.Height) ? modal.ActualHeight : modal.Height;
        if (w <= 0) w = 500;
        if (h <= 0) h = 580;

        modal.Left = this.Left + (this.Width - w) / 2;
        modal.Top = this.Top + (this.Height - h) / 2;
    }

    private void ServerInfoBackdrop_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        foreach (Window win in OwnedWindows)
        {
            if (win is Views.ServerInfoModal)
            {
                win.Close();
                break;
            }
        }
    }

    private async void OnConnectionLost()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(OnConnectionLost));
            return;
        }

        if (_isDeletingServer || _connectedProfile == null || !ReferenceEquals(_connectedProfile, _vm.Selected)) return;
        if (_connectionAttemptsActive > 0)
        {
            _connectedProfile.IsConnected = false;
            _connectedProfile.ConnectionFailure = Models.ConnectionFailureKind.Unavailable;
            CancelConnectionPolling();
            return;
        }
        if (_isReconnecting || _connectionActionPending) return;
        _isReconnecting = true;
        var reconnectProfile = _vm.Selected;
        int selectionVersion = _mapSelectionVersion;
        bool reconnectFull = reconnectProfile?.WantsFullConnection == true || reconnectProfile?.IsFullConnected == true || _vm.IsFullConnectRequested || _vm.IsInitializing
            || (_connectionActionPending && !_isSoftConnecting);

        try
        {
            CancelConnectionPolling();
            StopDynPolling(clearKnown: false);

            if (_vm?.Selected != null)
            {
                _vm.Selected.IsConnected = false;
                _vm.Selected.IsFullConnected = false;
                _vm.NotifyDevicesChanged();
            }

            try
            {
                // Suppress errors if the socket is already completely dead
                await _rust.DisconnectAsync();
            }
            catch { /* Ignore */ }

            Dispatcher.Invoke(() => ChkShops.IsChecked = false);
            AppendLog("[auto-reconnect] Connection lost detected. Starting recovery...");

            if (selectionVersion != _mapSelectionVersion || !ReferenceEquals(_vm?.Selected, reconnectProfile) || reconnectProfile == null) return;
            if (reconnectProfile.HasConnectionIssue && !RustPlusConnectionException.CanRetry(reconnectProfile.ConnectionFailure)) return;
            reconnectProfile.IsReconnecting = true;
            int recovery = _connectionRecoveryVersion;
            bool StillWanted() => selectionVersion == _mapSelectionVersion && recovery == _connectionRecoveryVersion
                && ReferenceEquals(_vm?.Selected, reconnectProfile) && !_isDeletingServer;

            bool success = reconnectFull
                ? await PerformConnectAsync(true)
                : await PerformConnectDevicesOnlyAsync(reconnectProfile);

            // One failed attempt is not the end of it. A lost socket raises no second event, so
            // stopping here left the app offline until something else woke it: a Facepunch 521
            // at 19:05 kept it away for 27 minutes, and an oil rig alarm was what brought it back.
            // Only what retrying can fix is retried; a re-pair or a sign-in waits for the user,
            // and disconnecting or picking another server ends it.
            for (int round = 0; !success; round++)
            {
                if (!StillWanted()) return;
                var failure = reconnectProfile.ConnectionFailure;
                if (failure != null && !RustPlusConnectionException.CanRetry(failure))
                {
                    AppendLog("[auto-reconnect] Paused; use the connection recovery action.");
                    return;
                }

                int seconds = AutoReconnectDelaysSeconds[Math.Min(round, AutoReconnectDelaysSeconds.Length - 1)];
                AppendLog($"[auto-reconnect] Still unreachable; next try in {seconds}s.");
                reconnectProfile.IsReconnecting = true;
                await Task.Delay(TimeSpan.FromSeconds(seconds));

                if (!StillWanted()) return;
                // The recovery button, or the network coming back, may have connected meanwhile.
                if (reconnectProfile.IsConnected) return;
                if (_connectionAttemptsActive > 0 || _connectionActionPending) continue;

                reconnectProfile.IsReconnecting = true;
                success = reconnectFull
                    ? await PerformConnectAsync(true)
                    : await PerformConnectDevicesOnlyAsync(reconnectProfile);
            }

            AppendLog("[auto-reconnect] Reconnected successfully.");
        }
        catch (Exception ex)
        {
            AppendLog("[auto-reconnect] Loop error: " + ex.Message);
        }
        finally
        {
            _isReconnecting = false;
            if (reconnectProfile != null) reconnectProfile.IsReconnecting = false;
        }
    }

    /// <summary>Waits between automatic reconnects: quick at first, then once a minute.</summary>
    private static readonly int[] AutoReconnectDelaysSeconds = { 15, 30, 60 };
}
