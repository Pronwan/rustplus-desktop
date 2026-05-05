using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RustPlusDesk
{
    public class CustomCrosshair
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "CUSTOM";
        public string Base64Image { get; set; } = "";
    }

    public static class CustomCrosshairManager
    {
        private static readonly string SavePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RustPlusDesk-Ryyott", "custom_crosshairs.json");

        public static List<CustomCrosshair> LoadCrosshairs()
        {
            try
            {
                if (!File.Exists(SavePath)) return new List<CustomCrosshair>();
                var json = File.ReadAllText(SavePath);
                return JsonSerializer.Deserialize<List<CustomCrosshair>>(json) ?? new List<CustomCrosshair>();
            }
            catch
            {
                return new List<CustomCrosshair>();
            }
        }

        public static void SaveCrosshairs(List<CustomCrosshair> crosshairs)
        {
            try
            {
                var dir = Path.GetDirectoryName(SavePath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var json = JsonSerializer.Serialize(crosshairs, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SavePath, json);
            }
            catch
            {
                // Ignore for now
            }
        }
    }
}
