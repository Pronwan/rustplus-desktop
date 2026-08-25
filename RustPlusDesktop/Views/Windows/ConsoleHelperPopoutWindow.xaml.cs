using System;
using System.Windows;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views.Windows;

/// <summary>
/// The Console Helper in its own window, so it can sit beside Rust while the F1 console is open.
///
/// This is a normal chromed window on purpose. The first version was borderless with
/// AllowsTransparency, which made it a layered window - and WPF popups inside a layered, topmost
/// window are unreliable, which is what stopped the item suggestion list from appearing. A plain
/// window also gets dragging, resizing and snapping from Windows instead of hand-rolled code.
///
/// Rust has to be running borderless or windowed. Nothing can draw over exclusive fullscreen.
/// </summary>
public partial class ConsoleHelperPopoutWindow : WpfUi.FluentWindow
{
    public ConsoleHelperPopoutWindow()
    {
        InitializeComponent();

        // No second popout from inside the popout.
        Helper.SetPopoutAvailable(false);
        Helper.CloseRequested += (_, __) => Close();

        Loaded += (_, __) =>
        {
            PlaceOnRightEdge();
            UpdatePinVisual();
        };
    }

    /// <summary>
    /// Parks the window against the right edge on first open, clear of the console's own input
    /// line at the bottom left. It is freely movable from there.
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

    /// <summary>
    /// Lets the user drop the window out of the always-on-top band. Useful when it is in the way,
    /// and the quickest way to tell whether a rendering oddity is caused by Topmost at all.
    /// </summary>
    private void BtnPin_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        UpdatePinVisual();
    }

    private void UpdatePinVisual()
    {
        if (BtnPin == null) return;
        BtnPin.Icon = new WpfUi.SymbolIcon(Topmost ? WpfUi.SymbolRegular.Pin24 : WpfUi.SymbolRegular.PinOff24);
        BtnPin.ToolTip = Topmost
            ? RustPlusDesk.Properties.Resources.GetString("ConsoleHelperKeepOnTop")
            : RustPlusDesk.Properties.Resources.GetString("ConsoleHelperNotOnTop");
    }
}
