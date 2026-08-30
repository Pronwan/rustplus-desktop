using System;
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
/// </summary>
public partial class LfgOverlay : UserControl
{
    public event RoutedEventHandler? CloseRequested;
    public event RoutedEventHandler? CloudSetupRequested;

    /// <summary>Set while the code, rather than the user, is ticking a radio button.</summary>
    private bool _suppressEvents;

    /// <summary>The mode waiting for consent, held back until the disclosure is accepted.</summary>
    private LfgMode _pendingMode = LfgMode.None;

    public LfgOverlay()
    {
        InitializeComponent();
        Loaded += (_, __) => Refresh();
    }

    public enum LfgMode { None, LookingForTeam, LookingForMembers }

    /// <summary>Reloads from stored state. Safe to call whenever the panel is opened.</summary>
    public void Refresh()
    {
        bool hasCloud = Services.Cloud.CloudAuthManager.IsAuthenticated;
        CloudGate.Visibility = hasCloud ? Visibility.Collapsed : Visibility.Visible;
        Body.Visibility = hasCloud ? Visibility.Visible : Visibility.Collapsed;
        if (!hasCloud) return;

        _suppressEvents = true;
        try
        {
            // TODO(social-layer): read from GET lfg/me and GET dm/settings once they exist.
            RbModeNone.IsChecked = true;
            RbAcceptAuto.IsChecked = true;
            ConsentPanel.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
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
            // TODO(social-layer): DELETE lfg/me
            return;
        }

        if (HasGivenConsent())
        {
            ApplyListedConstraints(true);
            // TODO(social-layer): PUT lfg/me with the chosen mode
            return;
        }

        _pendingMode = mode;
        ConsentPanel.Visibility = Visibility.Visible;
    }

    private void BtnConsentAccept_Click(object sender, RoutedEventArgs e)
    {
        RecordConsent();
        ConsentPanel.Visibility = Visibility.Collapsed;
        // TODO(social-layer): POST lfg/consent, then PUT lfg/me with _pendingMode
        ApplyListedConstraints(_pendingMode != LfgMode.None);
        _pendingMode = LfgMode.None;
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

        // TODO(social-layer): PUT dm/settings with accept_mode=approval
    }

    private void BtnConsentCancel_Click(object sender, RoutedEventArgs e)
    {
        // Declining puts the radio back rather than leaving a mode selected that was never
        // published — the panel must not claim a state the server does not have.
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
        _pendingMode = LfgMode.None;
    }

    private void Accept_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        // TODO(social-layer): PUT dm/settings with the chosen accept mode
    }

    private void BtnCloudSetup_Click(object sender, RoutedEventArgs e)
        => CloudSetupRequested?.Invoke(this, e);

    private void BtnClose_Click(object sender, RoutedEventArgs e)
        => CloseRequested?.Invoke(this, e);

    // TODO(social-layer): both back onto social_settings.lfg_consent_version from the API. Local
    // for now so the flow can be exercised before the endpoints exist.
    private static bool HasGivenConsent() => false;

    private static void RecordConsent() { }
}
