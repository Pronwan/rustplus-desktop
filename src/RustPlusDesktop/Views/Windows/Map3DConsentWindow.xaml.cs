using System.Windows;

namespace RustPlusDesk.Views.Windows;

public partial class Map3DConsentWindow : Wpf.Ui.Controls.FluentWindow
{
    public bool Accepted { get; private set; }
    public bool Remember { get; private set; }

    public Map3DConsentWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
    }

    private void BtnAccept_Click(object sender, RoutedEventArgs e)
    {
        Accepted = true;
        Remember = false;
        DialogResult = true;
        Close();
    }

    private void BtnAcceptRemember_Click(object sender, RoutedEventArgs e)
    {
        Accepted = true;
        Remember = true;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Accepted = false;
        Remember = false;
        DialogResult = false;
        Close();
    }
}
