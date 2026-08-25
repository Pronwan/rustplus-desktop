using System;
using System.Windows;
using System.Windows.Input;

namespace RustPlusDesk.Views.Windows;

/// <summary>
/// The Console Helper detached into its own always-on-top window, so it can sit beside Rust while
/// the F1 console is open. It takes focus on purpose - the panel has buttons and text fields, so
/// the click-through style used by the crosshair overlay would make it useless here.
///
/// Rust has to be running borderless or windowed. Nothing can draw over exclusive fullscreen, and
/// that is a property of the game's display mode, not something this window can work around.
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
    /// Parks the window against the right edge of the screen Rust is most likely on, which is
    /// where it stays clear of the console's own input line at the bottom left.
    /// </summary>
    private void PlaceOnRightEdge()
    {
        try
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 12;
            Top = area.Top + 12;
            Height = Math.Min(Height, area.Height - 24);
        }
        catch
        {
            // Multi-monitor edge cases are not worth failing the window over; the default
            // position is still usable and the user can drag it.
        }
    }

    private void DragStrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }
}
