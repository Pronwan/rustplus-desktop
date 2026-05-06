using System.Windows;
using System.Windows.Input;

namespace RustPlusDesk.Views;

/// <summary>
/// Generic shadcn-themed yes/no confirm modal. Replaces calls to
/// <c>MessageBox.Show(... OKCancel ...)</c> for destructive actions like
/// "Delete group". Returns <c>true</c> from <see cref="ShowDialog"/> when the
/// user confirms, <c>false</c> on cancel / Esc / window close.
/// </summary>
public partial class ConfirmModal : Window
{
    public ConfirmModal(string title, string message, string okLabel = "OK")
    {
        InitializeComponent();
        Title = title;
        TxtTitle.Text = title;
        TxtMessage.Text = message;
        TxtOkLabel.Text = okLabel;
    }

    /// <summary>Convenience helper. Centers on owner, shows modally, returns true on confirm.</summary>
    public static bool Show(Window? owner, string title, string message, string okLabel = "OK")
    {
        var dlg = new ConfirmModal(title, message, okLabel);
        if (owner != null) dlg.Owner = owner;
        return dlg.ShowDialog() == true;
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
