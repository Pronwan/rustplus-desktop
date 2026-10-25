using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RustPlusDesk.Services;

/// <summary>
/// Rebuilds the monument list the Rust+ API would send, from a parsed map file, for maps
/// that have no server to ask (offline and manually parsed maps). Each monument prefab in
/// map_data.json is looked up in monument_metadata.json, whose landmarks carry the same
/// display tokens the API uses and their offset inside the prefab (train tunnel entrances
/// inside a monument, for example).
/// </summary>
public static class OfflineMonumentBuilder
{
    private const string MetadataResourceUri = "pack://application:,,,/Assets/Data/monument_metadata.json";

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<Landmark>>> s_metadata =
        new(LoadMetadataResource);

    public static IReadOnlyList<(double X, double Y, string Name)> BuildFromMapDataFile(string mapDataPath)
    {
        // Size and prefabs are all it reads, and the overlay slice carries both.
        string? core = Map.MapDataSlices.EnsureCore(Path.GetDirectoryName(mapDataPath) ?? string.Empty);
        if (core == null) return Array.Empty<(double, double, string)>();

        using var fs = new FileStream(core, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var doc = JsonDocument.Parse(fs);
        return Build(doc.RootElement, s_metadata.Value);
    }

    /// <summary>
    /// Returns monuments in Rust+ API coordinates (origin at the map corner) named with the
    /// API's display tokens, so the existing icons and labels apply unchanged.
    /// </summary>
    internal static IReadOnlyList<(double X, double Y, string Name)> Build(
        JsonElement mapData, IReadOnlyDictionary<string, IReadOnlyList<Landmark>> metadata)
    {
        var result = new List<(double X, double Y, string Name)>();
        if (!mapData.TryGetProperty("size", out var sizeEl) || !sizeEl.TryGetDouble(out double size) || size <= 0) return result;
        if (!mapData.TryGetProperty("prefabs", out var prefabs) || prefabs.ValueKind != JsonValueKind.Array) return result;

        double half = size * 0.5;
        foreach (var prefab in prefabs.EnumerateArray())
        {
            if (!TryGetDouble(prefab, "x", out double px) || !TryGetDouble(prefab, "y", out double pz)) continue;

            // The API marks each underwater lab moonpool as its own "DungeonBase" monument.
            if (string.Equals(GetString(prefab, "c"), "underwater-lab-base", StringComparison.OrdinalIgnoreCase)
                && GetString(prefab, "i").Contains("moonpool", StringComparison.OrdinalIgnoreCase))
            {
                result.Add((px + half, pz + half, "DungeonBase"));
                continue;
            }

            string path = GetString(prefab, "p");
            if (path.Length == 0 || !metadata.TryGetValue(path, out var landmarks)) continue;

            TryGetDouble(prefab, "ry", out double ryDeg);
            double ry = ryDeg * Math.PI / 180.0;
            double cos = Math.Cos(ry), sin = Math.Sin(ry);

            foreach (var landmark in landmarks)
            {
                // Unity rotation about Y applied to the landmark's offset inside the prefab.
                double wx = landmark.OffsetX * cos + landmark.OffsetZ * sin;
                double wz = -landmark.OffsetX * sin + landmark.OffsetZ * cos;
                result.Add((px + wx + half, pz + wz + half, landmark.Token));
            }
        }

        return result;
    }

    internal static IReadOnlyDictionary<string, IReadOnlyList<Landmark>> ParseMetadata(JsonElement root)
    {
        var map = new Dictionary<string, IReadOnlyList<Landmark>>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("prefabs", out var prefabs) || prefabs.ValueKind != JsonValueKind.Object) return map;

        foreach (var prefab in prefabs.EnumerateObject())
        {
            if (!prefab.Value.TryGetProperty("landmarks", out var landmarks) || landmarks.ValueKind != JsonValueKind.Array) continue;

            var list = new List<Landmark>();
            foreach (var landmark in landmarks.EnumerateArray())
            {
                if (!landmark.TryGetProperty("should_display_on_map", out var showEl) || showEl.ValueKind != JsonValueKind.True) continue;
                // Underwater labs come from their moonpool modules instead (see Build).
                if (GetString(landmark, "component_type") == "DungeonBaseLandmarkInfo") continue;

                string token = GetString(landmark, "display_token");
                if (token.Length == 0) continue;

                double ox = 0, oz = 0;
                if (landmark.TryGetProperty("local_matrix", out var matrix) && matrix.ValueKind == JsonValueKind.Array && matrix.GetArrayLength() >= 3)
                {
                    ox = ReadMatrixTranslation(matrix[0]);
                    oz = ReadMatrixTranslation(matrix[2]);
                }

                list.Add(new Landmark(token, ox, oz));
            }

            if (list.Count > 0) map[prefab.Name] = list;
        }

        return map;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<Landmark>> LoadMetadataResource()
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri(MetadataResourceUri));
            if (info == null) return new Dictionary<string, IReadOnlyList<Landmark>>();
            using var stream = info.Stream;
            using var doc = JsonDocument.Parse(stream);
            return ParseMetadata(doc.RootElement);
        }
        catch
        {
            return new Dictionary<string, IReadOnlyList<Landmark>>();
        }
    }

    private static double ReadMatrixTranslation(JsonElement row)
        => row.ValueKind == JsonValueKind.Array && row.GetArrayLength() >= 4 && row[3].TryGetDouble(out double v) ? v : 0;

    private static bool TryGetDouble(JsonElement el, string name, out double value)
    {
        value = 0;
        return el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out value);
    }

    private static string GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() ?? "" : "";

    internal readonly record struct Landmark(string Token, double OffsetX, double OffsetZ);
}
