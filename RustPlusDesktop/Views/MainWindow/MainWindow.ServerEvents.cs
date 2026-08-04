using System;
using System.Threading.Tasks;
using RustPlusDesk.Services;
using RustPlusDesk.Services.Audio;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    /// <summary>
    /// Ties the audio listener and the cloud event state to the connected server.
    ///
    /// Called once a connection is fully initialised. Everything in here is a no-op on a
    /// server that still delivers events over Rust+ — the API is more precise than audio in
    /// every respect, so there is nothing to gain from listening.
    /// </summary>
    private async Task StartServerEventTrackingAsync()
    {
        try
        {
            string serverKey = GetServerKey();
            if (string.IsNullOrWhiteSpace(serverKey) || serverKey == "unknown-server") return;

            // Start in the mode this server used last time. Correcting after the first shop
            // poll is unavoidable, but starting from the remembered answer means the correction
            // is usually a no-op instead of a visible switch on every single connect.
            ApplyRememberedEventSource();
            HookCloudEventAlerts();

            // Force the next presence upload to go through with this server's key.
            //
            // Presence is pushed from the team poll and is both throttled and gated on a
            // signature, and it fires roughly half a second BEFORE a connection finishes
            // initialising — so the upload can still carry the previous server. Normally the
            // next poll corrects that within seconds, but a player who goes AFK right after
            // connecting produces no further uploads at all, and current_server_key then stays
            // wrong for the whole session. Every report is refused as rejected_wrong_server
            // while presence, key format and profile all look healthy.
            _lastCloudPresenceSignature = null;
            _hasCriticalPresenceChange = true;

            // And let the watcher force one on demand, for the AFK case where no poll would
            // otherwise refresh it.
            CloudEventWatcher.Instance.PresenceRefresh = async () =>
            {
                _hasCriticalPresenceChange = true;
                _lastCloudPresenceSignature = null;
                await Dispatcher.InvokeAsync(async () => await LoadTeamAsync()).Task.Unwrap();
            };

            await CloudEventWatcher.Instance.AttachAsync(serverKey);
            UpdateAudioListenerState();

            // Settle the verdict for this session. Runs regardless of the remembered mode, so
            // a server that starts delivering again is picked up — and it is the only
            // detection there is, since the ongoing shop timer stays off in fallback mode.
            await ProbeShopDataAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"[server-events] Could not start tracking: {ex.Message}");
        }
    }

    private void StopServerEventTracking()
    {
        try
        {
            UnhookCloudEventAlerts();
            CloudEventWatcher.Instance.Detach();
            GameAudioListener.Instance.Stop();
        }
        catch { }
    }

    /// <summary>
    /// The listener runs only when it can contribute something: the setting is on, and this
    /// server does not deliver events itself. Note it still gates on Rust actually running —
    /// that check lives in the listener.
    /// </summary>
    internal void UpdateAudioListenerState()
    {
        bool wanted = TrackingService.ListenForServerEvents && EventCapabilities.IsCloudSourced;

        if (wanted && !GameAudioListener.Instance.IsRunning) GameAudioListener.Instance.Start();
        else if (!wanted && GameAudioListener.Instance.IsRunning) GameAudioListener.Instance.Stop();
    }

    /// <summary>
    /// Alert entries that cannot mean anything on a server without event markers.
    ///
    /// Patrol Heli, Chinook and Travelling Vendor have no server-wide audio cue, so they are
    /// gone rather than degraded. Cargo's docking, egress and arrival warnings all need the
    /// ship's position, which only the API carries — audio can say a cargo spawned and nothing
    /// more, so only the spawn entry survives.
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> CloudHiddenAlertTags =
        new(StringComparer.Ordinal) { "Heli", "Chinook", "Vendor", "CargoDock", "CargoEgress", "CargoArrival" };

    /// <summary>
    /// Command entries with no answer to give. Keyed by the command field they configure.
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> CloudHiddenCommandTags =
        new(StringComparer.Ordinal) { "CmdHeli", "CmdVendor" };

    /// <summary>
    /// Hides everything the current server cannot deliver. Driven by tags that already exist
    /// in the XAML rather than by names added for the purpose — a menu entry that gains a tag
    /// is then covered automatically, and one that loses it fails loudly instead of silently
    /// staying visible.
    /// </summary>
    internal void ApplyEventCapabilitiesToMenus()
    {
        bool cloud = EventCapabilities.IsCloudSourced;

        if (AlertsEventsColumn != null)
            foreach (object item in AlertsEventsColumn.Items)
                ApplyTagVisibility(item, CloudHiddenAlertTags, cloud);
    }

    private static void ApplyTagVisibility(object item, System.Collections.Generic.HashSet<string> hidden, bool cloud)
    {
        if (item is not System.Windows.Controls.MenuItem menuItem) return;

        if (menuItem.Tag is string tag && hidden.Contains(tag))
            menuItem.Visibility = cloud
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;

        foreach (object child in menuItem.Items)
            ApplyTagVisibility(child, hidden, cloud);
    }

    private void ApplyRememberedEventSource()
    {
        string remembered = _vm?.Selected?.LastEventSource ?? "";

        // Unknown servers fall back on purpose: showing events that can never arrive is worse
        // than briefly hiding ones that will.
        bool wasApi = string.Equals(remembered, "RustApi", StringComparison.OrdinalIgnoreCase);

        // Seed the detector's own state, not just the capability flag. ApplyShopDataAvailability
        // recomputes the source from _shopDataAvailable, so leaving that at its optimistic
        // default made the next ApplySettings pass overwrite the remembered mode — which is why
        // a reconnect still showed the full view until the probe had run again.
        _shopDataAvailable = wasApi;

        EventCapabilities.SetSource(wasApi ? ServerEventSource.RustApi : ServerEventSource.Cloud);
        ApplyShopDataAvailability();
    }

    /// <summary>Persists what the shop poll actually found, for the next connect.</summary>
    private void RememberEventSource(ServerEventSource source)
    {
        var profile = _vm?.Selected;
        if (profile == null) return;

        string value = source == ServerEventSource.RustApi ? "RustApi" : "Cloud";
        if (profile.LastEventSource == value) return;

        profile.LastEventSource = value;
        try { _vm!.Save(); } catch { }
    }
}
