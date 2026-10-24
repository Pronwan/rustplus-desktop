using RustPlusDesk.Services.Data;
using RustPlusDesk.Services.Social;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RustPlusDesk.Controls.Chat;

/// <summary>Whether the user has dismissed the strip, remembered across runs.</summary>
public sealed record GlobalChatTickerState(bool Hidden);

/// <summary>
/// The room, one line at a time, along the bottom of the window.
///
/// Chat was four levels down: an unlabelled rail icon, a badge that could not light for it, a
/// takeover panel, and a section inside Community. Every level was a reason not to go, and fixing
/// them one at a time still leaves chat somewhere you have to decide to go to. This does not fix
/// the descent — it removes the need to make it.
/// </summary>
public partial class GlobalChatTicker : UserControl
{
    private const string StateKey = "global_chat_ticker";

    /// <summary>The strip's own height, and therefore how far a line travels on the roll.</summary>
    private const double LineHeight = 30;

    private static readonly Duration RollDuration = new(TimeSpan.FromMilliseconds(260));

    /// <summary>Amber for "somebody is talking to you", matching the mention highlight in the room.</summary>
    private static readonly Brush MentionPillBrush = Frozen(0xF5, 0xB1, 0x3D);

    /// <summary>The ordinary traffic colour: present, but not asking for anything.</summary>
    private static readonly Brush AmbientPillBrush = Frozen(0x3F, 0xD7, 0xFF);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>Asks the host to open the room. Raised by a click anywhere on the strip.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>Raised after the user hides the strip, so the host can drop it from the layout.</summary>
    public event EventHandler? HideRequested;

    private bool _wired;

    /// <summary>The slot currently at rest. The other one is the one that rolls in.</summary>
    private GlobalChatTickerLine _front = null!;

    private GlobalChatTickerLine _back = null!;

    /// <summary>The line on screen, so an unrelated redraw does not re-run the animation.</summary>
    private string? _shownId;

    /// <summary>The slot currently rolling out, or null when nothing is in flight.</summary>
    private GlobalChatTickerLine? _rolling;

    public GlobalChatTicker()
    {
        InitializeComponent();

        _front = SlotA;
        _back = SlotB;

        Loaded += (_, __) =>
        {
            Wire();
            Render();
        };

        Unloaded += (_, __) => Unwire();
    }

    /// <summary>Whether the user has hidden the strip for good.</summary>
    public static bool IsHidden =>
        DataManager.LoadCache<GlobalChatTickerState>(StateKey)?.Hidden == true;

    /// <summary>Hides or restores the strip. The settings checkbox and the chevron both land here.</summary>
    public static void SetHidden(bool hidden) =>
        DataManager.SaveCache(StateKey, new GlobalChatTickerState(hidden));

    private void Wire()
    {
        if (_wired) return;
        _wired = true;

        GlobalChatFeed.RoomChanged += OnRoomChanged;
        GlobalChatFeed.UnseenChanged += OnRoomChanged;
        GlobalChatFeed.OnlineCountChanged += OnOnlineCountChanged;
    }

    private void Unwire()
    {
        if (!_wired) return;
        _wired = false;

        GlobalChatFeed.RoomChanged -= OnRoomChanged;
        GlobalChatFeed.UnseenChanged -= OnRoomChanged;
        GlobalChatFeed.OnlineCountChanged -= OnOnlineCountChanged;
    }

    private void OnRoomChanged(string room)
    {
        // The strip speaks for the public room only. The supporters' room is not somewhere to
        // advertise to accounts that cannot write in it.
        if (!string.Equals(room, GlobalChatFeed.DefaultRoom, StringComparison.OrdinalIgnoreCase)) return;
        Render();
    }

    private void OnOnlineCountChanged(int count) => Render();

    /// <summary>
    /// Draws whatever the room can currently offer, in descending order of what actually persuades:
    /// somebody talking, then how many people are here, then what the room is.
    /// </summary>
    public void Render()
    {
        var room = GlobalChatFeed.DefaultRoom;
        var latest = GlobalChatFeed.Latest(room);

        var online = GlobalChatFeed.OnlineCount;
        if (online > 0)
        {
            OnlineText.Text = string.Format(
                Properties.Resources.GetString("GlobalChatTickerOnline"), online);
            OnlinePill.Visibility = Visibility.Visible;
        }
        else
        {
            OnlinePill.Visibility = Visibility.Collapsed;
        }

        if (latest is not null && !latest.IsSystemSanction)
        {
            ShowLine(latest.Id, slot => slot.SetLine(latest));
        }
        else
        {
            // Never blank. With nobody talking the strip still says where it goes — the alternative
            // is an empty bar that reads as a broken one.
            var noticeKey = online > 0 ? "GlobalChatTickerQuiet" : "GlobalChatTickerIdle";
            ShowLine("notice:" + noticeKey, slot => slot.SetNotice(Properties.Resources.GetString(noticeKey)));
        }

        // Mentions are counted apart from the rest and shown in place of them.
        //
        // A raw count on a global room reads 247 forever, and a number that is always high is a
        // number nobody sees. "3 people mentioned you" is a reason to look; "247 messages" is
        // wallpaper — so when there are mentions the pill speaks for those alone.
        var mentions = GlobalChatFeed.UnseenMentions(room);
        var unseen = GlobalChatFeed.Unseen(room);

        if (mentions > 0)
        {
            UnseenPill.Visibility = Visibility.Visible;
            UnseenPill.Background = MentionPillBrush;
            UnseenText.Text = "@" + (mentions > 99 ? "99+" : mentions.ToString());
            return;
        }

        UnseenPill.Background = AmbientPillBrush;
        UnseenPill.Visibility = unseen > 0 ? Visibility.Visible : Visibility.Collapsed;
        UnseenText.Text = unseen > 99 ? "99+" : unseen.ToString();
    }

    /// <summary>
    /// Puts content on the strip, rolling the previous line out of the way if there was one.
    ///
    /// Keyed on an id so the occupant count moving — which redraws the strip several times a
    /// minute — does not re-animate a line that has not changed.
    /// </summary>
    private void ShowLine(string id, Action<GlobalChatTickerLine> fill)
    {
        if (string.Equals(_shownId, id, StringComparison.Ordinal)) return;

        var first = _shownId is null;
        _shownId = id;

        // The first line is not an arrival — it is what was already there when the strip appeared.
        // Rolling it in would animate the app's own start-up.
        if (first || Services.TrackingService.ReduceUiEffects)
        {
            SettleRoll();
            fill(_front);
            _front.SlideOffset = 0;
            _front.Visibility = Visibility.Visible;
            _back.Visibility = Visibility.Hidden;
            return;
        }

        // A line arriving mid-roll cancels the one in flight and settles it, so the strip never
        // shows two half-slid lines at once.
        SettleRoll();

        var departed = _front;
        var arrived = _back;

        fill(arrived);
        arrived.SlideOffset = -LineHeight;
        arrived.Visibility = Visibility.Visible;

        // Swap roles now: the line rolling in is the one that will be rolled out next.
        _front = arrived;
        _back = departed;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // FillBehavior.Stop so the animation releases the property when it ends; the resting value
        // is set in Completed. Holding it instead would leave the next roll starting from wherever
        // this one finished.
        var outgoing = new DoubleAnimation(0, LineHeight, RollDuration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop,
        };

        var incoming = new DoubleAnimation(-LineHeight, 0, RollDuration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop,
        };

        incoming.Completed += (_, __) =>
        {
            arrived.SlideOffset = 0;
            departed.SlideOffset = 0;
            departed.Visibility = Visibility.Hidden;
            _rolling = null;
        };

        _rolling = departed;

        departed.Slide.BeginAnimation(TranslateTransform.YProperty, outgoing);
        arrived.Slide.BeginAnimation(TranslateTransform.YProperty, incoming);
    }

    /// <summary>
    /// Ends a roll in progress and leaves both slots where they would have finished.
    /// </summary>
    private void SettleRoll()
    {
        SlotA.Slide.BeginAnimation(TranslateTransform.YProperty, null);
        SlotB.Slide.BeginAnimation(TranslateTransform.YProperty, null);

        _front.SlideOffset = 0;
        _back.SlideOffset = 0;

        if (_rolling is not null)
        {
            _rolling.Visibility = Visibility.Hidden;
            _rolling = null;
        }
    }

    private void Strip_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BtnHide_Click(object sender, RoutedEventArgs e)
    {
        // Hiding the strip is not leaving the room: the feed keeps holding it, and the rail still
        // carries what arrived. This is one surface being turned off, not the feature — and it can
        // be turned back on from Settings, next to the cloud account the room runs on.
        SetHidden(true);
        Visibility = Visibility.Collapsed;
        HideRequested?.Invoke(this, EventArgs.Empty);
    }
}
