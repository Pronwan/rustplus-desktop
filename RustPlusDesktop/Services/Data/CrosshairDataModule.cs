using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RustPlusDesk.Services;

namespace RustPlusDesk.Services.Data
{
    public static class CrosshairDataModule
    {
        private static readonly string SavePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RustPlusDesk", "custom_crosshairs.json");

        public static List<CustomCrosshair> LoadCrosshairs()
        {
            try
            {
                if (!File.Exists(SavePath)) return new List<CustomCrosshair>();
                var json = File.ReadAllText(SavePath);
                return JsonSerializer.Deserialize<List<CustomCrosshair>>(json) ?? new List<CustomCrosshair>();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[CrosshairDataModule] LoadCrosshairs Error: " + ex.Message);
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
            catch (Exception ex)
            {
                Console.WriteLine("[CrosshairDataModule] SaveCrosshairs Error: " + ex.Message);
            }
        }
    }
}
