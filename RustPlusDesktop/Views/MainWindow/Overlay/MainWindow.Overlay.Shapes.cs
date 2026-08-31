using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

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
}
