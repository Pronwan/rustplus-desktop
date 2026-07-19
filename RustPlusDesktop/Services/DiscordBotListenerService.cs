using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Auth;
using Supabase.Realtime;
using static Postgrest.Constants;

namespace RustPlusDesk.Services;

public class DiscordBotListenerService
{
    private static DiscordBotListenerService? _instance;
    public static DiscordBotListenerService Instance => _instance ??= new DiscordBotListenerService();

    private readonly List<RealtimeChannel> _activeChannels = new();
    private readonly HashSet<string> _subscribedGuildIds = new();
    private bool _isListening;
    private bool _isNotificationMaster;
    private List<string> _teamSteamIds = new();
    private static readonly ConcurrentDictionary<string, DateTime> InvalidChannelUntilUtc = new();

    private DiscordBotListenerService() { }

    public async Task UpdateSubscriptionStateAsync(bool isMaster, List<string> teamSteamIds)
    {
        _isNotificationMaster = isMaster;

        if (SupabaseAuthManager.IsUpgradeRequiredSnackbarShown)
        {
            StopListening();
            return;
        }

        // Command rows are claimed atomically; master status only gates outgoing notifications.
        if (teamSteamIds == null || teamSteamIds.Count == 0 || !SupabaseAuthManager.IsPremium)
        {
            if (_isListening)
            {
                Log($"[DiscordBotListener] Stopping subscription: isMaster={isMaster}, IsPremium={SupabaseAuthManager.IsPremium}");
                StopListening();
            }
            return;
        }

        // Check if team composition changed or we weren't listening
        var sortedNew = teamSteamIds.OrderBy(x => x).ToList();
        var sortedOld = _teamSteamIds.OrderBy(x => x).ToList();
        
        if (_isListening && sortedNew.SequenceEqual(sortedOld))
        {
            return; // No changes in team, keep existing subscription
        }

        Log($"[DiscordBotListener] Updating subscription: isMaster={isMaster}, teamCount={teamSteamIds.Count}, IsPremium={SupabaseAuthManager.IsPremium}");
        StopListening();
        _teamSteamIds = sortedNew;
        _isListening = true;

        try
        {
            // Fetch guild IDs for all team members (including ourselves)
            var response = await SupabaseAuthManager.Client
                .From<DiscordBotSettingsModel>()
                .Filter("owner_steam_id", Operator.In, _teamSteamIds)
                .Get();

            var settings = response.Models;
            if (settings == null || settings.Count == 0)
            {
                return;
            }

            foreach (var setting in settings)
            {
                if (string.IsNullOrEmpty(setting.GuildId)) continue;
                await SubscribeToGuildQueueAsync(setting.GuildId);
            }
        }
        catch (Exception ex)
        {
            Log($"[DiscordBotListener] Error setting up subscriptions: {ex.Message}");
        }
    }

    private async Task SubscribeToGuildQueueAsync(string guildId)
    {
        if (SupabaseAuthManager.IsUpgradeRequiredSnackbarShown) return;

        try
        {
            var channel = SupabaseAuthManager.Client.Realtime
                .Channel($"discord_queue_{guildId}");

            var options = new Supabase.Realtime.PostgresChanges.PostgresChangesOptions(
                "public", 
                "bot_commands_queue", 
                Supabase.Realtime.PostgresChanges.PostgresChangesOptions.ListenType.Inserts,
                $"guild_id=eq.{guildId}");
            channel.Register(options);

            // Listen to inserts in the command queue for this guild
            channel.AddPostgresChangeHandler(Supabase.Realtime.PostgresChanges.PostgresChangesOptions.ListenType.Inserts, (sender, change) =>
            {
                try
                {
                    // Use the typed Model<T>() API - requires REPLICA IDENTITY FULL on the table
                    var record = change.Model<BotCommandsQueueModel>();
                    if (record == null)
                    {
                        Log($"[DiscordBotListener] Record is null - make sure REPLICA IDENTITY FULL is set: ALTER TABLE public.bot_commands_queue REPLICA IDENTITY FULL;");
                        return;
                    }
                    Log($"[DiscordBotListener] Received command: id={record.Id}, guild={record.GuildId}, type={record.CommandType}, status={record.Status}");
                    _ = ProcessIncomingCommandAsync(record);
                }
                catch (Exception ex)
                {
                    Log($"[DiscordBotListener] Error in change handler: {ex.Message}");
                }
            });

            lock (_subscribedGuildIds) { _subscribedGuildIds.Add(guildId); }
            await channel.Subscribe();
            _activeChannels.Add(channel);
            
            Log($"[DiscordBotListener] Subscribed to command queue for Guild: {guildId}");
            await ProcessRecentPendingCommandsAsync(guildId);
        }
        catch (Exception ex)
        {
            lock (_subscribedGuildIds) { _subscribedGuildIds.Remove(guildId); }
            Log($"[DiscordBotListener] Failed to subscribe to Guild {guildId}: {ex.Message}");
        }
    }

    private async Task ProcessIncomingCommandAsync(BotCommandsQueueModel record)
    {
        if (SupabaseAuthManager.IsUpgradeRequiredSnackbarShown)
        {
            Log($"[DiscordBotListener] Ignoring command {record.Id}: application update is required.");
            return;
        }

        try
        {
            var id = record.Id;
            var guildId = record.GuildId;
            var commandType = record.CommandType;
            var status = record.Status;

            if (status != "pending" || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(guildId))
            {
                Log($"[DiscordBotListener] Ignoring invalid command: id={id}, guild={guildId}, status={status}");
                return;
            }

            // Filter locally to ensure we only process commands for guilds we are subscribed to
            lock (_subscribedGuildIds)
            {
                if (!_subscribedGuildIds.Contains(guildId))
                {
                    // Log($"[DiscordBotListener] Ignoring command {id}: Guild {guildId} is not active on this client.");
                    // return;
                }
            }

            // Try to acquire the command lock by changing status to 'processing'
            var updateResponse = await SupabaseAuthManager.Client
                .From<BotCommandsQueueModel>()
                .Filter("id", Operator.Equals, id)
                .Filter("status", Operator.Equals, "pending")
                .Set(x => x.Status, "processing")
                .Update();

            if (updateResponse.Models == null || updateResponse.Models.Count == 0)
            {
                // Lock acquisition failed (another client picked it up)
                Log($"[DiscordBotListener] Command {id} was not claimed (already handled or rejected by RLS).");
                return;
            }

            Log($"[DiscordBotListener] Acquired lock for command {id} ({commandType})");

            // Execute command & prepare response
            var reply = await ExecuteCommandActionAsync(commandType, record);

            // Update database with final response (ResponsePayload is JSONB, serialize via JObject)
            var replyJson = Newtonsoft.Json.Linq.JObject.FromObject(reply);
            await SupabaseAuthManager.Client
                .From<BotCommandsQueueModel>()
                .Filter("id", Operator.Equals, id)
                .Set(x => x.Status, reply.Success ? "completed" : "failed")
                .Set(x => x.ResponsePayload!, replyJson)
                .Update();

            Log($"[DiscordBotListener] Command {id} completed with status: {(reply.Success ? "completed" : "failed")}");
        }
        catch (Exception ex)
        {
            Log($"[DiscordBotListener] Error processing command: {ex.Message}");
        }
    }

    private class CommandResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
    }

    private Task<CommandResult> ExecuteCommandActionAsync(string? commandType, BotCommandsQueueModel record)
    {
        // IMPORTANT: All WPF ViewModel property access MUST happen on the UI thread.
        // Dispatcher.InvokeAsync posts the entire async operation to the UI message queue.
        return System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            var result = new CommandResult { Success = false };
            try
            {
                var mainWindow = System.Windows.Application.Current.MainWindow as RustPlusDesk.Views.MainWindow;
                if (mainWindow?.DataContext is not RustPlusDesk.ViewModels.MainViewModel vm)
                {
                    result.Message = "Desktop client is initializing or not fully loaded.";
                    return result;
                }

                switch (commandType?.ToLowerInvariant())
                {
                    case "time":
                        result.Success = true;
                        var timeStr = vm.ServerTime;
                        if (!string.IsNullOrWhiteSpace(vm.TimeUntilNextPhase))
                            timeStr += $" ({vm.TimeUntilNextPhase})";
                        result.Message = $"🕒 Current Server Time: {timeStr}";
                        break;

                    case "pop":
                        result.Success = true;
                        var popStr = $"Players: {vm.ServerPlayers}";
                        if (vm.ServerQueue != "0" && vm.ServerQueue != "-")
                            popStr += $" (Queue: {vm.ServerQueue})";
                        result.Message = $"👥 Server Population: {popStr}";
                        break;

                    case "toggle_switch":
                        {
                            string deviceNameOrId = "";
                            var deviceToken = record.Payload?["device"];
                            var entityIdToken = record.Payload?["entity_id"];

                            if (deviceToken != null && !string.IsNullOrEmpty(deviceToken.ToObject<string>()))
                                deviceNameOrId = deviceToken.ToObject<string>()!;
                            else if (entityIdToken != null)
                                deviceNameOrId = entityIdToken.ToString();

                            if (string.IsNullOrEmpty(deviceNameOrId))
                            {
                                result.Message = "❌ Invalid command payload: missing device name or ID.";
                            }
                            else
                            {
                                var (success, msg) = await mainWindow.ToggleSmartSwitchFromDiscordAsync(deviceNameOrId);
                                result.Success = success;
                                result.Message = msg;
                            }
                        }
                        break;

                    case "heli":
                        result.Success = true;
                        result.Message = mainWindow.GetHeliStatusForDiscord();
                        break;

                    case "cargo":
                        result.Success = true;
                        result.Message = mainWindow.GetCargoStatusForDiscord();
                        break;

                    case "oilrig":
                        result.Success = true;
                        result.Message = mainWindow.GetOilRigStatusForDiscord();
                        break;

                    case "deepsea":
                        result.Success = true;
                        result.Message = mainWindow.GetDeepSeaStatusForDiscord();
                        break;

                    case "vendor":
                        result.Success = true;
                        result.Message = mainWindow.GetVendorStatusForDiscord();
                        break;

                    case "upkeep":
                        result.Success = true;
                        result.Message = mainWindow.GetUpkeepDetailsForDiscord();
                        break;

                    case "commands":
                        result.Success = true;
                        result.Message = mainWindow.GetDiscordCommandListForDiscord();
                        break;

                    case "devicelist":
                        result.Success = true;
                        result.Message = mainWindow.GetSmartSwitchListForDiscord();
                        break;

                    case "map":
                        result.Success = true;
                        result.Message = "⌛ Die Map wird gerendert... bitte warten.";
                        // Start upload asynchronously so it doesn't block
                        _ = Task.Run(async () =>
                        {
                            var base64 = await mainWindow.GetCurrentMapScreenshotBase64Async();
                            await mainWindow.UploadMapScreenshotToDiscordAsync(base64, 
                                record.Payload?["interaction_token"]?.ToString(),
                                record.Payload?["application_id"]?.ToString(), null);
                        });
                        break;

                    case "mapfull":
                        result.Success = true;
                        result.Message = "⌛ Die gesamte Map wird gerendert... bitte warten.";
                        _ = Task.Run(async () =>
                        {
                            var base64 = await mainWindow.GetFullMapScreenshotBase64Async();
                            await mainWindow.UploadMapScreenshotToDiscordAsync(base64, 
                                record.Payload?["interaction_token"]?.ToString(),
                                record.Payload?["application_id"]?.ToString(), null);
                        });
                        break;

                    default:
                        result.Message = $"Unknown or unsupported command: {commandType}";
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Message = $"Error executing command: {ex.Message}";
            }
            return result;
        }).Task.Unwrap();
    }

    private static Task<RustPlusDesk.Views.MainWindow?> GetMainWindowAsync()
    {
        var tcs = new TaskCompletionSource<RustPlusDesk.Views.MainWindow?>();
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            tcs.SetResult(System.Windows.Application.Current.MainWindow as RustPlusDesk.Views.MainWindow);
        });
        return tcs.Task;
    }

    public void StopListening()
    {
        if (!_isListening) return;

        foreach (var channel in _activeChannels)
        {
            try
            {
                channel.Unsubscribe();
            }
            catch { }
        }

        _activeChannels.Clear();
        lock (_subscribedGuildIds) { _subscribedGuildIds.Clear(); }
        _teamSteamIds.Clear();
        _isListening = false;
        Log("[DiscordBotListener] Stopped listening to Discord queues.");
    }

    public async Task SendNotificationAsync(string notificationType, string message)
    {
        if (!_isNotificationMaster || !_isListening || _teamSteamIds.Count == 0) return;

        await SendNotificationToOwnersAsync(notificationType, message, _teamSteamIds);
    }

    public async Task SendRaidNotificationAsync(string serverKey, string ownerSteamId, string message)
    {
        Log(
            $"[DiscordBotListener] Raid notification requested: serverKey='{serverKey}', "
            + $"ownerSteamId='{ownerSteamId}', isListening={_isListening}, isNotificationMaster={_isNotificationMaster}, "
            + $"teamCount={_teamSteamIds.Count}, IsPremium={SupabaseAuthManager.IsPremium}.");

        if (_isNotificationMaster && _isListening && _teamSteamIds.Count > 0)
        {
            await SendNotificationToOwnersAsync("raid", message, _teamSteamIds);
            return;
        }

        if (string.IsNullOrWhiteSpace(serverKey)
            || string.IsNullOrWhiteSpace(ownerSteamId)
            || ownerSteamId == "0")
        {
            Log("[DiscordBotListener] Raid fallback skipped: server key or owner Steam ID is missing.");
            return;
        }

        var hasActiveMaster = await SupabaseAuthManager
            .HasActiveTeamFeatureMasterForMemberAsync(serverKey, ownerSteamId);
        if (hasActiveMaster)
        {
            Log($"[DiscordBotListener] Raid notification skipped: another active team master found for {serverKey}.");
            return;
        }

        Log($"[DiscordBotListener] Sending raid notification via local fallback for {serverKey}.");
        await SendNotificationToOwnersAsync("raid", message, new List<string> { ownerSteamId });
    }

    private static async Task SendNotificationToOwnersAsync(
        string notificationType,
        string message,
        List<string> ownerSteamIds)
    {
        if (SupabaseAuthManager.IsUpgradeRequiredSnackbarShown) return;

        if (ownerSteamIds.Count == 0)
        {
            Log($"[DiscordBotListener] {notificationType} notification skipped: no owner Steam IDs.");
            return;
        }

        try
        {
            var settingsRes = await SupabaseAuthManager.Client
                .From<DiscordBotSettingsModel>()
                .Filter("owner_steam_id", Operator.In, ownerSteamIds)
                .Get();

            var ownerIds = settingsRes.Models?
                .Select(s => s.OwnerSteamId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();
            if (ownerIds == null || ownerIds.Count == 0)
            {
                Log(
                    $"[DiscordBotListener] {notificationType} notification skipped: "
                    + $"no Discord bot settings found for owners [{string.Join(", ", ownerSteamIds)}].");
                return;
            }

            var ownerProfilesRes = await SupabaseAuthManager.Client
                .From<UserProfileModel>()
                .Filter("steam_id", Operator.In, ownerIds)
                .Get();

            var premiumOwnerIds = (ownerProfilesRes.Models ?? new List<UserProfileModel>())
                .Where(IsPremiumBotOwner)
                .Select(p => p.SteamId)
                .ToHashSet();

            var guildIds = settingsRes.Models?
                .Where(s => premiumOwnerIds.Contains(s.OwnerSteamId))
                .Select(s => s.GuildId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();
            if (guildIds == null || guildIds.Count == 0)
            {
                Log(
                    $"[DiscordBotListener] {notificationType} notification skipped: "
                    + $"no premium Discord bot owner found for [{string.Join(", ", ownerIds)}].");
                return;
            }

            var response = await SupabaseAuthManager.Client
                .From<RustPlusDesk.Models.DiscordChannelsConfigModel>()
                .Filter("notification_type", Operator.Equals, notificationType)
                .Filter("guild_id", Operator.In, guildIds)
                .Get();

            var configs = response.Models;
            if (configs == null || configs.Count == 0)
            {
                Log(
                    $"[DiscordBotListener] {notificationType} notification skipped: "
                    + $"no channel configuration found for guilds [{string.Join(", ", guildIds)}].");
                return;
            }

            foreach (var config in configs)
            {
                if (string.IsNullOrEmpty(config.ChannelId)) continue;
                if (InvalidChannelUntilUtc.TryGetValue(config.ChannelId, out var invalidUntil) && invalidUntil > DateTime.UtcNow)
                    continue;

                var payload = new
                {
                    channel_id = config.ChannelId,
                    content = string.IsNullOrWhiteSpace(config.MentionText) ? message : $"{config.MentionText}\n{message}",
                    tts = config.TtsEnabled,
                    audio_alert = config.AudioAlertEnabled
                };

                if (RustPlusDesk.Services.Auth.SupabaseAuthManager.IsUpgradeRequiredSnackbarShown)
                {
                    Log("[DiscordBotListener] Skipping notification: application update is required.");
                    return;
                }

                using (var httpClient = new System.Net.Http.HttpClient())
                {
                    var url = $"{RustPlusDesk.Services.Data.DataManager.SUPABASE_URL.TrimEnd('/')}/functions/v1/discord-bot-interactions";
                    var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url);
                    request.Headers.Add("apikey", RustPlusDesk.Services.Data.DataManager.SUPABASE_ANON_KEY);
                    request.Headers.Add("X-Client-Version", RustPlusDesk.Helpers.VersionHelper.GetClientVersion());

                    var token = SupabaseAuthManager.Client.Auth.CurrentSession?.AccessToken;
                    if (!string.IsNullOrEmpty(token))
                    {
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    }

                    request.Content = new System.Net.Http.StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

                    var responseMsg = await httpClient.SendAsync(request);
                    if (!responseMsg.IsSuccessStatusCode)
                    {
                        var responseContent = await responseMsg.Content.ReadAsStringAsync();
                        if (SupabaseAuthManager.HandleUpgradeRequiredResponse(responseContent))
                            return;

                        if (responseContent.Contains("50013", StringComparison.OrdinalIgnoreCase) ||
                            responseContent.Contains("Missing Permissions", StringComparison.OrdinalIgnoreCase))
                        {
                            InvalidChannelUntilUtc[config.ChannelId] = DateTime.UtcNow.AddHours(1);
                            WarnMissingChannelPermission(config.ChannelId);
                            continue;
                        }
                        throw new Exception($"HTTP {responseMsg.StatusCode}: {responseContent}");
                    }
                }
            }

            Log(
                $"[DiscordBotListener] Sent {notificationType} notification to "
                + $"{configs.Count(c => !string.IsNullOrEmpty(c.ChannelId))} configured channel(s).");
        }
        catch (Exception ex)
        {
            Log($"[DiscordBotListener] Failed to send notification: {ex.Message}");
        }
    }

    private async Task ProcessRecentPendingCommandsAsync(string guildId)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddSeconds(-15);
            var response = await SupabaseAuthManager.Client
                .From<BotCommandsQueueModel>()
                .Filter("guild_id", Operator.Equals, guildId)
                .Filter("status", Operator.Equals, "pending")
                .Get();

            foreach (var command in response.Models.Where(x => x.CreatedAt >= cutoff))
            {
                Log($"[DiscordBotListener] Recovering pending command {command.Id} ({command.CommandType}).");
                await ProcessIncomingCommandAsync(command);
            }
        }
        catch (Exception ex)
        {
            Log($"[DiscordBotListener] Failed to recover pending commands for Guild {guildId}: {ex.Message}");
        }
    }

    private static void WarnMissingChannelPermission(string channelId)
    {
        Log($"[DiscordBotListener] Channel {channelId} disabled for one hour: Discord bot is missing permissions.");
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (System.Windows.Application.Current.MainWindow is Views.MainWindow mainWindow)
            {
                mainWindow.ShowInfoSnackbar(
                    "Discord permissions missing",
                    "A configured Discord channel was disabled for one hour. Check the bot's channel permissions.",
                    Wpf.Ui.Controls.ControlAppearance.Caution);
            }
        }));
    }

    private static bool IsPremiumBotOwner(UserProfileModel profile)
    {
        if (profile.IsManualSupporter) return true;
        if (profile.PremiumUntil.HasValue && profile.PremiumUntil.Value.ToUniversalTime() > DateTime.UtcNow) return true;

        var tier = profile.SubscriptionTier?.ToLowerInvariant() ?? "free";
        return tier != "free" && tier != "guest";
    }

    private static void Log(string message)
    {
        Console.WriteLine(message);
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (System.Windows.Application.Current.MainWindow is RustPlusDesk.Views.MainWindow win)
            {
                win.AppendLog(message);
            }
        });
    }
}
