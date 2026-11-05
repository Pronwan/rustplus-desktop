using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

/// <summary>
/// The status side of the Connected Services page: one chip per integration, the plan-usage
/// meters, and the Supporter locks.
///
/// Every card answers "is this set up?" at a glance, so somebody scanning the page sees what is
/// working and what still needs doing without opening each one. The chips only read state the
/// page already loads; nothing here talks to the backend.
/// </summary>
public partial class AppSettingsOverlay
{
    private enum ChipTone { Neutral, Good, Locked, Info, Warn }

    /// <summary>Guild the Discord bot is linked to, as last loaded from the backend.</summary>
    private string? _discordBotGuildId;

    /// <summary>Server Alexa is linked to, as last loaded from the backend.</summary>
    private string? _alexaActiveServerKey;

    private bool _supporterUnlocked;

    private static string FormatTier()
    {
        var tier = Services.Auth.SupabaseAuthManager.CurrentTier ?? "";
        return tier.Length == 0 ? tier : char.ToUpperInvariant(tier[0]) + tier[1..].ToLowerInvariant();
    }

    /// <summary>Plan limits as three meters instead of one line of numbers.</summary>
    private void ApplyCloudUsage(bool connected)
    {
        PnlCloudUsage.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        if (!connected) return;

        int maxBytes = Services.Auth.SupabaseAuthManager.GetMaxOverlayBytes();
        int maxDevices = Services.Auth.SupabaseAuthManager.GetMaxDevices();
        int maxBases = Services.Auth.SupabaseAuthManager.GetMaxBases();

        int overlayKb = ParentWindow != null ? Math.Max(1, (int)Math.Ceiling(ParentWindow.GetCurrentOverlaySizeBytes() / 1024.0)) : 0;
        int devices = ParentWindow?.GetCurrentDevicesCount() ?? 0;
        int bases = ParentWindow?.GetCurrentBaseCount() ?? 0;

        SetUsage(TxtUsageOverlay, BarUsageOverlay, overlayKb, maxBytes == int.MaxValue ? int.MaxValue : maxBytes / 1024, " KB");
        SetUsage(TxtUsageDevices, BarUsageDevices, devices, maxDevices, "");
        SetUsage(TxtUsageBases, BarUsageBases, bases, maxBases, "");
    }

    private static void SetUsage(TextBlock text, ProgressBar bar, int used, int max, string unit)
    {
        bool unlimited = max == int.MaxValue;
        text.Text = unlimited ? $"{used}{unit} / ∞" : $"{used} / {max}{unit}";
        bar.Value = unlimited || max <= 0 ? 0 : Math.Clamp((double)used / max, 0, 1);
        bar.Foreground = !unlimited && max > 0 && used >= max
            ? new SolidColorBrush(Color.FromRgb(0xF0, 0x8A, 0x8A))
            : (Brush)bar.FindResource("Accent");
    }

    /// <summary>
    /// Locks each Supporter card on its own instead of greying out one big block, so the free
    /// cards in between stay usable and a locked card says why and offers the upgrade.
    /// </summary>
    private void ApplySupporterGate(bool connected, bool premium)
    {
        _supporterUnlocked = connected && premium;
        string lockText = connected
            ? T("CsLockSupporter", "Needs a Supporter plan.")
            : T("CsLockSignIn", "Sign in to your RustPlus Desktop account above. Needs a Supporter plan.");

        foreach (var (lockBanner, body) in new (Border, FrameworkElement)[]
                 {
                     (LockOfflineAlerts, PnlOfflineAlertsBody),
                     (LockTelegram, PnlTelegramBody),
                     (LockDiscordBot, PnlDiscordBotBody),
                     (LockAlexa, PnlAlexaBody),
                     (LockHomeAssistant, PnlHomeAssistantBody),
                 })
        {
            lockBanner.Visibility = _supporterUnlocked ? Visibility.Collapsed : Visibility.Visible;
            if (lockBanner.Child is Grid grid && grid.Children.OfType<TextBlock>().FirstOrDefault() is { } text)
            {
                text.Text = lockText;
            }
            body.IsEnabled = _supporterUnlocked;
            body.Opacity = _supporterUnlocked ? 1.0 : 0.5;
        }

        RefreshServiceChips();
    }

    /// <summary>Re-reads every integration's state into its chip. Cheap; call after any change.</summary>
    private void RefreshServiceChips()
    {
        if (ChipCloudAccount == null) return;

        bool connected = Services.Auth.SupabaseAuthManager.IsDiscordAuthenticated
                         || Services.Auth.SupabaseAuthManager.IsEmailAuthenticated;
        string signIn = T("CsChipSignIn", "Sign in first");
        string supporter = T("CsChipSupporter", "Supporter");
        string notSetUp = T("CsChipNotSetUp", "Not set up");
        string unavailable = T("CsChipUnavailable", "Unavailable");

        // Accounts
        if (!connected)
            SetServiceChip(ChipCloudAccount, T("CsChipNotSignedIn", "Not signed in"), ChipTone.Neutral);
        else
            SetServiceChip(ChipCloudAccount, FormatTier(), Services.Auth.SupabaseAuthManager.IsPremium ? ChipTone.Good : ChipTone.Neutral);

        var vm = ParentWindow?.DataContext as ViewModels.MainViewModel ?? DataContext as ViewModels.MainViewModel;
        var steamId = vm?.SteamId64;
        if (string.IsNullOrEmpty(steamId)) steamId = TrackingService.SteamId64;
        bool paired = !string.IsNullOrEmpty(steamId);
        SetServiceChip(ChipRustPlusAccount, paired ? T("CsChipPaired", "Paired") : T("CsChipNotPaired", "Not paired"), paired ? ChipTone.Good : ChipTone.Neutral);
        TxtRustPlusPairingStatus.Text = paired
            ? string.Format(T("CsRustPlusTokenFormat", "Pairing token: {0}"), vm?.FcmExpiryText ?? "")
            : T("CsRustPlusNotPaired", "Pair a server from the main window to start.");

        // Cloud features
        if (!Services.Cloud.CloudFeatureFlags.IsEnabled("cloud_247"))
            SetServiceChip(ChipCloud247, unavailable, ChipTone.Warn);
        else if (!connected)
            SetServiceChip(ChipCloud247, signIn, ChipTone.Neutral);
        else
            SetServiceChip(ChipCloud247, T("CsChipExperimental", "Experimental"), ChipTone.Info);

        if (!connected)
            SetServiceChip(ChipGlobalChat, signIn, ChipTone.Neutral);
        else if (ChkGlobalChatTicker.IsChecked == true)
            SetServiceChip(ChipGlobalChat, T("CsChipTickerOn", "Ticker on"), ChipTone.Good);
        else
            SetServiceChip(ChipGlobalChat, T("CsChipTickerOff", "Ticker hidden"), ChipTone.Neutral);

        // Alerts while the app is closed
        if (!_supporterUnlocked)
        {
            SetLocked(ChipOfflineAlerts);
            SetLocked(ChipTelegram);
        }
        else
        {
            // A saved destination is synced on every edit (TriggerBackgroundFcmSync) and counts as
            // consent (EnsureOfflineIntegrationsConsent), so it is what "active" means here.
            bool hasDestination = !string.IsNullOrWhiteSpace(TrackingService.DiscordWebhookUrl)
                                  || !string.IsNullOrWhiteSpace(TrackingService.SmartHomeWebhookUrl);
            if (hasDestination)
                SetServiceChip(ChipOfflineAlerts, T("CsChipActive", "Active"), ChipTone.Good);
            else if (TrackingService.OfflineIntegrationsConsented)
                SetServiceChip(ChipOfflineAlerts, T("CsChipNoDestination", "Add a destination"), ChipTone.Info);
            else
                SetServiceChip(ChipOfflineAlerts, notSetUp, ChipTone.Neutral);

            if (!string.IsNullOrWhiteSpace(TrackingService.TelegramCallWebhookUrl))
                SetServiceChip(ChipTelegram, T("CsChipActive", "Active"), ChipTone.Good);
            else
                SetServiceChip(ChipTelegram, notSetUp, ChipTone.Neutral);
        }

        // Discord
        bool hasServer = vm?.Selected != null;
        TxtDiscordWebhookNoServer.Visibility = hasServer ? Visibility.Collapsed : Visibility.Visible;
        if (!connected)
            SetServiceChip(ChipDiscordWebhook, signIn, ChipTone.Neutral);
        else if (!hasServer)
            SetServiceChip(ChipDiscordWebhook, T("CsChipNoServer", "No server"), ChipTone.Neutral);
        else if (!string.IsNullOrWhiteSpace(vm!.Selected!.DiscordWebhookChatAlertsUrl))
            SetServiceChip(ChipDiscordWebhook, T("CsChipActive", "Active"), ChipTone.Good);
        else
            SetServiceChip(ChipDiscordWebhook, notSetUp, ChipTone.Neutral);

        if (!_supporterUnlocked)
            SetLocked(ChipDiscordBot);
        else if (!string.IsNullOrEmpty(_discordBotGuildId))
            SetServiceChip(ChipDiscordBot, T("CsChipLinked", "Linked"), ChipTone.Good);
        else
            SetServiceChip(ChipDiscordBot, notSetUp, ChipTone.Neutral);

        // Smart home
        if (!_supporterUnlocked)
            SetLocked(ChipAlexa);
        else if (!Services.Cloud.CloudFeatureFlags.IsEnabled("alexa"))
            SetServiceChip(ChipAlexa, unavailable, ChipTone.Warn);
        else if (!string.IsNullOrEmpty(_alexaActiveServerKey))
            SetServiceChip(ChipAlexa, T("CsChipLinked", "Linked"), ChipTone.Good);
        else
            SetServiceChip(ChipAlexa, notSetUp, ChipTone.Neutral);

        if (!_supporterUnlocked)
            SetLocked(ChipHomeAssistant);
        else if (!Services.Cloud.CloudBackend.UsePlatform || !Services.Cloud.CloudFeatureFlags.IsEnabled("home_assistant"))
            SetServiceChip(ChipHomeAssistant, unavailable, ChipTone.Warn);
        else if (!string.IsNullOrEmpty(TxtHaToken.Text))
            SetServiceChip(ChipHomeAssistant, T("CsChipTokenActive", "Token active"), ChipTone.Good);
        else
            SetServiceChip(ChipHomeAssistant, T("CsChipNoToken", "No token"), ChipTone.Neutral);

        void SetLocked(Border chip) => SetServiceChip(chip, connected ? supporter : signIn, connected ? ChipTone.Locked : ChipTone.Neutral);
    }

    private static void SetServiceChip(Border chip, string text, ChipTone tone)
    {
        var (background, foreground) = tone switch
        {
            ChipTone.Good => (Color.FromRgb(0x1E, 0x3A, 0x2B), Color.FromRgb(0x7B, 0xD8, 0x8F)),
            ChipTone.Locked => (Color.FromRgb(0x3A, 0x2E, 0x15), Color.FromRgb(0xF0, 0xB7, 0x5E)),
            ChipTone.Info => (Color.FromRgb(0x16, 0x35, 0x4A), Color.FromRgb(0x60, 0xCD, 0xFF)),
            ChipTone.Warn => (Color.FromRgb(0x3A, 0x1E, 0x1E), Color.FromRgb(0xF0, 0x8A, 0x8A)),
            _ => (Color.FromRgb(0x2A, 0x2F, 0x37), Color.FromRgb(0xAE, 0xB6, 0xC2)),
        };

        chip.Background = new SolidColorBrush(background);
        if (chip.Child is TextBlock label)
        {
            label.Text = text;
            label.Foreground = new SolidColorBrush(foreground);
        }
        chip.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
}
