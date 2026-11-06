using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.Map;

/// <summary>
/// The part of a parsed map_data.json the 2D map's overlays read: size, prefabs, rock
/// clusters, the cargo route and keycard sites. The whole file runs to ~90 MB because it
/// also carries the 3D map's terrain layers, and the overlays used to parse all of it, on
/// the UI thread, once each - several seconds of a frozen window every time a generated
/// map loaded. This reads them, off the UI thread, from the slice <see cref="MapDataSlices"/>
/// cut of the file, and keeps only those few KB.
/// </summary>
public static class ParsedMapDataCache
{
    private sealed record Entry(string Path, DateTime WrittenUtc, JsonElement Root);

    private static readonly object Gate = new();
    private static Entry? _entry;

    /// <summary>The cached overlay data for this map folder, or null when it is not loaded (or stale).</summary>
    public static JsonElement? TryGetCached(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        string path = Path.Combine(folder, "map_data.json");
        lock (Gate)
        {
            if (_entry is { } e && string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)
                && File.Exists(path) && File.GetLastWriteTimeUtc(path) == e.WrittenUtc)
            {
                return e.Root;
            }
        }
        return null;
    }

    /// <summary>Forgets the loaded map, for when the app leaves it.</summary>
    public static void Clear()
    {
        lock (Gate) _entry = null;
    }

    /// <summary>Loads (or returns the cached) overlay data for this map folder off the calling thread.</summary>
    public static async Task<JsonElement?> LoadAsync(string? folder)
    {
        if (TryGetCached(folder) is { } cached) return cached;
        if (string.IsNullOrWhiteSpace(folder)) return null;
        return await Task.Run(() => Load(folder)).ConfigureAwait(false);
    }

    /// <summary>Synchronous load, for the rare caller that has no way to wait.</summary>
    public static JsonElement? Load(string folder)
    {
        if (TryGetCached(folder) is { } cached) return cached;

        string path = Path.Combine(folder, "map_data.json");
        if (!File.Exists(path)) return null;

        try
        {
            DateTime written = File.GetLastWriteTimeUtc(path);
            JsonElement root = ReadOverlayProperties(path);
            lock (Gate)
            {
                _entry = new Entry(path, written, root);
            }
            return root;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The entries of a prefab array file that <paramref name="keep"/> accepts, off the calling
    /// thread; null when missing or unreadable.
    ///
    /// Reads one entry at a time. Parsing map_resolved.json as a document rented buffers of
    /// several times its size from the shared ArrayPool, which keeps them after the document is
    /// disposed, where no collection can reach them: nearly 200 MB for a 19 MB file.
    /// </summary>
    public static Task<List<JsonElement>?> ReadPrefabsAsync(string path, Func<JsonElement, bool> keep) => Task.Run(async () =>
    {
        if (!File.Exists(path)) return null;
        try
        {
            var kept = new List<JsonElement>();
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            await foreach (var prefab in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(fs).ConfigureAwait(false))
            {
                if (keep(prefab)) kept.Add(prefab);
            }
            return kept;
        }
        catch
        {
            return (List<JsonElement>?)null;
        }
    });

    internal static JsonElement ReadOverlayProperties(string path)
    {
        // The overlay slice holds exactly these properties; the 90 MB file itself is never parsed.
        string core = MapDataSlices.EnsureCore(Path.GetDirectoryName(path) ?? string.Empty)
            ?? throw new FileNotFoundException("map_data.json was not found.", path);

        using var fs = new FileStream(core, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var slim = JsonDocument.Parse(fs);
        // Clone so the element outlives its document and needs no disposal.
        return slim.RootElement.Clone();
    }
}
