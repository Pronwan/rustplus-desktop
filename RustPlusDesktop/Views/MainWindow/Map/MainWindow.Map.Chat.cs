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
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    /// <summary>
    /// The lanes in the chat window.
    ///
    /// <see cref="Global"/> is not a Rust channel — it is the platform's public room, put here
    /// because this is the chat surface people already watch while they play. A room nobody visits
    /// does not need a better front door so much as it needs to be where the traffic is.
    /// </summary>
    public enum ChatChannel { Team, Clan, Global }

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

    // ====== STATE (Global) ======
    // No history log of its own: GlobalChatFeed holds the room for the whole app, and a second
    // copy here would be a second thing to keep in step with it.
    private DateTime _lastGlobalChatDate = DateTime.MinValue;
    private int _globalDisplayedMessagesCount = 20;
    private bool _globalLaneWired;

    // ====== UNREAD NOTIFICATION STATE ======
    private int _unreadTeamCount = 0;
    private int _unreadClanCount = 0;

    public int UnreadTeamCount => _unreadTeamCount;
    public int UnreadClanCount => _unreadClanCount;

    /// <summary>Counted by the feed, which keeps counting while this window is shut.</summary>
    private int UnreadGlobalCount => Services.Social.GlobalChatFeed.Unseen(
        Services.Social.GlobalChatFeed.DefaultRoom);

    public int TotalUnreadChatCount => _unreadTeamCount + _unreadClanCount + UnreadGlobalCount;

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

        /// <summary>
        /// The room message this row was built from, or null for an in-game line.
        ///
        /// The two kinds of message do not carry the same facts — a Rust line has a Steam id and
        /// no roles, a room line the reverse — and rather than flattening every room-only fact
        /// into its own property, the line itself is kept so the row can offer what only a room
        /// message can: a reply, a report, a name colour its sender chose.
        /// </summary>
        private Models.ChatLine? _sourceLine;
        public Models.ChatLine? SourceLine
        {
            get => _sourceLine;
            set
            {
                if (_sourceLine != value)
                {
                    _sourceLine = value;
                    OnChanged(nameof(SourceLine));
                    OnChanged(nameof(IsRoomLine));
                    OnChanged(nameof(AuthorBrush));
                    OnChanged(nameof(RoleBadge));
                    OnChanged(nameof(HasRoleBadge));
                    OnChanged(nameof(HasReply));
                    OnChanged(nameof(ReplyAuthor));
                    OnChanged(nameof(ReplyExcerpt));
                    OnChanged(nameof(CanActOnSender));
                }
            }
        }

        /// <summary>Whether this row came from the public room rather than from a Rust server.</summary>
        public bool IsRoomLine => _sourceLine != null;

        /// <summary>
        /// Whether this line addresses the signed-in account.
        ///
        /// Taken from the server's resolution rather than by looking for your own name in the
        /// text: names are neither unique nor stable, so searching for one finds other people's
        /// conversations and misses your own.
        /// </summary>
        public bool MentionsMe => _sourceLine?.MentionsMe == true;

        /// <summary>
        /// The sender's chosen name colour, falling back to the supporter gold or the room's
        /// default. Null for in-game lines, which keep the template's own accent.
        /// </summary>
        public Brush? AuthorBrush => _sourceLine?.SenderNameBrush;

        public RoleBadgeInfo? RoleBadge => _sourceLine?.RoleBadge;

        public bool HasRoleBadge => _sourceLine?.HasRoleBadge == true;

        /// <summary>Whether the row answers another message.</summary>
        public bool HasReply => _sourceLine?.HasReply == true;

        public string ReplyAuthor => _sourceLine?.ReplyTo?.SenderName ?? "";

        /// <summary>
        /// The quoted line, or a note that it is gone. A reference without a quote means the
        /// original was deleted, which is worth saying rather than hiding.
        /// </summary>
        public string ReplyExcerpt => _sourceLine?.ReplyTo is { } reply
            ? (string.IsNullOrWhiteSpace(reply.Excerpt)
                ? Properties.Resources.GetString("ChatReplyDeleted")
                : reply.Excerpt!)
            : "";

        /// <summary>
        /// Whether reporting or blocking this sender is on offer. Doing either to yourself is an
        /// offer that makes no sense, and making it anyway makes the menu look untended.
        /// </summary>
        public bool CanActOnSender => _sourceLine is { IsMine: false, SenderId: not null };

        /// <summary>
        /// Whether a friend request can be addressed to this sender.
        ///
        /// Friends are keyed by Steam account, and not every room account has one attached. The
        /// entry is hidden rather than shown and refused.
        /// </summary>
        public bool CanAddSenderAsFriend =>
            CanActOnSender && !string.IsNullOrWhiteSpace(_sourceLine?.SteamId);

        /// <summary>
        /// Whether this row draws its own name, badge, time and avatar, or continues the one above.
        ///
        /// Somebody saying four things in a row is one person talking, and repeating their name and
        /// picture over every line turns a short exchange into a wall of headers. Always true for
        /// the in-game lanes, which are not grouped.
        /// </summary>
        private bool _showHeader = true;
        public bool ShowHeader
        {
            get => _showHeader;
            set
            {
                if (_showHeader != value)
                {
                    _showHeader = value;
                    OnChanged(nameof(ShowHeader));
                }
            }
        }

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

    private void AddIncomingChatMessage(string author, string text, DateTime? ts = null, ulong steamId = 0, bool autoScroll = true, bool? forceSupporter = null, bool? forceIsMe = null, ImageSource? avatarOverride = null, Models.ChatLine? sourceLine = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var time = ts ?? DateTime.Now;
        var lastDate = LastChatDateFor(_activeChatChannel);

        bool showSep = false;
        string? sepText = null;

        if (time.Date != lastDate.Date)
        {
            showSep = true;
            sepText = time.ToString("D", CultureInfo.CurrentUICulture);
            SetLastChatDate(_activeChatChannel, time.Date);
        }

        // Avatar: an override wins, because a room line's picture comes from the platform account
        // rather than from Steam — and asking Steam would mean holding a stranger's Steam id.
        ImageSource? avatar = avatarOverride;
        if (avatar == null && steamId != 0)
        {
            avatar = AvatarLoader.GetCachedAvatar(steamId) ?? (_avatarCache.TryGetValue(steamId, out var img) ? img : null);
            if (avatar == null)
            {
                _ = AvatarLoader.GetOrLoadAvatarAsync(steamId);
            }
        }

        // Without a Steam id there is nothing to compare, so the room says which line is its
        // reader's rather than letting it be worked out from an identity it does not carry.
        bool isMe = forceIsMe ?? (steamId != 0 && steamId == _mySteamId);

        // In-game chat carries no supporter flag, so the only one we can know is our own. The
        // public room does carry it per line, and passes it in rather than being guessed at.
        bool isSupporter = forceSupporter ?? (isMe && Services.Auth.SupabaseAuthManager.IsPremium);
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
            IsSystemAlert = isAlert,
            SourceLine = sourceLine
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
            else if (_activeChatChannel == ChatChannel.Global) _globalDisplayedMessagesCount++;
            else _displayedMessagesCount++;

            if (ChatOverlayPanel?.Visibility == Visibility.Visible && ChatContentBorder?.Visibility == Visibility.Visible)
            {
                ScrollChatToBottom();
            }
        }
    }

    /// <summary>
    /// The date of the last line drawn in a lane, so a day boundary gets one separator.
    ///
    /// Per lane rather than shared: the lanes are drawn independently and a separator owed to one
    /// of them must not be considered already spent by another.
    /// </summary>
    private DateTime LastChatDateFor(ChatChannel channel) => channel switch
    {
        ChatChannel.Clan => _lastClanChatDate,
        ChatChannel.Global => _lastGlobalChatDate,
        _ => _lastChatDate,
    };

    private void SetLastChatDate(ChatChannel channel, DateTime date)
    {
        switch (channel)
        {
            case ChatChannel.Clan: _lastClanChatDate = date; break;
            case ChatChannel.Global: _lastGlobalChatDate = date; break;
            default: _lastChatDate = date; break;
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

            // Global Tab Badge
            if (BadgeUnreadGlobal != null && TxtUnreadGlobal != null)
            {
                var unseen = UnreadGlobalCount;
                BadgeUnreadGlobal.Visibility = unseen > 0 ? Visibility.Visible : Visibility.Collapsed;
                TxtUnreadGlobal.Text = unseen > 99 ? "99+" : unseen.ToString();
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

    /// <param name="forceChannel">
    /// Overrides the configured alert channel. Passed by command replies, which belong to whoever
    /// asked: "boat turned ON" is an automated message like any other, but it is an *answer*, and
    /// an answer that appears in a channel nobody asked in leaves the asker staring at silence.
    /// Alerts leave this null and follow the setting.
    /// </param>
    private async Task SendTeamChatSafeAsync(string text, bool bypassChatAlertMasterBlock = false, bool skipDiscordChatForwarding = false, string? discordText = null, bool skipBasicWebhook = false, ChatChannel? forceChannel = null)
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

        // Alerts go to one in-game channel or the other, never both: the same raid alarm arriving
        // twice is noise, and a clan of a hundred accounts has no business seeing what the team's
        // TC is doing unless someone chose that deliberately. A caller that already knows where
        // the message belongs says so and this choice does not apply.
        var target = forceChannel ?? (_vm?.Selected?.ChatAlertsUseClanChannel == true
            ? ChatChannel.Clan
            : ChatChannel.Team);

        try
        {
            await SendChatReliableAsync(text, target);
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
            using var client = new System.Net.Http.HttpClient(new Services.TrafficTrackingHttpMessageHandler("Discord Webhook"));
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
        // This path talks to a Rust server, and everything below treats anything that is not Clan
        // as Team. A room message arriving here would be sent to the player's team instead — so it
        // is refused rather than quietly misrouted.
        if (channel == ChatChannel.Global)
        {
            AppendLog("[Chat] Global chat does not go through the in-game send path.");
            return false;
        }

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
        // Clan messages count as commands only while clan answering is on. Without that condition
        // a clan member writing "!!!" would have their message relabelled as a command on a server
        // where commands never run there.
        bool isCommand = m.Text.TrimStart().StartsWith(prefix)
            && (channel == ChatChannel.Team || profile?.ClanChatCommandsEnabled == true);

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
                _ = ProcessChatCommands(m, channel);
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

        if (_activeChatChannel == ChatChannel.Global)
        {
            RebuildGlobalChatMessages();
            return;
        }

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
        var channel = tag switch
        {
            "Clan" => ChatChannel.Clan,
            "Global" => ChatChannel.Global,
            _ => ChatChannel.Team,
        };
        if (channel == _activeChatChannel) return;

        // Global chat needs an account rather than a server. Refuse before switching, so the lane
        // never shows as selected over somebody else's messages.
        if (channel == ChatChannel.Global && !GlobalLaneAvailable)
        {
            WarnGlobalLaneUnavailable();
            SelectChatLanePill(_activeChatChannel);
            return;
        }

        _activeChatChannel = channel;
        TxtChatInput?.Clear();
        if (ChatErrorBox != null) ChatErrorBox.Visibility = Visibility.Collapsed;

        // Reset unread count for the active channel
        if (channel == ChatChannel.Team) _unreadTeamCount = 0;
        else if (channel == ChatChannel.Clan) _unreadClanCount = 0;
        UpdateUnreadBadges();

        UpdateChatComposerForLane();

        if (channel == ChatChannel.Global)
        {
            // The room has no server to prime and no history to fetch from one, so the lane is
            // fully entered here and none of the in-game work below applies.
            await EnterGlobalLaneAsync();
            return;
        }

        // The two channels do not offer the same chips, so they are rebuilt on the switch rather
        // than only when the drawer opens — the empty-state row shows them without any drawer.
        RebuildQuickCommandChips();
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

        // Command names and the prefix can have been edited since the panel was last open, and the
        // empty-state row shows chips before anyone touches the drawer.
        RebuildQuickCommandChips();

        // Global chat needs no server, so a disconnected client is no longer a reason to refuse the
        // whole window — only the two in-game lanes. Without this the room would be unreachable
        // from here exactly when somebody is most likely to be looking for people to play with.
        var connected = _rust is RustPlusClientReal && (_vm.Selected?.IsConnected ?? false);

        if (!connected)
        {
            if (!GlobalLaneAvailable)
            {
                ShowInfoSnackbar(
                    Properties.Resources.SnackbarTitleChat,
                    _rust is RustPlusClientReal ? Properties.Resources.PleaseConnectFirst : Properties.Resources.NotConnectedError,
                    WpfUi.ControlAppearance.Info);
                return;
            }

            await OpenChatOverlayOnGlobalAsync();
            return;
        }

        var real = (RustPlusClientReal)_rust!;

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

        UpdateChatComposerForLane();

        // The room can be the lane that was left selected last time, and it is not primed or
        // labelled by any of the work above. Enter it properly rather than drawing an empty list
        // under a server name.
        if (_activeChatChannel == ChatChannel.Global)
        {
            ShowChatOverlayCard();
            await EnterGlobalLaneAsync();
        }
        else
        {
            RebuildChatMessages();

            // Update server header label
            if (TxtChatServerContext != null)
            {
                string serverName = _vm.Selected?.Name ?? "Rust Server";
                int teamCount = TeamMembers.Count;
                TxtChatServerContext.Text = teamCount > 0 ? $"{serverName} · {teamCount} Teammates" : serverName;
            }

            ShowChatOverlayCard();
        }

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

    /// <summary>
    /// Reveals the chat card and puts the cursor in the box.
    ///
    /// Shared by both ways in: with a server connected, after the lanes have been primed, and
    /// without one, straight onto the room.
    /// </summary>
    private void ShowChatOverlayCard()
    {
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
    }

    /// <summary>
    /// Opens the window straight onto the room, for when there is no server connected.
    /// </summary>
    private async Task OpenChatOverlayOnGlobalAsync()
    {
        _activeChatChannel = ChatChannel.Global;
        SelectChatLanePill(ChatChannel.Global);
        UpdateChatComposerForLane();

        ShowChatOverlayCard();
        await EnterGlobalLaneAsync();
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

            // The room is not a Rust channel: it has no server to confirm against and its refusals
            // are its own, so it does not go through the reliable-send path.
            bool confirmed = _activeChatChannel == ChatChannel.Global
                ? await SendGlobalChatAsync(text)
                : await SendChatReliableAsync(text, _activeChatChannel);

            if (confirmed)
            {
                TxtChatInput.Clear();
            }
            else if (_activeChatChannel != ChatChannel.Global)
            {
                // The global path has already said which refusal this was; a generic line after it
                // would overwrite the specific one.
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
        bool isGlobal = _activeChatChannel == ChatChannel.Global;

        // The room's history lives in the feed, not in a log here, so its total comes from there.
        int totalAvailable;
        if (isGlobal)
        {
            totalAvailable = Services.Social.GlobalChatFeed.Lines(
                Services.Social.GlobalChatFeed.DefaultRoom).Count;
        }
        else
        {
            var log = isClan ? _clanChatHistoryLog : _chatHistoryLog;
            lock (log)
            {
                totalAvailable = log.Count;
            }
        }

        int displayCount = isGlobal ? _globalDisplayedMessagesCount
            : isClan ? _clanDisplayedMessagesCount
            : _displayedMessagesCount;
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

                if (isGlobal) _globalDisplayedMessagesCount += 20;
                else if (isClan) _clanDisplayedMessagesCount += 20;
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

    /// <summary>One chip in the quick command bar: what it reads as, and what it types.</summary>
    public sealed record QuickCommandChip(string Label, string Command, string Tooltip);

    public System.Collections.ObjectModel.ObservableCollection<QuickCommandChip> QuickCommandChips { get; } = new();

    /// <summary>
    /// Rebuilds the chip bar for the channel now in front.
    ///
    /// The chips used to be seven hard-coded buttons reading "!upkeep", "!heli" and so on, which
    /// was wrong in three separate ways: the prefix is configurable and is not always "!", the
    /// command names are configurable too, and two of the chips named commands that do not exist —
    /// Patrol Heli is gone from the game, and there has never been a "!switches". Building them
    /// from the profile means a chip can only ever offer something the profile actually answers.
    /// </summary>
    private void RebuildQuickCommandChips()
    {
        QuickCommandChips.Clear();

        var profile = _vm?.Selected;
        if (profile == null) return;

        string p = string.IsNullOrEmpty(profile.ChatCommandPrefix) ? "!" : profile.ChatCommandPrefix;

        void Chip(string command, string tooltip) =>
            QuickCommandChips.Add(new QuickCommandChip(p + command, p + command, tooltip));

        // The first tool cupboard mapping is named "upkeep" when it is created, but the player can
        // rename it; fall back to the all-cupboards command when nothing is paired yet.
        string upkeep = profile.UpkeepCommandMappings
            .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.Command) && m.EntityId != 0)?.Command
            ?? profile.CmdUpkeepDetail;

        Chip(upkeep, Loc.TextOrNull("QuickCmdUpkeepTip") ?? "Tool cupboard upkeep");
        Chip(profile.CmdCargo, Loc.TextOrNull("QuickCmdCargoTip") ?? "Cargo ship status");

        // Timers are created as "<name>,<minutes>" — a single comma-separated pair. The old chip
        // sent "!timer 15 Oil Rig", which splits into one argument and was silently ignored.
        QuickCommandChips.Add(new QuickCommandChip(
            $"{p}{profile.CmdCustomTimer} 15",
            $"{p}{profile.CmdCustomTimer} oilrig,15",
            Loc.TextOrNull("QuickCmdTimerTip") ?? "Start a 15 minute Oil Rig timer"));

        // Door codes belong to the team. A clan can hold a hundred accounts, so the clan bar
        // offers the in-game time instead of a shortcut to the base codes.
        if (_activeChatChannel == ChatChannel.Clan)
            Chip(profile.CmdTime, Loc.TextOrNull("QuickCmdTimeTip") ?? "In-game time");
        else
            Chip(profile.CmdBaseCodes, Loc.TextOrNull("QuickCmdCodeTip") ?? "Base codes");

        Chip(profile.CmdPop, Loc.TextOrNull("QuickCmdPopTip") ?? "Server player count");
        Chip(profile.CmdList, Loc.TextOrNull("QuickCmdCommandsTip") ?? "List available commands");
    }

    private void BtnToggleQuickCommands_Click(object sender, RoutedEventArgs e)
    {
        if (ChatQuickCommandsDrawer == null) return;

        bool opening = ChatQuickCommandsDrawer.Visibility != Visibility.Visible;
        if (opening) RebuildQuickCommandChips();

        ChatQuickCommandsDrawer.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
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
        if (vm == null) return;

        // In the room, address the handle rather than the display name. Only the handle is unique
        // and only the handle is what the server resolves against — "@Dave Smith" reaches nobody,
        // and would not survive him renaming himself even if it did.
        var token = vm.SourceLine?.Handle;

        if (string.IsNullOrWhiteSpace(token))
        {
            // An in-game line, or a room account from before handles existed. The display name is
            // all there is, and in team chat it is what people read anyway.
            token = vm.Author;
        }

        if (string.IsNullOrWhiteSpace(token)) return;

        TxtChatInput.Text = $"@{token} " + TxtChatInput.Text;
        TxtChatInput.CaretIndex = TxtChatInput.Text.Length;
        TxtChatInput.Focus();
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
