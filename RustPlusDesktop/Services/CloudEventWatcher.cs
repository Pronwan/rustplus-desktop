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
    IReadOnlyList<DateTime> RecentUtc)
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
/// </summary>
public sealed class CloudEventWatcher
{
    public static CloudEventWatcher Instance { get; } = new();

    /// <summary>Raised when an event appears or changes. Marshal to the UI yourself.</summary>
    public event Action<CloudEventState>? EventChanged;

    /// <summary>Raised whenever the overall picture changed and views should refresh.</summary>
    public event Action? StateRefreshed;

    private readonly object _gate = new();
    private readonly Dictionary<RustEventKind, CloudEventState> _events = new();
    private readonly SemaphoreSlim _subscribeLock = new(1, 1);

    private RealtimeChannel? _channel;
    private RealtimeBroadcast<BaseBroadcast<JObject>>? _broadcast;
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

        if (_serverKey == serverKey && _channel != null) return;
        Detach();
        _serverKey = serverKey;

        HookListener();
        await RefreshAsync(firstFetch: true);
        await SubscribeAsync(serverKey);
    }

    public void Detach()
    {
        UnhookListener();
        UnsubscribeBroadcast();
        _serverKey = null;
        lock (_gate) _events.Clear();
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
        if (serverKey == null || !SupabaseAuthManager.IsAuthenticated) return;

        try
        {
            string json = await SupabaseAuthManager.CallEdgeFunctionAsync(
                "server-events", HttpMethod.Get, null,
                new Dictionary<string, string> { ["server_key"] = serverKey });

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("events", out var events)) return;

            var parsed = new Dictionary<RustEventKind, CloudEventState>();
            foreach (var row in events.EnumerateArray())
            {
                var state = ParseRow(row);
                if (state != null) parsed[state.Kind] = state;
            }

            // Diff before overwriting. A refresh happens on every broadcast and on connect,
            // but an alert must only fire when something actually happened — otherwise
            // reconnecting would re-announce an event that started an hour ago.
            var changed = new List<CloudEventState>();
            lock (_gate)
            {
                foreach (var (kind, state) in parsed)
                {
                    bool known = _events.TryGetValue(kind, out var previous);

                    // A new occurrence (different start), or one that just crossed from a
                    // single unverified report to corroborated. Both are news; a mere
                    // confirmation count going up is not.
                    bool isNews = !known
                                  || previous!.StartedAtUtc != state.StartedAtUtc
                                  || (!previous.IsConfirmed && state.IsConfirmed);

                    if (isNews) changed.Add(state);
                }

                _events.Clear();
                foreach (var (kind, state) in parsed) _events[kind] = state;
            }

            // On the very first fetch after connecting, everything looks new — report it as a
            // refresh only, so joining a server does not replay its history into team chat.
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
        _ = ReportAsync(detection);
    }

    private async Task ReportAsync(GameAudioDetection detection)
    {
        string? serverKey = _serverKey;
        if (serverKey == null || !SupabaseAuthManager.IsAuthenticated) return;

        try
        {
            string json = await SupabaseAuthManager.CallEdgeFunctionAsync(
                "server-events/report", HttpMethod.Post,
                new
                {
                    server_key = serverKey,
                    event_type = detection.EventType,
                    capture_mode = detection.CaptureMode,
                    score = detection.Score,
                });

            using var doc = JsonDocument.Parse(json);
            string result = doc.RootElement.TryGetProperty("result", out var r) ? r.GetString() ?? "" : "";

            // Rejections are expected outcomes, not failures — reporting while not actually
            // in-game, or on a different server, is exactly what the backend is meant to
            // refuse. Worth logging, not worth alarming anyone.
            Log($"[cloud-events] Reported {detection.EventType} (score {detection.Score:F0}) → {result}");

            if (result.StartsWith("accepted") || result.StartsWith("corroborated"))
                await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log($"[cloud-events] Report failed: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- broadcast

    /// <summary>
    /// Same mechanism team presence uses. Push rather than polling, and without putting
    /// server_events into the realtime publication.
    /// </summary>
    private async Task SubscribeAsync(string serverKey)
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
                    // The payload carries the new state, but re-reading is cheap and keeps a
                    // single parsing path — a broadcast that arrives out of order or partially
                    // cannot then leave us with a state the backend never had.
                    _ = RefreshAsync();
                }
                catch (Exception ex)
                {
                    Log($"[cloud-events] Broadcast handler error: {ex.Message}");
                }
            });

            await _channel.Subscribe();
            Log($"[cloud-events] Subscribed to {channelName}.");
        }
        catch (Exception ex)
        {
            Log($"[cloud-events] Could not subscribe: {ex.Message}");
            _channel = null;
            _broadcast = null;
        }
        finally
        {
            _subscribeLock.Release();
        }
    }

    private void UnsubscribeBroadcast()
    {
        try { _channel?.Unsubscribe(); } catch { }
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
