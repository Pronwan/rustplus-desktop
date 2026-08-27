using RustPlusDesk.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    // One check per app run. The point is to catch a token that went stale between sessions,
    // not to keep poking Expo while the app is open.
    private int _fcmHealthChecked;

    /// <summary>
    /// Verifies after start-up that pushes actually arrive, and quietly renews the registration
    /// when they do not.
    ///
    /// A connected socket proves nothing: when Google invalidates a push registration the socket
    /// still connects and the stored expiry date still looks healthy, but pairings stop arriving.
    /// Users only discover this by trying to pair a server and getting nothing — which is where
    /// most "I can't pair servers" reports come from. Sending ourselves one probe answers the
    /// question the connection cannot.
    /// </summary>
    private void ScheduleFcmHealthCheck()
    {
        if (Interlocked.Exchange(ref _fcmHealthChecked, 1) == 1) return;

        _ = Task.Run(async () =>
        {
            try
            {
                // Let the socket settle before judging it.
                await Task.Delay(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
                await RunFcmHealthCheckAsync(announceHealthy: false).ConfigureAwait(false);
            }
            catch { /* never let a background check take the app down */ }
        });
    }

    /// <summary>
    /// Runs the probe and, if it fails, repairs and probes once more. Returns true when pushes are
    /// confirmed working. <paramref name="announceHealthy"/> keeps the automatic run quiet on
    /// success while a manual run still says so.
    /// </summary>
    private async Task<bool> RunFcmHealthCheckAsync(bool announceHealthy, CancellationToken ct = default)
    {
        void Log(string m) => Dispatcher.BeginInvoke(new Action(() => AppendLog(m)));

        var report = await FcmSelfTestService.RunAsync(Log, ct: ct).ConfigureAwait(false);

        if (report.Outcome == FcmSelfTestOutcome.Healthy)
        {
            if (announceHealthy) Log("[fcm-health] Push notifications are working.");
            return true;
        }

        // Inconclusive means we learned nothing — no network, Expo unreachable. Re-registering on
        // that basis would throw away a perfectly good token.
        if (!report.NeedsRepair)
        {
            Log($"[fcm-health] Could not verify push notifications ({report.Detail}).");
            return false;
        }

        Log($"[fcm-health] Push notifications are not arriving ({report.Detail}). Renewing the registration …");

        // The renewal ends in a listener restart, which has no business landing in the middle of a
        // connect. Same wait the full connect already does before claiming the socket.
        await WaitForSoftConnectAsync(Log).ConfigureAwait(false);

        var repair = await FcmRepairService.TryRepairAsync(Log, ct).ConfigureAwait(false);
        if (repair.Outcome != FcmRepairService.RepairOutcome.Repaired)
        {
            // Re-pairing needs the Steam login window, so it stays the user's decision rather than
            // something we spring on them.
            Log(repair.Outcome == FcmRepairService.RepairOutcome.NeedsFullRePair
                ? "[fcm-health] Automatic renewal is not possible — prompting for a re-pair."
                : $"[fcm-health] Automatic renewal failed ({repair.Detail}).");
            await Dispatcher.InvokeAsync(() =>
            {
                _vm.NotifyFcmChanged();
                PromptForRePair();
            });
            return false;
        }

        // New credentials only take effect on the next connect.
        Log("[fcm-health] Restarting the listener with the renewed registration …");
        await _pairing.StopAsync().ConfigureAwait(false);
        await Task.Delay(500, ct).ConfigureAwait(false);
        await Dispatcher.InvokeAsync(() =>
        {
            _listenerStarting = false;
            _vm.NotifyFcmChanged();
            StartPairingSilent(false);
        });

        // Give the fresh socket time to come up before asking it to prove itself.
        await Task.Delay(TimeSpan.FromSeconds(8), ct).ConfigureAwait(false);

        var again = await FcmSelfTestService.RunAsync(Log, ct: ct).ConfigureAwait(false);
        if (again.Outcome == FcmSelfTestOutcome.Healthy)
        {
            Log("[fcm-health] ✔ Push notifications are working again.");
            return true;
        }

        Log($"[fcm-health] Still no push notifications after renewal ({again.Detail}). Prompting for a re-pair.");
        await Dispatcher.InvokeAsync(PromptForRePair);
        return false;
    }

    /// <summary>
    /// Holds off while a soft-connect is running. The flag lives on the UI thread, so it is read
    /// there. Bounded, and deliberately generous: unlike a user-initiated connect, nothing here is
    /// waiting on us, and giving up early would defeat the point of waiting at all.
    /// </summary>
    private async Task WaitForSoftConnectAsync(Action<string> log)
    {
        if (!await Dispatcher.InvokeAsync(() => _isSoftConnecting)) return;

        log("[fcm-health] Soft-connect in progress, holding the renewal back …");

        var waited = 0;
        while (waited < 15000 && await Dispatcher.InvokeAsync(() => _isSoftConnecting))
        {
            await Task.Delay(250).ConfigureAwait(false);
            waited += 250;
        }

        log(await Dispatcher.InvokeAsync(() => _isSoftConnecting)
            ? "[fcm-health] Soft-connect is taking too long. Renewing anyway."
            : "[fcm-health] Soft-connect finished, continuing.");
    }

    /// <summary>
    /// Offers the one repair we cannot perform on the user's behalf, in the same corner as update
    /// and alarm notifications. Pressing the button is the whole interaction — it does what the
    /// Reset + Listen context-menu entry does, without asking a second time.
    /// </summary>
    private void PromptForRePair()
    {
        ShowActionSnackbar(
            Properties.Resources.GetString("FcmRepairTitle"),
            Properties.Resources.GetString("FcmRepairMessage"),
            Properties.Resources.GetString("FcmRepairButton"),
            () => _ = RePairFromPromptAsync(),
            Wpf.Ui.Controls.ControlAppearance.Caution);
    }

    private async Task RePairFromPromptAsync()
    {
        if (await ResetPairingConfigAsync(stopListenerFirst: true).ConfigureAwait(true))
            await StartPairingListenerUiAsync().ConfigureAwait(true);
    }
}
