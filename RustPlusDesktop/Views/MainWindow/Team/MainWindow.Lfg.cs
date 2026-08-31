using RustPlusDesk.Services;
using System.Threading.Tasks;
using System.Windows;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private bool _lfgWired;

    /// <summary>
    /// Opens the Looking for Group panel from the Team tab. It is a panel rather than a window so
    /// it sits where the team already is — you are looking for people to play with while looking
    /// at the people you play with.
    /// </summary>
    private void BtnLfg_Click(object sender, RoutedEventArgs e)
    {
        if (LfgPanel.Visibility == Visibility.Visible)
        {
            LfgPanel.Visibility = Visibility.Collapsed;
            return;
        }

        EnsureLfgWired();

        LfgPanel.Refresh();
        LfgPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Opens the Community area from the rail. Same panel as the Team tab's LFG button for now —
    /// global chat arrives here next, and the two share one inbox, so they share one surface
    /// rather than two that each hold half the messages.
    /// </summary>
    private void BtnSocial_Click(object sender, RoutedEventArgs e) => BtnLfg_Click(sender, e);

    /// <summary>
    /// Hooks the panel up, once.
    ///
    /// Called at start rather than only on first open, because the unread count belongs on the
    /// rail before anybody has opened anything - a badge that only appears after you have already
    /// looked is telling you what you know.
    /// </summary>
    private void EnsureLfgWired()
    {
        if (_lfgWired) return;
        _lfgWired = true;

        LfgPanel.CloseRequested += (_, __) => LfgPanel.Visibility = Visibility.Collapsed;

        LfgPanel.CloudSetupRequested += (_, __) =>
        {
            // Same dialog the cloud icon under the device list opens, so there is one place that
            // explains cloud rather than two that drift apart.
            LfgPanel.Visibility = Visibility.Collapsed;
            new Views.Windows.CloudAccountWindow(this).ShowDialog();
        };

        LfgPanel.SupporterOfferRequested += (_, __) =>
        {
            // The same window every other premium feature opens. A second explanation of what
            // supporting buys would be a second thing to keep true.
            LfgPanel.Visibility = Visibility.Collapsed;
            ShowPremiumLimitDialog(Properties.Resources.GetString("SupporterGateBody"));
        };

        LfgPanel.UnreadChanged += (_, count) => ShowSocialUnread(count);
    }

    /// <summary>The same number the Inbox tab carries, on the rail.</summary>
    private void ShowSocialUnread(int count)
    {
        RailSocialBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RailSocialBadgeText.Text = count > 9 ? "9+" : count.ToString();
    }

    /// <summary>
    /// Shows or hides the Community entry according to whether the platform has opened the layer
    /// to this account.
    ///
    /// The feature is rolled out in stages, and during the early ones most accounts are behind
    /// the door. A rail button that leads to "not for you yet" every time is worse than no
    /// button - it reads as something broken rather than something coming.
    ///
    /// Only a definite no hides it. Signed out we cannot ask, and the panel's own cloud gate is
    /// the right answer there; unreachable is not a no either.
    /// </summary>
    public async Task RefreshSocialAvailabilityAsync()
    {
        EnsureLfgWired();

        if (!Services.Cloud.CloudAuthManager.IsAuthenticated)
        {
            SetSocialRailVisible(true);
            return;
        }

        var settings = await Services.Social.SocialApi.GetSettingsAsync().ConfigureAwait(true);

        SetSocialRailVisible(settings?.Enabled ?? true);
    }

    private void SetSocialRailVisible(bool visible)
    {
        var state = visible ? Visibility.Visible : Visibility.Collapsed;

        // The divider above it goes too. The one below stays, so the rule between the tools and
        // the pin/settings pair survives the button disappearing from between them.
        RailSocialButton.Visibility = state;
        RailSocialDivider.Visibility = state;

        // The count goes with the button it belongs to.
        if (!visible) RailSocialBadge.Visibility = Visibility.Collapsed;

        if (!visible) LfgPanel.Visibility = Visibility.Collapsed;
    }
}
