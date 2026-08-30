using RustPlusDesk.Services.Social;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace RustPlusDesk.Views;

/// <summary>
/// Looking for Group: your own status, who may message you, and (shortly) the listings and inbox.
///
/// Two rules shape this panel. Nothing about you is published until you have read what becomes
/// visible and said so — the consent block appears the moment a mode is picked and the status only
/// commits once it is accepted. And the whole feature needs a cloud account, because an account is
/// what other players message; without one there is nothing for them to reach.
///
/// State lives on the server, not here. Reinstalling or moving to a second machine should not
/// silently drop somebody out of the list they thought they were in.
/// </summary>
public partial class LfgOverlay : UserControl
{
    public event RoutedEventHandler? CloseRequested;
    public event RoutedEventHandler? CloudSetupRequested;

    /// <summary>Set while the code, rather than the user, is ticking a radio button.</summary>
    private bool _suppressEvents;

    /// <summary>The mode waiting for consent, held back until the disclosure is accepted.</summary>
    private LfgMode _pendingMode = LfgMode.None;

    private bool _hasLfgConsent;

    /// <summary>Whether the rules of the public room have been read and accepted.</summary>
    private bool _hasChatConsent;

    public LfgOverlay()
    {
        InitializeComponent();

        Loaded += (_, __) =>
        {
            AttachRealtime();
            _ = RefreshAsync();
        };

        Unloaded += (_, __) => DetachRealtime();
    }

    // ── Live updates ────────────────────────────────────────────────────────

    private bool _realtimeAttached;

    /// <summary>
    /// Hooks the push channels. Attached while the control is in the tree rather than while the
    /// panel is visible: hiding a panel is not the same as leaving the room, and a whisper that
    /// arrives with the panel closed should already be there when it is opened again.
    /// </summary>
    private void AttachRealtime()
    {
        if (_realtimeAttached) return;
        _realtimeAttached = true;

        SocialRealtime.ChatChanged += OnChatChanged;
        SocialRealtime.MessageArrived += OnMessageArrived;
        SocialRealtime.RequestArrived += OnRequestArrived;
    }

    private void DetachRealtime()
    {
        if (!_realtimeAttached) return;
        _realtimeAttached = false;

        SocialRealtime.ChatChanged -= OnChatChanged;
        SocialRealtime.MessageArrived -= OnMessageArrived;
        SocialRealtime.RequestArrived -= OnRequestArrived;
    }

    private void OnChatChanged() => _ = CatchUpChatAsync();

    private void OnRequestArrived() => _ = LoadInboxAsync();

    private async void OnMessageArrived(string conversationId)
    {
        if (_openThread is { } open && open.Id == conversationId)
        {
            MessageList.ItemsSource = await SocialApi.GetMessagesAsync(open.Id).ConfigureAwait(true);

            // It is on screen, so it has been read. Without this the thread would light up as
            // unread in the list behind the very view showing the message.
            await SocialApi.MarkReadAsync(open.Id).ConfigureAwait(true);
        }

        await LoadInboxAsync().ConfigureAwait(true);
    }

    /// <summary>Kept for callers that cannot await; the work still happens.</summary>
    public void Refresh() => _ = RefreshAsync();

    /// <summary>Loads the stored state. Safe to call whenever the panel is opened.</summary>
    public async Task RefreshAsync()
    {
        bool hasCloud = Services.Cloud.CloudAuthManager.IsAuthenticated;
        CloudGate.Visibility = hasCloud ? Visibility.Collapsed : Visibility.Visible;
        Body.Visibility = hasCloud ? Visibility.Visible : Visibility.Collapsed;
        if (!hasCloud) return;

        SocialRealtime.EnsureStarted();

        var settings = await SocialApi.GetSettingsAsync().ConfigureAwait(true);

        // Closed for this account. Everything below would be refused, so nothing below is asked -
        // and the notice says which of the two silences this is, because "not yet" and "you were
        // removed" look identical from an empty panel.
        if (settings is { Enabled: false })
        {
            ClosedGate.Visibility = Visibility.Visible;
            Body.Visibility = Visibility.Collapsed;
            return;
        }

        ClosedGate.Visibility = Visibility.Collapsed;

        var mode = await SocialApi.GetListingAsync().ConfigureAwait(true);

        _suppressEvents = true;
        try
        {
            _hasLfgConsent = settings?.LfgConsent ?? false;
            _hasChatConsent = settings?.ChatConsent ?? false;

            (mode switch
            {
                LfgMode.LookingForTeam => RbModeLfg,
                LfgMode.LookingForMembers => RbModeLfm,
                _ => RbModeNone,
            }).IsChecked = true;

            PopulateAcceptModes(mode != LfgMode.None);
            SelectAcceptMode(settings?.Accept ?? AcceptMode.Auto);

            ConsentPanel.Visibility = Visibility.Collapsed;
            ApplyListedConstraints(mode != LfgMode.None);
        }
        finally
        {
            _suppressEvents = false;
        }

        // A listing expires two days after its last sign of life. Renewing on open keeps somebody
        // who uses the app listed, and lets the entry of somebody who stopped fall away.
        if (mode != LfgMode.None)
            _ = SocialApi.RenewListingAsync();

        await LoadChatAsync().ConfigureAwait(true);
        await LoadListingsAsync().ConfigureAwait(true);
        await LoadInboxAsync().ConfigureAwait(true);
    }

    private async void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;

        var mode = ReferenceEquals(sender, RbModeLfg) ? LfgMode.LookingForTeam
                 : ReferenceEquals(sender, RbModeLfm) ? LfgMode.LookingForMembers
                 : LfgMode.None;

        // Stopping never needs consent — only appearing does.
        if (mode == LfgMode.None)
        {
            ConsentPanel.Visibility = Visibility.Collapsed;
            _pendingMode = LfgMode.None;
            ApplyListedConstraints(false);
            await SocialApi.SetListingAsync(LfgMode.None).ConfigureAwait(true);
            return;
        }

        if (_hasLfgConsent)
        {
            await PublishAsync(mode).ConfigureAwait(true);
            return;
        }

        _pendingMode = mode;
        ConsentPanel.Visibility = Visibility.Visible;
    }

    private async void BtnConsentAccept_Click(object sender, RoutedEventArgs e)
    {
        ConsentPanel.Visibility = Visibility.Collapsed;

        if (!await SocialApi.ConsentAsync("lfg").ConfigureAwait(true))
        {
            // The disclosure was never recorded, so publishing would be refused anyway. Put the
            // switch back rather than leave the panel claiming a state the server does not have.
            ResetModeToNone();
            return;
        }

        _hasLfgConsent = true;
        await PublishAsync(_pendingMode).ConfigureAwait(true);
        _pendingMode = LfgMode.None;
    }

    private void BtnConsentCancel_Click(object sender, RoutedEventArgs e)
    {
        ResetModeToNone();
        _pendingMode = LfgMode.None;
    }

    private async void AcceptMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (CmbAcceptMode.SelectedItem is not AcceptChoice choice) return;

        await SocialApi.SetAcceptModeAsync(choice.Mode).ConfigureAwait(true);
    }

    /// <summary>
    /// Three settings in a dropdown rather than three radio rows. The choice is made once and
    /// afterwards only read, so the row that says what it currently is does the same job in a
    /// fifth of the height.
    ///
    /// While a listing is up, "don't allow messages" is not among them. Offering a choice and
    /// then undoing it is worse than not offering it: the user sees their pick reverted and has
    /// no way to tell a rule from a bug.
    /// </summary>
    private void PopulateAcceptModes(bool isListed)
    {
        var choices = new System.Collections.Generic.List<AcceptChoice>
        {
            new(AcceptMode.Auto, Properties.Resources.GetString("LfgAcceptAuto")),
            new(AcceptMode.Approval, Properties.Resources.GetString("LfgAcceptApproval")),
        };

        if (!isListed)
            choices.Add(new AcceptChoice(AcceptMode.Off, Properties.Resources.GetString("LfgAcceptOff")));

        // Rebuilding drops the selection, so remember it and restore it below.
        var current = (CmbAcceptMode.SelectedItem as AcceptChoice)?.Mode ?? AcceptMode.Auto;

        _suppressEvents = true;
        try
        {
            CmbAcceptMode.ItemsSource = choices;
            CmbAcceptMode.DisplayMemberPath = nameof(AcceptChoice.Label);
            CmbAcceptMode.SelectedItem = choices.FirstOrDefault(c => c.Mode == current) ?? choices[0];
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void SelectAcceptMode(AcceptMode mode)
    {
        if (CmbAcceptMode.ItemsSource is not System.Collections.Generic.IEnumerable<AcceptChoice> items) return;

        CmbAcceptMode.SelectedItem = items.FirstOrDefault(c => c.Mode == mode) ?? items.First();
    }

    private sealed record AcceptChoice(AcceptMode Mode, string Label);

    private async Task PublishAsync(LfgMode mode)
    {
        if (mode == LfgMode.None) return;

        if (await SocialApi.SetListingAsync(mode).ConfigureAwait(true))
        {
            ApplyListedConstraints(true);
            return;
        }

        // Refused — most likely the consent version moved on. Reload rather than guess, so the
        // panel ends up showing whatever is actually stored.
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Refusing all messages while advertising is a contradiction: it puts you in a list nobody
    /// can reach you through. So while a mode is set, that choice is unavailable — and if it was
    /// the active one, it becomes "ask me first" rather than "accept everything". Being listed
    /// should not quietly open the floodgates either.
    /// </summary>
    private void ApplyListedConstraints(bool isListed)
    {
        // Somebody who had refused all messages and then lists themselves lands on "ask me
        // first", which is what the server does too. Rebuilding the list handles that on its
        // own: the option is gone, so the nearest remaining choice is taken.
        var wasOff = (CmbAcceptMode.SelectedItem as AcceptChoice)?.Mode == AcceptMode.Off;

        PopulateAcceptModes(isListed);

        if (isListed && wasOff)
        {
            _suppressEvents = true;
            try { SelectAcceptMode(AcceptMode.Approval); }
            finally { _suppressEvents = false; }
        }

        AcceptOffBlockedNote.Visibility = isListed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResetModeToNone()
    {
        _suppressEvents = true;
        try
        {
            RbModeNone.IsChecked = true;
            ConsentPanel.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    // ── The two lists ───────────────────────────────────────────────────────

    /// <summary>Raised with the player the user wants to write to; the inbox handles it from there.</summary>
    public event EventHandler<Models.LfgEntry>? ChatRequested;

    private bool _languagesPopulated;

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _ = LoadListingsAsync();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressEvents) return;
        _ = LoadListingsAsync();
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
        => Filter_Changed(sender, (RoutedEventArgs)e);

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => _ = LoadListingsAsync();

    private void BtnOpenChat_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.LfgEntry entry) return;

        ChatRequested?.Invoke(this, entry);
        StartCompose(entry);
    }

    private async Task LoadListingsAsync()
    {
        PopulateLanguages();

        var mode = TabPlayers.IsChecked == true ? LfgMode.LookingForTeam : LfgMode.LookingForMembers;
        var language = (CmbLanguage.SelectedItem as LanguageChoice)?.Code;

        var entries = await SocialApi
            .GetListingsAsync(mode, language, ChkOnlineOnly.IsChecked == true)
            .ConfigureAwait(true);

        ListingList.ItemsSource = entries;

        // An empty board and an unreachable one look the same from here, and the notice says the
        // same thing for both: there is nobody to write to right now.
        ListEmptyNotice.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Every language the app ships, because that is every language somebody can be listed
    /// under. Offering a shorter list made the players running the app in the other twenty-five
    /// unfindable — they were in the board, and no filter reached them.
    /// </summary>
    private void PopulateLanguages()
    {
        if (_languagesPopulated) return;
        _languagesPopulated = true;

        _suppressEvents = true;
        try
        {
            var choices = new System.Collections.Generic.List<LanguageChoice>
            {
                new(null, Properties.Resources.GetString("LfgFilterLanguage")),
            };

            choices.AddRange(Helpers.AppLanguages.All.Select(l => new LanguageChoice(l.Code, l.Name)));

            CmbLanguage.ItemsSource = choices;
            CmbLanguage.DisplayMemberPath = nameof(LanguageChoice.Label);
            CmbLanguage.SelectedIndex = 0;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private sealed record LanguageChoice(string? Code, string Label);

    // ── Inbox ───────────────────────────────────────────────────────────────

    private Models.SocialThread? _openThread;

    /// <summary>Set while writing a first message to somebody who has no thread with us yet.</summary>
    private Models.LfgEntry? _composeTarget;

    /// <summary>
    /// Opens the thread view as an empty conversation addressed to somebody from a listing.
    ///
    /// Reuses the thread view rather than adding a compose dialog: from the user's side this is
    /// the same act as replying, and a second surface for it would only be a second place where
    /// the send button lives.
    /// </summary>
    private void StartCompose(Models.LfgEntry entry)
    {
        _openThread = null;
        _composeTarget = entry;

        ThreadTitle.Text = string.Format(
            Properties.Resources.GetString("LfgComposeTitle"), entry.DisplayName);

        ThreadListView.Visibility = Visibility.Collapsed;
        ThreadView.Visibility = Visibility.Visible;

        PendingBar.Visibility = Visibility.Collapsed;
        DeclinedHint.Visibility = Visibility.Collapsed;
        ComposeHint.Visibility = Visibility.Visible;
        ReplyRow.Visibility = Visibility.Visible;

        MessageList.ItemsSource = null;

        // The server refuses a longer opener; matching it here means the limit is felt while
        // typing rather than reported after sending.
        TxtReply.MaxLength = 300;
        TxtReply.PlaceholderText = Properties.Resources.GetString("LfgComposePlaceholder");
        TxtReply.Text = "";
        TxtReply.Focus();
    }

    /// <summary>Loads the thread list. Called on open and after anything that changes it.</summary>
    private async Task LoadInboxAsync()
    {
        var threads = await SocialApi.GetThreadsAsync().ConfigureAwait(true);

        ThreadList.ItemsSource = threads;
        InboxEmptyNotice.Visibility = threads.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Thread_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.SocialThread thread) return;

        await OpenThreadAsync(thread).ConfigureAwait(true);
    }

    private async Task OpenThreadAsync(Models.SocialThread thread)
    {
        _openThread = thread;
        _composeTarget = null;
        ComposeHint.Visibility = Visibility.Collapsed;
        TxtReply.MaxLength = 1000;
        TxtReply.PlaceholderText = Properties.Resources.GetString("LfgReplyPlaceholder");

        ThreadTitle.Text = thread.CounterpartName;
        ThreadListView.Visibility = Visibility.Collapsed;
        ThreadView.Visibility = Visibility.Visible;

        // A request that has not been answered gets its decision above the reply box, and no reply
        // box at all: being able to type before deciding invites answering by accident.
        PendingBar.Visibility = thread.IsPending ? Visibility.Visible : Visibility.Collapsed;
        PendingHint.Text = string.Format(
            Properties.Resources.GetString("LfgPendingHint"), thread.CounterpartName);

        DeclinedHint.Visibility = thread.IsDeclined ? Visibility.Visible : Visibility.Collapsed;
        ReplyRow.Visibility = thread.IsPending || thread.IsDeclined ? Visibility.Collapsed : Visibility.Visible;

        MessageList.ItemsSource = await SocialApi.GetMessagesAsync(thread.Id).ConfigureAwait(true);

        // Reading it is what marks it read. Doing that on send instead would leave a thread you
        // looked at and did not answer sitting there as unread.
        if (thread.UnreadCount > 0)
        {
            await SocialApi.MarkReadAsync(thread.Id).ConfigureAwait(true);
            await LoadInboxAsync().ConfigureAwait(true);
        }
    }

    private void BtnThreadBack_Click(object sender, RoutedEventArgs e)
    {
        _openThread = null;
        _composeTarget = null;
        ThreadView.Visibility = Visibility.Collapsed;
        ThreadListView.Visibility = Visibility.Visible;
    }

    private async void BtnSend_Click(object sender, RoutedEventArgs e) => await SendReplyAsync();

    private async void TxtReply_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Enter sends, Shift+Enter would be a newline — but the box is single-line, so this is
        // just the shortcut people reach for.
        if (e.Key != System.Windows.Input.Key.Enter) return;
        if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) return;

        e.Handled = true;
        await SendReplyAsync();
    }

    private async Task SendReplyAsync()
    {
        var body = TxtReply.Text?.Trim();
        if (string.IsNullOrEmpty(body)) return;

        // Cleared before the call, not after: leaving the text in place while the request is in
        // flight is how the same message gets sent twice.
        TxtReply.Text = "";

        var sent = _composeTarget is { } target
            ? await SocialApi.OpenThreadAsync(target.UserId, body!).ConfigureAwait(true)
            : _openThread is { } thread
                ? await SocialApi.ReplyAsync(thread.Id, body!).ConfigureAwait(true)
                : false;

        await LoadInboxAsync().ConfigureAwait(true);

        if (!sent)
        {
            // Refused — most often a closed inbox or a block, neither of which the sender is
            // told apart. Put the text back so it is not lost.
            TxtReply.Text = body;
            return;
        }

        if (_composeTarget is { } opened)
        {
            _composeTarget = null;

            // Step into the thread that now exists, so the first message appears where the reply
            // to it will. Falling back to the list is better than an empty view if the thread
            // cannot be found - that only happens if the server disagrees about what was created.
            var created = (ThreadList.ItemsSource as System.Collections.Generic.IEnumerable<Models.SocialThread>)?
                .FirstOrDefault(t => t.CounterpartId == opened.UserId);

            if (created is null) BtnThreadBack_Click(this, new RoutedEventArgs());
            else await OpenThreadAsync(created).ConfigureAwait(true);

            return;
        }

        if (_openThread is { } current)
            MessageList.ItemsSource = await SocialApi.GetMessagesAsync(current.Id).ConfigureAwait(true);
    }

    private async void BtnAcceptRequest_Click(object sender, RoutedEventArgs e)
        => await SettleRequestAsync(accept: true);

    private async void BtnDeclineRequest_Click(object sender, RoutedEventArgs e)
        => await SettleRequestAsync(accept: false);

    private async Task SettleRequestAsync(bool accept)
    {
        if (_openThread is null) return;

        var ok = accept
            ? await SocialApi.AcceptThreadAsync(_openThread.Id).ConfigureAwait(true)
            : await SocialApi.DeclineThreadAsync(_openThread.Id).ConfigureAwait(true);

        await LoadInboxAsync().ConfigureAwait(true);

        if (!ok)
        {
            BtnThreadBack_Click(this, e: new RoutedEventArgs());
            return;
        }

        // Reopen from the refreshed list rather than patching the object in hand, so what is shown
        // is the state the server actually holds.
        var refreshed = (ThreadList.ItemsSource as System.Collections.Generic.IEnumerable<Models.SocialThread>)?
            .FirstOrDefault(t => t.Id == _openThread.Id);

        if (refreshed is null) BtnThreadBack_Click(this, new RoutedEventArgs());
        else await OpenThreadAsync(refreshed).ConfigureAwait(true);
    }

    /// <summary>Opens a thread with somebody from the listings, or reuses the one that exists.</summary>
    public async Task StartConversationAsync(Models.LfgEntry entry, string firstMessage)
    {
        if (string.IsNullOrWhiteSpace(entry.UserId)) return;

        await SocialApi.OpenThreadAsync(entry.UserId, firstMessage).ConfigureAwait(true);
        await LoadInboxAsync().ConfigureAwait(true);
    }

    private async void MenuBlock_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.SocialThread thread) return;
        if (thread.CounterpartId is null) return;

        await SocialApi.BlockAsync(thread.CounterpartId).ConfigureAwait(true);
        await LoadInboxAsync().ConfigureAwait(true);
    }

    private void MenuReport_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.SocialThread thread) return;
        if (thread.CounterpartId is null) return;

        OpenReport(new ReportTarget(thread.CounterpartId, thread.CounterpartName, null, null));
    }

    // ── Reporting ───────────────────────────────────────────────────────────

    /// <summary>Always a person, and sometimes one line they wrote.</summary>
    private sealed record ReportTarget(string UserId, string DisplayName, string? MessageId, string? Quote);

    private ReportTarget? _reportTarget;

    /// <summary>
    /// The reasons, paired with the word the server files them under.
    ///
    /// A fixed set rather than free text: somebody working through the queue needs to see the
    /// same six words, not six hundred phrasings of them, and a set is what makes "twelve spam
    /// reports this week" a sentence anybody can act on. The note underneath is where the
    /// phrasing goes.
    /// </summary>
    private static readonly (string Reason, string Key)[] ReasonChoices =
    {
        ("harassment", "ReportReasonHarassment"),
        ("spam", "ReportReasonSpam"),
        ("scam", "ReportReasonScam"),
        ("hate", "ReportReasonHate"),
        ("explicit", "ReportReasonExplicit"),
        ("other", "ReportReasonOther"),
    };

    private void OpenReport(ReportTarget target)
    {
        _reportTarget = target;

        ReportHeading.Text = string.Format(
            Properties.Resources.GetString("ReportTitle"), target.DisplayName);

        BuildReportReasons();

        // The quote is what makes the report actionable without a moderator having to go and
        // find the line - which, a week later, they cannot: the room is pruned.
        var hasQuote = !string.IsNullOrWhiteSpace(target.Quote);
        ReportQuoted.Visibility = hasQuote ? Visibility.Visible : Visibility.Collapsed;
        ReportQuotedText.Text = target.Quote ?? "";

        TxtReportNote.Text = "";
        ReportError.Visibility = Visibility.Collapsed;
        ReportForm.Visibility = Visibility.Visible;
        ReportDone.Visibility = Visibility.Collapsed;
        ReportSheet.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Nothing is preselected. A pre-ticked first option would file "harassment" for everyone
    /// who did not read the list, which is the one reason that must never be the default.
    /// </summary>
    private void BuildReportReasons()
    {
        ReportReasons.Children.Clear();
        BtnReportSubmit.IsEnabled = false;

        var style = (Style)FindResource("SocialDotRadio");

        foreach (var (reason, key) in ReasonChoices)
        {
            var radio = new RadioButton
            {
                GroupName = "ReportReason",
                Style = style,
                Content = Properties.Resources.GetString(key),
                Tag = reason,
            };

            radio.Checked += (_, __) => BtnReportSubmit.IsEnabled = true;
            ReportReasons.Children.Add(radio);
        }
    }

    private async void BtnReportSubmit_Click(object sender, RoutedEventArgs e)
    {
        if (_reportTarget is not { } target) return;

        if (ReportReasons.Children.OfType<RadioButton>()
                .FirstOrDefault(r => r.IsChecked == true)?.Tag is not string reason) return;

        BtnReportSubmit.IsEnabled = false;
        ReportError.Visibility = Visibility.Collapsed;

        var note = TxtReportNote.Text?.Trim();

        var sent = await SocialApi
            .ReportAsync(target.UserId, reason, target.MessageId, string.IsNullOrEmpty(note) ? null : note)
            .ConfigureAwait(true);

        if (!sent)
        {
            ReportError.Visibility = Visibility.Visible;
            BtnReportSubmit.IsEnabled = true;
            return;
        }

        // The receipt replaces the form in the same sheet, so the answer arrives where the
        // question was asked rather than as a toast somewhere else on screen.
        ReportHeading.Text = Properties.Resources.GetString("ReportDoneTitle");
        ReportForm.Visibility = Visibility.Collapsed;
        ReportDone.Visibility = Visibility.Visible;
    }

    private void BtnReportCancel_Click(object sender, RoutedEventArgs e)
    {
        ReportSheet.Visibility = Visibility.Collapsed;
        _reportTarget = null;
    }

    /// <summary>
    /// Blocking straight from the receipt. Reporting asks somebody else to act and takes as long
    /// as that takes; blocking is the part the reporter can do now, and offering it here means
    /// they do not have to go and find the same person again in a list.
    /// </summary>
    private async void BtnReportBlock_Click(object sender, RoutedEventArgs e)
    {
        if (_reportTarget is not { } target) return;

        await SocialApi.BlockAsync(target.UserId).ConfigureAwait(true);

        ReportSheet.Visibility = Visibility.Collapsed;
        _reportTarget = null;

        // Their lines, their listing and their thread all go, and the server filters all three.
        await LoadChatAsync().ConfigureAwait(true);
        await LoadInboxAsync().ConfigureAwait(true);
        await LoadListingsAsync().ConfigureAwait(true);
    }

    // ── The public room ─────────────────────────────────────────────────────

    private void Section_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        var chat = SecChat.IsChecked == true;
        ChatSection.Visibility = chat ? Visibility.Visible : Visibility.Collapsed;
        LfgSection.Visibility = chat ? Visibility.Collapsed : Visibility.Visible;

        if (chat) _ = LoadChatAsync();
    }

    /// <summary>What the room currently holds, so a pushed line can be added to it.</summary>
    private readonly System.Collections.Generic.List<Models.ChatLine> _chatLines = new();

    /// <summary>The window the server serves, matched here so an evening in the room stays bounded.</summary>
    private const int ChatWindow = 200;

    private async Task LoadChatAsync()
    {
        var snapshot = await SocialApi.GetChatAsync().ConfigureAwait(true);

        _chatLines.Clear();
        _chatLines.AddRange(snapshot.Lines);
        ShowChatLines();

        if (snapshot.Ok) ApplyChatSanction(snapshot.Sanction);
    }

    private void ShowChatLines()
    {
        // A fresh array each time: ItemsControl does not notice a list mutated behind its back,
        // and the room is small enough that rebinding it costs nothing worth a collection type.
        ChatList.ItemsSource = _chatLines.ToArray();
        ChatEmptyNotice.Visibility = _chatLines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool _catchUpRunning;
    private bool _catchUpAgain;

    /// <summary>
    /// Fetches what was written since the last line we hold, in answer to a push.
    ///
    /// Reading rather than trusting the payload is what keeps blocks working: the endpoint knows
    /// who the reader has blocked and who has blocked them, and the broadcast — one frame for
    /// the whole room — cannot. A busy room is folded into one request at a time, so ten lines
    /// arriving together cost one read rather than ten.
    /// </summary>
    private async Task CatchUpChatAsync()
    {
        if (_catchUpRunning)
        {
            _catchUpAgain = true;
            return;
        }

        _catchUpRunning = true;
        try
        {
            do
            {
                _catchUpAgain = false;

                var since = _chatLines.LastOrDefault()?.SentAtIso;
                if (since is null)
                {
                    // Nothing to count from — an empty room, or one that failed to load.
                    await LoadChatAsync().ConfigureAwait(true);
                    continue;
                }

                var snapshot = await SocialApi.GetChatAsync(since).ConfigureAwait(true);
                if (!snapshot.Ok) continue;

                ApplyChatSanction(snapshot.Sanction);

                var known = new System.Collections.Generic.HashSet<string>(
                    _chatLines.Select(line => line.Id), StringComparer.Ordinal);

                var added = false;
                foreach (var line in snapshot.Lines)
                    if (known.Add(line.Id)) { _chatLines.Add(line); added = true; }

                if (!added) continue;

                if (_chatLines.Count > ChatWindow)
                    _chatLines.RemoveRange(0, _chatLines.Count - ChatWindow);

                ShowChatLines();
            }
            while (_catchUpAgain);
        }
        finally
        {
            _catchUpRunning = false;
        }
    }

    /// <summary>
    /// Replaces the text box with the reason when writing is closed.
    ///
    /// A box that silently refuses is worse than no box: the user retypes, tries again, and
    /// concludes the app is broken. Saying "until 19:40, for spam" costs one line and answers
    /// the support message before it is written.
    /// </summary>
    private void ApplyChatSanction(Models.ChatSanction? sanction)
    {
        if (sanction is null)
        {
            ChatSilencedBar.Visibility = Visibility.Collapsed;
            ChatComposeRow.Visibility = Visibility.Visible;
            return;
        }

        ChatSilencedBar.Visibility = Visibility.Visible;
        ChatComposeRow.Visibility = Visibility.Collapsed;

        ChatSilencedText.Text = sanction.ExpiresAt is { } until
            ? string.Format(
                Properties.Resources.GetString("ChatSilencedTimeout"),
                until.ToLocalTime().ToString("g"),
                sanction.Reason)
            : string.Format(
                Properties.Resources.GetString("ChatSilencedBan"),
                sanction.Reason);
    }

    private async void BtnChatSend_Click(object sender, RoutedEventArgs e) => await SendChatAsync();

    private async void TxtChat_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) return;

        e.Handled = true;
        await SendChatAsync();
    }

    private async Task SendChatAsync()
    {
        var body = TxtChat.Text?.Trim();
        if (string.IsNullOrEmpty(body)) return;

        // The rules come before the first line, not after it. Sending and then being told is how
        // somebody ends up banned for a message they were never warned about.
        if (!_hasChatConsent)
        {
            ChatRulesPanel.Visibility = Visibility.Visible;
            return;
        }

        ChatRefusal.Visibility = Visibility.Collapsed;
        TxtChat.Text = "";

        var result = await SocialApi.PostChatAsync(body!).ConfigureAwait(true);

        if (result != ChatPostResult.Ok)
        {
            // Put the text back. Whatever the reason, the words are still worth keeping - and for
            // a link the whole point is that they can rewrite it.
            TxtChat.Text = body;
            ShowChatRefusal(result);
        }

        await LoadChatAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// One sentence above the box, saying which refusal this was.
    ///
    /// The reasons are answered differently: reading a notice, waiting, or rewriting the message.
    /// A single "could not send" leaves the user to guess which, and the usual guess is that the
    /// app is broken.
    /// </summary>
    private void ShowChatRefusal(ChatPostResult result)
    {
        // A sanction already has its own bar in place of the text box, drawn from the read that
        // follows this. Saying it twice would be shouting.
        if (result == ChatPostResult.Sanctioned)
        {
            ChatRefusal.Visibility = Visibility.Collapsed;
            return;
        }

        if (result == ChatPostResult.ConsentRequired)
        {
            // The server disagrees with what we thought we had recorded. Ask again.
            _hasChatConsent = false;
            ChatRulesPanel.Visibility = Visibility.Visible;
            ChatRefusal.Visibility = Visibility.Collapsed;
            return;
        }

        ChatRefusal.Text = Properties.Resources.GetString(result switch
        {
            ChatPostResult.LinkNotAllowed => "ChatRefusedLink",
            ChatPostResult.Duplicate => "ChatRefusedDuplicate",
            ChatPostResult.TooNew => "ChatRefusedTooNew",
            ChatPostResult.Empty => "ChatRefusedEmpty",
            _ => "ChatRefusedFailed",
        });

        ChatRefusal.Visibility = Visibility.Visible;
    }

    private async void BtnChatRulesAccept_Click(object sender, RoutedEventArgs e)
    {
        ChatRulesPanel.Visibility = Visibility.Collapsed;

        if (!await SocialApi.ConsentAsync("chat").ConfigureAwait(true))
        {
            // Nothing was recorded, so posting would be refused anyway. Better to say so than to
            // let them type a line that vanishes.
            ShowChatRefusal(ChatPostResult.Failed);
            return;
        }

        _hasChatConsent = true;
        TxtChat.Focus();
    }

    private void BtnChatRulesCancel_Click(object sender, RoutedEventArgs e)
        => ChatRulesPanel.Visibility = Visibility.Collapsed;

    // ── The block list ──────────────────────────────────────────────────────

    private async void BtnBlocks_Click(object sender, RoutedEventArgs e)
    {
        BlocksSheet.Visibility = Visibility.Visible;
        await LoadBlocksAsync().ConfigureAwait(true);
    }

    private void BtnBlocksClose_Click(object sender, RoutedEventArgs e)
        => BlocksSheet.Visibility = Visibility.Collapsed;

    private async Task LoadBlocksAsync()
    {
        var blocks = await SocialApi.GetBlocksAsync().ConfigureAwait(true);

        BlockList.ItemsSource = blocks;
        BlocksEmptyNotice.Visibility = blocks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void BtnUnblock_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.BlockedPlayer blocked) return;

        if (sender is System.Windows.Controls.Control button) button.IsEnabled = false;

        await SocialApi.UnblockAsync(blocked.UserId).ConfigureAwait(true);
        await LoadBlocksAsync().ConfigureAwait(true);

        // Both directions come back at once: their lines reappear in the room, their listing in
        // the board. Reloading here rather than on close means the change is visible where it
        // was made.
        await LoadChatAsync().ConfigureAwait(true);
        await LoadListingsAsync().ConfigureAwait(true);
    }

    // ── The Steam profile behind a listing ──────────────────────────────────

    private Models.LfgEntry? _profileEntry;

    /// <summary>
    /// Opens the profile card for a listing.
    ///
    /// Deliberately reachable only from a listing. The public room shows a name and no more,
    /// which is the promise it makes; a listing already discloses the Steam account and did so
    /// with consent, so showing what Steam shows anyone adds nothing that was not agreed to.
    /// </summary>
    private async void Listing_ProfileClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.LfgEntry entry) return;

        _profileEntry = entry;

        ProfileName.Text = entry.DisplayName;
        ProfileSteamId.Text = entry.SteamId ?? "";
        ProfileSupporterRing.Visibility = entry.IsSupporter ? Visibility.Visible : Visibility.Collapsed;

        ProfileBlurb.Text = entry.Blurb ?? "";
        ProfileBlurb.Visibility = string.IsNullOrWhiteSpace(entry.Blurb) ? Visibility.Collapsed : Visibility.Visible;

        ProfileFacts.Children.Clear();
        ProfileNotice.Visibility = Visibility.Collapsed;
        ProfileVacWarning.Visibility = Visibility.Collapsed;
        ProfileAvatar.ImageSource = SafeImage(entry.AvatarUrl);

        // Shown immediately with what the listing already carries; Steam fills in the rest when
        // it answers. A card that waits on a network call feels broken on a slow connection.
        BtnProfileSteam.IsEnabled = !string.IsNullOrWhiteSpace(entry.SteamId);
        BtnProfileCopy.IsEnabled = !string.IsNullOrWhiteSpace(entry.SteamId);
        ProfileSheet.Visibility = Visibility.Visible;

        var profile = await Services.Social.SteamProfileService.GetAsync(entry.SteamId).ConfigureAwait(true);

        // They may have closed it, or opened another one, while Steam was thinking.
        if (!ReferenceEquals(_profileEntry, entry)) return;

        if (profile is null)
        {
            ProfileNotice.Text = Properties.Resources.GetString("ProfileUnavailable");
            ProfileNotice.Visibility = Visibility.Visible;
            return;
        }

        if (!string.IsNullOrWhiteSpace(profile.PersonaName)) ProfileName.Text = profile.PersonaName;
        if (SafeImage(profile.AvatarUrl) is { } avatar) ProfileAvatar.ImageSource = avatar;

        AddProfileFact("ProfileMember", profile.MemberSince);
        AddProfileFact("ProfileLocation", profile.Location);

        ProfileVacWarning.Visibility = profile.VacBanned ? Visibility.Visible : Visibility.Collapsed;

        if (profile.IsPrivate)
        {
            ProfileNotice.Text = Properties.Resources.GetString("ProfilePrivate");
            ProfileNotice.Visibility = Visibility.Visible;
        }
    }

    /// <summary>One label/value row, skipped entirely when Steam did not give us the value.</summary>
    private void AddProfileFact(string labelKey, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };

        row.Children.Add(new TextBlock
        {
            Text = Properties.Resources.GetString(labelKey) + "  ",
            Foreground = (System.Windows.Media.Brush)FindResource("LfgMuted"),
            FontSize = 11,
        });

        row.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = (System.Windows.Media.Brush)FindResource("LfgText"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        });

        ProfileFacts.Children.Add(row);
    }

    /// <summary>
    /// A remote image, or null. Binding a URL straight into an ImageBrush throws on a malformed
    /// one and takes the panel with it.
    /// </summary>
    private static System.Windows.Media.ImageSource? SafeImage(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try { return new System.Windows.Media.Imaging.BitmapImage(new Uri(url)); }
        catch { return null; }
    }

    private void BtnProfileSteam_Click(object sender, RoutedEventArgs e)
    {
        if (_profileEntry?.SteamId is not { Length: > 0 } steamId) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"https://steamcommunity.com/profiles/{steamId}",
                UseShellExecute = true,
            });
        }
        catch
        {
            // No browser, or the shell refused. Nothing useful to say about it.
        }
    }

    private void BtnProfileCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_profileEntry?.SteamId is not { Length: > 0 } steamId) return;

        try { Clipboard.SetText(steamId); } catch { }
    }

    private void BtnProfileClose_Click(object sender, RoutedEventArgs e)
    {
        ProfileSheet.Visibility = Visibility.Collapsed;
        _profileEntry = null;
    }

    private async void ChatBlock_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.ChatLine line || line.SenderId is null) return;

        await SocialApi.BlockAsync(line.SenderId).ConfigureAwait(true);
        // Their lines disappear on the next read, since the server filters both directions.
        await LoadChatAsync().ConfigureAwait(true);
    }

    private void ChatReport_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.ChatLine line || line.SenderId is null) return;

        // The id goes with it, so the server keeps a copy of what was said even after the line
        // itself is pruned a week later.
        OpenReport(new ReportTarget(line.SenderId, line.SenderName, line.Id, line.Body));
    }

    private void BtnCloudSetup_Click(object sender, RoutedEventArgs e)
        => CloudSetupRequested?.Invoke(this, e);

    private void BtnClose_Click(object sender, RoutedEventArgs e)
        => CloseRequested?.Invoke(this, e);
}
