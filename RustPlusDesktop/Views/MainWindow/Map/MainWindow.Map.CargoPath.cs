using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private class CargoPathHarborInfo
    {
        public double PosX { get; set; }
        public double PosY { get; set; }
        public double RotY { get; set; }
        public string Name { get; set; } = "Harbor";
    }

    private class CargoPathData
    {
        public List<Point> Waypoints { get; set; } = new();
        public List<List<Point>> Branches { get; set; } = new();
        public Point MarkerPosition { get; set; }
        public List<CargoPathHarborInfo> Harbors { get; set; } = new();
    }

    private CargoPathData? _cargoPathData;
    private readonly List<UIElement> _cargoPathElements = new();

    /// <summary>
    /// Same treatment as the keycard layers: ticking it before the map has been parsed
    /// offers the parse instead of being a dead control. Left enabled and only dimmed,
    /// because a disabled CheckBox never sees the click.
    /// </summary>
    private async void ChkCargoPath_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.ToggleButton box && box.IsChecked == true && _cargoPathData == null)
        {
            box.IsChecked = false;

            var profile = _vm?.Selected;
            if (profile == null) return;
            if (!await OfferMapParseForHeatmapAsync(profile)) return;

            // The parse loads the route; show it if one was actually found.
            if (_cargoPathData != null) box.IsChecked = true;
            return;
        }

        RedrawCargoPath();
    }

    private void ResetCargoPathForServerChange()
    {
        _cargoPathData = null;
        if (ChkCargoPath != null)
        {
            ChkCargoPath.IsChecked = false;
            ChkCargoPath.Opacity = 0.45;
            ChkCargoPath.ToolTip = RustPlusDesk.Properties.Resources.GetString("UiLayersNeedsParsedMap");
        }

        if (CargoPathLayer != null)
        {
            CargoPathLayer.Children.Clear();
        }
        _cargoPathElements.Clear();
        RedrawCargoPath();
    }

    private void LoadCachedCargoPathForCurrentServer()
    {
        try
        {
            if (_vm?.Selected == null)
            {
                LoadCargoPathForCurrentMap(null);
                return;
            }

            var profile = _vm.Selected;
            string folder = RustPlusDesk.Services.Map3DLocalBuildService.GetPreparedFolderPath(profile, profile.RustMapsMapId);
            _ = LoadCargoPathWhenReadyAsync(profile, folder);
        }
        catch
        {
            LoadCargoPathForCurrentMap(null);
        }
    }

    // Reads the map data off the UI thread, then applies it unless the server changed meanwhile.
    private async System.Threading.Tasks.Task LoadCargoPathWhenReadyAsync(RustPlusDesk.Models.ServerProfile profile, string folder)
    {
        var root = await RustPlusDesk.Services.Map.ParsedMapDataCache.LoadAsync(folder);
        await Dispatcher.InvokeAsync(() =>
        {
            if (!ReferenceEquals(_vm?.Selected, profile)) return;
            LoadCargoPathForCurrentMap(root.HasValue ? folder : null, root);
        });
    }

    // mapRoot: the parsed map's overlay data when the caller already has it (see ParsedMapDataCache).
    private void LoadCargoPathForCurrentMap(string? folderPath, JsonElement? mapRoot = null)
    {
        _cargoPathData = null;
        if (ChkCargoPath != null)
        {
            ChkCargoPath.Opacity = 0.45;
            ChkCargoPath.IsChecked = false;
            ChkCargoPath.ToolTip = RustPlusDesk.Properties.Resources.GetString("UiLayersNeedsParsedMap");
        }

        // ONLY use data from parsing - strictly DO NOT fall back if parsed data is not present
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            RedrawCargoPath();
            return;
        }

        string mapDataPath = System.IO.Path.Combine(folderPath, "map_data.json");
        if (!File.Exists(mapDataPath))
        {
            RedrawCargoPath();
            return;
        }

        try
        {
            if ((mapRoot ?? RustPlusDesk.Services.Map.ParsedMapDataCache.Load(folderPath)) is not { } root)
            {
                RedrawCargoPath();
                return;
            }

            double parsedWorldSize = root.TryGetProperty("size", out var sizeEl) ? sizeEl.GetDouble() : _worldSizeS;
            if (parsedWorldSize <= 0)
            {
                RedrawCargoPath();
                return;
            }

            // The route comes only from the map's CargoPath layer, surfaced by the
            // parser as cargoPaths / cargoPathNodes. There is deliberately no
            // generated fallback: a guessed route drawn over real map data is fiction.
            var (nodes, branches) = ReadAuthoredCargoRoutes(root);
            if (nodes == null || nodes.Count < 3)
            {
                RedrawCargoPath();
                return;
            }

            _cargoPathData = new CargoPathData
            {
                Waypoints = nodes,
                Branches = branches,
                MarkerPosition = PickCargoMarker(nodes),
                Harbors = ReadCargoHarbors(root)
            };

            if (ChkCargoPath != null)
            {
                ChkCargoPath.Opacity = 1.0;
                ChkCargoPath.IsChecked = true;
                // No reason to show: the Layers panel prints this tooltip under the toggle.
                ChkCargoPath.ToolTip = null;
            }

            RedrawCargoPath();
        }
        catch (Exception ex)
        {
            AppendLog($"[3D Map] Failed to load cargo path from parsed map: {ex.Message}");
            _cargoPathData = null;
            if (ChkCargoPath != null)
            {
                ChkCargoPath.Opacity = 0.45;
                ChkCargoPath.IsChecked = false;
                ChkCargoPath.ToolTip = RustPlusDesk.Properties.Resources.GetString("UiLayersNeedsParsedMap");
            }
            RedrawCargoPath();
        }
    }

    /// <summary>
    /// Reads the routes the map carries in its CargoPath layer (or procedurally generated paths).
    /// Path 0 is the main ocean patrol loop; subsequent paths are harbor docking approaches.
    /// </summary>
    private static (List<Point>? mainLoop, List<List<Point>> branches) ReadAuthoredCargoRoutes(JsonElement root)
    {
        static List<Point> ReadNodes(JsonElement arr)
        {
            var list = new List<Point>();
            foreach (var el in arr.EnumerateArray())
            {
                if (TryReadPoint(el, out double nx, out double ny)) list.Add(new Point(nx, ny));
            }
            return list;
        }

        var branches = new List<List<Point>>();
        List<Point>? mainLoop = null;

        if (root.TryGetProperty("cargoPaths", out var pathsEl) && pathsEl.ValueKind == JsonValueKind.Array)
        {
            bool first = true;
            foreach (var pEl in pathsEl.EnumerateArray())
            {
                if (!pEl.TryGetProperty("nodes", out var nodesEl) || nodesEl.ValueKind != JsonValueKind.Array) continue;
                var pts = ReadNodes(nodesEl);
                if (pts.Count < 2) continue;

                if (first)
                {
                    mainLoop = pts;
                    first = false;
                }
                else
                {
                    branches.Add(pts);
                }
            }
        }

        if ((mainLoop == null || mainLoop.Count < 3) &&
            root.TryGetProperty("cargoPathNodes", out var flatEl) && flatEl.ValueKind == JsonValueKind.Array)
        {
            mainLoop = ReadNodes(flatEl);
        }

        return (mainLoop != null && mainLoop.Count >= 3 ? mainLoop : null, branches);
    }

    /// <summary>
    /// Harbor monuments from the parsed prefab list. The prefab category is just
    /// "monument", so match on short name and asset path. Ferry terminals sit in the
    /// same asset folder but are not cargo berths.
    /// </summary>
    private static List<CargoPathHarborInfo> ReadCargoHarbors(JsonElement root)
    {
        var harbors = new List<CargoPathHarborInfo>();
        if (!root.TryGetProperty("prefabs", out var prefabs) || prefabs.ValueKind != JsonValueKind.Array)
            return harbors;

        foreach (var el in prefabs.EnumerateArray())
        {
            string name = ReadString(el, "i").ToLowerInvariant();
            if (name.Contains("ferry_terminal")) continue;

            string assetPath = ReadString(el, "p").ToLowerInvariant();
            bool isHarbor = System.Text.RegularExpressions.Regex.IsMatch(name, "^harbor_[0-9]+$")
                            || name.Contains("harbour")
                            || System.Text.RegularExpressions.Regex.IsMatch(assetPath, "/harbor(_[0-9]+)?\\.prefab$");
            if (!isHarbor || !TryReadPoint(el, out double x, out double y)) continue;

            harbors.Add(new CargoPathHarborInfo
            {
                PosX = x,
                PosY = y,
                RotY = el.TryGetProperty("ry", out var ryEl) ? ryEl.GetDouble() : 0.0,
                Name = name.Contains("harbor_1") ? "Harbor 1" : name.Contains("harbor_2") ? "Harbor 2" : "Harbor"
            });
        }
        return harbors;
    }

    /// <summary>Western-most waypoint, so the badge clears the map's corner furniture.</summary>
    private static Point PickCargoMarker(List<Point> waypoints)
    {
        var best = waypoints[0];
        foreach (var pt in waypoints) if (pt.X < best.X) best = pt;
        return best;
    }

    private void RedrawCargoPath()
    {
        if (CargoPathLayer == null) return;

        foreach (var el in _cargoPathElements)
        {
            CargoPathLayer.Children.Remove(el);
        }
        _cargoPathElements.Clear();

        if (_isShowingDeepSeaMap) return;
        if (ChkCargoPath?.IsChecked != true || _cargoPathData == null || _cargoPathData.Waypoints.Count < 3 || _worldSizeS <= 0 || _worldRectPx.Width <= 0)
            return;

        var waypoints = _cargoPathData.Waypoints;

        // 1. Draw dashed in-game ocean & harbor route
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var firstPt = CenteredRustToImagePx(waypoints[0].X, waypoints[0].Y);
            ctx.BeginFigure(firstPt, isFilled: false, isClosed: true);

            for (int i = 1; i < waypoints.Count; i++)
            {
                var pt = CenteredRustToImagePx(waypoints[i].X, waypoints[i].Y);
                ctx.LineTo(pt, isStroked: true, isSmoothJoin: true);
            }

            // Draw harbor docking approach branches
            if (_cargoPathData.Branches != null)
            {
                foreach (var branch in _cargoPathData.Branches)
                {
                    if (branch == null || branch.Count < 2) continue;
                    var bFirst = CenteredRustToImagePx(branch[0].X, branch[0].Y);
                    ctx.BeginFigure(bFirst, isFilled: false, isClosed: false);
                    for (int b = 1; b < branch.Count; b++)
                    {
                        var bPt = CenteredRustToImagePx(branch[b].X, branch[b].Y);
                        ctx.LineTo(bPt, isStroked: true, isSmoothJoin: true);
                    }
                }
            }
        }
        geometry.Freeze();

        var pathShape = new System.Windows.Shapes.Path
        {
            Data = geometry,
            Stroke = new SolidColorBrush(Color.FromArgb(170, 203, 213, 225)), // Slate-white #CBD5E1 ~65% opacity
            StrokeThickness = 2.0,
            StrokeDashArray = new DoubleCollection { 6, 4 },
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        };

        CargoPathLayer.Children.Add(pathShape);
        _cargoPathElements.Add(pathShape);

        // 2. Draw Cargo Ship Ocean Track Marker Badge (🚢)
        var markerPos = CenteredRustToImagePx(_cargoPathData.MarkerPosition.X, _cargoPathData.MarkerPosition.Y);
        var badge = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromRgb(225, 29, 72)), // In-game badge red #E11D48
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            ToolTip = "Cargo Ship Ocean & Docking Path",
            IsHitTestVisible = true,
            Effect = new DropShadowEffect
            {
                BlurRadius = 8,
                ShadowDepth = 2,
                Opacity = 0.5,
                Color = Colors.Black
            }
        };

        var badgeText = new TextBlock
        {
            Text = "🚢",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.Child = badgeText;

        Canvas.SetLeft(badge, markerPos.X - 12);
        Canvas.SetTop(badge, markerPos.Y - 12);
        CargoPathLayer.Children.Add(badge);
        _cargoPathElements.Add(badge);

        // 3. Draw Harbor Dock Berth Markers (⚓)
        foreach (var harbor in _cargoPathData.Harbors)
        {
            var harborPos = CenteredRustToImagePx(harbor.PosX, harbor.PosY);
            var dockBadge = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb(2, 132, 199)), // Harbor blue #0284C7
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1.5),
                ToolTip = $"Cargo Docking Berth: {harbor.Name}",
                IsHitTestVisible = true,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 6,
                    ShadowDepth = 1,
                    Opacity = 0.45,
                    Color = Colors.Black
                }
            };

            var dockText = new TextBlock
            {
                Text = "⚓",
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            dockBadge.Child = dockText;

            Canvas.SetLeft(dockBadge, harborPos.X - 10);
            Canvas.SetTop(dockBadge, harborPos.Y - 10);
            CargoPathLayer.Children.Add(dockBadge);
            _cargoPathElements.Add(dockBadge);
        }
    }
}
