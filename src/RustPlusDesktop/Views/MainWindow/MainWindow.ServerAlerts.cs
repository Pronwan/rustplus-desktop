using System;
using System.Linq;
using System.Text.RegularExpressions;
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
        var title = ServerAlertTitle(n.Title, n.Message);
        var text = (n.Message ?? "").Trim();
        var notice = title.Length > 0 && text.Length > 0 ? $"{title}: {text}" : title.Length > 0 ? title : text;
        AppendLog($"[alarm] Server alert from '{cleanSrv}' treated as a standard alert: \"{notice}\"");

        try
        {
            ShowInfoSnackbar(
                Properties.Resources.GetString("UiServerAlertSnackTitle") ?? "Server alert",
                notice,
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

        _ = SendDiscordWebhookAsync(profile, $"🔔 Server Alert: {notice}");
        _ = DiscordBotListenerService.Instance.SendRaidNotificationAsync(
            profile?.PlatformServerKey ?? "",
            ownerSteamId,
            ServerAlertDiscordText(asRaid: false, n.Title, n.Message, cleanSrv),
            "server_alert",
            fallbackType: "events");
    }

    /// <summary>
    /// A server alert for Discord, as the cloud posts it. Our own label first, so it reads the
    /// same on every server and says how the owner classed it; then the plugin's title, which may
    /// carry what the alert is about ("Cargo ship spawned"), and its text; then the server's name
    /// as one short grey line - on modded servers the full name runs to a paragraph.
    /// </summary>
    internal static string ServerAlertDiscordText(bool asRaid, string? title, string? message, string? server)
    {
        var label = asRaid ? "🚨 **Alarm**" : "🔔 **Server Alert**";
        var t = ServerAlertTitle(title, message);
        var body = (message ?? "").Trim();

        var text = label + (t.Length > 0 ? $" – {t}" : "") + (body.Length > 0 ? $"\n{body}" : "");

        var name = Regex.Replace(server ?? "", @"\s+", " ").Trim();
        if (name.Length > 0 && name != "-")
        {
            if (name.Length > 45) name = name[..44].TrimEnd() + "…";
            text += "\n-# " + name;
        }
        return text;
    }

    /// <summary>The plugin's title when it says something the text does not, else empty.</summary>
    private static string ServerAlertTitle(string? title, string? message)
    {
        var t = (title ?? "").Trim();
        if (t.Length == 0 || t == "Rust+ Notification") return "";
        return string.Equals(t, (message ?? "").Trim(), StringComparison.OrdinalIgnoreCase) ? "" : t;
    }
}
