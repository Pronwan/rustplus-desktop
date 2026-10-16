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
            Directory.CreateDirectory(DataManager.AppDir);
            var json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(DataManager.ProfilesPath, json);
        }

        private static bool? _missingAtFirstLoad;

        /// <summary>
        /// The profiles file did not exist when this run first read it.
        ///
        /// Told apart from an empty list on purpose: a file holding no servers is a user
        /// who removed them all, while no file at all is a deleted profile folder or a
        /// fresh install, the two cases where pulling the servers back from the cloud is
        /// what the user wants. Taken on the first read, before any save recreates it.
        /// </summary>
        public static bool ProfilesFileWasMissing => _missingAtFirstLoad == true;

        public static List<ServerProfile> LoadProfiles()
        {
            _missingAtFirstLoad ??= !File.Exists(DataManager.ProfilesPath);
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
