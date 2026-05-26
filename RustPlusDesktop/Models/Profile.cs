using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RustPlusDesk.Services;

namespace RustPlusDesk.Models;

public class Profile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("steamId")]
    public string SteamId { get; set; } = "";

    [JsonPropertyName("steamName")]
    public string SteamName { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("lastUsedAt")]
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

    public string FolderPath => Path.Combine(ProfileManager.ProfilesRootPath, Id);
    public string SettingsPath => Path.Combine(FolderPath, "settings.json");
    public string MarkersPath => Path.Combine(FolderPath, "markers.json");
    public string FcmConfigPath => Path.Combine(FolderPath, "rustplusjs-config.json");

    public static string GlobalIconsCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RustPlusDesk", "icons");

    public string CachePath => Path.Combine(FolderPath, "cache");
    public string OverlaysPath => Path.Combine(FolderPath, "Overlays");
    public string ChatCachePath => Path.Combine(FolderPath, "chat");

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrEmpty(Name)) return Name;
            if (!string.IsNullOrEmpty(SteamName)) return $"{SteamName} ({SteamId})";
            return $"Account {SteamId}";
        }
    }
}

public class ProfileList
{
    [JsonPropertyName("currentProfileId")]
    public string CurrentProfileId { get; set; } = "";

    [JsonPropertyName("profiles")]
    public List<Profile> Profiles { get; set; } = new();
}
