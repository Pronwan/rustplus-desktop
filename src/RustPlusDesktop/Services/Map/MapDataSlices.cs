using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime;
using System.Text.Json;

namespace RustPlusDesk.Services.Map;

/// <summary>
/// The parts of map_data.json the 2D map reads, kept in files of their own.
///
/// map_data.json runs to ~90 MB, nearly all of it terrain for the 3D viewer. The 2D map needs
/// well under 1 MB of it, plus ~20 MB of heatmaps while one is shown. Parsing the whole file
/// with JsonDocument rents buffers of several times its size from the shared ArrayPool, which
/// holds on to them for minutes: that, not the map image, was most of the 1.3 GB a parsed map
/// cost. The slices are cut once per parse, by walking the file without building a document,
/// and read from then on.
/// </summary>
internal static class MapDataSlices
{
    public const string MapDataFileName = "map_data.json";

    // Versioned names: a slice that should carry more later gets a new name and is cut again.
    internal const string CoreFileName = "map_data.2d.v1.json";
    internal const string HeatmapsFileName = "map_data.heatmaps.v1.json";

    private static readonly HashSet<string> CoreProperties = new(StringComparer.Ordinal)
        { "size", "prefabs", "rockClusters", "cargoPaths", "cargoPathNodes", "keycardSites" };

    private static readonly HashSet<string> HeatmapProperties = new(StringComparer.Ordinal)
        { "size", "heatmaps", "cellLayerResolution", "cellHeatmapsB64" };

    private static readonly object Gate = new();

    /// <summary>The overlay slice of the map_data.json in <paramref name="folder"/>; null without one.</summary>
    public static string? EnsureCore(string folder) => Ensure(folder, CoreFileName);

    /// <summary>The heatmap slice of the map_data.json in <paramref name="folder"/>; null without one.</summary>
    public static string? EnsureHeatmaps(string folder) => Ensure(folder, HeatmapsFileName);

    /// <summary>The world size from the overlay slice, or 0.</summary>
    public static int ReadWorldSize(string folder)
    {
        if (EnsureCore(folder) is not { } core) return 0;
        using var fs = new FileStream(core, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var doc = JsonDocument.Parse(fs);
        return doc.RootElement.TryGetProperty("size", out var size) && size.TryGetInt32(out int s) ? s : 0;
    }

    private static string? Ensure(string folder, string sliceName)
    {
        string source = Path.Combine(folder, MapDataFileName);
        if (!File.Exists(source)) return null;

        string slice = Path.Combine(folder, sliceName);
        lock (Gate)
        {
            // A slice carries its source's timestamp; any other means the map was parsed again.
            DateTime written = File.GetLastWriteTimeUtc(source);
            if (File.Exists(slice) && File.GetLastWriteTimeUtc(slice) == written) return slice;

            // Both at once: the expensive part is reading the source, not writing the slices.
            var targets = new Dictionary<string, HashSet<string>>
            {
                [Path.Combine(folder, CoreFileName)] = CoreProperties,
                [Path.Combine(folder, HeatmapsFileName)] = HeatmapProperties,
            };
            Cut(source, targets);
            foreach (string target in targets.Keys)
                File.SetLastWriteTimeUtc(target, written);
            return slice;
        }
    }

    private static void Cut(string source, Dictionary<string, HashSet<string>> targets)
    {
        byte[] bytes = File.ReadAllBytes(source);
        var writers = new List<(string Final, string Temp, FileStream Stream, Utf8JsonWriter Writer, HashSet<string> Wanted)>();
        try
        {
            foreach (var (path, wanted) in targets)
            {
                string temp = path + ".tmp";
                var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
                var writer = new Utf8JsonWriter(stream);
                writer.WriteStartObject();
                writers.Add((path, temp, stream, writer, wanted));
            }

            ForEachTopLevelProperty(bytes, (name, value) =>
            {
                foreach (var w in writers)
                {
                    if (!w.Wanted.Contains(name)) continue;
                    w.Writer.WritePropertyName(name);
                    w.Writer.WriteRawValue(value, skipInputValidation: true);
                }
            });

            foreach (var w in writers)
            {
                w.Writer.WriteEndObject();
                w.Writer.Flush();
            }
        }
        finally
        {
            foreach (var w in writers)
            {
                w.Writer.Dispose();
                w.Stream.Dispose();
            }
        }

        foreach (var w in writers)
            File.Move(w.Temp, w.Final, overwrite: true);

        bytes = null!;
        ReturnLargeBuffers();
    }

    /// <summary>
    /// Copies map_data.json to <paramref name="target"/> with some top-level properties replaced
    /// or added, for the 3D viewer. Streams through the file instead of building a tree of it,
    /// which for this file cost well over a gigabyte.
    /// </summary>
    public static void RewriteWithOverrides(string source, string target, IReadOnlyDictionary<string, Action<Utf8JsonWriter>> overrides)
    {
        byte[] bytes = File.ReadAllBytes(source);
        string temp = target + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            ForEachTopLevelProperty(bytes, (name, value) =>
            {
                if (overrides.ContainsKey(name)) return;
                writer.WritePropertyName(name);
                writer.WriteRawValue(value, skipInputValidation: true);
            });
            foreach (var (name, write) in overrides)
            {
                writer.WritePropertyName(name);
                write(writer);
            }
            writer.WriteEndObject();
        }
        File.Move(temp, target, overwrite: true);

        bytes = null!;
        ReturnLargeBuffers();
    }

    internal delegate void PropertyVisitor(string name, ReadOnlySpan<byte> rawValue);

    /// <summary>Hands each top-level property's name and raw JSON value over, without parsing the values.</summary>
    internal static void ForEachTopLevelProperty(ReadOnlySpan<byte> json, PropertyVisitor visit)
    {
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new InvalidDataException("map_data.json root must be an object.");

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string name = reader.GetString() ?? string.Empty;
            reader.Read();
            int start = (int)reader.TokenStartIndex;
            reader.Skip();
            int end = (int)reader.BytesConsumed;
            visit(name, json.Slice(start, end - start));
        }
    }

    /// <summary>
    /// The source was read into one large array. Without this it sits on the large-object heap,
    /// which is not compacted, and the process keeps the memory long after it is garbage.
    /// </summary>
    private static void ReturnLargeBuffers()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
    }
}
