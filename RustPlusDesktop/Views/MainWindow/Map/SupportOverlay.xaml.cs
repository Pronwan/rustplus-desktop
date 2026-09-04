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
        foreach (var path in PickFiles())
            _composeAttachments.Add(new AttachmentPickVm(path));
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
            ComposeError.Text = "A subject and some detail are needed.";
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
            var files = _composeAttachments.Select(a => a.Path).ToList();
            var ok = await SupportApi.CreateTicketAsync(category, subject, body, DesktopContext(), files, sanctionId).ConfigureAwait(true);
            if (!ok)
            {
                ComposeError.Text = "That could not be sent. Try again in a moment.";
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

        _messages.Clear();
        _messages.Add(MessageVm.Original(detail, id));
        foreach (var m in detail.Messages)
        {
            if (m.Kind == "system")
                continue;
            _messages.Add(new MessageVm(m, id));
        }

        var closed = detail.Status == "closed";
        ReplyBar.Visibility = closed ? Visibility.Collapsed : Visibility.Visible;
        ThreadClosed.Visibility = closed ? Visibility.Visible : Visibility.Collapsed;
        ReplyBox.Text = "";
        _replyAttachments.Clear();

        ShowThread();
        await SupportApi.MarkTicketReadAsync(id).ConfigureAwait(true);
        await LoadTicketsAsync().ConfigureAwait(true);
    }

    private void ThreadBack_Click(object sender, RoutedEventArgs e) => ShowTicketsList();

    private void ReplyAttach_Click(object sender, RoutedEventArgs e)
    {
        foreach (var path in PickFiles())
            _replyAttachments.Add(new AttachmentPickVm(path));
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

    /// <summary>An attachment already on a message. Image thumbnails load from the server in the background.</summary>
    public sealed class MessageAttachmentVm : INotifyPropertyChanged
    {
        public string TicketId { get; }
        public string Id { get; }
        public string Name { get; }
        public string SizeLabel { get; }
        public bool IsImage { get; }
        public Visibility ImageVisibility => IsImage ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ChipVisibility => IsImage ? Visibility.Collapsed : Visibility.Visible;

        private ImageSource? _thumb;
        public ImageSource? Thumb
        {
            get => _thumb;
            private set { _thumb = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumb))); }
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
            var bytes = await SupportApi.GetAttachmentBytesAsync(TicketId, Id).ConfigureAwait(true);
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
