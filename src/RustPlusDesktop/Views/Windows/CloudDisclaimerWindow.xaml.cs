using System.Windows;

namespace RustPlusDesk.Views;

public partial class CloudDisclaimerWindow : Wpf.Ui.Controls.FluentWindow
{
    public bool HasMadeChoice { get; private set; } = false;
    public bool CloudSyncAccepted { get; private set; } = false;

    public CloudDisclaimerWindow()
    {
        InitializeComponent();
    }

    private void BtnAccept_Click(object sender, RoutedEventArgs e)
    {
        CloudSyncAccepted = true;
        HasMadeChoice = true;
        Close();
    }

    private void BtnDecline_Click(object sender, RoutedEventArgs e)
    {
        CloudSyncAccepted = false;
        HasMadeChoice = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!HasMadeChoice)
        {
            e.Cancel = true;
        }
        base.OnClosing(e);
    }
}
