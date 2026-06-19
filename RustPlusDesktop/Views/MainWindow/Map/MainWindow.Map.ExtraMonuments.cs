using RustPlusDesk.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private const string ExtraMonumentsFileName = "map_extra_monuments.json";

    private void GenerateAndLoadExtraMonumentsForCurrentMap(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        try
        {
            string mapDataPath = Path.Combine(folderPath, "map_data.json");
            if (!File.Exists(mapDataPath)) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(mapDataPath));
            var root = doc.RootElement;
            double half = root.TryGetProperty("size", out var sizeEl) ? sizeEl.GetDouble() * 0.5 : _worldSizeS * 0.5;
            var extras = new List<ExtraMonument>();

            void AddPrefab(JsonElement el, string name)
            {
                if (!TryReadPoint(el, out double x, out double y)) return;
                extras.Add(new ExtraMonument { X = x + half, Y = y + half, Name = name });
            }

            if (root.TryGetProperty("prefabs", out var prefabs) && prefabs.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in prefabs.EnumerateArray())
                {
                    string c = ReadString(el, "c").ToLowerInvariant();
                    string i = ReadString(el, "i").ToLowerInvariant();
                    if (c == "iceberg") AddPrefab(el, "Iceberg");
                    else if (c == "tunnel-entrance") AddPrefab(el, "Tunnel Entrance");
                    else if (i.StartsWith("water_well", StringComparison.OrdinalIgnoreCase)) AddPrefab(el, "Water Well");
                    else if (i.StartsWith("ice_lake", StringComparison.OrdinalIgnoreCase)) AddPrefab(el, "Ice Lake");
                    else if (i.StartsWith("jungle_ruins", StringComparison.OrdinalIgnoreCase)) AddPrefab(el, "Jungle Ruins");
                    else if (i.StartsWith("cave_", StringComparison.OrdinalIgnoreCase)) AddPrefab(el, "Cave");
                }
            }

            if (root.TryGetProperty("rockClusters", out var rocks) && rocks.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in rocks.EnumerateArray())
                {
                    string kind = ReadString(el, "kind");
                    if (!kind.Contains("God Rock", StringComparison.OrdinalIgnoreCase) && !kind.Contains("Anvil Rock", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!TryReadPoint(el, out double x, out double y)) continue;
                    string name = kind.Contains("Anvil", StringComparison.OrdinalIgnoreCase) ? "Anvil Rock" : "God Rock";
                    extras.Add(new ExtraMonument { X = x + half, Y = y + half, Name = name });
                }
            }

            var filtered = extras
                .GroupBy(m => $"{m.Name}:{Math.Round(m.X / 20) * 20}:{Math.Round(m.Y / 20) * 20}")
                .Select(g => g.First())
                .OrderBy(m => m.Name)
                .ThenBy(m => m.X)
                .ToList();

            string outPath = Path.Combine(folderPath, ExtraMonumentsFileName);
            File.WriteAllText(outPath, JsonSerializer.Serialize(filtered, new JsonSerializerOptions { WriteIndented = true }));
            MergeExtraMonuments(filtered);
            BuildMonumentOverlays();
        }
        catch (Exception ex)
        {
            AppendLog($"[3D Map] Failed to generate extra monuments: {ex.Message}");
        }
    }

    private void MergeCachedExtraMonumentsForCurrentMap()
    {
        try
        {
            if (_vm?.Selected == null) return;
            string folder = Map3DLocalBuildService.GetPreparedFolderPath(_vm.Selected, _vm.Selected.RustMapsMapId);
            string path = Path.Combine(folder, ExtraMonumentsFileName);
            if (!File.Exists(path)) return;
            var extras = JsonSerializer.Deserialize<List<ExtraMonument>>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (extras != null) MergeExtraMonuments(extras);
        }
        catch { }
    }

    private void MergeExtraMonuments(IEnumerable<ExtraMonument> extras)
    {
        foreach (var extra in extras)
        {
            if (_monData.Any(m => string.Equals(m.Name, extra.Name, StringComparison.OrdinalIgnoreCase) && Math.Sqrt((m.X - extra.X) * (m.X - extra.X) + (m.Y - extra.Y) * (m.Y - extra.Y)) < 35))
                continue;
            _monData.Add((extra.X, extra.Y, extra.Name));
        }
    }

    private static bool TryReadPoint(JsonElement el, out double x, out double y)
    {
        x = y = 0;
        if (!el.TryGetProperty("x", out var xEl) || !el.TryGetProperty("y", out var yEl)) return false;
        x = xEl.GetDouble();
        y = yEl.GetDouble();
        return true;
    }

    private static string ReadString(JsonElement el, string name)
        => el.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private sealed class ExtraMonument
    {
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = "";
    }
}
