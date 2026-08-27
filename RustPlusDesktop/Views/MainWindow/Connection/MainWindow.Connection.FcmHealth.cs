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

        var repair = await FcmRepairService.TryRepairAsync(Log, ct).ConfigureAwait(false);
        if (repair.Outcome != FcmRepairService.RepairOutcome.Repaired)
        {
            // Reset + Re-pair is the manual equivalent, and it needs the Steam login window, so it
            // has to be the user's decision rather than something we spring on them.
            Log(repair.Outcome == FcmRepairService.RepairOutcome.NeedsFullRePair
                ? "[fcm-health] Automatic renewal is not possible — please use Reset + Listen (re-pair)."
                : $"[fcm-health] Automatic renewal failed ({repair.Detail}).");
            await Dispatcher.InvokeAsync(() => _vm.NotifyFcmChanged());
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

        Log($"[fcm-health] Still no push notifications after renewal ({again.Detail}). " +
            "Please use Reset + Listen (re-pair).");
        return false;
    }
}
