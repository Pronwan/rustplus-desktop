using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using RustPlusDesk.Helpers;
using RustPlusDesk.Services.Builder;
using RustPlusDesk.Services.Cloud;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

/// <summary>
/// Co-op Base Builder invites, answered wherever the player happens to be.
///
/// The popup lives in the app rather than in the builder because an invite rarely finds the
/// builder open: the player is on the map or the dashboard. Accepting opens the workshop and
/// leaves the session for the builder to pick up as a guest.
/// </summary>
public partial class MainWindow
{
    private int _buildInvitesAttached;

    /// <summary>Open invite popups by session, so one that ends or lapses can be closed.</summary>
    private readonly Dictionary<string, Controls.ToastItem> _buildInviteToasts = new();

    /// <summary>The session this account joined as a guest, for the builder to load.</summary>
    internal BuildSessionInfo? BuildGuestSession { get; set; }

    private void AttachBuildInvites()
    {
        if (Interlocked.Exchange(ref _buildInvitesAttached, 1) == 1) return;

        BuildSessionRealtime.InviteReceived += ShowBuildInvite;
        BuildSessionRealtime.InviteWithdrawn += CloseBuildInvite;
        BuildSessionRealtime.Kicked += OnBuildSessionKicked;
        BuildSessionRealtime.SessionEnded += OnBuildGuestSessionEnded;
        BuildSessionRealtime.EnsureStarted();

        _ = ShowMissedBuildInvitesAsync();
    }

    /// <summary>Invites sent while the app was closed or the socket was down.</summary>
    private async Task ShowMissedBuildInvitesAsync()
    {
        try
        {
            await CloudFeatureFlags.RefreshAsync();
            if (!BuildSessionApi.IsAvailable) return;

            var state = await BuildSessionApi.CurrentAsync();
            await Dispatcher.InvokeAsync(() =>
            {
                BuildGuestSession ??= state.Joined;
                foreach (var invite in state.Invites) ShowBuildInvite(invite);
            });
        }
        catch (Exception ex)
        {
            // Offline or signed out: a missed invite is two minutes from lapsing anyway.
            AppendLog($"[Build Together] Could not check for invites: {ex.Message}");
        }
    }

    private void ShowBuildInvite(BuildInvite invite)
    {
        if (_buildInviteToasts.ContainsKey(invite.SessionId)) return;

        // The popup lives exactly as long as the invite does. One without an expiry gets the
        // platform's two minutes rather than lingering as a button that can only fail.
        var remaining = (invite.ExpiresAt ?? DateTimeOffset.UtcNow.AddMinutes(2)) - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) return;

        var host = invite.Host?.DisplayName ?? "—";
        var message = string.IsNullOrWhiteSpace(invite.BaseName)
            ? string.Format(Loc.Text("BuildInviteMessage", "{0} invites you to build together in the Base Builder."), host)
            : string.Format(Loc.Text("BuildInviteMessageBase", "{0} invites you to build \"{1}\" together in the Base Builder."), host, invite.BaseName);

        var item = new Controls.ToastItem
        {
            Title = Loc.Text("BuildInviteTitle", "Build together?"),
            Icon = WpfUi.SymbolRegular.People24,
            AccentBrush = ToastAccentBrush(WpfUi.ControlAppearance.Info),
            MaxCardWidth = 460,
            Timeout = remaining,
        };
        item.Closed = () => _buildInviteToasts.Remove(invite.SessionId);

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });

        var buttons = new WrapPanel();
        var accept = new WpfUi.Button { Content = Loc.Text("BuildInviteAccept", "Join"), Appearance = WpfUi.ControlAppearance.Primary, Margin = new Thickness(0, 0, 8, 0) };
        var decline = new WpfUi.Button { Content = Loc.Text("BuildInviteDecline", "Decline"), Appearance = WpfUi.ControlAppearance.Secondary, Margin = new Thickness(0, 0, 8, 0) };
        var ignore = new WpfUi.Button
        {
            Content = Loc.Text("BuildInviteIgnore", "Ignore"),
            Appearance = WpfUi.ControlAppearance.Transparent,
            ToolTip = Loc.Text("BuildInviteIgnoreTooltip", "Decline and block this player, so they cannot invite you again. Undo it under Blocked players in the Community Hub."),
        };
        buttons.Children.Add(accept);
        buttons.Children.Add(decline);
        buttons.Children.Add(ignore);
        panel.Children.Add(buttons);
        item.Content = panel;

        void Busy(bool busy) => accept.IsEnabled = decline.IsEnabled = ignore.IsEnabled = !busy;

        accept.Click += async (_, _) =>
        {
            Busy(true);
            try
            {
                var session = await BuildSessionApi.AcceptAsync(invite.SessionId);
                DismissToast(item);
                BuildGuestSession = session;
                // The workshop may already be open, in which case it will not ask again.
                SyncBuildTogetherConnection();
                PushBuildTogetherState();
                ShowInfoSnackbar(
                    Loc.Text("BuildInviteJoinedTitle", "Joined"),
                    string.Format(Loc.Text("BuildInviteJoinedMessage", "You are watching {0} build. The host decides whether you may build too."), host),
                    WpfUi.ControlAppearance.Success, WpfUi.SymbolRegular.People24);
                MainTabs.SelectedItem = BaseBuilderTab;
            }
            catch (CloudApiException ex) when (ex.StatusCode is 404 or 410)
            {
                DismissToast(item);
                ShowInfoSnackbar(Loc.Text("BuildInviteTitle", "Build together?"),
                    Loc.Text("BuildInviteGone", "This invite has expired or the session has ended."), WpfUi.ControlAppearance.Caution);
            }
            catch (Exception ex)
            {
                AppendLog($"[Build Together] Could not join: {ex.Message}");
                Busy(false);
            }
        };

        decline.Click += async (_, _) => await AnswerBuildInviteAsync(item, () => BuildSessionApi.DeclineAsync(invite.SessionId));
        ignore.Click += async (_, _) => await AnswerBuildInviteAsync(item, () => BuildSessionApi.IgnoreAsync(invite.SessionId));

        _buildInviteToasts[invite.SessionId] = item;
        AddToast(item);
    }

    /// <summary>Decline and ignore close the popup whatever the platform says: the answer is no either way.</summary>
    private async Task AnswerBuildInviteAsync(Controls.ToastItem item, Func<Task> answer)
    {
        DismissToast(item);
        try { await answer(); }
        catch (Exception ex) { AppendLog($"[Build Together] Could not answer the invite: {ex.Message}"); }
    }

    private void CloseBuildInvite(string sessionId)
    {
        if (_buildInviteToasts.TryGetValue(sessionId, out var item)) DismissToast(item);
    }

    private void OnBuildSessionKicked(string sessionId)
    {
        if (BuildGuestSession?.Id != sessionId) return;

        BuildGuestSession = null;
        SyncBuildTogetherConnection();
        PushBuildTogetherState();
        ShowInfoSnackbar(Loc.Text("BuildSessionLeftTitle", "Build session"),
            Loc.Text("BuildSessionKicked", "The host removed you from the build session."), WpfUi.ControlAppearance.Caution);
    }

    private void OnBuildGuestSessionEnded(string sessionId, string? reason)
    {
        if (BuildGuestSession?.Id != sessionId) return;

        BuildGuestSession = null;
        SyncBuildTogetherConnection();
        PushBuildTogetherState();
        ShowInfoSnackbar(Loc.Text("BuildSessionLeftTitle", "Build session"),
            Loc.Text("BuildSessionEnded", "The host ended the build session."), WpfUi.ControlAppearance.Info);
    }
}
