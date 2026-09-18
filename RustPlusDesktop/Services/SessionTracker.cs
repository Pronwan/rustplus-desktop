using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;

namespace RustPlusDesk.Services
{
    /// <summary>What the tracker needs to know about right now, sampled once a second.</summary>
    public sealed record SessionSnapshot(
        bool Connected,
        string? ServerKey,
        ulong MySteamId,
        double? MyX,
        double? MyY,
        bool MyAfk,
        int Population,
        IReadOnlyList<(ulong SteamId, bool IsDead)> Team);

    /// <summary>One play session on one server.</summary>
    public sealed class SessionState
    {
        public string ServerKey { get; set; } = "";

        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the tracker last saw this session. A restart that lands inside
        /// <see cref="SessionTracker.ResumeWindow"/> carries on rather than starting over —
        /// closing the app to fix a setting should not cost you the evening's numbers.
        /// </summary>
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

        public double OnlineSeconds { get; set; }
        public double AfkSeconds { get; set; }

        /// <summary>Metres walked, in world units, which Rust measures in metres.</summary>
        public double DistanceMetres { get; set; }

        public int Deaths { get; set; }
        public int TeamDeaths { get; set; }

        /// <summary>Hourly population readings, for the server-info tile's graph.</summary>
        public List<PopulationSample> Population { get; set; } = new();
    }

    public sealed class PopulationSample
    {
        public DateTime Utc { get; set; }
        public int Players { get; set; }
    }

    /// <summary>
    /// Keeps a running session per server: time online, ground covered, time idle, deaths.
    ///
    /// It samples rather than subscribes. Half of what it counts is elapsed time, so a timer has
    /// to run either way, and pulling one snapshot a second is both simpler and impossible to
    /// leak across the connect and disconnect cycles the alternative would have to survive.
    ///
    /// Stored locally, per server. A session is a local thing — it starts when you sit down and
    /// ends when you stop — and nothing about it needs the cloud to be right.
    /// </summary>
    public sealed class SessionTracker
    {
        public static SessionTracker Instance { get; } = new();

        /// <summary>A gap longer than this starts a new session instead of continuing the old.</summary>
        public static readonly TimeSpan ResumeWindow = TimeSpan.FromMinutes(30);

        /// <summary>Movement below this is noise in the position feed, not walking.</summary>
        private const double MinStepMetres = 0.5;

        /// <summary>Above this, the player teleported, respawned or the map changed.</summary>
        private const double MaxStepMetres = 500;

        /// <summary>
        /// How often the population is written down.
        ///
        /// Five minutes rather than an hour: the reading is already in hand from the status the
        /// app polls anyway, so a longer gap buys nothing and costs the graph — a three-hour
        /// evening would be three dots. At this rate the same evening is a line.
        /// </summary>
        private static readonly TimeSpan PopulationInterval = TimeSpan.FromMinutes(5);

        /// <summary>Half a day of readings, which is longer than any tile draws.</summary>
        private const int MaxPopulationSamples = 144;

        /// <summary>Set once by the main window. Null until then, and the tracker idles.</summary>
        public Func<SessionSnapshot?>? Source { get; set; }

        private readonly DispatcherTimer _timer;
        private readonly Dictionary<ulong, bool> _deadLastTick = new();

        private SessionState? _current;
        private double? _lastX, _lastY;
        private DateTime _lastTickUtc = DateTime.UtcNow;
        private DateTime _lastSaveUtc = DateTime.MinValue;

        private SessionTracker()
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1),
            };
            _timer.Tick += (_, __) => Tick();
            _timer.Start();
        }

        /// <summary>The session for a server, loaded from disk if this one is not it.</summary>
        public SessionState? For(string? serverKey)
        {
            if (string.IsNullOrEmpty(serverKey)) return null;
            if (_current != null && _current.ServerKey == serverKey) return _current;

            return StorageService.LoadCache<SessionState>(CacheKey(serverKey));
        }

        /// <summary>Throws the session away and starts counting again from now.</summary>
        public void Wipe(string? serverKey)
        {
            if (string.IsNullOrEmpty(serverKey)) return;

            var fresh = new SessionState { ServerKey = serverKey };
            if (_current?.ServerKey == serverKey) _current = fresh;

            _lastX = _lastY = null;
            _deadLastTick.Clear();
            StorageService.SaveCache(CacheKey(serverKey), fresh);
        }

        private static string CacheKey(string serverKey) => $"session_{Sanitise(serverKey)}";

        private static string Sanitise(string key)
            => new(key.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

        private void Tick()
        {
            var now = DateTime.UtcNow;
            double elapsed = Math.Clamp((now - _lastTickUtc).TotalSeconds, 0, 5);
            _lastTickUtc = now;

            SessionSnapshot? snapshot;
            try { snapshot = Source?.Invoke(); }
            catch { return; }

            if (snapshot == null || !snapshot.Connected || string.IsNullOrEmpty(snapshot.ServerKey))
            {
                // Not counting, and not resetting either: the resume window decides whether
                // coming back continues this session or starts the next one.
                Persist(force: true);
                return;
            }

            var session = EnsureSession(snapshot.ServerKey, now);

            session.OnlineSeconds += elapsed;
            if (snapshot.MyAfk) session.AfkSeconds += elapsed;
            session.LastSeenUtc = now;

            AccumulateDistance(session, snapshot);
            CountDeaths(session, snapshot);
            SamplePopulation(session, snapshot, now);

            Persist(force: false);
        }

        private SessionState EnsureSession(string serverKey, DateTime now)
        {
            if (_current != null && _current.ServerKey == serverKey && now - _current.LastSeenUtc <= ResumeWindow)
                return _current;

            var stored = StorageService.LoadCache<SessionState>(CacheKey(serverKey));

            bool resumable = stored != null
                             && stored.ServerKey == serverKey
                             && now - stored.LastSeenUtc <= ResumeWindow;

            _current = resumable
                ? stored!
                : new SessionState { ServerKey = serverKey, StartedUtc = now, LastSeenUtc = now };

            // A resumed session knows nothing about where the player was while the app was shut,
            // so the first position after it comes back is a starting point, not a step.
            _lastX = _lastY = null;
            _deadLastTick.Clear();

            return _current;
        }

        private void AccumulateDistance(SessionState session, SessionSnapshot snapshot)
        {
            if (snapshot.MyX is not { } x || snapshot.MyY is not { } y)
            {
                _lastX = _lastY = null;
                return;
            }

            if (_lastX is { } px && _lastY is { } py)
            {
                double step = Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));

                // Between two samples the player walked a curve and this counts the chord, so
                // the total is a floor rather than an exact figure — which is why the tile
                // rounds it instead of claiming decimals it does not have.
                if (step is > MinStepMetres and < MaxStepMetres) session.DistanceMetres += step;
            }

            _lastX = x;
            _lastY = y;
        }

        private void CountDeaths(SessionState session, SessionSnapshot snapshot)
        {
            foreach (var (steamId, isDead) in snapshot.Team)
            {
                bool wasDead = _deadLastTick.TryGetValue(steamId, out var previous) && previous;
                _deadLastTick[steamId] = isDead;

                // The transition, not the state: a body lies there until the player respawns,
                // and counting the state would add a death every second it did.
                if (!isDead || wasDead) continue;

                if (steamId == snapshot.MySteamId) session.Deaths++;
                else session.TeamDeaths++;
            }
        }

        private static void SamplePopulation(SessionState session, SessionSnapshot snapshot, DateTime now)
        {
            if (snapshot.Population <= 0) return;

            var last = session.Population.Count > 0 ? session.Population[^1] : null;
            if (last != null && now - last.Utc < PopulationInterval) return;

            session.Population.Add(new PopulationSample { Utc = now, Players = snapshot.Population });

            if (session.Population.Count > MaxPopulationSamples) session.Population.RemoveAt(0);
        }

        /// <summary>
        /// Writes at most twice a minute. The numbers move every second, but the only thing that
        /// reads them off disk is the next app start, and a file rewritten sixty times a minute
        /// for that is a waste of a disk.
        /// </summary>
        private void Persist(bool force)
        {
            if (_current == null) return;

            var now = DateTime.UtcNow;
            if (!force && now - _lastSaveUtc < TimeSpan.FromSeconds(30)) return;

            _lastSaveUtc = now;
            try { StorageService.SaveCache(CacheKey(_current.ServerKey), _current); }
            catch { /* a session is not worth an exception on the UI thread */ }
        }
    }
}
