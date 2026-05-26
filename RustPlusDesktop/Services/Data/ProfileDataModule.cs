using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services.Data
{
    public static class ProfileDataModule
    {
        public static void SaveProfiles(IEnumerable<ServerProfile> profiles)
        {
            var path = DataManager.ProfilesPath;
            var dir = Path.GetDirectoryName(path);
            if (dir != null) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        public static List<ServerProfile> LoadProfiles()
        {
            if (!File.Exists(DataManager.ProfilesPath)) return new List<ServerProfile>();
            try
            {
                var json = File.ReadAllText(DataManager.ProfilesPath);
                var data = JsonSerializer.Deserialize<List<ServerProfile>>(json);
                return data ?? new List<ServerProfile>();
            }
            catch (Exception ex)
            {
                Console.WriteLine("LoadProfiles Error: " + ex);
                return new List<ServerProfile>();
            }
        }
    }
}
