using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    public enum ChatChannel { Team, Clan }

    // ====== STATE (Team) ======
    private readonly List<TeamChatMessage> _chatHistoryLog = new();
    private DateTime? _lastChatTsForCurrentServer = null;
    private readonly HashSet<string> _pendingChatConfirms = new();
    private DateTime _lastChatDate = DateTime.MinValue;
    private int _displayedMessagesCount = 20;

    // ====== STATE (Clan) ======
    private readonly List<TeamChatMessage> _clanChatHistoryLog = new();
    private DateTime? _lastClanChatTsForCurrentServer = null;
    private readonly HashSet<string> _clanPendingChatConfirms = new();
    private DateTime _lastClanChatDate = DateTime.MinValue;
    private int _clanDisplayedMessagesCount = 20;

    // ====== UNREAD NOTIFICATION STATE ======
    private int _unreadTeamCount = 0;
    private int _unreadClanCount = 0;

    public int UnreadTeamCount => _unreadTeamCount;
    public int UnreadClanCount => _unreadClanCount;
    public int TotalUnreadChatCount => _unreadTeamCount + _unreadClanCount;

    // ====== SHARED UI STATE ======
    private ChatChannel _activeChatChannel = ChatChannel.Team;
    private bool _isLoadingMoreChat = false;
    private ScrollViewer? _chatScrollViewer;
    private string _chatSearchQuery = "";
    private bool _chatAvatarListenerInitialized = false;
    private Controls.Chat.ChatEmojiInputHelper? _teamChatEmojiHelper;

    // ====== VIEW MODEL ======
    public ObservableCollection<ChatMessageVM> ChatMessages { get; } = new();

    public sealed class ChatMessageVM : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private ulong _steamId;
        public ulong SteamId
        {
            get => _steamId;
            set
            {
                if (_steamId != value)
                {
                    _steamId = value;
                    OnChanged(nameof(SteamId));
                    OnChanged(nameof(HasSteamId));
                    OnChanged(nameof(SteamIdFormatted));
                    OnChanged(nameof(AvatarBackgroundBrush));
                }
            }
        }

        public bool HasSteamId => SteamId != 0;
        public string SteamIdFormatted => SteamId != 0 ? SteamId.ToString() : "";

        private string _author = "";
        public string Author
        {
            get => _author;
            set
            {
                if (_author != value)
                {
                    _author = value;
                    OnChanged(nameof(Author));
                    OnChanged(nameof(AuthorInitials));
                    OnChanged(nameof(AvatarBackgroundBrush));
                }
            }
        }

        private string _text = "";
        public string Text
        {
            get => _text;
            set
            {
                if (_text != value)
                {
                    _text = value;
                    OnChanged(nameof(Text));
                    OnChanged(nameof(DisplayText));
                }
            }
        }

        private DateTime _timestamp;
        public DateTime Timestamp
        {
            get => _timestamp;
            set
            {
                if (_timestamp != value)
                {
                    _timestamp = value;
                    OnChanged(nameof(Timestamp));
                    OnChanged(nameof(FormattedTime));
                    OnChanged(nameof(FullTimestampTooltip));
                }
            }
        }

        public string FormattedTime => Timestamp.ToString("HH:mm");
        public string FullTimestampTooltip => Timestamp.ToString("F", CultureInfo.CurrentUICulture);

        private ImageSource? _avatar;
        public ImageSource? Avatar
        {
            get => _avatar;
            set
            {
                if (_avatar != value)
                {
                    _avatar = value;
                    OnChanged(nameof(Avatar));
                    OnChanged(nameof(HasAvatar));
                }
            }
        }

        public bool HasAvatar => Avatar != null;

        private bool _showSeparator;
        public bool ShowSeparator
        {
            get => _showSeparator;
            set
            {
                if (_showSeparator != value)
                {
                    _showSeparator = value;
                    OnChanged(nameof(ShowSeparator));
                }
            }
        }

        private string? _separatorText;
        public string? SeparatorText
        {
            get => _separatorText;
            set
            {
                if (_separatorText != value)
                {
                    _separatorText = value;
                    OnChanged(nameof(SeparatorText));
                }
            }
        }

        private bool _isMe;
        public bool IsMe
        {
            get => _isMe;
            set
            {
                if (_isMe != value)
                {
                    _isMe = value;
                    OnChanged(nameof(IsMe));
                    OnChanged(nameof(AvatarBackgroundBrush));
                }
            }
        }

        private bool _isSupporter;
        public bool IsSupporter
        {
            get => _isSupporter;
            set
            {
                if (_isSupporter != value)
                {
                    _isSupporter = value;
                    OnChanged(nameof(IsSupporter));
                }
            }
        }

        private bool _isBotOrCommand;
        public bool IsBotOrCommand
        {
            get => _isBotOrCommand;
            set
            {
                if (_isBotOrCommand != value)
                {
                    _isBotOrCommand = value;
                    OnChanged(nameof(IsBotOrCommand));
                    OnChanged(nameof(AuthorInitials));
                    OnChanged(nameof(AvatarBackgroundBrush));
                }
            }
        }

        private bool _isSystemAlert;
        public bool IsSystemAlert
        {
            get => _isSystemAlert;
            set
            {
                if (_isSystemAlert != value)
                {
                    _isSystemAlert = value;
                    OnChanged(nameof(IsSystemAlert));
                    OnChanged(nameof(AuthorInitials));
                    OnChanged(nameof(AvatarBackgroundBrush));
                }
            }
        }

        public string DisplayText
        {
            get
            {
                if (string.IsNullOrEmpty(Text)) return "";
                if (Text.StartsWith("[Chat Command] ", StringComparison.OrdinalIgnoreCase))
                    return Text.Substring(15).Trim();
                return Text;
            }
        }

        public string AuthorInitials
        {
            get
            {
                if (IsBotOrCommand) return "BOT";
                if (IsSystemAlert) return "R+";
                if (string.IsNullOrWhiteSpace(Author)) return "?";

                var parts = Author.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var f = char.ToUpperInvariant(parts[0][0]);
                    var s = char.ToUpperInvariant(parts[1][0]);
                    return $"{f}{s}";
                }
                if (parts.Length == 1)
                {
                    var word = parts[0];
                    return word.Length >= 2
                        ? word.Substring(0, 2).ToUpperInvariant()
                        : word.ToUpperInvariant();
                }
                return "?";
            }
        }

        private static readonly Brush[] DeterministicPalettes = new Brush[]
        {
            new SolidColorBrush(Color.FromRgb(13, 148, 136)), // Teal #0D9488
            new SolidColorBrush(Color.FromRgb(2, 132, 199)),  // Sky #0284C7
            new SolidColorBrush(Color.FromRgb(79, 70, 229)),  // Indigo #4F46E5
            new SolidColorBrush(Color.FromRgb(124, 58, 237)), // Violet #7C3AED
            new SolidColorBrush(Color.FromRgb(192, 38, 211)), // Fuchsia #C026D3
            new SolidColorBrush(Color.FromRgb(219, 39, 119)), // Pink #DB2777
            new SolidColorBrush(Color.FromRgb(225, 29, 72)),  // Rose #E11D48
            new SolidColorBrush(Color.FromRgb(234, 88, 12)),  // Orange #EA580C
            new SolidColorBrush(Color.FromRgb(217, 119, 6)),  // Amber #D97706
            new SolidColorBrush(Color.FromRgb(22, 163, 74)),  // Green #16A34A
            new SolidColorBrush(Color.FromRgb(37, 99, 235))   // Blue #2563EB
        };

        static ChatMessageVM()
        {
            foreach (var b in DeterministicPalettes)
            {
                if (b.CanFreeze) b.Freeze();
            }
        }

        public Brush AvatarBackgroundBrush
        {
            get
            {
                if (IsBotOrCommand || IsSystemAlert)
                    return new SolidColorBrush(Color.FromRgb(30, 58, 76));

                if (IsMe)
                    return new SolidColorBrush(Color.FromRgb(20, 80, 120));

                int hash = (SteamId != 0 ? (int)(SteamId ^ (SteamId >> 32)) : Author.GetHashCode()) & 0x7FFFFFFF;
                return DeterministicPalettes[hash % DeterministicPalettes.Length];
            }
        }
    }

    // ====== INITIALIZATION & AVATAR REACTIVITY ======

    private void EnsureChatSystemInitialized()
    {
        if (_chatAvatarListenerInitialized) return;
        _chatAvatarListenerInitialized = true;

        AvatarLoader.AvatarLoaded += (steamId, img) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                foreach (var msg in ChatMessages)
                {
                    if (msg.SteamId == steamId && msg.Avatar != img)
                    {
                        msg.Avatar = img;
                    }
                }
            });
        };
    }

    // ====== LOGIC ======

    private void AddIncomingChatMessage(string author, string text, DateTime? ts = null, ulong steamId = 0, bool autoScroll = true)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var time = ts ?? DateTime.Now;
        bool isClanActive = _activeChatChannel == ChatChannel.Clan;
        var lastDate = isClanActive ? _lastClanChatDate : _lastChatDate;

        bool showSep = false;
        string? sepText = null;

        if (time.Date != lastDate.Date)
        {
            showSep = true;
            sepText = time.ToString("D", CultureInfo.CurrentUICulture);
            if (isClanActive) _lastClanChatDate = time.Date;
            else _lastChatDate = time.Date;
        }

        // Avatar: check cache first, or fetch asynchronously
        ImageSource? avatar = null;
        if (steamId != 0)
        {
            avatar = AvatarLoader.GetCachedAvatar(steamId) ?? (_avatarCache.TryGetValue(steamId, out var img) ? img : null);
            if (avatar == null)
            {
                _ = AvatarLoader.GetOrLoadAvatarAsync(steamId);
            }
        }

        bool isMe = steamId != 0 && steamId == _mySteamId;
        bool isSupporter = (isMe && Services.Auth.SupabaseAuthManager.IsPremium);
        bool isBot = text.StartsWith("[Chat Command]", StringComparison.OrdinalIgnoreCase) ||
                     text.StartsWith("!", StringComparison.OrdinalIgnoreCase);
        bool isAlert = text.Contains("[Raid Alert]", StringComparison.OrdinalIgnoreCase) ||
                       text.Contains("[Alarm]", StringComparison.OrdinalIgnoreCase) ||
                       text.Contains("[Timer]", StringComparison.OrdinalIgnoreCase);

        var vm = new ChatMessageVM
        {
            Author = author,
            Text = text,
            Timestamp = time,
            SteamId = steamId,
            Avatar = avatar,
            ShowSeparator = showSep,
            SeparatorText = sepText,
            IsMe = isMe,
            IsSupporter = isSupporter,
            IsBotOrCommand = isBot,
            IsSystemAlert = isAlert
        };

        // Filter check if search is active
        if (string.IsNullOrWhiteSpace(_chatSearchQuery) ||
            vm.Text.Contains(_chatSearchQuery, StringComparison.OrdinalIgnoreCase) ||
            vm.Author.Contains(_chatSearchQuery, StringComparison.OrdinalIgnoreCase))
        {
            ChatMessages.Add(vm);
        }

        // Update Empty State Visibility
        UpdateChatEmptyState();

        // Auto-Scroll if chat overlay is visible
        if (autoScroll)
        {
            if (_activeChatChannel == ChatChannel.Clan) _clanDisplayedMessagesCount++;
            else _displayedMessagesCount++;

            if (ChatOverlayPanel?.Visibility == Visibility.Visible && ChatContentBorder?.Visibility == Visibility.Visible)
            {
                ScrollChatToBottom();
            }
        }
    }

    private void UpdateChatEmptyState()
    {
        if (ChatEmptyNoticeCard != null)
        {
            ChatEmptyNoticeCard.Visibility = ChatMessages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ScrollChatToBottom()
    {
        if (ChatList != null && VisualTreeHelper.GetChildrenCount(ChatList) > 0)
        {
            var border = VisualTreeHelper.GetChild(ChatList, 0) as Border;
            var scrollViewer = border?.Child as ScrollViewer;
            scrollViewer?.ScrollToBottom();
        }

        if (BtnJumpToBottom != null)
        {
            BtnJumpToBottom.Visibility = Visibility.Collapsed;
        }
    }

    // ====== UNREAD BADGES MANAGEMENT ======

    private void UpdateUnreadBadges()
    {
        Dispatcher.InvokeAsync(() =>
        {
            // Team Tab Badge
            if (BadgeUnreadTeam != null && TxtUnreadTeam != null)
            {
                BadgeUnreadTeam.Visibility = _unreadTeamCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                TxtUnreadTeam.Text = _unreadTeamCount > 99 ? "99+" : _unreadTeamCount.ToString();
            }

            // Clan Tab Badge
            if (BadgeUnreadClan != null && TxtUnreadClan != null)
            {
                BadgeUnreadClan.Visibility = _unreadClanCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                TxtUnreadClan.Text = _unreadClanCount > 99 ? "99+" : _unreadClanCount.ToString();
            }

            // Bottom Dock Button Badge
            if (BadgeUnreadDock != null && TxtUnreadDock != null)
            {
                int total = TotalUnreadChatCount;
                BadgeUnreadDock.Visibility = total > 0 && ChatContentBorder.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
                TxtUnreadDock.Text = total > 99 ? "99+" : total.ToString();
            }
        });
    }

    // ====== CORE SENDING ======

    private readonly HashSet<string> _recentAutomatedMessages = new();

    private async Task SendTeamChatSafeAsync(string text, bool bypassChatAlertMasterBlock = false, bool skipDiscordChatForwarding = false, string? discordText = null, bool skipBasicWebhook = false)
    {
        if (skipDiscordChatForwarding)
        {
            lock (_recentAutomatedMessages)
            {
                _recentAutomatedMessages.Add(text);
            }
        }
        if (!bypassChatAlertMasterBlock && !CanSendAutomatedTeamChat()) return;

        // Discord Webhook Integration (Free Tier)
        if (!bypassChatAlertMasterBlock && !skipBasicWebhook)
        {
            _ = SendDiscordWebhookAsync(_vm?.Selected, discordText ?? text);
        }

        // If Discord Exclusive (not in-game) is enabled, skip posting to Rust in-game team chat
        if (_vm?.Selected?.DiscordWebhookChatAlertsExclusive == true)
        {
            return;
        }

        try
        {
            await SendChatReliableAsync(text, ChatChannel.Team);
        }
        catch { /* ignore background errors */ }
    }

    private async Task SendDiscordWebhookAsync(ServerProfile? profile, string message)
    {
        if (profile?.DiscordWebhookChatAlertsEnabled != true
            || !_vm.IsCloudConnected
            || string.IsNullOrWhiteSpace(profile.DiscordWebhookChatAlertsUrl)) return;

        try
        {
            var mention = string.IsNullOrWhiteSpace(profile.DiscordWebhookChatAlertsMention) ? "" : $"{profile.DiscordWebhookChatAlertsMention}\n";
            var payload = new
            {
                content = $"{mention}**[{profile.Name ?? "Rust Server"}]** {message}",
                tts = profile.DiscordWebhookChatAlertsTts
            };
            var json = JsonSerializer.Serialize(payload);
            using var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var client = new System.Net.Http.HttpClient();
            using var response = await client.PostAsync(profile.DiscordWebhookChatAlertsUrl, content);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            AppendLog($"[Discord] Webhook send failed: {ex.Message}");
        }
    }

    private async Task<bool> SendChatReliableAsync(string text, ChatChannel channel)
    {
        if (_rust is not RustPlusClientReal real) return false;

        if (text == null)
        {
            AppendLog("[Chat] Fail to send: text is null");
            return false;
        }

        var pending = channel == ChatChannel.Clan ? _clanPendingChatConfirms : _pendingChatConfirms;
        string tag = channel == ChatChannel.Clan ? "ClanChat" : "Chat";

        AppendLog($"[{tag}] Sending: {text}");

        string trackKey = $"{text.Trim()}_{DateTime.UtcNow:HHmmss}";
        lock (pending) { pending.Add(trackKey); }

        async Task<bool> SendOnceAsync()
        {
            if (channel == ChatChannel.Clan)
                await real.SendClanMessageAsync(text);
            else
                await real.SendTeamMessageAsync(text);
            return true;
        }

        bool sentOk = false;
        try
        {
            await SendOnceAsync();
            sentOk = true;
        }
        catch (Exception ex)
        {
            AppendLog($"[{tag}] Fail to send: {ex.Message}");
            lock (pending) { pending.Remove(trackKey); }
            return false;
        }

        int waitMs = 0;
        int intervalMs = 100;
        int timeoutMs = 2000;

        while (waitMs < timeoutMs)
        {
            await Task.Delay(intervalMs);
            waitMs += intervalMs;

            lock (pending)
            {
                if (!pending.Contains(trackKey))
                {
                    return true;
                }
            }
        }

        lock (pending) { pending.Remove(trackKey); }
        if (sentOk)
        {
            string myName = _vm?.Selected?.Name ?? "Me";
            var selfMsg = new TeamChatMessage(DateTime.UtcNow, myName, _mySteamId, text);
            AppendChatIfNew(selfMsg, channel, isHistorical: false);
            return true;
        }

        return false;
    }

    // ====== EVENT HANDLERS ======

    private void Real_TeamChatReceived(object? sender, TeamChatMessage m)
    {
        lock (_pendingChatConfirms)
        {
            var match = _pendingChatConfirms.FirstOrDefault(k => k.StartsWith(m.Text.Trim() + "_"));
            if (match != null)
            {
                _pendingChatConfirms.Remove(match);
            }
        }
        
        AppendChatIfNew(m, ChatChannel.Team, isHistorical: false);
    }

    private void Real_ClanChatReceived(object? sender, TeamChatMessage m)
    {
        lock (_clanPendingChatConfirms)
        {
            var match = _clanPendingChatConfirms.FirstOrDefault(k => k.StartsWith(m.Text.Trim() + "_"));
            if (match != null)
            {
                _clanPendingChatConfirms.Remove(match);
            }
        }

        AppendChatIfNew(m, ChatChannel.Clan, isHistorical: false);
    }

    private bool AppendChatIfNew(TeamChatMessage m, ChatChannel channel, bool isHistorical = false)
    {
        var log = channel == ChatChannel.Clan ? _clanChatHistoryLog : _chatHistoryLog;

        var profile = _vm?.Selected;
        string prefix = profile?.ChatCommandPrefix ?? "!";
        bool isCommand = channel == ChatChannel.Team && m.Text.TrimStart().StartsWith(prefix);

        var mUtc = m.Timestamp.Kind == DateTimeKind.Utc ? m.Timestamp : m.Timestamp.ToUniversalTime();

        lock (log)
        {
            bool isDuplicate = false;
            int thresholdSec = isHistorical ? 30 : 5;
            foreach (var ext in log.AsEnumerable().Reverse().Take(100))
            {
                var extUtc = ext.Timestamp.Kind == DateTimeKind.Utc ? ext.Timestamp : ext.Timestamp.ToUniversalTime();
                if ((ext.SteamId == m.SteamId || ext.SteamId == 0 || m.SteamId == 0) &&
                    string.Equals(ext.Text.Trim(), m.Text.Trim(), StringComparison.Ordinal) &&
                    Math.Abs((extUtc - mUtc).TotalSeconds) <= thresholdSec)
                {
                    isDuplicate = true;
                    break;
                }
            }
            if (!isDuplicate)
            {
                var msgToStore = m.Timestamp.Kind == DateTimeKind.Utc ? m : new TeamChatMessage(mUtc, m.Author, m.SteamId, m.Text);
                log.Add(msgToStore);
            }
            else
            {
                return false;
            }

            if (log.Count > 1000)
            {
                log.RemoveRange(0, 200);
            }
        }

        if (isCommand)
        {
            if (!isHistorical && _rust is RustPlusClientReal)
            {
                _ = ProcessChatCommands(m);
            }
            
            m = new TeamChatMessage(m.Timestamp, m.Author, m.SteamId, $"[Chat Command] {m.Text}");
        }

        if (!isHistorical)
        {
            bool isPanelOpen = ChatContentBorder?.Visibility == Visibility.Visible;
            bool isMatchingChannel = channel == _activeChatChannel;

            if (isMatchingChannel)
            {
                Dispatcher.InvokeAsync(() => AddIncomingChatMessage(m.Author, m.Text, mUtc.ToLocalTime(), m.SteamId, autoScroll: true));
            }

            // Manage unread count
            if (!isPanelOpen || !isMatchingChannel)
            {
                if (channel == ChatChannel.Team) _unreadTeamCount++;
                else if (channel == ChatChannel.Clan) _unreadClanCount++;
                UpdateUnreadBadges();
            }

            if (!isCommand)
            {
                bool isAutomated;
                lock (_recentAutomatedMessages)
                {
                    isAutomated = _recentAutomatedMessages.Remove(m.Text);
                }

                if (!isAutomated)
                {
                    string emoji = channel == ChatChannel.Clan ? "🏰" : "💬";
                    _ = DiscordBotListenerService.Instance.SendNotificationAsync("chat", $"{emoji} **{m.Author}**: {m.Text}");
                }
            }
        }
        
        if (channel == ChatChannel.Clan)
        {
            if (!_lastClanChatTsForCurrentServer.HasValue || mUtc > _lastClanChatTsForCurrentServer.Value)
                _lastClanChatTsForCurrentServer = mUtc;
        }
        else
        {
            if (!_lastChatTsForCurrentServer.HasValue || mUtc > _lastChatTsForCurrentServer.Value)
                _lastChatTsForCurrentServer = mUtc;
        }

        return true;
    }

    private void OnTeamChatReceived(object? _, RustPlusDesk.Models.TeamChatMessage m)
    {
        Dispatcher.Invoke(() => AddIncomingChatMessage(m.Author, m.Text, m.Timestamp));
    }

    private void OnChatReceived(object? sender, TeamChatMessage e)
    {
        Dispatcher.Invoke(() => AddIncomingChatMessage(e.Author, e.Text, e.Timestamp.ToLocalTime(), e.SteamId));
    }

    private void RebuildChatMessages()
    {
        ChatMessages.Clear();

        bool isClan = _activeChatChannel == ChatChannel.Clan;
        var log = isClan ? _clanChatHistoryLog : _chatHistoryLog;
        int displayCount = isClan ? _clanDisplayedMessagesCount : _displayedMessagesCount;

        if (isClan) _lastClanChatDate = DateTime.MinValue;
        else _lastChatDate = DateTime.MinValue;

        List<TeamChatMessage> toDisplay;
        lock (log)
        {
            toDisplay = log
                .OrderBy(x => x.Timestamp.Kind == DateTimeKind.Utc ? x.Timestamp : x.Timestamp.ToUniversalTime())
                .Skip(Math.Max(0, log.Count - displayCount))
                .ToList();
        }

        foreach (var m in toDisplay)
        {
            var localTs = m.Timestamp.Kind == DateTimeKind.Utc ? m.Timestamp.ToLocalTime() : m.Timestamp;
            AddIncomingChatMessage(m.Author, m.Text, localTs, m.SteamId, autoScroll: false);
        }

        UpdateChatEmptyState();
    }

    // ====== UI INTERACTIONS ======

    private async void BtnToggleChat_Click(object sender, RoutedEventArgs e)
    {
        if (ChatContentBorder.Visibility == Visibility.Visible)
        {
            CloseChatOverlay();
            return;
        }

        await OpenChatOverlayAsync();
    }

    private async void ChatTab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;
        var channel = tag == "Clan" ? ChatChannel.Clan : ChatChannel.Team;
        if (channel == _activeChatChannel) return;

        _activeChatChannel = channel;
        TxtChatInput?.Clear();
        if (ChatErrorBox != null) ChatErrorBox.Visibility = Visibility.Collapsed;

        // Reset unread count for the active channel
        if (channel == ChatChannel.Team) _unreadTeamCount = 0;
        else if (channel == ChatChannel.Clan) _unreadClanCount = 0;
        UpdateUnreadBadges();

        RebuildChatMessages();
        ScrollChatToBottom();

        if (_rust is RustPlusClientReal real && (_vm.Selected?.IsConnected ?? false))
        {
            try
            {
                if (channel == ChatChannel.Clan)
                {
                    await PrimeClanChatIfNeededAsync(real);
                }
                else
                {
                    real.TeamChatReceived -= Real_TeamChatReceived;
                    real.TeamChatReceived += Real_TeamChatReceived;
                    await real.PrimeTeamChatAsync();
                }
            }
            catch { /* tolerant */ }
        }
    }

    private async Task PrimeClanChatIfNeededAsync(RustPlusClientReal real)
    {
        real.ClanChatReceived -= Real_ClanChatReceived;
        real.ClanChatReceived += Real_ClanChatReceived;
        await real.PrimeClanChatAsync();

        try
        {
            var history = await real.GetClanChatHistoryAsync(_lastClanChatTsForCurrentServer, limit: 120);
            if (history != null && history.Count > 0)
            {
                bool anyNew = false;
                foreach (var m in history)
                {
                    if (AppendChatIfNew(m, ChatChannel.Clan, isHistorical: true))
                        anyNew = true;
                }

                if (anyNew && _activeChatChannel == ChatChannel.Clan)
                {
                    Dispatcher.Invoke(() =>
                    {
                        RebuildChatMessages();
                        ScrollChatToBottom();
                    });
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog("GetClanChatHistory Error: " + ex.Message);
        }
    }

    public async Task OpenChatOverlayAsync()
    {
        EnsureChatSystemInitialized();

        if (_rust is not RustPlusClientReal real)
        {
            ShowInfoSnackbar(Properties.Resources.SnackbarTitleConnection, Properties.Resources.NotConnectedError, WpfUi.ControlAppearance.Caution);
            return;
        }

        if (!(_vm.Selected?.IsConnected ?? false))
        {
            ShowInfoSnackbar(Properties.Resources.SnackbarTitleChat, Properties.Resources.PleaseConnectFirst, WpfUi.ControlAppearance.Info);
            return;
        }

        try
        {
            real.TeamChatReceived -= Real_TeamChatReceived;
            real.TeamChatReceived += Real_TeamChatReceived;
            await real.PrimeTeamChatAsync();
        }
        catch (InvalidOperationException)
        {
            ShowInfoSnackbar(Properties.Resources.SnackbarTitleChat, Properties.Resources.PleaseConnectFirst, WpfUi.ControlAppearance.Info);
            return;
        }
        catch (Exception ex)
        {
            AppendLog("PrimeChat failed: " + ex.Message);
            ShowInfoSnackbar(Properties.Resources.SnackbarTitleChat, Properties.Resources.ChatNotAvailable, WpfUi.ControlAppearance.Danger);
            return;
        }

        _ = PrimeClanChatIfNeededAsync(real).ContinueWith(t =>
        {
            if (t.Exception != null) AppendLog("PrimeClanChat failed: " + t.Exception.InnerException?.Message);
        }, TaskScheduler.Default);

        _displayedMessagesCount = 20;
        _clanDisplayedMessagesCount = 20;

        // Reset unread count for current active channel
        if (_activeChatChannel == ChatChannel.Team) _unreadTeamCount = 0;
        else if (_activeChatChannel == ChatChannel.Clan) _unreadClanCount = 0;
        UpdateUnreadBadges();

        RebuildChatMessages();

        // Update server header label
        if (TxtChatServerContext != null)
        {
            string serverName = _vm.Selected?.Name ?? "Rust Server";
            int teamCount = TeamMembers.Count;
            TxtChatServerContext.Text = teamCount > 0 ? $"{serverName} · {teamCount} Teammates" : serverName;
        }

        // Overlay animation
        ChatContentBorder.Visibility = Visibility.Visible;
        ChatContentBorder.Opacity = 0;

        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        var sb = new System.Windows.Media.Animation.Storyboard();
        sb.Children.Add(fade);
        System.Windows.Media.Animation.Storyboard.SetTarget(fade, ChatContentBorder);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
        sb.Begin();

        EnsureTeamChatEmojiHelper();
        TxtChatInput.Focus();
        ScrollChatToBottom();

        try
        {
            var history = await real.GetTeamChatHistoryAsync(_lastChatTsForCurrentServer, limit: 120);
            if (history != null && history.Count > 0)
            {
                bool anyNew = false;
                foreach (var m in history)
                {
                    if (AppendChatIfNew(m, ChatChannel.Team, isHistorical: true))
                        anyNew = true;
                }
                
                if (anyNew && _activeChatChannel == ChatChannel.Team)
                {
                    RebuildChatMessages();
                    ScrollChatToBottom();
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog("GetHistory Error: " + ex.Message);
        }
    }

    private void EnsureTeamChatEmojiHelper()
    {
        if (_teamChatEmojiHelper != null) return;
        if (TxtChatInput != null && TeamChatAutocompletePopup != null && TeamChatAutocompleteControl != null &&
            TeamChatPickerPopup != null && TeamChatPickerControl != null && BtnTeamEmoji != null)
        {
            _teamChatEmojiHelper = new Controls.Chat.ChatEmojiInputHelper(
                TxtChatInput,
                TeamChatAutocompletePopup,
                TeamChatAutocompleteControl,
                TeamChatPickerPopup,
                TeamChatPickerControl,
                BtnTeamEmoji);
        }
    }

    private void CloseChatOverlay()
    {
        if (ChatContentBorder.Visibility == Visibility.Collapsed) return;

        var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
        var sb = new System.Windows.Media.Animation.Storyboard();
        sb.Children.Add(fade);
        System.Windows.Media.Animation.Storyboard.SetTarget(fade, ChatContentBorder);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
        
        sb.Completed += (s, ev) => 
        {
            ChatContentBorder.Visibility = Visibility.Collapsed;
            if (ChatErrorBox != null) ChatErrorBox.Visibility = Visibility.Collapsed;
            if (ChatSearchPanel != null) ChatSearchPanel.Visibility = Visibility.Collapsed;
            if (ChatQuickCommandsDrawer != null) ChatQuickCommandsDrawer.Visibility = Visibility.Collapsed;
            UpdateUnreadBadges();
        };
        sb.Begin();
    }

    private void BtnCloseChatOverlay_Click(object sender, RoutedEventArgs e)
    {
        CloseChatOverlay();
    }

    private async Task SendChatInputAsync()
    {
        var text = TxtChatInput.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        if (ChatErrorBox != null) ChatErrorBox.Visibility = Visibility.Collapsed;

        try
        {
            BtnSendChat.IsEnabled = false;
            TxtChatInput.IsEnabled = false;
            var oldContent = BtnSendChat.Content;
            BtnSendChat.Content = "...";

            bool confirmed = await SendChatReliableAsync(text, _activeChatChannel);

            if (confirmed)
            {
                TxtChatInput.Clear();
            }
            else
            {
                if (ChatErrorBox != null && ChatErrorText != null)
                {
                    ChatErrorBox.Visibility = Visibility.Visible;
                    ChatErrorText.Text = Properties.Resources.MessageNotSentError;
                }
            }
        }
        catch (Exception ex)
        {
            if (ChatErrorBox != null && ChatErrorText != null)
            {
                ChatErrorBox.Visibility = Visibility.Visible;
                ChatErrorText.Text = Properties.Resources.ErrorPrefix + ex.Message;
            }
        }
        finally
        {
            BtnSendChat.IsEnabled = true;
            TxtChatInput.IsEnabled = true;
            BtnSendChat.Content = Properties.Resources.Send;
            TxtChatInput.Focus();
        }
    }

    private async void BtnSendChat_Click(object sender, RoutedEventArgs e)
    {
        await SendChatInputAsync();
    }

    private async void TxtChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            await SendChatInputAsync();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseChatOverlay();
        }
    }

    private void TxtChatInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TxtChatCharCount != null && TxtChatInput != null)
        {
            int len = TxtChatInput.Text.Length;
            TxtChatCharCount.Text = $"{len}/128";
            if (len >= 128)
                TxtChatCharCount.Foreground = Brushes.Red;
            else if (len >= 115)
                TxtChatCharCount.Foreground = Brushes.Orange;
            else
                TxtChatCharCount.Foreground = (Brush)FindResource("TextSubtle");
        }
    }

    private ScrollViewer? GetChatScrollViewer()
    {
        if (VisualTreeHelper.GetChildrenCount(ChatList) > 0)
        {
            var border = VisualTreeHelper.GetChild(ChatList, 0) as Border;
            return border?.Child as ScrollViewer;
        }
        return null;
    }

    private void ChatList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = _chatScrollViewer ?? GetChatScrollViewer();
        if (scrollViewer != null)
        {
            _chatScrollViewer = scrollViewer;
            if (scrollViewer.VerticalOffset == 0 && e.Delta > 0 && !_isLoadingMoreChat)
            {
                LoadMoreChatMessages();
                e.Handled = true;
            }
        }
    }

    private void ChatScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is ScrollViewer scrollViewer)
        {
            _chatScrollViewer = scrollViewer;
            if (scrollViewer.VerticalOffset == 0 && e.VerticalChange < 0 && !_isLoadingMoreChat)
            {
                LoadMoreChatMessages();
            }

            // Jump to bottom button visibility
            if (BtnJumpToBottom != null)
            {
                bool isScrolledUp = scrollViewer.VerticalOffset < (scrollViewer.ScrollableHeight - 40);
                BtnJumpToBottom.Visibility = (isScrolledUp && ChatMessages.Count > 5) ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void BtnJumpToBottom_Click(object sender, RoutedEventArgs e)
    {
        ScrollChatToBottom();
    }

    private void LoadMoreChatMessages()
    {
        bool isClan = _activeChatChannel == ChatChannel.Clan;
        var log = isClan ? _clanChatHistoryLog : _chatHistoryLog;

        int totalAvailable;
        lock (log)
        {
            totalAvailable = log.Count;
        }

        int displayCount = isClan ? _clanDisplayedMessagesCount : _displayedMessagesCount;
        if (displayCount >= totalAvailable)
        {
            return;
        }

        _isLoadingMoreChat = true;
        try
        {
            var scrollViewer = _chatScrollViewer ?? GetChatScrollViewer();
            if (scrollViewer != null)
            {
                double oldOffset = scrollViewer.VerticalOffset;
                double oldHeight = scrollViewer.ExtentHeight;

                if (isClan) _clanDisplayedMessagesCount += 20;
                else _displayedMessagesCount += 20;

                RebuildChatMessages();
                ChatList.UpdateLayout();

                double newHeight = scrollViewer.ExtentHeight;
                scrollViewer.ScrollToVerticalOffset(newHeight - oldHeight + oldOffset);
            }
        }
        finally
        {
            _isLoadingMoreChat = false;
        }
    }

    // ====== SEARCH & FILTERING ======

    private void BtnToggleChatSearch_Click(object sender, RoutedEventArgs e)
    {
        if (ChatSearchPanel == null) return;
        bool isVis = ChatSearchPanel.Visibility == Visibility.Visible;
        ChatSearchPanel.Visibility = isVis ? Visibility.Collapsed : Visibility.Visible;
        if (!isVis && TxtChatSearch != null)
        {
            TxtChatSearch.Focus();
        }
        else if (isVis)
        {
            TxtChatSearch?.Clear();
        }
    }

    private void TxtChatSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _chatSearchQuery = TxtChatSearch?.Text?.Trim() ?? "";
        RebuildChatMessages();
    }

    private void BtnClearChatSearch_Click(object sender, RoutedEventArgs e)
    {
        TxtChatSearch?.Clear();
    }

    // ====== QUICK COMMANDS DRAWER ======

    private void BtnToggleQuickCommands_Click(object sender, RoutedEventArgs e)
    {
        if (ChatQuickCommandsDrawer == null) return;
        ChatQuickCommandsDrawer.Visibility = ChatQuickCommandsDrawer.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void BtnQuickCommandChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string cmd)
        {
            TxtChatInput.Text = cmd;
            TxtChatInput.CaretIndex = TxtChatInput.Text.Length;
            TxtChatInput.Focus();
            if (ChatQuickCommandsDrawer != null)
                ChatQuickCommandsDrawer.Visibility = Visibility.Collapsed;
        }
    }

    // ====== CONTEXT MENU ACTIONS ======

    private ChatMessageVM? GetContextMessage(object sender)
    {
        if (sender is MenuItem mi)
        {
            if (mi.DataContext is ChatMessageVM vm) return vm;
            if (mi.Tag is ChatMessageVM tagVm) return tagVm;
            if (mi.Parent is ContextMenu cm && cm.PlacementTarget is FrameworkElement fe && fe.DataContext is ChatMessageVM feVm)
                return feVm;
        }
        return null;
    }

    private void ChatContext_CopyText_Click(object sender, RoutedEventArgs e)
    {
        var vm = GetContextMessage(sender);
        if (vm != null && !string.IsNullOrEmpty(vm.Text))
        {
            Clipboard.SetText(vm.Text);
            ShowInfoSnackbar("Copied", "Message text copied to clipboard.", WpfUi.ControlAppearance.Success);
        }
    }

    private void ChatContext_CopySteamId_Click(object sender, RoutedEventArgs e)
    {
        var vm = GetContextMessage(sender);
        if (vm != null && vm.SteamId != 0)
        {
            Clipboard.SetText(vm.SteamId.ToString());
            ShowInfoSnackbar("Copied", $"Steam ID {vm.SteamId} copied to clipboard.", WpfUi.ControlAppearance.Success);
        }
    }

    private void ChatContext_Mention_Click(object sender, RoutedEventArgs e)
    {
        var vm = GetContextMessage(sender);
        if (vm != null && !string.IsNullOrEmpty(vm.Author))
        {
            TxtChatInput.Text = $"@{vm.Author} " + TxtChatInput.Text;
            TxtChatInput.CaretIndex = TxtChatInput.Text.Length;
            TxtChatInput.Focus();
        }
    }

    private void ChatContext_CenterMap_Click(object sender, RoutedEventArgs e)
    {
        var vm = GetContextMessage(sender);
        if (vm != null && vm.SteamId != 0)
        {
            var member = TeamMembers.FirstOrDefault(m => m.SteamId == vm.SteamId);
            if (member != null)
            {
                CenterOnMember(member);
            }
            else
            {
                ShowInfoSnackbar("Map", "Teammate is not currently visible on the active map.", WpfUi.ControlAppearance.Info);
            }
        }
    }

    private void ChatContext_OpenSteam_Click(object sender, RoutedEventArgs e)
    {
        var vm = GetContextMessage(sender);
        if (vm != null && vm.SteamId != 0)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"https://steamcommunity.com/profiles/{vm.SteamId}",
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
