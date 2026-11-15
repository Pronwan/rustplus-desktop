using RustPlusDesk.Models;
using RustPlusDesk.Services;
using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private int _connectionRecoveryVersion;
    private int _connectionAttemptsActive;
    private bool _connectionRecoveryHooksAdded;

    private async Task<bool> RunConnectionAttemptsAsync(bool full, bool silent = true, bool showBusy = true)
    {
        var profile = _vm.Selected;
        if (profile == null) return false;
        int selection = _mapSelectionVersion, recovery = _connectionRecoveryVersion;
        bool IsCurrent() => selection == _mapSelectionVersion && recovery == _connectionRecoveryVersion
            && ReferenceEquals(_vm.Selected, profile) && !_isDeletingServer;
        profile.WantsFullConnection = full;
        if (!_connectionRecoveryHooksAdded)
        {
            _connectionRecoveryHooksAdded = true;
            NetworkChange.NetworkAvailabilityChanged += OnConnectionNetworkAvailable;
            Closed += (_, _) => NetworkChange.NetworkAvailabilityChanged -= OnConnectionNetworkAvailable;
        }
        _connectionAttemptsActive++;
        try
        {
            return await RustPlusConnectionException.RetryAsync(async () =>
            {
                profile.ConnectionFailure = null;
                return full ? await PerformConnectOnceAsync(silent, showBusy) : await ConnectDevicesOnlyCoreAsync(profile);
            }, IsCurrent, () => profile.ConnectionFailure, async milliseconds =>
            {
                profile.IsReconnecting = true;
                AppendLog($"[connection] Retrying in {milliseconds / 1000}s...");
                await Task.Delay(milliseconds);
            });
        }
        finally
        {
            _connectionAttemptsActive--;
            profile.IsReconnecting = false;
        }
    }

    private void RecordConnectionFailure(ServerProfile profile, Exception error)
    {
        var kind = RustPlusConnectionException.Classify(error);
        profile.IsFullConnected = false;
        if (kind is not (ConnectionFailureKind.Unsupported or ConnectionFailureKind.ServerError)) profile.IsConnected = false;
        profile.ConnectionFailure = kind;
        _vm.NotifyDevicesChanged();
    }

    private async void BtnConnectionRecovery_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is not { } profile || _connectionActionPending || _connectionAttemptsActive > 0) return;
        if (profile.ConnectionFailure == ConnectionFailureKind.SignInRequired)
        {
            _connectionActionPending = true;
            try
            {
                await _pairing.StopAsync();
                _listenerStarting = false;
                if (await CompanionAuthManager.Provider.RunRegistrationFlowAsync(AppendLog))
                {
                    _vm.NotifyFcmChanged();
                    StartPairingSilent(false);
                    if (ReferenceEquals(_vm.Selected, profile))
                        await RunConnectionAttemptsAsync(profile.WantsFullConnection, silent: false);
                }
            }
            catch (Exception error) { AppendLog("[connection] Sign-in failed: " + error.Message); }
            finally { _connectionActionPending = false; }
            return;
        }
        if (profile.ConnectionFailure is ConnectionFailureKind.RePairRequired or ConnectionFailureKind.MissingCompanionId)
        {
            StartPairingSilent(false);
            ShowInfoSnackbar(profile.ConnectionIssueTitle, profile.ConnectionIssueDescription, Wpf.Ui.Controls.ControlAppearance.Info);
            return;
        }
        _connectionActionPending = true;
        try { await RunConnectionAttemptsAsync(profile.WantsFullConnection, silent: false); }
        finally { _connectionActionPending = false; }
    }

    private void OnConnectionNetworkAvailable(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (!e.IsAvailable) return;
        _ = Dispatcher.InvokeAsync(async () =>
        {
            if (_connectionAttemptsActive != 0 || _isReconnecting || _connectionActionPending || _vm.Selected is not { } profile
                || !RustPlusConnectionException.CanRetry(profile.ConnectionFailure)) return;
            await RunConnectionAttemptsAsync(profile.WantsFullConnection);
        });
    }

    private void OnFeatureUnsupported() => Dispatcher.BeginInvoke(() => ShowInfoSnackbar(
        Properties.Resources.GetString("ConnectionUnsupportedTitle"), Properties.Resources.GetString("ConnectionUnsupportedDescription"),
        Wpf.Ui.Controls.ControlAppearance.Info));
}
