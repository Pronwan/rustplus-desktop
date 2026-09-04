using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RustPlusDesk.Services.Social;
using RustPlusDesk.Services.Support;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private bool _supportWired;

    private static readonly Brush NotifUnreadBg = FrozenBrush("#1AE8683C");
    private static readonly Brush NotifReadBg = FrozenBrush("#FF111820");
    private static readonly Brush NotifAccentDot = FrozenBrush("#FFE8683C");
    private static readonly Brush NotifMutedDot = FrozenBrush("#FF5C6572");

    private readonly ObservableCollection<NotificationRowVm> _topNotifications = new();

    /// <summary>
    /// The Tickets rail button switches to the Tickets tab (see CompactSidebarTab_Click), so it
    /// sits in the workspace like Recycler rather than floating over the device panel. Selecting the
    /// tab refreshes it via MainTabs_SelectionChanged; closing it returns to Devices.
    /// </summary>
    private void SupportPanel_CloseRequested(object sender, EventArgs e)
    {
        MainTabs.SelectedItem = DevicesTabItem;
    }

    /// <summary>
    /// Hooks the notification centre up, once. Called from the social-availability refresh at start
    /// rather than only on first open, so the title-bar bell carries a count before anybody has
    /// opened anything — a badge that only appears after you have looked is telling you what you know.
    /// </summary>
    private void EnsureSupportWired()
    {
        if (_supportWired) return;
        _supportWired = true;

        TopNotificationsList.ItemsSource = _topNotifications;

        // Live: a ticket reply or an announcement lands on the private user channel the social layer
        // already subscribes to. The bell bumps whether or not the dropdown is open.
        SocialRealtime.EnsureStarted();
        SocialRealtime.NotificationArrived += OnSupportNotificationArrived;

        _ = RefreshNotificationBadgeAsync();
    }

    // ── The title-bar bell ──────────────────────────────────────────────────

    private async void BtnTopNotifications_Click(object sender, RoutedEventArgs e)
    {
        EnsureSupportWired();

        if (NotificationsPopup.IsOpen)
        {
            NotificationsPopup.IsOpen = false;
            return;
        }

        await LoadNotificationsAsync().ConfigureAwait(true);
        NotificationsPopup.IsOpen = true;
    }

    private void OnSupportNotificationArrived()
    {
        Dispatcher.Invoke(() =>
        {
            if (NotificationsPopup.IsOpen)
                _ = LoadNotificationsAsync();
            else
                _ = RefreshNotificationBadgeAsync();
        });
    }

    private async Task LoadNotificationsAsync()
    {
        var rows = await SupportApi.GetNotificationsAsync().ConfigureAwait(true);

        _topNotifications.Clear();
        var unread = 0;
        foreach (var n in rows)
        {
            _topNotifications.Add(new NotificationRowVm(n));
            if (!n.Read) unread++;
        }

        TopNotificationsEmpty.Visibility = _topNotifications.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateNotificationBadge(unread);
    }

    private async Task RefreshNotificationBadgeAsync()
    {
        UpdateNotificationBadge(await SupportApi.GetUnreadCountAsync().ConfigureAwait(true));
    }

    private void UpdateNotificationBadge(int count)
    {
        TopNotifBadgeText.Text = count > 99 ? "99+" : count.ToString();
        TopNotifBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void BtnMarkAllRead_Click(object sender, RoutedEventArgs e)
    {
        await SupportApi.MarkAllNotificationsReadAsync().ConfigureAwait(true);
        await LoadNotificationsAsync().ConfigureAwait(true);
    }

    private async void TopNotificationRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string id)
            return;

        var vm = _topNotifications.FirstOrDefault(n => n.Id == id);
        await SupportApi.MarkNotificationReadAsync(id).ConfigureAwait(true);

        // A ticket notification opens its ticket in the Tickets tab rather than sending the user to
        // the web. Selecting the tab refreshes the list; then jump straight into the ticket.
        if (vm?.TicketId is string ticketId && ticketId.Length > 0)
        {
            NotificationsPopup.IsOpen = false;
            EnsureSupportWired();
            MainTabs.SelectedItem = TicketsTab;
            await SupportPanel.OpenTicketAsync(ticketId).ConfigureAwait(true);
        }

        await LoadNotificationsAsync().ConfigureAwait(true);
    }

    private static Brush FrozenBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    /// <summary>One row in the title-bar notification dropdown.</summary>
    public sealed class NotificationRowVm
    {
        public string Id { get; }
        public string Title { get; }
        public string Body { get; }
        public string When { get; }
        public string? TicketId { get; }
        public Brush Dot { get; }
        public Brush Background { get; }

        public NotificationRowVm(NotificationItem n)
        {
            Id = n.Id;
            Title = n.Title;
            Body = n.Body;
            When = n.CreatedAt?.LocalDateTime.ToString("g") ?? "";
            Dot = n.Read ? NotifMutedDot : NotifAccentDot;
            Background = n.Read ? NotifReadBg : NotifUnreadBg;
            // Ticket notifications carry the id in their url tail: /dashboard/tickets/{id}.
            TicketId = n.Url is { } url && url.Contains("/tickets/")
                ? url.Substring(url.LastIndexOf('/') + 1)
                : null;
        }
    }
}
