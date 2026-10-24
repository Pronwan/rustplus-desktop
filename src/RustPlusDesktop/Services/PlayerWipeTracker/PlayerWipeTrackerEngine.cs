using System;
using System.Collections.Generic;
using System.Linq;

namespace RustPlusDesk.Services.PlayerWipeTracker;

/// <summary>Pure observation classifier and segment builder for one player/wipe.</summary>
public sealed class PlayerWipeTrackerEngine
{
    public const double PersistDistanceWorldUnits = 10;
    public const double MonumentExitHysteresisWorldUnits = 15;
    public const int MonumentOutsideObservationsToExit = 2;
    public const int MinimumMonumentVisitSeconds = 30;
    public const int ReentryMergeSeconds = 60;
    public const int HeartbeatSeconds = 60;

    /// <summary>How often the team poll normally arrives; movement is judged per this interval.</summary>
    public const int NominalPollSeconds = 5;
    public const int MaxContinuityGapSeconds = 15;

    /// <summary>
    /// The widest gap between two stored records that still counts as one unbroken stretch.
    ///
    /// The live poll arrives every few seconds, but a player standing still is only written
    /// down once per heartbeat — storing every poll is what turned a monthly wipe into 150,000
    /// records and a gigabyte on disk. So a reload sees records up to a heartbeat apart, plus
    /// one poll of slack, and has to read that as continuous; the live engine uses the same
    /// figure so that a reload rebuilds the timeline the live session showed.
    /// </summary>
    public const int MaxRecordGapSeconds = HeartbeatSeconds + MaxContinuityGapSeconds;

    private readonly List<TrackerSegment> _segments = new();
    private readonly List<MonumentVisit> _visits = new();
    private PlayerObservation? _last;
    private PlayerObservation? _lastPersisted;
    private PlayerObservation? _pendingTail;
    private PlayerActivityState _state;
    private DateTime _stateStartUtc;
    private PlayerObservation? _stateStartObservation;
    private string? _activeMonument;
    private DateTime _monumentStartUtc;
    private double? _monumentEntryX;
    private double? _monumentEntryY;
    private int _outsideMonumentObservations;

    public IReadOnlyList<TrackerSegment> Segments => _segments;
    public IReadOnlyList<MonumentVisit> MonumentVisits => _visits;
    public PlayerObservation? LastObservation => _last;

    /// <summary>The latest observation when it was not stored, so a session end can store it.</summary>
    public PlayerObservation? UnpersistedLast => _last is not null && !ReferenceEquals(_last, _lastPersisted) ? _last : null;
    public double EstimatedDistance { get; private set; }
    public int Deaths { get; private set; }

    public bool Observe(PlayerObservation observation)
    {
        observation = observation with { TimestampUtc = observation.TimestampUtc.ToUniversalTime() };
        if (observation.SteamId == 0 || string.IsNullOrWhiteSpace(observation.SessionId))
            return false;

        if (_last is null)
        {
            _last = observation;
            _lastPersisted = observation;
            _state = Classify(observation);
            _stateStartUtc = observation.TimestampUtc;
            _stateStartObservation = observation;
            UpdateMonument(observation);
            return true;
        }

        if (observation.TimestampUtc <= _last.TimestampUtc)
            return false;

        var previous = _last;
        var gap = observation.TimestampUtc - previous.TimestampUtc;
        var continuity = previous.SessionId == observation.SessionId
            && gap.TotalSeconds <= MaxRecordGapSeconds
            && previous.IsConnected && previous.SnapshotValid
            && observation.IsConnected && observation.SnapshotValid;

        if (!continuity)
        {
            CloseState(previous.TimestampUtc);
            if (observation.TimestampUtc > previous.TimestampUtc)
            {
                _segments.Add(new TrackerSegment(
                    previous.TimestampUtc,
                    observation.TimestampUtc,
                    PlayerActivityState.Unknown,
                    TrackerLocationType.Unknown,
                    null,
                    null,
                    previous.X,
                    previous.Y,
                    observation.X,
                    observation.Y,
                    observation.SessionId));
            }

            _state = Classify(observation);
            _stateStartUtc = observation.TimestampUtc;
            _stateStartObservation = observation;
            _outsideMonumentObservations = 0;
            UpdateMonument(observation);
            // The stretch before the break ends at the previous poll; unless that poll was
            // stored, a reload would end it at the last heartbeat instead.
            if (!ReferenceEquals(previous, _lastPersisted))
                _pendingTail = previous;
            _last = observation;
            _lastPersisted = observation;
            return true;
        }

        if (previous.Dead != observation.Dead && !previous.Dead && observation.Dead)
            Deaths++;

        // Judged per poll interval rather than per step: after thinning, two stored records can be
        // several polls apart, and a player creeping a few units per poll must not read as
        // moving on a reload when the live session saw them standing.
        var displacement = DistanceFromLastPersisted(previous, observation)
            * NominalPollSeconds / Math.Max(NominalPollSeconds, gap.TotalSeconds);
        var nextState = Classify(observation, displacement);
        var stateChanged = nextState != _state || observation.LocationType != _stateStartObservation?.LocationType ||
            !string.Equals(observation.LocationName, _stateStartObservation?.LocationName, StringComparison.Ordinal);
        if (stateChanged)
        {
            CloseState(observation.TimestampUtc);
            _state = nextState;
            _stateStartUtc = observation.TimestampUtc;
            _stateStartObservation = observation;
        }

        if (CanMeasureDistance(previous, observation))
            EstimatedDistance += Distance(previous, observation);

        UpdateMonument(observation);
        _last = observation;

        // A late poll would leave more than a heartbeat between stored records, which a reload
        // reads as a break; storing the poll before it keeps the stored gaps within bounds.
        // So does a state change: the poll before it is what a reload measures the first step of
        // the new state against, and the last heartbeat could be a minute further back.
        var latePoll = gap.TotalSeconds > MaxContinuityGapSeconds;
        if ((latePoll || stateChanged) && !ReferenceEquals(previous, _lastPersisted))
            _pendingTail = previous;

        // Measured against the last stored record rather than the last poll: a player creeping a
        // few units per poll still gets written down once the creep adds up, and one standing
        // still costs a record per heartbeat instead of one per poll. The heartbeat used to be
        // measured from the start of the state, which after the first minute stored every poll.
        var persist = stateChanged || latePoll ||
            observation.TimestampUtc - _lastPersisted!.TimestampUtc >= TimeSpan.FromSeconds(HeartbeatSeconds) ||
            DistanceFromLastPersisted(_lastPersisted, observation) >= PersistDistanceWorldUnits;
        if (persist)
            _lastPersisted = observation;
        return persist;
    }

    /// <summary>
    /// Feeds a record read back from storage. Everything replayed was stored by definition, so
    /// the thinning that follows a reload measures from it rather than from nothing.
    /// </summary>
    public void Replay(PlayerObservation observation)
    {
        Observe(observation);
        _lastPersisted = _last;
        _pendingTail = null;
    }

    /// <summary>
    /// An earlier observation that has to be stored ahead of the one <see cref="Observe"/> just
    /// asked to store: the last poll before a break, which marks where the stretch ended.
    /// </summary>
    public PlayerObservation? TakePendingTail()
    {
        var tail = _pendingTail;
        _pendingTail = null;
        return tail;
    }

    /// <summary>
    /// The records this engine would have stored for a run of polls. Used to thin histories
    /// written before the heartbeat was fixed, which stored nearly every poll.
    /// </summary>
    public static List<PlayerObservation> Compact(IEnumerable<PlayerObservation> ordered)
    {
        var engine = new PlayerWipeTrackerEngine();
        var kept = new List<PlayerObservation>();
        foreach (var observation in ordered)
        {
            if (!engine.Observe(observation))
                continue;
            if (engine.TakePendingTail() is { } tail)
                kept.Add(tail);
            kept.Add(engine._last!);
        }

        // The final poll is where the player was last seen; it stays even when it was not due.
        if (engine.UnpersistedLast is { } last)
            kept.Add(last);
        return kept;
    }

    public void EndSession(DateTime timestampUtc)
    {
        if (_last is null)
            return;

        var end = timestampUtc.ToUniversalTime();
        if (end <= _last.TimestampUtc)
            return;

        CloseState(_last.TimestampUtc);
        _segments.Add(new TrackerSegment(
            _last.TimestampUtc,
            end,
            PlayerActivityState.Unknown,
            TrackerLocationType.Unknown,
            null,
            null,
            _last.X,
            _last.Y,
            null,
            null,
            _last.SessionId));
        CloseMonument(end, _last.X, _last.Y);
        _last = null;
    }

    public TrackerSummary Summarize()
    {
        var durations = _segments
            .Where(s => s.EndUtc > s.StartUtc)
            .GroupBy(s => s.State)
            .ToDictionary(g => g.Key, g => TimeSpan.FromTicks(g.Sum(s => (s.EndUtc - s.StartUtc).Ticks)));

        TimeSpan Duration(PlayerActivityState state) => durations.TryGetValue(state, out var d) ? d : TimeSpan.Zero;

        return new TrackerSummary(
            Duration(PlayerActivityState.Moving) + Duration(PlayerActivityState.Stationary) +
            Duration(PlayerActivityState.Afk) + Duration(PlayerActivityState.Dead) + Duration(PlayerActivityState.Offline),
            Duration(PlayerActivityState.Unknown),
            Duration(PlayerActivityState.Moving),
            Duration(PlayerActivityState.Stationary),
            Duration(PlayerActivityState.Afk),
            Duration(PlayerActivityState.Dead),
            Duration(PlayerActivityState.Offline),
            EstimatedDistance,
            Deaths,
            _visits.ToArray());
    }

    public static PlayerActivityState Classify(PlayerObservation observation)
    {
        if (!observation.IsConnected || !observation.SnapshotValid)
            return PlayerActivityState.Unknown;
        if (!observation.Online)
            return PlayerActivityState.Offline;
        if (observation.Dead)
            return PlayerActivityState.Dead;
        if (observation.Afk)
            return PlayerActivityState.Afk;
        return PlayerActivityState.Stationary;
    }

    public static PlayerActivityState Classify(PlayerObservation observation, double displacement)
    {
        var state = Classify(observation);
        return state == PlayerActivityState.Stationary && displacement >= PersistDistanceWorldUnits
            ? PlayerActivityState.Moving
            : state;
    }

    private void CloseState(DateTime endUtc)
    {
        if (_stateStartObservation is null || endUtc <= _stateStartUtc)
            return;

        var last = _last ?? _stateStartObservation;
        _segments.Add(new TrackerSegment(
            _stateStartUtc,
            endUtc,
            _state,
            _stateStartObservation.LocationType,
            _stateStartObservation.LocationName,
            _stateStartObservation.Grid,
            _stateStartObservation.X,
            _stateStartObservation.Y,
            last.X,
            last.Y,
            _stateStartObservation.SessionId));
    }

    private void UpdateMonument(PlayerObservation observation)
    {
        if (!observation.IsConnected || !observation.SnapshotValid)
            return;

        if (observation.LocationType == TrackerLocationType.Monument && !string.IsNullOrWhiteSpace(observation.LocationName))
        {
            if (_activeMonument is null)
            {
                _activeMonument = observation.LocationName;
                _monumentStartUtc = observation.TimestampUtc;
                _monumentEntryX = observation.X;
                _monumentEntryY = observation.Y;
            }
            else if (!string.Equals(_activeMonument, observation.LocationName, StringComparison.Ordinal))
            {
                CloseMonument(observation.TimestampUtc, observation.X, observation.Y);
                _activeMonument = observation.LocationName;
                _monumentStartUtc = observation.TimestampUtc;
                _monumentEntryX = observation.X;
                _monumentEntryY = observation.Y;
            }

            _outsideMonumentObservations = 0;
            return;
        }

        if (_activeMonument is null)
            return;

        _outsideMonumentObservations++;
        if (_outsideMonumentObservations >= MonumentOutsideObservationsToExit)
            CloseMonument(observation.TimestampUtc, observation.X, observation.Y);
    }

    private void CloseMonument(DateTime endUtc, double? exitX, double? exitY)
    {
        if (_activeMonument is null)
            return;

        if ((endUtc - _monumentStartUtc).TotalSeconds >= MinimumMonumentVisitSeconds)
        {
            var visit = new MonumentVisit(_activeMonument, _monumentStartUtc, endUtc, _monumentEntryX, _monumentEntryY, exitX, exitY);
            var previous = _visits.LastOrDefault();
            if (previous is not null && string.Equals(previous.Name, visit.Name, StringComparison.Ordinal)
                && (visit.StartUtc - previous.EndUtc).TotalSeconds <= ReentryMergeSeconds)
            {
                _visits[^1] = previous with { EndUtc = visit.EndUtc, ExitX = visit.ExitX, ExitY = visit.ExitY };
            }
            else
            {
                _visits.Add(visit);
            }
        }

        _activeMonument = null;
        _outsideMonumentObservations = 0;
    }

    private static bool CanMeasureDistance(PlayerObservation previous, PlayerObservation current)
        => previous.SnapshotValid && current.SnapshotValid && previous.IsConnected && current.IsConnected &&
            !TrackerRouteContinuity.IsJump(previous, current);

    private static double Distance(PlayerObservation a, PlayerObservation b)
        => Math.Sqrt(Math.Pow(a.X!.Value - b.X!.Value, 2) + Math.Pow(a.Y!.Value - b.Y!.Value, 2));

    private static double DistanceFromLastPersisted(PlayerObservation a, PlayerObservation b)
        => a.X is null || a.Y is null || b.X is null || b.Y is null ? 0 : Distance(a, b);
}

/// <summary>
/// Decides whether two consecutive records belong to one stretch of travel (joined by a route
/// line and counted as distance) or whether the player got from one to the other some other way.
/// </summary>
public static class TrackerRouteContinuity
{
    /// <summary>
    /// West of this the player is in the Deep Sea. That instance sits far off the map's west
    /// edge in world space while in game it is entered from the south, so the step between the
    /// two is a teleport. Same threshold the live map uses to switch to the Deep Sea view.
    /// </summary>
    public const double DeepSeaWorldX = -1000;

    /// <summary>World units per second no vehicle reaches; anything faster was not travel.</summary>
    public const double MaxTravelSpeed = 35;

    /// <summary>Short hops always pass, so a slightly late poll cannot split a route.</summary>
    public const double MinJumpDistance = 100;

    public static bool IsInDeepSea(double x) => x < DeepSeaWorldX;

    public static bool IsJump(PlayerObservation previous, PlayerObservation current)
    {
        if (previous.X is not { } px || previous.Y is not { } py || current.X is not { } cx || current.Y is not { } cy)
            return true;

        // Death and respawn are a teleport by nature: the body stays where it fell and the player
        // reappears at a bag or a beach. Going offline and back is treated the same way, because
        // Rust+ reports some offline players at a placeholder position.
        if (previous.SessionId != current.SessionId || previous.Dead != current.Dead ||
            previous.Online != current.Online || previous.SpawnTime != current.SpawnTime ||
            previous.DeathTime != current.DeathTime)
            return true;

        if (IsInDeepSea(px) != IsInDeepSea(cx))
            return true;

        var gap = (current.TimestampUtc - previous.TimestampUtc).TotalSeconds;
        if (gap <= 0 || gap > PlayerWipeTrackerEngine.MaxRecordGapSeconds)
            return true;

        var distance = Math.Sqrt((cx - px) * (cx - px) + (cy - py) * (cy - py));
        return distance > Math.Max(MinJumpDistance, gap * MaxTravelSpeed);
    }
}
