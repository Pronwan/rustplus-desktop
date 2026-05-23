using RustPlusDesk.Models;
using RustPlusDesk.Services;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private ServerProfile? _connectedProfile;
    private DispatcherTimer? _storageTimer;
    private bool _storageTickBusy; // optionaler Reentrancy-Schutz
    private bool _isReconnecting = false;

    private async Task HardResetAsync(bool reconnect = false)
    {
        _connectedProfile = null;
        // 1) Laufende Polls/Tokens abbrechen
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
        try { _statusTimer?.Stop(); } catch { }
        try { _shopTimer?.Stop(); _shopTimer = null; } catch { }
        try { _storageTimer?.Stop(); _storageTimer = null; } catch { }
        try { Dispatcher.Invoke(() => ChkShops.IsChecked = false); } catch { }

        // 3) UI-/In-Memory-State leeren
        try { TeamMembers.Clear(); } catch { }
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
        _initialShopSnapshotTimeUtc = DateTime.MinValue;
        _alertsNeedRebaseline = true;
        _lastChatSendUtc = DateTime.MinValue;

        // 4) Overlay-Elemente wirklich vom Canvas runternehmen
        try
        {
            foreach (var el in _shopEls.Values)
                Overlay.Children.Remove(el);
            _shopEls.Clear();
        }
        catch { }

        // Wenn du noch player-overlays / team-overlays hast:
        try
        {
            ClearUserOverlayElements();   // du hast das im Connect schon – nutzen!
        }
        catch { }

        // 5) WIRKLICH vom Rust-Server trennen
        try
        {
            if (_rust != null)
                await _rust.DisconnectAsync();   // RustPlusClientReal trennt hier sauber und setzt _api = null
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

        ResetMapDisplay();

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

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        await PerformConnectAsync(false);
    }

    private async void BtnDisconnect_Click(object sender, RoutedEventArgs e)
    {
        await HardResetAsync(reconnect: false);
    }

    private void BtnShowServerInfo_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected == null) return;
        var modal = new Views.ServerInfoModal(_vm.Selected.Name, _vm.Selected.Description) { Owner = this };
        modal.ShowDialog();
    }

    private async void OnConnectionLost()
    {
        if (_isReconnecting) return;
        _isReconnecting = true;

        try
        {
            // Suppress errors if the socket is already completely dead
            await _rust.DisconnectAsync();
        }
        catch { /* Ignore */ }

        Dispatcher.Invoke(() => ChkShops.IsChecked = false);
        AppendLog("[auto-reconnect] Connection lost detected. Starting recovery...");

        int delay = 2000;
        int maxDelay = 60000;

        try
        {
            while (_isReconnecting)
            {
                AppendLog($"[auto-reconnect] Retrying in {delay / 1000}s...");
                await Task.Delay(delay);

                bool success = await PerformConnectAsync(true);
                if (success)
                {
                    AppendLog("[auto-reconnect] Reconnected successfully!");
                    _isReconnecting = false;
                    return;
                }

                delay = Math.Min(delay * 2, maxDelay);
            }
        }
        catch (Exception ex)
        {
            AppendLog("[auto-reconnect] Loop error: " + ex.Message);
            _isReconnecting = false;
        }
    }
}
