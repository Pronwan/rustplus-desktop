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
            int dsCells = 20; // 3000m / 150m grid size

            double dsOx = _worldRectPx.X;
            double dsOy = _worldRectPx.Y;
            double dsOw = _worldRectPx.Width;
            double dsOh = _worldRectPx.Height;
            double dsStep = dsOw / dsCells;

            var dsStroke = Brushes.Black;
            double dsThin = 1.0;

            for (int i = 0; i <= dsCells; i++)
            {
                double x = dsOx + i * dsStep;
                var line = new System.Windows.Shapes.Line
                {
                    X1 = x,
                    Y1 = dsOy,
                    X2 = x,
                    Y2 = dsOy + dsOh,
                    Stroke = dsStroke,
                    StrokeThickness = dsThin
                };
                GridLayer.Children.Add(line);
            }

            for (int j = 0; j <= dsCells; j++)
            {
                double y = dsOy + j * dsStep;
                var line = new System.Windows.Shapes.Line
                {
                    X1 = dsOx,
                    Y1 = y,
                    X2 = dsOx + dsOw,
                    Y2 = y,
                    Stroke = dsStroke,
                    StrokeThickness = dsThin
                };
                GridLayer.Children.Add(line);
            }

            for (int i = 0; i < dsCells; i++)
            {
                string col = "DS-" + ColumnLabel(i);
                for (int j = 0; j < dsCells; j++)
                {
                    var tb = new TextBlock
                    {
                        Text = $"{col}{j}",
                        Foreground = Brushes.LightBlue,
                        FontSize = 9,
                        Margin = new Thickness(6, 4, 0, 0),
                        Background = Brushes.Transparent,
                        Padding = new Thickness(0)
                    };

                    double x = dsOx + i * dsStep + 1;
                    double y = dsOy + j * dsStep + 1;

                    GridLayer.Children.Add(tb);
                    Canvas.SetLeft(tb, x);
                    Canvas.SetTop(tb, y);
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

        int cells = Math.Max(1, (int)Math.Round(_worldSizeS / 150.0));
        double cell = _worldSizeS / (double)cells;

        int col = Math.Clamp((int)Math.Floor(x / cell), 0, cells - 1);
        int row = Math.Clamp((int)Math.Floor((_worldSizeS - y) / cell), 0, cells - 1);

        label = $"{ColumnLabel(col)}{row}";
        return true;
    }

    private string GetGridLabel(RustPlusClientReal.ShopMarker s)
        => TryGetGridRef(s.X, s.Y, out var g) ? g : "off-grid";

    private string GetGridLabel(RustPlusClientReal.DynMarker m) => GetGridLabel(m.X, m.Y);

    private string GetGridLabel(double x, double y)
        => TryGetGridRef(x, y, out var g) ? g : "off-grid";
}


