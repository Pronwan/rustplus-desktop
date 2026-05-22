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
        if (ChkGrid.IsChecked != true || _worldSizeS <= 0 || _worldRectPx.Width <= 0) return;

        int cells = Math.Max(1, (int)Math.Round(_worldSizeS / 150.0));

        double ox = _worldRectPx.X, oy = _worldRectPx.Y;
        double ow = _worldRectPx.Width, oh = _worldRectPx.Height;
        double step = ow / cells;

        double shiftPxX = GridShiftX * (ow / _worldSizeS);
        double shiftPxY = GridShiftY * (oh / _worldSizeS);

        var stroke = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
        double thin = 1.0, thick = 2.0;

        int extra = 3;

        // Draw vertical lines
        for (int i = -extra; i <= cells + extra; i++)
        {
            double x = ox + i * step + shiftPxX;
            if (x < ox || x > ox + ow) continue;

            var line = new System.Windows.Shapes.Line
            {
                X1 = x,
                Y1 = oy,
                X2 = x,
                Y2 = oy + oh,
                Stroke = stroke,
                StrokeThickness = (Math.Abs(i) % 5 == 0) ? thick : thin
            };
            GridLayer.Children.Add(line);
        }

        // Draw horizontal lines
        for (int j = -extra; j <= cells + extra; j++)
        {
            double y = oy + j * step - shiftPxY;
            if (y < oy || y > oy + oh) continue;

            var line = new System.Windows.Shapes.Line
            {
                X1 = ox,
                Y1 = y,
                X2 = ox + ow,
                Y2 = y,
                Stroke = stroke,
                StrokeThickness = (Math.Abs(j) % 5 == 0) ? thick : thin
            };
            GridLayer.Children.Add(line);
        }

        // Draw labels
        for (int i = -extra; i < cells + extra; i++)
        {
            for (int j = -extra; j < cells + extra; j++)
            {
                double x = ox + i * step + 1 + shiftPxX;
                double y = oy + j * step + 1 - shiftPxY;

                if (x < ox || x >= ox + ow || y < oy || y >= oy + oh) continue;

                int colIdx = Math.Clamp(i, 0, cells - 1);
                int rowIdx = Math.Clamp(j, 0, cells - 1);
                string col = ColumnLabel(colIdx);

                var tb = new TextBlock
                {
                    Text = $"{col}{rowIdx}",
                    Foreground = Brushes.White,
                    FontSize = 10,
                    Margin = new Thickness(2, 2, 0, 0),
                    Background = new SolidColorBrush(Color.FromArgb(96, 0, 0, 0)),
                    Padding = new Thickness(2, 0, 2, 0)
                };

                GridLayer.Children.Add(tb);
                Canvas.SetLeft(tb, x);
                Canvas.SetTop(tb, y);
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

        double shiftedX = x - GridShiftX;
        double shiftedY = y - GridShiftY;

        int cells = Math.Max(1, (int)Math.Round(_worldSizeS / 150.0));
        double cell = _worldSizeS / (double)cells;

        int col = Math.Clamp((int)Math.Floor(shiftedX / cell), 0, cells - 1);
        int row = Math.Clamp((int)Math.Floor((_worldSizeS - shiftedY) / cell), 0, cells - 1);

        label = $"{ColumnLabel(col)}{row}";
        return true;
    }

    private string GetGridLabel(RustPlusClientReal.ShopMarker s)
        => TryGetGridRef(s.X, s.Y, out var g) ? g : "off-grid";

    private string GetGridLabel(RustPlusClientReal.DynMarker m) => GetGridLabel(m.X, m.Y);

    private string GetGridLabel(double x, double y)
        => TryGetGridRef(x, y, out var g) ? g : "off-grid";
}
