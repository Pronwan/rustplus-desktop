using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace RustPlusDesk.Views;

public partial class TeamTabContent : UserControl
{
    public TeamTabContent()
    {
        InitializeComponent();
    }

    private void Team_Center_Click(object sender, RoutedEventArgs e) { }
    private void Team_Follow_Click(object sender, RoutedEventArgs e) { }
    private void Team_OpenProfile_Click(object sender, RoutedEventArgs e) { }
    private void Team_Promote_Click(object sender, RoutedEventArgs e) { }
    private void Team_Kick_Click(object sender, RoutedEventArgs e) { }
    private void TeamItem_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e) { }
    private void ChkProfileMarkers_Toggled(object sender, RoutedEventArgs e) { }
    private void ChkPlayerArrows_Toggled(object sender, RoutedEventArgs e) { }
    private void ChkDeathMarkers_Toggled(object sender, RoutedEventArgs e) { }
    private void BtnAbbreviateNames_Toggled(object sender, RoutedEventArgs e) { }
    private void SliderPlayerIconSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { }
    private void BtnDeathMarkerSettings_Click(object sender, RoutedEventArgs e) { }
}
