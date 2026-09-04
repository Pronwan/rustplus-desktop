using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RustPlusDesk.Services.Social;
using RustPlusDesk.Services.Support;
using WpfUi = Wpf.Ui.Controls;

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

        // The panel owns the ticket-unread count while it is open (it drops the moment a ticket is
        // read); the rail badge mirrors it whether or not the panel is open.
        SupportPanel.TicketsUnreadChanged += UpdateTicketsBadge;

        // Live: a ticket reply or an announcement lands on the private user channel the social layer
        // already subscribes to. The bell bumps whether or not the dropdown is open.
        SocialRealtime.EnsureStarted();
        SocialRealtime.NotificationArrived += OnSupportNotificationArrived;

        _ = RefreshNotificationBadgeAsync();
        _ = RefreshTicketsBadgeAsync();
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

    private void OnSupportNotificationArrived(SocialRealtime.NotificationInfo info)
    {
        Dispatcher.Invoke(() =>
        {
            // Chime and toast so a reply or announcement is noticed even away from the panel, then
            // it settles into the bell with its unread count.
            PlayTicketChime();

            var appearance = info.Level switch
            {
                "critical" => WpfUi.ControlAppearance.Danger,
                "warning" => WpfUi.ControlAppearance.Caution,
                "success" => WpfUi.ControlAppearance.Success,
                _ => WpfUi.ControlAppearance.Info,
            };

            // A distinct icon and a source tag so a support/announcement toast reads apart from an
            // in-game Rust+ notification at a glance - not just another blue "info" bubble.
            var isAnnouncement = info.Type == "announcement";
            var icon = isAnnouncement ? WpfUi.SymbolRegular.Megaphone24 : WpfUi.SymbolRegular.ChatHelp24;
            var tag = isAnnouncement ? "Announcement" : "Support";
            var title = string.IsNullOrWhiteSpace(info.Title) ? tag : $"{tag} · {info.Title}";

            ShowInfoSnackbar(title, info.Body, appearance, icon);

            if (NotificationsPopup.IsOpen)
                _ = LoadNotificationsAsync();
            else
                _ = RefreshNotificationBadgeAsync();

            // A reply usually means a ticket just gained unread activity - keep its badge honest.
            _ = RefreshTicketsBadgeAsync();
        });
    }

    private System.Windows.Media.MediaPlayer? _ticketChimePlayer;

    /// <summary>
    /// Plays the distinct ticket chime (Assets/notification-incoming.mp3) via MediaPlayer, since
    /// SoundPlayer only handles WAV. Falls back to the shared notification sound if the file is not
    /// present, so there is always a chime.
    /// </summary>
    private void PlayTicketChime()
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "notification-incoming.mp3");
            if (!System.IO.File.Exists(path))
            {
                PlayNotificationSound("icq-message.wav");
                return;
            }

            if (_ticketChimePlayer == null)
            {
                _ticketChimePlayer = new System.Windows.Media.MediaPlayer();
                _ticketChimePlayer.Open(new Uri(path, UriKind.Absolute));
            }
            _ticketChimePlayer.Position = TimeSpan.Zero;
            _ticketChimePlayer.Play();
        }
        catch (Exception ex)
        {
            AppendLog($"[TicketChime] {ex.Message}");
        }
    }

    /// <summary>Counts the user's tickets with unread activity and shows it on the rail button.</summary>
    private async Task RefreshTicketsBadgeAsync()
    {
        var tickets = await SupportApi.GetTicketsAsync().ConfigureAwait(true);
        UpdateTicketsBadge(tickets.Count(t => t.HasUnread));
    }

    private void UpdateTicketsBadge(int count)
    {
        RailTicketsBadgeText.Text = count > 99 ? "99+" : count.ToString();
        RailTicketsBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
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

    private async void BtnClearNotifications_Click(object sender, RoutedEventArgs e)
    {
        await SupportApi.ClearAllNotificationsAsync().ConfigureAwait(true);
        _topNotifications.Clear();
        TopNotificationsEmpty.Visibility = Visibility.Visible;
        UpdateNotificationBadge(0);
    }

    private async void NotificationDismiss_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not NotificationRowVm vm)
            return;

        await SupportApi.DismissNotificationAsync(vm.Id).ConfigureAwait(true);
        await LoadNotificationsAsync().ConfigureAwait(true);
    }

    private void NotificationCta_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is NotificationRowVm { Url: { Length: > 0 } url })
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { /* the OS declined to open the link */ }
        }
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
        public string? Url { get; }
        public string? CtaLabel { get; }
        public Visibility CtaVisibility => !string.IsNullOrEmpty(Url) && !string.IsNullOrEmpty(CtaLabel) ? Visibility.Visible : Visibility.Collapsed;
        public Brush Dot { get; }
        public Brush Background { get; }

        public NotificationRowVm(NotificationItem n)
        {
            Id = n.Id;
            Title = n.Title;
            Body = n.Body;
            When = n.CreatedAt?.LocalDateTime.ToString("g") ?? "";
            Url = n.Url;
            CtaLabel = n.CtaLabel;
            Dot = n.Read ? NotifMutedDot : NotifAccentDot;
            Background = n.Read ? NotifReadBg : NotifUnreadBg;
            // Ticket notifications carry the id in their url tail: /dashboard/tickets/{id}.
            TicketId = n.Url is { } url && url.Contains("/tickets/")
                ? url.Substring(url.LastIndexOf('/') + 1)
                : null;
        }
    }
}
