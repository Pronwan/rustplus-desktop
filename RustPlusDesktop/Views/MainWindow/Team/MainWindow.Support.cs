using System.Threading.Tasks;
using System.Windows;
using RustPlusDesk.Services.Social;
using RustPlusDesk.Services.Support;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private bool _supportWired;

    /// <summary>
    /// Opens the Support panel — tickets and the notification centre — from the map rail. A panel
    /// rather than a window, like LFG, so it sits over the map the user is already looking at.
    /// </summary>
    private void BtnSupportPanel_Click(object sender, RoutedEventArgs e)
    {
        if (SupportPanel.Visibility == Visibility.Visible)
        {
            SupportPanel.Visibility = Visibility.Collapsed;
            return;
        }

        EnsureSupportWired();

        SupportPanel.Refresh();
        SupportPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Hooks the panel up, once. Called from the social-availability refresh at start rather than
    /// only on first open, so the rail carries an unread number before anybody has opened anything —
    /// a badge that only appears after you have already looked is telling you what you know.
    /// </summary>
    private void EnsureSupportWired()
    {
        if (_supportWired) return;
        _supportWired = true;

        SupportPanel.CloseRequested += (_, __) => SupportPanel.Visibility = Visibility.Collapsed;

        // The panel owns the count while it is open; the rail shows it whether or not it is.
        SupportPanel.UnreadChanged += UpdateSupportBadge;

        // Live: a ticket reply or an announcement lands on the private user channel the social layer
        // already subscribes to. If the panel is open it refreshes in place; if not, the rail count
        // bumps so the badge is right the next time it is looked at.
        SocialRealtime.EnsureStarted();
        SocialRealtime.NotificationArrived += OnSupportNotificationArrived;

        _ = RefreshSupportUnreadAsync();
    }

    private void OnSupportNotificationArrived()
    {
        Dispatcher.Invoke(() =>
        {
            if (SupportPanel.Visibility == Visibility.Visible)
                SupportPanel.RefreshNotifications();
            else
                _ = RefreshSupportUnreadAsync();
        });
    }

    private async Task RefreshSupportUnreadAsync()
    {
        var unread = await SupportApi.GetUnreadCountAsync().ConfigureAwait(true);
        UpdateSupportBadge(unread);
    }

    /// <summary>Puts the unread count on the Tickets rail button, as a small badge over its icon.</summary>
    private void UpdateSupportBadge(int count)
    {
        RailTicketsBadgeText.Text = count > 99 ? "99+" : count.ToString();
        RailTicketsBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
