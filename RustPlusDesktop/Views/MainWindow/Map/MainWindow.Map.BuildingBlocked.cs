using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private const double BuildingBlockedMinZoneSize = 10.0;
    private BuildingBlockedData? _buildingBlockedData;
    private readonly List<Shape> _buildingBlockedZoneEls = new();

    private void ChkNoBuildZones_Checked(object sender, RoutedEventArgs e)
    {
        RedrawGrid();
    }
    private void ResetBuildingBlockedZonesForServerChange()
    {
        _buildingBlockedData = null;
        _currentMapFolderPath = null;
        if (ChkNoBuildZones != null)
        {
            ChkNoBuildZones.IsChecked = false;
            ChkNoBuildZones.Visibility = Visibility.Collapsed;
        }

        foreach (var shape in _buildingBlockedZoneEls)
        {
            GridLayer.Children.Remove(shape);
        }
        _buildingBlockedZoneEls.Clear();
        RedrawGrid();
    }

    public void ResetBuildingBlockedZonesAfterCacheDelete()
    {
        ResetBuildingBlockedZonesForServerChange();
    }

    private void LoadCachedBuildingBlockedZonesForCurrentServer()
    {
        try
        {
            if (_vm?.Selected == null)
            {
                LoadBuildingBlockedZonesForCurrentMap(null);
                return;
            }

            string folder = RustPlusDesk.Services.Map3DLocalBuildService.GetPreparedFolderPath(_vm.Selected, _vm.Selected.RustMapsMapId);
            LoadBuildingBlockedZonesForCurrentMap(folder);
        }
        catch
        {
            LoadBuildingBlockedZonesForCurrentMap(null);
        }
    }
    private void LoadBuildingBlockedZonesForCurrentMap(string? folderPath)
    {
        _buildingBlockedData = null;
        if (ChkNoBuildZones != null)
        {
            ChkNoBuildZones.Visibility = Visibility.Collapsed;
            ChkNoBuildZones.IsChecked = false;
        }

        if (string.IsNullOrWhiteSpace(folderPath))
        {
            RedrawGrid();
            return;
        }

        string path = System.IO.Path.Combine(folderPath, "building_blocked.json");
        if (!File.Exists(path))
        {
            RedrawGrid();
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            _buildingBlockedData = JsonSerializer.Deserialize<BuildingBlockedData>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch (Exception ex)
        {
            AppendLog($"[3D Map] Failed to load building blocked zones: {ex.Message}");
            _buildingBlockedData = null;
        }

        bool hasData = (_buildingBlockedData?.Spheres?.Count ?? 0) > 0 || (_buildingBlockedData?.Boxes?.Count ?? 0) > 0;
        if (ChkNoBuildZones != null)
        {
            ChkNoBuildZones.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;
            ChkNoBuildZones.IsChecked = false;
        }
        RedrawGrid();
    }

    private void RedrawBuildingBlockedZones()
    {
        foreach (var shape in _buildingBlockedZoneEls)
        {
            GridLayer.Children.Remove(shape);
        }
        _buildingBlockedZoneEls.Clear();

        if (ChkNoBuildZones?.IsChecked != true || _buildingBlockedData == null || _worldSizeS <= 0 || _worldRectPx.Width <= 0)
            return;

        double worldToPx = _worldRectPx.Width / _worldSizeS;
        var sphereFill = new SolidColorBrush(Color.FromArgb(40, 244, 63, 94));
        var sphereStroke = new SolidColorBrush(Color.FromArgb(120, 244, 63, 94));
        var boxFill = new SolidColorBrush(Color.FromArgb(40, 244, 63, 94));
        var boxStroke = new SolidColorBrush(Color.FromArgb(110, 244, 63, 94));

        foreach (var box in _buildingBlockedData.Boxes ?? Enumerable.Empty<BuildingBlockedBox>())
        {
            if (box.Corners == null || box.Corners.Count < 3) continue;
            var poly = new Polygon
            {
                Fill = boxFill,
                Stroke = boxStroke,
                StrokeThickness = 0.85,
                IsHitTestVisible = false
            };
            foreach (var corner in box.Corners)
            {
                poly.Points.Add(CenteredRustToImagePx(corner.X, corner.Y));
            }
            ToolTipService.SetToolTip(poly, BuildZoneTooltip(box.Owner, "No-build box"));
            GridLayer.Children.Add(poly);
            Panel.SetZIndex(poly, 40);
            _buildingBlockedZoneEls.Add(poly);
        }

        foreach (var sphere in _buildingBlockedData.Spheres ?? Enumerable.Empty<BuildingBlockedSphere>())
        {
            if (sphere.Radius <= 0) continue;
            var center = CenteredRustToImagePx(sphere.X, sphere.Y);
            double r = Math.Max(1.0, sphere.Radius * worldToPx);
            var ellipse = new Ellipse
            {
                Width = r * 2.0,
                Height = r * 2.0,
                Fill = sphereFill,
                Stroke = sphereStroke,
                StrokeThickness = 0.85,
                IsHitTestVisible = false
            };
            ToolTipService.SetToolTip(ellipse, BuildZoneTooltip(sphere.Owner, $"No-build radius {sphere.Radius:0}m"));
            Canvas.SetLeft(ellipse, center.X - r);
            Canvas.SetTop(ellipse, center.Y - r);
            GridLayer.Children.Add(ellipse);
            Panel.SetZIndex(ellipse, 41);
            _buildingBlockedZoneEls.Add(ellipse);
        }
    }

    private Point CenteredRustToImagePx(double x, double y)
    {
        double half = _worldSizeS * 0.5;
        return WorldToImagePx(x + half, y + half);
    }

    private static string BuildZoneTooltip(BuildingBlockedOwner? owner, string fallback)
    {
        if (owner == null) return fallback;
        return $"{fallback}\n{owner.Name}\n{owner.Type} / {owner.Prefab}";
    }

    private async System.Threading.Tasks.Task GenerateBuildingBlockedZonesForCurrentMap(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        string mapDataPath = System.IO.Path.Combine(folderPath, "map_data.json");
        if (!File.Exists(mapDataPath)) return;
        string outputPath = System.IO.Path.Combine(folderPath, "building_blocked.json");

        JsonDocument? doc = null;
        JsonDocument? resolvedDoc = null;
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/Data/monument_bounds.json");
            var streamInfo = Application.GetResourceStream(uri);
            if (streamInfo == null) return;
            using var reader = new StreamReader(streamInfo.Stream);
            var dbJson = await reader.ReadToEndAsync();
            var boundsDb = JsonSerializer.Deserialize<Dictionary<string, BoundsDBEntry>>(dbJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (boundsDb == null) return;

            doc = JsonDocument.Parse(File.ReadAllText(mapDataPath));
            if (!doc.RootElement.TryGetProperty("prefabs", out var prefabs) || prefabs.ValueKind != JsonValueKind.Array) return;

            var allPrefabs = new List<JsonElement>();
            foreach (var p in prefabs.EnumerateArray()) allPrefabs.Add(p);

            string resolvedPath = System.IO.Path.Combine(folderPath, "map_resolved.json");
            if (File.Exists(resolvedPath))
            {
                resolvedDoc = JsonDocument.Parse(File.ReadAllText(resolvedPath));
                if (resolvedDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in resolvedDoc.RootElement.EnumerateArray()) allPrefabs.Add(p);
                }
            }

            var finalSpheres = new List<BuildingBlockedSphere>();
            var finalBoxes = new List<BuildingBlockedBox>();

            foreach (var p in allPrefabs)
            {
                if (!p.TryGetProperty("i", out var iEl)) continue;
                string i = iEl.GetString()?.ToLowerInvariant() ?? "";
                string c = p.TryGetProperty("c", out var cEl) ? cEl.GetString()?.ToLowerInvariant() ?? "" : "";
                
                string dbKey = FindBuildingBlockedBoundsKey(boundsDb, i);
                
                if (string.IsNullOrEmpty(dbKey) || !boundsDb.TryGetValue(dbKey, out var entry)) continue;
                
                double px = p.TryGetProperty("x", out var xEl) ? xEl.GetDouble() : 0;
                double pz = p.TryGetProperty("y", out var yEl) ? yEl.GetDouble() : (p.TryGetProperty("z", out var zEl) ? zEl.GetDouble() : 0);
                double rotY = p.TryGetProperty("r", out var rEl) ? rEl.GetDouble() : (p.TryGetProperty("ry", out var ryEl) ? ryEl.GetDouble() : 0);
                
                double angle = -rotY * Math.PI / 180.0;
                double cosA = Math.Cos(angle);
                double sinA = Math.Sin(angle);

                string niceName = i.Replace("_", " ");
                if (niceName == "compound") niceName = "Outpost";
                else niceName = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(niceName);

                var owner = new BuildingBlockedOwner { Name = niceName, Prefab = i, Type = c };

                if (entry.Spheres != null)
                {
                    foreach (var s in entry.Spheres)
                    {
                        double dx = s.X * cosA - s.Z * sinA;
                        double dz = s.X * sinA + s.Z * cosA;
                        finalSpheres.Add(new BuildingBlockedSphere { X = Math.Round(px + dx, 2), Y = Math.Round(pz + dz, 2), Radius = s.R, Owner = owner });
                    }
                }
                
                if (entry.Boxes != null)
                {
                    foreach (var b in entry.Boxes)
                    {
                        if (b.Corners == null) continue;
                        var corners = new List<BuildingBlockedPoint>();
                        foreach (var lc in b.Corners)
                        {
                            double dx = lc.X * cosA - lc.Z * sinA;
                            double dz = lc.X * sinA + lc.Z * cosA;
                            corners.Add(new BuildingBlockedPoint { X = Math.Round(px + dx, 2), Y = Math.Round(pz + dz, 2) });
                        }
                        finalBoxes.Add(new BuildingBlockedBox { Corners = corners, Owner = owner });
                    }
                }
            }

            finalSpheres = DedupeBuildingBlockedSpheres(finalSpheres);
            finalBoxes = DedupeBuildingBlockedBoxes(finalBoxes);
            var resultData = new BuildingBlockedData { Spheres = finalSpheres, Boxes = finalBoxes };
            var outputJson = JsonSerializer.Serialize(resultData, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(outputPath, outputJson);
        }
        catch (Exception ex)
        {
            AppendLog($"[3D Map] Failed to generate building_blocked.json: {ex.Message}");
        }
        finally
        {
            doc?.Dispose();
            resolvedDoc?.Dispose();
        }
    }


    private static string FindBuildingBlockedBoundsKey(Dictionary<string, BoundsDBEntry> boundsDb, string prefabId)
    {
        if (string.IsNullOrWhiteSpace(prefabId)) return "";
        if (boundsDb.ContainsKey(prefabId)) return prefabId;

        string withoutVariant = System.Text.RegularExpressions.Regex.Replace(prefabId, @"_[a-e]$", "");
        if (boundsDb.ContainsKey(withoutVariant)) return withoutVariant;

        return "";
    }

    private static double GetBoundsBoxMaxSide(List<BoundsDBPoint> corners)
    {
        if (corners.Count < 2) return 0;
        double max = 0;
        for (int i = 0; i < corners.Count; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % corners.Count];
            max = Math.Max(max, Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Z - a.Z, 2)));
        }
        return max;
    }

    private static double GetWorldBoxMaxSide(List<BuildingBlockedPoint> corners)
    {
        if (corners.Count < 2) return 0;
        double max = 0;
        for (int i = 0; i < corners.Count; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % corners.Count];
            max = Math.Max(max, Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2)));
        }
        return max;
    }

    private static List<BuildingBlockedSphere> DedupeBuildingBlockedSpheres(List<BuildingBlockedSphere> spheres)
    {
        var result = new List<BuildingBlockedSphere>();
        foreach (var sphere in spheres)
        {
            if (sphere.Radius * 2.0 < BuildingBlockedMinZoneSize) continue;
            bool exists = result.Any(other =>
                string.Equals(other.Owner?.Prefab, sphere.Owner?.Prefab, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(other.X - sphere.X) < 1.0 &&
                Math.Abs(other.Y - sphere.Y) < 1.0 &&
                Math.Abs(other.Radius - sphere.Radius) < 1.0);
            if (!exists) result.Add(sphere);
        }
        return result;
    }

    private static List<BuildingBlockedBox> DedupeBuildingBlockedBoxes(List<BuildingBlockedBox> boxes)
    {
        var result = new List<BuildingBlockedBox>();
        foreach (var box in boxes)
        {
            if (box.Corners == null || box.Corners.Count < 4 || GetWorldBoxMaxSide(box.Corners) < BuildingBlockedMinZoneSize) continue;
            double cx = box.Corners.Average(point => point.X);
            double cy = box.Corners.Average(point => point.Y);
            double side = GetWorldBoxMaxSide(box.Corners);
            bool exists = result.Any(other =>
            {
                if (other.Corners == null) return false;
                double ox = other.Corners.Average(point => point.X);
                double oy = other.Corners.Average(point => point.Y);
                double otherSide = GetWorldBoxMaxSide(other.Corners);
                return string.Equals(other.Owner?.Prefab, box.Owner?.Prefab, StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs(ox - cx) < 1.0 &&
                    Math.Abs(oy - cy) < 1.0 &&
                    Math.Abs(otherSide - side) < 1.0;
            });
            if (!exists) result.Add(box);
        }
        return result;
    }
    private sealed class BoundsDBEntry
    {
        [JsonPropertyName("spheres")] public List<BoundsDBSphere>? Spheres { get; set; }
        [JsonPropertyName("boxes")] public List<BoundsDBBox>? Boxes { get; set; }
    }

    private sealed class BoundsDBSphere
    {
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("z")] public double Z { get; set; }
        [JsonPropertyName("r")] public double R { get; set; }
    }

    private sealed class BoundsDBBox
    {
        [JsonPropertyName("corners")] public List<BoundsDBPoint>? Corners { get; set; }
    }

    private sealed class BoundsDBPoint
    {
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("z")] public double Z { get; set; }
    }

    private sealed class BuildingBlockedData
    {
        [JsonPropertyName("spheres")] public List<BuildingBlockedSphere>? Spheres { get; set; }
        [JsonPropertyName("boxes")] public List<BuildingBlockedBox>? Boxes { get; set; }
    }

    private sealed class BuildingBlockedSphere
    {
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("radius")] public double Radius { get; set; }
        [JsonPropertyName("owner")] public BuildingBlockedOwner? Owner { get; set; }
    }

    private sealed class BuildingBlockedBox
    {
        [JsonPropertyName("corners")] public List<BuildingBlockedPoint>? Corners { get; set; }
        [JsonPropertyName("owner")] public BuildingBlockedOwner? Owner { get; set; }
    }

    private sealed class BuildingBlockedPoint
    {
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
    }

    private sealed class BuildingBlockedOwner
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("prefab")] public string Prefab { get; set; } = "";
    }
}



