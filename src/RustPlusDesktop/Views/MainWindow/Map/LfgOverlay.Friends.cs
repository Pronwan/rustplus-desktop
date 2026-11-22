using RustPlusDesk.Services.Social;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Motion = RustPlusDesk.Helpers.Motion;

namespace RustPlusDesk.Views;

/// <summary>
/// Friends, friend requests, the block list and the panes they open.
///
/// Every action on somebody here follows one rule: ask in the row, then say what happened in a
/// strip at the bottom, with the way back where there is one. No modal boxes, no silent failures.
/// </summary>
public partial class LfgOverlay
{
    // ── Panes ───────────────────────────────────────────────────────────────

    private readonly Dictionary<Border, int> _paneEpoch = new();

    /// <summary>
    /// Slides a pane in over the hub. Without animation when the system has it turned off or the
    /// control is not on screen yet, which is also what lets it be rendered in a test.
    /// </summary>
    private void ShowPane(Border pane, double dx = 24, double dy = 0)
    {
        _paneEpoch[pane] = _paneEpoch.GetValueOrDefault(pane) + 1;

        var alreadyShown = pane.Visibility == Visibility.Visible;

        // A hide may be mid-way: take its fade away and start from whole.
        pane.BeginAnimation(OpacityProperty, null);
        pane.Opacity = 1;
        pane.Visibility = Visibility.Visible;

        if (pane.RenderTransform is not TranslateTransform { IsFrozen: false } move)
        {
            move = new TranslateTransform();
            pane.RenderTransform = move;
        }

        move.BeginAnimation(TranslateTransform.XProperty, null);
        move.BeginAnimation(TranslateTransform.YProperty, null);
        move.X = 0;
        move.Y = 0;

        if (alreadyShown || !IsLoaded || !Motion.Enabled) return;

        pane.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, Motion.For(Motion.Fast)) { EasingFunction = Motion.Enter });

        if (dx != 0)
            move.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(dx, 0, Motion.For(Motion.Normal)) { EasingFunction = Motion.Enter });

        if (dy != 0)
            move.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(dy, 0, Motion.For(Motion.Normal)) { EasingFunction = Motion.Enter });
    }

    private void HidePane(Border pane)
    {
        if (pane.Visibility != Visibility.Visible) return;

        var epoch = _paneEpoch[pane] = _paneEpoch.GetValueOrDefault(pane) + 1;

        if (!IsLoaded || !Motion.Enabled)
        {
            pane.Visibility = Visibility.Collapsed;
            return;
        }

        var fade = new DoubleAnimation(1, 0, Motion.For(Motion.Faster)) { EasingFunction = Motion.Exit };
        fade.Completed += (_, __) =>
        {
            // Shown again while it was leaving: leave it be.
            if (_paneEpoch.GetValueOrDefault(pane) != epoch) return;

            pane.BeginAnimation(OpacityProperty, null);
            pane.Opacity = 1;
            pane.Visibility = Visibility.Collapsed;
        };

        pane.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Esc peels panes off from the top: an open question first, then the newest pane.</summary>
    private void Hub_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        var asking = _friendRows.Values.FirstOrDefault(r => r.Mode != Models.FriendRowMode.Normal);
        if (asking is not null)
        {
            asking.Mode = Models.FriendRowMode.Normal;
            e.Handled = true;
            return;
        }

        if (ReportSheet.Visibility == Visibility.Visible) BtnReportCancel_Click(this, new RoutedEventArgs());
        else if (ProfileSheet.Visibility == Visibility.Visible) BtnProfileClose_Click(this, new RoutedEventArgs());
        else if (AddFriendSheet.Visibility == Visibility.Visible) BtnAddFriendClose_Click(this, new RoutedEventArgs());
        else if (BlocksSheet.Visibility == Visibility.Visible) BtnBlocksClose_Click(this, new RoutedEventArgs());
        else if (FriendsSheet.Visibility == Visibility.Visible) BtnFriendsClose_Click(this, new RoutedEventArgs());
        else return;

        e.Handled = true;
    }

    private static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private static string Str(string key) => Properties.Resources.GetString(key);

    // ── The status strip ────────────────────────────────────────────────────

    private DispatcherTimer? _toastTimer;
    private Func<Task>? _toastAction;

    /// <summary>
    /// Says what just happened, once, at the bottom. An action label makes it a way back (Undo) or a
    /// way to try again (Retry); errors stay a little longer than confirmations.
    /// </summary>
    private void ShowToast(string text, bool isError = false, string? actionLabel = null, Func<Task>? action = null)
    {
        _toastAction = action;

        HubToastText.Text = text;
        HubToastIcon.Symbol = isError
            ? Wpf.Ui.Controls.SymbolRegular.ErrorCircle24
            : Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle24;
        HubToastIcon.Foreground = (Brush)FindResource(isError ? "LfgDangerText" : "StatusSuccess");

        HubToastAction.Content = actionLabel;
        HubToastAction.Visibility = Vis(!string.IsNullOrEmpty(actionLabel) && action is not null);

        ShowPane(HubToast, 0, 8);

        _toastTimer ??= new DispatcherTimer();
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(isError ? 10 : 8);
        _toastTimer.Tick -= ToastTimer_Tick;
        _toastTimer.Tick += ToastTimer_Tick;
        _toastTimer.Start();
    }

    private void ToastTimer_Tick(object? sender, EventArgs e) => DismissToast();

    private void DismissToast()
    {
        _toastTimer?.Stop();
        _toastAction = null;
        HidePane(HubToast);
    }

    private void HubToastDismiss_Click(object sender, RoutedEventArgs e) => DismissToast();

    private async void HubToastAction_Click(object sender, RoutedEventArgs e)
    {
        var action = _toastAction;
        DismissToast();

        if (action is not null) await action().ConfigureAwait(true);
    }

    private void ShowFailureToast(Func<Task> retry)
        => ShowToast(Str("FriendsActionFailed"), true, Str("FriendsRetry"), retry);

    // ── Friends ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Who the pending request is for, when the pane was opened from somebody's chat line.
    ///
    /// The card above the form shows their name and face; this is the id it stands for. Held
    /// here rather than in the text box because the text box is what the user can see, and an
    /// id belongs to the person it identifies, not to whoever happens to be adding them.
    /// </summary>
    private string? _friendTargetSteamId;

    private List<Models.Friend> _allFriends = new();
    private List<Models.Friend> _allIncoming = new();
    private List<Models.Friend> _allOutgoing = new();

    /// <summary>Rows are kept between reads so an open "are you sure?" survives a refresh.</summary>
    private readonly Dictionary<string, Models.FriendRow> _friendRows = new();

    private bool _friendsLoading;
    private bool _friendsLoaded;
    private bool _friendsFailed;
    private int _friendsLoadSeq;
    private bool _onlineExpanded = true;
    private bool _offlineExpanded = true;

    private async void BtnFriends_Click(object sender, RoutedEventArgs e)
    {
        TxtFriendSearch.Text = "";
        SeedFriendsFromCache();
        RenderFriends();
        ShowPane(FriendsSheet);
        TxtFriendSearch.Focus();

        await LoadFriendsAsync().ConfigureAwait(true);
    }

    /// <summary>What the background poll already knows, so the pane opens full instead of empty.</summary>
    private void SeedFriendsFromCache()
    {
        if (_friendsLoaded) return;
        if (SocialFriends.Friends.Count + SocialFriends.Incoming.Count + SocialFriends.Outgoing.Count == 0) return;

        _allFriends = SocialFriends.Friends.ToList();
        _allIncoming = SocialFriends.Incoming.ToList();
        _allOutgoing = SocialFriends.Outgoing.ToList();
        _friendsLoaded = true;
    }

    private void BtnFriendsClose_Click(object sender, RoutedEventArgs e)
    {
        CancelFriendConfirms();
        HidePane(FriendsSheet);
    }

    private void BtnFriendsRetry_Click(object sender, RoutedEventArgs e) => _ = LoadFriendsAsync();

    private void TxtFriendSearch_TextChanged(object sender, TextChangedEventArgs e) => RenderFriends();

    private void BtnFriendsSection_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag as string == "online") _onlineExpanded = !_onlineExpanded;
        else _offlineExpanded = !_offlineExpanded;

        RenderFriends();
    }

    /// <summary>
    /// Reads the list and the requests on both sides.
    ///
    /// Called whether or not the pane is open, because the badge on the button has to be right
    /// before somebody opens it - the same reason the unread count moved out of the panel. A failed
    /// read keeps what was on screen and says so, rather than turning into "no friends".
    /// </summary>
    private async Task LoadFriendsAsync()
    {
        var seq = ++_friendsLoadSeq;
        _friendsLoading = true;
        RenderFriends();

        Models.FriendList list;
        try { list = await SocialApi.GetFriendsAsync().ConfigureAwait(true); }
        catch { list = new Models.FriendList(new(), new(), new(), false); }

        // A newer read is in flight and will have the last word.
        if (seq != _friendsLoadSeq) return;

        _friendsLoading = false;

        if (!list.Ok)
        {
            _friendsFailed = true;
            RenderFriends();
            return;
        }

        _friendsFailed = false;
        _friendsLoaded = true;
        _allFriends = list.Friends;
        _allIncoming = list.Incoming;
        _allOutgoing = list.Outgoing;

        RenderFriends();
        ShowFriendRequests(_allIncoming.Count);
    }

    private void ShowFriendRequests(int count)
    {
        FriendsBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FriendsBadgeText.Text = count > 9 ? "9+" : count.ToString();
    }

    private List<Models.FriendRow> Rows(IEnumerable<Models.Friend> friends, Models.FriendRowKind kind)
    {
        var rows = new List<Models.FriendRow>();

        foreach (var friend in friends)
        {
            var key = $"{kind}:{friend.Id}";

            if (_friendRows.TryGetValue(key, out var row)) row.Update(friend);
            else _friendRows[key] = row = new Models.FriendRow(friend, kind);

            rows.Add(row);
        }

        return rows;
    }

    private void RenderFriends()
    {
        if (TxtFriendSearch is null) return;

        var layout = FriendsListLayout.Build(_allFriends, _allIncoming, _allOutgoing, TxtFriendSearch.Text);

        var showLoading = _friendsLoading && !_friendsLoaded;
        var showError = _friendsFailed && !_friendsLoaded && !_friendsLoading;

        FriendsLoadingState.Visibility = Vis(showLoading);
        FriendsErrorState.Visibility = Vis(showError);
        FriendsErrorBanner.Visibility = Vis(_friendsFailed && _friendsLoaded);

        var lists = _friendsLoaded;
        FriendsEmptyNotice.Visibility = Vis(lists && layout.IsEmpty);
        FriendsSearchEmptyNotice.Visibility = Vis(lists && layout.HasNoMatches);

        var requests = Rows(layout.Incoming, Models.FriendRowKind.Incoming)
            .Concat(Rows(layout.Outgoing, Models.FriendRowKind.Outgoing))
            .ToList();
        var online = Rows(layout.Online, Models.FriendRowKind.Friend);
        var offline = Rows(layout.Offline, Models.FriendRowKind.Friend);

        // A search always shows what it found, whatever was folded away.
        var onlineOpen = _onlineExpanded || layout.HasQuery;
        var offlineOpen = _offlineExpanded || layout.HasQuery;

        FriendsRequestsSection.Visibility = Vis(lists && requests.Count > 0);
        FriendsRequestsCount.Text = requests.Count.ToString();
        FriendRequestList.ItemsSource = requests;

        FriendsOnlineSection.Visibility = Vis(lists && online.Count > 0);
        FriendsOnlineCount.Text = online.Count.ToString();
        FriendList.ItemsSource = online;
        FriendList.Visibility = Vis(onlineOpen);
        OnlineChevron.Symbol = onlineOpen
            ? Wpf.Ui.Controls.SymbolRegular.ChevronDown24
            : Wpf.Ui.Controls.SymbolRegular.ChevronRight24;

        FriendsOfflineSection.Visibility = Vis(lists && offline.Count > 0);
        FriendsOfflineCount.Text = offline.Count.ToString();
        FriendOfflineList.ItemsSource = offline;
        FriendOfflineList.Visibility = Vis(offlineOpen);
        OfflineChevron.Symbol = offlineOpen
            ? Wpf.Ui.Controls.SymbolRegular.ChevronDown24
            : Wpf.Ui.Controls.SymbolRegular.ChevronRight24;

        if (lists && layout.TotalFriends > 0)
        {
            FriendsSummary.Text = string.Format(Str("FriendsSummary"),
                _allFriends.Count(f => f.IsOnline), layout.TotalFriends);
            FriendsSummary.Visibility = Visibility.Visible;
        }
        else
        {
            FriendsSummary.Visibility = Visibility.Collapsed;
        }

        // Rows that left the list take their state with them.
        var live = new HashSet<string>(
            _allFriends.Select(f => $"{Models.FriendRowKind.Friend}:{f.Id}")
                .Concat(_allIncoming.Select(f => $"{Models.FriendRowKind.Incoming}:{f.Id}"))
                .Concat(_allOutgoing.Select(f => $"{Models.FriendRowKind.Outgoing}:{f.Id}")));
        foreach (var stale in _friendRows.Keys.Where(k => !live.Contains(k)).ToList()) _friendRows.Remove(stale);
    }

    private void CancelFriendConfirms()
    {
        foreach (var row in _friendRows.Values) row.Mode = Models.FriendRowMode.Normal;
    }

    private static Models.FriendRow? RowOf(object sender)
        => (sender as FrameworkElement)?.Tag as Models.FriendRow;

    // ── Row actions ─────────────────────────────────────────────────────────

    private async void BtnFriendAccept_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) await AcceptAsync(row).ConfigureAwait(true);
    }

    private async Task AcceptAsync(Models.FriendRow row)
    {
        row.IsBusy = true;
        var ok = await SocialApi.AcceptFriendAsync(row.Friend.Id).ConfigureAwait(true);
        row.IsBusy = false;

        if (!ok) { ShowFailureToast(() => AcceptAsync(row)); return; }

        await LoadFriendsAsync().ConfigureAwait(true);
        ShowToast(string.Format(Str("FriendsAccepted"), row.DisplayName));
    }

    private async void BtnFriendDecline_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) await DeclineAsync(row).ConfigureAwait(true);
    }

    private async Task DeclineAsync(Models.FriendRow row)
    {
        row.IsBusy = true;
        var ok = await SocialApi.DeclineFriendAsync(row.Friend.Id).ConfigureAwait(true);
        row.IsBusy = false;

        if (!ok) { ShowFailureToast(() => DeclineAsync(row)); return; }

        await LoadFriendsAsync().ConfigureAwait(true);
        ShowToast(Str("FriendsDeclined"));
    }

    private async void BtnFriendWithdraw_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) await WithdrawAsync(row).ConfigureAwait(true);
    }

    private async Task WithdrawAsync(Models.FriendRow row)
    {
        row.IsBusy = true;
        var ok = await SocialApi.RemoveFriendAsync(row.Friend.Id).ConfigureAwait(true);
        row.IsBusy = false;

        if (!ok) { ShowFailureToast(() => WithdrawAsync(row)); return; }

        await LoadFriendsAsync().ConfigureAwait(true);
        ShowToast(Str("FriendsWithdrawn"));
    }

    /// <summary>Delete and the menu both start the question rather than the removal.</summary>
    private void BeginRemove(Models.FriendRow row)
    {
        if (!row.IsFriend) return;

        CancelFriendConfirms();
        row.Mode = Models.FriendRowMode.ConfirmRemove;
    }

    private void BeginBlock(Models.FriendRow row)
    {
        if (string.IsNullOrWhiteSpace(row.UserId)) return;

        CancelFriendConfirms();
        row.AlsoRemoveOnBlock = true;
        row.Mode = Models.FriendRowMode.ConfirmBlock;
    }

    private void MenuFriendRemove_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) BeginRemove(row);
    }

    private void MenuFriendBlock_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) BeginBlock(row);
    }

    private void BtnFriendConfirmCancel_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) row.Mode = Models.FriendRowMode.Normal;
    }

    private async void BtnFriendRemoveConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) await RemoveFriendAsync(row).ConfigureAwait(true);
    }

    private async Task RemoveFriendAsync(Models.FriendRow row)
    {
        row.IsBusy = true;
        var ok = await SocialApi.RemoveFriendAsync(row.Friend.Id).ConfigureAwait(true);
        row.IsBusy = false;
        row.Mode = Models.FriendRowMode.Normal;

        if (!ok) { ShowFailureToast(() => RemoveFriendAsync(row)); return; }

        await LoadFriendsAsync().ConfigureAwait(true);
        ShowToast(string.Format(Str("FriendsRemoved"), row.DisplayName));
    }

    private async void BtnFriendBlockConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;

        // A request has no friendship to keep: blocking settles it, whichever way it was going.
        await BlockPersonAsync(row, row.IsFriend ? row.AlsoRemoveOnBlock : true).ConfigureAwait(true);
    }

    /// <summary>
    /// Blocks somebody from their row. Ending the friendship first (when asked to) mirrors what
    /// blocking has always done here; if the block itself then fails the list is refreshed so it
    /// shows what is true, and Retry repeats only the part that did not happen.
    /// </summary>
    private async Task BlockPersonAsync(Models.FriendRow row, bool settleFirst)
    {
        row.IsBusy = true;

        try
        {
            if (settleFirst)
            {
                var settled = row.Kind == Models.FriendRowKind.Incoming
                    ? await SocialApi.DeclineFriendAsync(row.Friend.Id).ConfigureAwait(true)
                    : await SocialApi.RemoveFriendAsync(row.Friend.Id).ConfigureAwait(true);

                if (!settled)
                {
                    ShowFailureToast(() => BlockPersonAsync(row, true));
                    return;
                }
            }

            if (!await SocialApi.BlockAsync(row.UserId).ConfigureAwait(true))
            {
                await LoadFriendsAsync().ConfigureAwait(true);
                ShowFailureToast(() => BlockPersonAsync(row, false));
                return;
            }
        }
        finally
        {
            row.IsBusy = false;
            row.Mode = Models.FriendRowMode.Normal;
        }

        await LoadFriendsAsync().ConfigureAwait(true);
        await LoadInboxAsync().ConfigureAwait(true);

        ShowBlockedToast(row.UserId, row.DisplayName);
    }

    /// <summary>"Blocked X" with the way back. Used wherever somebody can be blocked in one click.</summary>
    private void ShowBlockedToast(string userId, string name)
        => ShowToast(string.Format(Str("FriendsBlockedToast"), name), false, Str("FriendsUndo"),
            () => UnblockByIdAsync(userId, name));

    private async Task UnblockByIdAsync(string userId, string name)
    {
        if (!await SocialApi.UnblockAsync(userId).ConfigureAwait(true))
        {
            ShowFailureToast(() => UnblockByIdAsync(userId, name));
            return;
        }

        await AfterBlockListChangedAsync().ConfigureAwait(true);
        ShowToast(string.Format(Str("FriendsUnblocked"), name));
    }

    /// <summary>Both directions come back at once: their lines in the room, their listing on the board.</summary>
    private async Task AfterBlockListChangedAsync()
    {
        if (BlocksSheet.Visibility == Visibility.Visible) await LoadBlocksAsync().ConfigureAwait(true);

        await ReloadChatAsync().ConfigureAwait(true);
        await LoadListingsAsync().ConfigureAwait(true);
    }

    /// <summary>Clicking a friend writes to them, which is the thing you want nine times in ten.</summary>
    private async void Friend_Click(object sender, MouseButtonEventArgs e)
    {
        if (RowOf(sender) is not { IsFriend: true, Mode: Models.FriendRowMode.Normal } row) return;

        (sender as UIElement)?.Focus();
        await MessageFriendAsync(row).ConfigureAwait(true);
    }

    private async void FriendRow_KeyDown(object sender, KeyEventArgs e)
    {
        if (RowOf(sender) is not { } row || e.OriginalSource is Button) return;

        if (row.Mode != Models.FriendRowMode.Normal)
        {
            if (e.Key == Key.Escape)
            {
                row.Mode = Models.FriendRowMode.Normal;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (row.Mode == Models.FriendRowMode.ConfirmRemove) await RemoveFriendAsync(row).ConfigureAwait(true);
                else await BlockPersonAsync(row, row.IsFriend ? row.AlsoRemoveOnBlock : true).ConfigureAwait(true);
            }

            return;
        }

        if (!row.IsFriend) return;

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await MessageFriendAsync(row).ConfigureAwait(true);
        }
        else if (e.Key == Key.Delete)
        {
            e.Handled = true;
            BeginRemove(row);
        }
    }

    private async void BtnFriendMessage_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) await MessageFriendAsync(row).ConfigureAwait(true);
    }

    private async void MenuFriendMessage_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) await MessageFriendAsync(row).ConfigureAwait(true);
    }

    private async Task MessageFriendAsync(Models.FriendRow row)
    {
        if (string.IsNullOrWhiteSpace(row.UserId)) return;

        HidePane(BlocksSheet);
        HidePane(FriendsSheet);

        // The same route a listing takes, so there is one way into a conversation rather than two
        // that drift apart.
        await OpenConversationWithAsync(new Models.LfgEntry
        {
            UserId = row.UserId,
            DisplayName = row.DisplayName,
            AvatarUrl = row.AvatarUrl,
            SteamId = row.Friend.SteamId,
        }).ConfigureAwait(true);
    }

    /// <summary>The ⋯ button opens the same menu a right click does.</summary>
    private void BtnFriendMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement button || RowOf(sender) is not { } row) return;

        for (DependencyObject? d = button; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is not FrameworkElement { ContextMenu: { } menu }) continue;

            menu.DataContext = row;
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
            return;
        }
    }

    private void MenuFriendProfile_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;

        // Opens over the list, so back returns to it.
        OpenProfileSheet(new Models.LfgEntry
        {
            UserId = row.UserId,
            DisplayName = row.DisplayName,
            AvatarUrl = row.AvatarUrl,
            SteamId = row.Friend.SteamId,
        });
    }

    private void MenuFriendReport_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row || string.IsNullOrWhiteSpace(row.UserId)) return;

        OpenReport(new ReportTarget(row.UserId, row.DisplayName, null, null));
    }

    // ── Add a friend ────────────────────────────────────────────────────────

    private bool _sendingFriendRequest;
    private CancellationTokenSource? _previewCts;

    private void BtnOpenAddFriend_Click(object sender, RoutedEventArgs e) => OpenAddFriendManual();

    private void OpenAddFriendManual()
    {
        ResetAddForm();
        ShowPane(AddFriendSheet);
        TxtFriendSteamId.Focus();
    }

    private void ResetAddForm()
    {
        ClearFriendTarget();

        TxtFriendSteamId.Text = "";
        TxtFriendMessage.Text = "";
        FriendPreviewCard.Visibility = Visibility.Collapsed;
        FriendsNoticeBox.Visibility = Visibility.Collapsed;
        FriendAddForm.Visibility = Visibility.Visible;
        FriendAddDone.Visibility = Visibility.Collapsed;
        BtnFriendAdd.Visibility = Visibility.Visible;

        RefreshFriendSuggestions();
        UpdateAddFriendState();
    }

    private void BtnAddFriendClose_Click(object sender, RoutedEventArgs e)
    {
        _previewCts?.Cancel();
        HidePane(AddFriendSheet);
    }

    private void BtnFriendAddAnother_Click(object sender, RoutedEventArgs e)
    {
        ResetAddForm();
        TxtFriendSteamId.Focus();
    }

    private void BtnFriendTargetClear_Click(object sender, RoutedEventArgs e)
    {
        ClearFriendTarget();
        UpdateAddFriendState();
        TxtFriendSteamId.Focus();
    }

    private void TxtFriendSteamId_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized || BtnFriendAdd is null) return;

        FriendsNoticeBox.Visibility = Visibility.Collapsed;
        UpdateAddFriendState();
        _ = UpdateFriendPreviewAsync(FriendInput.Parse(TxtFriendSteamId.Text).SteamId);
    }

    private void TxtFriendSteamId_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        e.Handled = true;
        TxtFriendMessage.Focus();
    }

    private void TxtFriendMessage_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized || BtnFriendAdd is null) return;

        UpdateAddFriendState();
    }

    /// <summary>Enter sends, Shift+Enter writes a new line.</summary>
    private void TxtFriendMessage_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;

        e.Handled = true;
        if (BtnFriendAdd.IsEnabled) BtnFriendAdd_Click(this, new RoutedEventArgs());
    }

    /// <summary>The id the request would go to right now, or null while the box does not hold one.</summary>
    private string? CurrentFriendSteamId()
        => _friendTargetSteamId ?? FriendInput.Parse(TxtFriendSteamId.Text).SteamId;

    /// <summary>The refusal that is already known without asking the server, or null.</summary>
    private static string? KnownRefusal(string steamId)
    {
        if (SocialFriends.IsSelf(steamId)) return "FriendsErrorSelf";
        if (SocialFriends.IsFriend(steamId)) return "FriendsErrorAlready";
        if (SocialFriends.HasPendingRequest(steamId)) return "FriendsErrorPending";
        return null;
    }

    /// <summary>
    /// What the box says about what is in it, and whether Send is allowed. Checked as it is typed
    /// so a wrong paste is explained where it happened instead of after the button is pressed.
    /// </summary>
    private void UpdateAddFriendState()
    {
        var hasTarget = _friendTargetSteamId is not null;

        FriendIdPanel.Visibility = Vis(!hasTarget);

        var message = TxtFriendMessage.Text?.Trim() ?? "";
        FriendMessageCount.Text = $"{(TxtFriendMessage.Text ?? "").Length}/{TxtFriendMessage.MaxLength}";

        var steamId = CurrentFriendSteamId();
        string? refusal = steamId is null ? null : KnownRefusal(steamId);

        if (!hasTarget)
        {
            var parsed = FriendInput.Parse(TxtFriendSteamId.Text);
            string text;
            string brush;

            if (refusal is not null) { text = Str(refusal); brush = "LfgDangerText"; }
            else switch (parsed.Kind)
            {
                case FriendInputKind.SteamId:
                    text = Str("FriendsAddValid"); brush = "StatusSuccess"; break;
                case FriendInputKind.IncompleteId:
                    text = string.Format(Str("FriendsAddDigits"), parsed.Digits); brush = "TextSecondary"; break;
                case FriendInputKind.VanityLink:
                    text = Str("FriendsAddVanity"); brush = "StatusWarning"; break;
                case FriendInputKind.Invalid:
                    text = Str("FriendsAddInvalid"); brush = "LfgDangerText"; break;
                default:
                    text = Str("FriendsAddHint"); brush = "TextSecondary"; break;
            }

            FriendInputHint.Text = text;
            FriendInputHint.Foreground = (Brush)FindResource(brush);
        }

        BtnFriendAdd.IsEnabled = steamId is not null && refusal is null && message.Length > 0 && !_sendingFriendRequest;
    }

    /// <summary>
    /// Shows who the id belongs to once it is a whole id, so a transposed digit is noticed before
    /// the request goes to a stranger. Best effort: no card is not an error.
    /// </summary>
    private async Task UpdateFriendPreviewAsync(string? steamId)
    {
        _previewCts?.Cancel();
        FriendPreviewCard.Visibility = Visibility.Collapsed;

        if (steamId is null) return;

        var cts = _previewCts = new CancellationTokenSource();

        try
        {
            await Task.Delay(400, cts.Token).ConfigureAwait(true);
            var profile = await Services.Social.SteamProfileService.GetAsync(steamId).ConfigureAwait(true);

            if (cts.IsCancellationRequested || string.IsNullOrWhiteSpace(profile?.PersonaName)) return;

            FriendPreviewName.Text = profile!.PersonaName;
            FriendPreviewAvatar.ImageSource = LoadAvatar(profile.AvatarUrl);
            FriendPreviewCard.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Steam not answering must not get in the way of adding somebody.
        }
    }

    private void ShowFriendNotice(string text)
    {
        FriendsNotice.Text = text;
        FriendsNoticeBox.Visibility = Visibility.Visible;
    }

    private async void BtnFriendAdd_Click(object sender, RoutedEventArgs e)
    {
        var steamId = CurrentFriendSteamId() ?? "";
        var message = TxtFriendMessage.Text?.Trim() ?? "";

        if (steamId.Length == 0 || message.Length == 0) return;

        if (KnownRefusal(steamId) is { } refusal)
        {
            ShowFriendNotice(Str(refusal));
            return;
        }

        _sendingFriendRequest = true;
        FriendsNoticeBox.Visibility = Visibility.Collapsed;
        UpdateAddFriendState();

        try
        {
            var result = await SocialApi.AddFriendAsync(steamId, message).ConfigureAwait(true);

            if (result == SocialApi.FriendRequestResult.Ok)
            {
                FriendAddForm.Visibility = Visibility.Collapsed;
                FriendAddDone.Visibility = Visibility.Visible;
                BtnFriendAdd.Visibility = Visibility.Collapsed;

                await LoadFriendsAsync().ConfigureAwait(true);
                return;
            }

            ShowFriendNotice(Str(result switch
            {
                SocialApi.FriendRequestResult.NotFound => "FriendsErrorNotFound",
                SocialApi.FriendRequestResult.Pending => "FriendsErrorPending",
                SocialApi.FriendRequestResult.AlreadyFriends => "FriendsErrorAlready",
                SocialApi.FriendRequestResult.Declined => "FriendsErrorDeclined",
                SocialApi.FriendRequestResult.Refused => "FriendsErrorRefused",
                _ => "FriendsErrorFailed",
            }));
        }
        finally
        {
            _sendingFriendRequest = false;
            UpdateAddFriendState();
        }
    }

    private void ChatAddFriend_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.ChatLine line) return;
        if (!SocialFriends.CanBeFriended(line.SteamId, line.SenderId)) return;
        _ = StartFriendRequestAsync(line.SteamId, line.SenderName, line.AvatarUrl);
    }

    /// <summary>
    /// Opens the add-friend pane for somebody, from outside this panel.
    ///
    /// The global chat lane in the team-chat window offers the same action, and a second friends
    /// pane built over there would be a second one to keep true. It opens this panel instead.
    /// </summary>
    public void StartFriendRequestFor(string? steamId, string? name = null, string? avatarUrl = null)
    {
        if (!SocialFriends.CanBeFriended(steamId)) return;
        ShowPublicRoom();
        _ = StartFriendRequestAsync(steamId, name, avatarUrl);
    }

    /// <summary>
    /// Opens the report pane for a line, from outside this panel.
    ///
    /// Reporting asks for a reason and optionally a note — the reporter's account of what happened
    /// is the part a moderator actually reads, and a one-click report that sends neither is worth
    /// less to them than no report at all.
    /// </summary>
    public void StartReportFor(Models.ChatLine line)
    {
        if (line.SenderId is null) return;

        ShowPublicRoom();
        OpenReport(new ReportTarget(line.SenderId, line.SenderName, line.Id, line.Body));
    }

    private void ThreadAddFriend_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.SocialThread thread) return;
        if (!SocialFriends.CanBeFriended(thread.CounterpartSteamId, thread.CounterpartId)) return;
        _ = StartFriendRequestAsync(thread.CounterpartSteamId, thread.CounterpartName, thread.AvatarUrl);
    }

    /// <summary>
    /// Opens the add pane already aimed at somebody, so the one message they get is all that is
    /// left to write. Somebody who has no Steam id on their account cannot be added, and the empty
    /// field says so more plainly than a refusal afterwards would.
    /// </summary>
    private async Task StartFriendRequestAsync(string? steamId, string? name = null, string? avatarUrl = null)
    {
        ResetAddForm();
        ShowPane(AddFriendSheet);

        await LoadFriendsAsync().ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(steamId) && KnownRefusal(steamId.Trim()) is { } refusal)
        {
            ClearFriendTarget();
            ShowFriendNotice(Str(refusal));
            return;
        }

        ShowFriendTarget(steamId, name, avatarUrl);
        UpdateAddFriendState();
        TxtFriendMessage.Focus();
    }

    /// <summary>
    /// Puts the person on the card and takes the id box off the screen.
    ///
    /// Without a name there is nothing to put on the card, and falling back to the id field is
    /// the only way such a request can still be sent — every caller here has a name, so that is
    /// a path for somebody we genuinely know nothing about rather than the ordinary case.
    /// </summary>
    private void ShowFriendTarget(string? steamId, string? name, string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(steamId) || string.IsNullOrWhiteSpace(name))
        {
            ClearFriendTarget();
            TxtFriendSteamId.Text = steamId ?? "";
            return;
        }

        _friendTargetSteamId = steamId.Trim();

        FriendTargetName.Text = name;
        FriendTargetAvatar.ImageSource = LoadAvatar(avatarUrl);
        FriendTargetCard.Visibility = Visibility.Visible;
        FriendIdPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>Back to the plain id field, with nothing of the last person left behind.</summary>
    private void ClearFriendTarget()
    {
        _friendTargetSteamId = null;

        FriendTargetCard.Visibility = Visibility.Collapsed;
        FriendTargetName.Text = "";
        FriendTargetAvatar.ImageSource = null;
        FriendIdPanel.Visibility = Visibility.Visible;
    }

    private sealed record FriendSuggestion(string SteamId, string Name, string? AvatarUrl, string Source);

    /// <summary>
    /// People already talked to who are not friends yet: private conversations first (somebody wrote
    /// to you on purpose), then whoever spoke recently in the room.
    /// </summary>
    private void RefreshFriendSuggestions()
    {
        var found = new List<FriendSuggestion>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Offer(string? steamId, string? userId, string? name, string? avatar, string sourceKey)
        {
            if (string.IsNullOrWhiteSpace(steamId) || string.IsNullOrWhiteSpace(name)) return;
            if (!SocialFriends.CanBeFriended(steamId, userId)) return;
            if (!seen.Add(steamId.Trim())) return;

            found.Add(new FriendSuggestion(steamId.Trim(), name, avatar, Str(sourceKey)));
        }

        foreach (var thread in (ThreadList.ItemsSource as IEnumerable<Models.SocialThread>) ?? Enumerable.Empty<Models.SocialThread>())
            Offer(thread.CounterpartSteamId, thread.CounterpartId, thread.CounterpartName, thread.AvatarUrl, "FriendsSuggestFromMessages");

        foreach (var line in Enumerable.Reverse(_chatLines))
        {
            if (line.IsMine) continue;
            Offer(line.SteamId, line.SenderId, line.SenderName, line.AvatarUrl, "FriendsSuggestFromRoom");
        }

        var top = found.Take(4).ToList();
        FriendSuggestionList.ItemsSource = top;
        FriendSuggestionsPanel.Visibility = Vis(top.Count > 0);
    }

    private void FriendSuggestion_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is FriendSuggestion s) PickSuggestion(s);
    }

    private void FriendSuggestion_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Space) return;
        if ((sender as FrameworkElement)?.Tag is not FriendSuggestion s) return;

        e.Handled = true;
        PickSuggestion(s);
    }

    private void PickSuggestion(FriendSuggestion s)
    {
        ShowFriendTarget(s.SteamId, s.Name, s.AvatarUrl);
        UpdateAddFriendState();
        TxtFriendMessage.Focus();
    }

    /// <summary>
    /// The avatar, or nothing. Frozen so it can be handed straight to the brush, and http only:
    /// a url from the server is still a url somebody else wrote.
    /// </summary>
    private static ImageSource? LoadAvatar(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.UriSource = uri;
            image.EndInit();
            if (image.CanFreeze) image.Freeze();

            return image;
        }
        catch
        {
            // A face that will not load is not a reason to lose the request.
            return null;
        }
    }

    // ── The block list ──────────────────────────────────────────────────────

    private List<Models.BlockedPlayer> _allBlocks = new();
    private bool _blocksLoading;
    private bool _blocksLoaded;
    private bool _blocksFailed;
    private int _blocksLoadSeq;

    private async void BtnBlocks_Click(object sender, RoutedEventArgs e)
    {
        TxtBlockSearch.Text = "";
        RenderBlocks();
        ShowPane(BlocksSheet);

        await LoadBlocksAsync().ConfigureAwait(true);
    }

    private void BtnBlocksClose_Click(object sender, RoutedEventArgs e) => HidePane(BlocksSheet);

    private void BtnBlocksRetry_Click(object sender, RoutedEventArgs e) => _ = LoadBlocksAsync();

    private void TxtBlockSearch_TextChanged(object sender, TextChangedEventArgs e) => RenderBlocks();

    private async Task LoadBlocksAsync()
    {
        var seq = ++_blocksLoadSeq;
        _blocksLoading = true;
        RenderBlocks();

        List<Models.BlockedPlayer> blocks;
        bool ok;
        try { (blocks, ok) = await SocialApi.TryGetBlocksAsync().ConfigureAwait(true); }
        catch { blocks = new(); ok = false; }

        if (seq != _blocksLoadSeq) return;

        _blocksLoading = false;
        _blocksFailed = !ok;

        if (ok)
        {
            _allBlocks = blocks;
            _blocksLoaded = true;
        }

        RenderBlocks();
    }

    private void RenderBlocks()
    {
        if (TxtBlockSearch is null) return;

        var query = TxtBlockSearch.Text;
        var shown = FriendsListLayout.OrderBlocks(_allBlocks, query);

        BlocksLoadingState.Visibility = Vis(_blocksLoading && !_blocksLoaded);
        BlocksErrorState.Visibility = Vis(_blocksFailed && !_blocksLoaded && !_blocksLoading);

        var lists = _blocksLoaded;
        var hasBlocks = _allBlocks.Count > 0;

        BlocksEmptyNotice.Visibility = Vis(lists && !hasBlocks);
        BlocksSearchEmptyNotice.Visibility = Vis(lists && hasBlocks && shown.Count == 0);
        BlocksListSection.Visibility = Vis(lists && shown.Count > 0);
        TxtBlockSearch.Visibility = Vis(lists && hasBlocks);

        BlockList.ItemsSource = shown;

        BlocksSummary.Text = string.Format(Str("BlocksCount"), _allBlocks.Count);
        BlocksSummary.Visibility = Vis(lists && hasBlocks);
    }

    private async void BtnUnblock_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Models.BlockedPlayer blocked) return;

        await UnblockAsync(blocked, sender as Control).ConfigureAwait(true);
    }

    private async Task UnblockAsync(Models.BlockedPlayer blocked, Control? button = null)
    {
        if (button is not null) button.IsEnabled = false;

        var ok = await SocialApi.UnblockAsync(blocked.UserId).ConfigureAwait(true);

        if (!ok)
        {
            if (button is not null) button.IsEnabled = true;
            ShowFailureToast(() => UnblockAsync(blocked));
            return;
        }

        // Undo is another block: the list is the source of truth, so nothing is held back here.
        await AfterBlockListChangedAsync().ConfigureAwait(true);

        ShowToast(string.Format(Str("FriendsUnblocked"), blocked.DisplayName), false, Str("FriendsUndo"),
            () => ReblockAsync(blocked));
    }

    private async Task ReblockAsync(Models.BlockedPlayer blocked)
    {
        if (!await SocialApi.BlockAsync(blocked.UserId).ConfigureAwait(true))
        {
            ShowFailureToast(() => ReblockAsync(blocked));
            return;
        }

        await AfterBlockListChangedAsync().ConfigureAwait(true);
        ShowToast(string.Format(Str("FriendsBlockedToast"), blocked.DisplayName));
    }
}
