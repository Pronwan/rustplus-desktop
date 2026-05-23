using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services.Data
{
    public static class OverlayDataModule
    {
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
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OverlayDataModule] SaveLocalOverlay Error for {steamId}: {ex.Message}");
            }
        }

        public static async Task UploadOverlayAsync(string serverKey, ulong steamId, OverlaySaveData data)
        {
            data.LastUpdatedUnix = DataManager.UnixNow();

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
            var rawBytes = Encoding.UTF8.GetBytes(json);
            if (rawBytes.Length > DataManager.OVERLAY_MAX_BYTES)
                throw new InvalidOperationException("Overlay drawing too big (>350KB).");

            var overlayB64 = Convert.ToBase64String(rawBytes);
            await DataManager.UploadPayloadAsync(steamId, serverKey, overlayB64);

            // Also keep local cache updated
            SaveLocalOverlay(serverKey, steamId, data);
        }

        public static async Task<OverlaySaveData?> FetchOverlayFromServerAsync(string serverKey, ulong steamId)
        {
            var b64 = await DataManager.FetchPayloadAsync(steamId, serverKey);
            if (string.IsNullOrEmpty(b64)) return null;

            var raw = Convert.FromBase64String(b64);
            if (raw.Length > DataManager.OVERLAY_MAX_BYTES)
                throw new InvalidOperationException("Fetched overlay data too big (>350KB).");

            var json = Encoding.UTF8.GetString(raw);
            var data = JsonSerializer.Deserialize<OverlaySaveData>(json);
            if (data == null) return null;

            // Cache it locally
            SaveLocalOverlay(serverKey, steamId, data);
            return data;
        }
    }
}
