using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services
{
    public enum FcmSelfTestOutcome
    {
        /// <summary>A probe was sent and came back out of our own socket.</summary>
        Healthy,

        /// <summary>Expo (or Google behind it) refuses the push token. Re-registration is the only fix.</summary>
        TokenRejected,

        /// <summary>Expo accepted the probe but it never arrived. The token is stale or the socket is dead.</summary>
        NotDelivered,

        /// <summary>Nothing was proven — no credentials, no network, Expo unreachable.</summary>
        Inconclusive,
    }

    public sealed record FcmSelfTestReport(FcmSelfTestOutcome Outcome, string Detail)
    {
        public bool NeedsRepair => Outcome is FcmSelfTestOutcome.TokenRejected or FcmSelfTestOutcome.NotDelivered;
    }

    /// <summary>
    /// Proves the push chain end to end by sending ourselves a notification.
    ///
    /// Rust+ pushes reach us through Facepunch → Expo → Google FCM → our socket. Expo's send API
    /// needs no authentication, and we hold our own Expo push token, so we can inject a probe at
    /// exactly the point Facepunch injects and watch whether it falls out of the socket. Nothing
    /// here is simulated; it is the same path, end to end.
    ///
    /// This exists because a token can go silently stale: the socket still connects, the expiry
    /// date still looks fine, and pairings simply stop arriving. Nothing in the connection itself
    /// reveals that — only actually pushing something does.
    /// </summary>
    public static class FcmSelfTestService
    {
        /// <summary>Carried in the probe's title, which is the one field that survives the trip verbatim.</summary>
        internal const string TitleMarker = "rpd-selftest:";

        private const string SendUrl = "https://exp.host/--/api/v2/push/send";
        private const string ReceiptUrl = "https://exp.host/--/api/v2/push/getReceipts";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
        private static readonly object Gate = new();

        private static string? _pendingNonce;
        private static TaskCompletionSource<bool>? _pendingArrival;

        /// <summary>
        /// Offered every inbound push by the listener before anything else looks at it. Returns true
        /// for our own probes so they are swallowed — a probe must never surface as a pairing, an
        /// alarm, or an "unhandled channel" line in the log.
        /// </summary>
        internal static bool TryConsume(string? title)
        {
            if (title is null || !title.StartsWith(TitleMarker, StringComparison.Ordinal)) return false;

            var nonce = title.Substring(TitleMarker.Length);
            lock (Gate)
            {
                if (_pendingNonce is not null && string.Equals(_pendingNonce, nonce, StringComparison.Ordinal))
                    _pendingArrival?.TrySetResult(true);
            }

            // Swallowed either way. A probe from an earlier run arriving late is still ours.
            return true;
        }

        /// <summary>
        /// Sends one probe and waits for it. <paramref name="wait"/> bounds how long we wait for
        /// delivery; the default is generous because FCM is not instant under a cold radio.
        /// </summary>
        public static async Task<FcmSelfTestReport> RunAsync(
            Action<string> log, TimeSpan? wait = null, CancellationToken ct = default)
        {
            var token = ReadExpoPushToken();
            if (string.IsNullOrWhiteSpace(token))
                return new FcmSelfTestReport(FcmSelfTestOutcome.Inconclusive, "no expo push token in config");

            var nonce = Guid.NewGuid().ToString("N").Substring(0, 12);
            var arrival = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Gate)
            {
                _pendingNonce = nonce;
                _pendingArrival = arrival;
            }

            try
            {
                log("[fcm-selftest] Sending a probe push to verify the connection …");

                string ticketId;
                try
                {
                    var (ok, id, error, detail) = await SendProbeAsync(token!, nonce, ct).ConfigureAwait(false);
                    if (!ok)
                    {
                        // Expo can reject the token outright. That is the fast, unambiguous answer.
                        if (string.Equals(error, "DeviceNotRegistered", StringComparison.OrdinalIgnoreCase))
                        {
                            log("[fcm-selftest] ❌ Push token rejected (DeviceNotRegistered).");
                            return new FcmSelfTestReport(FcmSelfTestOutcome.TokenRejected, "expo ticket: DeviceNotRegistered");
                        }

                        log($"[fcm-selftest] Probe could not be sent: {detail}");
                        return new FcmSelfTestReport(FcmSelfTestOutcome.Inconclusive, detail);
                    }
                    ticketId = id ?? string.Empty;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // No network, DNS down, Expo unreachable — says nothing about our token.
                    log($"[fcm-selftest] Could not reach Expo: {ex.Message}");
                    return new FcmSelfTestReport(FcmSelfTestOutcome.Inconclusive, ex.Message);
                }

                var timeout = wait ?? TimeSpan.FromSeconds(20);
                using var timer = new CancellationTokenSource(timeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, ct);

                var completed = await Task.WhenAny(
                    arrival.Task, Task.Delay(Timeout.Infinite, linked.Token)).ConfigureAwait(false);

                if (completed == arrival.Task)
                {
                    log("[fcm-selftest] ✔ Probe received — the push connection is working.");
                    return new FcmSelfTestReport(FcmSelfTestOutcome.Healthy, "probe delivered");
                }

                ct.ThrowIfCancellationRequested();

                // Accepted but undelivered. The receipt is where Google's verdict shows up, and it
                // needs a moment to exist at all.
                var receipt = await ReadReceiptAsync(ticketId, ct).ConfigureAwait(false);
                if (string.Equals(receipt, "DeviceNotRegistered", StringComparison.OrdinalIgnoreCase))
                {
                    log("[fcm-selftest] ❌ Push token rejected by Google (DeviceNotRegistered).");
                    return new FcmSelfTestReport(FcmSelfTestOutcome.TokenRejected, "expo receipt: DeviceNotRegistered");
                }

                log($"[fcm-selftest] ❌ Probe accepted but never arrived within {timeout.TotalSeconds:0}s.");
                return new FcmSelfTestReport(FcmSelfTestOutcome.NotDelivered,
                    receipt is null ? "no delivery" : $"no delivery, receipt: {receipt}");
            }
            finally
            {
                lock (Gate)
                {
                    if (ReferenceEquals(_pendingArrival, arrival))
                    {
                        _pendingNonce = null;
                        _pendingArrival = null;
                    }
                }
            }
        }

        /// <summary>
        /// The probe travels as a normal Expo notification. The nonce rides in the title because
        /// that field reaches the listener as a plain string; custom data is parsed into a typed
        /// Body that has nowhere to put it.
        /// </summary>
        private static async Task<(bool ok, string? ticketId, string? error, string detail)> SendProbeAsync(
            string expoPushToken, string nonce, CancellationToken ct)
        {
            var payload = new
            {
                to = expoPushToken,
                title = TitleMarker + nonce,
                body = "Rust+ Desk connection check",
                channelId = "rpd-selftest",
                priority = "high",
                // Pointless to deliver late: by then the answer no longer describes the connection
                // we were asking about.
                ttl = 60,
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, SendUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            req.Headers.TryAddWithoutValidation("accept", "application/json");

            using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
            var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
                return (false, null, null, $"HTTP {(int)res.StatusCode}");

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data))
                return (false, null, null, "malformed response");

            // A single message yields a single ticket; an array yields an array. Accept both.
            var ticket = data.ValueKind == JsonValueKind.Array
                ? (data.GetArrayLength() > 0 ? data[0] : default)
                : data;
            if (ticket.ValueKind != JsonValueKind.Object)
                return (false, null, null, "empty ticket");

            var status = ticket.TryGetProperty("status", out var s) ? s.GetString() : null;
            if (string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
                return (true, ticket.TryGetProperty("id", out var id) ? id.GetString() : null, null, "ok");

            var error = ticket.TryGetProperty("details", out var det) && det.TryGetProperty("error", out var e)
                ? e.GetString() : null;
            var message = ticket.TryGetProperty("message", out var m) ? m.GetString() : null;
            return (false, null, error, message ?? error ?? "rejected");
        }

        /// <summary>
        /// Reads the delivery receipt for a ticket. Returns the error code, or null when there is
        /// no verdict yet — Google often takes its time deciding a token is dead.
        /// </summary>
        private static async Task<string?> ReadReceiptAsync(string ticketId, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(ticketId)) return null;

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, ReceiptUrl)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { ids = new[] { ticketId } }), Encoding.UTF8, "application/json"),
                };
                req.Headers.TryAddWithoutValidation("accept", "application/json");

                using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return null;

                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
                if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
                if (!data.TryGetProperty(ticketId, out var entry)) return null;

                return entry.TryGetProperty("details", out var det) && det.TryGetProperty("error", out var e)
                    ? e.GetString() : null;
            }
            catch (OperationCanceledException) { throw; }
            catch { return null; }
        }

        private static string? ReadExpoPushToken()
        {
            try
            {
                var path = NativeFcmListener.ConfigPath;
                if (!File.Exists(path)) return null;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                return doc.RootElement.TryGetProperty("expo_push_token", out var t) ? t.GetString() : null;
            }
            catch { return null; }
        }
    }
}
