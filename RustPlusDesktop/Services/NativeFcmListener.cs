using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services
{
    /// <summary>
    /// Node-free FCM pairing listener built on RustPlusApi.Fcm's MCS socket.
    ///
    /// Replaces the bundled Node <c>fcm-listen</c> process (and its ~900 lines of stdout
    /// regex parsing) with an in-process socket that yields fully-typed <see cref="FcmMessage"/>
    /// objects. We parse the raw <see cref="FcmMessage"/> ourselves rather than using the
    /// library's high-level typed events, because those are lossy for our needs: the typed
    /// AlarmNotification carries no ip/port, and its pairing PlayerToken is an <c>int</c> that
    /// cannot hold a Rust+ token. The raw <see cref="Body"/> has everything (Ip, Port,
    /// PlayerToken as string, EntityId, Desc, …).
    ///
    /// Drop-in for <see cref="IPairingListener"/>, so it maps to the same app events the Node
    /// listener raises (<see cref="Paired"/>, <see cref="AlarmReceived"/>,
    /// <see cref="ChatReceived"/>, <see cref="OfflineDeathReceived"/>,
    /// <see cref="ServerInfoReceived"/>). Registration is unchanged — it reuses the credentials
    /// in <c>rustplusjs-config.json</c>, and runs native registration if none exist yet.
    /// </summary>
    public sealed class NativeFcmListener : IPairingListener
    {
        public event EventHandler<PairingPayload>? Paired;
        public event EventHandler? Listening;
        public event EventHandler? RegistrationCompleted;
        public event EventHandler? Stopped;
        public event EventHandler<string>? Failed;
        public event EventHandler<AlarmNotification>? AlarmReceived;
        public event EventHandler<TeamChatMessage>? ChatReceived;
        public event EventHandler<OfflineDeathNotification>? OfflineDeathReceived;
        // Concrete-only, mirrors PairingListenerRealProcess (not on IPairingListener yet).
        public event EventHandler<PairingPayload>? ServerInfoReceived;

        private static readonly Regex DeathTitleRegex = new(
            @"^(?:You were killed by|Du wurdest getötet von)\s+(?<attacker>.+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly Action<string> _log;
        private readonly object _gate = new();

        private CancellationTokenSource? _cts;
        private RawFcmClient? _client;
        private List<string>? _persistentIds;
        private volatile bool _running;
        private int _reconnecting;

        // De-duplicates the same pairing bounced twice in quick succession, matching the
        // Node listener's 20-second window.
        private string? _lastPairKey;
        private DateTime _lastPairAt;

        public NativeFcmListener(Action<string> log) => _log = log;

        public bool IsRunning => _running;

        public bool IsConfigured => File.Exists(ConfigPath) && new FileInfo(ConfigPath).Length > 50;

        private static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RustPlusDesk", "rustplusjs-config.json");

        private static string PersistentIdPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RustPlusDesk", "fcm-persistent-ids.json");

        public async Task StartAsync(CancellationToken ct = default)
        {
            if (_running)
            {
                _log("[fcm-native] Listener already running.");
                return;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

            // Register if we have no credentials yet (native path; the Node listener remains
            // available for the full browser-fallback registration flow).
            if (!IsConfigured)
            {
                _log("[fcm-native] No FCM config found — running native registration first …");
                bool ok = await NativeFcmRegistrationService.TryRegisterAsync(ConfigPath, _log, ct: _cts.Token)
                    .ConfigureAwait(false);
                if (!ok)
                {
                    _log("[fcm-native] ❌ Registration failed. Use the Node listener to register, then retry.");
                    Failed?.Invoke(this, "native-registration-failed");
                    Stopped?.Invoke(this, EventArgs.Empty);
                    return;
                }

                var issuedAt = DateTime.Now;
                var expiresAt = issuedAt.AddDays(15);
                TrackingService.FcmIssuedAt = issuedAt;
                TrackingService.FcmExpiresAt = expiresAt;
                EnrichFcmConfig(issuedAt, expiresAt, TrackingService.SteamId64);
                RegistrationCompleted?.Invoke(this, EventArgs.Empty);
            }

            if (!TryLoadCredentials(out var credentials))
            {
                _log("[fcm-native] ❌ Could not read gcm.androidId/securityToken from config.");
                Failed?.Invoke(this, "invalid-config");
                Stopped?.Invoke(this, EventArgs.Empty);
                return;
            }

            _persistentIds = LoadPersistentIds();
            await ConnectAsync(credentials!, _cts.Token).ConfigureAwait(false);
        }

        private async Task ConnectAsync(Credentials credentials, CancellationToken ct)
        {
            try
            {
                var client = new RawFcmClient(credentials, _persistentIds, HandleMessage);
                client.Connected += (_, __) =>
                {
                    _running = true;
                    _log("[fcm-native] Listening for FCM Notifications");
                    Listening?.Invoke(this, EventArgs.Empty);
                };
                client.ErrorOccurred += (_, ex) =>
                {
                    if (IsSocketResetOrDisconnect(ex))
                    {
                        _log("[fcm-native] Connection reset by remote host.");
                    }
                    else
                    {
                        _log("[fcm-native:err] " + ex.Message);
                    }
                };
                client.PersistentIdReceived += (_, __) => SavePersistentIds();
                client.Disconnected += (_, __) => OnSocketDown();
                client.SocketClosed += (_, __) => OnSocketDown();

                lock (_gate) _client = client;

                await client.ConnectAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _running = false;
                Stopped?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _running = false;
                if (IsSocketResetOrDisconnect(ex))
                {
                    _log("[fcm-native] Connection dropped by remote host.");
                }
                else
                {
                    _log("[fcm-native:err] connect failed: " + ex.Message);
                    Failed?.Invoke(this, ex.Message);
                }
                OnSocketDown();
            }
        }

        // Bounded auto-reconnect, mirroring the Node listener's restart-on-exit behaviour.
        private void OnSocketDown()
        {
            var wasRunning = _running;
            _running = false;
            if (wasRunning) Stopped?.Invoke(this, EventArgs.Empty);

            var cts = _cts;
            if (cts is null || cts.IsCancellationRequested) return;

            // Debounce so Disconnected + SocketClosed don't spawn duplicate reconnect tasks
            if (Interlocked.CompareExchange(ref _reconnecting, 1, 0) != 0) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(3000, cts.Token).ConfigureAwait(false);
                    if (cts.IsCancellationRequested) return;
                    if (!TryLoadCredentials(out var creds)) return;

                    RawFcmClient? oldClient;
                    lock (_gate)
                    {
                        oldClient = _client;
                        _client = null;
                    }
                    if (oldClient != null)
                    {
                        try { await oldClient.DisposeAsync().ConfigureAwait(false); } catch { }
                    }

                    _log("[fcm-native] Reconnecting …");
                    await ConnectAsync(creds!, cts.Token).ConfigureAwait(false);
                }
                catch { /* cancelled */ }
                finally
                {
                    Interlocked.Exchange(ref _reconnecting, 0);
                }
            });
        }

        public async Task StopAsync()
        {
            try { _cts?.Cancel(); } catch { }
            Interlocked.Exchange(ref _reconnecting, 0);

            RawFcmClient? client;
            lock (_gate) { client = _client; _client = null; }

            if (client != null)
            {
                try { await client.DisposeAsync().ConfigureAwait(false); } catch { }
            }

            SavePersistentIds();

            var wasRunning = _running;
            _running = false;
            _cts = null;
            if (wasRunning) Stopped?.Invoke(this, EventArgs.Empty);
            _log("[fcm-native] Listener stopped.");
        }

        private static bool IsSocketResetOrDisconnect(Exception? ex)
        {
            while (ex != null)
            {
                if (ex is System.Net.Sockets.SocketException se)
                {
                    if (se.SocketErrorCode is System.Net.Sockets.SocketError.ConnectionReset
                        or System.Net.Sockets.SocketError.ConnectionAborted
                        or System.Net.Sockets.SocketError.Shutdown
                        or System.Net.Sockets.SocketError.TimedOut
                        or System.Net.Sockets.SocketError.NetworkReset)
                    {
                        return true;
                    }
                }

                if (ex is IOException or TimeoutException or OperationCanceledException)
                {
                    var msg = ex.Message;
                    if (msg.Contains("transport connection", StringComparison.OrdinalIgnoreCase)
                        || msg.Contains("closed", StringComparison.OrdinalIgnoreCase)
                        || msg.Contains("geschlossen", StringComparison.OrdinalIgnoreCase)
                        || msg.Contains("reset", StringComparison.OrdinalIgnoreCase)
                        || msg.Contains("10054", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                ex = ex.InnerException;
            }

            return false;
        }

        // ---- Dispatch: one uniform parse of the full FcmMessage into app events ----

        private void HandleMessage(FcmMessage message)
        {
            try
            {
                var data = message.Data;
                var body = data.Body;

                // Offline death is title-driven and not tied to a single channel.
                var deathMatch = DeathTitleRegex.Match(data.Title ?? string.Empty);
                if (deathMatch.Success)
                {
                    var attacker = deathMatch.Groups["attacker"].Value.Trim().Trim('\'', '"');
                    var server = !string.IsNullOrWhiteSpace(body?.Name) ? body!.Name
                               : (!string.IsNullOrWhiteSpace(data.Message) ? data.Message : "-");
                    OfflineDeathReceived?.Invoke(this,
                        new OfflineDeathNotification(DateTime.Now, server, attacker,
                            NullIfEmpty(body?.Ip), NonZero(body?.Port)));
                    _log($"[fcm-native] Offline Death | {server} | {attacker}");
                    return;
                }

                switch ((data.ChannelId ?? string.Empty).ToLowerInvariant())
                {
                    case "pairing":
                        HandlePairing(message, body);
                        break;

                    case "alarm":
                        var title = string.IsNullOrWhiteSpace(data.Title) ? null : data.Title;
                        var msg = !string.IsNullOrWhiteSpace(data.Message) ? data.Message : (title ?? "");
                        var device = (body?.EntityName ?? "Alarm")
                                     + (body?.EntityId is { } eid ? $"#{eid}" : "");
                        AlarmReceived?.Invoke(this, new AlarmNotification(
                            DateTime.Now,
                            body?.Name ?? "-",
                            device,
                            (uint?)body?.EntityId,
                            msg,
                            NullIfEmpty(body?.Ip),
                            NonZero(body?.Port),
                            title,
                            NullIfEmpty(message.PersistentId)));
                        _log($"[fcm-native] Alarm | {body?.Name ?? "-"} | {device} | \"{msg}\"");
                        break;

                    case "chat":
                        var author = string.IsNullOrWhiteSpace(data.Title) ? "Team" : data.Title;
                        var text = data.Message ?? string.Empty;
                        ChatReceived?.Invoke(this, new TeamChatMessage(
                            DateTime.Now, author, 0, text,
                            NullIfEmpty(body?.Ip), NonZero(body?.Port)));
                        break;

                    default:
                        _log($"[fcm-native] Unhandled channel '{data.ChannelId}' | title=\"{data.Title}\"");
                        break;
                }

                // Server description can ride along on any message that carries a Body.Desc.
                if (!string.IsNullOrWhiteSpace(body?.Desc))
                {
                    ServerInfoReceived?.Invoke(this, new PairingPayload
                    {
                        Host = body!.Ip ?? string.Empty,
                        Port = body.Port,
                        ServerName = body.Name,
                        ServerDescription = body.Desc!.Trim(),
                    });
                }
            }
            catch (Exception ex)
            {
                _log("[fcm-native:err] parse error: " + ex.Message);
            }
        }

        private void HandlePairing(FcmMessage message, Body? body)
        {
            if (body is null || string.IsNullOrWhiteSpace(body.Ip) || string.IsNullOrWhiteSpace(body.PlayerToken))
                return;

            string? kind = body.EntityType switch
            {
                1 => "SmartSwitch",
                2 => "SmartAlarm",
                3 => "StorageMonitor",
                _ => null,
            } ?? (string.IsNullOrWhiteSpace(body.Type) ? null : body.Type);

            var payload = new PairingPayload
            {
                Host = body.Ip,
                Port = body.Port,
                ServerName = string.IsNullOrWhiteSpace(body.Name) ? null : body.Name,
                ServerDescription = string.IsNullOrWhiteSpace(body.Desc) ? null : body.Desc,
                SteamId64 = body.PlayerId.ToString(CultureInfo.InvariantCulture),
                PlayerToken = body.PlayerToken,
                EntityId = (uint?)body.EntityId,
                EntityName = string.IsNullOrWhiteSpace(body.EntityName) ? null : body.EntityName,
                EntityType = kind,
            };

            var key = $"{payload.Host}:{payload.Port}|{payload.SteamId64}|{payload.PlayerToken}|{payload.EntityId}";
            if (_lastPairKey == key && (DateTime.UtcNow - _lastPairAt).TotalSeconds < 20)
            {
                _log("[fcm-native] duplicate pairing ignored.");
                return;
            }
            _lastPairKey = key;
            _lastPairAt = DateTime.UtcNow;

            Paired?.Invoke(this, payload);
            _log($"[fcm-native] Pairing → {(payload.ServerName ?? payload.Host)}:{payload.Port}"
                 + (payload.EntityId.HasValue ? $"  // Entity {payload.EntityId}" : ""));
        }

        // ---- Helpers ----

        private bool TryLoadCredentials(out Credentials? credentials)
        {
            credentials = null;
            try
            {
                var json = File.ReadAllText(ConfigPath);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("fcm_credentials", out var fcm) ||
                    !fcm.TryGetProperty("gcm", out var gcm))
                    return false;

                var androidId = ReadUlong(gcm, "androidId");
                var securityToken = ReadUlong(gcm, "securityToken");
                if (androidId == 0 || securityToken == 0) return false;

                credentials = new Credentials
                {
                    Gcm = new Gcm { AndroidId = androidId, SecurityToken = securityToken },
                };
                return true;
            }
            catch (Exception ex)
            {
                _log("[fcm-native:err] read config: " + ex.Message);
                return false;
            }
        }

        private static ulong ReadUlong(JsonElement obj, string name)
        {
            if (!obj.TryGetProperty(name, out var v)) return 0;
            return v.ValueKind switch
            {
                JsonValueKind.String => ulong.TryParse(v.GetString(), out var s) ? s : 0,
                JsonValueKind.Number => v.TryGetUInt64(out var n) ? n : 0,
                _ => 0,
            };
        }

        private List<string> LoadPersistentIds()
        {
            try
            {
                if (File.Exists(PersistentIdPath))
                    return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(PersistentIdPath))
                           ?? new List<string>();
            }
            catch { }
            return new List<string>();
        }

        private void SavePersistentIds()
        {
            try
            {
                var ids = _persistentIds;
                if (ids is null) return;
                // Keep the file bounded; only the most recent ids matter for dedup.
                List<string> snapshot;
                lock (ids) snapshot = new List<string>(ids);
                if (snapshot.Count > 1000)
                    snapshot = snapshot.GetRange(snapshot.Count - 1000, 1000);
                File.WriteAllText(PersistentIdPath, JsonSerializer.Serialize(snapshot));
            }
            catch { }
        }

        private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
        private static int? NonZero(int? p) => p is > 0 ? p : null;

        /// <summary>
        /// Reads rustplusjs-config.json, injects issue_date / expiry_date / steam_id, and writes
        /// it back — same shape the Node path persists, so the rest of the app is unaffected.
        /// </summary>
        private void EnrichFcmConfig(DateTime issuedAt, DateTime expiresAt, string? steamId)
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;
                var json = File.ReadAllText(ConfigPath);
                using var doc = JsonDocument.Parse(json);
                using var ms = new MemoryStream();
                using var wtr = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });
                wtr.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject()) prop.WriteTo(wtr);
                wtr.WriteString("steam_id", steamId ?? "");
                wtr.WriteString("issue_date", issuedAt.ToString("o"));
                wtr.WriteString("expiry_date", expiresAt.ToString("o"));
                wtr.WriteEndObject();
                wtr.Flush();
                File.WriteAllBytes(ConfigPath, ms.ToArray());
                _ = FcmSyncService.SyncFcmCredentialsAsync();
            }
            catch (Exception ex)
            {
                _log($"[fcm-native] could not enrich config: {ex.Message}");
            }
        }

        /// <summary>
        /// Subclasses RustPlusFcm so we receive the fully-typed <see cref="FcmMessage"/> directly
        /// in <see cref="ParseNotification"/>, bypassing the library's lossy high-level events.
        /// </summary>
        private sealed class RawFcmClient : RustPlusFcm
        {
            private readonly Action<FcmMessage> _onMessage;

            public RawFcmClient(Credentials credentials, ICollection<string>? persistentIds, Action<FcmMessage> onMessage)
                : base(credentials, persistentIds) => _onMessage = onMessage;

            protected override void ParseNotification(FcmMessage message) => _onMessage(message);
        }
    }
}
