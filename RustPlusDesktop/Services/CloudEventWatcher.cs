using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RustPlusDesk.Services.Audio;
using RustPlusDesk.Services.Auth;
using RustPlusDesk.Services.Cloud;
using Supabase.Realtime;
using Supabase.Realtime.Models;

namespace RustPlusDesk.Services;

/// <summary>State of one event as the backend currently sees it.</summary>
public sealed record CloudEventState(
    RustEventKind Kind,
    DateTime StartedAtUtc,
    DateTime ExpiresAtUtc,
    int Confirmations,
    bool IsConfirmed,
    IReadOnlyList<DateTime> RecentUtc,
    bool LocalOnly = false)
{
    public bool IsActive => DateTime.UtcNow < ExpiresAtUtc;

    /// <summary>Deep Sea runs exactly three hours, so its close is arithmetic, not a guess.</summary>
    public TimeSpan Remaining => ExpiresAtUtc - DateTime.UtcNow;

    public TimeSpan Age => DateTime.UtcNow - StartedAtUtc;
}

/// <summary>
/// Keeps the event state for the connected server in sync with the backend, and reports what
/// this client hears.
///
/// Deliberately does NOT synthesise <c>DynMarker</c>s, which was the first plan. Only Cargo
/// maps to a marker type at all — Deep Sea rides on its own flag and Oil Rig has no marker —
/// and a synthetic marker carries no position, so it would draw at the map origin and claim a
/// location that does not exist. Consumers read this state directly instead; the map stays
/// honest about knowing nothing.
///
/// Two realtime transports sit behind the same refresh logic. Supabase uses its broadcast
/// channel; the platform uses <see cref="RealtimeClient"/> (Pusher protocol) on a private
/// channel. Both trigger <see cref="RefreshAsync"/> on any inbound event, so the parsing and
/// diffing path is shared.
/// </summary>
public sealed class CloudEventWatcher
{
    public static CloudEventWatcher Instance { get; } = new();

    /// <summary>Raised when an event appears or changes. Marshal to the UI yourself.</summary>
    public event Action<CloudEventState>? EventChanged;

    /// <summary>Raised whenever the overall picture changed and views should refresh.</summary>
    public event Action? StateRefreshed;

    /// <summary>
    /// Pushes a fresh presence upload. Set by the window that owns the team poll.
    ///
    /// Needed because presence goes stale while a player is AFK — and an AFK player is exactly
    /// the one most likely to have the game running and hear a cue. Without this the backend
    /// refuses their report as rejected_stale_presence even though they are in-game on the
    /// right server. Reports are rare enough that one extra round trip costs nothing.
    /// </summary>
    public Func<Task>? PresenceRefresh { get; set; }

    private readonly object _gate = new();
    private readonly Dictionary<RustEventKind, CloudEventState> _events = new();
    private readonly SemaphoreSlim _subscribeLock = new(1, 1);

    // Supabase realtime state.
    private RealtimeChannel? _channel;
    private RealtimeBroadcast<BaseBroadcast<JObject>>? _broadcast;

    // Platform realtime state.
    private string? _realtimeChannel;
    private bool _realtimeHandlerAttached;

    private string? _serverKey;
    private bool _hooked;

    private CloudEventWatcher() { }

    public string? ServerKey => _serverKey;

    public IReadOnlyList<CloudEventState> Events
    {
        get { lock (_gate) return _events.Values.ToList(); }
    }

    public CloudEventState? Get(RustEventKind kind)
    {
        lock (_gate) return _events.TryGetValue(kind, out var state) ? state : null;
    }

    // ---------------------------------------------------------------- lifecycle

    /// <summary>
    /// Call on connect. Fetches the current state and subscribes to the broadcast channel the
    /// edge function pushes on.
    /// </summary>
    public async Task AttachAsync(string serverKey)
    {
        if (string.IsNullOrWhiteSpace(serverKey)) return;

        if (_serverKey == serverKey && IsSubscribed()) return;
        Detach();
        _serverKey = serverKey;

        HookListener();
        await RefreshAsync(firstFetch: true);

        if (CloudBackend.UsePlatform)
            await SubscribePlatformAsync(serverKey);
        else
            await SubscribeSupabaseAsync(serverKey);
    }

    private bool IsSubscribed()
    {
        if (CloudBackend.UsePlatform)
            return _realtimeChannel != null && RealtimeClient.Shared.IsSubscribed(_realtimeChannel);
        return _channel != null;
    }

    public void Detach()
    {
        UnhookListener();

        if (CloudBackend.UsePlatform)
            UnsubscribePlatform();
        else
            UnsubscribeSupabase();

        _serverKey = null;
        lock (_gate)
        {
            _events.Clear();
            _heardLocally.Clear();
        }
        StateRefreshed?.Invoke();
    }

    private void HookListener()
    {
        if (_hooked) return;
        GameAudioListener.Instance.Detected += OnAudioDetected;
        _hooked = true;
    }

    private void UnhookListener()
    {
        if (!_hooked) return;
        GameAudioListener.Instance.Detected -= OnAudioDetected;
        _hooked = false;
    }

    // ---------------------------------------------------------------- reading

    /// <summary>
    /// Pulls the authoritative state. An empty result is itself information: nobody has ever
    /// reported for this server.
    /// </summary>
    public async Task RefreshAsync(bool firstFetch = false)
    {
        string? serverKey = _serverKey;
        if (serverKey == null || !CloudAuth.IsAuthenticated) return;

        try
        {
            string json = await CallServerEventsAsync(
                HttpMethod.Get, null,
                new Dictionary<string, string> { ["server_key"] = serverKey });

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("events", out var events)) return;

            var parsed = new Dictionary<RustEventKind, CloudEventState>();
            foreach (var row in events.EnumerateArray())
            {
                var state = ParseRow(row);
                if (state != null) parsed[state.Kind] = state;
            }

            var changed = new List<CloudEventState>();
            lock (_gate)
            {
                var before = new Dictionary<RustEventKind, CloudEventState>(_events);

                _events.Clear();
                foreach (var (kind, state) in parsed) _events[kind] = state;
                ApplyLocalTrustLocked();

                foreach (var (kind, state) in _events)
                {
                    bool known = before.TryGetValue(kind, out var previous);

                    bool supersedesLocal =
                        known && previous!.LocalOnly
                        && Math.Abs((state.StartedAtUtc - previous.StartedAtUtc).TotalSeconds)
                           <= LocalTrustToleranceSeconds;

                    bool isNews = !known
                                  || (!supersedesLocal
                                      && (previous!.StartedAtUtc != state.StartedAtUtc
                                          || (!previous.IsConfirmed && state.IsConfirmed)));

                    if (isNews) changed.Add(state);
                }
            }

            if (!firstFetch)
                foreach (var state in changed) EventChanged?.Invoke(state);

            StateRefreshed?.Invoke();
        }
        catch (Exception ex)
        {
            Log($"[cloud-events] Could not load state: {ex.Message}");
        }
    }

    private static CloudEventState? ParseRow(JsonElement row)
    {
        try
        {
            var kind = EventCapabilities.FromBackendKey(Str(row, "event_type"));
            if (kind == null) return null;

            var recent = new List<DateTime>();
            if (row.TryGetProperty("recent", out var recentArray) && recentArray.ValueKind == JsonValueKind.Array)
                foreach (var entry in recentArray.EnumerateArray())
                    if (DateTime.TryParse(entry.GetString(), null,
                            System.Globalization.DateTimeStyles.AdjustToUniversal, out var when))
                        recent.Add(when);

            return new CloudEventState(
                kind.Value,
                ParseUtc(Str(row, "started_at")),
                ParseUtc(Str(row, "expires_at")),
                row.TryGetProperty("confirmations", out var c) && c.TryGetInt32(out int ci) ? ci : 1,
                string.Equals(Str(row, "status"), "confirmed", StringComparison.OrdinalIgnoreCase),
                recent);
        }
        catch
        {
            return null;
        }
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static DateTime ParseUtc(string? value) =>
        DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var when)
            ? when
            : DateTime.UtcNow;

    // ---------------------------------------------------------------- reporting

    private void OnAudioDetected(GameAudioDetection detection)
    {
        NoteLocalDetection(detection);
        _ = ReportAsync(detection);
    }

    // ---------------------------------------------------------------- local trust

    /// <summary>
    /// How far apart the cue and the backend's own timestamp for the same occurrence may sit.
    /// The backend stamps a report when it lands, which is up to a buffer length plus network
    /// latency after the sound began, and another client may have got there slightly earlier.
    /// </summary>
    private const double LocalTrustToleranceSeconds = 120;

    private readonly Dictionary<RustEventKind, DateTime> _heardLocally = new();

    /// <summary>
    /// Records a cue this client heard for itself, and shows it immediately.
    ///
    /// Quorum exists so that one client cannot speak for a whole server. It was never meant to
    /// stop a client speaking for itself, and that distinction matters most for the people it
    /// currently fails: someone alone with the listener on, or surrounded by players who have
    /// it switched off, waits for corroboration that can never arrive. Reporting is untouched —
    /// what everyone else sees still goes through the backend's rules unchanged.
    /// </summary>
    private void NoteLocalDetection(GameAudioDetection detection)
    {
        if (!TrackingService.TrustOwnDetections) return;

        var kind = EventCapabilities.FromBackendKey(detection.EventType);
        if (kind == null) return;

        CloudEventState? announce = null;
        lock (_gate)
        {
            _heardLocally[kind.Value] = detection.CueStartedAtUtc;

            bool wasConfirmed = _events.TryGetValue(kind.Value, out var previous) && previous.IsConfirmed;
            ApplyLocalTrustLocked();

            if (!wasConfirmed
                && _events.TryGetValue(kind.Value, out var now)
                && now.IsConfirmed && now.IsActive)
                announce = now;
        }

        if (announce == null) return;

        Log($"[cloud-events] Trusting our own {detection.EventType} detection without corroboration.");
        EventChanged?.Invoke(announce);
        StateRefreshed?.Invoke();
    }

    /// <summary>
    /// Folds locally heard cues into the current picture. Caller holds <see cref="_gate"/>.
    ///
    /// Only ever upgrades, never rewrites. If the backend is holding an older occurrence it has
    /// a reason to — "still active" is a legitimate answer — and replacing its start with ours
    /// would move an expiry the rest of the server already agrees on.
    /// </summary>
    private void ApplyLocalTrustLocked()
    {
        if (!TrackingService.TrustOwnDetections)
        {
            _heardLocally.Clear();
            return;
        }

        if (_heardLocally.Count == 0) return;

        DateTime now = DateTime.UtcNow;
        foreach (var kind in _heardLocally.Keys.ToList())
        {
            DateTime cue = _heardLocally[kind];
            DateTime expires = cue + EventCapabilities.NominalDuration(kind);

            if (now >= expires)
            {
                _heardLocally.Remove(kind);
                continue;
            }

            if (_events.TryGetValue(kind, out var known))
            {
                if (!known.IsConfirmed
                    && known.StartedAtUtc >= cue.AddSeconds(-LocalTrustToleranceSeconds))
                    _events[kind] = known with { IsConfirmed = true };
            }
            else
            {
                _events[kind] = new CloudEventState(
                    kind, cue, expires, 1, true, Array.Empty<DateTime>(), LocalOnly: true);
            }
        }
    }

    private async Task ReportAsync(GameAudioDetection detection)
    {
        string? serverKey = _serverKey;
        if (serverKey == null || !CloudAuth.IsAuthenticated) return;

        try
        {
            if (PresenceRefresh != null)
            {
                try { await PresenceRefresh(); }
                catch (Exception ex) { Log($"[cloud-events] Presence refresh failed: {ex.Message}"); }
            }

            string json = await CallServerEventsAsync(
                HttpMethod.Post,
                new
                {
                    server_key = serverKey,
                    event_type = detection.EventType,
                    capture_mode = detection.CaptureMode,
                    score = detection.Score,
                    cue_started_at = detection.CueStartedAtUtc.ToString("o"),
                },
                routeSuffix: "report");

            using var doc = JsonDocument.Parse(json);
            string result = doc.RootElement.TryGetProperty("result", out var r) ? r.GetString() ?? "" : "";

            Log($"[cloud-events] Reported {detection.EventType} (score {detection.Score:F0}) → {result}");

            string? explanation = result switch
            {
                _ when result.StartsWith("rejected_wrong_server", StringComparison.Ordinal) =>
                    $"the account is registered on a different server than '{serverKey}'. " +
                    "Presence is uploaded from the team poll and can lag behind a server switch.",

                "rejected_stale_presence" =>
                    "the account has not checked in recently enough. This happens while AFK, " +
                    "because presence stops being refreshed.",

                "rejected_cloud_sync_off" =>
                    "cloud sync is switched off, so the app uploads no team data and the account " +
                    "cannot vouch for being in-game. Detection still works, reporting does not.",

                "rejected_not_in_game" =>
                    "this account is online on a different server. Being connected here in the app " +
                    "while playing elsewhere is exactly what this check exists to catch.",

                "rejected_too_soon"     => "the same event was already reported moments ago.",
                "rejected_still_active" => "that event is already running, so it cannot start again.",
                "rejected_rate_limited" => "too many reports from this account within the last hour.",
                "rejected_no_profile"   => "no cloud profile is linked to this account.",

                _ => null,
            };

            if (explanation != null)
                Log($"[cloud-events] Not recorded — {explanation}");

            if (result.StartsWith("accepted") || result.StartsWith("corroborated"))
                await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log($"[cloud-events] Report failed: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- HTTP helpers

    /// <summary>
    /// Routes server-events calls through the appropriate backend. On Platform mode this goes
    /// directly to <see cref="CloudApiClient"/>; on Supabase it goes through the edge function
    /// bridge.
    /// </summary>
    private static async Task<string> CallServerEventsAsync(
        HttpMethod method,
        object? payload,
        Dictionary<string, string>? queryParams = null,
        string? routeSuffix = null)
    {
        string function = routeSuffix != null ? $"server-events/{routeSuffix}" : "server-events";

        if (CloudBackend.UsePlatform)
        {
            string route = CloudBackend.MapEdgeFunctionToRoute(function, method.Method)
                           ?? function;
            return await CloudApiClient.CallApiAsync(route, method, null, payload, queryParams);
        }

        return await SupabaseAuthManager.CallEdgeFunctionAsync(function, method, payload, queryParams);
    }

    // ---------------------------------------------------------------- platform realtime (Pusher)

    private async Task SubscribePlatformAsync(string serverKey)
    {
        await _subscribeLock.WaitAsync();
        try
        {
            var channelKey = serverKey.Replace('.', '_');
            var channel = $"private-server-events.{channelKey}";

            if (_realtimeChannel == channel && RealtimeClient.Shared.IsSubscribed(channel))
                return;

            if (_realtimeChannel != null && _realtimeChannel != channel)
            {
                await RealtimeClient.Shared.UnsubscribeAsync(_realtimeChannel);
                _realtimeChannel = null;
            }

            AttachRealtimeHandler();
            _realtimeChannel = channel;

            RealtimeClient.Shared.Start();
            await RealtimeClient.Shared.SubscribeAsync(channel);
            Log($"[cloud-events] Subscribed to {channel} (platform).");
        }
        catch (Exception ex)
        {
            Log($"[cloud-events] Could not subscribe (platform): {ex.Message}");
            _realtimeChannel = null;
        }
        finally
        {
            _subscribeLock.Release();
        }
    }

    private void AttachRealtimeHandler()
    {
        if (_realtimeHandlerAttached) return;
        _realtimeHandlerAttached = true;

        RealtimeClient.Shared.EventReceived += (channel, eventName, data) =>
        {
            if (_realtimeChannel != null && channel != _realtimeChannel) return;

            try
            {
                _ = RefreshAsync();
            }
            catch (Exception ex)
            {
                Log($"[cloud-events] Platform realtime handler error: {ex.Message}");
            }
        };
    }

    private void UnsubscribePlatform()
    {
        var channel = _realtimeChannel;
        _realtimeChannel = null;

        if (channel != null)
        {
            try { _ = RealtimeClient.Shared.UnsubscribeAsync(channel); } catch { }
        }
    }

    // ---------------------------------------------------------------- supabase realtime (broadcast)

    /// <summary>
    /// Same mechanism team presence uses. Push rather than polling, and without putting
    /// server_events into the realtime publication.
    /// </summary>
    private async Task SubscribeSupabaseAsync(string serverKey)
    {
        await _subscribeLock.WaitAsync();
        try
        {
            var client = SupabaseAuthManager.Client;
            if (client?.Realtime == null) return;

            string channelName = $"server_events:{serverKey}";
            _channel = client.Realtime.Channel(channelName);
            _broadcast = _channel.Register<BaseBroadcast<JObject>>();
            _broadcast.AddBroadcastEventHandler((sender, args) =>
            {
                try
                {
                    _ = RefreshAsync();
                }
                catch (Exception ex)
                {
                    Log($"[cloud-events] Broadcast handler error: {ex.Message}");
                }
            });

            await _channel.Subscribe();
            Log($"[cloud-events] Subscribed to {channelName} (supabase).");
        }
        catch (Exception ex)
        {
            Log($"[cloud-events] Could not subscribe (supabase): {ex.Message}");
            _channel = null;
            _broadcast = null;
        }
        finally
        {
            _subscribeLock.Release();
        }
    }

    private void UnsubscribeSupabase()
    {
        if (_channel != null)
        {
            try { _channel.Unsubscribe(); } catch { }

            // Remove it from the client too, not just from here. The Realtime client keeps
            // channels in a registry keyed by topic, so Channel(sameName) later returns this
            // very object — and Register may only be called on a channel once. Reconnecting to
            // a server already visited this session would otherwise throw and stay broken
            // until restart. Same failure that killed clan chat on a server switch.
            try { SupabaseAuthManager.Client?.Realtime?.Remove(_channel); } catch { }
        }

        _channel = null;
        _broadcast = null;
    }

    private static void Log(string message)
    {
        try
        {
            var app = System.Windows.Application.Current;
            app?.Dispatcher.Invoke(() =>
            {
                if (app.MainWindow is Views.MainWindow window) window.AppendLog(message);
            });
        }
        catch { }
    }
}
