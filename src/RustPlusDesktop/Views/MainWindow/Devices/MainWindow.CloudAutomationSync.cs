using System;
using System.Windows.Threading;
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

        _vm.Saved += () => Dispatcher.BeginInvoke(() =>
        {
            _cloudAutomationSyncTimer.Stop();
            _cloudAutomationSyncTimer.Start();
        });
    }

    private void SyncCloudAutomations()
    {
        var profile = _vm?.Selected;
        if (profile == null || string.IsNullOrWhiteSpace(profile.ServerKey)) return;

        _ = CloudAutomationSync.SyncAsync(profile, profile.ServerKey);
    }
}
