using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RustPlusDesk.Services.Auth;

namespace RustPlusDesk.Services.Cloud
{
    /// <summary>
    /// Connection details for the realtime server, served by
    /// <c>GET /api/v1/broadcasting/config</c> so the WebSocket endpoint can move
    /// without shipping a new desktop build.
    /// </summary>
    public sealed class RealtimeConnectionInfo
    {
        public string WsUrl { get; init; } = "";
        public string AuthEndpoint { get; init; } = "";
        public string Key { get; init; } = "";
    }

    /// <summary>
    /// Minimal Pusher-protocol (v7) WebSocket client for the realtime service, covering
    /// exactly what the desktop needs: connect, authorize and subscribe to private
    /// channels with the bearer token, answer keepalives, and transparently
    /// reconnect with backoff — resubscribing whatever was requested.
    ///
    /// This deliberately avoids a Pusher SDK dependency: the protocol surface in use
    /// is small, and the auth step has to run through <see cref="CloudApiClient"/>
    /// so it inherits the client-version header and upgrade-required handling.
    /// </summary>
    public sealed class RealtimeClient
    {
        /// <summary>Process-wide client. Realtime is a single connection by design.</summary>
        public static RealtimeClient Shared { get; } = new();

        private const int MaxBackoffSeconds = 30;
        private const int ReceiveBufferSize = 16 * 1024;

        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly SemaphoreSlim _stateLock = new(1, 1);

        /// <summary>Channels the caller wants subscribed; the source of truth across reconnects.</summary>
        private readonly HashSet<string> _desiredChannels = new(StringComparer.Ordinal);

        /// <summary>Channels the server has confirmed on the current connection.</summary>
        private readonly HashSet<string> _confirmedChannels = new(StringComparer.Ordinal);

        /// <summary>
        /// How many people are in each presence channel.
        ///
        /// The protocol already sends this — the subscription reply carries the member list and
        /// every join and leave follows as its own frame — and it was being dropped with the rest
        /// of the internal frames. A room is the difference between an abandoned box and a place
        /// with people in it, so the one number that says which is worth keeping.
        /// </summary>
        private readonly Dictionary<string, int> _occupants = new(StringComparer.Ordinal);

        private RealtimeConnectionInfo? _connectionInfo;
        private ClientWebSocket? _socket;
        private CancellationTokenSource? _cts;
        private Task? _runLoop;
        private string? _socketId;
        private int _activityTimeoutSeconds = 120;
        private DateTime _lastInboundUtc = DateTime.UtcNow;

        /// <summary>
        /// Raised for every application event (channel, event name, decoded payload).
        /// Pusher protocol frames are handled internally and never surface here.
        /// </summary>
        public event Action<string, string, JObject>? EventReceived;

        /// <summary>
        /// Raised with the channel and its new occupant count whenever a presence channel's
        /// membership changes. Fires on the receive loop, so handlers that touch controls must
        /// marshal for themselves.
        /// </summary>
        public event Action<string, int>? PresenceChanged;

        /// <summary>
        /// How many people are in a presence channel right now, or 0 when it is not subscribed or
        /// is not a presence channel. Zero and "we do not know yet" are the same answer here:
        /// both mean there is no count worth showing.
        /// </summary>
        public int OccupantCount(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel)) return 0;
            var name = Normalize(channel);
            lock (_occupants)
                return _occupants.TryGetValue(name, out var count) ? count : 0;
        }

        /// <summary>True once the connection is established and a socket id is known.</summary>
        public bool IsConnected => _socket?.State == WebSocketState.Open && _socketId != null;

        /// <summary>True when a channel is subscribed and confirmed on the live connection.</summary>
        public bool IsSubscribed(string channel)
        {
            lock (_confirmedChannels)
                return _confirmedChannels.Contains(channel);
        }

        /// <summary>
        /// Start the connection loop if it is not already running. Safe to call repeatedly.
        /// </summary>
        public void Start()
        {
            if (!CloudAuth.IsAuthenticated) return;
            if (_runLoop is { IsCompleted: false }) return;

            _cts = new CancellationTokenSource();
            _runLoop = Task.Run(() => RunAsync(_cts.Token));
        }

        /// <summary>Tear down the connection and forget all channels.</summary>
        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }

            lock (_desiredChannels) _desiredChannels.Clear();
            lock (_confirmedChannels) _confirmedChannels.Clear();
            ClearOccupants();

            _socketId = null;
            _connectionInfo = null;

            var socket = _socket;
            _socket = null;
            try { socket?.Abort(); socket?.Dispose(); } catch { }
        }

        /// <summary>
        /// Request a private channel (name without the <c>private-</c> prefix, e.g.
        /// <c>team-sync.{id}</c>). Subscribes immediately when connected, otherwise on
        /// the next successful connect.
        /// </summary>
        public async Task SubscribeAsync(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel)) return;
            if (!CloudAuth.IsAuthenticated) return;

            var name = Normalize(channel);
            lock (_desiredChannels) _desiredChannels.Add(name);

            Start();

            if (IsConnected)
                await TrySubscribeAsync(name, CancellationToken.None);
        }

        /// <summary>Drop a channel and stop resubscribing it on reconnect.</summary>
        public async Task UnsubscribeAsync(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel)) return;

            var name = Normalize(channel);
            lock (_desiredChannels) _desiredChannels.Remove(name);
            lock (_confirmedChannels) _confirmedChannels.Remove(name);
            SetOccupants(name, 0);
            lock (_occupants) _occupants.Remove(name);

            if (_socket?.State == WebSocketState.Open)
            {
                await SendAsync(new JObject
                {
                    ["event"] = "pusher:unsubscribe",
                    ["data"] = new JObject { ["channel"] = name },
                }, CancellationToken.None);
            }
        }

        /// <summary>
        /// Private channels carry the `private-` prefix on the wire and presence channels the
        /// `presence-` one. A name that already says which kind it is passes through; anything
        /// else is private, which every channel here was until the public room arrived.
        /// </summary>
        private static string Normalize(string channel) =>
            channel.StartsWith("private-", StringComparison.Ordinal)
            || channel.StartsWith("presence-", StringComparison.Ordinal)
                ? channel
                : "private-" + channel;

        private async Task RunAsync(CancellationToken ct)
        {
            var attempt = 0;

            while (!ct.IsCancellationRequested)
            {
                if (!CloudAuth.IsAuthenticated)
                    break;

                try
                {
                    await ConnectAndPumpAsync(ct);
                    attempt = 0; // A clean close still warrants an immediate retry.
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!CloudAuth.IsAuthenticated || ex.Message.Contains("401") || ex.Message.Contains("Unauthenticated") || ex.Message.Contains("not signed in"))
                        break;

                    Log($"[Realtime/Error] Connection failed: {ex.Message}");
                    attempt++;
                }
                finally
                {
                    _socketId = null;
                    lock (_confirmedChannels) _confirmedChannels.Clear();
                    ClearOccupants();
                }

                if (ct.IsCancellationRequested || !CloudAuth.IsAuthenticated) break;

                // Exponential backoff with jitter so a restarted server does not get
                // hit by every client at the same instant.
                var seconds = Math.Min(MaxBackoffSeconds, Math.Pow(2, Math.Min(attempt, 5)));
                var delay = TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));

                try { await Task.Delay(delay, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task ConnectAndPumpAsync(CancellationToken ct)
        {
            var info = await EnsureConnectionInfoAsync();
            if (info == null || string.IsNullOrWhiteSpace(info.WsUrl))
                throw new InvalidOperationException("realtime connection details unavailable.");

            var uri = new Uri($"{info.WsUrl}?protocol=7&client=rustplusdesk&version={Helpers.VersionHelper.GetClientVersion()}");

            using var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            _socket = socket;

            await socket.ConnectAsync(uri, ct);
            Log("[Realtime] WebSocket connected.");

            _lastInboundUtc = DateTime.UtcNow;

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var keepalive = Task.Run(() => KeepaliveAsync(linked.Token), linked.Token);

            try
            {
                await ReceiveLoopAsync(socket, linked.Token);
            }
            finally
            {
                linked.Cancel();
                try { await keepalive; } catch { }
                _socket = null;
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
        {
            var buffer = new byte[ReceiveBufferSize];
            var message = new StringBuilder();

            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Log($"[Realtime] Server closed the connection: {result.CloseStatus} {result.CloseStatusDescription}");
                    return;
                }

                message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage) continue;

                var raw = message.ToString();
                message.Clear();
                _lastInboundUtc = DateTime.UtcNow;

                try
                {
                    await HandleFrameAsync(raw, ct);
                }
                catch (Exception ex)
                {
                    Log($"[Realtime/Error] Frame handling failed: {ex.Message}");
                }
            }
        }

        private async Task HandleFrameAsync(string raw, CancellationToken ct)
        {
            var frame = JObject.Parse(raw);
            var eventName = frame["event"]?.ToString();
            if (string.IsNullOrEmpty(eventName)) return;

            var channel = frame["channel"]?.ToString() ?? "";
            var data = DecodeData(frame["data"]);

            switch (eventName)
            {
                case "pusher:connection_established":
                    _socketId = data?["socket_id"]?.ToString();
                    if (data?["activity_timeout"] != null)
                        _activityTimeoutSeconds = data["activity_timeout"]!.Value<int>();

                    Log($"[Realtime] Connection established (socket {_socketId}).");
                    await ResubscribeAllAsync(ct);
                    break;

                case "pusher:ping":
                    await SendAsync(new JObject { ["event"] = "pusher:pong", ["data"] = new JObject() }, ct);
                    break;

                case "pusher:pong":
                    break;

                case "pusher_internal:subscription_succeeded":
                    lock (_confirmedChannels) _confirmedChannels.Add(channel);
                    Log($"[Realtime] Subscribed to {channel}.");
                    // A presence channel answers with the whole member list. Take the count from it
                    // rather than counting joins from zero, which would be wrong for everyone who
                    // was already in the room before we arrived.
                    if (channel.StartsWith("presence-", StringComparison.Ordinal))
                    {
                        var count = data?["presence"]?["count"]?.Value<int>();
                        if (count.HasValue) SetOccupants(channel, count.Value);
                    }
                    break;

                case "pusher_internal:member_added":
                    AdjustOccupants(channel, +1);
                    break;

                case "pusher_internal:member_removed":
                    AdjustOccupants(channel, -1);
                    break;

                case "pusher:error":
                    Log($"[Realtime/Error] {data?["code"]}: {data?["message"]}");
                    break;

                default:
                    if (eventName.StartsWith("pusher", StringComparison.Ordinal)) break;
                    if (data != null)
                        EventReceived?.Invoke(channel, eventName, data);
                    break;
            }
        }

        /// <summary>Records an authoritative count and tells anybody watching.</summary>
        private void SetOccupants(string channel, int count)
        {
            if (count < 0) count = 0;

            lock (_occupants)
            {
                if (_occupants.TryGetValue(channel, out var existing) && existing == count) return;
                _occupants[channel] = count;
            }

            PresenceChanged?.Invoke(channel, count);
        }

        /// <summary>
        /// Moves a count by one join or leave.
        ///
        /// Ignored for a channel we hold no count for: the subscription reply is what establishes
        /// the number, and counting from an assumed zero would report a busy room as empty until
        /// the next reconnect.
        /// </summary>
        private void AdjustOccupants(string channel, int delta)
        {
            int updated;

            lock (_occupants)
            {
                if (!_occupants.TryGetValue(channel, out var current)) return;
                updated = Math.Max(0, current + delta);
                if (updated == current) return;
                _occupants[channel] = updated;
            }

            PresenceChanged?.Invoke(channel, updated);
        }

        /// <summary>
        /// Forgets every count.
        ///
        /// Called when the connection drops: the numbers describe a membership we are no longer
        /// party to, and a stale count is worse than none — it reads as current.
        /// </summary>
        private void ClearOccupants()
        {
            string[] channels;

            lock (_occupants)
            {
                if (_occupants.Count == 0) return;
                channels = _occupants.Keys.ToArray();
                _occupants.Clear();
            }

            foreach (var channel in channels)
                PresenceChanged?.Invoke(channel, 0);
        }

        /// <summary>
        /// The protocol carries `data` as a JSON-encoded string, but some frames use a
        /// bare object. Accept both rather than assuming one.
        /// </summary>
        private static JObject? DecodeData(JToken? token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token is JObject obj) return obj;

            var text = token.ToString();
            if (string.IsNullOrWhiteSpace(text)) return null;

            try { return JObject.Parse(text); }
            catch { return null; }
        }

        private async Task ResubscribeAllAsync(CancellationToken ct)
        {
            string[] channels;
            lock (_desiredChannels) channels = _desiredChannels.ToArray();

            foreach (var channel in channels)
                await TrySubscribeAsync(channel, ct);
        }

        private async Task TrySubscribeAsync(string channel, CancellationToken ct)
        {
            var socketId = _socketId;
            if (socketId == null || _socket?.State != WebSocketState.Open) return;

            try
            {
                var auth = await AuthorizeAsync(channel, socketId);
                if (auth == null)
                {
                    Log($"[Realtime/Error] Channel auth denied for {channel}.");
                    return;
                }

                var data = new JObject { ["auth"] = auth.Auth, ["channel"] = channel };

                // Presence channels sign the member payload together with the channel name, so
                // the same string has to travel back or the server rejects the signature.
                if (auth.ChannelData != null)
                    data["channel_data"] = auth.ChannelData;

                await SendAsync(new JObject
                {
                    ["event"] = "pusher:subscribe",
                    ["data"] = data,
                }, ct);
            }
            catch (Exception ex)
            {
                Log($"[Realtime/Error] Subscribe to {channel} failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Exchange the bearer token for a Pusher channel signature. The
        /// framework's own /broadcasting/auth is session+CSRF gated, so this uses the
        /// bearer-friendly route under /api/v1.
        /// </summary>
        private static async Task<ChannelAuth?> AuthorizeAsync(string channel, string socketId)
        {
            var body = await CloudApiClient.CallApiAsync(
                "broadcasting/auth",
                HttpMethod.Post,
                payload: new { socket_id = socketId, channel_name = channel });

            if (string.IsNullOrWhiteSpace(body)) return null;

            var json = JObject.Parse(body);
            var auth = json["auth"]?.ToString();
            if (string.IsNullOrWhiteSpace(auth)) return null;

            // A private channel answers with a signature alone; a presence channel adds the
            // member payload that signature covers. It arrives already JSON-encoded and is taken
            // verbatim rather than re-serialised - a re-encode that reorders one key would break
            // the signature.
            var channelData = json["channel_data"] switch
            {
                null => null,
                { Type: JTokenType.Null } => null,
                { Type: JTokenType.String } token => token.ToString(),
                var token => token.ToString(Newtonsoft.Json.Formatting.None),
            };

            return new ChannelAuth(auth!, channelData);
        }

        /// <summary>What the auth endpoint hands back for a single channel.</summary>
        private sealed record ChannelAuth(string Auth, string? ChannelData);

        private async Task<RealtimeConnectionInfo?> EnsureConnectionInfoAsync()
        {
            if (!CloudAuth.IsAuthenticated) return null;
            if (_connectionInfo != null) return _connectionInfo;

            await _stateLock.WaitAsync();
            try
            {
                if (!CloudAuth.IsAuthenticated) return null;
                if (_connectionInfo != null) return _connectionInfo;

                var body = await CloudApiClient.CallApiAsync("broadcasting/config", HttpMethod.Get);
                var data = JObject.Parse(body)["data"];
                if (data == null) return null;

                _connectionInfo = new RealtimeConnectionInfo
                {
                    WsUrl = data["ws_url"]?.ToString() ?? "",
                    AuthEndpoint = data["auth_endpoint"]?.ToString() ?? "",
                    Key = data["key"]?.ToString() ?? "",
                };

                return _connectionInfo;
            }
            finally
            {
                _stateLock.Release();
            }
        }

        /// <summary>
        /// Pusher expects the client to ping once the connection has been idle for the
        /// server-advertised activity timeout; silence past that means a dead link.
        /// </summary>
        private async Task KeepaliveAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(10), ct); }
                catch (OperationCanceledException) { return; }

                var idle = DateTime.UtcNow - _lastInboundUtc;
                if (idle < TimeSpan.FromSeconds(_activityTimeoutSeconds)) continue;

                // Past twice the timeout without a reply the socket is not coming back;
                // aborting drops out of the receive loop and triggers a reconnect.
                if (idle > TimeSpan.FromSeconds(_activityTimeoutSeconds * 2))
                {
                    Log("[Realtime] Keepalive timed out; reconnecting.");
                    try { _socket?.Abort(); } catch { }
                    return;
                }

                try { await SendAsync(new JObject { ["event"] = "pusher:ping", ["data"] = new JObject() }, ct); }
                catch { return; }
            }
        }

        private async Task SendAsync(JObject frame, CancellationToken ct)
        {
            var socket = _socket;
            if (socket?.State != WebSocketState.Open) return;

            var bytes = Encoding.UTF8.GetBytes(frame.ToString(Newtonsoft.Json.Formatting.None));

            await _sendLock.WaitAsync(ct);
            try
            {
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private static void Log(string message) => SupabaseAuthManager.AppendLog(message);
    }
}
