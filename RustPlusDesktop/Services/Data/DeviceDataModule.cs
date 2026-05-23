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

        public static async Task<int> UploadDevicesSnapshotAsync(string serverKey, ulong steamId, IEnumerable<SmartDevice> devices, OverlaySaveData canvasOverlay)
        {
            // 1) Set up payload with canvas drawing details
            var data = new OverlaySaveData
            {
                LastUpdatedUnix = DataManager.UnixNow(),
                Strokes = canvasOverlay?.Strokes ?? new(),
                Icons = canvasOverlay?.Icons ?? new(),
                Texts = canvasOverlay?.Texts ?? new()
            };

            // 2) Add recursive mapped devices
            data.Devices.Clear();
            foreach (var d in devices)
            {
                data.Devices.Add(MapDeviceToDto(d));
            }

            // 3) Serialize, validate sizes, encode base64
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
            var rawBytes = Encoding.UTF8.GetBytes(json);
            if (rawBytes.Length > DataManager.OVERLAY_MAX_BYTES)
                throw new InvalidOperationException("Device export payload too big (>350KB).");

            var overlayB64 = Convert.ToBase64String(rawBytes);

            // 4) Remote upload
            await DataManager.UploadPayloadAsync(steamId, serverKey, overlayB64);

            // 5) Local overlay JSON write
            OverlayDataModule.SaveLocalOverlay(serverKey, steamId, data);

            return data.Devices.Count;
        }
    }
}
