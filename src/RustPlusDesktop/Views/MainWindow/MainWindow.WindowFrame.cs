using System.Windows;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    // Set from the saved window state; applied once the window has been shown.
    private bool _startMaximized;

    /// <summary>
    /// Maximizes the window if it was maximized when the app last closed.
    ///
    /// This has to run after the window is shown. Setting Maximized in the constructor, before
    /// the window existed, created it already maximized while WPF still reported Normal, so the
    /// later switch to Maximized raised no StateChanged. Wpf.Ui pads a maximized window for its
    /// resize border only from StateChanged, and without that padding about 8px of the app sat
    /// off every screen edge after a restart.
    /// </summary>
    internal void ApplyStartupWindowState()
    {
        if (!_startMaximized) return;
        _startMaximized = false;
        WindowState = WindowState.Maximized;
    }
}
