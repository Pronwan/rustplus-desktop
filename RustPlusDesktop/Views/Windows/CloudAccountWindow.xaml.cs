using System;
using System.Linq;
using System.Windows;
using RustPlusDesk.Services.Auth;
using RustPlusDesk.Services;
using Wpf.Ui.Controls;

namespace RustPlusDesk.Views.Windows;

public partial class CloudAccountWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly MainWindow _owner;
    private bool _hasDiscord;
    private bool _hasEmail;

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
        bool hasDiscord = _hasDiscord = SupabaseAuthManager.HasAuthProvider("discord");
        bool hasEmail = _hasEmail = SupabaseAuthManager.HasAuthProvider("email");
        string tier = FriendlyTier(SupabaseAuthManager.CurrentTier);

        // Identity fields come from whichever backend is active. Under the platform
        // backend the Supabase client is null, so read the cached platform user
        // instead — otherwise display name / email / id fall back to placeholders.
        var platformUser = RustPlusDesk.Services.Cloud.CloudBackend.UsePlatform
            ? RustPlusDesk.Services.Cloud.CloudAuthManager.CurrentUser
            : null;
        string? displayName = platformUser != null ? platformUser.DisplayName : GetDisplayName(user?.UserMetadata);
        string? email = platformUser != null ? platformUser.Email : user?.Email;
        string? userId = platformUser != null ? platformUser.Id : user?.Id;

        PlanBadge.Content = string.Format(Properties.Resources.GetString(SupabaseAuthManager.IsPremium ? "FormatPremiumPlan" : "FormatFreePlan"), tier);
        // Premium reads as a soft success tint, free as a neutral chip - both are
        // tints of the surface so the badge never shouts over the account details.
        PlanBadge.Background = (System.Windows.Media.Brush)FindResource(
            SupabaseAuthManager.IsPremium ? "SuccessTintBrush" : "CardBorder");
        PlanBadge.Foreground = (System.Windows.Media.Brush)FindResource(
            SupabaseAuthManager.IsPremium ? "SuccessTextBrush" : "TextSubtle");
        TxtAccountSummary.Text = TrackingService.CloudSyncEnabled ? RustPlusDesk.Properties.Resources.GetString("CodeUiConnectedCloudSyncEnabled") : RustPlusDesk.Properties.Resources.GetString("CodeUiConnectedCloudSyncPaused");
        TxtDisplayName.Text = displayName ?? email ?? RustPlusDesk.Properties.Resources.GetString("CodeUiCloudUser");
        TxtEmail.Text = string.IsNullOrWhiteSpace(email) ? RustPlusDesk.Properties.Resources.GetString("CodeUiNotLinked") : email;
        TxtUserId.Text = userId ?? RustPlusDesk.Properties.Resources.GetString("CodeUiUnavailable");
        TxtSteamAccount.Text = string.IsNullOrWhiteSpace(_owner.ViewModel.SteamId64)
            ? "Not connected"
            : $"{_owner.SteamDisplayName} · {_owner.ViewModel.SteamId64}";

        TxtDiscordState.Text = hasDiscord ? RustPlusDesk.Properties.Resources.GetString("CodeUiLinkedToThisCloudAccount") : RustPlusDesk.Properties.Resources.GetString("CodeUiNotLinked");
        BtnLinkDiscord.Content = hasDiscord ? RustPlusDesk.Properties.Resources.GetString("CodeUiLinked") : RustPlusDesk.Properties.Resources.GetString("CodeUiLinkDiscord");
        BtnLinkDiscord.IsEnabled = !hasDiscord;

        TxtEmailState.Text = hasEmail ? RustPlusDesk.Properties.Resources.GetString("CodeUiEmailLoginEnabled") : RustPlusDesk.Properties.Resources.GetString("CodeUiAddAnEmailAndPasswordToThisAccount");
        BtnShowEmailLink.Content = hasEmail ? RustPlusDesk.Properties.Resources.GetString("CodeUiEnabled") : RustPlusDesk.Properties.Resources.GetString("UiAddLogin");
        BtnShowEmailLink.IsEnabled = !hasEmail;
        if (!hasEmail && !string.IsNullOrWhiteSpace(email))
            TxtLinkEmail.Text = email;

        SetUsage(TxtDevicesUsage, ProgressDevices, _owner.GetCurrentDevicesCount(), SupabaseAuthManager.GetMaxDevices(), value => value.ToString());
        SetUsage(TxtBasesUsage, ProgressBases, _owner.GetCurrentBaseCount(), SupabaseAuthManager.GetMaxBases(), value => value.ToString());
        SetUsage(TxtOverlayUsage, ProgressOverlay, _owner.GetCurrentOverlaySizeBytes(), SupabaseAuthManager.GetMaxOverlayBytes(), FormatBytes);
        int screenshotLimit = SupabaseAuthManager.GetMaxScreenshotsPerBase();
        TxtScreenshotLimit.Text = $"Screenshots per base: {(screenshotLimit == int.MaxValue ? RustPlusDesk.Properties.Resources.GetString("CodeUiUnlimited") : screenshotLimit)}";
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
        label.Text = $"{formatter(current)} / {(unlimited ? RustPlusDesk.Properties.Resources.GetString("CodeUiUnlimited") : formatter(limit))}";
        bar.Value = unlimited || limit <= 0 ? 0 : Math.Clamp(current * 100.0 / limit, 0, 100);
        bar.IsIndeterminate = false;
        // An empty track under "Unlimited" reads as a maxed-out quota - hide it.
        bar.Visibility = unlimited || limit <= 0 ? Visibility.Collapsed : Visibility.Visible;
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
        SetBusy(false, success ? "Discord is now linked to this cloud account." : error ?? "Discord linking failed.",
            success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
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
        PwdLinkEmail.Password = string.Empty;
        SetBusy(false, success
            ? error ?? "Email login added. Check your inbox if Supabase asks you to confirm the address."
            : error ?? "Could not add email login.",
            success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        if (success) RefreshAccountData();
    }

    private async void BtnLogout_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Signing out...");
        await Services.Cloud.CloudAuth.LogoutAsync();
        _owner.UpdateCloudSyncUI();
        DialogResult = true;
        Close();
    }

    private void SetBusy(bool busy, string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        BtnLinkDiscord.IsEnabled = !busy && !_hasDiscord;
        BtnShowEmailLink.IsEnabled = !busy && !_hasEmail;
        BtnAddEmailLogin.IsEnabled = !busy;
        BtnLogout.IsEnabled = !busy;
        BtnWebPortal.IsEnabled = !busy;
        InfoStatus.Severity = busy ? InfoBarSeverity.Informational : severity;
        InfoStatus.Message = message;
        InfoStatus.IsOpen = true;
    }

    private void BtnWebPortal_Click(object sender, RoutedEventArgs e)
    {
        // Same host the client talks to, so the portal always matches this build's backend.
        string url = $"{Services.Data.DataManager.CLOUD_API_BASEURL.TrimEnd('/')}/dashboard";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetBusy(false, ex.Message, InfoBarSeverity.Error);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
