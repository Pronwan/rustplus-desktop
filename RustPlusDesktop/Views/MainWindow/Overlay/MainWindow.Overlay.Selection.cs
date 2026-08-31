using System;
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
    private double _selectionSizeDragStart;
    private bool _suppressSelectionSlider;

    private static readonly Color SelectionGlow = Color.FromRgb(0x60, 0xCD, 0xFF);

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
        fe.Effect = new DropShadowEffect
        {
            Color = SelectionGlow,
            BlurRadius = 16,
            ShadowDepth = 0,
            Opacity = 1.0
        };
        ConfigureSelectionPanel(fe);
    }

    private void DeselectElement()
    {
        if (_selectedElement == null) return;
        _selectedElement.Effect = null;
        _selectedElement = null;
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
        if (_selectedElement is not { } fe) return;
        if (sender is not Button { Tag: string hex }) return;
        if (GetElementColor(fe) is not { } current) return;

        var target = (Color)ColorConverter.ConvertFromString(hex);
        if (current == target) return;

        SetElementColor(fe, target);
        PushOverlayEdit(() => SetElementColor(fe, current), () => SetElementColor(fe, target));
        SaveOwnOverlayToJson();
    }

    private void SelectionSizeSlider_GotMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_selectedElement is { } fe) _selectionSizeDragStart = GetElementSize(fe);
    }

    private void SelectionSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSelectionSlider || _selectedElement is not { } fe) return;
        SetElementSize(fe, e.NewValue);
    }

    private void SelectionSizeSlider_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_selectedElement is not { } fe) return;
        double from = _selectionSizeDragStart;
        double to = GetElementSize(fe);
        if (Math.Abs(from - to) < 0.01) return;
        PushOverlayEdit(() => SetElementSize(fe, from), () => SetElementSize(fe, to));
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
        if (_selectedElement is not { } fe) return;
        DeselectElement();
        RemoveOwnElement(fe);
        PushOverlayEdit(() => ReAddOwnElement(fe), () => RemoveOwnElement(fe));
        UpdateOptionsPanelVisibility();
        SaveOwnOverlayToJson();
    }
}
