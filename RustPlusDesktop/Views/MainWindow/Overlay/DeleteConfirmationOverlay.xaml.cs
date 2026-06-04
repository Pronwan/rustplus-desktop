using System;
using System.Windows;
using System.Windows.Controls;

namespace RustPlusDesk.Views;

public partial class DeleteConfirmationOverlay : UserControl
{
    public event EventHandler? CancelClicked;
    public event EventHandler? ConfirmClicked;

    public DeleteConfirmationOverlay()
    {
        InitializeComponent();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => CancelClicked?.Invoke(this, EventArgs.Empty);
    private void BtnConfirm_Click(object sender, RoutedEventArgs e) => ConfirmClicked?.Invoke(this, EventArgs.Empty);
}
