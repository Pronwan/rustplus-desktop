using System;
using System.Collections.Generic;
using System.IO;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Data;

namespace RustPlusDesk.Services;

public static class StorageService
{
    public static void SaveProfiles(IEnumerable<ServerProfile> profiles)
    {
        ProfileDataModule.SaveProfiles(profiles);
    }

    public static string GetProfilesPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustPlusDesk", "profiles.json");

    public static List<ServerProfile> LoadProfiles()
    {
        return ProfileDataModule.LoadProfiles();
    }

    public static void SaveCache<T>(string key, T data)
    {
        DataManager.SaveCache(key, data);
    }

    public static T? LoadCache<T>(string key)
    {
        return DataManager.LoadCache<T>(key);
    }
}

public record MiniMapSettings(int ShapeIndex, double Size, double Opacity, bool ShowTime);

