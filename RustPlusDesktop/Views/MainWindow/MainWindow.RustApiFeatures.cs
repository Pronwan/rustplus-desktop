using System.Windows;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    /// <summary>
    /// When the Rust+ events/shops API restriction lands (see
    /// <see cref="RustApiFeatures.EventsAndShopsRemoved"/>), hide the now-dead
    /// shops + events UI and make sure nothing polls for them. No-op while the
    /// flag is false, so today it changes nothing.
    /// </summary>
    private void ApplyRustApiFeatureFlags()
    {
        if (!RustApiFeatures.EventsAndShopsRemoved)
            return;

        // Shops: uncheck (which stops the poll timer), hide the map toggle and the
        // auto-load option.
        if (ChkShops != null)
        {
            ChkShops.IsChecked = false;
            ChkShops.Visibility = Visibility.Collapsed;
        }
        if (ChkLayerAutoLoadShops != null)
        {
            ChkLayerAutoLoadShops.IsChecked = false;
            ChkLayerAutoLoadShops.Visibility = Visibility.Collapsed;
        }
        _shopTimer?.Stop();
        _shopTimer = null;

        // Events: hide the events dock, the alerts "Events" column, and the
        // trade/vending alerts item.
        if (EventDock != null)
            EventDock.Visibility = Visibility.Collapsed;
        if (AlertsEventsColumn != null)
            AlertsEventsColumn.Visibility = Visibility.Collapsed;
        if (TradeAlertsMenuItem != null)
            TradeAlertsMenuItem.Visibility = Visibility.Collapsed;
    }
}
