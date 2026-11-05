using System.Windows;
using System.Windows.Controls;
using RustPlusDesk.Views;

namespace RustPlusDesk.Views;

public partial class PlayersTabContent : UserControl
{
    private Views.MainWindow? _mainWindow;

    public PlayersTabContent()
    {
        InitializeComponent();
    }

    public void SetMainWindow(Views.MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    private void BtnHowToTrack_Click(object sender, RoutedEventArgs e) =>
        _mainWindow?.BtnHowToTrack_Click(sender, e);

    private void BtnShowOnline_Click(object sender, RoutedEventArgs e) =>
        _mainWindow?.BtnShowOnline_Click(sender, e);

    private void BtnPopoutPlayers_Click(object sender, RoutedEventArgs e) =>
        _mainWindow?.BtnPopoutPlayers_Click(sender, e);

    private void BtnAddManual_Click(object sender, RoutedEventArgs e) =>
        _mainWindow?.BtnAddManual_Click(sender, e);

    private void TxtOnlineFilter_TextChanged(object sender, TextChangedEventArgs e) =>
        _mainWindow?.TxtOnlineFilter_TextChanged(sender, e);

    private void BtnTrackPlayer_Click(object sender, RoutedEventArgs e) =>
        _mainWindow?.BtnTrackPlayer_Click(sender, e);

    private void BtnManageGroups_Click(object sender, RoutedEventArgs e) =>
        _mainWindow?.BtnManageGroups_Click(sender, e);

    private void TxtTrackedFilter_TextChanged(object sender, TextChangedEventArgs e) =>
        _mainWindow?.TxtTrackedFilter_TextChanged(sender, e);

    private void BtnViewAllAnalysis_Click(object sender, RoutedEventArgs e) =>
        _mainWindow?.BtnViewAllAnalysis_Click(sender, e);

    private void Server_Delete_Click(object sender, RoutedEventArgs e) =>
        _mainWindow?.Server_Delete_Click(sender, e);
}
