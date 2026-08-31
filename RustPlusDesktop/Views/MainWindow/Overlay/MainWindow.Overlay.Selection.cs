using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace RustPlusDesk.Views;

// Phase 2b of the map-tools overhaul: click a drawing in Select mode to recolour,
// resize, edit or delete it. Edits apply live and are undoable.
public partial class MainWindow
{
    private FrameworkElement? _selectedElement;
    private List<FrameworkElement>? _selectedGroup;   // set when the selection is a group (e.g. a route)
    private List<FrameworkElement>? _marquee;         // set when several elements are drag-selected
    private double _selectionSizeDragStart;
    private bool _suppressSelectionSlider;

    // Rubber-band drag-select state.
    private bool _isMarqueeSelecting;
    private Point? _marqueeAnchor;
    private Rectangle? _marqueeRect;

    private static readonly Color SelectionGlow = Color.FromRgb(0x60, 0xCD, 0xFF);

    // All of my elements sharing a group id.
    private List<FrameworkElement> GetGroupMembers(string groupId)
    {
        var list = new List<FrameworkElement>();
        foreach (UIElement child in Overlay.Children)
            if (child is FrameworkElement fe && fe.Tag is OverlayTag t
                && t.OwnerSteamId == _mySteamId && t.GroupId == groupId)
                list.Add(fe);
        return list;
    }

    // The element(s) the current selection acts on — a marquee set, a group, or a single element.
    private IReadOnlyList<FrameworkElement> SelectedTargets =>
        _marquee is { Count: > 0 } ? _marquee
        : _selectedGroup is { Count: > 0 } ? _selectedGroup
        : _selectedElement is { } fe ? new List<FrameworkElement> { fe }
        : new List<FrameworkElement>();

    // Topmost of my editable elements under the point — strokes/shapes hit-test by proximity,
    // icons/text by bounding box.
    private FrameworkElement? FindOwnElementAt(Point mapPos)
    {
        for (int i = Overlay.Children.Count - 1; i >= 0; i--)
        {
            if (Overlay.Children[i] is not FrameworkElement fe) continue;
            if (fe.Tag is not OverlayTag meta || meta.OwnerSteamId != _mySteamId || !meta.IsUserEditable) continue;

            if (fe is Polyline line)
            {
                if (DistancePointToPolyline(mapPos, line) <= Math.Max(line.StrokeThickness, 6))
                    return fe;
            }
            else
            {
                double x = Canvas.GetLeft(fe), y = Canvas.GetTop(fe);
                double w = !double.IsNaN(fe.Width) ? fe.Width : (fe.ActualWidth > 0 ? fe.ActualWidth : 32);
                double h = !double.IsNaN(fe.Height) ? fe.Height : (fe.ActualHeight > 0 ? fe.ActualHeight : 16);
                if (mapPos.X >= x && mapPos.X <= x + w && mapPos.Y >= y && mapPos.Y <= y + h)
                    return fe;
            }
        }
        return null;
    }

    private void SelectElement(FrameworkElement fe)
    {
        if (_selectedElement == fe) return;
        DeselectElement();
        _selectedElement = fe;

        string? groupId = (fe.Tag as OverlayTag)?.GroupId;
        _selectedGroup = groupId != null ? GetGroupMembers(groupId) : null;

        foreach (FrameworkElement t in SelectedTargets)
            t.Effect = new DropShadowEffect { Color = SelectionGlow, BlurRadius = 16, ShadowDepth = 0, Opacity = 1.0 };

        ConfigureSelectionPanel(fe);
    }

    // Select several elements at once (from a drag-select marquee), expanding any groups.
    private void SelectMarquee(List<FrameworkElement> elements)
    {
        DeselectElement();
        if (elements.Count == 0) return;
        _marquee = elements;
        _selectedElement = elements[0];
        foreach (FrameworkElement t in elements)
            t.Effect = new DropShadowEffect { Color = SelectionGlow, BlurRadius = 16, ShadowDepth = 0, Opacity = 1.0 };
        ConfigureSelectionPanel(elements[0]);
    }

    private void DeselectElement()
    {
        if (_selectedElement == null && _marquee == null) return;
        foreach (FrameworkElement t in SelectedTargets) t.Effect = null;
        _selectedElement = null;
        _selectedGroup = null;
        _marquee = null;
    }

    private void ConfigureSelectionPanel(FrameworkElement fe)
    {
        bool isText = fe is TextBlock;
        bool isStroke = fe is Polyline;

        SelectionColorRow.Visibility = (isText || isStroke) ? Visibility.Visible : Visibility.Collapsed;
        SelectionEditTextButton.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        SelectionSizeLabel.Text = isStroke ? "Thickness" : "Size";

        _suppressSelectionSlider = true;
        if (isStroke) { SelectionSizeSlider.Minimum = 1; SelectionSizeSlider.Maximum = 20; }
        else if (isText) { SelectionSizeSlider.Minimum = 10; SelectionSizeSlider.Maximum = 60; }
        else { SelectionSizeSlider.Minimum = 12; SelectionSizeSlider.Maximum = 64; }
        SelectionSizeSlider.Value = Math.Clamp(GetElementSize(fe), SelectionSizeSlider.Minimum, SelectionSizeSlider.Maximum);
        _suppressSelectionSlider = false;

        UpdateOptionsPanelVisibility();
    }

    // --- property get/set per element type ---

    private static double GetElementSize(FrameworkElement fe) => fe switch
    {
        Polyline pl => pl.StrokeThickness,
        TextBlock tb => tb.FontSize,
        _ => fe.Width > 0 ? fe.Width : (fe.Tag as OverlayTag)?.BaseSize ?? 24
    };

    private static void SetElementSize(FrameworkElement fe, double value)
    {
        switch (fe)
        {
            case Polyline pl:
                pl.StrokeThickness = value;
                break;
            case TextBlock tb:
                tb.FontSize = value;
                break;
            default:
                // Icon / marker: keep its centre fixed while it grows or shrinks.
                double oldW = fe.Width > 0 ? fe.Width : value;
                double oldH = fe.Height > 0 ? fe.Height : value;
                double cx = Canvas.GetLeft(fe) + (oldW / 2);
                double cy = Canvas.GetTop(fe) + (oldH / 2);
                fe.Width = value;
                fe.Height = value;
                Canvas.SetLeft(fe, cx - (value / 2));
                Canvas.SetTop(fe, cy - (value / 2));
                if (fe.Tag is OverlayTag tag && tag.BaseSize != null) tag.BaseSize = value;
                break;
        }
    }

    private static Color? GetElementColor(FrameworkElement fe) => fe switch
    {
        Polyline pl => (pl.Stroke as SolidColorBrush)?.Color,
        TextBlock tb => (tb.Foreground as SolidColorBrush)?.Color,
        _ => null
    };

    private static void SetElementColor(FrameworkElement fe, Color color)
    {
        if (fe is Polyline pl) pl.Stroke = new SolidColorBrush(color);
        else if (fe is TextBlock tb) tb.Foreground = new SolidColorBrush(color);
    }

    // --- UI handlers ---

    private void SelectionColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedElement is null) return;
        if (sender is not Button { Tag: string hex }) return;

        var target = (Color)ColorConverter.ConvertFromString(hex);
        // Recolour the whole selection (all group members).
        var changes = new List<(FrameworkElement fe, Color from)>();
        foreach (FrameworkElement fe in SelectedTargets)
            if (GetElementColor(fe) is { } current && current != target)
                changes.Add((fe, current));
        if (changes.Count == 0) return;

        foreach (var c in changes) SetElementColor(c.fe, target);
        PushOverlayEdit(
            () => { foreach (var c in changes) SetElementColor(c.fe, c.from); },
            () => { foreach (var c in changes) SetElementColor(c.fe, target); });
        SaveOwnOverlayToJson();
    }

    private void SelectionSizeSlider_GotMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_selectedElement is { } fe) _selectionSizeDragStart = GetElementSize(fe);
    }

    private void SelectionSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSelectionSlider || _selectedElement is null) return;
        foreach (FrameworkElement fe in SelectedTargets) SetElementSize(fe, e.NewValue);
    }

    private void SelectionSizeSlider_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_selectedElement is null) return;
        double from = _selectionSizeDragStart;
        double to = SelectionSizeSlider.Value;
        if (Math.Abs(from - to) < 0.01) return;
        var targets = SelectedTargets.ToList();
        PushOverlayEdit(
            () => { foreach (FrameworkElement fe in targets) SetElementSize(fe, from); },
            () => { foreach (FrameworkElement fe in targets) SetElementSize(fe, to); });
        SaveOwnOverlayToJson();
    }

    private void SelectionEditText_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedElement is TextBlock tb) BeginEditText(tb);
    }

    // Reopen the inline editor on an existing label (double-click or the Edit button).
    private void BeginEditText(TextBlock tb)
    {
        DeselectElement();
        OpenTextEditor(new Point(Canvas.GetLeft(tb), Canvas.GetTop(tb)), tb.Text, tb);
        UpdateOptionsPanelVisibility();
    }

    private void SelectionDelete_Click(object sender, RoutedEventArgs e) => DeleteSelectedElement();

    private void DeleteSelectedElement()
    {
        var targets = SelectedTargets.ToList();
        if (targets.Count == 0) return;
        DeselectElement();
        foreach (FrameworkElement t in targets) RemoveOwnElement(t);
        PushOverlayEdit(
            () => { foreach (FrameworkElement t in targets) ReAddOwnElement(t); },
            () => { foreach (FrameworkElement t in targets) RemoveOwnElement(t); });
        UpdateOptionsPanelVisibility();
        SaveOwnOverlayToJson();
    }

    // Combine the current map selection into one group (Ctrl+G).
    private void GroupMapSelection()
    {
        var targets = SelectedTargets.Distinct().ToList();
        if (targets.Count < 2) return;
        string groupId = "grp-" + Guid.NewGuid().ToString("N");
        foreach (FrameworkElement t in targets)
            if (t.Tag is OverlayTag tag) tag.GroupId = groupId;
        FrameworkElement anchor = targets[0];
        DeselectElement();
        SaveOwnOverlayToJson();
        SelectElement(anchor);          // reselect as the new group
        UpdateOptionsPanelVisibility();
    }

    // --- rubber-band marquee ---

    private void BeginMarquee(Point mapPos)
    {
        _isMarqueeSelecting = true;
        _marqueeAnchor = mapPos;
        _marqueeRect = new Rectangle
        {
            Stroke = new SolidColorBrush(SelectionGlow),
            StrokeThickness = 1.2,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(0x22, 0x60, 0xCD, 0xFF)),
            IsHitTestVisible = false,
            Width = 0,
            Height = 0
        };
        Canvas.SetLeft(_marqueeRect, mapPos.X);
        Canvas.SetTop(_marqueeRect, mapPos.Y);
        Overlay.Children.Add(_marqueeRect);
        WebViewHost.CaptureMouse();
    }

    private void UpdateMarquee(Point mapPos)
    {
        if (!_isMarqueeSelecting || _marqueeRect is null || _marqueeAnchor is not Point a) return;
        Canvas.SetLeft(_marqueeRect, Math.Min(a.X, mapPos.X));
        Canvas.SetTop(_marqueeRect, Math.Min(a.Y, mapPos.Y));
        _marqueeRect.Width = Math.Abs(mapPos.X - a.X);
        _marqueeRect.Height = Math.Abs(mapPos.Y - a.Y);
    }

    private void EndMarquee(Point mapPos)
    {
        if (!_isMarqueeSelecting) return;
        _isMarqueeSelecting = false;
        Point a = _marqueeAnchor ?? mapPos;
        if (_marqueeRect is not null) Overlay.Children.Remove(_marqueeRect);
        _marqueeRect = null;
        _marqueeAnchor = null;
        WebViewHost.ReleaseMouseCapture();

        var area = new Rect(Math.Min(a.X, mapPos.X), Math.Min(a.Y, mapPos.Y), Math.Abs(mapPos.X - a.X), Math.Abs(mapPos.Y - a.Y));
        if (area.Width < 2 && area.Height < 2) { UpdateOptionsPanelVisibility(); return; }

        List<FrameworkElement> hits = FindOwnElementsInRect(area);
        if (hits.Count > 0) SelectMarquee(hits);
        UpdateOptionsPanelVisibility();
    }

    // My visible, editable elements whose bounds intersect the marquee (groups pulled in whole).
    private List<FrameworkElement> FindOwnElementsInRect(Rect area)
    {
        var result = new List<FrameworkElement>();
        var seen = new HashSet<FrameworkElement>();
        foreach (UIElement child in Overlay.Children)
        {
            if (child is not FrameworkElement fe || fe.Tag is not OverlayTag meta) continue;
            if (meta.OwnerSteamId != _mySteamId || !meta.IsUserEditable) continue;
            if (fe.Visibility != Visibility.Visible) continue;
            if (!GetElementSceneBounds(fe).IntersectsWith(area)) continue;

            if (meta.GroupId is { } gid)
            {
                foreach (FrameworkElement m in GetGroupMembers(gid))
                    if (seen.Add(m)) result.Add(m);
            }
            else if (seen.Add(fe))
            {
                result.Add(fe);
            }
        }
        return result;
    }

    private static Rect GetElementSceneBounds(FrameworkElement fe)
    {
        if (fe is Polyline pl && pl.Points.Count > 0)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (Point p in pl.Points)
            {
                minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
            }
            return new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
        }

        double x = Canvas.GetLeft(fe); if (double.IsNaN(x)) x = 0;
        double y = Canvas.GetTop(fe); if (double.IsNaN(y)) y = 0;
        double w = !double.IsNaN(fe.Width) && fe.Width > 0 ? fe.Width : (fe.ActualWidth > 0 ? fe.ActualWidth : 24);
        double h = !double.IsNaN(fe.Height) && fe.Height > 0 ? fe.Height : (fe.ActualHeight > 0 ? fe.ActualHeight : 16);
        return new Rect(x, y, w, h);
    }

    // Remove an element (or its whole group), undoable. Used by the selection panel, Del key and layers list.
    private void DeleteElement(FrameworkElement fe)
    {
        string? groupId = (fe.Tag as OverlayTag)?.GroupId;
        List<FrameworkElement> targets = groupId != null ? GetGroupMembers(groupId) : new List<FrameworkElement> { fe };

        DeselectElement();
        foreach (FrameworkElement t in targets) RemoveOwnElement(t);
        PushOverlayEdit(
            () => { foreach (FrameworkElement t in targets) ReAddOwnElement(t); },
            () => { foreach (FrameworkElement t in targets) RemoveOwnElement(t); });
        UpdateOptionsPanelVisibility();
        SaveOwnOverlayToJson();
    }
}
