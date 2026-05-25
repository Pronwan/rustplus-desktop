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
    // ====== STATE ======
    private readonly List<TeamChatMessage> _chatHistoryLog = new();
    private DateTime? _lastChatTsForCurrentServer = null;
    private readonly HashSet<string> _pendingChatConfirms = new();
    private DateTime _lastChatDate = DateTime.MinValue;
    private int _displayedMessagesCount = 20;
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

        bool showSep = false;
        string? sepText = null;

        if (time.Date != _lastChatDate.Date)
        {
            showSep = true;
            // Localize the date using the current selected UI culture
            sepText = time.ToString("D", System.Globalization.CultureInfo.CurrentUICulture);
            _lastChatDate = time.Date;
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
            _displayedMessagesCount++;
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
    
    private async Task SendTeamChatSafeAsync(string text)
    {
        // Thread-safe wrapper für Hintergrund-Alerts
        try
        {
            await SendTeamChatReliableAsync(text);
        }
        catch { /* ignore background errors */ }
    }

    private async Task<bool> SendTeamChatReliableAsync(string text)
    {
        if (_rust is not RustPlusClientReal real) return false;
        
        if (text == null)
        {
            AppendLog("[Chat] Fail to send: text is null");
            return false;
        }

        AppendLog($"[Chat] Sending: {text}");
        
        // Füge die Nachricht zu unseren ausstehenden Bestätigungen hinzu
        string trackKey = $"{text.Trim()}_{DateTime.UtcNow:HHmmss}";
        lock (_pendingChatConfirms) { _pendingChatConfirms.Add(trackKey); }

        try
        {
            await real.SendTeamMessageAsync(text);
        }
        catch (Exception ex)
        {
            AppendLog($"[Chat] Fail to send: {ex.Message}");
            return false;
        }

        // Wir warten passiv darauf, dass die WebSocket-Event-Schleife (Real_TeamChatReceived)
        // die Nachricht als Echo zurückbekomnt. Wenn sie ankommt, entfernt die Schleife den trackKey.
        int waitMs = 0;
        int intervalMs = 150;
        int timeoutMs = 4000; // max 4 Sekunden warten pro Versuch

        while (waitMs < timeoutMs)
        {
            await Task.Delay(intervalMs);
            waitMs += intervalMs;

            lock (_pendingChatConfirms)
            {
                if (!_pendingChatConfirms.Contains(trackKey))
                {
                    return true; // Bestätigt!
                }
            }
        }

        // --- RETRY LOGIC (Sanfter Ansatz, max 2 Versuche um Lags nicht zu verschlimmern) ---
        AppendLog($"[Chat] Send unconfirmed (Attempt 2), retrying once...");
        try
        {
            await real.SendTeamMessageAsync(text);
        }
        catch (Exception ex)
        {
            AppendLog($"[Chat] Fail to send on retry: {ex.Message}");
            return false;
        }

        waitMs = 0;
        while (waitMs < timeoutMs)
        {
            await Task.Delay(intervalMs);
            waitMs += intervalMs;

            lock (_pendingChatConfirms)
            {
                if (!_pendingChatConfirms.Contains(trackKey))
                {
                    return true; // Bestätigt beim zweiten Versuch!
                }
            }
        }

        AppendLog($"[Chat] Failed to verify message delivery after 2 attempts: \"{text}\"");
        lock (_pendingChatConfirms) { _pendingChatConfirms.Remove(trackKey); }
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
        
        AppendChatIfNew(m, isHistorical: false);
    }

    private bool AppendChatIfNew(TeamChatMessage m, bool isHistorical = false)
    {
        var profile = _vm?.Selected;
        string prefix = profile?.ChatCommandPrefix ?? "!";
        bool isCommand = m.Text.TrimStart().StartsWith(prefix);

        lock (_chatHistoryLog)
        {
            bool isDuplicate = false;
            foreach (var ext in _chatHistoryLog.AsEnumerable().Reverse().Take(10))
            {
                if (ext.SteamId == m.SteamId && ext.Text == m.Text && Math.Abs((ext.Timestamp - m.Timestamp).TotalSeconds) < 2)
                {
                    isDuplicate = true;
                    break;
                }
            }
            if (!isDuplicate)
            {
                _chatHistoryLog.Add(m);
            }
            else
            {
                return false;
            }

            if (_chatHistoryLog.Count > 1000)
            {
                _chatHistoryLog.RemoveRange(0, 200);
            }
        }

        if (isCommand)
        {
            if (!isHistorical && _rust is RustPlusClientReal real)
            {
                _ = ProcessChatCommands(m);
            }
            
            // Mask the command in the UI to prevent clutter and indicate it was processed
            m = new TeamChatMessage(m.Timestamp, m.Author, m.SteamId, $"[Chat Command] {m.Text}");
        }

        if (!isHistorical)
        {
            Dispatcher.InvokeAsync(() => AddIncomingChatMessage(m.Author, m.Text, m.Timestamp.ToLocalTime(), m.SteamId, autoScroll: true));
        }
        
        // Timestamp für History-Anfragen aktuell halten
        if (!_lastChatTsForCurrentServer.HasValue || m.Timestamp > _lastChatTsForCurrentServer.Value)
            _lastChatTsForCurrentServer = m.Timestamp;

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
        _lastChatDate = DateTime.MinValue;

        List<TeamChatMessage> toDisplay;
        lock (_chatHistoryLog)
        {
            toDisplay = _chatHistoryLog
                .OrderBy(x => x.Timestamp)
                .Skip(Math.Max(0, _chatHistoryLog.Count - _displayedMessagesCount))
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

        // Initialize displayed messages count and rebuild messages from log
        _displayedMessagesCount = 20;
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

        // Fehlende History vom Server nachladen
        try
        {
            var history = await real.GetTeamChatHistoryAsync(_lastChatTsForCurrentServer, limit: 120);
            if (history != null)
            {
                history.Reverse(); // Älteste zuerst
                foreach (var m in history)
                {
                    AppendChatIfNew(m, isHistorical: true);
                }
                
                // Refresh list with any new historical items
                RebuildChatMessages();
                ScrollChatToBottom();
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

            bool confirmed = await SendTeamChatReliableAsync(text);

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
        int totalAvailable;
        lock (_chatHistoryLog)
        {
            totalAvailable = _chatHistoryLog.Count;
        }

        if (_displayedMessagesCount >= totalAvailable)
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
                _displayedMessagesCount += 20;

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
