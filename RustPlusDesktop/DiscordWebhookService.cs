using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services;

/// <summary>
/// Posts Discord webhook embeds for in-game events. One global webhook URL,
/// per-server mute, per-event-type toggle. Messages go through a single
/// async queue with rate limiting so a chaotic raid never hits Discord's
/// 30 requests/minute webhook cap.
/// </summary>
public static class DiscordWebhookService
{
    // Discord allows ~30 webhook posts per minute per route. Stay safely under.
    private const int MaxRequestsPerMinute = 25;
    private const int MinSpacingMs = 60_000 / MaxRequestsPerMinute; // 2.4s between sends

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly BlockingCollection<QueuedMessage> _queue = new(boundedCapacity: 200);
    private static readonly CancellationTokenSource _cts = new();
    private static Task? _worker;
    private static long _lastSendTicks;

    public static event Action<string>? OnLog;

    public static void Start()
    {
        if (_worker != null) return;
        _worker = Task.Run(() => WorkerLoopAsync(_cts.Token));
    }

    public static void Stop()
    {
        try { _cts.Cancel(); } catch { }
        _queue.CompleteAdding();
    }

    /// <summary>
    /// Queue an embed for delivery. No-op if the URL is empty, the server is
    /// muted, or the event type is disabled. Caller checks none of that.
    /// </summary>
    public static void QueueEvent(string serverHost, string serverName, string eventTag,
                                   string title, string description, int colorRgb,
                                   string? gridCoord = null)
    {
        var url = TrackingService.DiscordWebhookUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        if (TrackingService.IsDiscordServerMuted(serverHost)) return;
        if (!TrackingService.IsDiscordEventEnabled(eventTag)) return;

        var msg = new QueuedMessage(url, serverName ?? "", title ?? "", description ?? "",
                                     colorRgb, gridCoord);
        try { _queue.Add(msg); } catch (InvalidOperationException) { /* shutdown */ }
    }

    /// <summary>
    /// Bypasses event-type and per-server filters for the "Test connection" button.
    /// Still requires a configured URL.
    /// </summary>
    public static async Task<bool> SendTestAsync()
    {
        var url = TrackingService.DiscordWebhookUrl;
        if (string.IsNullOrWhiteSpace(url)) return false;

        var payload = BuildPayload("Rust+ Desktop", "Webhook test successful. You'll receive in-game alerts here.",
                                    serverName: "Test", colorRgb: 0x4FC3F7, gridCoord: null);
        return await PostAsync(url, payload).ConfigureAwait(false);
    }

    private static async Task WorkerLoopAsync(CancellationToken ct)
    {
        try
        {
            foreach (var msg in _queue.GetConsumingEnumerable(ct))
            {
                await SpaceOutAsync(ct).ConfigureAwait(false);

                var payload = BuildPayload(msg.Title, msg.Description, msg.ServerName, msg.ColorRgb, msg.GridCoord);
                await PostAsync(msg.Url, payload).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            OnLog?.Invoke($"[discord] worker stopped: {ex.Message}");
        }
    }

    private static async Task SpaceOutAsync(CancellationToken ct)
    {
        var last = Interlocked.Read(ref _lastSendTicks);
        if (last == 0) return;
        var elapsed = (Environment.TickCount64 - last);
        var waitMs = MinSpacingMs - (int)elapsed;
        if (waitMs > 0)
            await Task.Delay(waitMs, ct).ConfigureAwait(false);
    }

    private static async Task<bool> PostAsync(string url, string jsonPayload)
    {
        try
        {
            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(url, content).ConfigureAwait(false);
            Interlocked.Exchange(ref _lastSendTicks, Environment.TickCount64);

            if (resp.IsSuccessStatusCode) return true;

            // Don't echo the URL into logs (it's a credential).
            OnLog?.Invoke($"[discord] webhook returned {(int)resp.StatusCode}");

            // 429 from Discord means we miscounted: back off explicitly.
            if ((int)resp.StatusCode == 429)
                await Task.Delay(2000).ConfigureAwait(false);

            return false;
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"[discord] webhook send failed: {ex.Message}");
            return false;
        }
    }

    private static string BuildPayload(string title, string description, string serverName,
                                       int colorRgb, string? gridCoord)
    {
        var fields = new List<object>();
        if (!string.IsNullOrWhiteSpace(serverName))
            fields.Add(new { name = "Server", value = serverName, inline = true });
        if (!string.IsNullOrWhiteSpace(gridCoord))
            fields.Add(new { name = "Grid", value = gridCoord, inline = true });

        var embed = new
        {
            title = Truncate(title, 256),
            description = Truncate(description, 2048),
            color = colorRgb & 0xFFFFFF,
            timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            footer = new { text = "Rust+ Desktop" },
            fields = fields
        };

        var doc = new { username = "Rust+ Desktop", embeds = new[] { embed } };
        return JsonSerializer.Serialize(doc);
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s.Substring(0, max - 1) + "…");

    private sealed record QueuedMessage(string Url, string ServerName, string Title, string Description,
                                         int ColorRgb, string? GridCoord);
}

/// <summary>
/// Standard Discord embed sidebar colors used by Rust+ Desktop event categories.
/// Discord interprets the color as a 24-bit RGB integer.
/// </summary>
public static class DiscordColors
{
    public const int EventBlue   = 0x4FC3F7;  // matches the app's Accent
    public const int EventOrange = 0xE8611A;  // cargo/oil-rig
    public const int Death       = 0xCE422B;  // rust red
    public const int Online      = 0x62D38B;  // tracking pulse green
    public const int Offline     = 0x9E9E9E;  // grey
    public const int Shop        = 0xFFD166;  // amber
    public const int Suspicious  = 0xFF6B6B;  // brighter red for warnings
    public const int Vendor      = 0xB39DDB;  // purple-ish
}
