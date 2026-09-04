using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using RustPlusDesk.Services.Support;

namespace RustPlusDesk.Views;

/// <summary>
/// Tickets, from the client's side: file one about anything, follow the thread, reply, and see the
/// files on both sides - a screenshot you attach previews before you send it, and one on a message
/// shows in the thread. It talks only to <see cref="SupportApi"/> and re-reads on every action.
/// </summary>
public partial class SupportOverlay : UserControl
{
    /// <summary>Raised when the user closes the panel, so the host can collapse/switch away.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Raised with the count of the user's tickets that have unread activity, for the rail badge.</summary>
    public event Action<int>? TicketsUnreadChanged;

    private static readonly Brush CardBrush = MakeBrush("#FF161C24");
    private static readonly Brush StaffBrush = MakeBrush("#FF14202B");
    private static readonly Brush InternalBrush = MakeBrush("#33E8A33C");

    private static readonly IReadOnlyList<string> FallbackCategories = new[] { "appeal", "bug", "feature", "help", "other" };

    private readonly ObservableCollection<TicketVm> _tickets = new();
    private readonly ObservableCollection<MessageVm> _messages = new();
    private readonly ObservableCollection<AttachmentPickVm> _composeAttachments = new();
    private readonly ObservableCollection<AttachmentPickVm> _replyAttachments = new();

    private IReadOnlyList<string> _categories = new List<string>();
    private AppealableSanction? _appealable;
    private string? _openTicketId;
    private bool _busy;

    /// <summary>Paths auto-attached for a bug report, tracked so switching category can drop them.</summary>
    private readonly List<string> _autoDiagnostics = new();

    /// <summary>
    /// Files this client has uploaded this session, keyed by "name|size", so a message it sent shows
    /// its own image straight from the local copy instead of fetching it back from the server.
    /// </summary>
    private readonly Dictionary<string, string> _localUploads = new();

    public SupportOverlay()
    {
        InitializeComponent();
        TicketsList.ItemsSource = _tickets;
        ThreadMessages.ItemsSource = _messages;
        ComposeAttachments.ItemsSource = _composeAttachments;
        ReplyAttachments.ItemsSource = _replyAttachments;
    }

    /// <summary>Reloads the list and drops the user back on it.</summary>
    public async void Refresh()
    {
        ShowTicketsList();
        await Task.WhenAll(LoadTicketsAsync(), LoadMetaAsync()).ConfigureAwait(true);
    }

    // ── Loading ─────────────────────────────────────────────────────────────

    private async Task LoadTicketsAsync()
    {
        var rows = await SupportApi.GetTicketsAsync().ConfigureAwait(true);
        _tickets.Clear();
        foreach (var t in rows)
            _tickets.Add(new TicketVm(t));
        TicketsEmpty.Visibility = _tickets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Keep the rail badge in step whenever the list is (re)loaded - including right after a
        // ticket is opened and marked read, which is when the count should drop.
        TicketsUnreadChanged?.Invoke(rows.Count(t => t.HasUnread));
    }

    private async Task LoadMetaAsync()
    {
        var meta = await SupportApi.GetMetaAsync().ConfigureAwait(true);
        _categories = meta.Categories.Count > 0 ? meta.Categories : FallbackCategories;
        _appealable = meta.Appealable;

        ComposeCategory.ItemsSource = _categories.Select(TicketVm.CategoryLabelFor).ToList();
        if (ComposeCategory.SelectedIndex < 0 && _categories.Count > 0)
        {
            var helpIndex = _categories.ToList().FindIndex(c => c == "help");
            ComposeCategory.SelectedIndex = helpIndex >= 0 ? helpIndex : 0;
        }
        ComposeAppeal.Visibility = _appealable != null ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── View switching ──────────────────────────────────────────────────────

    private void ShowTicketsList()
    {
        TicketsView.Visibility = Visibility.Visible;
        ComposeView.Visibility = Visibility.Collapsed;
        ThreadView.Visibility = Visibility.Collapsed;
    }

    private void ShowCompose()
    {
        TicketsView.Visibility = Visibility.Collapsed;
        ComposeView.Visibility = Visibility.Visible;
        ThreadView.Visibility = Visibility.Collapsed;
    }

    private void ShowThread()
    {
        TicketsView.Visibility = Visibility.Collapsed;
        ComposeView.Visibility = Visibility.Collapsed;
        ThreadView.Visibility = Visibility.Visible;
    }

    // ── Handlers ────────────────────────────────────────────────────────────

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void NewTicket_Click(object sender, RoutedEventArgs e)
    {
        ComposeSubject.Text = "";
        ComposeBody.Text = "";
        _composeAttachments.Clear();
        _autoDiagnostics.Clear();
        // If the form opens already on the bug category, its logs come along from the start.
        if (SelectedCategory() == "bug") AddBugDiagnostics();
        UpdateComposeAttachHint();
        ComposeAppeal.IsChecked = false;
        ComposeError.Visibility = Visibility.Collapsed;
        ShowCompose();
    }

    /// <summary>The category slug currently picked, or empty.</summary>
    private string SelectedCategory()
        => ComposeCategory.SelectedIndex >= 0 && ComposeCategory.SelectedIndex < _categories.Count
            ? _categories[ComposeCategory.SelectedIndex]
            : "";

    /// <summary>
    /// A bug report carries the client's own diagnostics automatically - a fresh log snapshot and
    /// the latest crash report - so staff are not asking a frustrated user to find their log folder.
    /// Attached visibly as removable chips, and dropped again if the category changes off "bug".
    /// </summary>
    private void ComposeCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComposeView.Visibility != Visibility.Visible)
            return; // ignore the programmatic selection made while the form is closed

        if (SelectedCategory() == "bug") AddBugDiagnostics();
        else RemoveBugDiagnostics();
    }

    private void AddBugDiagnostics()
    {
        foreach (var path in Services.CrashReporter.CollectSupportDiagnostics())
        {
            if (_autoDiagnostics.Contains(path) || _composeAttachments.Any(a => a.Path == path))
                continue;
            _composeAttachments.Add(new AttachmentPickVm(path));
            _autoDiagnostics.Add(path);
        }
        UpdateComposeAttachHint();
    }

    private void RemoveBugDiagnostics()
    {
        foreach (var path in _autoDiagnostics.ToList())
        {
            var vm = _composeAttachments.FirstOrDefault(a => a.Path == path);
            if (vm != null) _composeAttachments.Remove(vm);
        }
        _autoDiagnostics.Clear();
        UpdateComposeAttachHint();
    }

    private void ComposeCancel_Click(object sender, RoutedEventArgs e) => ShowTicketsList();

    private void ComposeAttach_Click(object sender, RoutedEventArgs e)
    {
        var (accepted, rejected) = SplitBySize(PickFiles());

        foreach (var path in accepted)
            _composeAttachments.Add(new AttachmentPickVm(path));

        if (rejected.Count > 0)
        {
            ComposeError.Text = TooLargeMessage(rejected);
            ComposeError.Visibility = Visibility.Visible;
        }

        UpdateComposeAttachHint();
    }

    private void RemoveComposeAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is AttachmentPickVm vm)
        {
            _composeAttachments.Remove(vm);
            UpdateComposeAttachHint();
        }
    }

    private void UpdateComposeAttachHint()
        => ComposeAttachCount.Text = _composeAttachments.Count > 0 ? $"{_composeAttachments.Count} attached" : "";

    private async void ComposeSubmit_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var subject = ComposeSubject.Text?.Trim() ?? "";
        var body = ComposeBody.Text?.Trim() ?? "";
        if (subject.Length == 0 || body.Length == 0)
        {
            ComposeError.Text = RustPlusDesk.Helpers.Loc.Text("SupportNeedSubjectAndDetail", "A subject and some detail are needed.");
            ComposeError.Visibility = Visibility.Visible;
            return;
        }

        var category = ComposeCategory.SelectedIndex >= 0 && ComposeCategory.SelectedIndex < _categories.Count
            ? _categories[ComposeCategory.SelectedIndex]
            : "help";

        string? sanctionId = null;
        if (category == "appeal" && ComposeAppeal.IsChecked == true && _appealable != null)
            sanctionId = _appealable.Id;

        _busy = true;
        BtnComposeSubmit.IsEnabled = false;
        try
        {
            RecordLocalUploads(_composeAttachments);
            var files = _composeAttachments.Select(a => a.Path).ToList();
            var ok = await SupportApi.CreateTicketAsync(category, subject, body, DesktopContext(), files, sanctionId).ConfigureAwait(true);
            if (!ok)
            {
                ComposeError.Text = RustPlusDesk.Helpers.Loc.Text("SupportSendFailed", "That could not be sent. Try again in a moment.");
                ComposeError.Visibility = Visibility.Visible;
                return;
            }
            ShowTicketsList();
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

    /// <summary>Opens a ticket's thread. Public so the notification bell can jump straight to one.</summary>
    public async Task OpenTicketAsync(string id)
    {
        var detail = await SupportApi.GetTicketAsync(id).ConfigureAwait(true);
        if (detail == null) return;

        _openTicketId = id;
        ThreadSubject.Text = detail.Subject;
        ThreadStatus.Text = $"{TicketVm.CategoryLabelFor(detail.Category)} · {TicketVm.StatusLabelFor(detail.Status)}";

        // Before the thumbnails start loading, point any attachment this client uploaded at its
        // local file, so its image renders inline without a server fetch.
        SeedLocalUploads(detail.Attachments);
        foreach (var m in detail.Messages)
            SeedLocalUploads(m.Attachments);

        _messages.Clear();
        _messages.Add(MessageVm.Original(detail, id));
        foreach (var m in detail.Messages)
        {
            if (m.Kind == "system")
                continue;
            _messages.Add(new MessageVm(m, id));
        }

        // Resolved or closed locks the thread for the filer - the conversation is over; more to
        // say is a new ticket.
        var locked = detail.Status is "closed" or "resolved";
        ReplyBar.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        ThreadClosed.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        ThreadClosed.Text = detail.Status == "resolved"
            ? RustPlusDesk.Helpers.Loc.Text("SupportTicketResolved", "This ticket is resolved. Open a new ticket if you still need help.")
            : RustPlusDesk.Helpers.Loc.Text("SupportTicketClosed", "This ticket is closed.");
        ReplyBox.Text = "";
        _replyAttachments.Clear();

        ShowThread();
        await SupportApi.MarkTicketReadAsync(id).ConfigureAwait(true);
        await LoadTicketsAsync().ConfigureAwait(true);
    }

    private void ThreadBack_Click(object sender, RoutedEventArgs e) => ShowTicketsList();

    private void ReplyAttach_Click(object sender, RoutedEventArgs e)
    {
        var (accepted, rejected) = SplitBySize(PickFiles());

        foreach (var path in accepted)
            _replyAttachments.Add(new AttachmentPickVm(path));

        if (rejected.Count > 0) ThreadStatus.Text = TooLargeMessage(rejected);
    }

    private void RemoveReplyAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is AttachmentPickVm vm)
            _replyAttachments.Remove(vm);
    }

    private async void ReplySend_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _openTicketId == null) return;
        var body = ReplyBox.Text?.Trim() ?? "";
        if (body.Length == 0 && _replyAttachments.Count == 0) return;

        _busy = true;
        BtnReplySend.IsEnabled = false;
        try
        {
            RecordLocalUploads(_replyAttachments);
            var files = _replyAttachments.Select(a => a.Path).ToList();
            var ok = await SupportApi.ReplyAsync(_openTicketId, body.Length == 0 ? "(see attachment)" : body, files).ConfigureAwait(true);
            if (ok)
            {
                ReplyBox.Text = "";
                _replyAttachments.Clear();
                await OpenTicketAsync(_openTicketId).ConfigureAwait(true);
            }
        }
        finally
        {
            _busy = false;
            BtnReplySend.IsEnabled = true;
        }
    }

    /// <summary>Opens a sent attachment in whatever the OS uses for its type.</summary>
    private async void MessageAttachment_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not MessageAttachmentVm vm)
            return;

        var path = await SupportApi.SaveAttachmentToTempAsync(vm.TicketId, vm.Id, vm.Name).ConfigureAwait(true);
        if (path == null) return;

        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { /* nothing we can do if the OS refuses to open it */ }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Largest file we will attach. There was no ceiling at all, and the picker
    /// offers .zip, .dmp and .mp4 — any of which runs to hundreds of megabytes.
    /// The server takes more than this in its request validation, but its media
    /// pipeline caps at ten, so an oversized file was accepted here and then
    /// failed somewhere the reporter could not see. Refusing it by name, before
    /// anything is sent, is the version they can act on. Ten matches that
    /// server-side ceiling deliberately: a full-screen PNG screenshot runs to
    /// several megabytes, and there should be no size the client accepts and
    /// the server then refuses.
    /// </summary>
    private const long MaxAttachmentBytes = 10L * 1024 * 1024;

    /// <summary>Splits picked paths into what we will send and what was too big.</summary>
    private static (List<string> Accepted, List<string> Rejected) SplitBySize(IEnumerable<string> paths)
    {
        var accepted = new List<string>();
        var rejected = new List<string>();

        foreach (var path in paths)
        {
            long size;
            try { size = new FileInfo(path).Length; }
            catch { continue; }   // vanished between the dialog closing and this

            if (size > MaxAttachmentBytes) rejected.Add(System.IO.Path.GetFileName(path));
            else accepted.Add(path);
        }

        return (accepted, rejected);
    }

    private static string TooLargeMessage(IReadOnlyList<string> rejected) =>
        string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            RustPlusDesk.Helpers.Loc.Text("SupportAttachmentTooLarge", "Not attached — {0} is larger than {1} MB."),
            string.Join(", ", rejected),
            MaxAttachmentBytes / (1024 * 1024));
    private static IEnumerable<string> PickFiles()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Attachments|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.txt;*.log;*.json;*.zip;*.dmp;*.mp4|All files|*.*",
        };
        return dialog.ShowDialog() == true ? dialog.FileNames : Array.Empty<string>();
    }

    private static Dictionary<string, string> DesktopContext() => new()
    {
        ["app_version"] = Helpers.VersionHelper.GetClientVersion(),
        ["os"] = Environment.OSVersion.VersionString,
    };

    /// <summary>Remembers the local path of each file being sent, keyed by name + byte size.</summary>
    private void RecordLocalUploads(IEnumerable<AttachmentPickVm> picks)
    {
        foreach (var p in picks)
        {
            try { _localUploads[$"{p.Name}|{new FileInfo(p.Path).Length}"] = p.Path; }
            catch { /* a file that vanished is simply not remembered */ }
        }
    }

    /// <summary>Points server attachments at their local copy (by name + size) so images show inline.</summary>
    private void SeedLocalUploads(IReadOnlyList<TicketAttachment> attachments)
    {
        foreach (var a in attachments)
            if (_localUploads.TryGetValue($"{a.Name}|{a.Size}", out var localPath))
                SupportApi.SeedAttachmentCache(a.Id, a.Name, localPath);
    }

    private static Brush MakeBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private static bool IsImageMime(string? mime) => mime != null && mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    private static bool IsImagePath(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp";
    }

    private static ImageSource? ThumbFromFile(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 120;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private static ImageSource? ThumbFromBytes(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 220;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024} KB";
        return $"{bytes / 1024d / 1024d:0.0} MB";
    }

    // ── View models ─────────────────────────────────────────────────────────

    // Status palette: each state reads at a glance instead of every pill being the same grey.
    private static readonly Brush StatusOpenBg = MakeBrush("#243FA9FF");
    private static readonly Brush StatusOpenFg = MakeBrush("#FF7FC1FF");
    private static readonly Brush StatusProgBg = MakeBrush("#33E0A33C");
    private static readonly Brush StatusProgFg = MakeBrush("#FFE7BB63");
    private static readonly Brush StatusWaitBg = MakeBrush("#2A9B6BFF");
    private static readonly Brush StatusWaitFg = MakeBrush("#FFB79CFF");
    private static readonly Brush StatusDoneBg = MakeBrush("#2E3FBF6A");
    private static readonly Brush StatusDoneFg = MakeBrush("#FF74D89B");
    private static readonly Brush StatusClosedBg = MakeBrush("#18FFFFFF");
    private static readonly Brush StatusClosedFg = MakeBrush("#FF7C8794");

    public sealed class TicketVm
    {
        public string Id { get; }
        public string Subject { get; }
        public string CategoryLabel { get; }
        public string StatusLabel { get; }
        public Brush StatusBackground { get; }
        public Brush StatusForeground { get; }
        public string When { get; }
        public Visibility UnreadVisibility { get; }

        public TicketVm(TicketSummary t)
        {
            Id = t.Id;
            Subject = t.Subject;
            CategoryLabel = CategoryLabelFor(t.Category);
            StatusLabel = StatusLabelFor(t.Status);
            (StatusBackground, StatusForeground) = t.Status switch
            {
                "open" => (StatusOpenBg, StatusOpenFg),
                "in_progress" => (StatusProgBg, StatusProgFg),
                "awaiting_user" => (StatusWaitBg, StatusWaitFg),
                "resolved" => (StatusDoneBg, StatusDoneFg),
                "closed" => (StatusClosedBg, StatusClosedFg),
                _ => (StatusClosedBg, StatusClosedFg),
            };
            When = t.LastActivityAt?.LocalDateTime.ToString("g") ?? "";
            UnreadVisibility = t.HasUnread ? Visibility.Visible : Visibility.Collapsed;
        }

        public static string CategoryLabelFor(string c) => c switch
        {
            "appeal" => RustPlusDesk.Helpers.Loc.Text("SupportCatAppeal", "Appeal"),
            "bug" => RustPlusDesk.Helpers.Loc.Text("SupportCatBug", "Bug"),
            "feature" => RustPlusDesk.Helpers.Loc.Text("SupportCatFeature", "Feature"),
            "help" => RustPlusDesk.Helpers.Loc.Text("SupportCatHelp", "Help"),
            _ => RustPlusDesk.Helpers.Loc.Text("SupportCatOther", "Other"),
        };

        public static string StatusLabelFor(string s) => s switch
        {
            "open" => RustPlusDesk.Helpers.Loc.Text("SupportStatusOpen", "Open"),
            "in_progress" => RustPlusDesk.Helpers.Loc.Text("SupportStatusInProgress", "In progress"),
            "awaiting_user" => RustPlusDesk.Helpers.Loc.Text("SupportStatusAwaitingUser", "Awaiting you"),
            "resolved" => RustPlusDesk.Helpers.Loc.Text("SupportStatusResolved", "Resolved"),
            "closed" => RustPlusDesk.Helpers.Loc.Text("SupportStatusClosed", "Closed"),
            _ => s,
        };
    }

    public sealed class MessageVm
    {
        public string Author { get; }
        public string Body { get; }
        public string When { get; }
        public Brush Background { get; }
        public Visibility BodyVisibility { get; }
        public ObservableCollection<MessageAttachmentVm> Attachments { get; } = new();

        public MessageVm(TicketMessage m, string ticketId)
        {
            Author = m.Kind == "internal" ? (m.AuthorName ?? "Staff") + " · internal" : (m.AuthorName ?? "Staff");
            Body = m.Body;
            When = m.CreatedAt?.LocalDateTime.ToString("g") ?? "";
            Background = m.Kind == "internal" ? InternalBrush : (m.IsStaff ? StaffBrush : CardBrush);
            BodyVisibility = string.IsNullOrWhiteSpace(m.Body) ? Visibility.Collapsed : Visibility.Visible;
            foreach (var a in m.Attachments)
                Attachments.Add(new MessageAttachmentVm(ticketId, a));
        }

        private MessageVm(string author, string body, string when)
        {
            Author = author;
            Body = body;
            When = when;
            Background = CardBrush;
            BodyVisibility = Visibility.Visible;
        }

        public static MessageVm Original(TicketDetail d, string ticketId)
        {
            var vm = new MessageVm("You", d.Body, d.CreatedAt?.LocalDateTime.ToString("g") ?? "");
            foreach (var a in d.Attachments)
                vm.Attachments.Add(new MessageAttachmentVm(ticketId, a));
            return vm;
        }
    }

    /// <summary>
    /// An attachment already on a message. An image shows as a thumbnail once it has loaded (cached
    /// locally on first fetch); until then - and if it cannot be fetched at all - it shows as a file
    /// chip, so there is never a blank box where a picture should be.
    /// </summary>
    public sealed class MessageAttachmentVm : INotifyPropertyChanged
    {
        public string TicketId { get; }
        public string Id { get; }
        public string Name { get; }
        public string SizeLabel { get; }
        public bool IsImage { get; }

        // The image box appears only once there is actually a thumbnail to put in it; the chip
        // covers every other moment - a non-image, a still-loading image, or one that failed.
        public Visibility ImageVisibility => _thumb != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ChipVisibility => _thumb != null ? Visibility.Collapsed : Visibility.Visible;

        private ImageSource? _thumb;
        public ImageSource? Thumb
        {
            get => _thumb;
            private set
            {
                _thumb = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumb)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChipVisibility)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public MessageAttachmentVm(string ticketId, TicketAttachment a)
        {
            TicketId = ticketId;
            Id = a.Id;
            Name = a.Name;
            SizeLabel = FormatSize(a.Size);
            IsImage = IsImageMime(a.Mime) || IsImagePath(a.Name);
            if (IsImage)
                _ = LoadThumbAsync();
        }

        private async Task LoadThumbAsync()
        {
            var bytes = await SupportApi.GetAttachmentCachedAsync(TicketId, Id, Name).ConfigureAwait(true);
            if (bytes != null)
                Thumb = ThumbFromBytes(bytes);
        }
    }

    /// <summary>A file the user has picked but not yet sent - previewed locally, no round trip.</summary>
    public sealed class AttachmentPickVm
    {
        public string Path { get; }
        public string Name { get; }
        public bool IsImage { get; }
        public Visibility ImageVisibility => IsImage ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ChipVisibility => IsImage ? Visibility.Collapsed : Visibility.Visible;
        public ImageSource? Thumb { get; }

        public AttachmentPickVm(string path)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path);
            IsImage = IsImagePath(path);
            Thumb = IsImage ? ThumbFromFile(path) : null;
        }
    }
}
