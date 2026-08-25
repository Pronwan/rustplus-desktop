using System;
using System.Windows;
using System.Windows.Input;

namespace RustPlusDesk.Views.Windows;

/// <summary>
/// The Console Helper in its own always-on-top window, so it can sit beside Rust while the F1
/// console is open. Frameless by preference, but without AllowsTransparency - a layered window
/// is a poor host for popups and buys nothing here, since the panel draws its own rounded shell.
/// Dragging comes from the strip over the header; resizing from the standard window border.
///
/// Rust has to be running borderless or windowed. Nothing can draw over exclusive fullscreen.
/// </summary>
public partial class ConsoleHelperPopoutWindow : Window
{
    public ConsoleHelperPopoutWindow()
    {
        InitializeComponent();

        // No second popout from inside the popout.
        Helper.SetPopoutAvailable(false);
        Helper.CloseRequested += (_, __) => Close();

        Loaded += (_, __) => PlaceOnRightEdge();
    }

    /// <summary>
    /// Parks the window against the right edge on first open, clear of the console's own input
    /// line at the bottom left. Free to move from there.
    /// </summary>
    private void PlaceOnRightEdge()
    {
        try
        {
            var area = SystemParameters.WorkArea;
            Height = Math.Min(Height, area.Height - 24);
            Left = area.Right - Width - 12;
            Top = area.Top + 12;
        }
        catch
        {
            // Multi-monitor edge cases are not worth failing the window over; the default
            // position is still usable and the user can drag it.
        }
    }

    private void DragStrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); } catch { }
    }
}
