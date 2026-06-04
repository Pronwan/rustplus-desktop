using System;
using System.Windows;
using System.Windows.Controls;

namespace RustPlusDesk.Views;

public partial class UploadConsentOverlay : UserControl
{
    public event EventHandler? AcceptClicked;
    public event EventHandler? DeclineClicked;

    public UploadConsentOverlay()
    {
        InitializeComponent();
    }

    private void BtnAccept_Click(object sender, RoutedEventArgs e) => AcceptClicked?.Invoke(this, EventArgs.Empty);
    private void BtnDecline_Click(object sender, RoutedEventArgs e) => DeclineClicked?.Invoke(this, EventArgs.Empty);
}
