using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
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

            var baseIcons = data.Icons.Where(icon =>
                icon.IconPath.Contains("base1.png") || icon.IconPath.Contains("base2.png")
            ).ToList();

            var nonBaseIcons = data.Icons.Where(icon =>
                !(icon.IconPath.Contains("base1.png") || icon.IconPath.Contains("base2.png"))
            ).ToList();

            // Client-side validations before uploading
            int baseCount = baseIcons.Count;
            int maxBases = Auth.SupabaseAuthManager.GetMaxBases();
            int maxScreenshots = Auth.SupabaseAuthManager.GetMaxScreenshotsPerBase();
            bool baseLimitHit = baseCount > maxBases;
            if (!baseLimitHit)
            {
                foreach (var icon in baseIcons)
                {
                    if (icon.Screenshots != null && icon.Screenshots.Count > maxScreenshots)
                    {
                        baseLimitHit = true;
                        break;
                    }
                }
            }

            var overlayOnlyData = new OverlaySaveData
            {
                LastUpdatedUnix = data.LastUpdatedUnix,
                Strokes = data.Strokes,
                Icons = nonBaseIcons,
                Texts = data.Texts,
                Devices = data.Devices
            };

            // Size limit check (excluding bases/screenshots)
            var mapJson = JsonSerializer.Serialize(overlayOnlyData, new JsonSerializerOptions { WriteIndented = false });
            int byteSize = Encoding.UTF8.GetByteCount(mapJson);
            int uncompressedSize = CalculateUncompressedSize(data);
            int maxBytes = Auth.SupabaseAuthManager.GetMaxOverlayBytes();
            bool overlayLimitHit = uncompressedSize > maxBytes;

            // Smart devices limit check
            var dtoList = data.Devices ?? new System.Collections.Generic.List<ExportedDeviceDto>();
            int maxDevices = Auth.SupabaseAuthManager.GetMaxDevices();
            int actualDeviceCount = DeviceDataModule.CountActualDevices(dtoList);
            bool deviceLimitHit = actualDeviceCount > maxDevices;

            // Log warnings for limit hits
            if (overlayLimitHit)
            {
                AppendLog($"[overlay/cloud] Map overlay size ({uncompressedSize / 1024} KB) exceeds limit ({maxBytes / 1024} KB) for {Auth.SupabaseAuthManager.CurrentTier} tier. Omitting from upload.");
            }
            if (baseLimitHit)
            {
                AppendLog($"[overlay/cloud] Base markers count ({baseCount}/{maxBases}) or screenshot limit exceeded for {Auth.SupabaseAuthManager.CurrentTier} tier. Omitting from upload.");
            }
            if (deviceLimitHit)
            {
                AppendLog($"[overlay/cloud] Smart devices count ({actualDeviceCount}/{maxDevices}) exceeds limit for {Auth.SupabaseAuthManager.CurrentTier} tier. Omitting from upload.");
            }

            try
            {
                var payload = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["server_key"] = serverKey,
                    ["steam_id"] = steamId.ToString()
                };

                bool hasUpdates = false;

                if (!overlayLimitHit)
                {
                    payload["map_overlay"] = new
                    {
                        overlay_data = mapJson,
                        uncompressed_size = uncompressedSize
                    };
                    hasUpdates = true;
                }

                if (!baseLimitHit)
                {
                    var baseJson = JsonSerializer.Serialize(baseIcons, new JsonSerializerOptions { WriteIndented = false });
                    payload["base_markers"] = new
                    {
                        marker_data = baseJson
                    };
                    hasUpdates = true;
                }

                if (!deviceLimitHit && dtoList.Count > 0)
                {
                    var devJson = JsonSerializer.Serialize(dtoList, new JsonSerializerOptions { WriteIndented = false });
                    payload["smart_devices"] = new
                    {
                        device_data = devJson
                    };
                    hasUpdates = true;
                }

                if (!hasUpdates)
                {
                    AppendLog("[overlay/cloud] Sync skipped: All modified components exceed tier limits.");
                    return false;
                }

                await Auth.SupabaseAuthManager.CallEdgeFunctionAsync("overlay", HttpMethod.Post, payload);

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

        public static int CalculateUncompressedSize(OverlaySaveData data)
        {
            var uncompressedStrokes = data.Strokes.Select(s => new
            {
                s.Color,
                s.Thickness,
                Points = s.Points.Select(p => new { X = p.X, Y = p.Y }).ToList()
            }).ToList();

            var nonBaseIcons = data.Icons.Where(icon =>
                !(icon.IconPath.Contains("base1.png") || icon.IconPath.Contains("base2.png"))
            ).ToList();

            var tempObj = new
            {
                data.LastUpdatedUnix,
                Strokes = uncompressedStrokes,
                Icons = nonBaseIcons,
                data.Texts,
                data.Devices
            };

            var json = JsonSerializer.Serialize(tempObj, new JsonSerializerOptions { WriteIndented = false });
            return Encoding.UTF8.GetByteCount(json);
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

            try
            {
                var queryParams = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["server_key"] = serverKey,
                    ["steam_id"] = steamId.ToString()
                };

                var body = await Auth.SupabaseAuthManager.CallEdgeFunctionAsync("overlay", HttpMethod.Get, null, queryParams);
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                // Parse map_overlay
                if (root.TryGetProperty("map_overlay", out var mapEl) && mapEl.ValueKind == JsonValueKind.Object)
                {
                    var mapRow = JsonSerializer.Deserialize<RustPlusDesk.Models.MapOverlayModel>(mapEl.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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

                // Parse base_markers
                if (root.TryGetProperty("base_markers", out var baseEl) && baseEl.ValueKind == JsonValueKind.Object)
                {
                    var baseRow = JsonSerializer.Deserialize<RustPlusDesk.Models.BaseMarkerModel>(baseEl.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (baseRow != null && !string.IsNullOrEmpty(baseRow.MarkerData))
                    {
                        var baseIcons = JsonSerializer.Deserialize<System.Collections.Generic.List<SavedIcon>>(baseRow.MarkerData);
                        if (baseIcons?.Count > 0)
                        {
                            if (data.Icons == null) data.Icons = new System.Collections.Generic.List<SavedIcon>();
                            data.Icons.AddRange(baseIcons);
                            
                            var baseUpdatedUnix = new DateTimeOffset(baseRow.UpdatedAt).ToUnixTimeSeconds();
                            if (baseUpdatedUnix > data.LastUpdatedUnix)
                                data.LastUpdatedUnix = baseUpdatedUnix;
                            foundData = true;
                        }
                    }
                }

                // Parse smart_devices
                if (root.TryGetProperty("smart_devices", out var devEl) && devEl.ValueKind == JsonValueKind.Object)
                {
                    var devRow = JsonSerializer.Deserialize<RustPlusDesk.Models.SmartDeviceModel>(devEl.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (devRow != null && !string.IsNullOrEmpty(devRow.DeviceData))
                    {
                        var devs = JsonSerializer.Deserialize<System.Collections.Generic.List<ExportedDeviceDto>>(devRow.DeviceData);
                        if (devs != null)
                        {
                            data.Devices = devs;
                            var deviceUpdatedUnix = new DateTimeOffset(devRow.UpdatedAt).ToUnixTimeSeconds();
                            if (deviceUpdatedUnix > data.LastUpdatedUnix)
                                data.LastUpdatedUnix = deviceUpdatedUnix;
                            foundData = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LastFetchHadError = true;
                AppendLog($"[overlay/cloud/err] FetchOverlay via Edge Function failed for {steamId}: {ex.Message}");
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
