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
        try { _storageTimer?.Stop(); _shopTimer = null; } catch { }

        // 3) UI-/In-Memory-State leeren
        try { TeamMembers.Clear(); } catch { }
        try { _avatarCache.Clear(); } catch { }
        try { _lastPresence.Clear(); } catch { }
        try { ClearAllDeathPins(); } catch { }
        try { ClearAllToggleBusy(); } catch { }

        // Shopspezifisch, wie du es im Connect auch machst
        try { _lastShops.Clear(); } catch { }
        try { _shopLifetimes.Clear(); } catch { }
        try { _knownShopIds.Clear(); } catch { }
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
        if (_vm?.Selected != null)
        {
            _vm.Selected.IsConnected = false;
        }
        if (_vm != null)
        {
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

    private async void BtnHardReset_Click(object sender, RoutedEventArgs e)
    {
        var ok = ConfirmModal.Show(this,
            "Reset connection (full wipe)",
            "Are you sure you want to reset everything?\n\n" +
            "• All paired servers will be deleted\n" +
            "• Your Steam login will be removed\n" +
            "• The pairing config will be wiped\n\n" +
            "You'll need to pair again from scratch after this.",
            okLabel: "Reset everything");

        if (!ok) return;

        await FullWipeResetAsync();
    }

    private async Task FullWipeResetAsync()
    {
        AppendLog("[WIPE] Starting full reset...");

        // 1. Connection reset
        await HardResetAsync(reconnect: false);

        // 2. Clear servers
        _vm.Servers.Clear();
        _vm.Save();
        AppendLog("[WIPE] Servers cleared.");

        // 3. Clear SteamID
        _vm.SteamId64 = "";
        TrackingService.SteamId64 = "";
        AppendLog("[WIPE] Steam credentials removed.");

        // 4. Wipe Pairing Config
        await ResetPairingConfigAsync(stopListenerFirst: true);
        AppendLog("[WIPE] Pairing configuration deleted.");

        // 5. Update UI
        HydrateSteamUiFromStorage();
        AppendLog("[WIPE] Full wipe completed. Starting new pairing flow...");

        // Trigger the pairing listener (which will trigger fcm-register because we wiped the config)
        _ = StartPairingListenerUiAsync();
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
