using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    // Facepunch is dropping vending-machine and dynamic event markers from the Rust+ feed.
    // Instead of deciding that at build time we watch the feed itself: a server that stops
    // delivering shops gets the shops + events UI hidden, one that (still) delivers gets it
    // back. That way nothing has to be shipped on the day the change lands, and a server
    // where the data is re-enabled simply works again.
    //
    // Events ride on the shop verdict on purpose. They are removed by the same change, and
    // their own absence proves nothing — Cargo and Heli are intermittent by design, so "no
    // heli right now" is the normal case.
    //
    // Polling keeps running while hidden (just slower), so the verdict is always reversible.
    //
    // "No shops" is an unambiguous signal, not a matter of server age: the NPC vending
    // machines at Outpost and Bandit Camp belong to the monuments, so a working feed
    // delivers them from the moment the server is up — a freshly wiped server included.
    // We still want a couple of polls before deciding, purely to survive a single
    // truncated or rate-limited response (the retry in PollShopsOnceAsync only kicks in
    // once we have seen shops before, so the first poll after connect is unguarded).

    private bool _shopDataAvailable = true;
    private int _emptyShopPolls;

    private const int EmptyPollsBeforeHiding = 3;
    private static readonly TimeSpan ShopPollInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ShopProbeInterval = TimeSpan.FromMinutes(2);

    /// <summary>Clears the verdict. Call on (re)connect and when switching server.</summary>
    private void ResetShopDataAvailability()
    {
        _shopDataAvailable = true;
        _emptyShopPolls = 0;
        Dispatcher.Invoke(ApplyShopDataAvailability);
    }

    /// <summary>
    /// Establishes the verdict once per connect, independent of whether the user has shops
    /// switched on. Without this there would be no detection at all: the ongoing shop timer
    /// only runs when the user enabled shops, and it is stopped entirely once a server is
    /// known not to deliver them.
    ///
    /// Talks to the client directly rather than going through PollShopsOnceAsync, because that
    /// path also draws the markers — probing must not put shops on the map of someone who
    /// turned them off.
    /// </summary>
    internal async Task ProbeShopDataAsync()
    {
        if (_rust is not RustPlusClientReal real) return;

        var token = _connectionPollingCts.Token;
        for (int attempt = 0; attempt < EmptyPollsBeforeHiding && !token.IsCancellationRequested; attempt++)
        {
            List<RustPlusClientReal.ShopMarker>? shops;
            try { shops = await real.GetVendingShopsAsync(token); }
            catch { return; }

            // null is a technical failure, not an answer — retry without counting it.
            if (shops != null)
            {
                TrackShopDataAvailability(shops.Count > 0);
                if (shops.Count > 0) return;        // server delivers; verdict settled
                if (!_shopDataAvailable) return;    // enough empties; verdict settled
            }

            try { await Task.Delay(TimeSpan.FromSeconds(8), token); } catch { return; }
        }
    }

    /// <summary>Feeds one shop poll result into the verdict.</summary>
    private void TrackShopDataAvailability(bool gotShops)
    {
        if (gotShops)
        {
            _emptyShopPolls = 0;
            if (_shopDataAvailable) return;

            _shopDataAvailable = true;
            AppendLog("[shops] Vending data is back — restoring the shops + events UI.");
            Dispatcher.Invoke(ApplyShopDataAvailability);
            return;
        }

        if (!_shopDataAvailable) return;
        if (++_emptyShopPolls < EmptyPollsBeforeHiding) return;

        _shopDataAvailable = false;
        AppendLog($"[shops] No vending data in {_emptyShopPolls} polls — hiding the shops UI, " +
                  "events now come from audio detection.");
        Dispatcher.Invoke(ApplyShopDataAvailability);
    }

    /// <summary>
    /// Shows or hides everything that can only ever be filled from vending + event markers.
    /// <see cref="RustApiFeatures.EventsAndShopsRemoved"/> still wins as a manual override.
    /// </summary>
    private void ApplyShopDataAvailability()
    {
        bool show = _shopDataAvailable && !RustApiFeatures.EventsAndShopsRemoved;

        // One place decides which events exist; dock, alert menu, command menu, templates and
        // the chat master all read it from there rather than each judging for itself.
        var source = show ? ServerEventSource.RustApi : ServerEventSource.Cloud;
        EventCapabilities.SetSource(source);
        RememberEventSource(source);
        UpdateAudioListenerState();
        ApplyEventCapabilitiesToMenus();

        Visibility vis = show ? Visibility.Visible : Visibility.Collapsed;

        // Shop-only UI: nothing can fill it, so it goes away entirely.
        if (ChkShops != null) ChkShops.Visibility = vis;
        if (ChkLayerAutoLoadShops != null) ChkLayerAutoLoadShops.Visibility = vis;
        if (BtnShopToggleBorder != null) BtnShopToggleBorder.Visibility = vis;
        if (AlertsShopsColumn != null) AlertsShopsColumn.Visibility = vis;
        if (TradeAlertsMenuItem != null) TradeAlertsMenuItem.Visibility = vis;

        // The event dock and the alerts "Events" column deliberately stay. They no longer
        // carry Patrol Heli, Chinook or Travelling Vendor, but Cargo, Deep Sea and Oil Rig
        // still arrive through audio detection — and the alert checkboxes must remain
        // configurable even before anything has been detected, otherwise a player cannot
        // decide in advance what they want to hear about.
        if (EventDock != null) EventDock.Visibility = Visibility.Visible;
        if (AlertsEventsColumn != null) AlertsEventsColumn.Visibility = Visibility.Visible;

        // Once a server is known not to deliver shops, stop asking altogether. Polling a dead
        // endpoint forever costs the server requests for nothing, and a server that starts
        // delivering again is caught by the probe on the next connect.
        //
        // Note this only stops the *shop* request. The dynamic marker poll must keep running
        // regardless: it carries player markers, which drive team positions on the map, the
        // Deep Sea auto-switch and follow/camera. Facepunch removes marker *types* from that
        // response, not the endpoint.
        if (!show)
        {
            _shopTimer?.Stop();
            _shopTimer = null;
        }
        else if (_shopTimer != null)
        {
            _shopTimer.Interval = ShopPollInterval;
        }

        if (show) return;

        // Close what the user may already have open.
        if (ShopSearchPanel != null) ShopSearchPanel.Visibility = Visibility.Collapsed;
        if (ProfitTradesPanel != null) ProfitTradesPanel.Visibility = Visibility.Collapsed;
    }
}
