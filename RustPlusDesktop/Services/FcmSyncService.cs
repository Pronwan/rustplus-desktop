using System;
using System.IO;
using System.Threading.Tasks;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Auth;

namespace RustPlusDesk.Services
{
    public static class FcmSyncService
    {
        private static string ConfigPath => Path.Combine(
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

                var userId = SupabaseAuthManager.Client?.Auth?.CurrentUser?.Id;
                if (string.IsNullOrEmpty(userId))
                {
                    SupabaseAuthManager.AppendLog("[FcmSync] User not authenticated.");
                    return false;
                }

                var jsonText = await File.ReadAllTextAsync(ConfigPath);
                var fcmConfigObj = Newtonsoft.Json.Linq.JObject.Parse(jsonText);

                if (!string.IsNullOrEmpty(TrackingService.DiscordWebhookUrl))
                {
                    fcmConfigObj["discord_webhook_url"] = TrackingService.DiscordWebhookUrl;
                }

                if (!string.IsNullOrEmpty(TrackingService.SmartHomeWebhookUrl))
                {
                    fcmConfigObj["smart_home_webhook_url"] = TrackingService.SmartHomeWebhookUrl;
                }

                var model = new UserFcmCredentialsModel
                {
                    UserId = userId,
                    SteamId = steamId,
                    FcmConfig = fcmConfigObj,
                    UpdatedAt = DateTime.UtcNow
                };

                var response = await SupabaseAuthManager.Client.From<UserFcmCredentialsModel>().Upsert(model);
                
                // Sync User Servers for the Cloud Worker
                try
                {
                    var profiles = RustPlusDesk.Services.Data.ProfileDataModule.LoadProfiles();
                    if (profiles != null && profiles.Count > 0)
                    {
                        var serverModels = new System.Collections.Generic.List<UserServerModel>();
                        foreach (var p in profiles)
                        {
                            if (!string.IsNullOrEmpty(p.Host) && p.Port > 0 && !string.IsNullOrEmpty(p.PlayerToken) && p.PlayerToken != "offline")
                            {
                                serverModels.Add(new UserServerModel
                                {
                                    UserId = userId,
                                    SteamId = steamId,
                                    ServerIp = p.Host,
                                    ServerPort = p.Port,
                                    PlayerToken = p.PlayerToken,
                                    UpdatedAt = DateTime.UtcNow
                                });
                            }
                        }

                        if (serverModels.Count > 0)
                        {
                            // Delete old servers first to prevent accumulation
                            await SupabaseAuthManager.Client.From<UserServerModel>().Where(x => x.UserId == userId).Delete();
                            await SupabaseAuthManager.Client.From<UserServerModel>().Upsert(serverModels);
                            SupabaseAuthManager.AppendLog($"[FcmSync] Synced {serverModels.Count} servers to cloud.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    SupabaseAuthManager.AppendLog($"[FcmSync] Failed to sync user servers: {ex.Message}");
                }

                if (response != null && response.Models.Count > 0)
                {
                    SupabaseAuthManager.AppendLog("[FcmSync] Successfully synced FCM credentials to Supabase.");
                    return true;
                }
                else
                {
                    SupabaseAuthManager.AppendLog("[FcmSync] Failed to sync FCM credentials (no models returned).");
                    return false;
                }
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
                var userId = SupabaseAuthManager.Client?.Auth?.CurrentUser?.Id;
                if (string.IsNullOrEmpty(userId)) return false;

                await SupabaseAuthManager.Client.From<UserServerModel>().Where(x => x.UserId == userId).Delete();
                await SupabaseAuthManager.Client.From<UserFcmCredentialsModel>().Where(x => x.UserId == userId).Delete();
                
                SupabaseAuthManager.AppendLog("[FcmSync] Successfully revoked FCM credentials and deleted cloud servers.");
                return true;
            }
            catch (Exception ex)
            {
                SupabaseAuthManager.AppendLog($"[FcmSync] Exception during revoke: {ex.Message}");
                return false;
            }
        }
    }
}
