using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using RustPlusDesk.Services.Cloud;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

/// <summary>
/// <c>!mute</c>, <c>!unmute</c> and <c>!device ...</c> answered by this app, and the alert bell
/// that is the same switch as <c>!mute</c>.
///
/// The bell (<c>ChatAnnounce</c>, the master switch for chat alerts) is the mute: turning it off
/// is an open-ended mute, <c>!mute 15m</c> turns it off until a time, and turning it on or
/// <c>!unmute</c> ends the mute. The state lives on the platform (sync/chat-alerts/mute), so the
/// cloud stays quiet while the app is closed and a mute typed while the cloud was answering
/// reaches the bell through the chat_alerts_muted event.
/// </summary>
public partial class MainWindow
{
    private DispatcherTimer? _chatAlertsMuteTimer;
    private int _chatAlertsMuteAttached;

    /// <summary>Handle a settings command. True when the message was one, answered or refused.</summary>
    private async Task<bool> TryHandleManageCommand(ServerProfile profile, TeamChatMessage m, string cmd, string prefix,
        ChatChannel channel, Func<string, Task> reply)
    {
        var word = cmd.Split(' ', 2)[0];
        if (Array.IndexOf(ChatManageCommands.Words, word) < 0) return false;

        // Settings belong to the team chat, not a clan of strangers.
        if (channel != ChatChannel.Team) return true;

        // The original casing, for a name.
        var text = m.Text.Trim();
        var rest = text.Length > prefix.Length + word.Length ? text[(prefix.Length + word.Length)..].Trim() : "";

        switch (word)
        {
            case "mute":
                if (!ChatManageCommands.TryParseDuration(rest, out var duration))
                {
                    await reply("Usage: mute, mute 15m, mute 2h (s/m/h/d; a plain number is minutes).");
                    return true;
                }
                ApplyChatAlertsMute(true, duration == null ? null : DateTime.UtcNow + duration.Value, push: true);
                await reply(duration == null ? "Alerts muted until unmuted." : $"Alerts muted for {ChatManageCommands.Describe(duration.Value)}.");
                return true;

            case "unmute":
                ApplyChatAlertsMute(false, null, push: true);
                await reply("Alerts unmuted.");
                return true;

            default:
                await reply(HandleDeviceCommand(profile, m, rest, prefix));
                return true;
        }
    }

    /// <summary>
    /// Rename a device or change its word. Only the account owner may - this is their
    /// configuration, not the game - the same rule the cloud applies (where an allow-list on the
    /// platform can widen it).
    /// </summary>
    private bool IsBotOwner(ServerProfile profile, TeamChatMessage m)
        => (_mySteamId != 0 && m.SteamId == _mySteamId)
           || (ulong.TryParse(profile.SteamId64, NumberStyles.None, CultureInfo.InvariantCulture, out var owner) && owner == m.SteamId);

    private string HandleDeviceCommand(ServerProfile profile, TeamChatMessage m, string rest, string prefix)
    {
        if (!IsBotOwner(profile, m)) return "Only the owner of this bot can change devices.";

        var parsed = ChatManageCommands.ParseDevice(rest);
        if (parsed == null)
            return $"Usage: {prefix}device <#n|command|name> name <new name>, or {prefix}device <#n|command|name> command <word>.";

        var device = ChatManageCommands.FindDevice(profile, parsed.Value.Ref);
        if (device == null) return $"No device matches \"{parsed.Value.Ref}\".";

        var label = device.PureName;

        if (parsed.Value.IsName)
        {
            var name = ChatManageCommands.CleanName(parsed.Value.Value);
            if (name == null) return $"A name needs 1 to {ChatManageCommands.DeviceNameMax} characters.";
            device.Alias = name;
            AppendLog($"[ChatCommand] {m.Author} renamed #{device.EntityId} to \"{name}\".");
            // The save pushes the tree and the bindings to the cloud within half a second.
            _vm.Save();
            return $"Renamed [{label}] to [{name}].";
        }

        var commandWord = parsed.Value.Value.TrimStart('!').Trim().ToLowerInvariant();
        if (commandWord.StartsWith(prefix, StringComparison.Ordinal)) commandWord = commandWord[prefix.Length..];
        var problem = ChatManageCommands.CommandWordProblem(profile, device.EntityId, commandWord);
        if (problem != null) return problem;

        ChatManageCommands.BindCommand(profile, device, commandWord);
        AppendLog($"[ChatCommand] {m.Author} bound {prefix}{commandWord} to #{device.EntityId}.");
        _vm.Save();
        return $"[{label}] now answers to {prefix}{commandWord}.";
    }

    /// <summary>
    /// Set the mute: the bell, the timer that lifts a timed mute, and - when this app made the
    /// change - the platform, so the cloud and the website agree.
    /// </summary>
    internal void ApplyChatAlertsMute(bool muted, DateTime? untilUtc, bool push)
    {
        if (muted && untilUtc is DateTime end && end <= DateTime.UtcNow)
            muted = false;

        TrackingService.ChatAlertsMutedUntilUtc = muted ? untilUtc : null;
        TrackingService.AnnounceSpawnsMaster = !muted;
        UpdateMasterToggleState();
        SyncAlertMenuItems();
        UpdateShopSearchConfig();
        RequestTeamFeatureMasterSync();

        _chatAlertsMuteTimer?.Stop();
        if (muted && untilUtc is DateTime until)
        {
            _chatAlertsMuteTimer ??= new DispatcherTimer();
            _chatAlertsMuteTimer.Tick -= ChatAlertsMuteTimer_Tick;
            _chatAlertsMuteTimer.Tick += ChatAlertsMuteTimer_Tick;
            // A long mute is re-armed on each tick rather than trusting one huge interval.
            var left = until - DateTime.UtcNow;
            _chatAlertsMuteTimer.Interval = left > TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : left;
            _chatAlertsMuteTimer.Start();
        }

        if (push) _ = PushChatAlertsMuteAsync(muted, muted ? untilUtc : null);
    }

    /// <summary>A preset in the alerts dropdown: "15m", "1h", "8h", or empty for until unmuted.</summary>
    private void MuteChatAlertsPreset_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var tag = (sender as System.Windows.FrameworkElement)?.Tag as string;
        if (!ChatManageCommands.TryParseDuration(tag, out var duration)) return;
        ApplyChatAlertsMute(true, duration == null ? null : DateTime.UtcNow + duration.Value, push: true);
        if (ChatAlertsContextMenu != null) ChatAlertsContextMenu.IsOpen = false;
    }

    /// <summary>Any length the user types, with the same rules and cap as !mute.</summary>
    private void MuteChatAlertsCustom_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ChatAlertsContextMenu != null) ChatAlertsContextMenu.IsOpen = false;

        var dlg = new Windows.PromptDialog("Mute chat alerts for (e.g. 45m, 2h, 3d; a number alone is minutes, up to 7d)", "30m")
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true) return;

        var entered = (dlg.InputText ?? "").Trim();
        if (entered.Length == 0 || !ChatManageCommands.TryParseDuration(entered, out var duration) || duration == null)
        {
            ShowInfoSnackbar("Mute chat alerts", $"\"{entered}\" is not a duration. Try 45m, 2h or 3d.", WpfUi.ControlAppearance.Caution);
            return;
        }

        ApplyChatAlertsMute(true, DateTime.UtcNow + duration.Value, push: true);
    }

    private void UnmuteChatAlerts_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ApplyChatAlertsMute(false, null, push: true);
        if (ChatAlertsContextMenu != null) ChatAlertsContextMenu.IsOpen = false;
    }

    /// <summary>
    /// Every local path that turns chat alerts on or off (select all, deselect all, a single
    /// alert toggle, a trade alert) comes through here, so the cloud and the website hear about
    /// it like the bell itself: off is an open-ended mute, on ends any mute. Nothing happens
    /// when the state does not change, so a toggle that leaves alerts on costs no request.
    /// </summary>
    internal void SetChatAlertsMaster(bool on)
    {
        if (TrackingService.AnnounceSpawnsMaster == on && (on || TrackingService.ChatAlertsMutedUntilUtc == null)) return;
        ApplyChatAlertsMute(!on, null, push: true);
    }

    private void ChatAlertsMuteTimer_Tick(object? sender, EventArgs e)
    {
        var until = TrackingService.ChatAlertsMutedUntilUtc;
        if (until == null) { _chatAlertsMuteTimer?.Stop(); return; }

        if (until.Value <= DateTime.UtcNow)
        {
            // Both sides read the same timestamp, so the platform needs no telling.
            ApplyChatAlertsMute(false, null, push: false);
            AppendLog("[alerts] Timed mute ended; chat alerts are back on.");
            return;
        }

        var left = until.Value - DateTime.UtcNow;
        if (_chatAlertsMuteTimer != null)
            _chatAlertsMuteTimer.Interval = left > TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : left;
        UpdateChatAlertsMuteIndicator();
    }

    /// <summary>The bell's look: slashed and dimmed while muted, with when the mute ends.</summary>
    internal void UpdateChatAlertsMuteIndicator()
    {
        if (ChatAnnounce == null) return;

        bool muted = !TrackingService.AnnounceSpawnsMaster;
        if (ChatAnnounceIcon != null)
        {
            ChatAnnounceIcon.Symbol = muted ? WpfUi.SymbolRegular.AlertOff24 : WpfUi.SymbolRegular.Alert24;
            ChatAnnounceIcon.Opacity = muted ? 0.6 : 1.0;
        }

        // The Chat Master's "paused on this device" tooltip wins while it applies.
        if (!ChatAnnounce.IsEnabled) return;

        var until = TrackingService.ChatAlertsMutedUntilUtc;
        ChatAnnounce.ToolTip = !muted
            ? (TryFindResource(Properties.Resources.GetString("Alerts") ?? "Alerts") ?? "Alerts")
            : until is DateTime end
                ? $"Chat alerts muted until {end.ToLocalTime():HH:mm} ({ChatManageCommands.Describe(end - DateTime.UtcNow)} left)"
                : "Chat alerts muted";
    }

    private static async Task PushChatAlertsMuteAsync(bool muted, DateTime? untilUtc)
    {
        if (!CloudBackend.UsePlatform || !CloudAuthManager.IsAuthenticated) return;
        try
        {
            await CloudApiClient.CallApiAsync("sync/chat-alerts/mute", HttpMethod.Put, payload: new
            {
                muted,
                muted_until = untilUtc?.ToString("o", CultureInfo.InvariantCulture),
            });
        }
        catch (Exception ex)
        {
            SupabaseAuthLogSafe($"[alerts] Could not sync the chat alert mute: {ex.Message}");
        }
    }

    private static void SupabaseAuthLogSafe(string line)
    {
        try { Services.Auth.SupabaseAuthManager.AppendLog(line); } catch { }
    }

    /// <summary>
    /// Follow the platform's mute: once on sign-in (a mute set in game while the app was closed,
    /// or a timed one that ran out meanwhile) and on every chat_alerts_muted event after that.
    /// </summary>
    private void AttachChatAlertsMuteSync()
    {
        if (System.Threading.Interlocked.Exchange(ref _chatAlertsMuteAttached, 1) == 1) return;

        RealtimeClient.Shared.EventReceived += OnChatAlertsMuteEvent;
        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var body = await CloudApiClient.CallApiAsync("sync/chat-alerts/mute", HttpMethod.Get);
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("data", out var data)) return;
                bool remoteMuted = data.TryGetProperty("muted", out var mv) && mv.ValueKind == JsonValueKind.True;
                var remoteUntil = data.TryGetProperty("muted_until", out var uv) && uv.ValueKind == JsonValueKind.String ? uv.GetString() : null;

                if (remoteMuted)
                    ApplyRemoteMute(true, remoteUntil);
                else if (TrackingService.ChatAlertsMutedUntilUtc != null)
                    ApplyChatAlertsMute(false, null, push: false); // a timed mute that ran out, or was lifted in game
                else if (!TrackingService.AnnounceSpawnsMaster)
                    _ = PushChatAlertsMuteAsync(true, null); // the bell is off here: tell the cloud
            }
            catch (Exception ex)
            {
                SupabaseAuthLogSafe($"[alerts] Could not read the chat alert mute: {ex.Message}");
            }
        });
    }

    private void OnChatAlertsMuteEvent(string channel, string eventName, Newtonsoft.Json.Linq.JObject data)
    {
        if (eventName != "chat_alerts_muted") return;

        var me = CloudAuthManager.CurrentUser?.Id;
        if (string.IsNullOrWhiteSpace(me) || data["user_id"]?.ToString() != me) return;
        if (data["origin"]?.ToString() == CloudApiClient.InstanceId) return; // our own change coming back

        var muted = data["muted"]?.Type == Newtonsoft.Json.Linq.JTokenType.Boolean && (bool)data["muted"]!;
        // Newtonsoft turns an ISO timestamp into a Date token; ToString() would then give a
        // culture-formatted local time that ApplyRemoteMute cannot parse, and the timed mute
        // would silently become open-ended. Read the instant itself.
        var untilToken = data["muted_until"];
        string? until = untilToken == null || untilToken.Type == Newtonsoft.Json.Linq.JTokenType.Null
            ? null
            : untilToken.Type == Newtonsoft.Json.Linq.JTokenType.Date
                ? untilToken.ToObject<DateTime>().ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
                : untilToken.ToString();
        Dispatcher.BeginInvoke(new Action(() => ApplyRemoteMute(muted, until)));
    }

    /// <summary>Re-arm a timed mute saved before the app last closed, or lift it if it has run out.</summary>
    private void RestoreLocalChatAlertsMute()
    {
        if (TrackingService.ChatAlertsMutedUntilUtc is DateTime until)
            ApplyChatAlertsMute(true, until, push: false);
        else
            UpdateChatAlertsMuteIndicator();
    }

    /// <summary>Take a mute or unmute the platform announced (from the game, the website or another PC).</summary>
    private void ApplyRemoteMute(bool muted, string? untilIso)
    {
        DateTime? until = DateTime.TryParse(untilIso, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

        if (muted)
        {
            ApplyChatAlertsMute(true, until, push: false);
        }
        else if (!TrackingService.AnnounceSpawnsMaster)
        {
            ApplyChatAlertsMute(false, null, push: false);
        }
    }
}
