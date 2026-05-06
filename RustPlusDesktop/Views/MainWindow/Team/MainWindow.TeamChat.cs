using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    public sealed class TeamChatRow
    {
        public DateTime Timestamp { get; set; }
        public string Author { get; set; } = "";
        public string Text { get; set; } = "";
        public string TimeStr => Timestamp.ToLocalTime().ToString("HH:mm");
        public Brush AuthorBrush { get; set; } = Brushes.White;
    }

    public ObservableCollection<TeamChatRow> ChatMessages { get; } = new();

    private bool _teamChatViewActive;
    private string? _selfDisplayName; // best-effort for "you" colour
    private bool _teamChatInitialized;

    /// <summary>Wire collection ↔ ItemsControl + replay history. Idempotent.</summary>
    private void InitTeamChat()
    {
        if (_teamChatInitialized) return;
        _teamChatInitialized = true;

        if (TeamChatList != null)
            TeamChatList.ItemsSource = ChatMessages;

        // Replay any messages already in the in-memory log so the inline view comes up populated.
        lock (_chatHistoryLog)
        {
            foreach (var m in _chatHistoryLog.TakeLast(200))
                ChatMessages.Add(BuildChatRow(m));
        }
        ScrollChatToEnd();

        StartSelfCommandPolling();
    }

    // ─── Self-command polling (workaround for Rust+ not echoing own messages) ───

    private DispatcherTimer? _selfCmdPollTimer;
    private DateTime? _selfCmdPollSince;
    private bool _selfCmdPollInFlight;

    private void StartSelfCommandPolling()
    {
        if (_selfCmdPollTimer != null) return;
        _selfCmdPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _selfCmdPollTimer.Tick += async (_, __) => await PollOwnInGameCommandsAsync();
        _selfCmdPollTimer.Start();
    }

    /// <summary>
    /// Pulls recent team-chat history every 5 s and merges it into the inline chat view.
    /// Closes the WebSocket-doesn't-echo-self gap so the user can see in-game messages
    /// (theirs and teammates') in the in-app chat.
    /// </summary>
    private async System.Threading.Tasks.Task PollOwnInGameCommandsAsync()
    {
        if (_selfCmdPollInFlight) return;
        if (_rust is not Services.RustPlusClientReal real) return;
        if (!(_vm?.Selected?.IsConnected ?? false)) return;

        _selfCmdPollInFlight = true;
        try
        {
            var since = _selfCmdPollSince ?? DateTime.UtcNow.AddSeconds(-15);
            var history = await real.GetTeamChatHistoryAsync(since, limit: 30);
            DateTime? newestSeen = null;

            foreach (var m in history.OrderBy(x => x.Timestamp))
            {
                if (newestSeen == null || m.Timestamp > newestSeen)
                    newestSeen = m.Timestamp;

                // AppendChatIfNew handles dedup + appends to inline chat on success.
                AppendChatIfNew(m);
            }

            if (newestSeen.HasValue)
                _selfCmdPollSince = newestSeen.Value.AddMilliseconds(1);
        }
        catch (Exception ex)
        {
            AppendLog($"[chat-poll] {ex.Message}");
        }
        finally
        {
            _selfCmdPollInFlight = false;
        }
    }

    private bool _chatPrimedOnce;

    /// <summary>Toggle between team-list and inline-chat views.</summary>
    private async void BtnTeamChatToggle_Click(object sender, RoutedEventArgs e)
    {
        InitTeamChat();
        _teamChatViewActive = !_teamChatViewActive;

        if (TeamList != null)
            TeamList.Visibility = _teamChatViewActive ? Visibility.Collapsed : Visibility.Visible;
        if (TeamChatPanel != null)
            TeamChatPanel.Visibility = _teamChatViewActive ? Visibility.Visible : Visibility.Collapsed;
        if (BtnTeamChatToggle != null)
            BtnTeamChatToggle.Content = _teamChatViewActive ? "TEAM" : "CHAT";

        if (_teamChatViewActive)
        {
            ScrollChatToEnd();
            TxtTeamChatInput?.Focus();

            // Prime chat subscription + replay missing server history the first time
            // chat is opened in this session.
            if (!_chatPrimedOnce)
            {
                _chatPrimedOnce = true;
                try { await EnsureChatPrimedAsync(); }
                catch (Exception ex) { AppendLog($"[chat] prime failed: {ex.Message}"); }
            }
        }
    }

    /// <summary>Public-ish hook used by Real_TeamChatReceived & co. so they can append into the inline view.</summary>
    private void AppendInlineChat(TeamChatMessage m)
    {
        if (!_teamChatInitialized) return;
        Dispatcher.Invoke(() =>
        {
            ChatMessages.Add(BuildChatRow(m));
            // Cap rendered list to avoid unbounded memory; the persistent _chatHistoryLog
            // is already capped to 100/dedup + 500/disk by existing infra.
            while (ChatMessages.Count > 500) ChatMessages.RemoveAt(0);
            ScrollChatToEnd();
        });
    }

    private TeamChatRow BuildChatRow(TeamChatMessage m)
    {
        bool isSelf = !string.IsNullOrEmpty(_selfDisplayName) &&
                      string.Equals(m.Author, _selfDisplayName, StringComparison.OrdinalIgnoreCase);
        bool isBot = m.Text != null && m.Text.StartsWith("[track]", StringComparison.OrdinalIgnoreCase);
        return new TeamChatRow
        {
            Timestamp = m.Timestamp == default ? DateTime.UtcNow : m.Timestamp,
            Author = m.Author ?? "",
            Text = m.Text ?? "",
            AuthorBrush = isBot ? new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7))
                       : isSelf ? new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A))
                       : new SolidColorBrush(Color.FromRgb(0xEC, 0xEF, 0xF1))
        };
    }

    private void ScrollChatToEnd()
    {
        if (TeamChatScroll == null) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            TeamChatScroll.UpdateLayout();
            TeamChatScroll.ScrollToEnd();
        }), System.Windows.Threading.DispatcherPriority.Render);
    }

    // ─── Send / Enter ────────────────────────────────────────────────────────

    private async void BtnTeamChatSend_Click(object sender, RoutedEventArgs e)
        => await SendChatFromInputAsync();

    private async void TxtTeamChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
        {
            e.Handled = true;
            await SendChatFromInputAsync();
        }
    }

    private async System.Threading.Tasks.Task SendChatFromInputAsync()
    {
        if (TxtTeamChatInput == null) return;
        var text = TxtTeamChatInput.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(text)) return;

        // Guard: never optimistically render a message we can't actually deliver.
        // The Send button + input TextBox are also IsEnabled-bound to Selected.IsConnected
        // in XAML, so this is mainly defense-in-depth for keyboard / programmatic paths.
        if (!(_vm?.Selected?.IsConnected ?? false))
        {
            AppendLog("[chat] not connected — message not sent.");
            return;
        }

        TxtTeamChatInput.Text = "";

        // Normal message: optimistically render locally, then send to game team chat.
        // AppendChatIfNew already routes through AppendInlineChat for the inline view
        // (see MainWindow.xaml.cs); calling AppendInlineChat again here was double-rendering
        // every outgoing message. Dedup handles the polled echo from PollOwnInGameCommandsAsync.
        var outgoing = new TeamChatMessage(DateTime.UtcNow, _selfDisplayName ?? "you", 0, text);
        AppendChatIfNew(outgoing);

        try
        {
            await SendTeamChatSafeAsync(text);
        }
        catch (Exception ex)
        {
            AppendLog($"[chat] send failed: {ex.Message}");
        }
    }

    /// <summary>Best-effort: cache the local player's display name so we can colour their messages.</summary>
    internal void SetSelfDisplayName(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name)) _selfDisplayName = name.Trim();
    }
}
