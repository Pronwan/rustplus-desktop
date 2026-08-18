using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Auth;
using RustPlusDesk.Services.Cloud;

namespace RustPlusDesk.Services
{
    public static class FcmSyncService
    {
        /// <summary>Local rustplus.js FCM credentials, the source of truth for what gets uploaded.</summary>
        public static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RustPlusDesk", "rustplusjs-config.json");

        public static async Task<bool> SyncFcmCredentialsAsync()
        {
            if (!SupabaseAuthManager.IsPremium)
            {
                SupabaseAuthManager.AppendLog("[FcmSync] User is not premium. Skipping FCM sync.");
                return false;
            }

            if (!File.Exists(ConfigPath))
            {
                SupabaseAuthManager.AppendLog("[FcmSync] No FCM config found locally.");
                return false;
            }

            try
            {
                var steamId = TrackingService.SteamId64;
                if (string.IsNullOrEmpty(steamId) || steamId == "0")
                {
                    SupabaseAuthManager.AppendLog("[FcmSync] Steam ID not available.");
                    return false;
                }

                var client = SupabaseAuthManager.Client;
                var userId = client?.Auth?.CurrentUser?.Id;
                if (!CloudBackend.UsePlatform && (client is null || string.IsNullOrEmpty(userId)))
                {
                    SupabaseAuthManager.AppendLog("[FcmSync] User not authenticated.");
                    return false;
                }
                if (CloudBackend.UsePlatform && !CloudAuthManager.IsAuthenticated)
                {
                    SupabaseAuthManager.AppendLog("[FcmSync] User not authenticated.");
                    return false;
                }

                var jsonText = await File.ReadAllTextAsync(ConfigPath);
                var fcmConfigDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonText)
                    ?? new Dictionary<string, object>();

                if (!string.IsNullOrEmpty(TrackingService.DiscordWebhookUrl))
                {
                    fcmConfigDict["discord_webhook_url"] = TrackingService.DiscordWebhookUrl;
                    fcmConfigDict["discord_webhook_mention"] = TrackingService.DiscordWebhookMention ?? "";
                }

                if (!string.IsNullOrEmpty(TrackingService.SmartHomeWebhookUrl))
                {
                    fcmConfigDict["smart_home_webhook_url"] = TrackingService.SmartHomeWebhookUrl;
                }

                if (!string.IsNullOrEmpty(TrackingService.TelegramCallWebhookUrl))
                {
                    fcmConfigDict["telegram_call_url"] = TrackingService.TelegramCallWebhookUrl;
                }

                bool stored;

                if (CloudBackend.UsePlatform)
                {
                    await CloudApiClient.CallApiAsync("me/fcm", HttpMethod.Put, payload: new
                    {
                        steam_id = steamId,
                        fcm_config = fcmConfigDict,
                    });

                    try
                    {
                        await CloudApiClient.CallApiAsync("me/notification-settings", HttpMethod.Patch, payload: new
                        {
                            fcm_discord_webhook_url = TrackingService.DiscordWebhookUrl ?? "",
                            fcm_discord_webhook_mention = TrackingService.DiscordWebhookMention ?? "",
                        });
                    }
                    catch (Exception ex)
                    {
                        SupabaseAuthManager.AppendLog($"[FcmSync] Notification settings sync info: {ex.Message}");
                    }

                    stored = true;
                }
                else
                {
                    var model = new UserFcmCredentialsModel
                    {
                        UserId = userId,
                        SteamId = steamId,
                        FcmConfig = Newtonsoft.Json.Linq.JObject.FromObject(fcmConfigDict),
                        UpdatedAt = DateTime.UtcNow
                    };

                    var response = await client!.From<UserFcmCredentialsModel>().Upsert(model);
                    stored = response != null && response.Models.Count > 0;
                }

                // Sync User Servers for the Cloud Worker
                try
                {
                    var profiles = RustPlusDesk.Services.Data.ProfileDataModule.LoadProfiles();
                    if (profiles != null && profiles.Count > 0)
                    {
                        var paired = profiles
                            .Where(p => !string.IsNullOrEmpty(p.Host) && p.Port > 0
                                        && !string.IsNullOrEmpty(p.PlayerToken) && p.PlayerToken != "offline")
                            .ToList();

                        if (paired.Count > 0)
                        {
                            if (CloudBackend.UsePlatform)
                            {
                                // Pairing is idempotent server-side (upsert per server key),
                                // so unlike the Supabase path there is nothing to clear first.
                                foreach (var p in paired)
                                {
                                    await CloudApiClient.CallApiAsync("me/servers", HttpMethod.Post, payload: new
                                    {
                                        server_ip = p.Host,
                                        server_port = p.Port,
                                        name = p.Name,
                                        player_token = p.PlayerToken,
                                        steam_id = steamId,
                                    });
                                }
                            }
                            else
                            {
                                var serverModels = paired.Select(p => new UserServerModel
                                {
                                    UserId = userId,
                                    SteamId = steamId,
                                    ServerIp = p.Host,
                                    ServerPort = p.Port,
                                    PlayerToken = p.PlayerToken,
                                    UpdatedAt = DateTime.UtcNow
                                }).ToList();

                                // Delete old servers first to prevent accumulation
                                await client!.From<UserServerModel>().Where(x => x.UserId == userId).Delete();
                                await client.From<UserServerModel>().Upsert(serverModels);
                            }

                            SupabaseAuthManager.AppendLog($"[FcmSync] Synced {paired.Count} servers to cloud.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    SupabaseAuthManager.AppendLog($"[FcmSync] Failed to sync user servers: {ex.Message}");
                }

                if (stored)
                {
                    SupabaseAuthManager.AppendLog("[FcmSync] Successfully synced FCM credentials to the cloud.");
                    return true;
                }

                SupabaseAuthManager.AppendLog("[FcmSync] Failed to sync FCM credentials (no models returned).");
                return false;
            }
            catch (Exception ex)
            {
                SupabaseAuthManager.AppendLog($"[FcmSync] Exception during sync: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> RevokeFcmCredentialsAsync()
        {
            if (!SupabaseAuthManager.IsAuthenticated) return false;
            try
            {
                if (CloudBackend.UsePlatform)
                {
                    if (!CloudAuthManager.IsAuthenticated) return false;

                    // Pairings are addressed by id, so they have to be enumerated first.
                    foreach (var serverId in await FetchPairedServerIdsAsync())
                        await CloudApiClient.CallApiAsync($"me/servers/{serverId}", HttpMethod.Delete);

                    await CloudApiClient.CallApiAsync("me/fcm", HttpMethod.Delete);

                    SupabaseAuthManager.AppendLog("[FcmSync] Successfully revoked FCM credentials and deleted cloud servers.");
                    return true;
                }

                var client = SupabaseAuthManager.Client;
                var userId = client?.Auth?.CurrentUser?.Id;
                if (client is null || string.IsNullOrEmpty(userId)) return false;

                await client.From<UserServerModel>().Where(x => x.UserId == userId).Delete();
                await client.From<UserFcmCredentialsModel>().Where(x => x.UserId == userId).Delete();

                SupabaseAuthManager.AppendLog("[FcmSync] Successfully revoked FCM credentials and deleted cloud servers.");
                return true;
            }
            catch (Exception ex)
            {
                SupabaseAuthManager.AppendLog($"[FcmSync] Exception during revoke: {ex.Message}");
                return false;
            }
        }

        /// <summary>Ids of the caller's paired servers on the cloud platform.</summary>
        private static async Task<List<string>> FetchPairedServerIdsAsync()
        {
            var ids = new List<string>();

            var body = await CloudApiClient.CallApiAsync("me/servers", HttpMethod.Get);
            using var doc = System.Text.Json.JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return ids;
            }

            foreach (var server in data.EnumerateArray())
            {
                if (server.TryGetProperty("id", out var id) &&
                    id.ValueKind == System.Text.Json.JsonValueKind.String &&
                    id.GetString() is { Length: > 0 } value)
                {
                    ids.Add(value);
                }
            }

            return ids;
        }
    }
}
