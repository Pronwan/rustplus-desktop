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

    private static readonly string _tombstonesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RustPlusDesk-Ryyott", "group_tombstones.json");

    private static List<PlayerGroup> _groups = new();
    private static List<GroupTombstone> _tombstones = new();
    private static readonly object _lock = new();

    public static event Action? OnGroupsChanged;

    public class GroupTombstone
    {
        public string Id { get; set; } = "";
        public long DeletedAtUnix { get; set; }
    }

    public static IReadOnlyList<PlayerGroup> Groups
    {
        get { lock (_lock) return _groups.ToList(); }
    }

    public static IReadOnlyList<GroupTombstone> Tombstones
    {
        get { lock (_lock) return _tombstones.ToList(); }
    }

    private static long UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

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
            NotifyOnOnline = notify,
            LastModifiedUnix = UnixNow()
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
            g.LastModifiedUnix = UnixNow();
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
            g.LastModifiedUnix = UnixNow();
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
            g.LastModifiedUnix = UnixNow();
        }
        SaveAndNotify();
        return true;
    }

    public static bool DeleteGroup(string groupId)
    {
        bool removed;
        lock (_lock)
        {
            removed = _groups.RemoveAll(g => g.Id == groupId) > 0;
            if (removed)
            {
                _tombstones.RemoveAll(t => t.Id == groupId);
                _tombstones.Add(new GroupTombstone { Id = groupId, DeletedAtUnix = UnixNow() });
                GcOldTombstones();
            }
        }
        if (removed) SaveAndNotify();
        return removed;
    }

    /// <summary>Single-membership: removes the player from any existing group, then adds to target.</summary>
    public static bool AssignPlayerToGroup(string bmId, string groupId)
    {
        if (string.IsNullOrEmpty(bmId)) return false;
        long now = UnixNow();
        lock (_lock)
        {
            foreach (var g in _groups)
            {
                if (g.BMIds.Remove(bmId)) g.LastModifiedUnix = now;
            }
            var target = _groups.FirstOrDefault(x => x.Id == groupId);
            if (target == null) return false;
            target.BMIds.Add(bmId);
            target.LastModifiedUnix = now;
        }
        SaveAndNotify();
        return true;
    }

    public static bool RemovePlayerFromGroup(string bmId)
    {
        if (string.IsNullOrEmpty(bmId)) return false;
        bool changed = false;
        long now = UnixNow();
        lock (_lock)
        {
            foreach (var g in _groups)
            {
                if (g.BMIds.Remove(bmId)) { g.LastModifiedUnix = now; changed = true; }
            }
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
            var g = _groups.FirstOrDefault(z => z.Id == groupId);
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
            g.LastModifiedUnix = UnixNow();
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
            var g = _groups.FirstOrDefault(z => z.Id == groupId);
            if (g == null) return false;
            changed = g.MapPins.RemoveAll(p =>
                string.Equals(p.Server, serverName, StringComparison.OrdinalIgnoreCase)) > 0;
            if (changed) g.LastModifiedUnix = UnixNow();
        }
        if (changed) SaveAndNotify();
        return changed;
    }

    // ─── Team-sync apply-remote helpers ─────────────────────────────────────

    /// <summary>Apply a tombstone received from a teammate. Returns true if it changed local state.</summary>
    public static bool ApplyRemoteGroupTombstone(string groupId, long deletedAtUnix)
    {
        if (string.IsNullOrEmpty(groupId)) return false;
        bool changed = false;
        lock (_lock)
        {
            var g = _groups.FirstOrDefault(x => x.Id == groupId);
            if (g != null && g.LastModifiedUnix < deletedAtUnix)
            {
                _groups.Remove(g);
                changed = true;
            }
            var existing = _tombstones.FirstOrDefault(t => t.Id == groupId);
            if (existing == null || existing.DeletedAtUnix < deletedAtUnix)
            {
                _tombstones.RemoveAll(t => t.Id == groupId);
                _tombstones.Add(new GroupTombstone { Id = groupId, DeletedAtUnix = deletedAtUnix });
                changed = true;
            }
            if (changed) GcOldTombstones();
        }
        return changed;
    }

    /// <summary>Apply a remote group definition. Returns true if it changed local state.</summary>
    public static bool ApplyRemoteGroup(PlayerGroup remote)
    {
        if (remote == null || string.IsNullOrEmpty(remote.Id)) return false;
        lock (_lock)
        {
            // Remote tombstone wins if newer than the incoming group's mtime.
            var tomb = _tombstones.FirstOrDefault(t => t.Id == remote.Id);
            if (tomb != null && tomb.DeletedAtUnix >= remote.LastModifiedUnix) return false;

            var existing = _groups.FirstOrDefault(g => g.Id == remote.Id);
            if (existing != null)
            {
                if (existing.LastModifiedUnix >= remote.LastModifiedUnix) return false;
                existing.Name = remote.Name;
                existing.ColorHex = remote.ColorHex;
                existing.NotifyOnOnline = remote.NotifyOnOnline;
                existing.BMIds = remote.BMIds?.ToList() ?? new List<string>();
                existing.MapPins = remote.MapPins?.Select(p => new GroupMapPin
                { Server = p.Server, X = p.X, Y = p.Y }).ToList() ?? new List<GroupMapPin>();
                existing.LastModifiedUnix = remote.LastModifiedUnix;
                return true;
            }

            _groups.Add(new PlayerGroup
            {
                Id = remote.Id,
                Name = remote.Name,
                ColorHex = remote.ColorHex,
                NotifyOnOnline = remote.NotifyOnOnline,
                BMIds = remote.BMIds?.ToList() ?? new List<string>(),
                MapPins = remote.MapPins?.Select(p => new GroupMapPin
                { Server = p.Server, X = p.X, Y = p.Y }).ToList() ?? new List<GroupMapPin>(),
                LastModifiedUnix = remote.LastModifiedUnix
            });
            return true;
        }
    }

    /// <summary>Persist + raise OnGroupsChanged after a batch of remote merges.</summary>
    public static void PersistAfterRemoteMerge()
    {
        SaveAndNotify();
    }

    private static void GcOldTombstones()
    {
        long cutoff = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds();
        _tombstones.RemoveAll(t => t.DeletedAtUnix < cutoff);
    }

    private static void Load()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                var json = File.ReadAllText(_dbPath);
                var loaded = JsonSerializer.Deserialize<List<PlayerGroup>>(json);
                if (loaded != null)
                    lock (_lock) _groups = loaded;
            }
            if (File.Exists(_tombstonesPath))
            {
                var json = File.ReadAllText(_tombstonesPath);
                var loaded = JsonSerializer.Deserialize<List<GroupTombstone>>(json);
                if (loaded != null)
                    lock (_lock) _tombstones = loaded;
                lock (_lock) GcOldTombstones();
            }
        }
        catch
        {
            lock (_lock) { _groups = new(); _tombstones = new(); }
        }
    }

    private static void SaveAndNotify()
    {
        try
        {
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            List<PlayerGroup> groupsSnap;
            List<GroupTombstone> tombSnap;
            lock (_lock)
            {
                groupsSnap = _groups.ToList();
                tombSnap = _tombstones.ToList();
            }
            File.WriteAllText(_dbPath,
                JsonSerializer.Serialize(groupsSnap, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(_tombstonesPath,
                JsonSerializer.Serialize(tombSnap, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best-effort persistence */ }

        OnGroupsChanged?.Invoke();
    }
}
