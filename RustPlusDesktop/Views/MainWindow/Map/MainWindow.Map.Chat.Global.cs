using RustPlusDesk.Services.Social;
using System;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

/// <summary>
/// Global chat as a third lane in the window that already shows team and clan chat.
///
/// The reasoning is about supply rather than navigation. The other work makes the room reachable;
/// this puts it where there is already an audience. People watch team chat constantly while they
/// play, and the public room was nowhere near it — so the room inherits an existing habit instead
/// of asking for a new one, and the composer was already built to be shared.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Whether the lane can be used at all. Global chat needs a cloud account, but — unlike the
    /// other two lanes — no game server: it is reachable with nothing connected, which is most of
    /// the time somebody has this app open.
    /// </summary>
    private bool GlobalLaneAvailable =>
        _socialAvailable && Services.Cloud.CloudAuthManager.IsAuthenticated;

    /// <summary>
    /// Subscribes the lane to the feed, once.
    ///
    /// The feed is running whether or not this window is open, so this only decides when the lane
    /// redraws — not whether the room accrues.
    /// </summary>
    private void EnsureGlobalLaneWired()
    {
        if (_globalLaneWired) return;
        _globalLaneWired = true;

        GlobalChatFeed.RoomChanged += OnGlobalFeedRoomChanged;
        GlobalChatFeed.UnseenChanged += _ => UpdateUnreadBadges();

        // The header says how many people are in the room, so it has to follow them arriving and
        // leaving rather than showing the count from whenever the lane was opened.
        GlobalChatFeed.OnlineCountChanged += _ => UpdateGlobalChatContext();

        // A timeout issued while the window is open closes the box there and then, rather than at
        // the next send.
        GlobalChatFeed.SanctionChanged += sanction =>
        {
            if (_activeChatChannel == ChatChannel.Global) ApplyGlobalChatSanction(sanction);
        };
    }

    private void OnGlobalFeedRoomChanged(string room)
    {
        if (!string.Equals(room, GlobalChatFeed.DefaultRoom, StringComparison.OrdinalIgnoreCase)) return;

        UpdateUnreadBadges();

        if (_activeChatChannel != ChatChannel.Global) return;

        // Slow mode and sanctions travel on the same pushes as the lines.
        UpdateGlobalChatState();
        if (ChatContentBorder?.Visibility != Visibility.Visible) return;

        RebuildChatMessages();
        ScrollChatToBottom();
        GlobalChatFeed.MarkSeen(room);
    }

    /// <summary>
    /// Draws the room into the shared message list.
    ///
    /// Maps the platform's <see cref="Models.ChatLine"/> onto the same view model the in-game lanes
    /// use, so one list template draws all three. The two models do not carry the same facts — a
    /// Rust line has a Steam id and no supporter flag, a room line the reverse — and the mapping is
    /// where that is reconciled rather than in the template.
    /// </summary>
    private void RebuildGlobalChatMessages()
    {
        _lastGlobalChatDate = DateTime.MinValue;

        var lines = GlobalChatFeed.Lines(GlobalChatFeed.DefaultRoom);
        var toDisplay = lines
            .Skip(Math.Max(0, lines.Count - _globalDisplayedMessagesCount))
            .ToList();

        foreach (var line in toDisplay)
        {
            // A moderation notice is a card of its own in the Community panel. Here it would be a
            // message with no author, so it is skipped rather than drawn as a malformed line.
            if (line.IsSystemSanction) continue;

            var localTs = line.SentAt.HasValue
                ? (line.SentAt.Value.Kind == DateTimeKind.Utc ? line.SentAt.Value.ToLocalTime() : line.SentAt.Value)
                : DateTime.Now;

            // Deliberately no Steam id, even though the payload carries one.
            //
            // The public room shows a name and no more — that is the promise it makes, and the
            // Community panel keeps it by only ever disclosing a Steam account from an LFG
            // listing, where the player published it themselves. This lane borrows the team-chat
            // template, whose context menu offers Copy Steam ID, Open Steam Profile and Center on
            // Map; every one of those is gated on there being an id, so withholding it here is
            // what keeps a stranger in the room from being resolvable to their Steam account.
            //
            // The picture comes from the platform account for the same reason: asking Steam for an
            // avatar means holding the Steam id in order to ask.
            AddIncomingChatMessage(
                line.SenderName,
                line.Body,
                localTs,
                steamId: 0,
                autoScroll: false,
                forceSupporter: line.IsSupporter,
                forceIsMe: line.IsMine,
                avatarOverride: ResolveRoomAvatar(line.AvatarUrl),
                sourceLine: line);
        }

        ApplyGlobalChatGrouping();
        UpdateChatEmptyState();
    }

    /// <summary>
    /// Collapses the header on a run of messages from one person.
    ///
    /// The same rule the Community panel uses — same sender inside five minutes — so a short
    /// exchange reads as somebody talking rather than as a wall of repeated names and avatars. A
    /// day separator always starts a fresh group: a header under a date that is not the first thing
    /// below it looks like a mistake.
    /// </summary>
    private void ApplyGlobalChatGrouping()
    {
        for (int i = 0; i < ChatMessages.Count; i++)
        {
            var current = ChatMessages[i];

            if (i == 0 || current.ShowSeparator)
            {
                current.ShowHeader = true;
                continue;
            }

            var previous = ChatMessages[i - 1];

            var sameSender = current.SourceLine?.SenderId is { } id
                && string.Equals(id, previous.SourceLine?.SenderId, StringComparison.Ordinal);

            var closeInTime = Math.Abs((current.Timestamp - previous.Timestamp).TotalMinutes) < 5;

            current.ShowHeader = !(sameSender && closeInTime);
        }
    }

    /// <summary>
    /// Avatars already fetched, by URL.
    ///
    /// The list is rebuilt on every arrival, and without this each rebuild would start a fresh
    /// download for every visible line.
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<string, ImageSource> RoomAvatars =
        new(StringComparer.Ordinal);

    /// <summary>
    /// The platform's avatar for a room line, or null when it has none — in which case the
    /// template falls back to initials, as it already does for a teammate whose picture has not
    /// arrived.
    /// </summary>
    private static ImageSource? ResolveRoomAvatar(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        if (RoomAvatars.TryGetValue(url!, out var cached)) return cached;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        try
        {
            // Loaded on its own, off the UI thread, and frozen so the cache can be shared. A
            // failure leaves the line with initials rather than taking the list down with it.
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = uri;
            image.EndInit();
            if (image.CanFreeze) image.Freeze();

            RoomAvatars[url!] = image;
            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Opens the lane: makes sure the room has been read, draws it, and marks it seen.
    /// </summary>
    private async Task EnterGlobalLaneAsync()
    {
        EnsureGlobalLaneWired();

        // Chips are the server's chat commands. The room has none, and offering them would offer
        // to run a command somewhere that cannot run one.
        QuickCommandChips.Clear();

        UpdateGlobalChatContext();

        await GlobalChatFeed.EnsureLoadedAsync(GlobalChatFeed.DefaultRoom).ConfigureAwait(true);

        _globalDisplayedMessagesCount = Math.Max(20, _globalDisplayedMessagesCount);
        RebuildChatMessages();
        ScrollChatToBottom();
        GlobalChatFeed.MarkSeen(GlobalChatFeed.DefaultRoom);
        UpdateUnreadBadges();
        UpdateGlobalChatContext();
        UpdateGlobalChatState();

        // Re-applied after the read, not only before it: the room's length cap arrives with the
        // first snapshot, so setting the composer up front caps it at the in-game 128 and silently
        // swallows the rest of a message the room would have accepted.
        UpdateChatComposerForLane();
        UpdateGlobalNameColorButton();
        await LoadGlobalNameColorAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// The header line. For the in-game lanes it names the server; for the room it says how many
    /// people are in it, which is the equivalent fact and the one that decides whether a newcomer
    /// bothers typing.
    /// </summary>
    private void UpdateGlobalChatContext()
    {
        if (TxtChatServerContext == null) return;
        if (_activeChatChannel != ChatChannel.Global) return;

        var online = GlobalChatFeed.OnlineCount;
        TxtChatServerContext.Text = online > 0
            ? string.Format(Properties.Resources.GetString("GlobalChatTickerOnline"), online)
            : Properties.Resources.GetString("GlobalChatTitle");
    }

    /// <summary>
    /// Sends to the room.
    ///
    /// Separate from <c>SendChatReliableAsync</c> because nothing about that path applies: there is
    /// no server to confirm against, no echo to wait for, and the refusals are different — slow
    /// mode, a profanity warning, a mute. Each of those is said in the bar that already explains a
    /// failed send.
    /// </summary>
    private async Task<bool> SendGlobalChatAsync(string text)
    {
        var replyToId = _globalReplyTarget?.Id;

        var outcome = await SocialApi.PostChatAsync(text, replyToId: replyToId).ConfigureAwait(true);

        if (outcome.Result == ChatPostResult.Ok)
        {
            // The broadcast brings the line back with its server id and timestamp, and the feed
            // appends it. Echoing it locally first would show it twice.
            ClearGlobalReplyTarget();
            return true;
        }

        // Slow mode is a wait, not a failure, so the bar counts it down instead of the error box
        // stating it once.
        if (outcome.Result == ChatPostResult.SlowMode && outcome.RemainingSeconds > 0)
        {
            StartGlobalSlowModeCountdown(outcome.RemainingSeconds);
            return false;
        }

        if (ChatErrorBox != null && ChatErrorText != null)
        {
            ChatErrorBox.Visibility = Visibility.Visible;
            ChatErrorText.Text = DescribeGlobalChatRefusal(outcome);
        }

        // A refused line usually means the room's view of this account has changed — a fresh
        // timeout, slow mode switched on — so the bars are re-read rather than left stale.
        UpdateGlobalChatState();
        return false;
    }

    /// <summary>
    /// One sentence saying which refusal this was.
    ///
    /// The reasons are answered differently — read a notice, wait, or rewrite the message — and a
    /// single "could not send" leaves the user to guess which. The usual guess is that the app is
    /// broken. Deliberately the same strings the Community panel shows: the same refusal explained
    /// two ways is two things to keep true.
    /// </summary>
    private static string DescribeGlobalChatRefusal(ChatPostOutcome outcome)
    {
        var result = outcome.Result;

        // The one refusal that carries a count: which warning this is, and how many there are
        // before the room stops warning and starts timing out.
        if (result == ChatPostResult.ProfanityWarning)
        {
            var template = Properties.Resources.GetString("ChatRefusedProfanity");
            return outcome.WarningMax > 0
                ? string.Format(CultureInfo.CurrentCulture, template, outcome.WarningNumber, outcome.WarningMax)
                : template;
        }

        // Not an error but a wait, so it says how long.
        if (result == ChatPostResult.SlowMode)
        {
            var template = Properties.Resources.GetString("ChatSlowModeWait");
            return string.IsNullOrEmpty(template)
                ? Properties.Resources.MessageNotSentError
                : string.Format(CultureInfo.CurrentCulture, template, Math.Max(0, outcome.RemainingSeconds));
        }

        // The rules of the room have not been accepted. They are shown in the Community panel and
        // nowhere else, so this lane points at it rather than growing a second copy of them.
        if (result == ChatPostResult.ConsentRequired)
            return Properties.Resources.GetString("GlobalChatConsentNotice");

        return Properties.Resources.GetString(result switch
        {
            ChatPostResult.Sanctioned => "ChatRefusedSanctioned",
            ChatPostResult.LinkNotAllowed => "ChatRefusedLink",
            ChatPostResult.Duplicate => "ChatRefusedDuplicate",
            ChatPostResult.TooNew => "ChatRefusedTooNew",
            ChatPostResult.Empty => "ChatRefusedEmpty",
            _ => "ChatRefusedFailed",
        });
    }

    /// <summary>
    /// Puts the pills back in step with the active lane, without running the Checked handler again.
    ///
    /// Needed when a lane refuses to be entered: the radio button has already moved by the time we
    /// find out, and leaving it there would show Global as selected over the team's messages.
    /// </summary>
    private void SelectChatLanePill(ChatChannel channel)
    {
        var pill = channel switch
        {
            ChatChannel.Clan => TabClanChat,
            ChatChannel.Global => TabGlobalChat,
            _ => TabTeamChat,
        };

        if (pill == null || pill.IsChecked == true) return;

        // The handler returns early when the channel has not changed, so this cannot recurse.
        pill.IsChecked = true;
    }

    /// <summary>
    /// Points the composer at the lane it is about to write into.
    ///
    /// One box for three destinations means the box has to say which, or a line meant for the team
    /// goes to several hundred strangers.
    /// </summary>
    private void UpdateChatComposerForLane()
    {
        if (TxtChatInput == null) return;

        if (_activeChatChannel == ChatChannel.Global)
        {
            TxtChatInput.PlaceholderText = Properties.Resources.GetString("ChatPlaceholder");
            TxtChatInput.MaxLength = Math.Max(1, GlobalChatFeed.MaxLength(GlobalChatFeed.DefaultRoom));
            return;
        }

        // Leaving the room takes its bars, its pending reply and its colour picker with it.
        ClearGlobalReplyTarget();
        UpdateGlobalChatState();
        UpdateGlobalNameColorButton();

        TxtChatInput.PlaceholderText = Properties.Resources.GetString("ChatMessagePlaceholder");

        // Back to the in-game limit the box is declared with in markup. The room's cap is served
        // per room and is usually the same number, but must not be left applied to a Rust channel.
        TxtChatInput.MaxLength = InGameChatMaxLength;
    }

    /// <summary>The limit the composer carries for the two in-game lanes, as set in markup.</summary>
    private const int InGameChatMaxLength = 128;

    /// <summary>
    /// Explains why a lane cannot be entered, in the snackbar the chat window already uses for it.
    /// </summary>
    private void WarnGlobalLaneUnavailable()
    {
        ShowInfoSnackbar(
            Properties.Resources.SnackbarTitleChat,
            Properties.Resources.GetString("GlobalChatSignInNotice"),
            WpfUi.ControlAppearance.Info);
    }
}
