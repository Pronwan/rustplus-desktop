using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using RustPlusDesk.Services.Support;

namespace RustPlusDesk.Views;

/// <summary>
/// Support, from the client's side: file a ticket about anything, follow the thread, and read the
/// one inbox everything lands in. A panel rather than a window so it sits where the user already is.
///
/// It talks only to <see cref="SupportApi"/> and holds no state the server does not - every action
/// re-reads, so what the panel shows and what the dashboard shows can never quietly disagree.
/// </summary>
public partial class SupportOverlay : UserControl
{
    /// <summary>Raised when the user closes the panel, so the host can collapse it.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Raised with the current unread count whenever it changes, for the rail badge.</summary>
    public event Action<int>? UnreadChanged;

    private static readonly Brush CardBrush = Brush("#FF111820");
    private static readonly Brush StaffBrush = Brush("#FF14202B");
    private static readonly Brush InternalBrush = Brush("#33E8A33C");
    private static readonly Brush UnreadBrush = Brush("#1AE8683C");
    private static readonly Brush AccentDot = Brush("#FFE8683C");
    private static readonly Brush MutedDot = Brush("#FF5C6572");

    private readonly ObservableCollection<TicketVm> _tickets = new();
    private readonly ObservableCollection<MessageVm> _messages = new();
    private readonly ObservableCollection<NotificationVm> _notifications = new();

    private TicketMeta? _meta;
    private string? _openTicketId;
    private List<string> _composeFiles = new();
    private List<string> _replyFiles = new();
    private bool _busy;

    public SupportOverlay()
    {
        InitializeComponent();
        TicketsList.ItemsSource = _tickets;
        ThreadMessages.ItemsSource = _messages;
        NotificationsList.ItemsSource = _notifications;
    }

    /// <summary>Reloads everything and drops the user back on the ticket list.</summary>
    public async void Refresh()
    {
        ShowTickets();
        await Task.WhenAll(LoadTicketsAsync(), LoadNotificationsAsync(), LoadMetaAsync()).ConfigureAwait(true);
    }

    // ── Loading ─────────────────────────────────────────────────────────────

    private async Task LoadTicketsAsync()
    {
        var rows = await SupportApi.GetTicketsAsync().ConfigureAwait(true);
        _tickets.Clear();
        foreach (var t in rows)
            _tickets.Add(new TicketVm(t));
        TicketsEmpty.Visibility = _tickets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadNotificationsAsync()
    {
        var rows = await SupportApi.GetNotificationsAsync().ConfigureAwait(true);
        _notifications.Clear();
        var unread = 0;
        foreach (var n in rows)
        {
            _notifications.Add(new NotificationVm(n));
            if (!n.Read) unread++;
        }
        NotificationsEmpty.Visibility = _notifications.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SetUnread(unread);
    }

    private async Task LoadMetaAsync()
    {
        _meta = await SupportApi.GetMetaAsync().ConfigureAwait(true);
        ComposeCategory.ItemsSource = _meta.Categories.Select(TicketVm.CategoryLabelFor).ToList();
        if (ComposeCategory.SelectedIndex < 0 && _meta.Categories.Count > 0)
        {
            var helpIndex = _meta.Categories.ToList().FindIndex(c => c == "help");
            ComposeCategory.SelectedIndex = helpIndex >= 0 ? helpIndex : 0;
        }
        ComposeAppeal.Visibility = _meta.Appealable != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetUnread(int count)
    {
        UnreadCount.Text = count > 99 ? "99+" : count.ToString();
        UnreadPill.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UnreadChanged?.Invoke(count);
    }

    /// <summary>
    /// Reloads just the notification list and count, without yanking the user out of whatever ticket
    /// they are reading. Called when a notification arrives live while the panel is open.
    /// </summary>
    public async void RefreshNotifications() => await LoadNotificationsAsync().ConfigureAwait(true);

    // ── View switching ──────────────────────────────────────────────────────

    private void ShowTickets()
    {
        TabTickets.IsChecked = true;
        TabNotifications.IsChecked = false;
        TicketsView.Visibility = Visibility.Visible;
        ComposeView.Visibility = Visibility.Collapsed;
        ThreadView.Visibility = Visibility.Collapsed;
        NotificationsView.Visibility = Visibility.Collapsed;
    }

    private void ShowNotifications()
    {
        TabTickets.IsChecked = false;
        TabNotifications.IsChecked = true;
        TicketsView.Visibility = Visibility.Collapsed;
        ComposeView.Visibility = Visibility.Collapsed;
        ThreadView.Visibility = Visibility.Collapsed;
        NotificationsView.Visibility = Visibility.Visible;
    }

    private void ShowCompose()
    {
        TicketsView.Visibility = Visibility.Collapsed;
        ComposeView.Visibility = Visibility.Visible;
        ThreadView.Visibility = Visibility.Collapsed;
        NotificationsView.Visibility = Visibility.Collapsed;
    }

    private void ShowThread()
    {
        TicketsView.Visibility = Visibility.Collapsed;
        ComposeView.Visibility = Visibility.Collapsed;
        ThreadView.Visibility = Visibility.Visible;
        NotificationsView.Visibility = Visibility.Collapsed;
    }

    // ── Handlers ────────────────────────────────────────────────────────────

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void TabTickets_Click(object sender, RoutedEventArgs e) => ShowTickets();

    private void TabNotifications_Click(object sender, RoutedEventArgs e) => ShowNotifications();

    private void NewTicket_Click(object sender, RoutedEventArgs e)
    {
        ComposeSubject.Text = "";
        ComposeBody.Text = "";
        ComposeFiles(new List<string>());
        ComposeAppeal.IsChecked = false;
        ComposeError.Visibility = Visibility.Collapsed;
        ShowCompose();
    }

    private void ComposeCancel_Click(object sender, RoutedEventArgs e) => ShowTickets();

    private void ComposeAttach_Click(object sender, RoutedEventArgs e)
    {
        var picked = PickFiles();
        if (picked != null) ComposeFiles(picked);
    }

    private void ComposeFiles(List<string> files)
    {
        _composeFiles = files;
        ComposeAttachCount.Text = files.Count > 0 ? $"{files.Count} file(s)" : "";
    }

    private async void ComposeSubmit_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var subject = ComposeSubject.Text?.Trim() ?? "";
        var body = ComposeBody.Text?.Trim() ?? "";
        if (subject.Length == 0 || body.Length == 0)
        {
            ComposeError.Text = "A subject and some detail are needed.";
            ComposeError.Visibility = Visibility.Visible;
            return;
        }

        var categories = _meta?.Categories ?? new List<string> { "help" };
        var category = ComposeCategory.SelectedIndex >= 0 && ComposeCategory.SelectedIndex < categories.Count
            ? categories[ComposeCategory.SelectedIndex]
            : "help";

        string? sanctionId = null;
        if (category == "appeal" && ComposeAppeal.IsChecked == true && _meta?.Appealable != null)
            sanctionId = _meta.Appealable.Id;

        _busy = true;
        BtnComposeSubmit.IsEnabled = false;
        try
        {
            var ok = await SupportApi.CreateTicketAsync(category, subject, body, DesktopContext(), _composeFiles, sanctionId).ConfigureAwait(true);
            if (!ok)
            {
                ComposeError.Text = "That could not be sent. Try again in a moment.";
                ComposeError.Visibility = Visibility.Visible;
                return;
            }
            ShowTickets();
            await LoadTicketsAsync().ConfigureAwait(true);
        }
        finally
        {
            _busy = false;
            BtnComposeSubmit.IsEnabled = true;
        }
    }

    private async void TicketRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string id)
            await OpenTicketAsync(id).ConfigureAwait(true);
    }

    private async Task OpenTicketAsync(string id)
    {
        var detail = await SupportApi.GetTicketAsync(id).ConfigureAwait(true);
        if (detail == null) return;

        _openTicketId = id;
        ThreadSubject.Text = detail.Subject;
        ThreadStatus.Text = $"{TicketVm.CategoryLabelFor(detail.Category)} · {TicketVm.StatusLabelFor(detail.Status)}";

        _messages.Clear();
        _messages.Add(MessageVm.Original(detail));
        foreach (var m in detail.Messages)
        {
            if (m.Kind == "system")
                continue; // the client keeps the thread to what was actually said
            _messages.Add(new MessageVm(m));
        }

        var closed = detail.Status == "closed";
        ReplyBar.Visibility = closed ? Visibility.Collapsed : Visibility.Visible;
        ThreadClosed.Visibility = closed ? Visibility.Visible : Visibility.Collapsed;
        ReplyBox.Text = "";
        _replyFiles = new List<string>();

        ShowThread();
        await SupportApi.MarkTicketReadAsync(id).ConfigureAwait(true);
        await LoadTicketsAsync().ConfigureAwait(true);
    }

    private void ThreadBack_Click(object sender, RoutedEventArgs e) => ShowTickets();

    private void ReplyAttach_Click(object sender, RoutedEventArgs e)
    {
        var picked = PickFiles();
        if (picked != null) _replyFiles = picked;
    }

    private async void ReplySend_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _openTicketId == null) return;
        var body = ReplyBox.Text?.Trim() ?? "";
        if (body.Length == 0) return;

        _busy = true;
        BtnReplySend.IsEnabled = false;
        try
        {
            var ok = await SupportApi.ReplyAsync(_openTicketId, body, _replyFiles).ConfigureAwait(true);
            if (ok)
            {
                ReplyBox.Text = "";
                _replyFiles = new List<string>();
                await OpenTicketAsync(_openTicketId).ConfigureAwait(true);
            }
        }
        finally
        {
            _busy = false;
            BtnReplySend.IsEnabled = true;
        }
    }

    private async void MarkAll_Click(object sender, RoutedEventArgs e)
    {
        await SupportApi.MarkAllNotificationsReadAsync().ConfigureAwait(true);
        await LoadNotificationsAsync().ConfigureAwait(true);
    }

    private async void NotificationRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string id)
            return;

        var vm = _notifications.FirstOrDefault(n => n.Id == id);
        await SupportApi.MarkNotificationReadAsync(id).ConfigureAwait(true);

        // A ticket notification opens its ticket right here rather than sending them to the web.
        if (vm?.TicketId is string ticketId && ticketId.Length > 0)
        {
            await OpenTicketAsync(ticketId).ConfigureAwait(true);
        }

        await LoadNotificationsAsync().ConfigureAwait(true);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static List<string>? PickFiles()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Attachments|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.txt;*.log;*.json;*.zip;*.dmp;*.mp4|All files|*.*",
        };
        return dialog.ShowDialog() == true ? dialog.FileNames.ToList() : null;
    }

    /// <summary>The app version and OS, filed with every ticket so the first question is answered.</summary>
    private static Dictionary<string, string> DesktopContext() => new()
    {
        ["app_version"] = Helpers.VersionHelper.GetClientVersion(),
        ["os"] = Environment.OSVersion.VersionString,
    };

    private static Brush Brush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    // ── View models ─────────────────────────────────────────────────────────

    public sealed class TicketVm
    {
        public string Id { get; }
        public string Subject { get; }
        public string CategoryLabel { get; }
        public string StatusLabel { get; }
        public string When { get; }
        public Visibility UnreadVisibility { get; }

        public TicketVm(TicketSummary t)
        {
            Id = t.Id;
            Subject = t.Subject;
            CategoryLabel = CategoryLabelFor(t.Category);
            StatusLabel = StatusLabelFor(t.Status);
            When = t.LastActivityAt?.LocalDateTime.ToString("g") ?? "";
            UnreadVisibility = t.HasUnread ? Visibility.Visible : Visibility.Collapsed;
        }

        public static string CategoryLabelFor(string c) => c switch
        {
            "appeal" => "Appeal", "bug" => "Bug", "feature" => "Feature", "help" => "Help", _ => "Other",
        };

        public static string StatusLabelFor(string s) => s switch
        {
            "open" => "Open", "in_progress" => "In progress", "awaiting_user" => "Awaiting you",
            "resolved" => "Resolved", "closed" => "Closed", _ => s,
        };
    }

    public sealed class MessageVm
    {
        public string Author { get; }
        public string Body { get; }
        public string When { get; }
        public Brush Background { get; }

        public MessageVm(TicketMessage m)
        {
            Author = m.Kind == "internal" ? (m.AuthorName ?? "Staff") + " · internal" : (m.AuthorName ?? "Staff");
            Body = m.Body;
            When = m.CreatedAt?.LocalDateTime.ToString("g") ?? "";
            Background = m.Kind == "internal" ? InternalBrush : (m.IsStaff ? StaffBrush : CardBrush);
        }

        private MessageVm(string author, string body, string when)
        {
            Author = author;
            Body = body;
            When = when;
            Background = CardBrush;
        }

        public static MessageVm Original(TicketDetail d) => new("You", d.Body, d.CreatedAt?.LocalDateTime.ToString("g") ?? "");
    }

    public sealed class NotificationVm
    {
        public string Id { get; }
        public string Title { get; }
        public string Body { get; }
        public string When { get; }
        public string? TicketId { get; }
        public Brush Dot { get; }
        public Brush Background { get; }

        public NotificationVm(NotificationItem n)
        {
            Id = n.Id;
            Title = n.Title;
            Body = n.Body;
            When = n.CreatedAt?.LocalDateTime.ToString("g") ?? "";
            Dot = n.Read ? MutedDot : AccentDot;
            Background = n.Read ? CardBrush : UnreadBrush;
            // Ticket notifications carry the id in their url tail: /dashboard/tickets/{id}.
            TicketId = n.Url is { } url && url.Contains("/tickets/")
                ? url.Substring(url.LastIndexOf('/') + 1)
                : null;
        }
    }
}
