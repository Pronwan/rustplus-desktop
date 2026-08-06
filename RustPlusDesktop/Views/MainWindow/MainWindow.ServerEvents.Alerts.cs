using System;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private bool _cloudAlertsHooked;

    /// <summary>
    /// Routes crowd-sourced detections into the same three outputs an API-sourced event uses:
    /// team chat, the basic Discord webhook and the advanced Discord bot.
    ///
    /// SendTeamChatSafeAsync covers chat *and* the basic webhook in one call, which is why the
    /// established pattern passes skipDiscordChatForwarding: true and then posts to the bot
    /// separately — forwarding the chat line as well would deliver it twice.
    /// </summary>
    private void HookCloudEventAlerts()
    {
        if (_cloudAlertsHooked) return;
        CloudEventWatcher.Instance.EventChanged += OnCloudEventChanged;
        _cloudAlertsHooked = true;
    }

    private void UnhookCloudEventAlerts()
    {
        if (!_cloudAlertsHooked) return;
        CloudEventWatcher.Instance.EventChanged -= OnCloudEventChanged;
        _cloudAlertsHooked = false;
    }

    private void OnCloudEventChanged(CloudEventState state)
    {
        try
        {
            // Only announce a start. A refresh, a rising confirmation count or an event that
            // has already expired are not news.
            if (!state.IsActive) return;

            // A single unverified report stays in the dock but never reaches chat or Discord.
            // On a busy server quorum arrives within seconds; on a quiet one it never does,
            // and shouting on the word of one client is exactly what griefing exploits.
            if (!state.IsConfirmed) return;

            if (!TrackingService.AnnounceSpawnsMaster) return;

            string? message = state.Kind switch
            {
                RustEventKind.Cargo when TrackingService.AnnounceCargo =>
                    AlertTemplateService.GetAlertTemplate("AlertCargoSpawnedAudio"),
                RustEventKind.DeepSea when TrackingService.AnnounceDeepSea =>
                    AlertTemplateService.GetAlertTemplate("AlertDeepSeaUp"),
                RustEventKind.OilRig when TrackingService.AnnounceOilRig =>
                    AlertTemplateService.GetAlertTemplate("AlertOilRigCrateUp"),
                _ => null,
            };

            if (string.IsNullOrWhiteSpace(message)) return;

            Dispatcher.Invoke(() =>
            {
                // Team chat + basic webhook.
                _ = SendTeamChatSafeAsync(message, false, true);

                // Advanced Discord bot. "events" is the channel the API-sourced spawns use, so
                // a server that switches to fallback keeps posting where people already look.
                _ = DiscordBotListenerService.Instance.SendNotificationAsync(
                    "events", $"📡 **Event:** {message}");

                AppendLog($"[server-events] Announced {state.Kind} " +
                          $"(confirmations {state.Confirmations}).");
            });
        }
        catch (Exception ex)
        {
            AppendLog($"[server-events] Alert failed: {ex.Message}");
        }
    }
}
