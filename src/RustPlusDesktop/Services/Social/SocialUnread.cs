using RustPlusDesk.Services.Cloud;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace RustPlusDesk.Services.Social;

/// <summary>
/// How many messages are waiting, as a fact about the account rather than about the panel.
///
/// It used to be counted inside the Community panel, which meant it only existed once that panel
/// had been opened — so the badge that is supposed to tell you somebody wrote appeared only after
/// you had already gone and looked. Counting here instead lets the rail carry the number from
/// start-up, whether or not anything has been opened.
///
/// Three things move it: a push over the websocket, the panel reporting what it just read, and a
/// slow timer for the case where the socket is not connected at all. The timer is the reason the
/// badge is never simply wrong for an evening, and it is slow enough to cost nothing.
/// </summary>
public static class SocialUnread
{
    /// <summary>Raised on the UI thread whenever the number changes.</summary>
    public static event Action<int>? Changed;

    public static int Count { get; private set; }

    /// <summary>The fallback for a dead socket. Long: this is a backstop, not the mechanism.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);

    private static readonly object Gate = new();

    private static bool _started;
    private static DispatcherTimer? _timer;
    private static bool _reading;

    /// <summary>
    /// Set when a refresh is requested while one is already running. The in-flight read finishes,
    /// then runs exactly one more to catch whatever changed after it started reading - so a burst of
    /// pushes converges on the true count immediately rather than waiting for the slow timer.
    /// </summary>
    private static bool _dirty;

    /// <summary>Begins counting. Safe to call repeatedly; only the first call does anything.</summary>
    public static void Start()
    {
        lock (Gate)
        {
            if (_started) return;
            _started = true;
        }

        // The socket has to be up for any of this to be live. It used to be started by the
        // Community panel, which WPF does not realise until it is first shown - so on a fresh
        // launch nothing was subscribed to anything and the count only ever moved on a restart.
        SocialRealtime.EnsureStarted();

        SocialRealtime.MessageArrived += conversationId => _ = RefreshAsync();
        SocialRealtime.RequestArrived += () => _ = RefreshAsync();
        // A pending friend request changes what is waiting too; without this the badge only moved on
        // messages and the two-minute timer.
        SocialRealtime.FriendRequestArrived += () => _ = RefreshAsync();

        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += (_, __) => _ = RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
    }

    /// <summary>
    /// Re-reads the inbox and publishes the total.
    ///
    /// Overlapping reads are collapsed rather than queued: a burst of pushes marks the read dirty
    /// instead of firing a request each, and the in-flight read runs one follow-up when it lands so
    /// the count settles on the truth immediately - without turning a busy conversation into a
    /// stream of identical requests.
    /// </summary>
    public static async Task RefreshAsync()
    {
        if (!CloudAuthManager.IsAuthenticated)
        {
            Report(0);
            return;
        }

        lock (Gate)
        {
            if (_reading)
            {
                // A read is already running; note that its result may already be stale and let it
                // pick this up when it finishes rather than starting a second one now.
                _dirty = true;
                return;
            }

            _reading = true;
            _dirty = false;
        }

        try
        {
            var threads = await SocialApi.GetThreadsAsync().ConfigureAwait(false);
            Report(threads.Sum(t => t.UnreadCount));
        }
        finally
        {
            bool runAgain;
            lock (Gate)
            {
                _reading = false;
                runAgain = _dirty;
                _dirty = false;
            }

            // Something arrived mid-read. Run exactly once more to catch it.
            if (runAgain) _ = RefreshAsync();
        }
    }

    /// <summary>
    /// Publishes a count somebody else already worked out.
    ///
    /// The panel reads the same threads when it draws its list, and asking the platform twice for
    /// one answer is the kind of thing that turns a busy conversation into a stream of requests.
    /// </summary>
    public static void Report(int count)
    {
        if (count == Count) return;
        Count = count;

        var app = System.Windows.Application.Current;
        if (app == null) return;

        if (app.Dispatcher.CheckAccess()) Changed?.Invoke(count);
        else app.Dispatcher.InvokeAsync(() => Changed?.Invoke(count));
    }
}
