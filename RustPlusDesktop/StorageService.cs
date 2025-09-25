using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services;

public static class StorageService
{
    private static string AppDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustPlusDesk");

    private static string ProfilesPath => Path.Combine(AppDir, "profiles.json");

    public static void SaveProfiles(IEnumerable<ServerProfile> profiles)
    {
        Directory.CreateDirectory(AppDir);
        var json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ProfilesPath, json);
    }
    public static string GetProfilesPath() =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustPlusDesk", "profiles.json");

    public static List<ServerProfile> LoadProfiles()
    {
        if (!File.Exists(ProfilesPath)) return new List<ServerProfile>();
        try
        {
            var json = File.ReadAllText(ProfilesPath);
            var data = JsonSerializer.Deserialize<List<ServerProfile>>(json);
            return data ?? new List<ServerProfile>();
        }
        catch (Exception ex)
        {
            // ggf. mal ausgeben:
            Console.WriteLine("LoadProfiles-Fehler: " + ex);
            return new List<ServerProfile>();
        }
    }
}
