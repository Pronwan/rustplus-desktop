using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services;

public static class ProfileManager
{
    private static readonly string ProfilesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RustPlusDesk", "profiles");
    public static string ProfilesRootPath => ProfilesDir;
    private static readonly string ProfilesIndexPath = Path.Combine(ProfilesDir, "profiles.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static ProfileList _profileList = new();

    public static Profile? CurrentProfile { get; private set; }

    public static void Initialize()
    {
        Directory.CreateDirectory(ProfilesDir);
        LoadProfiles();

        if (_profileList.Profiles.Count == 0)
        {
            var defaultProfile = CreateDefaultProfileWithMigration();
            SwitchToProfile(defaultProfile.Id);
        }
        else
        {
            var current = _profileList.Profiles.FirstOrDefault(p => p.Id == _profileList.CurrentProfileId)
                ?? _profileList.Profiles.First();
            SwitchToProfile(current.Id);
        }
    }

    /// <summary>
    /// Creates a brand-new empty profile with no data migration. Used when wiping the last profile
    /// so that old data is not copied back in and re-appear on next launch.
    /// </summary>
    private static Profile CreateFreshEmptyProfile()
    {
        var profile = new Profile
        {
            Name = "Default",
            Id = Guid.NewGuid().ToString()
        };
        Directory.CreateDirectory(profile.FolderPath);
        _profileList.Profiles.Add(profile);
        _profileList.CurrentProfileId = profile.Id;
        SaveProfiles();
        return profile;
    }

    private static Profile CreateDefaultProfileWithMigration()
    {
        var profile = new Profile
        {
            Name = "Default",
            Id = Guid.NewGuid().ToString()
        };

        Directory.CreateDirectory(profile.FolderPath);

        var globalBaseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RustPlusDesk");

        var languageSettingsPath = Path.Combine(globalBaseDir, "language_settings.json");
        var settingsPath = Path.Combine(globalBaseDir, "tracking_settings.json");

        if (!File.Exists(languageSettingsPath) && File.Exists(settingsPath))
        {
            try
            {
                var json = File.ReadAllText(settingsPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("SelectedLanguage", out var lang) && lang.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var langValue = lang.GetString();
                    if (!string.IsNullOrEmpty(langValue))
                    {
                        var langJson = System.Text.Json.JsonSerializer.Serialize(new { Language = langValue }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(languageSettingsPath, langJson);
                    }
                }
            }
            catch { }
        }

        var trackedPlayersPath = Path.Combine(globalBaseDir, "tracked_players.json");
        if (File.Exists(trackedPlayersPath))
        {
            try { File.Copy(trackedPlayersPath, Path.Combine(profile.FolderPath, "tracked_players.json"), true); } catch { }
        }

        if (File.Exists(settingsPath))
        {
            try { File.Copy(settingsPath, Path.Combine(profile.FolderPath, "tracking_settings.json"), true); } catch { }
        }

        var fcmConfigPath = Path.Combine(globalBaseDir, "rustplusjs-config.json");
        if (File.Exists(fcmConfigPath))
        {
            try { File.Copy(fcmConfigPath, Path.Combine(profile.FolderPath, "rustplusjs-config.json"), true); } catch { }
        }

        var serversPath = Path.Combine(globalBaseDir, "servers.json");
        if (File.Exists(serversPath))
        {
            try { File.Copy(serversPath, Path.Combine(profile.FolderPath, "servers.json"), true); } catch { }
        }

        var globalProfilesPath = Path.Combine(globalBaseDir, "profiles.json");
        if (File.Exists(globalProfilesPath))
        {
            try { File.Copy(globalProfilesPath, Path.Combine(profile.FolderPath, "profiles.json"), true); } catch { }
        }

        var overlaysPath = Path.Combine(globalBaseDir, "Overlays");
        if (Directory.Exists(overlaysPath))
        {
            try
            {
                var profileOverlaysPath = profile.OverlaysPath;
                Directory.CreateDirectory(profileOverlaysPath);
                CopyDirectoryRecursive(overlaysPath, profileOverlaysPath);
            }
            catch { }
        }

        var cachePath = Path.Combine(globalBaseDir, "cache");
        if (Directory.Exists(cachePath))
        {
            try
            {
                var profileCachePath = profile.CachePath;
                Directory.CreateDirectory(profileCachePath);
                CopyDirectoryRecursive(cachePath, profileCachePath);
            }
            catch { }
        }

        var legacyChatPath = Path.Combine(globalBaseDir, "chat");
        if (Directory.Exists(legacyChatPath))
        {
            try
            {
                var profileChatPath = profile.ChatCachePath;
                Directory.CreateDirectory(profileChatPath);
                CopyDirectoryRecursive(legacyChatPath, profileChatPath);
            }
            catch { }
        }

        _profileList.Profiles.Add(profile);
        _profileList.CurrentProfileId = profile.Id;
        SaveProfiles();
        return profile;
    }

    private static void LoadProfiles()
    {
        if (File.Exists(ProfilesIndexPath))
        {
            try
            {
                var json = File.ReadAllText(ProfilesIndexPath);
                _profileList = JsonSerializer.Deserialize<ProfileList>(json) ?? new ProfileList();
            }
            catch
            {
                _profileList = new ProfileList();
            }
        }
    }

    private static void SaveProfiles()
    {
        var json = JsonSerializer.Serialize(_profileList, JsonOptions);
        File.WriteAllText(ProfilesIndexPath, json);
    }

    /// <summary>
    /// Re-reads profiles.json from disk into the in-memory list.
    /// Call this after an external operation (e.g. backup restore) writes the index file directly.
    /// </summary>
    public static void ReloadFromDisk()
    {
        LoadProfiles();
    }

    public static Profile CreateProfile(string name, string steamId = "", string steamName = "")
    {
        var profile = new Profile
        {
            Name = name,
            SteamId = steamId,
            SteamName = steamName
        };
        _profileList.Profiles.Add(profile);
        Directory.CreateDirectory(profile.FolderPath);
        SaveProfiles();
        SwitchToProfile(profile.Id);
        return profile;
    }

    public static void SwitchToProfile(string profileId)
    {
        var profile = _profileList.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return;

        CurrentProfile = profile;
        _profileList.CurrentProfileId = profileId;
        profile.LastUsedAt = DateTime.UtcNow;
        SaveProfiles();
        TrackingService.ReloadForProfile();
    }

    public static void UpdateProfile(string profileId, string newName)
    {
        var profile = _profileList.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return;
        profile.Name = newName;
        SaveProfiles();
    }

    public static void DeleteProfile(string profileId)
    {
        DeleteProfile(profileId, true);
    }

    public static void DeleteProfileAllowLast(string profileId)
    {
        DeleteProfile(profileId, true);
    }

    private static void DeleteProfile(string profileId, bool allowLast)
    {
        if (_profileList.Profiles.Count <= 1 && !allowLast) return;

        var profile = _profileList.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return;

        _profileList.Profiles.Remove(profile);

        if (_profileList.CurrentProfileId == profileId)
        {
            var next = _profileList.Profiles.FirstOrDefault();
            if (next != null)
            {
                SwitchToProfile(next.Id);
            }
            else
            {
                CurrentProfile = null;
                _profileList.CurrentProfileId = "";
            }
        }

        if (Directory.Exists(profile.FolderPath))
        {
            try { Directory.Delete(profile.FolderPath, true); } catch { }
        }

        if (_profileList.Profiles.Count == 0)
        {
            // Use a clean empty profile — never migrate old data back when this is a wipe.
            var defaultProfile = CreateFreshEmptyProfile();
            SwitchToProfile(defaultProfile.Id);
        }

        SaveProfiles();
    }

    public static bool ProfileExists(string profileId)
    {
        return _profileList.Profiles.Any(p => p.Id == profileId);
    }

    public static List<Profile> GetAllProfiles()
    {
        return _profileList.Profiles.OrderBy(p => p.Name).ToList();
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string dest = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, dest, true);
        }
        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            string dest = Path.Combine(destinationDir, Path.GetFileName(subDir));
            CopyDirectoryRecursive(subDir, dest);
        }
    }
}
