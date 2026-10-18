using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services.Data;

/// <summary>
/// Removes what the app stored on disk for a server profile that was deleted: its map
/// caches and 3D map data, chat history, deaths, wipe tracking, drawings and API caches.
/// Most of it is keyed by host and port rather than by profile, so it is only removed when
/// no remaining profile points at the same server. Offline maps all share 127.0.0.1:0 and
/// only lose their own 3D map folder.
/// </summary>
public static class ServerLocalDataCleanup
{
    public static int DeleteFor(ServerProfile deleted, IEnumerable<ServerProfile> remaining)
    {
        ArgumentNullException.ThrowIfNull(deleted);

        bool isOffline = deleted.Port == 0 || deleted.PlayerToken == "offline";
        bool serverStillUsed = remaining.Any(p => !ReferenceEquals(p, deleted)
            && string.Equals(p.Host, deleted.Host, StringComparison.OrdinalIgnoreCase)
            && p.Port == deleted.Port);

        return DeleteFor(
            deleted.Host ?? string.Empty,
            deleted.Port,
            isOffline || serverStillUsed,
            Map3DLocalBuildService.GetPreparedFolderPath(deleted, deleted.RustMapsMapId),
            DataManager.AppDir,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RustPlusDesk"));
    }

    /// <summary>
    /// Deletes the profile's own 3D map folder and, unless <paramref name="keepServerData"/>,
    /// everything stored under the server's host and port. Returns how many files and
    /// folders were removed. Each removal is best effort; a locked file is left behind.
    /// </summary>
    internal static int DeleteFor(string host, int port, bool keepServerData, string? ownMapFolder, string appDir, string localDir)
    {
        int removed = 0;
        if (!string.IsNullOrWhiteSpace(ownMapFolder)) removed += DeleteDirectory(ownMapFolder);
        if (keepServerData || string.IsNullOrWhiteSpace(host)) return removed;

        string dashKey = Safe($"{host}-{port}");
        string underscoreKey = Safe($"{host}_{port}");

        removed += DeleteFile(Path.Combine(appDir, "deaths", dashKey + ".jsonl"));
        removed += DeleteFile(Path.Combine(appDir, "deaths", dashKey + ".killers.jsonl"));
        removed += DeleteDirectory(Path.Combine(appDir, "player-wipes", dashKey));
        removed += DeleteDirectory(Path.Combine(appDir, "Overlays", dashKey));
        removed += DeleteMatching(Path.Combine(appDir, "cache"), underscoreKey + "_*.json");

        removed += DeleteFile(Path.Combine(localDir, "chat", underscoreKey + ".json"));
        removed += DeleteFile(Path.Combine(localDir, "chat", underscoreKey + "_clan.json"));
        removed += DeleteFile(Path.Combine(localDir, "map_cache", underscoreKey + ".json"));
        removed += DeleteFile(Path.Combine(localDir, "map_cache", underscoreKey + ".png"));
        removed += DeleteFile(Path.Combine(localDir, "map_cache", underscoreKey + "_custom.png"));

        // 3D map folders are named after the profile and wipe, so earlier wipes are found
        // by the server their manifest was prepared for.
        string mapsRoot = Path.Combine(appDir, "3DMaps");
        if (Directory.Exists(mapsRoot))
        {
            foreach (string folder in Directory.EnumerateDirectories(mapsRoot))
            {
                if (ManifestIsForServer(Path.Combine(folder, "map3d_manifest.json"), host, port))
                    removed += DeleteDirectory(folder);
            }
        }

        return removed;
    }

    private static bool ManifestIsForServer(string manifestPath, string host, int port)
    {
        if (!File.Exists(manifestPath)) return false;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = doc.RootElement;
            string? manifestHost = root.TryGetProperty("Host", out var hostEl) && hostEl.ValueKind == JsonValueKind.String ? hostEl.GetString() : null;
            int manifestPort = root.TryGetProperty("Port", out var portEl) && portEl.ValueKind == JsonValueKind.Number ? portEl.GetInt32() : -1;
            return manifestPort == port && string.Equals(manifestHost, host, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static int DeleteMatching(string directory, string pattern)
    {
        if (!Directory.Exists(directory)) return 0;
        int removed = 0;
        foreach (string file in Directory.EnumerateFiles(directory, pattern)) removed += DeleteFile(file);
        return removed;
    }

    private static int DeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return 0;
            File.Delete(path);
            return 1;
        }
        catch
        {
            return 0;
        }
    }

    private static int DeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return 0;
            Directory.Delete(path, recursive: true);
            return 1;
        }
        catch
        {
            return 0;
        }
    }

    private static string Safe(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }
}
