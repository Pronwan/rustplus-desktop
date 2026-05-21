using RustPlusDesk.Models;
using RustPlusDesk.Helpers;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.IO.Compression;
using System.Threading.Tasks;

namespace RustPlusDesk.Services
{

    /// <summary>
    /// Startet das rustplus.js-CLI (fcm-register/fcm-listen) als Hintergrundprozess
    /// und leitet eingehende Pairing-Payloads an die App weiter.
    /// </summary>
    public class PairingListenerRealProcess : IPairingListener
    {
        public event EventHandler<PairingPayload>? Paired;
        private static readonly Regex Ansi = new(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);
        private static readonly Regex RustUrl = new(@"rustplus://[^\s'\"">]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // in PairingListenerRealProcess (Feldebene)
        private string? _lastPairKey;
        private DateTime _lastPairAt;

        // key/value-Zeilen (z.B. { key: 'gcm.notification.body', value: 'Your base is under attack!' })
        private static readonly Regex KvLine = new(@"\{\s*key:\s*'(?<k>[^']+)'\s*,\s*value:\s*'(?<v>.*)'\s*\}", RegexOptions.Compiled);
        public event EventHandler<AlarmNotification>? AlarmReceived;
        // body-JSON in der gleichen Zeile (klassisch)
        private static readonly Regex BodyJson =
    new(@"value:\s*(?:'|`)(?<json>\{.*?\})(?:'|`)", RegexOptions.Compiled | RegexOptions.Singleline);

        // message-Zeilen (körper des Alarms)
        private static readonly Regex MsgLine = new(@"\{\s*key:\s*'(?:message|gcm\.notification\.body)'\s*,\s*value:\s*'(?<msg>[^']+)'\s*\}", RegexOptions.Compiled);
        private readonly Action<string> _log;
        private CancellationTokenSource? _cts;
        private Process? _listenProc;
        // Zusatz-Regex: fängt sowohl { key: 'message', ... } als auch { key: 'gcm.notification.body', ... }


        // Kontext für eine anstehende Alarm-Zeile

        private (string? server, string? entityName, uint? entityId)? _pendingAlarm;
        private string? _pendingAlarmMsg;
        private DateTime? _pendingAlarmMsgTs;

        private bool _chatBundleOpen;
        private string? _pendingChatMsg;
        private string? _pendingChatTitle;
        private DateTime? _pendingChatTs;
        public PairingListenerRealProcess(Action<string> log) => _log = log;
        public event EventHandler? Listening;                 // wenn "Listening for FCM Notifications" erscheint
        public event EventHandler? Stopped;
        public event EventHandler<string>? Failed;            // bei erkennbaren Fehlerzeilen

        public event EventHandler<string>? Status;            // optional, für UI-Text
        public event EventHandler? RegistrationCompleted;
        private volatile bool _running;
        public bool IsRunning => _running;
        public bool IsConfigured => File.Exists(ConfigPath) && new FileInfo(ConfigPath).Length > 50;



        private string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RustPlusDesk", "rustplusjs-config.json");
        public event EventHandler<TeamChatMessage>? ChatReceived;

        private void TryFlushChat()
        {
            if (!_chatBundleOpen || string.IsNullOrEmpty(_pendingChatMsg)) return;
            var author = string.IsNullOrWhiteSpace(_pendingChatTitle) ? "Team" : _pendingChatTitle!;
            ChatReceived?.Invoke(this,
                 new TeamChatMessage(_pendingChatTs ?? DateTime.Now, author, 0, _pendingChatMsg!));
            _pendingChatMsg = null;
            _pendingChatTitle = null;
            _pendingChatTs = null;
        }



        public async Task StartAsync(CancellationToken ct = default)
        {
            Status?.Invoke(this, "starting");
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            if (_running && _listenProc != null && !_listenProc.HasExited)
            {
                _log("Listener already running.");
                return;
            }

            var node = RuntimeHelper.FindBundledNode()
                ?? throw new InvalidOperationException(RuntimeHelper.GetNodeNotFoundMessage());

            var cli = RuntimeHelper.ResolveCliEntry(out var wd)
                ?? throw new InvalidOperationException("rustplus-cli not found (rustplus-cli.zip missing or extraction failed).");

            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

            // 1) Registrierung nur, wenn keine/zu kleine Config
            if (!File.Exists(ConfigPath) || new FileInfo(ConfigPath).Length < 50)
            {
                _log("Starting one time registration (fcm-register) …");
                _log("IMPORTANT: Log into the SAME Steam account in your browser that you use in the Rust+ app!");
                int regExitCode = await RunCliWithLoggingAsync(
                    node,
                    $"\"{cli}\" fcm-register --config-file=\"{ConfigPath}\"",
                    wd,
                    "fcm-register",
                    _cts.Token
                );
                
                if (regExitCode != 0)
                {
                    var edge = FindEdge();
                    if (edge != null)
                    {
                        var env = new (string key, string value)[] {
                            ("PUPPETEER_EXECUTABLE_PATH", edge),
                            ("CHROME_PATH", edge)
                        };
                        _log("Chrome start failed. Trying fallback with Microsoft Edge...");
                        regExitCode = await RunCliWithLoggingAsync(
                            node,
                            $"\"{cli}\" fcm-register --config-file=\"{ConfigPath}\"",
                            wd,
                            "fcm-register",
                            _cts.Token,
                            env
                        );
                    }
                }
                
                if (regExitCode != 0)
                {
                    _log("❌ Registering failed. Please ensure Chrome or Edge is installed, or run Start using Edge.");
                    _running = false;
                    Stopped?.Invoke(this, EventArgs.Empty);
                    return; // Stop here, do not start listener or loop
                }

                _log("Registering completed (Confirm login in browser if applicable).");

                // Record FCM Token dates
                var issuedAt  = DateTime.Now;
                var expiresAt = issuedAt.AddDays(15); // FCM tokens expire after 15 days
                TrackingService.FcmIssuedAt  = issuedAt;
                TrackingService.FcmExpiresAt = expiresAt;

                // Persist dates (and SteamId if known) directly into the config file
                EnrichFcmConfig(issuedAt, expiresAt, TrackingService.SteamId64);

                RegistrationCompleted?.Invoke(this, EventArgs.Empty);
            }

            // 2) Listener starten
            _log("Starting Listener (fcm-listen) …");
            _listenProc = StartProcessDirect(
                node,
                $"\"{cli}\" fcm-listen --config-file=\"{ConfigPath}\"",
                workingDir: wd,
                onOut: HandleListenOutput,
                onErr: s => _log("[fcm-listen:err] " + HumanizeCli(s)),
                noWindow: true,
                redirect: true
            );

            if (_listenProc == null)
            {
                throw new InvalidOperationException("Failed to start fcm-listen process.");
            }

            _running = true;
            _listenProc.EnableRaisingEvents = true;
            _listenProc.Exited += async (_, __) =>
            {
                _running = false;
                Stopped?.Invoke(this, EventArgs.Empty);
                if (_cts is null || _cts.IsCancellationRequested) return;
                _log("Pairing-Listener canceled – restarting in 3s…");
                try
                {
                    await Task.Delay(3000, _cts.Token);
                    if (_cts is not null && !_cts.IsCancellationRequested)
                        await StartAsync(_cts.Token);
                }
                catch { /* ignore */ }
            };
        }
        private readonly StringBuilder _jsonBuffer = new();
        private bool _collectingJson = false;
        private int _braceDepth = 0;

        // Hilfsroutine zum Auslösen + Loggen der „schönen“ Einzeile
        private void FireAlarm(string? server, string? deviceName, uint? entityId, string message, DateTime ts)
        {
            var srv = server ?? "-";
            var dev = (deviceName ?? "Alarm");
            var alarm = new AlarmNotification(ts, srv, dev, entityId, message);
            AlarmReceived?.Invoke(this, alarm);
            _log($"[{ts:HH:mm:ss}] Alarm | {srv} | {dev}#{(entityId?.ToString() ?? "?")} | \"{message}\"");
        }

        public Task StopAsync()
        {
            try
            {
                try { _listenProc?.Kill(entireProcessTree: true); } catch { }
                try { _listenProc?.Dispose(); } catch { }
                _listenProc = null;

                try { _cts?.Cancel(); } catch { }
                _cts = null;

                var wasRunning = _running;
                _running = false;
                if (wasRunning) Stopped?.Invoke(this, EventArgs.Empty);

                _log("Pairing-Listener stopped.");
            }
            catch (Exception ex)
            {
                _log("Error stopping listener: " + ex.Message);
            }
            
            return Task.CompletedTask;
        }

        private static bool TryParseRustPlusUrl(string url, out PairingPayload? p)
        {
            p = null;
            try
            {
                // Query-Teil holen
                var qIndex = url.IndexOf('?');
                if (qIndex < 0) return false;
                var query = url.Substring(qIndex + 1);

                string? ip = null, portStr = null, name = null, playerId = null, playerToken = null;

                foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split('=', 2);
                    var k = Uri.UnescapeDataString(kv[0]).ToLowerInvariant();
                    var v = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
                    switch (k)
                    {
                        case "ip": ip = v; break;
                        case "host": ip = v; break;
                        case "port": portStr = v; break;
                        case "name": name = v; break;
                        case "playerid": playerId = v; break;
                        case "playertoken": playerToken = v; break;
                    }
                }

                if (string.IsNullOrWhiteSpace(ip) ||
                    string.IsNullOrWhiteSpace(playerId) ||
                    string.IsNullOrWhiteSpace(playerToken))
                    return false;

                if (!int.TryParse(portStr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var port)) port = 28082;

                p = new PairingPayload
                {
                    Host = ip!,
                    Port = port,
                    ServerName = string.IsNullOrWhiteSpace(name) ? null : name,
                    SteamId64 = playerId!,
                    PlayerToken = playerToken!
                };
                return true;
            }
            catch { return false; }
        }

        private static string? ExtractSingleQuotedAfterValue(string s)
        {
            var i = s.IndexOf("value:", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            i = s.IndexOf('\'', i);
            if (i < 0) return null;
            var j = s.LastIndexOf('\'');
            if (j <= i) return null;
            return s.Substring(i + 1, j - i - 1);
        }

        private static string? JGet(JsonElement root, params string[] names)
        {
            foreach (var n in names)
                if (root.TryGetProperty(n, out var v))
                    return v.GetString();

            // names ggf. case-insensitiv suchen
            foreach (var p in root.EnumerateObject())
                if (names.Any(n => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
                    return p.Value.GetString();

            return null;
        }

        private static string? GetJsonString(JsonElement el, string name)
        {
            if (el.TryGetProperty(name, out var v))
            {
                return v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString(),
                    JsonValueKind.Number => v.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => null,
                    _ => v.GetRawText()
                };
            }
            return null;
        }

        private static uint? JGetUInt(JsonElement root, params string[] names)
        {
            foreach (var n in names)
            {
                var s = GetJsonString(root, n);
                if (uint.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var u)) return u;
            }
            return null;
        }



        // ---- ERSETZEN: komplette HandleListenOutput ----
        private void HandleListenOutput(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            var s = Ansi.Replace(line, "").Trim();

            // Status-Marker des CLI
            if (s.IndexOf("Listening for FCM Notifications", StringComparison.OrdinalIgnoreCase) >= 0)
                Listening?.Invoke(this, EventArgs.Empty);

            if (s.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("ERR!", StringComparison.OrdinalIgnoreCase) >= 0)
                Failed?.Invoke(this, s);

            // 0) rustplus:// Deep-Link (falls vorhanden)
            var lm = RustUrl.Match(s);
            if (lm.Success && TryParseRustPlusUrl(lm.Value, out var urlPayload) && urlPayload != null)
            {
                Paired?.Invoke(this, urlPayload);
                _log($"Pairing (via rustplus://) → {urlPayload.Host}:{urlPayload.Port} // Steam {urlPayload.SteamId64}");
                return;
            }
            // ### A) raw key/value-Zeilen erkennen (channelId/title/body)
            var kv = KvLine.Match(s);
            if (kv.Success)
            {
                var k = kv.Groups["k"].Value;
                var v = kv.Groups["v"].Value;

                // Kanal: chat ↔︎ Bundle beginnen/enden
                if (k.Equals("gcm.notification.android_channel_id", StringComparison.OrdinalIgnoreCase) ||
                    k.Equals("channelId", StringComparison.OrdinalIgnoreCase))
                {
                    _chatBundleOpen = v.Equals("chat", StringComparison.OrdinalIgnoreCase);
                    if (!_chatBundleOpen)
                    {
                        _pendingChatMsg = null;
                        _pendingChatTitle = null;
                        _pendingChatTs = null;
                    }
                    // nicht returnen → evtl. mehr Zeilen in diesem Durchlauf
                }

                // Absender (title) – erst merken, später mit message flushen
                if (k.Equals("title", StringComparison.OrdinalIgnoreCase) ||
                    k.Equals("gcm.notification.title", StringComparison.OrdinalIgnoreCase))
                {
                    if (_chatBundleOpen)
                    {
                        _pendingChatTitle = v;
                        TryFlushChat();
                        return;
                    }
                }
            }

            // ### B) message/body-Zeilen
            var mm = MsgLine.Match(s);
            if (mm.Success)
            {
                if (_chatBundleOpen)
                {
                    _pendingChatMsg = mm.Groups["msg"].Value;
                    _pendingChatTs = DateTime.Now;
                    TryFlushChat();
                    return;
                }
                // kein Chat-Kanal → andere Handler (Alarm etc.) übernehmen
            }

            // ### C) JSON "value: '...'" – falls vorhanden, zusätzlich heuristisch chat erkennen
            var m = BodyJson.Match(s);
            if (m.Success)
            {
                var json = m.Groups["json"].Value;
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    static string? J(JsonElement el, string name)
                        => el.TryGetProperty(name, out var v) ? v.GetString() : null;

                    var type = GetJsonString(root, "type");   // "alarm" | "entity" | "server" | evtl. "chat"
                    if (string.Equals(type, "chat", StringComparison.OrdinalIgnoreCase))
                    {
                        var author = GetJsonString(root, "name") ?? GetJsonString(root, "username") ?? "Team";
                        var text = GetJsonString(root, "message") ?? _pendingChatMsg ?? "";
                        ChatReceived?.Invoke(this,
                            new TeamChatMessage(DateTime.Now, author, 0, text));
                        // Chat-Bundle zurücksetzen
                        _pendingChatMsg = null; _pendingChatTitle = null; _pendingChatTs = null;
                        return;
                    }
                }
                catch
                {
                    // JSON-Fehler hier ignorieren; andere Pfade behandeln die Logs weiter
                }
            }

            // 1) ALARM: message-Zeilen (kommen manchmal vor/nach dem body)

            if (mm.Success)
            {
                _pendingAlarmMsg = mm.Groups["msg"].Value;
                _pendingAlarmMsgTs = DateTime.Now;

                if (_pendingAlarm is { } ctx)
                {
                    // sofort feuern (wir haben jetzt body + message)
                    AlarmReceived?.Invoke(this, new AlarmNotification(
                        _pendingAlarmMsgTs ?? DateTime.Now,
                        ctx.server ?? "-",
                        (ctx.entityName ?? "Alarm") + (ctx.entityId.HasValue ? $"#{ctx.entityId}" : ""),
                        ctx.entityId,
                        _pendingAlarmMsg ?? ""
                    ));
                    _pendingAlarm = null; _pendingAlarmMsg = null; _pendingAlarmMsgTs = null;
                }
                return; // message verarbeitet
            }

            // 2) appData-body: JSON in der Zeile "value: '...'" ODER "value: `...`"

            if (m.Success)
            {
                var json = m.Groups["json"].Value;
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    string? host = GetJsonString(root, "ip");
                    string? portStr = GetJsonString(root, "port");
                    string? name = GetJsonString(root, "name");
                    string? playerId = GetJsonString(root, "playerId");
                    string? playerToken = GetJsonString(root, "playerToken");
                    string? entityIdStr = GetJsonString(root, "entityId") ?? GetJsonString(root, "entityID");
                    string? entityName = GetJsonString(root, "entityName");
                    string? type = GetJsonString(root, "type");          // "server" | "entity" | "alarm"
                    string? entityType = GetJsonString(root, "entityType");    // z.B. "1" (Switch) / "2" (Alarm)
                    string? issueDateStr = GetJsonString(root, "issueDate");
                    string? expiryDateStr = GetJsonString(root, "expiryDate") ?? GetJsonString(root, "expirtyDate");

                    if (!int.TryParse(portStr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var port)) port = 28082;
                    uint? entityId = (uint.TryParse(entityIdStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var eid) ? eid : (uint?)null);

                    // entityType → Kind mappen
                    string? kind = null;
                    if (!string.IsNullOrWhiteSpace(entityType))
                    {
                        if (entityType == "1") kind = "SmartSwitch";
                        else if (entityType == "2") kind = "SmartAlarm";
                    }
                    if (kind == null && !string.IsNullOrWhiteSpace(entityName))
                    {
                        if (entityName.Contains("Switch", StringComparison.OrdinalIgnoreCase)) kind = "SmartSwitch";
                        else if (entityName.Contains("Alarm", StringComparison.OrdinalIgnoreCase)) kind = "SmartAlarm";
                    }

                    // === SERVER / ENTITY → Paired feuern ===
                    if (!string.IsNullOrWhiteSpace(host) &&
                        !string.IsNullOrWhiteSpace(playerId) &&
                        !string.IsNullOrWhiteSpace(playerToken) &&
                        (string.Equals(type, "server", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(type, "entity", StringComparison.OrdinalIgnoreCase)))
                    {
                        var payload = new PairingPayload
                        {
                            Host = host!,
                            Port = port,
                            ServerName = string.IsNullOrWhiteSpace(name) ? null : name,
                            SteamId64 = playerId!,
                            PlayerToken = playerToken!,
                            EntityId = entityId,
                            EntityName = string.IsNullOrWhiteSpace(entityName) ? null : entityName,
                            EntityType = kind ?? type,
                            IssueDate = issueDateStr,
                            ExpiryDate = expiryDateStr
                        };

                        var key = $"{payload.Host}:{payload.Port}|{payload.SteamId64}|{payload.PlayerToken}|{payload.EntityId}";
                        if (_lastPairKey == key && (DateTime.UtcNow - _lastPairAt).TotalSeconds < 20)
                        {
                            _log("[fcm] duplicate pairing ignored.");
                            return; // ← denselben Pairing-Bounce innerhalb 20 s ignorieren
                        }
                        _lastPairKey = key;
                        _lastPairAt = DateTime.UtcNow;

                        Paired?.Invoke(this, payload);

                        
                        _log($"Pairing empfangen → {(payload.ServerName ?? payload.Host)}:{payload.Port}" +
                             (payload.EntityId.HasValue ? $"  // Entity {payload.EntityId}" : ""));
                        return;
                    }

                    // === ALARM-Body → Kontext puffern und ggf. sofort feuern ===
                    if (string.Equals(type, "alarm", StringComparison.OrdinalIgnoreCase))
                    {
                        _pendingAlarm = (name, entityName, entityId);

                        if (_pendingAlarmMsg is string buffered)
                        {
                            var ts = (_pendingAlarmMsgTs ?? DateTime.Now);
                            AlarmReceived?.Invoke(this, new AlarmNotification(
                                ts,
                                name ?? "-",
                                (entityName ?? "Alarm") + (entityId.HasValue ? $"#{entityId}" : ""),
                                entityId,
                                buffered
                            ));
                            _pendingAlarm = null; _pendingAlarmMsg = null; _pendingAlarmMsgTs = null;
                        }
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _log("[fcm-listen] body-JSON Parse-Fehler: " + ex.Message);
                    // nicht returnen → unten normal loggen
                }
            }

            // 3) sonst normal loggen
            _log("[fcm-listen] " + s);
        }


        private static string PathJoin(params string[] parts) => Path.Combine(parts);





        // --- schlanke Prozessstarter (ohne cmd.exe) ---
        private static Process StartProcessDirect(
            string fileName, string args, string? workingDir = null,
            Action<string>? onOut = null, Action<string>? onErr = null,
            bool noWindow = true, bool redirect = true)
        {
            var psi = new ProcessStartInfo(fileName, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = redirect,
                RedirectStandardError = redirect,
                CreateNoWindow = noWindow,
                WorkingDirectory = string.IsNullOrEmpty(workingDir) ? "" : workingDir
            };
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (redirect)
            {
                p.OutputDataReceived += (_, e) => { if (e.Data != null) onOut?.Invoke(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) onErr?.Invoke(e.Data); };
            }
            p.Start();
            if (redirect) { p.BeginOutputReadLine(); p.BeginErrorReadLine(); }
            return p;
        }


        private static async Task<int> RunProcessDirectAsync(
    string fileName, string args, string? workingDir = null,
    bool waitForExit = true, bool redirect = true, CancellationToken token = default)
        {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var psi = new ProcessStartInfo(fileName, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = redirect,
                RedirectStandardError = redirect,
                CreateNoWindow = false,               // Browser darf aufgehen (fcm-register)
                WorkingDirectory = string.IsNullOrEmpty(workingDir) ? "" : workingDir
            };
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (redirect)
            {
                p.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.WriteLine("[out] " + e.Data); };
                p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.WriteLine("[err] " + e.Data); };
            }
            p.Exited += (_, __) => tcs.TrySetResult(p.ExitCode);
            p.Start();
            if (redirect) { p.BeginOutputReadLine(); p.BeginErrorReadLine(); }

            using (token.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } }))
                return waitForExit ? await tcs.Task.ConfigureAwait(false) : 0;
        }

        // CLI PROPER ERROR LOGGING

        // macht typische CLI-/Node-/Puppeteer-Fehler für Nutzer verständlich
        private static string HumanizeCli(string s)
        {
            var l = s?.ToLowerInvariant() ?? "";

            if (l.Contains("fcm credentials missing"))
                return "❌ FCM-Zugangsdaten fehlen. Bitte zuerst „fcm-register“ ausführen.";

            if ((l.Contains("could not find") || l.Contains("not found") || l.Contains("enoent")) && l.Contains("chrome"))
                return "❌ Kein Chrome/Chromium gefunden. Bitte Google Chrome installieren (oder Edge/Chromium verfügbar machen).";

            if (l.Contains("failed to launch") && l.Contains("chrome"))
                return "❌ Chrome/Chromium ließ sich nicht starten (Antivirus/Policy/fehlende Rechte?).";

            if ((l.Contains("getaddrinfo") || l.Contains("enotfound") || l.Contains("eai_again")) && l.Contains("mtalk.google.com"))
                return "⚠️ Keine Verbindung zu mtalk.google.com (Port 5228). Firewall/Proxy/DNS prüfen.";

            if (l.Contains("err_proxy") || l.Contains("proxy"))
                return "⚠️ Proxy-Problem beim Start. Proxy-Konfiguration prüfen oder deaktivieren.";

            if (l.Contains("eacces") || l.Contains("eperm"))
                return "⚠️ Zugriffsrechte-Problem. Als Benutzer mit ausreichenden Rechten starten.";

            if (l.Contains("node:internal") && l.Contains("modules") && l.Contains("cannot find module"))
                return "❌ CLI-Module fehlen oder sind beschädigt. Bitte „rustplus-cli.zip“ korrekt entpacken.";

            // Fallback: Originalzeile beibehalten
            return s;
        }

        private async Task<int> RunCliWithLoggingAsync(
    string fileName, string args, string? workingDir, string tag, CancellationToken token,
    params (string key, string value)[] env)
        {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            var p = StartProcessDirectWithEnv(
                fileName, args, workingDir,
                onOut: s => { if (!string.IsNullOrEmpty(s)) _log($"[{tag}] {HumanizeCli(s)}"); },
                onErr: s => { if (!string.IsNullOrEmpty(s)) _log($"[{tag}:err] {HumanizeCli(s)}"); },
                noWindow: false,           // wie zuvor beim Register: Browser darf aufgehen
                redirect: true,
                env: env
            );

            p.EnableRaisingEvents = true;
            p.Exited += (_, __) => tcs.TrySetResult(p.ExitCode);

            try
            {
                using (token.Register(() => { try { if (p is { HasExited: false }) p.Kill(entireProcessTree: true); } catch { } }))
                {
                    return await tcs.Task;
                }
            }
            catch (OperationCanceledException)
            {
                return -1;
            }
        }


        // TRY PAIRING WITH EDGE

        private static string? FindEdge()
        {
            string p1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                                     "Microsoft", "Edge", "Application", "msedge.exe");
            string p2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                     "Microsoft", "Edge", "Application", "msedge.exe");
            return File.Exists(p1) ? p1 : (File.Exists(p2) ? p2 : null);
        }

        private static Process StartProcessDirectWithEnv(
    string fileName, string args, string? workingDir = null,
    Action<string>? onOut = null, Action<string>? onErr = null,
    bool noWindow = true, bool redirect = true,
    params (string key, string value)[] env)
        {
            var psi = new ProcessStartInfo(fileName, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = redirect,
                RedirectStandardError = redirect,
                CreateNoWindow = noWindow,
                WorkingDirectory = string.IsNullOrEmpty(workingDir) ? "" : workingDir
            };
            foreach (var (k, v) in env) psi.Environment[k] = v;

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (redirect)
            {
                p.OutputDataReceived += (_, e) => { if (e.Data != null) onOut?.Invoke(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) onErr?.Invoke(e.Data); };
            }
            p.Start();
            if (redirect) { p.BeginOutputReadLine(); p.BeginErrorReadLine(); }
            return p;
        }

        private static async Task<int> RunProcessDirectAsyncWithEnv(
            string fileName, string args, string? workingDir = null,
            bool waitForExit = true, bool redirect = true, CancellationToken token = default,
            params (string key, string value)[] env)
        {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var psi = new ProcessStartInfo(fileName, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = redirect,
                RedirectStandardError = redirect,
                CreateNoWindow = false,   // Register darf Browser öffnen
                WorkingDirectory = string.IsNullOrEmpty(workingDir) ? "" : workingDir
            };
            foreach (var (k, v) in env) psi.Environment[k] = v;

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (redirect)
            {
                p.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.WriteLine("[out] " + e.Data); };
                p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.WriteLine("[err] " + e.Data); };
            }
            p.Exited += (_, __) => tcs.TrySetResult(p.ExitCode);
            p.Start();
            if (redirect) { p.BeginOutputReadLine(); p.BeginErrorReadLine(); }

            using (token.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } }))
                return waitForExit ? await tcs.Task.ConfigureAwait(false) : 0;
        }

        public async Task StartAsyncUsingEdge(CancellationToken ct = default)
        {
            Status?.Invoke(this, "starting");
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            if (_running && _listenProc != null && !_listenProc.HasExited)
            { _log("Listener already running."); return; }

            var node = RuntimeHelper.FindBundledNode()
                ?? throw new InvalidOperationException(RuntimeHelper.GetNodeNotFoundMessage());

            var cli = RuntimeHelper.ResolveCliEntry(out var wd)
                ?? throw new InvalidOperationException("rustplus-cli not found (rustplus-cli.zip missing or extraction failed).");

            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

            var edge = FindEdge();
            if (edge == null)
            {
                _log("❌ Microsoft Edge wurde nicht gefunden. Bitte Edge installieren oder normalen Start verwenden.");
                return;
            }
            var env = new (string key, string value)[] {
        ("PUPPETEER_EXECUTABLE_PATH", edge),
        ("CHROME_PATH", edge)
    };
            _log($"Using Edge for Puppeteer: {edge}");

            // Registrierung (nur falls nötig), aber via Edge
            if (!File.Exists(ConfigPath) || new FileInfo(ConfigPath).Length < 50)
            {
                _log("Starting one time registration (fcm-register) via Edge …");
                await RunProcessDirectAsyncWithEnv(
                    node,
                    $"\"{cli}\" fcm-register --config-file=\"{ConfigPath}\"",
                    workingDir: wd,
                    waitForExit: true,
                    redirect: true,
                    token: _cts.Token,
                    env: env
                );
                _log("Registering completed (Confirm login in browser if applicable).");

                // Persist dates into config file
                var issuedAt2  = DateTime.Now;
                var expiresAt2 = issuedAt2.AddDays(15);
                TrackingService.FcmIssuedAt  = issuedAt2;
                TrackingService.FcmExpiresAt = expiresAt2;
                EnrichFcmConfig(issuedAt2, expiresAt2, TrackingService.SteamId64);
                RegistrationCompleted?.Invoke(this, EventArgs.Empty);
            }

            // Listener via Edge (mit ENV)
            _log("Starting Listener (fcm-listen) via Edge …");
            _listenProc = StartProcessDirectWithEnv(
                node,
                $"\"{cli}\" fcm-listen --config-file=\"{ConfigPath}\"",
                workingDir: wd,
                onOut: HandleListenOutput,
                onErr: s => _log("[fcm-listen:err] " + s),
                noWindow: true,
                redirect: true,
                env: env
            );

            _running = true;
            _listenProc.EnableRaisingEvents = true;
            _listenProc.Exited += async (_, __) =>
            {
                _running = false;
                Stopped?.Invoke(this, EventArgs.Empty);
                if (_cts is null || _cts.IsCancellationRequested) return;
                _log("Pairing-Listener canceled – restarting in 3s…");
                try
                {
                    await Task.Delay(3000, _cts.Token);
                    if (_cts is not null && !_cts.IsCancellationRequested)
                        await StartAsyncUsingEdge(_cts.Token);
                }
                catch { /* ignore */ }
            };
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the rustplusjs-config.json, injects issue_date / expiry_date / steam_id,
        /// and writes it back so the file is self-contained for the next app launch.
        /// </summary>
        public void EnrichFcmConfig(DateTime issuedAt, DateTime expiresAt, string? steamId)
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;

                var json = File.ReadAllText(ConfigPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                using var ms  = new System.IO.MemoryStream();
                using var wtr = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });
                wtr.WriteStartObject();

                // Copy all existing properties
                foreach (var prop in root.EnumerateObject())
                    prop.WriteTo(wtr);

                // Inject / overwrite our metadata fields
                wtr.WriteString("steam_id",    steamId ?? "");
                wtr.WriteString("issue_date",  issuedAt.ToString("o"));
                wtr.WriteString("expiry_date", expiresAt.ToString("o"));

                wtr.WriteEndObject();
                wtr.Flush();

                File.WriteAllBytes(ConfigPath, ms.ToArray());
                _log($"[fcm] Config enriched – expires {expiresAt:yyyy-MM-dd}");
            }
            catch (Exception ex)
            {
                _log($"[fcm] Warning: could not enrich config file: {ex.Message}");
            }
        }

    }
}
