using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    // ====== SHARED UI STATE ======
    private ChatChannel _activeChatChannel = ChatChannel.Team;
    private bool _isLoadingMoreChat = false;
    private ScrollViewer? _chatScrollViewer;

    // ====== VIEW MODEL ======
    public ObservableCollection<ChatMessageVM> ChatMessages { get; } = new();

    public class ChatMessageVM
    {
        public string Author { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public ImageSource? Avatar { get; set; }
        public bool ShowSeparator { get; set; }
        public string? SeparatorText { get; set; }
        public bool IsMe { get; set; }
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
            // Localize the date using the current selected UI culture
            sepText = time.ToString("D", System.Globalization.CultureInfo.CurrentUICulture);
            if (isClanActive) _lastClanChatDate = time.Date;
            else _lastChatDate = time.Date;
        }

        var vm = new ChatMessageVM
        {
            Author = author,
            Text = text,
            Timestamp = time,
            Avatar = (steamId != 0 && _avatarCache.TryGetValue(steamId, out var img)) ? img : null,
            ShowSeparator = showSep,
            SeparatorText = sepText,
            IsMe = steamId != 0 && steamId == _mySteamId
        };

        ChatMessages.Add(vm);
        
        // Auto-Scroll if chat overlay is visible
        if (autoScroll)
        {
            if (_activeChatChannel == ChatChannel.Clan) _clanDisplayedMessagesCount++;
            else _displayedMessagesCount++;

            if (ChatOverlayPanel.Visibility == Visibility.Visible)
            {
                ScrollChatToBottom();
            }
        }
    }

    private void ScrollChatToBottom()
    {
        if (VisualTreeHelper.GetChildrenCount(ChatList) > 0)
        {
            var border = VisualTreeHelper.GetChild(ChatList, 0) as Border;
            var scrollViewer = border?.Child as ScrollViewer;
            scrollViewer?.ScrollToBottom();
        }
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

        // Thread-safe wrapper für Hintergrund-Alerts
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

        // Füge die Nachricht zu unseren ausstehenden Bestätigungen hinzu
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

        try
        {
            await SendOnceAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"[{tag}] Fail to send: {ex.Message}");
            return false;
        }

        // Wir warten passiv darauf, dass die WebSocket-Event-Schleife (Real_TeamChatReceived/Real_ClanChatReceived)
        // die Nachricht als Echo zurückbekommt. Wenn sie ankommt, entfernt die Schleife den trackKey.
        int waitMs = 0;
        int intervalMs = 150;
        int timeoutMs = 4000; // max 4 Sekunden warten pro Versuch

        while (waitMs < timeoutMs)
        {
            await Task.Delay(intervalMs);
            waitMs += intervalMs;

            lock (pending)
            {
                if (!pending.Contains(trackKey))
                {
                    return true; // Bestätigt!
                }
            }
        }

        // --- RETRY LOGIC (Sanfter Ansatz, max 2 Versuche um Lags nicht zu verschlimmern) ---
        AppendLog($"[{tag}] Send unconfirmed (Attempt 2), retrying once...");
        try
        {
            await SendOnceAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"[{tag}] Fail to send on retry: {ex.Message}");
            return false;
        }

        waitMs = 0;
        while (waitMs < timeoutMs)
        {
            await Task.Delay(intervalMs);
            waitMs += intervalMs;

            lock (pending)
            {
                if (!pending.Contains(trackKey))
                {
                    return true; // Bestätigt beim zweiten Versuch!
                }
            }
        }

        AppendLog($"[{tag}] Failed to verify message delivery after 2 attempts: \"{text}\"");
        lock (pending) { pending.Remove(trackKey); }
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
                // Keine Ausgabe, um Log sauber zu halten (nur im Fehlerfall)
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
        // Bot commands are only recognised on Team chat for now.
        bool isCommand = channel == ChatChannel.Team && m.Text.TrimStart().StartsWith(prefix);

        lock (log)
        {
            bool isDuplicate = false;
            foreach (var ext in log.AsEnumerable().Reverse().Take(10))
            {
                if (ext.SteamId == m.SteamId && ext.Text == m.Text && Math.Abs((ext.Timestamp - m.Timestamp).TotalSeconds) < 2)
                {
                    isDuplicate = true;
                    break;
                }
            }
            if (!isDuplicate)
            {
                log.Add(m);
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
            
            // Mask the command in the UI to prevent clutter and indicate it was processed
            m = new TeamChatMessage(m.Timestamp, m.Author, m.SteamId, $"[Chat Command] {m.Text}");
        }

        if (!isHistorical)
        {
            if (channel == _activeChatChannel)
            {
                Dispatcher.InvokeAsync(() => AddIncomingChatMessage(m.Author, m.Text, m.Timestamp.ToLocalTime(), m.SteamId, autoScroll: true));
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
        
        // Timestamp für History-Anfragen aktuell halten
        if (channel == ChatChannel.Clan)
        {
            if (!_lastClanChatTsForCurrentServer.HasValue || m.Timestamp > _lastClanChatTsForCurrentServer.Value)
                _lastClanChatTsForCurrentServer = m.Timestamp;
        }
        else
        {
            if (!_lastChatTsForCurrentServer.HasValue || m.Timestamp > _lastChatTsForCurrentServer.Value)
                _lastChatTsForCurrentServer = m.Timestamp;
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
                .OrderBy(x => x.Timestamp)
                .Skip(Math.Max(0, log.Count - displayCount))
                .ToList();
        }

        foreach (var m in toDisplay)
        {
            AddIncomingChatMessage(m.Author, m.Text, m.Timestamp.ToLocalTime(), m.SteamId, autoScroll: false);
        }
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
        TxtChatInput.Clear();
        ChatErrorBox.Visibility = Visibility.Collapsed;

        RebuildChatMessages();
        ScrollChatToBottom();

        // Make sure the newly-selected channel is primed and has its history loaded,
        // in case the panel was opened before the tab switch (or the clan tab is being
        // visited for the first time this session).
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
            catch { /* tolerant: chat starts empty, user can still try to send */ }
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
                history.Reverse();
                foreach (var m in history)
                {
                    AppendChatIfNew(m, ChatChannel.Clan, isHistorical: true);
                }

                if (_activeChatChannel == ChatChannel.Clan)
                {
                    RebuildChatMessages();
                    ScrollChatToBottom();
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

        // Clan chat priming happens in the background — it's best-effort (not every
        // server has an active clan) and must never block opening the team chat panel.
        _ = PrimeClanChatIfNeededAsync(real).ContinueWith(t =>
        {
            if (t.Exception != null) AppendLog("PrimeClanChat failed: " + t.Exception.InnerException?.Message);
        }, TaskScheduler.Default);

        // Initialize displayed messages count and rebuild messages from log
        _displayedMessagesCount = 20;
        _clanDisplayedMessagesCount = 20;
        RebuildChatMessages();

        // Overlay einblenden
        ChatContentBorder.Visibility = Visibility.Visible;
        ChatContentBorder.Opacity = 0;

        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        var sb = new System.Windows.Media.Animation.Storyboard();
        sb.Children.Add(fade);
        System.Windows.Media.Animation.Storyboard.SetTarget(fade, ChatContentBorder);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
        sb.Begin();

        // Fokus auf Input
        TxtChatInput.Focus();
        ScrollChatToBottom();

        // Fehlende History vom Server nachladen (Team)
        try
        {
            var history = await real.GetTeamChatHistoryAsync(_lastChatTsForCurrentServer, limit: 120);
            if (history != null)
            {
                history.Reverse(); // Älteste zuerst
                foreach (var m in history)
                {
                    AppendChatIfNew(m, ChatChannel.Team, isHistorical: true);
                }
                
                // Refresh list with any new historical items
                if (_activeChatChannel == ChatChannel.Team)
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
            ChatErrorBox.Visibility = Visibility.Collapsed; // Reset error state
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

        ChatErrorBox.Visibility = Visibility.Collapsed; // Fehler zurücksetzen

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
                // Nicht bestätigt -> Error-Box im Overlay anzeigen, KEIN Popup
                ChatErrorBox.Visibility = Visibility.Visible;
                ChatErrorText.Text = Properties.Resources.MessageNotSentError;
            }
        }
        catch (Exception ex)
        {
            ChatErrorBox.Visibility = Visibility.Visible;
            ChatErrorText.Text = Properties.Resources.ErrorPrefix + ex.Message;
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
        }
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
            // No more older messages to load
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

                // Load 20 more messages
                if (isClan) _clanDisplayedMessagesCount += 20;
                else _displayedMessagesCount += 20;

                // Rebuild the chat list
                RebuildChatMessages();

                // Force layout update so the ScrollViewer updates its ExtentHeight
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
}
