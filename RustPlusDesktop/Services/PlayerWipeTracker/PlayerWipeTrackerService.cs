using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.PlayerWipeTracker;

/// <summary>Coordinates the existing team snapshots, pure engines, and local JSONL storage.</summary>
public sealed class PlayerWipeTrackerService : IAsyncDisposable
{
    private readonly PlayerWipeTrackerStore _store;
    private readonly PlayerWipeTrackerCapabilityService _capabilities;
    private readonly PlayerWipeTrackerCloudClient _cloudClient = new();
    private readonly PlayerWipeTrackerCloudSyncQueue _cloudQueue;
    private readonly Dictionary<ulong, PlayerWipeTrackerEngine> _engines = new();
    private string? _serverKey;
    private string? _wipeKey;
    private string? _sessionId;
    private ulong _ownSteamId;
    private DateTime? _wipeStartedAtUtc;

    public PlayerWipeTrackerService(PlayerWipeTrackerStore store, PlayerWipeTrackerCapabilityService capabilities)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _cloudQueue = new PlayerWipeTrackerCloudSyncQueue((request, cancellationToken) => _cloudClient.PutDayAsync(request, cancellationToken));
    }

    public bool Enabled { get; set; }
    public bool CloudBackupEnabled { get; set; }
    public string? CurrentServerKey => _serverKey;
    public string? CurrentWipeKey => _wipeKey;
    public DateTime? CurrentWipeStartedAtUtc => _wipeStartedAtUtc;
    public string? CurrentSessionId => _sessionId;
    public IReadOnlyCollection<ulong> TrackedPlayers => _engines.Keys.ToArray();
    public PlayerWipeTrackerCapabilities Capabilities => _capabilities.Current;

    public PlayerWipeTrackerCapabilities UpdateCapabilities(JsonElement bootstrap) => _capabilities.Update(bootstrap);
    public IReadOnlyList<StoredWipeSummary> GetStoredWipes() => _store.GetStoredWipes();

    public void StartConnection(string serverKey, DateTime? wipeTimeUtc, string? mapIdentity, ulong ownSteamId, string? sessionId = null, string? serverName = null)
    {
        _serverKey = serverKey;
        _wipeKey = BuildWipeKey(serverKey, wipeTimeUtc, mapIdentity);
        _wipeStartedAtUtc = wipeTimeUtc?.ToUniversalTime();
        _sessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId;
        _ownSteamId = ownSteamId;

        if (!string.IsNullOrWhiteSpace(serverName) || _store.LoadWipeMetadata(serverKey, _wipeKey) is null)
        {
            _store.SaveWipeMetadata(serverKey, _wipeKey, new StoredWipeMetadata(
                serverKey,
                string.IsNullOrWhiteSpace(serverName) ? serverKey : serverName,
                _wipeKey,
                _wipeStartedAtUtc,
                DateTime.UtcNow));
        }

        _engines.Clear();
        foreach (var steamId in _store.LoadPlayerIds(serverKey, _wipeKey))
        {
            if (_capabilities.Current.CanTrackPlayer(steamId, ownSteamId))
                _engines[steamId] = LoadEngine(steamId);
        }
    }

    public void SwitchWipe(string serverKey, string wipeKey, DateTime? wipeStartedAtUtc = null)
    {
        _serverKey = serverKey;
        _wipeKey = wipeKey;
        _wipeStartedAtUtc = wipeStartedAtUtc?.ToUniversalTime();
        _sessionId = null;
        _engines.Clear();
        foreach (var steamId in _store.LoadPlayerIds(serverKey, _wipeKey))
        {
            if (_capabilities.Current.CanTrackPlayer(steamId, _ownSteamId))
                _engines[steamId] = LoadEngine(steamId);
        }
    }

    public void Observe(PlayerObservation observation)
    {
        if (!Enabled || _serverKey is null || _wipeKey is null ||
            !_capabilities.Current.IsTrackerAvailable ||
            !_capabilities.Current.CanTrackPlayer(observation.SteamId, _ownSteamId))
            return;

        if (_sessionId is null)
            _sessionId = observation.SessionId;

        if (!_engines.TryGetValue(observation.SteamId, out var engine))
        {
            engine = LoadEngine(observation.SteamId);
            _engines[observation.SteamId] = engine;
        }

        if (!engine.Observe(observation with { SessionId = _sessionId }))
            return;

        _store.Append(_serverKey, _wipeKey, observation.SteamId,
            new TrackerPersistedObservation(1, "observation", observation with { SessionId = _sessionId }));

        if (CloudBackupEnabled && _capabilities.Current.CanUseCloudSync)
            _ = QueueCloudDayAsync(observation.SteamId, observation.TimestampUtc, observation.Name);
    }

    public void Disconnect(DateTime? timestampUtc = null)
    {
        var timestamp = (timestampUtc ?? DateTime.UtcNow).ToUniversalTime();
        foreach (var (steamId, engine) in _engines)
        {
            var last = engine.LastObservation;
            engine.EndSession(timestamp);
            if (_serverKey is not null && _wipeKey is not null && last is not null && timestamp > last.TimestampUtc)
            {
                _store.Append(_serverKey, _wipeKey, steamId,
                    new TrackerPersistedObservation(1, "observation", last with
                    {
                        TimestampUtc = timestamp,
                        IsConnected = false,
                        SnapshotValid = false,
                    }));
            }
        }
        _sessionId = null;
    }

    public TrackerSummary GetSummary(ulong steamId)
        => _engines.TryGetValue(steamId, out var engine) ? engine.Summarize() : new TrackerSummary(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 0, 0, Array.Empty<MonumentVisit>());

    public IReadOnlyList<TrackerSegment> GetSegments(ulong steamId)
        => _engines.TryGetValue(steamId, out var engine) ? engine.Segments : Array.Empty<TrackerSegment>();

    /// <summary>Derives glance-value intel (current status, patterns, blind spots) for one player.</summary>
    public TrackerInsights GetInsights(ulong steamId, DateTime? nowUtc = null)
        => TrackerInsightsBuilder.Build(
            GetObservations(steamId),
            GetSegments(steamId),
            GetSummary(steamId),
            nowUtc ?? DateTime.UtcNow);

    public IReadOnlyList<PlayerObservation> GetObservations(ulong steamId)
        => _serverKey is null || _wipeKey is null
            ? Array.Empty<PlayerObservation>()
            : _store.Load(_serverKey, _wipeKey, steamId)
                .Where(item => item.Kind == "observation")
                .Select(item => item.Observation)
                .OrderBy(item => item.TimestampUtc)
                .ToArray();

    public string GetPlayerName(ulong steamId)
        => GetObservations(steamId).LastOrDefault()?.Name ?? steamId.ToString(CultureInfo.InvariantCulture);

    public async Task<IReadOnlyList<CloudArchiveSummary>> GetCloudArchivesAsync(CancellationToken cancellationToken = default)
    {
        if (!Capabilities.CanUseCloudSync)
            return Array.Empty<CloudArchiveSummary>();
        return await _cloudClient.GetArchiveSummariesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CloudRestoreResult> RestoreCloudArchiveAsync(
        string archiveId,
        CancellationToken cancellationToken = default)
    {
        if (!Capabilities.CanUseCloudSync)
            throw new InvalidOperationException("Cloud restore is available on premium plans only.");

        var archive = await _cloudClient.GetArchiveDetailsAsync(archiveId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The cloud archive could not be loaded.");
        if (string.IsNullOrWhiteSpace(archive.ServerKey) || string.IsNullOrWhiteSpace(archive.WipeKey))
            throw new InvalidOperationException("The cloud archive is missing its server or wipe identity.");

        var players = 0;
        var days = 0;
        var observations = 0;
        var restoredSteamIds = new HashSet<ulong>();
        foreach (var player in archive.Players)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ulong.TryParse(player.SteamId, NumberStyles.None, CultureInfo.InvariantCulture, out var steamId) || steamId == 0)
                continue;

            var playerDays = await _cloudClient.GetRestoreDaysAsync(archive.Id, player.SteamId, cancellationToken).ConfigureAwait(false);
            if (playerDays.Count == 0)
                continue;

            players++;
            restoredSteamIds.Add(steamId);
            days += playerDays.Count;
            foreach (var day in playerDays)
            {
                observations += await ImportCloudDayAsync(archive, steamId, day, cancellationToken).ConfigureAwait(false);
            }
        }

        if (!_store.HasWipeMap(archive.ServerKey, archive.WipeKey))
        {
            try
            {
                var cloudMap = await _cloudClient.DownloadWipeMapAsync(archive.ServerKey, archive.WipeKey, cancellationToken).ConfigureAwait(false);
                if (cloudMap is not null && cloudMap.PngBytes.Length > 0)
                {
                    _store.SaveWipeMap(archive.ServerKey, archive.WipeKey, cloudMap);
                    _store.MarkWipeMapUploaded(archive.ServerKey, archive.WipeKey);
                }
            }
            catch
            {
                // Ignore map download error
            }
        }

        _store.SaveWipeMetadata(archive.ServerKey, archive.WipeKey, new StoredWipeMetadata(
            archive.ServerKey,
            string.IsNullOrWhiteSpace(archive.ServerName) ? archive.ServerKey : archive.ServerName,
            archive.WipeKey,
            archive.WipeStartedAtUtc,
            DateTime.UtcNow));

        await _store.FlushAsync(cancellationToken).ConfigureAwait(false);
        var isCurrent = string.Equals(_serverKey, archive.ServerKey, StringComparison.Ordinal) &&
            string.Equals(_wipeKey, archive.WipeKey, StringComparison.Ordinal);
        if (isCurrent)
        {
            foreach (var steamId in restoredSteamIds)
            {
                if (_capabilities.Current.CanTrackPlayer(steamId, _ownSteamId))
                    _engines[steamId] = LoadEngine(steamId);
            }
        }

        return new CloudRestoreResult(archive.Id, players, days, observations, isCurrent);
    }

    public long StorageBytes => _store.StorageBytes;
    public bool HasCurrentWipeMap => _serverKey is not null && _wipeKey is not null && _store.HasWipeMap(_serverKey, _wipeKey);

    public void SaveCurrentWipeMap(TrackerWipeMap map)
    {
        if (_serverKey is not null && _wipeKey is not null)
        {
            if (!HasCurrentWipeMap)
                _store.SaveWipeMap(_serverKey, _wipeKey, map);

            _ = TryUploadWipeMapSilentlyAsync(_serverKey, _wipeKey, map, _wipeStartedAtUtc);
        }
    }

    private async Task TryUploadWipeMapSilentlyAsync(string serverKey, string wipeKey, TrackerWipeMap map, DateTime? wipeStarted)
    {
        if (_store.IsWipeMapUploaded(serverKey, wipeKey))
            return;

        if (!RustPlusDesk.Services.Cloud.CloudAuthManager.IsAuthenticated)
            return;

        try
        {
            var status = await _cloudClient.UploadWipeMapAsync(serverKey, wipeKey, map.PngBytes, map, wipeStarted).ConfigureAwait(false);
            if (status is 200 or 201 or 409)
            {
                _store.MarkWipeMapUploaded(serverKey, wipeKey);
            }
        }
        catch
        {
            // Silently handle any network or upload failures
        }
    }

    public TrackerWipeMap? LoadCurrentWipeMap()
        => _serverKey is null || _wipeKey is null ? null : _store.LoadWipeMap(_serverKey, _wipeKey);

    public CloudDayUploadRequest? BuildCloudDay(ulong steamId, DateOnly day, string? playerName)
    {
        if (_serverKey is null || _wipeKey is null)
            return null;

        var observations = _store.Load(_serverKey, _wipeKey, steamId)
            .Where(item => item.Kind == "observation" && DateOnly.FromDateTime(item.Observation.TimestampUtc.ToUniversalTime()) == day)
            .Select(item => item.Observation)
            .OrderBy(item => item.TimestampUtc)
            .ToArray();
        if (observations.Length == 0)
            return null;

        var cloud = new List<CloudTrackerObservation>(observations.Length);
        PlayerObservation? previous = null;
        foreach (var observation in observations)
        {
            var displacement = previous is null || previous.X is null || previous.Y is null || observation.X is null || observation.Y is null
                ? 0
                : Math.Sqrt(Math.Pow(previous.X.Value - observation.X.Value, 2) + Math.Pow(previous.Y.Value - observation.Y.Value, 2));
            var continuity = previous is not null && previous.SessionId == observation.SessionId &&
                observation.TimestampUtc > previous.TimestampUtc &&
                (observation.TimestampUtc - previous.TimestampUtc).TotalSeconds <= PlayerWipeTrackerEngine.MaxContinuityGapSeconds &&
                previous.IsConnected && previous.SnapshotValid && observation.IsConnected && observation.SnapshotValid;
            var state = !continuity && previous is not null ? PlayerActivityState.Unknown :
                PlayerWipeTrackerEngine.Classify(observation, displacement);
            var eventName = previous is not null && !previous.Dead && observation.Dead ? "death" :
                previous is not null && previous.Dead && !observation.Dead ? "respawn" : null;
            cloud.Add(new CloudTrackerObservation
            {
                Timestamp = observation.TimestampUtc.ToUniversalTime().ToString("O"),
                X = observation.X,
                Y = observation.Y,
                State = state.ToString().ToLowerInvariant(),
                LocationType = observation.LocationType.ToString().ToLowerInvariant(),
                LocationName = observation.LocationName,
                Grid = observation.Grid,
                Event = eventName,
            });
            previous = observation;
        }

        var payload = new CloudTrackerDayPayload
        {
            GeneratedAt = DateTime.UtcNow.ToString("O"),
            ObservationSessions = observations.Select(item => item.SessionId).Distinct(StringComparer.Ordinal).ToArray(),
            Observations = cloud,
        };
        var json = System.Text.Json.JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new CloudDayUploadRequest
        {
            ServerKey = _serverKey,
            WipeKey = _wipeKey,
            WipeStartedAt = _wipeStartedAtUtc?.ToString("O"),
            PlayerSteamId = steamId.ToString(),
            PlayerName = playerName,
            Day = day.ToString("yyyy-MM-dd"),
            Payload = payload,
            Checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant(),
        };
    }

    public void DeleteWipe(string serverKey, string wipeKey) => _store.DeleteWipe(serverKey, wipeKey);
    public void DeleteAll() => _store.DeleteAll();

    public static string BuildWipeKey(string serverKey, DateTime? wipeTimeUtc, string? mapIdentity)
    {
        var normalized = wipeTimeUtc.HasValue
            ? wipeTimeUtc.Value.ToUniversalTime().ToString("O")
            : "unknown";
        var source = $"{serverKey.Trim()}|{normalized}|{mapIdentity?.Trim() ?? "unknown"}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }

    private PlayerWipeTrackerEngine LoadEngine(ulong steamId)
    {
        var engine = new PlayerWipeTrackerEngine();
        if (_serverKey is null || _wipeKey is null)
            return engine;
        foreach (var item in _store.Load(_serverKey, _wipeKey, steamId).Where(x => x.Kind == "observation"))
            engine.Observe(item.Observation);
        return engine;
    }

    private async Task<int> ImportCloudDayAsync(
        CloudArchiveSummary archive,
        ulong steamId,
        CloudRestoreDay day,
        CancellationToken cancellationToken)
    {
        var sessionId = day.Payload.ObservationSessions.FirstOrDefault(session => !string.IsNullOrWhiteSpace(session))
            ?? $"cloud:{archive.Id}:{day.Day}";
        var playerName = string.IsNullOrWhiteSpace(day.PlayerName) ? steamId.ToString(CultureInfo.InvariantCulture) : day.PlayerName!;
        var imported = 0;
        foreach (var point in day.Payload.Observations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!DateTimeOffset.TryParse(point.Timestamp, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                continue;

            var timestamp = parsed.UtcDateTime;
            var state = ParseState(point.State);
            var connected = state != PlayerActivityState.Unknown;
            var observation = new PlayerObservation(
                steamId,
                playerName,
                timestamp,
                sessionId,
                connected,
                connected,
                state is not PlayerActivityState.Offline and not PlayerActivityState.Unknown,
                state == PlayerActivityState.Dead,
                state == PlayerActivityState.Afk,
                point.X,
                point.Y,
                ParseLocation(point.LocationType),
                point.LocationName,
                point.Grid,
                null,
                null);

            await _store.AppendAsync(
                archive.ServerKey,
                archive.WipeKey,
                steamId,
                new TrackerPersistedObservation(1, "observation", observation),
                cancellationToken).ConfigureAwait(false);
            imported++;
        }

        return imported;
    }

    private static PlayerActivityState ParseState(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "moving" => PlayerActivityState.Moving,
            "stationary" => PlayerActivityState.Stationary,
            "afk" => PlayerActivityState.Afk,
            "dead" => PlayerActivityState.Dead,
            "offline" => PlayerActivityState.Offline,
            _ => PlayerActivityState.Unknown,
        };

    private static TrackerLocationType ParseLocation(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "monument" => TrackerLocationType.Monument,
            "base" => TrackerLocationType.Base,
            "open" => TrackerLocationType.Open,
            _ => TrackerLocationType.Unknown,
        };

    private async Task QueueCloudDayAsync(ulong steamId, DateTime timestampUtc, string playerName)
    {
        try
        {
            await _store.FlushAsync().ConfigureAwait(false);
            var request = BuildCloudDay(steamId, DateOnly.FromDateTime(timestampUtc.ToUniversalTime()), playerName);
            if (request is not null)
                _cloudQueue.Enqueue(request);
        }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        try { await _store.FlushAsync().ConfigureAwait(false); } catch { }
        await _cloudQueue.DisposeAsync().ConfigureAwait(false);
        await _store.DisposeAsync().ConfigureAwait(false);
    }
}
