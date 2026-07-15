using System;
using System.Linq;
using System.Windows;
using RustPlusDesk.Services.Auth;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views.Windows;

public partial class CloudAccountWindow : Window
{
    private readonly MainWindow _owner;

    public CloudAccountWindow(MainWindow owner)
    {
        InitializeComponent();
        Owner = owner;
        _owner = owner;
        RefreshAccountData();
    }

    private void RefreshAccountData()
    {
        var user = SupabaseAuthManager.Client?.Auth?.CurrentUser;
        var identities = user?.Identities;
        bool hasDiscord = identities?.Any(identity => string.Equals(identity.Provider, "discord", StringComparison.OrdinalIgnoreCase)) == true;
        bool hasEmail = identities?.Any(identity => string.Equals(identity.Provider, "email", StringComparison.OrdinalIgnoreCase)) == true;
        string tier = FriendlyTier(SupabaseAuthManager.CurrentTier);

        TxtPlanBadge.Text = SupabaseAuthManager.IsPremium ? $"{tier} · Premium" : $"{tier} · Free";
        TxtAccountSummary.Text = TrackingService.CloudSyncEnabled ? "Connected · cloud sync enabled" : "Connected · cloud sync paused";
        TxtDisplayName.Text = GetDisplayName(user?.UserMetadata) ?? user?.Email ?? "Cloud user";
        TxtEmail.Text = string.IsNullOrWhiteSpace(user?.Email) ? "Not linked" : user.Email;
        TxtUserId.Text = user?.Id ?? "Unavailable";
        TxtSteamAccount.Text = string.IsNullOrWhiteSpace(_owner.ViewModel.SteamId64)
            ? "Not connected"
            : $"{_owner.SteamDisplayName} · {_owner.ViewModel.SteamId64}";

        TxtDiscordState.Text = hasDiscord ? "Linked to this cloud account" : "Not linked";
        BtnLinkDiscord.Content = hasDiscord ? "Linked" : "Link Discord";
        BtnLinkDiscord.IsEnabled = !hasDiscord;

        TxtEmailState.Text = hasEmail ? "Email login enabled" : "Add an email and password to this account";
        BtnShowEmailLink.Content = hasEmail ? "Enabled" : "Add login";
        BtnShowEmailLink.IsEnabled = !hasEmail;
        if (!hasEmail && !string.IsNullOrWhiteSpace(user?.Email))
            TxtLinkEmail.Text = user.Email;

        SetUsage(TxtDevicesUsage, ProgressDevices, _owner.GetCurrentDevicesCount(), SupabaseAuthManager.GetMaxDevices(), value => value.ToString());
        SetUsage(TxtBasesUsage, ProgressBases, _owner.GetCurrentBaseCount(), SupabaseAuthManager.GetMaxBases(), value => value.ToString());
        SetUsage(TxtOverlayUsage, ProgressOverlay, _owner.GetCurrentOverlaySizeBytes(), SupabaseAuthManager.GetMaxOverlayBytes(), FormatBytes);
        int screenshotLimit = SupabaseAuthManager.GetMaxScreenshotsPerBase();
        TxtScreenshotLimit.Text = $"Screenshots per base: {(screenshotLimit == int.MaxValue ? "Unlimited" : screenshotLimit)}";
    }

    private static string? GetDisplayName(System.Collections.Generic.Dictionary<string, object>? metadata)
    {
        if (metadata == null) return null;
        foreach (string key in new[] { "full_name", "name", "user_name", "preferred_username" })
            if (metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value?.ToString()))
                return value.ToString();
        return null;
    }

    private static string FriendlyTier(string tier) =>
        string.Join(" ", (string.IsNullOrWhiteSpace(tier) ? "free" : tier).Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    private static void SetUsage(System.Windows.Controls.TextBlock label, System.Windows.Controls.ProgressBar bar,
        int current, int limit, Func<int, string> formatter)
    {
        bool unlimited = limit == int.MaxValue;
        label.Text = $"{formatter(current)} / {(unlimited ? "Unlimited" : formatter(limit))}";
        bar.Value = unlimited || limit <= 0 ? 0 : Math.Clamp(current * 100.0 / limit, 0, 100);
        bar.IsIndeterminate = false;
    }

    private static string FormatBytes(int bytes)
    {
        if (bytes >= 1024 * 1024) return $"{bytes / 1024d / 1024d:0.#} MB";
        return $"{Math.Max(0, bytes) / 1024d:0.#} KB";
    }

    private async void BtnLinkDiscord_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Complete the Discord authorization in your browser...");
        var (success, error) = await SupabaseAuthManager.LinkDiscordIdentityAsync();
        SetBusy(false, success ? "Discord is now linked to this cloud account." : error ?? "Discord linking failed.");
        if (success) RefreshAccountData();
    }

    private void BtnShowEmailLink_Click(object sender, RoutedEventArgs e)
    {
        PanelEmailLink.Visibility = PanelEmailLink.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void BtnAddEmailLogin_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Adding email login...");
        var (success, error) = await SupabaseAuthManager.AddEmailLoginAsync(TxtLinkEmail.Text.Trim(), PwdLinkEmail.Password);
        PwdLinkEmail.Clear();
        SetBusy(false, success
            ? "Email login added. Check your inbox if Supabase asks you to confirm the address."
            : error ?? "Could not add email login.");
        if (success) RefreshAccountData();
    }

    private async void BtnLogout_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Signing out...");
        await SupabaseAuthManager.LogoutAsync();
        _owner.UpdateCloudSyncUI();
        DialogResult = true;
        Close();
    }

    private void SetBusy(bool busy, string message)
    {
        BtnLinkDiscord.IsEnabled = !busy && BtnLinkDiscord.Content?.ToString() != "Linked";
        BtnAddEmailLogin.IsEnabled = !busy;
        BtnLogout.IsEnabled = !busy;
        PanelStatus.Visibility = Visibility.Visible;
        TxtStatus.Text = message;
        TxtStatus.Foreground = busy ? System.Windows.Media.Brushes.LightSkyBlue : System.Windows.Media.Brushes.White;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
