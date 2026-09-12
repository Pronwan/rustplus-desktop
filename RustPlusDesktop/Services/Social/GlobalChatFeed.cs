using RustPlusDesk.Services.Cloud;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace RustPlusDesk.Services.Social;

/// <summary>
/// The rooms, as a fact about the account rather than about whichever panel happens to be open.
///
/// The socket was always up from start-up — <see cref="SocialUnread"/> brings it up, not the
/// Community panel — and every line in the room already arrived at every running client. But the
/// only subscriber was the panel, attaching on open and detaching on close, so with it shut the
/// lines landed on a live connection and were thrown away. The room could not accrue while nobody
/// was looking at it, which is the reason no badge, ticker or dock could have worked: there was
/// never anything to have missed.
///
/// So the subscription lives here instead, for as long as the app runs, and the panel becomes a
/// view of this buffer rather than the thing that owns the connection.
/// </summary>
public static class GlobalChatFeed
{
    /// <summary>How many lines each room keeps. The same window the panel used to hold itself.</summary>
    public const int Window = 200;

    /// <summary>The room the ticker and the rail speak for when nobody has said which.</summary>
    public const string DefaultRoom = ChatRooms.Public;

    /// <summary>
    /// The backstop for a socket that is not delivering. Long, because it is a backstop and not
    /// the mechanism — and it only reads when the room has actually gone quiet, so a busy evening
    /// costs nothing.
    /// </summary>
    private static readonly TimeSpan QuietCatchUpInterval = TimeSpan.FromMinutes(3);

    /// <summary>A room's lines, and what the last read said about it.</summary>
    private sealed class RoomState
    {
        public readonly List<Models.ChatLine> Lines = new();
        public int SlowModeSeconds;
        public int MaxLength = 128;
        public bool Loaded;
        public int Unseen;

        /// <summary>
        /// Unseen lines that address this account, counted apart from the rest.
        ///
        /// A global room produces a raw unread count that reads 247 forever, and a number that is
        /// always high is a number nobody looks at. Being spoken to is the one event in the room
        /// that is reliably worth interrupting somebody for, so it gets its own count.
        /// </summary>
        public int UnseenMentions;

        public DateTime LastArrivalUtc = DateTime.MinValue;
    }

    private static readonly object Gate = new();

    private static readonly Dictionary<string, RoomState> Rooms =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [ChatRooms.Public] = new RoomState(),
            [ChatRooms.Supporter] = new RoomState(),
        };

    private static bool _started;
    private static DispatcherTimer? _timer;

    /// <summary>Raised with the room whose lines changed — appended to, trimmed or reloaded.</summary>
    public static event Action<string>? RoomChanged;

    /// <summary>
    /// Raised once per genuinely new line, after it has been buffered.
    ///
    /// Separate from <see cref="RoomChanged"/> because a strip that shows the newest line wants
    /// the line, while a list that redraws itself only wants to know something moved.
    /// </summary>
    public static event Action<Models.ChatLine>? LineArrived;

    /// <summary>Raised with the room whose unseen count changed.</summary>
    public static event Action<string>? UnseenChanged;

    /// <summary>
    /// Raised once per arriving line that addresses this account.
    ///
    /// Separate from <see cref="LineArrived"/> because being spoken to is the one thing in a
    /// global room worth interrupting somebody for, and everything else is not.
    /// </summary>
    public static event Action<Models.ChatLine>? MentionArrived;

    /// <summary>Raised with the public room's occupant count whenever it moves.</summary>
    public static event Action<int>? OnlineCountChanged;

    /// <summary>Why the box is closed, if it is. An account-wide fact, not a per-room one.</summary>
    public static Models.ChatSanction? Sanction { get; private set; }

    /// <summary>Raised when the account's chat sanction is applied or lifted.</summary>
    public static event Action<Models.ChatSanction?>? SanctionChanged;

    /// <summary>Whether the supporters' room is open to this account, per the last read.</summary>
    public static bool SupporterRoomOpen { get; private set; }

    /// <summary>
    /// How many people are in the public room.
    ///
    /// Free: <c>chat.global</c> was always a presence channel and the count was already on the
    /// wire — it was simply never read. It is also the one number that makes the difference
    /// between a strip that looks like an abandoned box and one that looks like a room.
    /// </summary>
    public static int OnlineCount => RealtimeClient.Shared.OccupantCount("presence-chat.global");

    /// <summary>
    /// Begins listening. Safe to call repeatedly; only the first call does anything.
    ///
    /// Deliberately does not read the room here. Start-up is busy enough, and the first read is
    /// cheap to defer to whoever first wants something to show.
    /// </summary>
    public static void Start()
    {
        lock (Gate)
        {
            if (_started) return;
            _started = true;
        }

        // The same call the unread count makes. Idempotent, and this must not depend on the order
        // the two services happen to be started in.
        SocialRealtime.EnsureStarted();

        SocialRealtime.ChatMessageReceived += OnLine;
        SocialRealtime.ChatMessageDeleted += OnDeleted;
        SocialRealtime.ChatChanged += OnChanged;
        SocialRealtime.SlowModeUpdated += OnSlowMode;
        SocialRealtime.SanctionEventReceived += OnSanction;

        RealtimeClient.Shared.PresenceChanged += OnPresenceChanged;

        _timer = new DispatcherTimer { Interval = QuietCatchUpInterval };
        _timer.Tick += (_, __) => CatchUpIfQuiet();
        _timer.Start();
    }

    /// <summary>The room's lines, oldest first. A snapshot: the caller may hold it.</summary>
    public static IReadOnlyList<Models.ChatLine> Lines(string room)
    {
        lock (Gate)
            return State(room).Lines.ToArray();
    }

    /// <summary>The newest line in the room, or null when there is nothing in it yet.</summary>
    public static Models.ChatLine? Latest(string room)
    {
        lock (Gate)
        {
            var lines = State(room).Lines;
            return lines.Count == 0 ? null : lines[^1];
        }
    }

    public static int SlowModeSeconds(string room)
    {
        lock (Gate) return State(room).SlowModeSeconds;
    }

    public static int MaxLength(string room)
    {
        lock (Gate) return State(room).MaxLength;
    }

    /// <summary>How many lines have arrived in the room since it was last looked at.</summary>
    public static int Unseen(string room)
    {
        lock (Gate) return State(room).Unseen;
    }

    /// <summary>How many of those lines addressed this account.</summary>
    public static int UnseenMentions(string room)
    {
        lock (Gate) return State(room).UnseenMentions;
    }

    /// <summary>Whether the room has ever been read from the platform.</summary>
    public static bool IsLoaded(string room)
    {
        lock (Gate) return State(room).Loaded;
    }

    /// <summary>
    /// Marks the room as looked at. Called by whichever view is showing it, on open and on every
    /// arrival while it stays open.
    /// </summary>
    public static void MarkSeen(string room)
    {
        lock (Gate)
        {
            var state = State(room);
            if (state.Unseen == 0 && state.UnseenMentions == 0) return;
            state.Unseen = 0;
            state.UnseenMentions = 0;
        }

        Raise(() => UnseenChanged?.Invoke(room));
    }

    /// <summary>
    /// Reads the room if it has never been read. Cheap to call on every open — the second caller
    /// and the two hundredth wait on the first read rather than starting one of their own.
    /// </summary>
    public static async Task EnsureLoadedAsync(string room)
    {
        lock (Gate)
        {
            if (State(room).Loaded) return;
        }

        await ReloadAsync(room).ConfigureAwait(false);
    }

    /// <summary>Re-reads the room from scratch, replacing whatever is buffered.</summary>
    public static async Task ReloadAsync(string room)
    {
        if (!CloudAuthManager.IsAuthenticated) return;

        var snapshot = await SocialApi.GetChatAsync(room: room).ConfigureAwait(false);
        if (!snapshot.Ok)
        {
            // An unreachable room and an empty one look the same to a reader, and pretending to
            // have read it would stop anything ever trying again.
            ApplyMeta(room, snapshot, markLoaded: false);
            return;
        }

        lock (Gate)
        {
            var state = State(room);
            state.Lines.Clear();
            state.Lines.AddRange(snapshot.Lines);
            Trim(state);
            state.Loaded = true;

            // A first read is a catch-up, not an event: everything in it is history the reader has
            // simply not seen the panel for. Counting it as unseen would open the app on a badge
            // reporting the last two hundred lines.
            state.Unseen = 0;
            state.UnseenMentions = 0;
            if (snapshot.Lines.Count > 0) state.LastArrivalUtc = DateTime.UtcNow;
        }

        ApplyMeta(room, snapshot, markLoaded: true);
        Raise(() => RoomChanged?.Invoke(room));
    }

    // ── Live arrivals ───────────────────────────────────────────────────────

    private static void OnLine(Models.ChatLine line)
    {
        var room = string.IsNullOrWhiteSpace(line.Room) ? DefaultRoom : line.Room;

        lock (Gate)
        {
            var state = State(room);

            // Nothing has read this room yet, so there is no buffer for the line to join. Dropping
            // it is right: the first read will fetch it along with everything before it.
            if (!state.Loaded) return;

            if (state.Lines.Any(l => string.Equals(l.Id, line.Id, StringComparison.Ordinal))) return;

            state.Lines.Add(line);
            Trim(state);
            state.LastArrivalUtc = DateTime.UtcNow;

            // Your own line is not something you missed.
            if (!line.IsMine)
            {
                state.Unseen++;
                if (line.MentionsMe) state.UnseenMentions++;
            }
        }

        Raise(() =>
        {
            RoomChanged?.Invoke(room);
            LineArrived?.Invoke(line);
            if (!line.IsMine)
            {
                UnseenChanged?.Invoke(room);
                if (line.MentionsMe) MentionArrived?.Invoke(line);
            }
        });
    }

    private static void OnDeleted(string messageId)
    {
        var touched = new List<string>();

        lock (Gate)
        {
            foreach (var (room, state) in Rooms)
            {
                if (state.Lines.RemoveAll(l => string.Equals(l.Id, messageId, StringComparison.Ordinal)) > 0)
                    touched.Add(room);
            }
        }

        if (touched.Count == 0) return;
        Raise(() =>
        {
            foreach (var room in touched) RoomChanged?.Invoke(room);
        });
    }

    /// <summary>
    /// A push that carried no usable line. Read rather than guess — the endpoint knows who the
    /// reader has blocked and who has blocked them, and one broadcast frame for the whole room
    /// cannot.
    /// </summary>
    private static void OnChanged() => _ = CatchUpAsync(DefaultRoom);

    private static void OnSlowMode(Models.ChatSlowModeEvent e)
    {
        var seconds = Math.Max(0, e.Seconds);

        lock (Gate)
        {
            // Slow mode is set per room but broadcast without saying which, and the public room is
            // the only one it has ever been used on.
            State(DefaultRoom).SlowModeSeconds = seconds;
        }

        Raise(() => RoomChanged?.Invoke(DefaultRoom));
    }

    private static void OnSanction(Models.SystemSanctionEvent e)
    {
        var line = Models.ChatLine.FromSanction(e);
        var room = string.IsNullOrWhiteSpace(line.Room) ? DefaultRoom : line.Room;

        lock (Gate)
        {
            var state = State(room);
            if (state.Loaded && !state.Lines.Any(l => string.Equals(l.Id, line.Id, StringComparison.Ordinal)))
            {
                state.Lines.Add(line);
                Trim(state);
            }
        }

        var me = CloudAuthManager.CurrentUser?.Id;
        if (!string.IsNullOrEmpty(me) && string.Equals(e.Target?.Id, me, StringComparison.OrdinalIgnoreCase))
        {
            Sanction = e.IsLifted ? null : new Models.ChatSanction(e.Kind, e.Reason, e.ExpiresAt);
            Raise(() => SanctionChanged?.Invoke(Sanction));
        }

        Raise(() => RoomChanged?.Invoke(room));
    }

    private static void OnPresenceChanged(string channel, int count)
    {
        if (!channel.Contains("chat.global", StringComparison.OrdinalIgnoreCase)) return;
        Raise(() => OnlineCountChanged?.Invoke(count));
    }

    // ── Catch-up ────────────────────────────────────────────────────────────

    private static readonly HashSet<string> CatchingUp = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Fetches what was written since the newest line we hold.
    ///
    /// Folded to one request at a time per room: ten lines arriving together cost one read rather
    /// than ten, and the follow-up flag means the one in flight is never the last word.
    /// </summary>
    private static async Task CatchUpAsync(string room)
    {
        if (!CloudAuthManager.IsAuthenticated) return;

        lock (Gate)
        {
            if (!State(room).Loaded) return;
            if (!CatchingUp.Add(room)) return;
        }

        try
        {
            string? since;
            lock (Gate)
            {
                var lines = State(room).Lines;
                since = lines.Count == 0 ? null : lines[^1].SentAtIso;
            }

            if (string.IsNullOrWhiteSpace(since))
            {
                await ReloadAsync(room).ConfigureAwait(false);
                return;
            }

            var snapshot = await SocialApi.GetChatAsync(since, room: room).ConfigureAwait(false);
            if (!snapshot.Ok) return;

            var arrived = new List<Models.ChatLine>();

            lock (Gate)
            {
                var state = State(room);
                var known = new HashSet<string>(state.Lines.Select(l => l.Id), StringComparer.Ordinal);

                foreach (var line in snapshot.Lines)
                {
                    if (!known.Add(line.Id)) continue;
                    state.Lines.Add(line);
                    arrived.Add(line);
                    if (line.IsMine) continue;

                    state.Unseen++;
                    if (line.MentionsMe) state.UnseenMentions++;
                }

                if (arrived.Count > 0)
                {
                    Trim(state);
                    state.LastArrivalUtc = DateTime.UtcNow;
                }
            }

            ApplyMeta(room, snapshot, markLoaded: true);

            if (arrived.Count == 0) return;

            Raise(() =>
            {
                RoomChanged?.Invoke(room);
                foreach (var line in arrived)
                {
                    LineArrived?.Invoke(line);
                    if (!line.IsMine && line.MentionsMe) MentionArrived?.Invoke(line);
                }
                UnseenChanged?.Invoke(room);
            });
        }
        finally
        {
            lock (Gate) CatchingUp.Remove(room);
        }
    }

    /// <summary>
    /// The backstop. Reads only when nothing has arrived for a while, so it catches a socket that
    /// has quietly stopped delivering without adding a request to a room that is working.
    /// </summary>
    private static void CatchUpIfQuiet()
    {
        if (!CloudAuthManager.IsAuthenticated) return;

        var stale = new List<string>();

        lock (Gate)
        {
            foreach (var (room, state) in Rooms)
            {
                if (!state.Loaded) continue;
                if (DateTime.UtcNow - state.LastArrivalUtc < QuietCatchUpInterval) continue;
                stale.Add(room);
            }
        }

        foreach (var room in stale) _ = CatchUpAsync(room);
    }

    // ── Plumbing ────────────────────────────────────────────────────────────

    /// <summary>
    /// The state for a room, creating it for a name we have not seen.
    ///
    /// Caller holds <see cref="Gate"/>. A room the platform invents later should buffer rather
    /// than throw.
    /// </summary>
    private static RoomState State(string room)
    {
        var key = string.IsNullOrWhiteSpace(room) ? DefaultRoom : room;
        if (!Rooms.TryGetValue(key, out var state))
        {
            state = new RoomState();
            Rooms[key] = state;
        }
        return state;
    }

    private static void Trim(RoomState state)
    {
        if (state.Lines.Count > Window)
            state.Lines.RemoveRange(0, state.Lines.Count - Window);
    }

    /// <summary>
    /// Takes the parts of a read that are not lines: slow mode, the length cap, supporter access
    /// and the reader's own sanction.
    /// </summary>
    private static void ApplyMeta(string room, Models.ChatSnapshot snapshot, bool markLoaded)
    {
        lock (Gate)
        {
            var state = State(room);
            state.SlowModeSeconds = snapshot.SlowModeSeconds;
            if (snapshot.MaxLength > 0) state.MaxLength = snapshot.MaxLength;
            if (markLoaded) state.Loaded = true;
        }

        // Only a successful read says anything about access. A failed one reports false for every
        // field, and taking that at face value would shut a supporter out of their own room because
        // the network blinked.
        if (!snapshot.Ok) return;

        SupporterRoomOpen = snapshot.SupporterRoom;

        var previous = Sanction;
        Sanction = snapshot.Sanction;

        var changed = previous?.Kind != Sanction?.Kind
            || previous?.ExpiresAt != Sanction?.ExpiresAt
            || previous?.Reason != Sanction?.Reason;

        if (changed) Raise(() => SanctionChanged?.Invoke(Sanction));
    }

    /// <summary>
    /// Subscribers touch controls, and arrivals land on the socket's receive loop. Marshalling
    /// here rather than in each handler means a subscriber cannot forget.
    /// </summary>
    private static void Raise(Action action)
    {
        var app = System.Windows.Application.Current;
        if (app == null) return;

        if (app.Dispatcher.CheckAccess()) action();
        else app.Dispatcher.InvokeAsync(action);
    }
}
