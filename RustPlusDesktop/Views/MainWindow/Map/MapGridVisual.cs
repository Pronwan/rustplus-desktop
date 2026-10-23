using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace RustPlusDesk.Views;

/// <summary>Shared, immutable map-space grid data for the map, mini-map and exports.</summary>
internal sealed class MapGridDrawing
{
    internal Rect Bounds { get; }
    internal Size MapSize { get; }
    internal int Cells { get; }
    internal bool IsDeepSea { get; }
    internal StreamGeometry Lines { get; }
    internal (Point Origin, DrawingGroup Glyph)[] Labels { get; }

    internal MapGridDrawing(Rect bounds, Size mapSize, int cells, bool deepSea)
    {
        Bounds = bounds;
        MapSize = mapSize;
        Cells = cells;
        IsDeepSea = deepSea;
        Lines = new StreamGeometry();
        double dx = bounds.Width / cells, dy = bounds.Height / cells;
        using (var context = Lines.Open())
        {
            // Continue the same spacing through the image's ocean padding.
            int firstCol = deepSea ? 0 : (int)Math.Ceiling(-bounds.Left / dx);
            int lastCol = deepSea ? cells : (int)Math.Floor((mapSize.Width - bounds.Left) / dx);
            int firstRow = deepSea ? 0 : (int)Math.Ceiling(-bounds.Top / dy);
            int lastRow = deepSea ? cells : (int)Math.Floor((mapSize.Height - bounds.Top) / dy);
            for (int col = firstCol; col <= lastCol; col++)
            {
                double x = bounds.Left + col * dx;
                context.BeginFigure(new Point(x, deepSea ? bounds.Top : 0), false, false);
                context.LineTo(new Point(x, deepSea ? bounds.Bottom : mapSize.Height), true, false);
            }
            for (int row = firstRow; row <= lastRow; row++)
            {
                double y = bounds.Top + row * dy;
                context.BeginFigure(new Point(deepSea ? bounds.Left : 0, y), false, false);
                context.LineTo(new Point(deepSea ? bounds.Right : mapSize.Width, y), true, false);
            }
        }
        Lines.Freeze();

        Labels = new (Point, DrawingGroup)[cells * cells];
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var brush = new SolidColorBrush(deepSea ? Colors.LightBlue : Color.FromArgb(210, 0, 0, 0));
        brush.Freeze();
        for (int col = 0; col < cells; col++)
        for (int row = 0; row < cells; row++)
        {
            var text = new FormattedText($"{MainWindow.ColumnLabel(col)}{row}", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 12, brush, 1);
            var glyph = new DrawingGroup();
            using (var context = glyph.Open()) context.DrawText(text, new Point());
            glyph.Freeze();
            Labels[col * cells + row] = (new Point(bounds.Left + col * dx, bounds.Top + row * dy), glyph);
        }
    }
}

/// <summary>Two retained vector visuals, with no per-cell layout controls.</summary>
public sealed class MapGridVisual : FrameworkElement
{
    private readonly DrawingVisual _lines = new();
    private readonly DrawingVisual _labels = new();
    private double _scale = double.NaN;
    private bool _aliased;
    internal MapGridDrawing? Drawing { get; private set; }

    public MapGridVisual()
    {
        IsHitTestVisible = false;
        AddVisualChild(_lines);
        AddVisualChild(_labels);
    }

    protected override int VisualChildrenCount => 2;
    protected override Visual GetVisualChild(int index) => index switch
    {
        0 => _lines,
        1 => _labels,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    internal void SetDrawing(MapGridDrawing? drawing)
    {
        if (ReferenceEquals(Drawing, drawing)) return;
        Drawing = drawing;
        double scale = double.IsFinite(_scale) ? _scale : 1;
        _scale = double.NaN;
        if (drawing != null)
        {
            Width = drawing.MapSize.Width;
            Height = drawing.MapSize.Height;
        }
        SetViewScale(scale, _aliased);
    }

    internal bool SetViewScale(double scale, bool aliased)
    {
        if (!double.IsFinite(scale) || scale <= 0) return false;
        if (_scale == scale && _aliased == aliased) return false;
        _scale = scale;
        _aliased = aliased;
        RenderOptions.SetEdgeMode(_lines, aliased ? EdgeMode.Aliased : EdgeMode.Unspecified);
        using (var context = _lines.RenderOpen())
        {
            if (Drawing != null)
            {
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), (aliased ? 1 : 0.65) / scale);
                pen.Freeze();
                context.DrawGeometry(null, pen, Drawing.Lines);
            }
        }
        using (var context = _labels.RenderOpen())
        {
            if (Drawing != null && Math.Min(Drawing.Bounds.Width, Drawing.Bounds.Height) / Drawing.Cells * scale >= 44)
            {
                // Text and its inset stay screen-sized; anchors remain at exact map coordinates.
                foreach (var (origin, glyph) in Drawing.Labels)
                {
                    context.PushTransform(new MatrixTransform(1 / scale, 0, 0, 1 / scale,
                        origin.X + 4 / scale, origin.Y + 2 / scale));
                    context.DrawDrawing(glyph);
                    context.Pop();
                }
            }
        }
        return true;
    }
}
