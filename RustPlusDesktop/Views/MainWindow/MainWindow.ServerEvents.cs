using System;
using System.Threading.Tasks;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    /// <summary>Initializes API event availability and Smart Alarm rules.</summary>
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
            RefreshOilRigTimerCapability();

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

    /// <summary>Alerts unavailable without Rust+ event markers.</summary>
    private static readonly System.Collections.Generic.HashSet<string> CloudHiddenAlertTags =
        new(StringComparer.Ordinal) { "Heli", "Chinook", "Vendor", "Cargo", "DeepSea", "CargoDock", "CargoEgress", "CargoArrival" };

    /// <summary>
    /// Command entries with no answer to give. Keyed by the command field they configure.
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> CloudHiddenCommandTags =
        new(StringComparer.Ordinal) { "CmdHeli", "CmdVendor", "CmdCargo", "CmdDeepSea" };

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
