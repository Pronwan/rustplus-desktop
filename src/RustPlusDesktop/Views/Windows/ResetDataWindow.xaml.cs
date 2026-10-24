using System.Windows;

namespace RustPlusDesk.Views
{
    public partial class ResetDataWindow : Wpf.Ui.Controls.FluentWindow
    {
        public bool ResetConnection => ChkConnection.IsChecked == true;
        public bool ResetProfiles => ChkProfiles.IsChecked == true;
        public bool ResetSteam => ChkSteam.IsChecked == true;
        public bool ResetPairing => ChkPairing.IsChecked == true;
        public bool ResetCrosshairs => ChkCrosshairs.IsChecked == true;
        public bool ResetCache => ChkCache.IsChecked == true;

        public ResetDataWindow()
        {
            InitializeComponent();
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
