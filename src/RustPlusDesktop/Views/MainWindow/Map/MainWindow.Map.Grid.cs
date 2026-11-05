using RustPlusDesk.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private DrawingBrush? _oceanGridBrush;
    private MapGridDrawing? _oceanGridDrawing;
    private double _oceanGridScale;
    private double _oceanGridOpacity;
    private bool _oceanGridAliased;

    private void RefreshMapGridViewport(object? sender = null, EventArgs? e = null)
    {
        var drawing = GridLayer?.Drawing;
        var background = _isShowingDeepSeaMap ? Color.FromRgb(24, 43, 73) : Color.FromRgb(11, 58, 74);
        if (drawing == null || ChkGrid?.IsChecked != true)
        {
            if (WebViewHost.Background is not SolidColorBrush solid || solid.Color != background)
                WebViewHost.Background = new SolidColorBrush(background);
            return;
        }
        double scale = GetEffectiveZoom();
        double opacity = TrackingService.MapGridOpacity;
        bool aliased = TrackingService.MapUseAliasedEdgeMode;
        if (_oceanGridBrush == null || !ReferenceEquals(_oceanGridDrawing, drawing) || _oceanGridScale != scale
            || _oceanGridOpacity != opacity || _oceanGridAliased != aliased)
        {
            _oceanGridBrush = MapGridVisual.CreateOceanBrush(drawing, scale, aliased, opacity, background);
            _oceanGridDrawing = drawing;
            _oceanGridScale = scale;
            _oceanGridOpacity = opacity;
            _oceanGridAliased = aliased;
        }
        var (s, offX, offY) = GetViewboxScaleAndOffset();
        var matrix = MapTransform.Matrix;
        matrix.Scale(s, s);
        matrix.Translate(offX, offY);
        ((MatrixTransform)_oceanGridBrush.Transform).Matrix = matrix;
        WebViewHost.Background = _oceanGridBrush;
        if (WebViewHost.ActualWidth > 0 && WebViewHost.ActualHeight > 0)
            GridLayer!.SetViewport(new Rect(HostToScenePreTransform(new Point()),
                HostToScenePreTransform(new Point(WebViewHost.ActualWidth, WebViewHost.ActualHeight))));
    }

    private void ChkGrid_Checked(object sender, RoutedEventArgs e)
    {
        RedrawGrid();
        UpdateSelectAllState();
        SyncMapLayersTo3D();
    }

    private void RedrawMapReferenceLayers()
    {
        RedrawGrid();
        RedrawBuildingBlockedZones();
        RedrawCargoPath();
        RedrawKeycards();
    }

    private void RedrawGrid()
    {
        ApplyIndependentLayerVisibility();

        bool wanted = ChkGrid.IsChecked == true || MiniMapWantsGrid;
        if (!wanted || _worldSizeS <= 0 || _worldRectPx.Width <= 0)
        {
            GridLayer.SetDrawing(null);
            RefreshMapGridViewport();
            RefreshMiniMapLayers();
            return;
        }

        int cells = _isShowingDeepSeaMap ? DeepSeaCells : GridCellCount(_worldSizeS);
        Point topLeft, bottomRight;
        if (_isShowingDeepSeaMap)
        {
            var (minX, maxX, minY, maxY) = GetDeepSeaWorldBox();
            topLeft = WorldToImagePx(minX, maxY);
            bottomRight = WorldToImagePx(maxX, minY);
        }
        else
        {
            topLeft = WorldToImagePx(0, _worldSizeS);
            bottomRight = WorldToImagePx(_worldSizeS, 0);
        }

        var bounds = new Rect(topLeft, bottomRight);
        var size = new Size(GridLayer.Width, GridLayer.Height);
        var previous = GridLayer.Drawing;
        if (bounds.Width <= 0 || bounds.Height <= 0 || size.Width <= 0 || size.Height <= 0) return;
        if (previous == null || previous.Bounds != bounds || previous.Cells != cells
            || previous.MapSize != size || previous.IsDeepSea != _isShowingDeepSeaMap)
        {
            GridLayer.SetDrawing(new MapGridDrawing(bounds, size, cells, _isShowingDeepSeaMap));
            RefreshMiniMapLayers();
        }
        RefreshGridLineThickness();
    }

    public void RefreshGridLineThickness()
    {
        if (GridLayer == null || MapTransform == null) return;
        GridLayer.SetViewScale(GetEffectiveZoom(), TrackingService.MapUseAliasedEdgeMode);
        RefreshMapGridViewport();
    }

    internal static string ColumnLabel(int index)
    {
        var s = "";
        index++;
        while (index > 0)
        {
            index--;
            s = (char)('A' + (index % 26)) + s;
            index /= 26;
        }
        return s;
    }

    /// <summary>
    /// How many cells a map of this size is divided into.
    ///
    /// 150 is what Rust divides by to get the count — it is not the width of a
    /// cell. A 4250 map gets 29 (A..AC), which is what the overlay has always
    /// drawn correctly.
    /// </summary>
    internal static int GridCellCount(double worldSize)
        => Math.Max(1, (int)Math.Ceiling(worldSize / 150.0));

    /// <summary>
    /// How wide one of those cells actually is.
    ///
    /// The cells are spread evenly across the map, so this is only 150 when the
    /// world size divides by 150 exactly. It does on 3000 and 4500, which is why
    /// treating every cell as 150 went unnoticed; a 4250 map has 29 cells of
    /// 146.55, and drawing them 150 wide spread the grid over 4350 units of a
    /// 4250 map. The boundaries drift outward as you go, so a death the game put
    /// in E24 was reported as D23.
    /// </summary>
    internal static double GridCellSize(double worldSize)
        => worldSize / GridCellCount(worldSize);

    private bool TryGetGridRef(double x, double y, out string label)
    {
        label = "";
        if (_worldSizeS <= 0) return false;

        if (x < -1000)
        {
            var (dsMinX, _, _, dsMaxY) = GetDeepSeaWorldBox();
            int col = (int)Math.Floor((x - dsMinX) / DeepSeaCellSize);
            int row = (int)Math.Floor((dsMaxY - y) / DeepSeaCellSize);
            label = $"DS-{ColumnLabel(col)}{row}";
            return true;
        }

        int cells = GridCellCount(_worldSizeS);
        double cellSize = GridCellSize(_worldSizeS);
        int colNormal = Math.Clamp((int)Math.Floor(x / cellSize), 0, cells - 1);
        int rowNormal = Math.Clamp((int)Math.Floor((_worldSizeS - y) / cellSize), 0, cells - 1);

        label = $"{ColumnLabel(colNormal)}{rowNormal}";
        return true;
    }

    private string GetGridLabel(RustPlusClientReal.ShopMarker s)
        => TryGetGridRef(s.X, s.Y, out var g) ? g : "off-grid";

    private string GetGridLabel(RustPlusClientReal.DynMarker m) => GetGridLabel(m.X, m.Y);

    private string GetGridLabel(double x, double y)
        => TryGetGridRef(x, y, out var g) ? g : "off-grid";
}


