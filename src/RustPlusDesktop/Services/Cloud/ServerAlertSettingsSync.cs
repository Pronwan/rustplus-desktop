using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.Cloud;

/// <summary>
/// Modded Server Alerts, one setting for the account shared with the website and Cloud 24/7
/// (sync/server-alerts): read once after sign-in, written whenever the user changes it here, and
/// followed live through the server_alerts_changed event when it is changed elsewhere.
/// </summary>
public static class ServerAlertSettingsSync
{
    private static int _attached;

    public static void Attach(Action<string> log)
    {
        if (Interlocked.Exchange(ref _attached, 1) == 1) return;

        RealtimeClient.Shared.EventReceived += OnEvent;
        _ = PullAsync(log);
    }

    /// <summary>Take the account's setting, which may have changed on the website meanwhile.</summary>
    public static async Task PullAsync(Action<string> log)
    {
        if (!CloudBackend.UsePlatform || !CloudAuthManager.IsAuthenticated) return;
        try
        {
            var body = await CloudApiClient.CallApiAsync("sync/server-alerts", HttpMethod.Get);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return;
            Apply(
                data.TryGetProperty("enabled", out var e) ? e.ValueKind != JsonValueKind.False : true,
                data.TryGetProperty("as_raid", out var r) ? r.ValueKind != JsonValueKind.False : true);
        }
        catch (Exception ex)
        {
            log($"[alerts] Could not read the Modded Server Alerts setting: {ex.Message}");
        }
    }

    /// <summary>Send this PC's setting to the account.</summary>
    public static async Task PushAsync(Action<string> log)
    {
        if (!CloudBackend.UsePlatform || !CloudAuthManager.IsAuthenticated) return;
        try
        {
            await CloudApiClient.CallApiAsync("sync/server-alerts", HttpMethod.Put, payload: new
            {
                enabled = TrackingService.ServerAlertsEnabled,
                as_raid = TrackingService.ServerAlertsAsRaid,
            });
        }
        catch (Exception ex)
        {
            log($"[alerts] Could not sync the Modded Server Alerts setting: {ex.Message}");
        }
    }

    private static void OnEvent(string channel, string eventName, Newtonsoft.Json.Linq.JObject data)
    {
        if (eventName != "server_alerts_changed") return;

        var me = CloudAuthManager.CurrentUser?.Id;
        if (string.IsNullOrWhiteSpace(me) || data["user_id"]?.ToString() != me) return;
        if (data["origin"]?.ToString() == CloudApiClient.InstanceId) return; // our own change coming back

        Apply(
            data["enabled"]?.Type != Newtonsoft.Json.Linq.JTokenType.Boolean || (bool)data["enabled"]!,
            data["as_raid"]?.Type != Newtonsoft.Json.Linq.JTokenType.Boolean || (bool)data["as_raid"]!);
    }

    private static void Apply(bool enabled, bool asRaid)
    {
        if (TrackingService.ServerAlertsEnabled == enabled && TrackingService.ServerAlertsAsRaid == asRaid) return;
        TrackingService.SetServerAlertSettings(enabled, asRaid);
    }
}
