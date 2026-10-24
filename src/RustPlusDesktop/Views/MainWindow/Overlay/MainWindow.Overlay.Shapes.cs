using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RustPlusDesk.Views;

// Phase 2 of the map-tools overhaul: line / arrow / box / circle tools.
// Each shape is emitted as a Polyline, so it flows through the existing
// stroke save/load/erase/undo/cloud pipeline unchanged.
public partial class MainWindow
{
    private static bool IsShapeTool(OverlayToolMode mode) =>
        mode is OverlayToolMode.Line or OverlayToolMode.Arrow
             or OverlayToolMode.Box or OverlayToolMode.Circle;

    private static bool IsShiftDown() => (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

    private static double PointDistance(Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    // Snap b so the a->b vector lands on the nearest 45° — used with Shift.
    private static Point SnapToAngle(Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len = Math.Sqrt((dx * dx) + (dy * dy));
        if (len < 1e-6) return b;
        double step = Math.PI / 4;
        double snapped = Math.Round(Math.Atan2(dy, dx) / step) * step;
        return new Point(a.X + (len * Math.Cos(snapped)), a.Y + (len * Math.Sin(snapped)));
    }

    /// <summary>
    /// Point list for a shape dragged from <paramref name="a"/> to <paramref name="b"/>.
    /// Line/arrow/box run corner-to-corner; the circle is centred on the anchor with radius = |a→b|.
    /// </summary>
    private List<Point> BuildShapePoints(OverlayToolMode tool, Point a, Point b, bool shift)
    {
        var pts = new List<Point>();
        switch (tool)
        {
            case OverlayToolMode.Line:
                if (shift) b = SnapToAngle(a, b);
                pts.Add(a);
                pts.Add(b);
                break;

            case OverlayToolMode.Arrow:
            {
                if (shift) b = SnapToAngle(a, b);
                double angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
                double shaft = PointDistance(a, b);
                double head = Math.Clamp(shaft * 0.28, _drawThickness * 2.5, _drawThickness * 9.0);
                const double spread = Math.PI / 7.0;
                var left = new Point(b.X - (head * Math.Cos(angle - spread)), b.Y - (head * Math.Sin(angle - spread)));
                var right = new Point(b.X - (head * Math.Cos(angle + spread)), b.Y - (head * Math.Sin(angle + spread)));
                // shaft, then one barb, back to the tip, then the other barb — a single continuous stroke.
                pts.Add(a);
                pts.Add(b);
                pts.Add(left);
                pts.Add(b);
                pts.Add(right);
                break;
            }

            case OverlayToolMode.Box:
            {
                if (shift)
                {
                    double side = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
                    b = new Point(a.X + (Math.Sign(b.X - a.X) * side), a.Y + (Math.Sign(b.Y - a.Y) * side));
                }
                pts.Add(new Point(a.X, a.Y));
                pts.Add(new Point(b.X, a.Y));
                pts.Add(new Point(b.X, b.Y));
                pts.Add(new Point(a.X, b.Y));
                pts.Add(new Point(a.X, a.Y));
                break;
            }

            case OverlayToolMode.Circle:
            {
                // Anchor is a corner of the bounding box; the ellipse grows toward the cursor
                // (same feel as the box). Shift constrains it to a perfect circle.
                if (shift)
                {
                    double side = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
                    b = new Point(a.X + (Math.Sign(b.X - a.X) * side), a.Y + (Math.Sign(b.Y - a.Y) * side));
                }
                double cx = (a.X + b.X) / 2.0, cy = (a.Y + b.Y) / 2.0;
                double rx = Math.Abs(b.X - a.X) / 2.0, ry = Math.Abs(b.Y - a.Y) / 2.0;
                const int segments = 48;
                for (int i = 0; i <= segments; i++)
                {
                    double t = (2 * Math.PI * i) / segments;
                    pts.Add(new Point(cx + (rx * Math.Cos(t)), cy + (ry * Math.Sin(t))));
                }
                break;
            }
        }
        return pts;
    }

    // Directly arm a tool (used by keyboard shortcuts). Unlike SetCurrentTool this never
    // toggles off, so pressing a shortcut always selects that tool.
    private void SelectOverlayTool(OverlayToolMode mode)
    {
        _currentTool = mode;
        if (mode != OverlayToolMode.None)
        {
            _draggingElement = null;
            DeselectElement();
        }
        UpdateToolButtonHighlights();
        UpdateOptionsPanelVisibility();
        ApplyToolCursor();
    }

    // Abandon an in-progress freehand stroke or shape preview (Esc). It was never committed,
    // so it just gets removed — no undo entry to unwind.
    private void CancelActiveOverlayDraw()
    {
        if (_currentStroke != null)
        {
            RemoveOwnElement(_currentStroke);
            _currentStroke = null;
        }
        _isDrawingStroke = false;
        _shapeAnchor = null;
    }

    private void ToolLineButton_Click(object sender, RoutedEventArgs e) => SetCurrentTool(OverlayToolMode.Line);
    private void ToolArrowButton_Click(object sender, RoutedEventArgs e) => SetCurrentTool(OverlayToolMode.Arrow);
    private void ToolBoxButton_Click(object sender, RoutedEventArgs e) => SetCurrentTool(OverlayToolMode.Box);
    private void ToolCircleButton_Click(object sender, RoutedEventArgs e) => SetCurrentTool(OverlayToolMode.Circle);
    private void ToolRouteButton_Click(object sender, RoutedEventArgs e) => SetCurrentTool(OverlayToolMode.Route);

    // Route = the freehand path replaced by evenly-spaced, separate direction arrows,
    // all tied together with one GroupId so they behave as a single layer.
    private void BuildArrowRouteElements(Polyline freehand)
    {
        List<Point> raw = freehand.Points.ToList();
        RemoveOwnElement(freehand);
        Overlay.Children.Remove(freehand);

        List<Point> wp = SimplifyPoints(raw);
        if (wp.Count < 2) return;

        double total = 0;
        for (int i = 0; i < wp.Count - 1; i++) total += PointDistance(wp[i], wp[i + 1]);
        if (total < 1e-4) return;

        // Fixed gap between arrows so the count grows with the route length (not capped at a handful).
        double spacing = Math.Max(_drawThickness * 14.0, 1e-3);
        if (total / spacing > 250) spacing = total / 250.0;   // safety cap for enormous routes
        double arrowLen = spacing * 0.6;                       // leaves a clear gap between arrows
        string groupId = "route-" + Guid.NewGuid().ToString("N");

        var created = new List<FrameworkElement>();
        for (double at = spacing * 0.5; at <= total; at += spacing)
        {
            (Point pos, double dx, double dy) = SampleAlongPath(wp, at);
            var a = new Point(pos.X - (dx * arrowLen / 2), pos.Y - (dy * arrowLen / 2));
            var b = new Point(pos.X + (dx * arrowLen / 2), pos.Y + (dy * arrowLen / 2));
            Polyline arrow = CreateRouteArrow(a, b, groupId);
            Overlay.Children.Add(arrow);
            RegisterElementForOwner(_mySteamId, arrow);
            created.Add(arrow);
        }
        if (created.Count == 0) return;

        PushOverlayEdit(
            undo: () => { foreach (FrameworkElement e in created) RemoveOwnElement(e); },
            redo: () => { foreach (FrameworkElement e in created) ReAddOwnElement(e); });
    }

    private Polyline CreateRouteArrow(Point a, Point b, string groupId)
    {
        double segLen = PointDistance(a, b);
        double dx = (b.X - a.X) / segLen, dy = (b.Y - a.Y) / segLen;
        double ang = Math.Atan2(dy, dx);
        double barb = Math.Clamp(segLen * 0.5, _drawThickness * 2.0, _drawThickness * 9.0);
        const double spread = Math.PI / 6.0;
        var left = new Point(b.X - (barb * Math.Cos(ang - spread)), b.Y - (barb * Math.Sin(ang - spread)));
        var right = new Point(b.X - (barb * Math.Cos(ang + spread)), b.Y - (barb * Math.Sin(ang + spread)));

        var pl = new Polyline
        {
            Stroke = new SolidColorBrush(_drawColor),
            StrokeThickness = _drawThickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeStartLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
            Tag = new OverlayTag { OwnerSteamId = _mySteamId, IsUserEditable = true, GroupId = groupId }
        };
        foreach (Point p in new[] { a, b, left, b, right }) pl.Points.Add(p);
        return pl;
    }

    // Point + unit direction at a given distance along the waypoint path.
    private static (Point pos, double dx, double dy) SampleAlongPath(List<Point> wp, double dist)
    {
        double cum = 0;
        for (int i = 0; i < wp.Count - 1; i++)
        {
            Point a = wp[i], b = wp[i + 1];
            double segLen = PointDistance(a, b);
            if (segLen < 1e-9) continue;
            if (cum + segLen >= dist)
            {
                double t = (dist - cum) / segLen;
                double dx = (b.X - a.X) / segLen, dy = (b.Y - a.Y) / segLen;
                return (new Point(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t)), dx, dy);
            }
            cum += segLen;
        }
        Point p1 = wp[^2], p2 = wp[^1];
        double len = Math.Max(PointDistance(p1, p2), 1e-9);
        return (p2, (p2.X - p1.X) / len, (p2.Y - p1.Y) / len);
    }

    // Douglas-Peucker thinning of a freehand point list into clean waypoints.
    private static List<Point> SimplifyPoints(List<Point> pts)
    {
        if (pts.Count < 3) return pts;
        double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
        double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        double diag = Math.Sqrt(((maxX - minX) * (maxX - minX)) + ((maxY - minY) * (maxY - minY)));
        double tolerance = Math.Max(diag * 0.02, 1e-4);

        var keep = new bool[pts.Count];
        keep[0] = true;
        keep[^1] = true;
        DouglasPeucker(pts, 0, pts.Count - 1, tolerance, keep);

        var outPts = new List<Point>();
        for (int i = 0; i < pts.Count; i++)
            if (keep[i]) outPts.Add(pts[i]);
        return outPts;
    }

    private static void DouglasPeucker(IReadOnlyList<Point> pts, int first, int last, double tol, bool[] keep)
    {
        if (last <= first + 1) return;
        double maxDist = 0;
        int index = -1;
        for (int i = first + 1; i < last; i++)
        {
            double d = PerpendicularDistance(pts[i], pts[first], pts[last]);
            if (d > maxDist) { maxDist = d; index = i; }
        }

        if (index >= 0 && maxDist > tol)
        {
            keep[index] = true;
            DouglasPeucker(pts, first, index, tol, keep);
            DouglasPeucker(pts, index, last, tol, keep);
        }
    }

    private static double PerpendicularDistance(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = (dx * dx) + (dy * dy);
        if (len2 < 1e-12) return PointDistance(p, a);
        double t = (((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / len2;
        t = Math.Clamp(t, 0, 1);
        var proj = new Point(a.X + (t * dx), a.Y + (t * dy));
        return PointDistance(p, proj);
    }
}
