using RustPlusDesk.Services.Social;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace RustPlusDesk.Views;

/// <summary>
/// Looking for Group: your own status, who may message you, and (shortly) the listings and inbox.
///
/// Two rules shape this panel. Nothing about you is published until you have read what becomes
/// visible and said so — the consent block appears the moment a mode is picked and the status only
/// commits once it is accepted. And the whole feature needs a cloud account, because an account is
/// what other players message; without one there is nothing for them to reach.
///
/// State lives on the server, not here. Reinstalling or moving to a second machine should not
/// silently drop somebody out of the list they thought they were in.
/// </summary>
public partial class LfgOverlay : UserControl
{
    public event RoutedEventHandler? CloseRequested;
    public event RoutedEventHandler? CloudSetupRequested;

    /// <summary>Set while the code, rather than the user, is ticking a radio button.</summary>
    private bool _suppressEvents;

    /// <summary>The mode waiting for consent, held back until the disclosure is accepted.</summary>
    private LfgMode _pendingMode = LfgMode.None;

    private bool _hasLfgConsent;

    public LfgOverlay()
    {
        InitializeComponent();
        Loaded += (_, __) => _ = RefreshAsync();
    }

    /// <summary>Kept for callers that cannot await; the work still happens.</summary>
    public void Refresh() => _ = RefreshAsync();

    /// <summary>Loads the stored state. Safe to call whenever the panel is opened.</summary>
    public async Task RefreshAsync()
    {
        bool hasCloud = Services.Cloud.CloudAuthManager.IsAuthenticated;
        CloudGate.Visibility = hasCloud ? Visibility.Collapsed : Visibility.Visible;
        Body.Visibility = hasCloud ? Visibility.Visible : Visibility.Collapsed;
        if (!hasCloud) return;

        var settings = await SocialApi.GetSettingsAsync().ConfigureAwait(true);
        var mode = await SocialApi.GetListingAsync().ConfigureAwait(true);

        _suppressEvents = true;
        try
        {
            _hasLfgConsent = settings?.LfgConsent ?? false;

            (mode switch
            {
                LfgMode.LookingForTeam => RbModeLfg,
                LfgMode.LookingForMembers => RbModeLfm,
                _ => RbModeNone,
            }).IsChecked = true;

            ((settings?.Accept ?? AcceptMode.Auto) switch
            {
                AcceptMode.Approval => RbAcceptApproval,
                AcceptMode.Off => RbAcceptOff,
                _ => RbAcceptAuto,
            }).IsChecked = true;

            ConsentPanel.Visibility = Visibility.Collapsed;
            ApplyListedConstraints(mode != LfgMode.None);
        }
        finally
        {
            _suppressEvents = false;
        }

        // A listing expires two days after its last sign of life. Renewing on open keeps somebody
        // who uses the app listed, and lets the entry of somebody who stopped fall away.
        if (mode != LfgMode.None)
            _ = SocialApi.RenewListingAsync();

        await LoadListingsAsync().ConfigureAwait(true);
    }

    private async void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;

        var mode = ReferenceEquals(sender, RbModeLfg) ? LfgMode.LookingForTeam
                 : ReferenceEquals(sender, RbModeLfm) ? LfgMode.LookingForMembers
                 : LfgMode.None;

        // Stopping never needs consent — only appearing does.
        if (mode == LfgMode.None)
        {
            ConsentPanel.Visibility = Visibility.Collapsed;
            _pendingMode = LfgMode.None;
            ApplyListedConstraints(false);
            await SocialApi.SetListingAsync(LfgMode.None).ConfigureAwait(true);
            return;
        }

        if (_hasLfgConsent)
        {
            await PublishAsync(mode).ConfigureAwait(true);
            return;
        }

        _pendingMode = mode;
        ConsentPanel.Visibility = Visibility.Visible;
    }

    private async void BtnConsentAccept_Click(object sender, RoutedEventArgs e)
    {
        ConsentPanel.Visibility = Visibility.Collapsed;

        if (!await SocialApi.ConsentAsync("lfg").ConfigureAwait(true))
        {
            // The disclosure was never recorded, so publishing would be refused anyway. Put the
            // switch back rather than leave the panel claiming a state the server does not have.
            ResetModeToNone();
            return;
        }

        _hasLfgConsent = true;
        await PublishAsync(_pendingMode).ConfigureAwait(true);
        _pendingMode = LfgMode.None;
    }

    private void BtnConsentCancel_Click(object sender, RoutedEventArgs e)
    {
        ResetModeToNone();
        _pendingMode = LfgMode.None;
    }

    private async void Accept_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;

        var mode = ReferenceEquals(sender, RbAcceptApproval) ? AcceptMode.Approval
                 : ReferenceEquals(sender, RbAcceptOff) ? AcceptMode.Off
                 : AcceptMode.Auto;

        await SocialApi.SetAcceptModeAsync(mode).ConfigureAwait(true);
    }

    private async Task PublishAsync(LfgMode mode)
    {
        if (mode == LfgMode.None) return;

        if (await SocialApi.SetListingAsync(mode).ConfigureAwait(true))
        {
            ApplyListedConstraints(true);
            return;
        }

        // Refused — most likely the consent version moved on. Reload rather than guess, so the
        // panel ends up showing whatever is actually stored.
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Refusing all messages while advertising is a contradiction: it puts you in a list nobody
    /// can reach you through. So while a mode is set, that choice is unavailable — and if it was
    /// the active one, it becomes "ask me first" rather than "accept everything". Being listed
    /// should not quietly open the floodgates either.
    /// </summary>
    private void ApplyListedConstraints(bool isListed)
    {
        RbAcceptOff.IsEnabled = !isListed;
        AcceptOffBlockedNote.Visibility = isListed ? Visibility.Visible : Visibility.Collapsed;

        if (!isListed || RbAcceptOff.IsChecked != true) return;

        _suppressEvents = true;
        try { RbAcceptApproval.IsChecked = true; }
        finally { _suppressEvents = false; }

        // The server makes the same switch when a listing goes up; this keeps the panel in step
        // rather than showing a choice that no longer applies.
    }

    private void ResetModeToNone()
    {
        _suppressEvents = true;
        try
        {
            RbModeNone.IsChecked = true;
            ConsentPanel.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    // ── The two lists ───────────────────────────────────────────────────────

    /// <summary>Raised with the player the user wants to write to; the inbox handles it from there.</summary>
    public event EventHandler<Models.LfgEntry>? ChatRequested;

    private bool _languagesPopulated;

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _ = LoadListingsAsync();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressEvents) return;
        _ = LoadListingsAsync();
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
        => Filter_Changed(sender, (RoutedEventArgs)e);

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => _ = LoadListingsAsync();

    private void BtnOpenChat_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Models.LfgEntry entry)
            ChatRequested?.Invoke(this, entry);
    }

    private async Task LoadListingsAsync()
    {
        PopulateLanguages();

        var mode = TabPlayers.IsChecked == true ? LfgMode.LookingForTeam : LfgMode.LookingForMembers;
        var language = (CmbLanguage.SelectedItem as LanguageChoice)?.Code;

        var entries = await SocialApi
            .GetListingsAsync(mode, language, ChkOnlineOnly.IsChecked == true)
            .ConfigureAwait(true);

        ListingList.ItemsSource = entries;

        // An empty board and an unreachable one look the same from here, and the notice says the
        // same thing for both: there is nobody to write to right now.
        ListEmptyNotice.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// The languages we actually ship, so the filter cannot offer one nobody can be listed under.
    /// </summary>
    private void PopulateLanguages()
    {
        if (_languagesPopulated) return;
        _languagesPopulated = true;

        _suppressEvents = true;
        try
        {
            CmbLanguage.ItemsSource = new[]
            {
                new LanguageChoice(null, Properties.Resources.GetString("LfgFilterLanguage")),
                new LanguageChoice("en-US", "English"),
                new LanguageChoice("de-DE", "Deutsch"),
                new LanguageChoice("es-ES", "Español"),
                new LanguageChoice("fr-FR", "Français"),
                new LanguageChoice("ru-RU", "Русский"),
                new LanguageChoice("zh-CN", "简体中文"),
                new LanguageChoice("zh-TW", "繁體中文"),
            };
            CmbLanguage.DisplayMemberPath = nameof(LanguageChoice.Label);
            CmbLanguage.SelectedIndex = 0;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private sealed record LanguageChoice(string? Code, string Label);

    private void BtnCloudSetup_Click(object sender, RoutedEventArgs e)
        => CloudSetupRequested?.Invoke(this, e);

    private void BtnClose_Click(object sender, RoutedEventArgs e)
        => CloseRequested?.Invoke(this, e);
}
