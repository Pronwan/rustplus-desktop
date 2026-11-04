using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;
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
        var message = (invite.IsShip, string.IsNullOrWhiteSpace(invite.BaseName)) switch
        {
            (false, true) => string.Format(Loc.Text("BuildInviteMessage", "{0} invites you to build together in the Base Builder."), host),
            (false, false) => string.Format(Loc.Text("BuildInviteMessageBase", "{0} invites you to build \"{1}\" together in the Base Builder."), host, invite.BaseName),
            (true, true) => string.Format(Loc.Text("BuildInviteMessageShip", "{0} invites you to build a ship together in the Ship Builder."), host),
            (true, false) => string.Format(Loc.Text("BuildInviteMessageShipNamed", "{0} invites you to build \"{1}\" together in the Ship Builder."), host, invite.BaseName),
        };

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
            // Joining replaces whatever the workshop shows. Someone building their own base, or hosting, is asked first.
            var own = await OwnWorkshopBaseAsync();
            if (_buildHostSession != null || own is { Pieces: > 0 })
            {
                AskToLeaveOwnBuild(invite, item, host, own?.Name);
                Busy(false);
                return;
            }
            if (!await JoinBuildInviteAsync(invite, item, host)) Busy(false);
        };

        decline.Click += async (_, _) => await AnswerBuildInviteAsync(item, () => BuildSessionApi.DeclineAsync(invite.SessionId));
        ignore.Click += async (_, _) => await AnswerBuildInviteAsync(item, () => BuildSessionApi.IgnoreAsync(invite.SessionId));

        _buildInviteToasts[invite.SessionId] = item;
        AddToast(item);
    }

    /// <summary>Accepts the invite and opens the workshop on the host's base. False when it failed and can be tried again.</summary>
    private async Task<bool> JoinBuildInviteAsync(BuildInvite invite, Controls.ToastItem item, string host)
    {
        try
        {
            // One session at a time: a guest somewhere else walks out of it first.
            if (BuildGuestSession is { } previous && previous.Id != invite.SessionId)
            {
                BuildGuestSession = null;
                try { await BuildSessionApi.LeaveAsync(previous.Id); }
                catch (Exception ex) { AppendLog($"[Build Together] Could not leave the previous session: {ex.Message}"); }
            }

            var session = await BuildSessionApi.AcceptAsync(invite.SessionId);
            DismissToast(item);
            BuildGuestSession = session;
            _buildGuestEnded = false;
            // The workshop may already be open, in which case it will not ask again.
            SyncBuildTogetherConnection();
            PushBuildTogetherState();
            ShowInfoSnackbar(
                Loc.Text("BuildInviteJoinedTitle", "Joined"),
                string.Format(Loc.Text("BuildInviteJoinedMessage", "You are watching {0} build. The host decides whether you may build too."), host),
                WpfUi.ControlAppearance.Success, WpfUi.SymbolRegular.People24);
            // The session's own builder. The other one, if it is open, saves its draft and makes room (CloseBuilderForOtherWorkspaceAsync).
            MainTabs.SelectedItem = BuilderTabOf(session?.IsShip ?? invite.IsShip ? ShipBuilderWorkspace : BaseBuilderWorkspace);
            return true;
        }
        catch (CloudApiException ex) when (ex.StatusCode is 404 or 410)
        {
            DismissToast(item);
            ShowInfoSnackbar(Loc.Text("BuildInviteTitle", "Build together?"),
                Loc.Text("BuildInviteGone", "This invite has expired or the session has ended."), WpfUi.ControlAppearance.Caution);
            return true;
        }
        catch (Exception ex)
        {
            AppendLog($"[Build Together] Could not join: {ex.Message}");
            return false;
        }
    }

    /// <summary>The base open in the workshop, when it is the player's own rather than a shared copy.</summary>
    private async Task<(string Name, int Pieces)?> OwnWorkshopBaseAsync()
    {
        var core = _baseBuilderWebView?.CoreWebView2;
        if (core == null) return null;
        try
        {
            var json = await core.ExecuteScriptAsync("window.workshopOwnBase ? window.workshopOwnBase() : null").WaitAsync(TimeSpan.FromSeconds(3));
            if (JToken.Parse(json) is not JObject own) return null;
            return (own["name"]?.ToString() ?? "", own["pieces"]?.Value<int>() ?? 0);
        }
        catch (Exception ex)
        {
            AppendLog($"[Build Together] Could not ask the workshop what is open: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The player is building a base of their own, or hosting: keep it in their library, end their own session, then join.
    /// A toast rather than a dialog, so it shows inside the builder too (MainWindow.BuilderToasts).
    /// </summary>
    private void AskToLeaveOwnBuild(BuildInvite invite, Controls.ToastItem inviteToast, string host, string? baseName)
    {
        var hosting = _buildHostSession != null;
        var name = !string.IsNullOrWhiteSpace(baseName) ? baseName
            : _buildHostSession?.BaseName ?? Loc.Text("BuildInviteSwitchYourBase", "your base");
        var message = hosting
            ? string.Format(Loc.Text("BuildInviteSwitchHosting", "You are hosting a build session. \"{0}\" is saved to your library and your session ends for your guests, then you join {1}."), name, host)
            : string.Format(Loc.Text("BuildInviteSwitchBuilding", "\"{0}\" is saved to your library, then you join {1}. You can open it again from there."), name, host);

        var remaining = (invite.ExpiresAt ?? DateTimeOffset.UtcNow.AddMinutes(2)) - DateTimeOffset.UtcNow;
        var item = new Controls.ToastItem
        {
            Title = string.Format(Loc.Text("BuildInviteSwitchTitle", "Join {0}'s build?"), host),
            Icon = WpfUi.SymbolRegular.People24,
            AccentBrush = ToastAccentBrush(WpfUi.ControlAppearance.Caution),
            MaxCardWidth = 460,
            Timeout = remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(30),
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var buttons = new WrapPanel();
        var confirm = new WpfUi.Button { Content = Loc.Text("BuildInviteSwitchConfirm", "Save and join"), Appearance = WpfUi.ControlAppearance.Primary, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new WpfUi.Button { Content = Loc.Text("BuildInviteSwitchCancel", "Keep building"), Appearance = WpfUi.ControlAppearance.Secondary };
        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        item.Content = panel;

        confirm.Click += async (_, _) =>
        {
            confirm.IsEnabled = cancel.IsEnabled = false;
            // "unsupported" is a viewer without the call; opening the host's base still saves the old one to the library.
            var saved = await AskBaseBuilderAsync("saveWorkshopOwnBase", TimeSpan.FromSeconds(20));
            DismissToast(item);
            if (saved is "failed" or "unanswered")
            {
                ShowInfoSnackbar(Loc.Text("BuildInviteTitle", "Build together?"),
                    Loc.Text("BuildInviteSaveFailed", "Your base could not be saved, so you stay with it. Export it to a file, then accept the invite again."),
                    WpfUi.ControlAppearance.Danger);
                return;
            }

            if (_buildHostSession is { } mine)
            {
                _buildHostSession = null;
                try { await BuildSessionApi.EndAsync(mine.Id); }
                catch (Exception ex) { AppendLog($"[Build Together] Could not end the own session before joining: {ex.Message}"); }
                SyncBuildTogetherConnection();
            }
            await JoinBuildInviteAsync(invite, inviteToast, host);
        };
        cancel.Click += (_, _) => DismissToast(item);

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
        _buildGuestEnded = true;
        SyncBuildTogetherConnection();
        PushBuildTogetherState();
        ShowInfoSnackbar(Loc.Text("BuildSessionLeftTitle", "Build session"),
            Loc.Text("BuildSessionKicked", "The host removed you from the build session."), WpfUi.ControlAppearance.Caution);
    }

    private void OnBuildGuestSessionEnded(string sessionId, string? reason)
    {
        if (BuildGuestSession?.Id != sessionId) return;

        BuildGuestSession = null;
        // The base stays open; the panel offers to keep a copy of it.
        _buildGuestEnded = true;
        SyncBuildTogetherConnection();
        PushBuildTogetherState();
        ShowInfoSnackbar(Loc.Text("BuildSessionLeftTitle", "Build session"),
            Loc.Text("BuildSessionEnded", "The host ended the build session."), WpfUi.ControlAppearance.Info);
    }
}
