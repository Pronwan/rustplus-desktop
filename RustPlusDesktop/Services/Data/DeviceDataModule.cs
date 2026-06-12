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
        private static string? _lastSyncedDevicesJson;
        private static string? _lastSyncedServerKey;
        private static ulong _lastSyncedSteamId;

        public static ExportedDeviceDto MapDeviceToDto(SmartDevice d)
        {
            var dto = new ExportedDeviceDto
            {
                EntityId = d.EntityId,
                Kind = d.Kind,
                Name = d.Name,
                Alias = d.Alias,
                IsGroup = d.IsGroup,
                CustomIconId = d.CustomIconId,
                CustomIconShortName = d.CustomIconShortName
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
                IsMissing = !dto.IsGroup,
                CustomIconId = dto.CustomIconId,
                CustomIconShortName = dto.CustomIconShortName
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

        public static int CountActualDevices(IEnumerable<SmartDevice>? devices)
        {
            if (devices == null) return 0;
            int count = 0;
            foreach (var d in devices)
            {
                if (d.IsGroup)
                {
                    if (d.Children != null)
                    {
                        count += CountActualDevices(d.Children);
                    }
                }
                else
                {
                    count++;
                }
            }
            return count;
        }

        public static int CountActualDevices(IEnumerable<ExportedDeviceDto>? dtos)
        {
            if (dtos == null) return 0;
            int count = 0;
            foreach (var d in dtos)
            {
                if (d.IsGroup)
                {
                    if (d.Children != null)
                    {
                        count += CountActualDevices(d.Children);
                    }
                }
                else
                {
                    count++;
                }
            }
            return count;
        }

        public static List<ExportedDeviceDto> GetTrimmedDeviceList(List<ExportedDeviceDto> dtos, int maxDevices)
        {
            var result = new List<ExportedDeviceDto>();
            int currentCount = 0;
            foreach (var dto in dtos)
            {
                if (currentCount >= maxDevices)
                    break;

                if (dto.IsGroup)
                {
                    var trimmedGroup = CloneGroupWithLimit(dto, maxDevices - currentCount, out int added);
                    if (added > 0)
                    {
                        result.Add(trimmedGroup);
                        currentCount += added;
                    }
                }
                else
                {
                    result.Add(dto);
                    currentCount++;
                }
            }
            return result;
        }

        private static ExportedDeviceDto CloneGroupWithLimit(ExportedDeviceDto group, int remainingLimit, out int added)
        {
            added = 0;
            var newGroup = new ExportedDeviceDto
            {
                EntityId = group.EntityId,
                Kind = group.Kind,
                Name = group.Name,
                Alias = group.Alias,
                IsGroup = group.IsGroup,
                Children = new List<ExportedDeviceDto>()
            };

            if (group.Children != null)
            {
                foreach (var child in group.Children)
                {
                    if (added >= remainingLimit)
                        break;

                    if (child.IsGroup)
                    {
                        var subGroup = CloneGroupWithLimit(child, remainingLimit - added, out int subAdded);
                        if (subAdded > 0)
                        {
                            newGroup.Children.Add(subGroup);
                            added += subAdded;
                        }
                    }
                    else
                    {
                        newGroup.Children.Add(child);
                        added++;
                    }
                }
            }

            return newGroup;
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
                if (!await Auth.SupabaseAuthManager.EnsureFreshSessionAsync()) return 0;
                int maxDevices = Auth.SupabaseAuthManager.GetMaxDevices();
                int actualCount = CountActualDevices(dtoList);
                if (actualCount > maxDevices)
                {
                    AppendLog($"[devices/cloud] Sync skipped: Smart devices count ({actualCount}/{maxDevices}) exceeds limit for {Auth.SupabaseAuthManager.CurrentTier} tier.");
                }
                else
                {
                    try
                    {
                        var devJson = JsonSerializer.Serialize(dtoList, new JsonSerializerOptions { WriteIndented = false });

                        if (!explicitWipe &&
                            _lastSyncedDevicesJson == devJson &&
                            _lastSyncedServerKey == serverKey &&
                            _lastSyncedSteamId == steamId)
                        {
                            return 0; // Skip uploading if nothing has changed
                        }

                        var payload = new
                        {
                            server_key = serverKey,
                            steam_id = steamId.ToString(),
                            smart_devices = new
                            {
                                device_data = devJson
                            }
                        };
                        await Auth.SupabaseAuthManager.CallEdgeFunctionAsync("overlay", System.Net.Http.HttpMethod.Post, payload);

                        _lastSyncedDevicesJson = devJson;
                        _lastSyncedServerKey = serverKey;
                        _lastSyncedSteamId = steamId;

                        syncedCount = actualCount;
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"[Cloud/Error] Syncing devices to Supabase failed: {ex.Message}");
                    }
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
