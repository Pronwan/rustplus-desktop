using System;
using System.Windows.Threading;
using RustPlusDesk.Services;
using RustPlusDesk.Services.Cloud;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private DispatcherTimer? _cloudAutomationSyncTimer;

    /// <summary>
    /// Sends the selected server's Logic Engine rules and Device Automations to the cloud a few
    /// seconds after anything is saved, so the cloud runs the same set while the app is closed.
    ///
    /// Every save rather than each editor's own buttons: rules are changed in two overlays and
    /// a context menu, and a path that forgot to sync would leave the cloud running a rule the
    /// user had already deleted. Unchanged rules cost nothing - the sync compares with what it
    /// sent last. The delay folds the burst of saves one edit makes into a single push.
    /// </summary>
    private void InitializeCloudAutomationSync()
    {
        _cloudAutomationSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _cloudAutomationSyncTimer.Tick += (_, _) =>
        {
            _cloudAutomationSyncTimer.Stop();
            SyncCloudAutomations();
        };

        // Device names, icons, groups, deletions and command words go out almost at once
        // rather than with the rules: the website and the cloud worker show and answer to
        // them, and the 10 s poll (still running as the fallback) made an edit visibly lag.
        // Every handler that edits a device saves, so the save is the one place to hook.
        _cloudDevicePushTimer = new DispatcherTimer { Interval = CloudDevicePushDelay };
        _cloudDevicePushTimer.Tick += (_, _) =>
        {
            _cloudDevicePushTimer.Stop();
            PushDevicesToCloudNow();
        };

        // A timed !mute outlives a restart; lift it if it ran out while the app was closed.
        RestoreLocalChatAlertsMute();

        _vm.Saved += () => Dispatcher.BeginInvoke(() =>
        {
            _cloudAutomationSyncTimer.Stop();
            _cloudAutomationSyncTimer.Start();
            _cloudDevicePushTimer.Stop();
            _cloudDevicePushTimer.Start();
        });
    }

    /// <summary>Folds the saves one edit makes into one push, without making the edit wait.</summary>
    internal static readonly TimeSpan CloudDevicePushDelay = TimeSpan.FromMilliseconds(400);

    private DispatcherTimer? _cloudDevicePushTimer;

    /// <summary>
    /// The selected server's device tree and command bindings, now. Both skip an unchanged
    /// payload, so a save that touched nothing else costs no traffic. Gated like the poll.
    /// </summary>
    private async void PushDevicesToCloudNow()
    {
        var profile = _vm?.Selected;
        if (profile == null) return;

        try
        {
            if (!string.IsNullOrWhiteSpace(profile.ServerKey))
                _ = CloudChatCommandSync.SyncAsync(profile, profile.ServerKey);

            if (TrackingService.CloudSyncEnabled && _ownCloudRestoreReady && _mySteamId != 0 && profile.Devices != null)
                await UploadDevicesSnapshotForCurrentServerAsync();
        }
        catch (Exception ex)
        {
            AppendLog("[dev/cloud] Immediate device push failed (the periodic sync retries): " + ex.Message);
        }
    }

    private void SyncCloudAutomations()
    {
        var profile = _vm?.Selected;
        if (profile == null || string.IsNullOrWhiteSpace(profile.ServerKey)) return;

        _ = CloudAutomationSync.SyncAsync(profile, profile.ServerKey);
        // The command words and the door-code choice follow every save too, not only a reconnect:
        // turning "Door codes in Cloud 24/7" off has to take the codes out of the cloud now.
        _ = CloudChatCommandSync.SyncAsync(profile, profile.ServerKey);
    }
}
