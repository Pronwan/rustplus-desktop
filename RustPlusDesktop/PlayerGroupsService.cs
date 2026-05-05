using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services;

public static class PlayerGroupsService
{
    private static readonly string _dbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RustPlusDesk-Ryyott", "player_groups.json");

    private static List<PlayerGroup> _groups = new();
    private static readonly object _lock = new();

    public static event Action? OnGroupsChanged;

    public static IReadOnlyList<PlayerGroup> Groups
    {
        get { lock (_lock) return _groups.ToList(); }
    }

    static PlayerGroupsService() { Load(); }

    public static PlayerGroup? GetGroupForPlayer(string bmId)
    {
        if (string.IsNullOrEmpty(bmId)) return null;
        lock (_lock) return _groups.FirstOrDefault(g => g.BMIds.Contains(bmId));
    }

    public static PlayerGroup CreateGroup(string name, string colorHex = "#FF6B35", bool notify = false)
    {
        var group = new PlayerGroup
        {
            Name = string.IsNullOrWhiteSpace(name) ? "New Group" : name.Trim(),
            ColorHex = colorHex,
            NotifyOnOnline = notify
        };
        lock (_lock) _groups.Add(group);
        SaveAndNotify();
        return group;
    }

    public static bool RenameGroup(string groupId, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return false;
        lock (_lock)
        {
            var g = _groups.FirstOrDefault(x => x.Id == groupId);
            if (g == null) return false;
            g.Name = newName.Trim();
        }
        SaveAndNotify();
        return true;
    }

    public static bool SetGroupColor(string groupId, string colorHex)
    {
        lock (_lock)
        {
            var g = _groups.FirstOrDefault(x => x.Id == groupId);
            if (g == null) return false;
            g.ColorHex = colorHex;
        }
        SaveAndNotify();
        return true;
    }

    public static bool SetGroupNotify(string groupId, bool enabled)
    {
        lock (_lock)
        {
            var g = _groups.FirstOrDefault(x => x.Id == groupId);
            if (g == null) return false;
            g.NotifyOnOnline = enabled;
        }
        SaveAndNotify();
        return true;
    }

    public static bool DeleteGroup(string groupId)
    {
        bool removed;
        lock (_lock) removed = _groups.RemoveAll(g => g.Id == groupId) > 0;
        if (removed) SaveAndNotify();
        return removed;
    }

    /// <summary>Single-membership: removes the player from any existing group, then adds to target.</summary>
    public static bool AssignPlayerToGroup(string bmId, string groupId)
    {
        if (string.IsNullOrEmpty(bmId)) return false;
        lock (_lock)
        {
            foreach (var g in _groups) g.BMIds.Remove(bmId);
            var target = _groups.FirstOrDefault(x => x.Id == groupId);
            if (target == null) return false;
            target.BMIds.Add(bmId);
        }
        SaveAndNotify();
        return true;
    }

    public static bool RemovePlayerFromGroup(string bmId)
    {
        if (string.IsNullOrEmpty(bmId)) return false;
        bool changed = false;
        lock (_lock)
        {
            foreach (var g in _groups)
                if (g.BMIds.Remove(bmId)) changed = true;
        }
        if (changed) SaveAndNotify();
        return changed;
    }

    // ─── MAP PINS (per server) ───────────────────────────────────────────────

    public static GroupMapPin? GetMapPin(string groupId, string serverName)
    {
        if (string.IsNullOrEmpty(groupId) || string.IsNullOrEmpty(serverName)) return null;
        lock (_lock)
        {
            var g = _groups.FirstOrDefault(x => x.Id == groupId);
            return g?.MapPins.FirstOrDefault(p =>
                string.Equals(p.Server, serverName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static IReadOnlyList<(PlayerGroup Group, GroupMapPin Pin)> GetMapPinsForServer(string serverName)
    {
        if (string.IsNullOrEmpty(serverName)) return Array.Empty<(PlayerGroup, GroupMapPin)>();
        lock (_lock)
        {
            var result = new List<(PlayerGroup, GroupMapPin)>();
            foreach (var g in _groups)
            {
                var pin = g.MapPins.FirstOrDefault(p =>
                    string.Equals(p.Server, serverName, StringComparison.OrdinalIgnoreCase));
                if (pin != null) result.Add((g, pin));
            }
            return result;
        }
    }

    /// <summary>Adds or updates the pin for (group, server).</summary>
    public static bool SetMapPin(string groupId, string serverName, double x, double y)
    {
        if (string.IsNullOrEmpty(groupId) || string.IsNullOrEmpty(serverName)) return false;
        lock (_lock)
        {
            var g = _groups.FirstOrDefault(x => x.Id == groupId);
            if (g == null) return false;
            var existing = g.MapPins.FirstOrDefault(p =>
                string.Equals(p.Server, serverName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.X = x;
                existing.Y = y;
            }
            else
            {
                g.MapPins.Add(new GroupMapPin { Server = serverName, X = x, Y = y });
            }
        }
        SaveAndNotify();
        return true;
    }

    public static bool ClearMapPin(string groupId, string serverName)
    {
        if (string.IsNullOrEmpty(groupId) || string.IsNullOrEmpty(serverName)) return false;
        bool changed;
        lock (_lock)
        {
            var g = _groups.FirstOrDefault(x => x.Id == groupId);
            if (g == null) return false;
            changed = g.MapPins.RemoveAll(p =>
                string.Equals(p.Server, serverName, StringComparison.OrdinalIgnoreCase)) > 0;
        }
        if (changed) SaveAndNotify();
        return changed;
    }

    private static void Load()
    {
        try
        {
            if (!File.Exists(_dbPath)) return;
            var json = File.ReadAllText(_dbPath);
            var loaded = JsonSerializer.Deserialize<List<PlayerGroup>>(json);
            if (loaded != null)
                lock (_lock) _groups = loaded;
        }
        catch
        {
            lock (_lock) _groups = new();
        }
    }

    private static void SaveAndNotify()
    {
        try
        {
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            List<PlayerGroup> snapshot;
            lock (_lock) snapshot = _groups.ToList();
            File.WriteAllText(_dbPath,
                JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best-effort persistence */ }

        OnGroupsChanged?.Invoke();
    }
}
