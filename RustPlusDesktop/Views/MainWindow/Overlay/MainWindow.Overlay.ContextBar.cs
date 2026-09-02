using System.Windows;

namespace RustPlusDesk.Views;

// Phase 3 of the map-tools overhaul: a unified context bar (active-tool identity + shortcut)
// and a first-run hint that navigation stays available while a tool is armed.
public partial class MainWindow
{
    private bool _panHintShownThisSession;

    // Name + keyboard shortcut of the active tool, shown above its options.
    private void UpdateToolContextLabel()
    {
        if (ToolContextBar == null || ToolContextLabel == null) return;

        string? label = (_selectedElement != null && _currentTool == OverlayToolMode.None)
            ? "SELECTED  ·  Del removes  ·  double-click text to edit"
            : _currentTool switch
            {
                OverlayToolMode.Draw => "PEN  ·  2",
                OverlayToolMode.Line => "LINE  ·  3  ·  Shift locks angle",
                OverlayToolMode.Arrow => "ARROW  ·  4  ·  Shift locks angle",
                OverlayToolMode.Box => "RECTANGLE  ·  5  ·  Shift for square",
                OverlayToolMode.Circle => "CIRCLE  ·  6  ·  Shift for circle",
                OverlayToolMode.Route => "ROUTE  ·  7  ·  draw a path, arrows follow it",
                OverlayToolMode.Text => "TEXT  ·  8",
                OverlayToolMode.Icon => "MARKER  ·  9",
                OverlayToolMode.Erase => "ERASER  ·  0",
                _ => null
            };

        ToolContextLabel.Text = label ?? string.Empty;
        ToolContextBar.Visibility = label != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowPanHintIfFirstOpen()
    {
        if (_panHintShownThisSession || OverlayPanHint == null) return;
        _panHintShownThisSession = true;
        OverlayPanHint.Visibility = Visibility.Visible;
    }

    private void HidePanHint()
    {
        if (OverlayPanHint != null) OverlayPanHint.Visibility = Visibility.Collapsed;
    }

    private void DismissPanHint_Click(object sender, RoutedEventArgs e) => HidePanHint();
}
