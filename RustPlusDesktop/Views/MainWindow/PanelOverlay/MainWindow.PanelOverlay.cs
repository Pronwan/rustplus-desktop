using System.Windows;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private int _panelOverlayDepth;

    /// <summary>
    /// Disable the map WebView2's hit-testing while a right-column slide-in
    /// panel is open. WebView2 is HWND-based and renders on top of WPF
    /// regardless of Z-index — its hwnd was intercepting mouse-wheel events
    /// meant for the patch-notes / report / tracker panels.
    ///
    /// Earlier versions of this helper toggled <c>_webView.Visibility</c>,
    /// which sidestepped the airspace issue but also tore down the WebView2's
    /// event wiring on the way back: zoom and pan stopped working after the
    /// first close. Toggling <c>IsHitTestVisible</c> instead leaves the
    /// underlying HWND fully alive — only WPF hit-testing is suppressed,
    /// which is enough to let wheel events bubble to the slide-in panel's
    /// ScrollViewer.
    /// Reference-counted so simultaneous opens don't trip over each other.
    /// </summary>
    private void OnSlideInPanelOpened()
    {
        _panelOverlayDepth++;
        if (_webView != null) _webView.IsHitTestVisible = false;
    }

    private void OnSlideInPanelClosed()
    {
        if (_panelOverlayDepth > 0) _panelOverlayDepth--;
        if (_panelOverlayDepth == 0 && _webView != null)
            _webView.IsHitTestVisible = true;
    }
}
