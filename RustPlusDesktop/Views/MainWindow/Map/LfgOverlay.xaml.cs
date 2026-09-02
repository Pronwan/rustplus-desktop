using RustPlusDesk.Services.Social;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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

    /// <summary>
    /// Which room the chat view is showing. Both tabs share one view: the two rooms differ in who
    /// may write in them, not in what a message looks like, and a second copy of the message
    /// list, the compose bar and the jump-to-bottom button would be two of everything to keep in
    /// step.
    /// </summary>
    private string _room = ChatRooms.Public;

    /// <summary>Whether the supporters' room is open to this account. Answered by every read.</summary>
    private bool _supporterRoom;

    private readonly Controls.Chat.ChatEmojiInputHelper _emojiHelper;

    public LfgOverlay()
    {
        InitializeComponent();

        _emojiHelper = new Controls.Chat.ChatEmojiInputHelper(
            TxtChat,
            GlobalChatAutocompletePopup,
            GlobalChatAutocompleteControl,
            GlobalChatPickerPopup,
            GlobalChatPickerControl,
            BtnChatEmoji);

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
        SocialRealtime.ChatMessageReceived += OnChatMessageReceived;
        SocialRealtime.ChatMessageDeleted += OnChatMessageDeleted;
        SocialRealtime.SlowModeUpdated += OnSlowModeUpdated;
        SocialRealtime.SanctionEventReceived += OnSanctionEventReceived;
        SocialRealtime.MessageArrived += OnMessageArrived;
        SocialRealtime.RequestArrived += OnRequestArrived;
        SocialRealtime.FriendRequestArrived += OnFriendRequestArrived;
        SocialUnread.Changed += ShowUnread;

        // Whatever it already knows, before anything is loaded.
        ShowUnread(SocialUnread.Count);
    }

    private void DetachRealtime()
    {
        if (!_realtimeAttached) return;
        _realtimeAttached = false;

        SocialRealtime.ChatChanged -= OnChatChanged;
        SocialRealtime.ChatMessageReceived -= OnChatMessageReceived;
        SocialRealtime.ChatMessageDeleted -= OnChatMessageDeleted;
        SocialRealtime.SlowModeUpdated -= OnSlowModeUpdated;
        SocialRealtime.SanctionEventReceived -= OnSanctionEventReceived;
        SocialRealtime.MessageArrived -= OnMessageArrived;
        SocialRealtime.RequestArrived -= OnRequestArrived;
        SocialRealtime.FriendRequestArrived -= OnFriendRequestArrived;
        SocialUnread.Changed -= ShowUnread;
    }

    private void OnChatChanged() => _ = CatchUpChatAsync();

    private void OnChatMessageReceived(Models.ChatLine line)
    {
        if (_chatLines.Any(l => l.Id == line.Id)) return;
        _chatLines.Add(line);
        if (_chatLines.Count > ChatWindow)
            _chatLines.RemoveRange(0, _chatLines.Count - ChatWindow);
        ShowChatLines();
    }

    private void OnChatMessageDeleted(string messageId)
    {
        var count = _chatLines.RemoveAll(l => l.Id == messageId);
        if (count > 0) ShowChatLines();
    }

    private void OnSlowModeUpdated(Models.ChatSlowModeEvent e)
    {
        _slowModeSeconds = Math.Max(0, e.Seconds);
        UpdateSlowModeUI();
    }

    private void OnSanctionEventReceived(Models.SystemSanctionEvent e)
    {
        var sanctionLine = Models.ChatLine.FromSanction(e);
        if (!_chatLines.Any(l => l.Id == sanctionLine.Id))
        {
            _chatLines.Add(sanctionLine);
            if (_chatLines.Count > ChatWindow)
                _chatLines.RemoveRange(0, _chatLines.Count - ChatWindow);
            ShowChatLines();
        }

        var myId = Services.Cloud.CloudAuthManager.CurrentUser?.Id;
        if (!string.IsNullOrEmpty(myId) && string.Equals(e.Target?.Id, myId, StringComparison.OrdinalIgnoreCase))
        {
            if (e.IsLifted)
            {
                ApplyChatSanction(null);
            }
            else
            {
                ApplyChatSanction(new Models.ChatSanction(e.Kind, e.Reason, e.ExpiresAt));
            }
        }
    }

    private void OnRequestArrived() => _ = LoadInboxAsync();

    private void OnFriendRequestArrived() => _ = LoadFriendsAsync();

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

        ApplyNameColorControl(settings, Services.Auth.SupabaseAuthManager.IsPremium);

        ClosedGate.Visibility = Visibility.Collapsed;
        _hasLfgConsent = settings?.LfgConsent ?? false;
        _hasChatConsent = settings?.ChatConsent ?? false;

        if (SecChat.IsChecked == true)
        {
            await LoadChatAsync().ConfigureAwait(true);
        }
        else
        {
            await LoadLfgSectionAsync().ConfigureAwait(true);
        }
    }

    private async Task LoadLfgSectionAsync()
    {
        var settings = await SocialApi.GetSettingsAsync().ConfigureAwait(true);
        var myListing = await SocialApi.GetListingDetailsAsync().ConfigureAwait(true);
        var mode = myListing.Mode;

        _suppressEvents = true;
        try
        {
            _hasLfgConsent = settings?.LfgConsent ?? false;
            TxtBlurb.Text = myListing.Blurb ?? "";

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

        _ = UpdateMyListingPreviewAsync();

        // A listing expires two days after its last sign of life. Renewing on open keeps somebody
        // who uses the app listed, and lets the entry of somebody who stopped fall away.
        if (mode != LfgMode.None)
            _ = SocialApi.RenewListingAsync();

        await LoadListingsAsync().ConfigureAwait(true);
        await LoadInboxAsync().ConfigureAwait(true);
        await LoadFriendsAsync().ConfigureAwait(true);
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
            _ = UpdateMyListingPreviewAsync();
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

    private void TxtBlurb_TextChanged(object sender, TextChangedEventArgs e)
    {
        _ = UpdateMyListingPreviewAsync();
    }

    private void TxtBlurb_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
            _ = SaveCurrentBlurbAsync();
        }
    }

    private async void BtnSaveBlurb_Click(object sender, RoutedEventArgs e)
    {
        await SaveCurrentBlurbAsync().ConfigureAwait(true);
    }

    private static string? GetConnectedServerName()
    {
        try
        {
            if (System.Windows.Application.Current?.MainWindow?.DataContext is ViewModels.MainViewModel vm)
            {
                if (vm.Selected?.IsConnected == true && !string.IsNullOrWhiteSpace(vm.Selected?.Name))
                {
                    return vm.Selected.Name;
                }
            }
        }
        catch { }
        return null;
    }

    private async Task SaveCurrentBlurbAsync()
    {
        var isLfg = RbModeLfg.IsChecked == true;
        var isLfm = RbModeLfm.IsChecked == true;
        if (!isLfg && !isLfm) return;

        var mode = isLfg ? LfgMode.LookingForTeam : LfgMode.LookingForMembers;
        var blurb = TxtBlurb.Text?.Trim();
        var serverName = GetConnectedServerName();
        await SocialApi.SetListingAsync(mode, blurb, Helpers.AppLanguages.Current(), serverName).ConfigureAwait(true);
        await UpdateMyListingPreviewAsync().ConfigureAwait(true);
    }

    private async Task UpdateMyListingPreviewAsync()
    {
        var isLfg = RbModeLfg.IsChecked == true;
        var isLfm = RbModeLfm.IsChecked == true;
        var isListed = isLfg || isLfm;

        LfgNotePanel.Visibility = isListed ? Visibility.Visible : Visibility.Collapsed;
        MyListingPreview.Visibility = isListed ? Visibility.Visible : Visibility.Collapsed;

        if (!isListed) return;

        PreviewModeBadge.Text = isLfg
            ? Properties.Resources.GetString("LfgModeLfg")
            : Properties.Resources.GetString("LfgModeLfm");

        var serverName = GetConnectedServerName();
        if (!string.IsNullOrWhiteSpace(serverName))
        {
            PreviewServerName.Text = serverName;
            PreviewServerBadge.Visibility = Visibility.Visible;
        }
        else
        {
            PreviewServerBadge.Visibility = Visibility.Collapsed;
        }

        var steamId = Services.TrackingService.SteamId64;
        var profile = await SteamProfileService.GetAsync(steamId).ConfigureAwait(true);
        var displayName = profile?.PersonaName
            ?? Services.Cloud.CloudAuthManager.CurrentUser?.DisplayName
            ?? Services.Cloud.CloudAuthManager.CurrentUser?.Email
            ?? "You";

        PreviewDisplayName.Text = displayName;

        var isPremium = Services.Auth.SupabaseAuthManager.IsPremium;
        PreviewSupporterRing.Visibility = isPremium ? Visibility.Visible : Visibility.Collapsed;

        var avatarUrl = profile?.AvatarUrl;
        if (!string.IsNullOrEmpty(avatarUrl))
        {
            try
            {
                PreviewAvatar.ImageSource = new System.Windows.Media.Imaging.BitmapImage(new Uri(avatarUrl));
            }
            catch { }
        }

        var lang = Helpers.AppLanguages.Current();
        var flagFile = !string.IsNullOrEmpty(lang) ? Helpers.AppLanguages.FlagFile(lang) : null;
        if (!string.IsNullOrEmpty(flagFile))
        {
            try
            {
                PreviewFlag.Source = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri($"pack://application:,,,/Assets/Flags/{flagFile}.png"));
                PreviewFlag.Visibility = Visibility.Visible;
            }
            catch
            {
                PreviewFlag.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            PreviewFlag.Visibility = Visibility.Collapsed;
        }

        var blurb = TxtBlurb.Text?.Trim();
        PreviewBlurb.Text = !string.IsNullOrEmpty(blurb)
            ? blurb
            : Properties.Resources.GetString("LfgEmptyBlurbPlaceholder");

        int teamCount = 0;
        try
        {
            if (System.Windows.Application.Current?.MainWindow is MainWindow mw && mw.TeamMembers != null)
            {
                teamCount = mw.TeamMembers.Count;
            }
        }
        catch { }

        if (isLfm)
        {
            PreviewTeamSize.Text = teamCount > 0 ? teamCount.ToString() : "1";
            PreviewTeamSizePanel.Visibility = Visibility.Visible;
        }
        else
        {
            PreviewTeamSizePanel.Visibility = Visibility.Collapsed;
        }
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

        var blurb = TxtBlurb.Text?.Trim();
        var serverName = GetConnectedServerName();
        if (await SocialApi.SetListingAsync(mode, blurb, Helpers.AppLanguages.Current(), serverName).ConfigureAwait(true))
        {
            ApplyListedConstraints(true);
            _ = UpdateMyListingPreviewAsync();
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

    private async void BtnOpenChat_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.LfgEntry entry) return;

        ChatRequested?.Invoke(this, entry);
        await OpenConversationWithAsync(entry).ConfigureAwait(true);
    }

    /// <summary>
    /// Takes you to the conversation with somebody from a listing: the one you already have, or
    /// an empty one addressed to them.
    ///
    /// The inbox is its own tab now, so this has to move you there first. It used to open the
    /// thread view in place, which since the tabs went in meant writing into a view that was not
    /// on screen - the button appeared to do nothing at all.
    /// </summary>
    private async Task OpenConversationWithAsync(Models.LfgEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.UserId)) return;

        // Checking the tab is what switches the section; it also kicks off a load of its own,
        // which the await below then waits out rather than racing.
        SecInbox.IsChecked = true;

        await LoadInboxAsync().ConfigureAwait(true);

        var existing = (ThreadList.ItemsSource as System.Collections.Generic.IEnumerable<Models.SocialThread>)?
            .FirstOrDefault(t => t.CounterpartId == entry.UserId);

        // Somebody you have written to before opens where you left off; somebody new gets an
        // empty thread addressed to them, which is the same view with nothing in it yet.
        if (existing is not null) await OpenThreadAsync(existing).ConfigureAwait(true);
        else StartCompose(entry);
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

        var empty = threads.Count == 0;
        InboxEmptyNotice.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        InboxHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

        // Reported rather than counted here: the rail carries the same number and needs it even
        // when this panel has never been opened.
        SocialUnread.Report(threads.Sum(t => t.UnreadCount));
    }

    /// <summary>
    /// Puts the count on the Inbox tab.
    ///
    /// Past a certain point the exact number stops being the useful part, so it stops counting.
    /// </summary>
    private void ShowUnread(int count)
    {
        InboxTabBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        InboxTabBadgeText.Text = count > 99 ? "99+" : count.ToString();
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
        if (thread.IsIncomingPending)
        {
            PendingBar.Visibility = Visibility.Visible;
            PendingActionsRow.Visibility = Visibility.Visible;
            PendingHint.Text = string.Format(
                Properties.Resources.GetString("LfgPendingHint"), thread.CounterpartName);
        }
        else if (thread.IsOutgoingPending)
        {
            PendingBar.Visibility = Visibility.Visible;
            PendingActionsRow.Visibility = Visibility.Collapsed;
            PendingHint.Text = string.Format(
                Properties.Resources.GetString("LfgPendingWaitingHint") ?? "Waiting for {0} to accept your message request...",
                thread.CounterpartName);
        }
        else
        {
            PendingBar.Visibility = Visibility.Collapsed;
        }

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

// Back to the list rather than into the thread that was just created. Stepping into
            // it would mark it read on arrival, and the new conversation would sink into the
            // list unmarked - the one moment it is worth pointing at.
            BtnThreadBack_Click(this, new RoutedEventArgs());

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

        var supporter = SecSupporter.IsChecked == true;
        var chat = SecChat.IsChecked == true || supporter;
        var inbox = SecInbox.IsChecked == true;

        _room = supporter ? ChatRooms.Supporter : ChatRooms.Public;

        // The gate stands in place of the room, not next to it: showing an empty message list
        // above an offer to buy access reads as though the room is simply quiet.
        var gated = supporter && !_supporterRoom;

        ChatSection.Visibility = chat && !gated ? Visibility.Visible : Visibility.Collapsed;
        SupporterGate.Visibility = gated ? Visibility.Visible : Visibility.Collapsed;
        LfgSection.Visibility = !chat && !inbox ? Visibility.Visible : Visibility.Collapsed;
        InboxSection.Visibility = inbox ? Visibility.Visible : Visibility.Collapsed;

        if (inbox)
        {
            _ = LoadInboxAsync();
        }
        else if (chat)
        {
            // Switching rooms means the lines in hand belong to the other one.
            _chatLines.Clear();
            ShowChatLines();

            _userScrolledUp = false;
            _ = LoadChatAsync();
        }
        else
        {
            _ = LoadLfgSectionAsync();
        }
    }

    /// <summary>What the room currently holds, so a pushed line can be added to it.</summary>
    private readonly System.Collections.Generic.List<Models.ChatLine> _chatLines = new();

    /// <summary>The window the server serves, matched here so an evening in the room stays bounded.</summary>
    private const int ChatWindow = 200;

    private int _slowModeSeconds;
    private System.Windows.Threading.DispatcherTimer? _cooldownTimer;
    private int _remainingCooldownSeconds;
    private bool _userScrolledUp;

    private void ChatScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (ChatScrollViewer == null) return;

        if (e.ExtentHeightChange > 0 && !_userScrolledUp)
        {
            ChatScrollViewer.ScrollToEnd();
            if (BtnScrollToBottom != null)
                BtnScrollToBottom.Visibility = Visibility.Collapsed;
            return;
        }

        bool isScrolledUp = ChatScrollViewer.VerticalOffset < (ChatScrollViewer.ScrollableHeight - 30);
        _userScrolledUp = isScrolledUp;

        if (BtnScrollToBottom != null)
        {
            BtnScrollToBottom.Visibility = (isScrolledUp && _chatLines.Count > 5) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void BtnScrollToBottom_Click(object sender, RoutedEventArgs e)
    {
        _userScrolledUp = false;
        ChatScrollViewer?.ScrollToEnd();
        if (BtnScrollToBottom != null)
            BtnScrollToBottom.Visibility = Visibility.Collapsed;
    }

    private void UpdateSlowModeUI()
    {
        if (_slowModeSeconds > 0)
        {
            var format = Properties.Resources.GetString("ChatSlowModeIndicator") ?? "Slow Mode: {0}s";
            ChatSlowModeText.Text = string.Format(format, _slowModeSeconds);
            ChatSlowModeBar.Visibility = Visibility.Visible;
        }
        else
        {
            ChatSlowModeBar.Visibility = Visibility.Collapsed;
        }
    }

    private void StartSendCooldown(int seconds)
    {
        if (seconds <= 0) return;
        _remainingCooldownSeconds = seconds;
        BtnChatSend.IsEnabled = false;
        UpdateCooldownButtonLabel();

        _cooldownTimer?.Stop();
        _cooldownTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _cooldownTimer.Tick += (_, __) =>
        {
            _remainingCooldownSeconds--;
            if (_remainingCooldownSeconds <= 0)
            {
                _cooldownTimer?.Stop();
                _cooldownTimer = null;
                BtnChatSend.IsEnabled = true;
                BtnChatSend.Content = Properties.Resources.GetString("LfgSend");
            }
            else
            {
                UpdateCooldownButtonLabel();
            }
        };
        _cooldownTimer.Start();
    }

    private void UpdateCooldownButtonLabel()
    {
        var sendLabel = Properties.Resources.GetString("LfgSend") ?? "Send";
        BtnChatSend.Content = $"{sendLabel} ({_remainingCooldownSeconds}s)";
    }

    /// <summary>Somebody wants to know what supporting buys. The window that answers is not ours.</summary>
    public event EventHandler? SupporterOfferRequested;

    private void BtnSupporterOffer_Click(object sender, RoutedEventArgs e)
        => SupporterOfferRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Keeps the supporter tab honest about whether it leads anywhere.
    ///
    /// The answer rides on every read of either room, so a plan bought while the panel is open
    /// takes effect on the next refresh rather than on the next restart.
    /// </summary>
    private void ApplySupporterAccess(Models.ChatSnapshot snapshot)
    {
        if (!snapshot.Ok) return;

        _supporterRoom = snapshot.SupporterRoom;

        if (SecSupporter.IsChecked != true) return;

        var gated = !_supporterRoom;
        SupporterGate.Visibility = gated ? Visibility.Visible : Visibility.Collapsed;
        ChatSection.Visibility = gated ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task LoadChatAsync()
    {
        var snapshot = await SocialApi.GetChatAsync(room: _room).ConfigureAwait(true);

        ApplySupporterAccess(snapshot);

        _chatLines.Clear();
        _chatLines.AddRange(snapshot.Lines);
        _userScrolledUp = false;
        ShowChatLines();

        _slowModeSeconds = snapshot.SlowModeSeconds;
        UpdateSlowModeUI();

        if (snapshot.MaxLength > 0 && TxtChat.MaxLength != snapshot.MaxLength)
        {
            TxtChat.MaxLength = snapshot.MaxLength;
            UpdateChatCharCount();
        }

        if (snapshot.Ok) ApplyChatSanction(snapshot.Sanction);
    }

    private void TxtChat_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateChatCharCount();
    }

    private void UpdateChatCharCount()
    {
        if (TxtChatCharCount == null || TxtChat == null) return;
        int len = TxtChat.Text?.Length ?? 0;
        int max = TxtChat.MaxLength > 0 ? TxtChat.MaxLength : 128;
        TxtChatCharCount.Text = $"{len}/{max}";
    }

    private void UpdateChatGrouping()
    {
        for (int i = 0; i < _chatLines.Count; i++)
        {
            if (i == 0)
            {
                _chatLines[i].ShowHeader = true;
                continue;
            }

            var curr = _chatLines[i];
            var prev = _chatLines[i - 1];

            if (curr.IsSystemSanction || prev.IsSystemSanction)
            {
                curr.ShowHeader = true;
                continue;
            }

            // Same sender within 5 minutes collapses the redundant header (Discord/Slack style)
            bool sameSender = (!string.IsNullOrEmpty(curr.SenderId) && string.Equals(curr.SenderId, prev.SenderId, StringComparison.Ordinal))
                || string.Equals(curr.SenderName, prev.SenderName, StringComparison.Ordinal);

            bool closeInTime = curr.SentAt.HasValue && prev.SentAt.HasValue && Math.Abs((curr.SentAt.Value - prev.SentAt.Value).TotalMinutes) < 5;

            curr.ShowHeader = !(sameSender && closeInTime);
        }
    }

    private void ShowChatLines()
    {
        UpdateChatGrouping();

        // A fresh array each time: ItemsControl does not notice a list mutated behind its back,
        // and the room is small enough that rebinding it costs nothing worth a collection type.
        ChatList.ItemsSource = _chatLines.ToArray();
        ChatEmptyNotice.Visibility = _chatLines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (!_userScrolledUp)
        {
            Dispatcher.InvokeAsync(() =>
            {
                ChatScrollViewer?.ScrollToEnd();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
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

                var snapshot = await SocialApi.GetChatAsync(since, room: _room).ConfigureAwait(true);
                if (!snapshot.Ok) continue;

                ApplyChatSanction(snapshot.Sanction);
                _slowModeSeconds = snapshot.SlowModeSeconds;
                UpdateSlowModeUI();

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
            ChatComposeArea.Visibility = Visibility.Visible;
            return;
        }

        ChatSilencedBar.Visibility = Visibility.Visible;
        ChatComposeArea.Visibility = Visibility.Collapsed;

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
        // Backspace in an empty box drops the reply instead of doing nothing — the gesture people
        // already have from every other chat. Only when the box is empty, so it never eats a
        // keystroke someone meant for their text.
        if (e.Key == System.Windows.Input.Key.Back
            && _replyTarget is not null
            && string.IsNullOrEmpty(TxtChat.Text))
        {
            e.Handled = true;
            ClearReplyTarget();
            return;
        }

        if (e.Key != System.Windows.Input.Key.Enter) return;
        if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) return;

        e.Handled = true;
        await SendChatAsync();
    }

    private async Task SendChatAsync()
    {
        if (_remainingCooldownSeconds > 0) return;

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

        var replyTo = _replyTarget;
        ClearReplyTarget();

        var result = await SocialApi.PostChatAsync(body!, _room, replyTo?.Id).ConfigureAwait(true);

        if (result != ChatPostResult.Ok)
        {
            // Put the text back. Whatever the reason, the words are still worth keeping - and for
            // a link the whole point is that they can rewrite it. The reply target comes back with
            // it, since retyping the message and losing what it answered would be its own annoyance.
            TxtChat.Text = body;
            _replyTarget = replyTo;
            ShowReplyBar();
            ShowChatRefusal(result);
        }
        else
        {
            if (_slowModeSeconds > 0)
            {
                StartSendCooldown(_slowModeSeconds);
            }
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

    // ── Friends ─────────────────────────────────────────────────────────────

    private async void BtnFriends_Click(object sender, RoutedEventArgs e)
    {
        FriendsSheet.Visibility = Visibility.Visible;
        FriendsNotice.Visibility = Visibility.Collapsed;

        await LoadFriendsAsync().ConfigureAwait(true);
    }

    private void BtnFriendsClose_Click(object sender, RoutedEventArgs e)
        => FriendsSheet.Visibility = Visibility.Collapsed;

    /// <summary>
    /// Reads the list and the requests on both sides.
    ///
    /// Called whether or not the sheet is open, because the badge on the button has to be right
    /// before somebody opens it - the same reason the unread count moved out of the panel.
    /// </summary>
    private async Task LoadFriendsAsync()
    {
        var list = await SocialApi.GetFriendsAsync().ConfigureAwait(true);

        FriendList.ItemsSource = list.Friends;
        FriendRequestList.ItemsSource = list.Incoming;
        FriendOutgoingList.ItemsSource = list.Outgoing;

        FriendsIncomingHeading.Visibility = list.Incoming.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FriendsOutgoingHeading.Visibility = list.Outgoing.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // The invitation to add somebody only makes sense when there is nothing to look at, and
        // only when we actually managed to look.
        FriendsEmptyNotice.Visibility = list.Ok && list.Friends.Count == 0 && list.Incoming.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        ShowFriendRequests(list.Incoming.Count);
    }

    private void ShowFriendRequests(int count)
    {
        FriendsBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FriendsBadgeText.Text = count > 9 ? "9+" : count.ToString();
    }

    private async void BtnFriendAdd_Click(object sender, RoutedEventArgs e)
    {
        var steamId = TxtFriendSteamId.Text?.Trim() ?? "";
        var message = TxtFriendMessage.Text?.Trim() ?? "";

        if (steamId.Length == 0 || message.Length == 0) return;

        BtnFriendAdd.IsEnabled = false;
        FriendsNotice.Visibility = Visibility.Collapsed;

        try
        {
            var result = await SocialApi.AddFriendAsync(steamId, message).ConfigureAwait(true);

            if (result == SocialApi.FriendRequestResult.Ok)
            {
                TxtFriendSteamId.Text = "";
                TxtFriendMessage.Text = "";

                // Green rather than red: this one is not a refusal.
                FriendsNotice.Foreground = (System.Windows.Media.Brush)FindResource("LfgMuted");
                FriendsNotice.Text = Properties.Resources.GetString("FriendsSent");
                FriendsNotice.Visibility = Visibility.Visible;

                await LoadFriendsAsync().ConfigureAwait(true);
                return;
            }

            FriendsNotice.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xFF, 0x8A, 0x8A));

            FriendsNotice.Text = Properties.Resources.GetString(result switch
            {
                SocialApi.FriendRequestResult.NotFound => "FriendsErrorNotFound",
                SocialApi.FriendRequestResult.Pending => "FriendsErrorPending",
                SocialApi.FriendRequestResult.AlreadyFriends => "FriendsErrorAlready",
                SocialApi.FriendRequestResult.Declined => "FriendsErrorDeclined",
                SocialApi.FriendRequestResult.Refused => "FriendsErrorRefused",
                _ => "FriendsErrorFailed",
            });

            FriendsNotice.Visibility = Visibility.Visible;
        }
        finally
        {
            BtnFriendAdd.IsEnabled = true;
        }
    }

    private async void BtnFriendAccept_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.Friend friend) return;

        await SocialApi.AcceptFriendAsync(friend.Id).ConfigureAwait(true);
        await LoadFriendsAsync().ConfigureAwait(true);
    }

    private async void BtnFriendDecline_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.Friend friend) return;

        await SocialApi.DeclineFriendAsync(friend.Id).ConfigureAwait(true);
        await LoadFriendsAsync().ConfigureAwait(true);
    }

    private async void BtnFriendWithdraw_Click(object sender, RoutedEventArgs e)
        => await RemoveFriendshipAsync(sender);

    private async void MenuFriendRemove_Click(object sender, RoutedEventArgs e)
        => await RemoveFriendshipAsync(sender);

    private async Task RemoveFriendshipAsync(object sender)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.Friend friend) return;

        await SocialApi.RemoveFriendAsync(friend.Id).ConfigureAwait(true);
        await LoadFriendsAsync().ConfigureAwait(true);
    }

    /// <summary>Clicking a friend writes to them, which is the thing you want nine times in ten.</summary>
    private async void Friend_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => await MessageFriendAsync(sender);

    private async void MenuFriendMessage_Click(object sender, RoutedEventArgs e)
        => await MessageFriendAsync(sender);

    private async Task MessageFriendAsync(object sender)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.Friend friend) return;
        if (string.IsNullOrWhiteSpace(friend.UserId)) return;

        FriendsSheet.Visibility = Visibility.Collapsed;

        // The same route a listing takes, so there is one way into a conversation rather than two
        // that drift apart.
        await OpenConversationWithAsync(new Models.LfgEntry
        {
            UserId = friend.UserId,
            DisplayName = friend.DisplayName,
            AvatarUrl = friend.AvatarUrl,
            SteamId = friend.SteamId,
        }).ConfigureAwait(true);
    }

    private void MenuFriendProfile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.Friend friend) return;

        FriendsSheet.Visibility = Visibility.Collapsed;

        OpenProfileSheet(new Models.LfgEntry
        {
            UserId = friend.UserId,
            DisplayName = friend.DisplayName,
            AvatarUrl = friend.AvatarUrl,
            SteamId = friend.SteamId,
        });
    }

    private async void MenuFriendBlock_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.Friend friend) return;
        if (string.IsNullOrWhiteSpace(friend.UserId)) return;

        // Blocking somebody you had as a friend ends the friendship too. Leaving the row behind
        // would be a friends list containing somebody you cannot reach.
        await SocialApi.RemoveFriendAsync(friend.Id).ConfigureAwait(true);
        await SocialApi.BlockAsync(friend.UserId).ConfigureAwait(true);

        await LoadFriendsAsync().ConfigureAwait(true);
        await LoadInboxAsync().ConfigureAwait(true);
    }

    private void ChatAddFriend_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.ChatLine line) return;
        _ = StartFriendRequestAsync(line.SteamId);
    }

    private void ThreadAddFriend_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.SocialThread thread) return;
        _ = StartFriendRequestAsync(thread.CounterpartSteamId);
    }

    /// <summary>
    /// Opens the friends sheet with the id already filled in, so the one message they get is all
    /// that is left to write. Somebody who has no Steam id on their account cannot be added, and
    /// the empty field says so more plainly than a refusal afterwards would.
    /// </summary>
    private async Task StartFriendRequestAsync(string? steamId)
    {
        FriendsSheet.Visibility = Visibility.Visible;
        FriendsNotice.Visibility = Visibility.Collapsed;

        TxtFriendSteamId.Text = steamId ?? "";
        TxtFriendMessage.Text = "";
        TxtFriendMessage.Focus();

        await LoadFriendsAsync().ConfigureAwait(true);
    }

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
    private void Listing_ProfileClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.LfgEntry entry) return;
        OpenProfileSheet(entry);
    }

    private void Preview_ProfileClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var steamId = Services.TrackingService.SteamId64;
        if (string.IsNullOrWhiteSpace(steamId)) return;

        var myEntry = new Models.LfgEntry
        {
            DisplayName = PreviewDisplayName.Text,
            SteamId = steamId,
            IsSupporter = Services.Auth.SupabaseAuthManager.IsPremium,
            Blurb = TxtBlurb.Text?.Trim(),
            ServerName = GetConnectedServerName(),
            Language = Helpers.AppLanguages.Current(),
            IsOnline = true,
            AvatarUrl = (PreviewAvatar.ImageSource as System.Windows.Media.Imaging.BitmapImage)?.UriSource?.ToString(),
        };

        OpenProfileSheet(myEntry);
    }

    private async void OpenProfileSheet(Models.LfgEntry entry)
    {
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

        AddProfileFact("ProfileRustHours", profile.RustHours);
        AddProfileFact("ProfileMember", profile.MemberSince);
        AddProfileFact("ProfileLocation", profile.Location);

        if (profile.VacBanned)
        {
            ProfileVacWarning.Visibility = Visibility.Visible;
            AddProfileFact("ProfileVacStatus", Properties.Resources.GetString("ProfileVac"));
        }
        else
        {
            ProfileVacWarning.Visibility = Visibility.Collapsed;
            AddProfileFact("ProfileVacStatus", Properties.Resources.GetString("ProfileVacClean"));
        }

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

    // ====== SUPPORTER NAME COLOUR ======

    /// <summary>One palette entry, as the swatch grid binds it.</summary>
    private sealed record NameColorChoice(string Key, string Label, Brush Swatch, Brush Selection);

    private static readonly Brush NameColorSelected = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
    private static readonly Brush NameColorUnselected = new SolidColorBrush(Colors.Transparent);

    private string? _myNameColor;

    /// <summary>
    /// Shows the colour button for supporters and points it at the colour in use.
    ///
    /// Called wherever the settings are refreshed, since the answer changes with the plan and with
    /// the user's own choice.
    /// </summary>
    private void ApplyNameColorControl(SocialSettings? settings, bool isSupporter)
    {
        if (BtnChatNameColor is null) return;

        BtnChatNameColor.Visibility = isSupporter ? Visibility.Visible : Visibility.Collapsed;
        if (!isSupporter) return;

        _myNameColor = settings?.NameColor;
        BtnChatNameColor.Tag = Models.ChatLine.ResolveNameBrush(_myNameColor)
            ?? new SolidColorBrush(Color.FromRgb(0x8A, 0x95, 0xA5));
    }

    private void BtnChatNameColor_Click(object sender, RoutedEventArgs e)
    {
        if (ChatNameColorList is null || ChatNameColorPopup is null) return;

        ChatNameColorList.ItemsSource = Models.ChatLine.NameColorPalette
            .Select(entry =>
            {
                var brush = new SolidColorBrush(entry.Color);
                brush.Freeze();
                return new NameColorChoice(
                    entry.Key,
                    Helpers.Loc.TextOrNull("ChatNameColor_" + entry.Key) ?? entry.Key,
                    brush,
                    string.Equals(entry.Key, _myNameColor, StringComparison.OrdinalIgnoreCase)
                        ? NameColorSelected
                        : NameColorUnselected);
            })
            .ToArray();

        ChatNameColorPopup.IsOpen = true;
    }

    private async void ChatNameColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key) return;
        await ApplyNameColorChoiceAsync(key).ConfigureAwait(true);
    }

    private async void ChatNameColorReset_Click(object sender, RoutedEventArgs e)
        => await ApplyNameColorChoiceAsync(null).ConfigureAwait(true);

    private async Task ApplyNameColorChoiceAsync(string? key)
    {
        if (ChatNameColorPopup is not null) ChatNameColorPopup.IsOpen = false;

        var ok = await SocialApi.SetNameColorAsync(key).ConfigureAwait(true);
        if (!ok)
        {
            ShowChatNotice(Helpers.Loc.TextOrNull("ChatNameColorFailed") ?? "The colour could not be saved.");
            return;
        }

        _myNameColor = key;
        if (BtnChatNameColor is not null)
        {
            BtnChatNameColor.Tag = Models.ChatLine.ResolveNameBrush(key)
                ?? new SolidColorBrush(Color.FromRgb(0x8A, 0x95, 0xA5));
        }

        // Existing lines were rendered with the old colour; a reload is the cheapest way to see
        // the change applied to your own past messages too.
        await LoadChatAsync().ConfigureAwait(true);
    }

    // ====== REPLIES ======

    /// <summary>The message the next line will answer, or null when it answers nothing.</summary>
    private Models.ChatLine? _replyTarget;

    private void ChatReply_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.ChatLine line) return;

        _replyTarget = line;
        ShowReplyBar();
        TxtChat.Focus();
        TxtChat.CaretIndex = TxtChat.Text?.Length ?? 0;
    }

    private void ChatCancelReply_Click(object sender, RoutedEventArgs e)
    {
        ClearReplyTarget();
        TxtChat.Focus();
    }

    private void ShowReplyBar()
    {
        if (_replyTarget is null)
        {
            ChatReplyBar.Visibility = Visibility.Collapsed;
            return;
        }

        ChatReplyBarName.Text = _replyTarget.SenderName;
        ChatReplyBarExcerpt.Text = _replyTarget.Body;
        ChatReplyBar.Visibility = Visibility.Visible;
    }

    private void ClearReplyTarget()
    {
        _replyTarget = null;
        ChatReplyBar.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Scrolls to the message a reply answers and flashes it, so the jump is visible.
    ///
    /// Only the loaded window is searched. Older originals are simply not there any more, and
    /// saying nothing happened is better than pretending to look further.
    /// </summary>
    private void ChatJumpToOriginal_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string originalId || string.IsNullOrEmpty(originalId)) return;

        var target = _chatLines.FirstOrDefault(line => line.Id == originalId);
        if (target is null)
        {
            ShowChatNotice(Helpers.Loc.TextOrNull("ChatReplyOriginalGone")
                ?? "The original message is no longer in the visible history.");
            return;
        }

        var container = ChatList.ItemContainerGenerator.ContainerFromItem(target) as FrameworkElement;
        container?.BringIntoView();
    }

    /// <summary>Copies the message body — the one action that makes sense on your own lines too.</summary>
    private void ChatCopyText_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.ChatLine line || string.IsNullOrEmpty(line.Body)) return;

        try { System.Windows.Clipboard.SetText(line.Body); } catch { /* clipboard can be held by another app */ }
    }

    /// <summary>
    /// Translates one message into the app's language, in place.
    ///
    /// Asks for consent the first time, because this sends the message text to Google — the same
    /// consent the patch notes ask for, and the same answer, so agreeing once covers both.
    /// </summary>
    /// <summary>The line waiting on the consent panel's answer, if the question was asked.</summary>
    private Models.ChatLine? _pendingTranslation;

    private async void ChatTranslate_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.ChatLine line || string.IsNullOrWhiteSpace(line.Body)) return;

        if (!Services.TrackingService.TranslationConsentGiven)
        {
            // Remembered so the answer applies to the line that prompted the question, rather
            // than making someone right-click it again afterwards.
            _pendingTranslation = line;
            ChatTranslateConsentPanel.Visibility = Visibility.Visible;
            return;
        }

        await TranslateLineAsync(line).ConfigureAwait(true);
    }

    private async void BtnTranslateConsentAccept_Click(object sender, RoutedEventArgs e)
    {
        Services.TrackingService.TranslationConsentGiven = true;
        ChatTranslateConsentPanel.Visibility = Visibility.Collapsed;

        var line = _pendingTranslation;
        _pendingTranslation = null;
        if (line is not null) await TranslateLineAsync(line).ConfigureAwait(true);
    }

    private void BtnTranslateConsentCancel_Click(object sender, RoutedEventArgs e)
    {
        _pendingTranslation = null;
        ChatTranslateConsentPanel.Visibility = Visibility.Collapsed;
    }

    private async Task TranslateLineAsync(Models.ChatLine line)
    {
        ChatRefusal.Visibility = Visibility.Collapsed;

        var result = await Services.TranslationService.TranslateAsync(line.Body).ConfigureAwait(true);

        // The three outcomes are genuinely different and used to be reported as one. Google
        // answers 429 to this endpoint often enough that "we could not reach it" has to be its
        // own sentence — telling someone their English message is already in German is worse
        // than saying nothing.
        if (!result.Ok)
        {
            ShowChatNotice(Helpers.Loc.TextOrNull("ChatTranslateFailed")
                ?? "The translation service could not be reached. Try again in a moment.");
            return;
        }

        if (result.Unchanged)
        {
            ShowChatNotice(Helpers.Loc.TextOrNull("ChatTranslateUnchanged")
                ?? "This message is already in your language.");
            return;
        }

        var index = _chatLines.FindIndex(item => item.Id == line.Id);
        if (index < 0) return;

        _chatLines[index] = line.WithBody(result.Text);
        ShowChatLines();
    }

    /// <summary>
    /// One sentence above the compose box, in the app's own look.
    ///
    /// Reuses the bar that explains a refused message: same place, same weight, and no modal
    /// window thrown over the map for something this small.
    /// </summary>
    private void ShowChatNotice(string message)
    {
        ChatRefusal.Text = message;
        ChatRefusal.Visibility = Visibility.Visible;
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
