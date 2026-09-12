using RustPlusDesk.Services.Social;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

/// <summary>
/// What you can do to a line in the room, from the lane.
///
/// These are the actions the Community panel already offers and the lane did not: answering a
/// message, reading one in your own language, and the two ways of dealing with somebody you would
/// rather not hear from. They need a message id and a sender account, which only a room line
/// carries — which is why every one of them is gated on <c>IsRoomLine</c> in the template rather
/// than being offered on a teammate's message and failing.
/// </summary>
public partial class MainWindow
{
    /// <summary>The message the next line will answer, or null when it answers nothing.</summary>
    private Models.ChatLine? _globalReplyTarget;

    /// <summary>Counts down the slow-mode wait, so the bar says how long rather than just refusing.</summary>
    private System.Windows.Threading.DispatcherTimer? _globalSlowModeTimer;

    private int _globalSlowModeRemaining;

    // ── Replying ────────────────────────────────────────────────────────────

    private void GlobalChatContext_Reply_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMessage(sender)?.SourceLine is not { } line) return;

        _globalReplyTarget = line;
        ShowGlobalReplyBar();
        TxtChatInput?.Focus();
    }

    private void GlobalChatCancelReply_Click(object sender, RoutedEventArgs e)
    {
        ClearGlobalReplyTarget();
        TxtChatInput?.Focus();
    }

    private void ShowGlobalReplyBar()
    {
        if (GlobalChatReplyBar == null || _globalReplyTarget is null) return;

        GlobalChatReplyAuthor.Text = _globalReplyTarget.SenderName;
        GlobalChatReplyExcerpt.Text = _globalReplyTarget.Body;
        GlobalChatReplyBar.Visibility = Visibility.Visible;
    }

    private void ClearGlobalReplyTarget()
    {
        _globalReplyTarget = null;
        if (GlobalChatReplyBar != null) GlobalChatReplyBar.Visibility = Visibility.Collapsed;
    }

    // ── Translating ─────────────────────────────────────────────────────────

    /// <summary>
    /// Shows the line in the reader's own language.
    ///
    /// A local change to one row rather than to the room: the feed keeps the original, so the
    /// translation lasts until the list is next rebuilt. Everyone else still sees what was
    /// actually written.
    /// </summary>
    private async void GlobalChatContext_Translate_Click(object sender, RoutedEventArgs e)
    {
        var vm = GetContextMessage(sender);
        if (vm?.SourceLine is not { } line) return;

        var result = await Services.TranslationService.TranslateAsync(line.Body).ConfigureAwait(true);

        if (!result.Ok)
        {
            ShowInfoSnackbar(
                Properties.Resources.SnackbarTitleChat,
                Helpers.Loc.TextOrNull("ChatTranslateFailed") ?? "Could not translate that message.",
                WpfUi.ControlAppearance.Caution);
            return;
        }

        if (result.Unchanged)
        {
            ShowInfoSnackbar(
                Properties.Resources.SnackbarTitleChat,
                Helpers.Loc.TextOrNull("ChatTranslateUnchanged") ?? "This message is already in your language.",
                WpfUi.ControlAppearance.Info);
            return;
        }

        vm.Text = result.Text;
    }

    // ── Reporting and blocking ──────────────────────────────────────────────

    /// <summary>
    /// Reports a line, through the Community panel's own report sheet.
    ///
    /// Reporting asks for a reason and optionally a note, and the reporter's account of what
    /// happened is the part a moderator actually reads. A one-click report that sends neither
    /// would be cheaper here and worth less to them, so this opens the sheet that asks.
    /// </summary>
    private void GlobalChatContext_Report_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMessage(sender)?.SourceLine is not { SenderId: not null } line) return;

        EnsureLfgWired();
        LfgPanel.Visibility = Visibility.Visible;
        LfgPanel.StartReportFor(line);
    }

    /// <summary>
    /// Offers a friend request, through the Community panel's friends sheet — the same sheet, with
    /// the id already filled in, so the one message they get is all that is left to write.
    /// </summary>
    private void GlobalChatContext_AddFriend_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMessage(sender)?.SourceLine is not { } line) return;
        if (string.IsNullOrWhiteSpace(line.SteamId)) return;

        EnsureLfgWired();
        LfgPanel.Visibility = Visibility.Visible;
        LfgPanel.StartFriendRequestFor(line.SteamId);
    }

    /// <summary>
    /// Blocks the sender, both directions, and drops their lines from the room now.
    /// </summary>
    private async void GlobalChatContext_Block_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMessage(sender)?.SourceLine is not { SenderId: not null } line) return;

        var ok = await SocialApi.BlockAsync(line.SenderId!).ConfigureAwait(true);
        if (!ok)
        {
            ShowInfoSnackbar(
                Properties.Resources.SnackbarTitleChat,
                Properties.Resources.GetString("ChatBlockFailed"),
                WpfUi.ControlAppearance.Caution);
            return;
        }

        // The server filters both directions from here on, so a re-read is what makes their
        // existing lines go rather than only their next one.
        await GlobalChatFeed.ReloadAsync(GlobalChatFeed.DefaultRoom).ConfigureAwait(true);

        ShowInfoSnackbar(
            Properties.Resources.SnackbarTitleChat,
            Properties.Resources.GetString("ChatBlockDone"),
            WpfUi.ControlAppearance.Success);
    }

    // ── Name colour ─────────────────────────────────────────────────────────

    /// <summary>One swatch in the picker.</summary>
    private sealed record GlobalNameColorChoice(string Key, string Label, System.Windows.Media.Brush Brush);

    /// <summary>The reader's own colour, so the dot shows what is currently set.</summary>
    private string? _globalNameColor;

    private bool _globalNameColorLoaded;

    /// <summary>
    /// Reads the colour the account already has, once, so the dot opens showing the current choice
    /// rather than the default until something is picked.
    /// </summary>
    private async Task LoadGlobalNameColorAsync()
    {
        if (_globalNameColorLoaded) return;
        _globalNameColorLoaded = true;

        var settings = await SocialApi.GetSettingsAsync().ConfigureAwait(true);
        _globalNameColor = settings?.NameColor;
        UpdateGlobalNameColorButton();
    }

    /// <summary>
    /// Shows the picker for supporters and hides it for everyone else.
    ///
    /// The platform refuses the write for a non-supporter, so offering the control would be
    /// offering something that only ever says no.
    /// </summary>
    private void UpdateGlobalNameColorButton()
    {
        if (BtnGlobalNameColor == null) return;

        var show = _activeChatChannel == ChatChannel.Global
            && Services.Auth.SupabaseAuthManager.IsPremium;

        BtnGlobalNameColor.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        BtnGlobalNameColor.Tag = Models.ChatLine.ResolveNameBrush(_globalNameColor)
            ?? new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x8A, 0x95, 0xA5));
    }

    private void BtnGlobalNameColor_Click(object sender, RoutedEventArgs e)
    {
        if (GlobalNameColorList == null || GlobalNameColorPopup == null) return;

        GlobalNameColorList.ItemsSource = Models.ChatLine.NameColorPalette
            .Select(entry =>
            {
                var brush = new System.Windows.Media.SolidColorBrush(entry.Color);
                brush.Freeze();
                return new GlobalNameColorChoice(
                    entry.Key,
                    Helpers.Loc.TextOrNull("ChatNameColor_" + entry.Key) ?? entry.Key,
                    brush);
            })
            .ToList();

        GlobalNameColorPopup.IsOpen = true;
    }

    private async void GlobalNameColorChoice_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key) return;

        GlobalNameColorPopup.IsOpen = false;

        if (!await SocialApi.SetNameColorAsync(key).ConfigureAwait(true)) return;

        _globalNameColor = key;
        UpdateGlobalNameColorButton();

        // Lines already drawn carry the old colour, and the server has restated every one of the
        // reader's own messages with the new one.
        await GlobalChatFeed.ReloadAsync(GlobalChatFeed.DefaultRoom).ConfigureAwait(true);
    }

    // ── Slow mode and sanctions ─────────────────────────────────────────────

    /// <summary>
    /// Puts the bars above the composer in step with what the room currently allows.
    ///
    /// Called on entering the lane and whenever the feed reports a change, so a timeout issued
    /// while the window is open closes the box there and then rather than at the next send.
    /// </summary>
    private void UpdateGlobalChatState()
    {
        if (_activeChatChannel != ChatChannel.Global)
        {
            // The bars belong to the room. Leaving them up over team chat would claim the Rust
            // server had timed the player out.
            if (GlobalChatSilencedBar != null) GlobalChatSilencedBar.Visibility = Visibility.Collapsed;
            if (GlobalChatSlowModeBar != null) GlobalChatSlowModeBar.Visibility = Visibility.Collapsed;
            if (GlobalChatReplyBar != null) GlobalChatReplyBar.Visibility = Visibility.Collapsed;
            return;
        }

        ApplyGlobalChatSanction(GlobalChatFeed.Sanction);

        var seconds = GlobalChatFeed.SlowModeSeconds(GlobalChatFeed.DefaultRoom);
        if (GlobalChatSlowModeBar != null && _globalSlowModeRemaining <= 0)
        {
            if (seconds > 0)
            {
                GlobalChatSlowModeText.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    Properties.Resources.GetString("ChatSlowModeActive"),
                    seconds);
                GlobalChatSlowModeBar.Visibility = Visibility.Visible;
            }
            else
            {
                GlobalChatSlowModeBar.Visibility = Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// Closes or reopens the box, saying until when and why.
    /// </summary>
    private void ApplyGlobalChatSanction(Models.ChatSanction? sanction)
    {
        if (GlobalChatSilencedBar == null) return;

        if (sanction is null)
        {
            GlobalChatSilencedBar.Visibility = Visibility.Collapsed;
            if (TxtChatInput != null) TxtChatInput.IsEnabled = true;
            if (BtnSendChat != null) BtnSendChat.IsEnabled = true;
            return;
        }

        GlobalChatSilencedText.Text = sanction.ExpiresAt is { } until
            ? string.Format(
                Properties.Resources.GetString("ChatSilencedTimeout"),
                until.ToLocalTime().ToString("g"),
                sanction.Reason)
            : string.Format(
                Properties.Resources.GetString("ChatSilencedBan"),
                sanction.Reason);

        GlobalChatSilencedBar.Visibility = Visibility.Visible;
        if (TxtChatInput != null) TxtChatInput.IsEnabled = false;
        if (BtnSendChat != null) BtnSendChat.IsEnabled = false;
    }

    /// <summary>
    /// Counts the slow-mode wait down in the bar, so the refusal becomes a number that is visibly
    /// going away rather than a sentence that is simply there.
    /// </summary>
    private void StartGlobalSlowModeCountdown(int seconds)
    {
        if (seconds <= 0 || GlobalChatSlowModeBar == null) return;

        _globalSlowModeRemaining = seconds;

        _globalSlowModeTimer ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };

        _globalSlowModeTimer.Tick -= GlobalSlowModeTick;
        _globalSlowModeTimer.Tick += GlobalSlowModeTick;

        RenderGlobalSlowModeCountdown();
        _globalSlowModeTimer.Start();
    }

    private void GlobalSlowModeTick(object? sender, EventArgs e)
    {
        _globalSlowModeRemaining--;

        if (_globalSlowModeRemaining <= 0)
        {
            _globalSlowModeTimer?.Stop();
            _globalSlowModeRemaining = 0;
            UpdateGlobalChatState();
            return;
        }

        RenderGlobalSlowModeCountdown();
    }

    private void RenderGlobalSlowModeCountdown()
    {
        if (GlobalChatSlowModeBar == null) return;

        GlobalChatSlowModeText.Text = string.Format(
            CultureInfo.CurrentCulture,
            Properties.Resources.GetString("ChatSlowModeWait"),
            _globalSlowModeRemaining);

        GlobalChatSlowModeBar.Visibility = Visibility.Visible;
    }
}
