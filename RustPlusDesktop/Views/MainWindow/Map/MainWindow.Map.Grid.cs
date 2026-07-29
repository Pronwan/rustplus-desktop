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
        if (ChkGrid.IsChecked != true || _worldSizeS <= 0 || _worldRectPx.Width <= 0) return;

        if (_isShowingDeepSeaMap)
        {
            var dsStroke = Brushes.Black;
            double dsThin = 1.0;

            double minX = -5975;
            int dsCells = 27;
            double maxX = minX + dsCells * 150.0;
            double maxY = _worldSizeS + 75;
            double minY = maxY - dsCells * 150.0;

            // Draw vertical grid lines (columns A to AA, i.e., 27 cells)
            for (int col = 0; col <= dsCells; col++)
            {
                double worldX = minX + col * 150.0;
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
                double worldY = maxY - row * 150.0;
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
                double cellX = minX + col * 150.0;

                for (int row = 0; row < dsCells; row++)
                {
                    double cellY = maxY - row * 150.0;

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

        int cells = Math.Max(1, (int)Math.Ceiling(_worldSizeS / 150.0));

        var stroke = Brushes.Black;
        double thin = 1.0;

        for (int i = 0; i <= cells; i++)
        {
            var pTop = WorldToImagePx(i * 150, cells * 150);
            var pBottom = WorldToImagePx(i * 150, 0);

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
            var pLeft = WorldToImagePx(0, (cells - j) * 150);
            var pRight = WorldToImagePx(cells * 150, (cells - j) * 150);

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

                var p = WorldToImagePx(i * 150, (cells - j) * 150);

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

    private bool TryGetGridRef(double x, double y, out string label)
    {
        label = "";
        if (_worldSizeS <= 0) return false;

        if (x < -1000)
        {
            double dsMaxY = _worldSizeS + 75;
            int col = (int)Math.Floor((x - (-5975)) / 150.0);
            int row = (int)Math.Floor((dsMaxY - y) / 150.0);
            label = $"DS-{ColumnLabel(col)}{row}";
            return true;
        }

        int cells = Math.Max(1, (int)Math.Ceiling(_worldSizeS / 150.0));
        int colNormal = Math.Clamp((int)Math.Floor(x / 150.0), 0, cells - 1);
        int rowNormal = Math.Clamp((int)Math.Floor((_worldSizeS - y) / 150.0), 0, cells - 1);

        label = $"{ColumnLabel(colNormal)}{rowNormal}";
        return true;
    }

    private string GetGridLabel(RustPlusClientReal.ShopMarker s)
        => TryGetGridRef(s.X, s.Y, out var g) ? g : "off-grid";

    private string GetGridLabel(RustPlusClientReal.DynMarker m) => GetGridLabel(m.X, m.Y);

    private string GetGridLabel(double x, double y)
        => TryGetGridRef(x, y, out var g) ? g : "off-grid";
}


