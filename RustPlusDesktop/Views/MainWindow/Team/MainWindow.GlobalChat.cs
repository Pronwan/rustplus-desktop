using RustPlusDesk.Services.Social;
using System;
using System.Windows;

namespace RustPlusDesk.Views;

/// <summary>
/// The global chat ticker, and the rules about when it is allowed on screen.
///
/// Kept apart from the Community panel's wiring because it answers a different question. The panel
/// is where you go to be in the room; the strip is the reason you would ever think to.
/// </summary>
public partial class MainWindow
{
    private bool _globalChatTickerWired;

    /// <summary>
    /// Whether the social layer is open to this account. The strip follows the rail: if the rail
    /// button is hidden there is no room to advertise.
    /// </summary>
    private bool _socialAvailable;

    /// <summary>
    /// Re-applies the strip's visibility. For Settings, which can turn it back on after the
    /// chevron turned it off.
    /// </summary>
    public void RefreshGlobalChatTicker() => ApplyGlobalChatTickerVisibility();

    private void EnsureGlobalChatTickerWired()
    {
        if (_globalChatTickerWired) return;
        _globalChatTickerWired = true;

        GlobalChatTickerStrip.OpenRequested += (_, __) => OpenGlobalChatRoom();
        GlobalChatTickerStrip.HideRequested += (_, __) => ApplyGlobalChatTickerVisibility();

        // Being spoken to is the one thing in a global room worth interrupting somebody for, so it
        // is the one thing that gets a toast. Everything else in the room is ambient and stays on
        // the strip. Wired here rather than in the chat window, because the whole point is that it
        // reaches you when the chat window is not open.
        GlobalChatFeed.MentionArrived += OnGlobalChatMentioned;
    }

    /// <summary>
    /// Announces a mention, wherever the user happens to be in the app.
    ///
    /// Suppressed while the room is already on screen — a toast telling you about a line you are
    /// looking at is noise, and the room marks itself seen as the line lands.
    /// </summary>
    private void OnGlobalChatMentioned(Models.ChatLine line)
    {
        var lookingAtIt = _activeChatChannel == ChatChannel.Global
            && ChatContentBorder?.Visibility == Visibility.Visible;

        if (lookingAtIt) return;

        // Also suppressed while the Community panel is showing the same room.
        if (LfgPanel?.Visibility == Visibility.Visible) return;

        ShowInfoSnackbar(
            string.Format(
                Properties.Resources.GetString("GlobalChatMentionToastTitle"),
                line.SenderName),
            line.Body,
            Wpf.Ui.Controls.ControlAppearance.Info);
    }

    /// <summary>
    /// Opens the room from the strip.
    ///
    /// Goes to the room rather than to Community's front door: a strip that shows a line and then
    /// lands you one level above it has spent its persuasion and then asked for another decision.
    /// </summary>
    private void OpenGlobalChatRoom()
    {
        EnsureLfgWired();

        LfgPanel.Refresh();
        LfgPanel.Visibility = Visibility.Visible;
        LfgPanel.ShowPublicRoom();
    }

    /// <summary>
    /// Shows or hides the strip.
    ///
    /// Three things have to hold: the layer is open to this account, the user has not dismissed
    /// the strip, and there is an account at all — signed out there is nothing to show and nothing
    /// to send.
    /// </summary>
    private void ApplyGlobalChatTickerVisibility()
    {
        EnsureGlobalChatTickerWired();

        var show = _socialAvailable
            && Services.Cloud.CloudAuthManager.IsAuthenticated
            && !Controls.Chat.GlobalChatTicker.IsHidden;

        GlobalChatTickerStrip.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        if (!show) return;

        GlobalChatTickerStrip.Render();

        // The strip is the first thing that will want lines, and it wants them before anybody has
        // opened a panel. This is the read that makes the room start accruing.
        _ = GlobalChatFeed.EnsureLoadedAsync(GlobalChatFeed.DefaultRoom);
    }
}
