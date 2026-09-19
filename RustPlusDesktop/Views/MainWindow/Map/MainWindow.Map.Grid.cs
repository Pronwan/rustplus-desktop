using RustPlusDesk.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private void ChkGrid_Checked(object sender, RoutedEventArgs e)
    {
        RedrawGrid();
        UpdateSelectAllState();
    }

    private void RedrawGrid()
    {
        GridLayer.Children.Clear();
        RedrawBuildingBlockedZones();
        RedrawCargoPath();
        RedrawKeycards();

        // Drawn whenever either map wants it. The main map hides its copy through the wrapper's
        // opacity instead of leaving the layer empty, because the mini-map mirrors this very
        // canvas and an empty one is all it could ever show.
        ApplyIndependentLayerVisibility();

        bool wanted = ChkGrid.IsChecked == true || MiniMapWantsGrid;
        if (!wanted || _worldSizeS <= 0 || _worldRectPx.Width <= 0) return;

        if (_isShowingDeepSeaMap)
        {
            var dsStroke = Brushes.Black;
            double dsThin = 1.0;

            int dsCells = DeepSeaCells;
            double cellSize = DeepSeaCellSize;
            var (minX, maxX, minY, maxY) = GetDeepSeaWorldBox();

            // Draw vertical grid lines (columns A to AA, i.e., 27 cells)
            for (int col = 0; col <= dsCells; col++)
            {
                double worldX = minX + col * cellSize;
                var pTop = WorldToImagePx(worldX, maxY);
                var pBottom = WorldToImagePx(worldX, minY);

                var line = new System.Windows.Shapes.Line
                {
                    X1 = pTop.X,
                    Y1 = pTop.Y,
                    X2 = pBottom.X,
                    Y2 = pBottom.Y,
                    Stroke = dsStroke,
                    StrokeThickness = dsThin
                };
                GridLayer.Children.Add(line);
            }

            // Draw horizontal grid lines (rows 0 to 26)
            for (int row = 0; row <= dsCells; row++)
            {
                double worldY = maxY - row * cellSize;
                var pLeft = WorldToImagePx(minX, worldY);
                var pRight = WorldToImagePx(maxX, worldY);

                var line = new System.Windows.Shapes.Line
                {
                    X1 = pLeft.X,
                    Y1 = pLeft.Y,
                    X2 = pRight.X,
                    Y2 = pRight.Y,
                    Stroke = dsStroke,
                    StrokeThickness = dsThin
                };
                GridLayer.Children.Add(line);
            }

            // Draw cell labels (A0 to AA26)
            for (int col = 0; col < dsCells; col++)
            {
                string colStr = ColumnLabel(col);
                double cellX = minX + col * cellSize;

                for (int row = 0; row < dsCells; row++)
                {
                    double cellY = maxY - row * cellSize;

                    var tb = new TextBlock
                    {
                        Text = $"{colStr}{row}",
                        Foreground = Brushes.LightBlue,
                        FontSize = 9,
                        Margin = new Thickness(6, 4, 0, 0),
                        Background = Brushes.Transparent,
                        Padding = new Thickness(0)
                    };

                    var p = WorldToImagePx(cellX, cellY);

                    GridLayer.Children.Add(tb);
                    Canvas.SetLeft(tb, p.X);
                    Canvas.SetTop(tb, p.Y);
                }
            }
            RefreshGridLineThickness();
            return;
        }

        int cells = GridCellCount(_worldSizeS);
        double cell = GridCellSize(_worldSizeS);

        var stroke = Brushes.Black;
        double thin = 1.0;

        for (int i = 0; i <= cells; i++)
        {
            var pTop = WorldToImagePx(i * cell, _worldSizeS);
            var pBottom = WorldToImagePx(i * cell, 0);

            var line = new System.Windows.Shapes.Line
            {
                X1 = pTop.X,
                Y1 = pTop.Y,
                X2 = pBottom.X,
                Y2 = pBottom.Y,
                Stroke = stroke,
                StrokeThickness = thin
            };
            GridLayer.Children.Add(line);
        }

        for (int j = 0; j <= cells; j++)
        {
            var pLeft = WorldToImagePx(0, _worldSizeS - j * cell);
            var pRight = WorldToImagePx(_worldSizeS, _worldSizeS - j * cell);

            var line = new System.Windows.Shapes.Line
            {
                X1 = pLeft.X,
                Y1 = pLeft.Y,
                X2 = pRight.X,
                Y2 = pRight.Y,
                Stroke = stroke,
                StrokeThickness = thin
            };
            GridLayer.Children.Add(line);
        }

        for (int i = 0; i < cells; i++)
        {
            string col = ColumnLabel(i);
            for (int j = 0; j < cells; j++)
            {
                var tb = new TextBlock
                {
                    Text = $"{col}{j}",
                    Foreground = Brushes.Black,
                    FontSize = 10,
                    Margin = new Thickness(6, 4, 0, 0),
                    Background = Brushes.Transparent,
                    Padding = new Thickness(0)
                };

                var p = WorldToImagePx(i * cell, _worldSizeS - j * cell);

                GridLayer.Children.Add(tb);
                Canvas.SetLeft(tb, p.X);
                Canvas.SetTop(tb, p.Y);
            }
        }
        RefreshGridLineThickness();
    }

    public void RefreshGridLineThickness()
    {
        if (GridLayer == null || MapTransform == null) return;
        double physicalThickness = TrackingService.MapUseAliasedEdgeMode ? 1.0 : 0.65;
        double strokeThickness = physicalThickness / GetEffectiveZoom();

        foreach (var child in GridLayer.Children)
        {
            if (child is System.Windows.Shapes.Line line)
            {
                line.StrokeThickness = strokeThickness;
            }
        }
    }

    private static string ColumnLabel(int index)
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


