using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services.Data
{
    public static class DeviceDataModule
    {
        public static ExportedDeviceDto MapDeviceToDto(SmartDevice d)
        {
            var dto = new ExportedDeviceDto
            {
                EntityId = d.EntityId,
                Kind = d.Kind,
                Name = d.Name,
                Alias = d.Alias,
                IsGroup = d.IsGroup
            };

            if (d.IsGroup && d.Children != null && d.Children.Count > 0)
            {
                dto.Children = new List<ExportedDeviceDto>();
                foreach (var child in d.Children)
                {
                    dto.Children.Add(MapDeviceToDto(child));
                }
            }

            return dto;
        }

        public static SmartDevice MapDtoToDevice(ExportedDeviceDto dto)
        {
            var dev = new SmartDevice
            {
                EntityId = dto.EntityId,
                Kind = dto.Kind,
                Name = dto.Name,
                Alias = dto.Alias,
                IsGroup = dto.IsGroup,
                IsMissing = !dto.IsGroup
            };

            if (dto.Children != null && dto.Children.Count > 0)
            {
                foreach (var childDto in dto.Children)
                {
                    dev.Children.Add(MapDtoToDevice(childDto));
                }
            }

            return dev;
        }

        public static async Task<int> UploadDevicesSnapshotAsync(string serverKey, ulong steamId, IEnumerable<SmartDevice> devices, OverlaySaveData canvasOverlay, bool explicitWipe = false)
        {
            var dtoList = new List<ExportedDeviceDto>();
            foreach (var d in devices)
                dtoList.Add(MapDeviceToDto(d));

            // Wipe protection: never upload empty device list unless this is an intentional delete.
            if (dtoList.Count == 0 && !explicitWipe)
            {
                return 0;
            }

            // Freemium check
            var syncedCount = 0;
            if (Auth.SupabaseAuthManager.Client != null)
            {
                await Auth.SupabaseAuthManager.EnsureFreshSessionAsync();
                bool isPremium = Auth.SupabaseAuthManager.IsPremium;
                if (!isPremium && dtoList.Count > 10)
                {
                    dtoList = dtoList.GetRange(0, 10);
                }

                try
                {
                    // Upsert directly using OnConflict – no pre-fetch needed.
                    // Generates a new UUID on insert, keeps existing on conflict.
                    var devJson = JsonSerializer.Serialize(dtoList, new JsonSerializerOptions { WriteIndented = false });
                    var model = new SmartDeviceModel
                    {
                        Id         = Guid.NewGuid().ToString(),
                        ServerKey  = serverKey,
                        SteamId    = steamId.ToString(),
                        DeviceData = devJson,
                        UpdatedAt  = DateTime.UtcNow
                    };
                    await Auth.SupabaseAuthManager.Client.From<SmartDeviceModel>()
                        .Upsert(model, new Postgrest.QueryOptions { OnConflict = "server_key, steam_id" });
                    syncedCount = dtoList.Count;
                }
                catch (Exception ex)
                {
                    AppendLog($"[Cloud/Error] Syncing devices to Supabase failed: {ex.Message}");
                }
            }

            // Local JSON: merge devices into existing local overlay to preserve strokes/icons/texts.
            // Do NOT overwrite drawing data with empty strokes from this sync path.
            var localData = OverlayDataModule.LoadLocalOverlay(serverKey, steamId)
                         ?? new OverlaySaveData
                         {
                             Strokes         = canvasOverlay?.Strokes ?? new(),
                             Icons           = canvasOverlay?.Icons   ?? new(),
                             Texts           = canvasOverlay?.Texts   ?? new(),
                             LastUpdatedUnix = DataManager.UnixNow()
                         };

            localData.Devices.Clear();
            foreach (var d in dtoList)
                localData.Devices.Add(d);

            OverlayDataModule.SaveLocalOverlay(serverKey, steamId, localData);
            return syncedCount;
        }

        private static void AppendLog(string msg)
        {
            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (System.Windows.Application.Current.MainWindow is RustPlusDesk.Views.MainWindow mainWin)
                    {
                        mainWin.AppendLog(msg);
                    }
                });
            }
        }
    }
}
