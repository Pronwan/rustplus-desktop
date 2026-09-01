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
    private readonly Dictionary<ulong, PlayerWipeTrackerEngine> _engines = new();
    private string? _serverKey;
    private string? _wipeKey;
    private string? _sessionId;
    private ulong _ownSteamId;
    private DateTime? _wipeStartedAtUtc;

    /// <summary>
    /// How often new observations are sent, per player and day.
    ///
    /// Uploading on every observation used to re-send the entire day — every observation since
    /// midnight — because the endpoint took whole days. For a moving player that fired on each
    /// five-second team poll with a body that grew all day, which saturated the uplink and put
    /// the game's own traffic behind it. Past 512 KB the server then rejected the document
    /// outright, so the traffic bought nothing at all.
    ///
    /// Now only observations above the acknowledged cursor go out, batched once a minute. A
    /// minute of a sprinting player is about twelve entries — a couple of kilobytes.
    /// </summary>
    private static readonly TimeSpan CloudUploadInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Ceiling on one request, matching the server's own per-batch limit. A backlog — a long
    /// offline stretch, a restart with a stale cursor — drains over several cycles instead of
    /// arriving as one oversized body that gets refused.
    /// </summary>
    private const int MaxObservationsPerBatch = 1000;

    /// <summary>
    /// Hard ceiling on one request body, on top of the count.
    ///
    /// A minute of a sprinting player is a dozen observations — a couple of kilobytes — so in
    /// normal operation this is never reached. It exists for the abnormal case: a backlog after a
    /// long offline stretch would otherwise go out as one 170 KB body, and this app has no
    /// business putting that much on a home uplink in a single burst. The remainder simply goes
    /// out on the next cycle.
    /// </summary>
    private const int MaxBatchBytes = 48 * 1024;

    /// <summary>Rough serialized size of one observation, used to stop filling a batch.</summary>
    private const int ApproximateObservationBytes = 180;

    /// <summary>Whichever of the two limits bites first.</summary>
    private static int EffectiveBatchSize => Math.Min(MaxObservationsPerBatch, MaxBatchBytes / ApproximateObservationBytes);

    /// <summary>Set by the host so a repeated upload failure reaches the app log.</summary>
    public Action<string>? Log { get; set; }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _cloudLastUpload = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (ulong SteamId, DateTime TimestampUtc, string PlayerName)> _cloudDirty = new(StringComparer.Ordinal);

    public PlayerWipeTrackerService(PlayerWipeTrackerStore store, PlayerWipeTrackerCapabilityService capabilities)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    public bool Enabled { get; set; }
    public bool CloudBackupEnabled { get; set; }
    public string? CurrentServerKey => _serverKey;
    public string? CurrentWipeKey => _wipeKey;
    public ulong CurrentOwnSteamId => _ownSteamId;
    public DateTime? CurrentWipeStartedAtUtc => _wipeStartedAtUtc;
    public string? CurrentSessionId => _sessionId;
    public IReadOnlyCollection<ulong> TrackedPlayers => _engines.Keys.ToArray();
    public PlayerWipeTrackerCapabilities Capabilities => _capabilities.Current;

    public PlayerWipeTrackerCapabilities UpdateCapabilities(JsonElement bootstrap) => _capabilities.Update(bootstrap);
    public void ResetCapabilities() => _capabilities.Reset();
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

        // The batching interval can be holding up to a minute of observations. A disconnect is
        // exactly the moment to hand them over, and it is cheap: one small batch per player.
        _ = FlushCloudBackupAsync();
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
            SaveWipeMap(_serverKey, _wipeKey, map, _wipeStartedAtUtc);
        }
    }

    /// <summary>
    /// Persist the wipe map locally (used by the tracker preview). Uploading is
    /// intentionally NOT done here — that is owned by <see cref="ServerWipeMapService"/>,
    /// which the caller invokes separately.
    /// </summary>
    public void SaveWipeMap(string serverKey, string wipeKey, TrackerWipeMap map, DateTime? wipeStartedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(serverKey) || string.IsNullOrWhiteSpace(wipeKey))
            return;

        if (!_store.HasWipeMap(serverKey, wipeKey))
            _store.SaveWipeMap(serverKey, wipeKey, map);
    }

    public TrackerWipeMap? LoadCurrentWipeMap()
        => _serverKey is null || _wipeKey is null ? null : _store.LoadWipeMap(_serverKey, _wipeKey);

    public void DeleteWipe(string serverKey, string wipeKey) => _store.DeleteWipe(serverKey, wipeKey);
    public void DeleteAll() => _store.DeleteAll();

    public static string BuildWipeKey(string serverKey, DateTime? wipeTimeUtc, string? mapIdentity = null)
    {
        if (wipeTimeUtc.HasValue)
        {
            return wipeTimeUtc.Value.ToUniversalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(mapIdentity))
        {
            return mapIdentity.Trim();
        }

        return "unknown";
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

    /// <summary>
    /// Marks a player's day as having new data, and sends it only when the interval has elapsed.
    ///
    /// Rate limiting has to happen here rather than in the upload queue: building the request is
    /// itself expensive — it flushes the store, reads the whole day back off disk and re-serialises
    /// it — so a queue that merely dropped the extra uploads would still pay that cost once per
    /// observation per player.
    /// </summary>
    private async Task QueueCloudDayAsync(ulong steamId, DateTime timestampUtc, string playerName)
    {
        var utc = timestampUtc.ToUniversalTime();
        var key = CloudDayKey(steamId, DateOnly.FromDateTime(utc));

        _cloudDirty[key] = (steamId, utc, playerName);

        var now = DateTime.UtcNow;
        if (_cloudLastUpload.TryGetValue(key, out var last) && now - last < CloudUploadInterval)
            return;

        // Claim the slot before the await, so two observations arriving together cannot both
        // decide it is their turn.
        _cloudLastUpload[key] = now;
        await UploadCloudDayAsync(key).ConfigureAwait(false);
    }

    private static string CloudDayKey(ulong steamId, DateOnly day) => $"{steamId}|{day:yyyy-MM-dd}";

    /// <summary>
    /// Sends the observations this player-day has gained since the server last acknowledged one.
    ///
    /// There is no retry loop on purpose. The cursor only moves once the server confirms, so a
    /// failed send leaves it where it was and the next cycle carries the same observations plus
    /// whatever arrived meanwhile. Retrying is what the next minute does anyway, and a batch that
    /// does arrive twice merges to nothing because the server matches on timestamp.
    /// </summary>
    private async Task UploadCloudDayAsync(string key)
    {
        if (_serverKey is null || _wipeKey is null)
            return;
        if (!_cloudDirty.TryRemove(key, out var entry))
            return;

        try
        {
            await _store.FlushAsync().ConfigureAwait(false);

            var day = DateOnly.FromDateTime(entry.TimestampUtc);
            var (cursor, fromOffset) = _store.GetCloudCursor(_serverKey, _wipeKey, entry.SteamId, day);
            var (request, nextOffset, complete) = BuildCloudDelta(entry.SteamId, day, entry.PlayerName, cursor, fromOffset);
            if (request is null)
            {
                // Nothing new, but the read still established how far the file has been examined.
                if (cursor is not null && nextOffset > fromOffset)
                    _store.SetCloudCursor(_serverKey, _wipeKey, entry.SteamId, day, cursor.Value, nextOffset);
                return;
            }

            var (status, acknowledged) = await _cloudClient.AppendDayAsync(request).ConfigureAwait(false);
            if (status is < 200 or >= 300)
            {
                // Put it back so the next cycle picks the same window up again.
                _cloudDirty.TryAdd(key, entry);
                return;
            }

            var mark = acknowledged ?? DateTime.Parse(
                request.Observations[^1].Timestamp, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
            // The offset only advances when the batch carried everything that was read. Otherwise
            // the remainder lies between the old offset and here, and moving it would skip it.
            _store.SetCloudCursor(_serverKey, _wipeKey, entry.SteamId, day, mark,
                complete ? nextOffset : fromOffset);

            // A capped batch leaves a backlog behind; mark the day so the next cycle continues
            // rather than waiting for the player to move again.
            if (!complete)
                _cloudDirty.TryAdd(key, entry);
        }
        catch (Exception ex)
        {
            // Reported rather than swallowed. An earlier version caught this silently, and an
            // invalid DateTimeStyles combination meant the cursor was never written — every cycle
            // re-sent the same capped batch, the server accepted it, deduplicated it and answered
            // 200, and nothing anywhere said a word. A failure that repeats every minute has to
            // be visible somewhere.
            Log?.Invoke($"[wipe-tracker] Cloud append failed for {key}: {ex.GetType().Name}: {ex.Message}");
            _cloudDirty.TryAdd(key, entry);
        }
    }

    /// <summary>
    /// Builds a batch of observations newer than <paramref name="cursorUtc"/>, capped, and
    /// reports where the day starts in the file so the next call can skip straight to it.
    /// </summary>
    private (CloudDayAppendRequest? Request, long NextOffset, bool Complete) BuildCloudDelta(
        ulong steamId, DateOnly day, string? playerName, DateTime? cursorUtc, long fromOffset)
    {
        if (_serverKey is null || _wipeKey is null)
            return (null, fromOffset, false);

        var (stored, nextOffset) = _store.LoadDay(_serverKey, _wipeKey, steamId, day, fromOffset);

        var pending = stored
            .Where(item => item.Kind == "observation")
            .Select(item => item.Observation)
            .Where(item => cursorUtc is null || item.TimestampUtc.ToUniversalTime() > cursorUtc.Value)
            .OrderBy(item => item.TimestampUtc)
            .ToArray();

        // Everything read is going out, so the offset may move past it. When the cap bites, it
        // must not: the remainder still sits between the old offset and here.
        var observations = pending.Take(EffectiveBatchSize).ToArray();
        var complete = observations.Length == pending.Length;

        if (observations.Length == 0)
            return (null, nextOffset, true);

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

        return (new CloudDayAppendRequest
        {
            ServerKey = _serverKey,
            WipeKey = _wipeKey,
            WipeStartedAt = _wipeStartedAtUtc?.ToString("O"),
            PlayerSteamId = steamId.ToString(),
            PlayerName = playerName,
            Day = day.ToString("yyyy-MM-dd"),
            Observations = cloud,
            Sessions = observations.Select(item => item.SessionId).Distinct(StringComparer.Ordinal).ToArray(),
        }, nextOffset, complete);
    }

    /// <summary>
    /// Sends every day that has unsent observations, ignoring the interval.
    ///
    /// Called when a connection ends and on shutdown, which is what keeps the rate limit from
    /// costing anything: the last window is written out rather than discarded.
    /// </summary>
    public async Task FlushCloudBackupAsync()
    {
        if (!CloudBackupEnabled || !_capabilities.Current.CanUseCloudSync)
            return;

        foreach (var key in _cloudDirty.Keys.ToArray())
        {
            _cloudLastUpload[key] = DateTime.UtcNow;
            await UploadCloudDayAsync(key).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        // The last window goes out before the store is torn down, so closing the app costs no
        // observations rather than up to a minute of them.
        try { await FlushCloudBackupAsync().ConfigureAwait(false); } catch { }
        try { await _store.FlushAsync().ConfigureAwait(false); } catch { }
        await _store.DisposeAsync().ConfigureAwait(false);
    }
}
