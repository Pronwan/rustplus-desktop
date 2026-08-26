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
    public const int MaxContinuityGapSeconds = 15;

    private readonly List<TrackerSegment> _segments = new();
    private readonly List<MonumentVisit> _visits = new();
    private PlayerObservation? _last;
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
            && gap.TotalSeconds <= MaxContinuityGapSeconds
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
            _last = observation;
            return true;
        }

        if (previous.Dead != observation.Dead && !previous.Dead && observation.Dead)
            Deaths++;

        var displacement = DistanceFromLastPersisted(previous, observation);
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

        if (CanMeasureDistance(previous, observation, gap))
            EstimatedDistance += Distance(previous, observation);

        UpdateMonument(observation);
        _last = observation;

        return stateChanged ||
            observation.TimestampUtc - _stateStartUtc >= TimeSpan.FromSeconds(HeartbeatSeconds) ||
            displacement >= PersistDistanceWorldUnits;
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

    private static bool CanMeasureDistance(PlayerObservation previous, PlayerObservation current, TimeSpan gap)
    {
        if (previous.SessionId != current.SessionId || !previous.SnapshotValid || !current.SnapshotValid ||
            !previous.IsConnected || !current.IsConnected || previous.X is null || previous.Y is null ||
            current.X is null || current.Y is null || previous.Dead != current.Dead ||
            previous.DeathTime != current.DeathTime || previous.SpawnTime != current.SpawnTime ||
            gap <= TimeSpan.Zero || gap.TotalSeconds > 15)
            return false;

        var distance = Distance(previous, current);
        return distance <= Math.Max(100, gap.TotalSeconds * 35);
    }

    private static double Distance(PlayerObservation a, PlayerObservation b)
        => Math.Sqrt(Math.Pow(a.X!.Value - b.X!.Value, 2) + Math.Pow(a.Y!.Value - b.Y!.Value, 2));

    private static double DistanceFromLastPersisted(PlayerObservation a, PlayerObservation b)
        => a.X is null || a.Y is null || b.X is null || b.Y is null ? 0 : Distance(a, b);
}
