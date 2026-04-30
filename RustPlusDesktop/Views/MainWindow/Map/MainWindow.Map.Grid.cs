using RustPlusDesk.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private void ChkGrid_Checked(object sender, RoutedEventArgs e) => RedrawGrid();

    private void RedrawGrid()
    {
        GridLayer.Children.Clear();
        if (ChkGrid.IsChecked != true || _worldSizeS <= 0 || _worldRectPx.Width <= 0) return;

        int cells = Math.Max(1, (int)Math.Round(_worldSizeS / 150.0));

        double ox = _worldRectPx.X, oy = _worldRectPx.Y;
        double ow = _worldRectPx.Width, oh = _worldRectPx.Height;
        double step = ow / cells;

        var stroke = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
        double thin = 1.0, thick = 2.0;

        for (int i = 0; i <= cells; i++)
        {
            double x = ox + i * step;
            var line = new System.Windows.Shapes.Line
            {
                X1 = x,
                Y1 = oy,
                X2 = x,
                Y2 = oy + oh,
                Stroke = stroke,
                StrokeThickness = (i % 5 == 0) ? thick : thin
            };
            GridLayer.Children.Add(line);
        }

        for (int j = 0; j <= cells; j++)
        {
            double y = oy + j * step;
            var line = new System.Windows.Shapes.Line
            {
                X1 = ox,
                Y1 = y,
                X2 = ox + ow,
                Y2 = y,
                Stroke = stroke,
                StrokeThickness = (j % 5 == 0) ? thick : thin
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
                    Foreground = Brushes.White,
                    FontSize = 10,
                    Margin = new Thickness(2, 2, 0, 0),
                    Background = new SolidColorBrush(Color.FromArgb(96, 0, 0, 0)),
                    Padding = new Thickness(2, 0, 2, 0)
                };

                double x = ox + i * step + 1;
                double y = oy + j * step + 1;

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
