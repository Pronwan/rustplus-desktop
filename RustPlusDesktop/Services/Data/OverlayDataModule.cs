using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services.Data
{
    public static class OverlayDataModule
    {
        public static bool LastFetchHadError { get; private set; }

        // Freemium size limits
        private const int FREE_MAX_BYTES      = 300_000;   // 300 KB
        private const int SUPPORTER_MAX_BYTES = 3_000_000; // 3 MB

        public static OverlaySaveData? LoadLocalOverlay(string serverKey, ulong steamId)
        {
            var path = DataManager.GetOverlayJsonPath(serverKey, steamId);
            if (!File.Exists(path)) return null;

            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<OverlaySaveData>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OverlayDataModule] LoadLocalOverlay Error for {steamId}: {ex.Message}");
                return null;
            }
        }

        public static void SaveLocalOverlay(string serverKey, ulong steamId, OverlaySaveData data)
        {
            try
            {
                var path = DataManager.GetOverlayJsonPath(serverKey, steamId);
                var dir  = System.IO.Path.GetDirectoryName(path);
                if (dir != null) System.IO.Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OverlayDataModule] SaveLocalOverlay Error for {steamId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Uploads the overlay to Supabase Cloud.
        /// Works with both Discord-authenticated sessions AND the anon key (no Discord needed up to free limits).
        /// </summary>
        /// <param name="explicitWipe">If true, an empty overlay is intentionally uploaded (e.g. trash button).</param>
        public static async Task<bool> UploadOverlayAsync(string serverKey, ulong steamId, OverlaySaveData data, bool explicitWipe = false)
        {
            if (Auth.SupabaseAuthManager.Client == null) return false;
            if (!await Auth.SupabaseAuthManager.EnsureFreshSessionAsync()) return false;

            data.LastUpdatedUnix = DataManager.UnixNow();

            bool isEmpty = (data.Strokes?.Count ?? 0) == 0
                        && (data.Icons?.Count   ?? 0) == 0
                        && (data.Texts?.Count   ?? 0) == 0;

            // Wipe protection: never upload empty overlay unless it was intentional (trash button)
            if (isEmpty && !explicitWipe)
            {
                return false;
            }

            // Size limit check
            var mapJson = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
            int byteSize = Encoding.UTF8.GetByteCount(mapJson);
            bool hasSupportBenefit = Auth.SupabaseAuthManager.IsPremium;
            int maxBytes = hasSupportBenefit ? SUPPORTER_MAX_BYTES : FREE_MAX_BYTES;

            if (byteSize > maxBytes)
            {
                int kbSize  = byteSize / 1024;
                int kbLimit = maxBytes / 1024;
                AppendLog($"[overlay/cloud] Upload blocked: overlay is {kbSize} KB, limit is {kbLimit} KB " +
                          $"({(hasSupportBenefit ? "Supporter tier" : "Free tier")}).");
                return false;
            }

            try
            {
                // Use OnConflict upsert – no need for a pre-fetch to get existing ID.
                // The unique constraint on (server_key, steam_id) handles duplicates.
                var mapModel = new MapOverlayModel
                {
                    Id          = Guid.NewGuid().ToString(), // ignored on conflict – DB keeps existing PK
                    ServerKey   = serverKey,
                    SteamId     = steamId.ToString(),
                    OverlayData = mapJson,
                    UpdatedAt   = DateTime.UtcNow
                };

                await Auth.SupabaseAuthManager.Client
                    .From<MapOverlayModel>()
                    .Upsert(mapModel, new Postgrest.QueryOptions { OnConflict = "server_key, steam_id" });

                // Also keep smart_devices table in sync when overlay contains devices
                var dtoList = data.Devices ?? new System.Collections.Generic.List<ExportedDeviceDto>();
                if (dtoList.Count > 0)
                {
                    var devJson = JsonSerializer.Serialize(dtoList, new JsonSerializerOptions { WriteIndented = false });
                    var devModel = new SmartDeviceModel
                    {
                        Id         = Guid.NewGuid().ToString(),
                        ServerKey  = serverKey,
                        SteamId    = steamId.ToString(),
                        DeviceData = devJson,
                        UpdatedAt  = DateTime.UtcNow
                    };
                    await Auth.SupabaseAuthManager.Client
                        .From<SmartDeviceModel>()
                        .Upsert(devModel, new Postgrest.QueryOptions { OnConflict = "server_key, steam_id" });
                }

                // Always keep local cache updated
                SaveLocalOverlay(serverKey, steamId, data);
                return true;
            }
            catch (Exception ex)
            {
                AppendLog($"[overlay/cloud/err] UploadOverlay failed for {steamId}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Fetches overlay + devices from Supabase. Works with anon key (no Discord login required).
        /// Uses .Get() instead of .Single() to avoid PGRST116 "0 rows" exceptions.
        /// Returns null if nothing found or on error.
        /// </summary>
        public static async Task<OverlaySaveData?> FetchOverlayFromServerAsync(string serverKey, ulong steamId)
        {
            if (Auth.SupabaseAuthManager.Client == null) return null;
            if (!await Auth.SupabaseAuthManager.EnsureFreshSessionAsync()) return null;
            LastFetchHadError = false;

            OverlaySaveData data = new OverlaySaveData();
            bool foundData = false;

            // --- map_overlays (drawing strokes, icons, texts) ---
            try
            {
                var mapResult = await Auth.SupabaseAuthManager.Client
                    .From<MapOverlayModel>()
                    .Filter("server_key", Postgrest.Constants.Operator.Equals, serverKey)
                    .Filter("steam_id", Postgrest.Constants.Operator.Equals, steamId.ToString())
                    .Get();

                var mapRow = mapResult?.Models?.FirstOrDefault();
                if (mapRow != null && !string.IsNullOrEmpty(mapRow.OverlayData))
                {
                    var mapData = JsonSerializer.Deserialize<OverlaySaveData>(mapRow.OverlayData);
                    if (mapData != null)
                    {
                        data.Strokes         = mapData.Strokes  ?? data.Strokes;
                        data.Icons           = mapData.Icons    ?? data.Icons;
                        data.Texts           = mapData.Texts    ?? data.Texts;
                        data.LastUpdatedUnix = mapData.LastUpdatedUnix > 0
                            ? mapData.LastUpdatedUnix
                            : new DateTimeOffset(mapRow.UpdatedAt).ToUnixTimeSeconds();
                        if (mapData.Devices?.Count > 0)
                            data.Devices = mapData.Devices;
                        foundData = true;
                    }
                }
            }
            catch (Exception ex)
            {
                LastFetchHadError = true;
                AppendLog($"[overlay/cloud/err] FetchOverlay map_overlays failed for {steamId}: {ex.Message}");
            }

            // --- smart_devices (device list) ---
            try
            {
                var devResult = await Auth.SupabaseAuthManager.Client
                    .From<SmartDeviceModel>()
                    .Filter("server_key", Postgrest.Constants.Operator.Equals, serverKey)
                    .Filter("steam_id", Postgrest.Constants.Operator.Equals, steamId.ToString())
                    .Get();

                var devRow = devResult?.Models?.FirstOrDefault();
                if (devRow != null && !string.IsNullOrEmpty(devRow.DeviceData))
                {
                    var devs = JsonSerializer.Deserialize<System.Collections.Generic.List<ExportedDeviceDto>>(devRow.DeviceData);
                    if (devs?.Count > 0)
                    {
                        data.Devices = devs;
                        var deviceUpdatedUnix = new DateTimeOffset(devRow.UpdatedAt).ToUnixTimeSeconds();
                        if (deviceUpdatedUnix > data.LastUpdatedUnix)
                            data.LastUpdatedUnix = deviceUpdatedUnix;
                        foundData = true;
                    }
                }
            }
            catch (Exception ex)
            {
                LastFetchHadError = true;
                AppendLog($"[overlay/cloud/err] FetchOverlay smart_devices failed for {steamId}: {ex.Message}");
            }

            if (!foundData) return null;

            // Cache locally (preserve existing drawing if cloud only had devices)
            SaveLocalOverlay(serverKey, steamId, data);
            return data;
        }

        private static void AppendLog(string msg)
        {
            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (System.Windows.Application.Current.MainWindow is RustPlusDesk.Views.MainWindow mainWin)
                        mainWin.AppendLog(msg);
                });
            }
        }
    }
}
