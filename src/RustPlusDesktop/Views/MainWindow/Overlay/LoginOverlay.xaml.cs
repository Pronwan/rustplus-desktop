using System;
using System.Windows;
using System.Windows.Controls;

namespace RustPlusDesk.Views;

public partial class LoginOverlay : UserControl
{
    public event EventHandler? PairClicked;
    public event EventHandler? RestoreClicked;

    public LoginOverlay()
    {
        InitializeComponent();
    }

    private void BtnPair_Click(object sender, RoutedEventArgs e) => PairClicked?.Invoke(this, EventArgs.Empty);
    private void BtnRestore_Click(object sender, RoutedEventArgs e) => RestoreClicked?.Invoke(this, EventArgs.Empty);
}
