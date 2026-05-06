using System.Windows;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private int _panelOverlayDepth;

    /// <summary>
    /// Hide the map WebView2 while a right-column slide-in panel is open.
    /// WebView2 is HWND-based and renders on top of WPF regardless of Z-index
    /// (the well-known "airspace" issue) — its hwnd was intercepting mouse-wheel
    /// events meant for the patch-notes / report / tracker panels, leaving the
    /// inner ScrollViewers dead until something else (typically a server connect)
    /// shoved layout around enough to mask the bug.
    /// Reference-counted so simultaneous opens (rare) don't trip over each other.
    /// </summary>
    private void OnSlideInPanelOpened()
    {
        _panelOverlayDepth++;
        if (_webView != null) _webView.Visibility = Visibility.Hidden;
    }

    private void OnSlideInPanelClosed()
    {
        if (_panelOverlayDepth > 0) _panelOverlayDepth--;
        if (_panelOverlayDepth == 0 && _webView != null)
            _webView.Visibility = Visibility.Visible;
    }
}
