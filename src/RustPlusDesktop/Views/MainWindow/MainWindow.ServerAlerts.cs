using System;
using System.Linq;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

/// <summary>
/// Modded Server Alerts: what a modded server's plugin sends on Rust+'s alarm channel on its own
/// ("Wall destroyed at R9", upkeep, events), with no Smart Alarm behind it. The account decides,
/// together with the website and Cloud 24/7, whether they count at all and whether as a raid.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// A server alert the account treats as a standard alert: not a raid, like an oil rig event.
    /// A short snackbar instead of the alarm window and sound, and the Discord bot's Server Alerts
    /// channel, or its events channel when none is set. No team chat raid announcement.
    /// </summary>
    private void HandleStandardServerAlert(AlarmNotification n, string cleanSrv)
    {
        var title = string.IsNullOrWhiteSpace(n.Title) ? cleanSrv : n.Title!;
        AppendLog($"[alarm] Server alert from '{cleanSrv}' treated as a standard alert: \"{title}: {n.Message}\"");

        try
        {
            ShowInfoSnackbar(
                Properties.Resources.GetString("UiServerAlertSnackTitle") ?? "Server alert",
                $"{title}: {n.Message}",
                WpfUi.ControlAppearance.Info,
                WpfUi.SymbolRegular.Info24);
        }
        catch (Exception ex)
        {
            AppendLog($"[alarm] Could not show the server alert notice: {ex.Message}");
        }

        var profile = _vm.Servers.FirstOrDefault(p =>
            string.Equals(CleanServerName(p.Name), cleanSrv, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Host, cleanSrv, StringComparison.OrdinalIgnoreCase));
        var ownerSteamId = !string.IsNullOrWhiteSpace(_vm.SteamId64) ? _vm.SteamId64 : profile?.SteamId64 ?? "";

        _ = SendDiscordWebhookAsync(profile, $"🔔 {title}: {n.Message}");
        _ = DiscordBotListenerService.Instance.SendRaidNotificationAsync(
            profile?.PlatformServerKey ?? "",
            ownerSteamId,
            $"🔔 **{title}**: {n.Message}",
            "server_alert",
            fallbackType: "events");
    }
}
