using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
            // Labels stay screen-sized: rasterize their text once rather than on every zoom frame.
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen()) context.DrawText(text, new Point());
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(text.WidthIncludingTrailingWhitespace * 3),
                (int)Math.Ceiling(text.Height * 3), 288, 288, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            var glyph = new DrawingGroup();
            using (var context = glyph.Open()) context.DrawImage(bitmap, new Rect(0, 0, bitmap.Width, bitmap.Height));
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
    private Rect? _viewport;
    private (int Left, int Top, int Right, int Bottom) _visibleCells;
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
        _visibleCells = VisibleCells();
        SetViewScale(scale, _aliased);
    }

    internal void SetViewport(Rect viewport)
    {
        _viewport = viewport;
        var cells = VisibleCells();
        if (cells == _visibleCells) return;
        _visibleCells = cells;
        DrawLabels();
    }

    private (int Left, int Top, int Right, int Bottom) VisibleCells()
    {
        if (Drawing == null) return default;
        if (_viewport is not Rect viewport) return (0, 0, Drawing.Cells, Drawing.Cells);
        var bounds = Drawing.Bounds;
        double dx = bounds.Width / Drawing.Cells, dy = bounds.Height / Drawing.Cells;
        return (Math.Clamp((int)Math.Floor((viewport.Left - bounds.Left) / dx) - 1, 0, Drawing.Cells),
            Math.Clamp((int)Math.Floor((viewport.Top - bounds.Top) / dy) - 1, 0, Drawing.Cells),
            Math.Clamp((int)Math.Ceiling((viewport.Right - bounds.Left) / dx) + 1, 0, Drawing.Cells),
            Math.Clamp((int)Math.Ceiling((viewport.Bottom - bounds.Top) / dy) + 1, 0, Drawing.Cells));
    }

    internal static DrawingBrush CreateOceanBrush(MapGridDrawing drawing, double scale, bool aliased, double opacity, Color background)
    {
        var tile = new Rect(0, 0, drawing.Bounds.Width / drawing.Cells, drawing.Bounds.Height / drawing.Cells);
        var content = new DrawingGroup();
        using (var context = content.Open())
        {
            var stroke = new SolidColorBrush(Color.FromArgb((byte)Math.Round(150 * Math.Clamp(opacity, 0, 1)), 0, 0, 0));
            context.DrawRectangle(new SolidColorBrush(background), new Pen(stroke, (aliased ? 1 : 0.65) / scale), tile);
        }
        content.Freeze();
        return new DrawingBrush(content)
        {
            TileMode = TileMode.Tile,
            ViewboxUnits = BrushMappingMode.Absolute, Viewbox = tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(drawing.Bounds.TopLeft, tile.Size),
            Transform = new MatrixTransform()
        };
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
        DrawLabels();
        return true;
    }

    private void DrawLabels()
    {
        double scale = _scale;
        using (var context = _labels.RenderOpen())
        {
            if (Drawing != null && Math.Min(Drawing.Bounds.Width, Drawing.Bounds.Height) / Drawing.Cells * scale >= 44)
            {
                // Text and its inset stay screen-sized; anchors remain at exact map coordinates.
                for (int col = _visibleCells.Left; col < _visibleCells.Right; col++)
                for (int row = _visibleCells.Top; row < _visibleCells.Bottom; row++)
                {
                    var (origin, glyph) = Drawing.Labels[col * Drawing.Cells + row];
                    context.PushTransform(new MatrixTransform(1 / scale, 0, 0, 1 / scale,
                        origin.X + 4 / scale, origin.Y + 2 / scale));
                    context.DrawDrawing(glyph);
                    context.Pop();
                }
            }
        }
    }
}
