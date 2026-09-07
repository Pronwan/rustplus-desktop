using System.Windows;

namespace RustPlusDesk.Views;

public partial class MigrationNoticeWindow : Wpf.Ui.Controls.FluentWindow
{
    public bool HasMadeChoice { get; private set; } = false;
    public bool CloudSyncAccepted { get; private set; } = false;

    public MigrationNoticeWindow()
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

    private void BtnCompare_Click(object sender, RoutedEventArgs e)
    {
        var cloudWindow = new RustPlusDesk.Views.Windows.CloudFeaturesWindow();
        cloudWindow.Owner = this;
        cloudWindow.ShowDialog();
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
