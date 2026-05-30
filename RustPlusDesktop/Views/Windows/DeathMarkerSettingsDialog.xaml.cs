using System.Windows;

namespace RustPlusDesk.Views.Windows
{
    public partial class DeathMarkerSettingsDialog : Window
    {
        public int MaxSelf { get; private set; }
        public int MaxTeam { get; private set; }

        public DeathMarkerSettingsDialog(int currentSelf, int currentTeam)
        {
            InitializeComponent();
            
            SldSelf.Value = currentSelf;
            SldTeam.Value = currentTeam;
            
            MaxSelf = currentSelf;
            MaxTeam = currentTeam;
        }

        private void SldSelf_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtSelfValue != null) TxtSelfValue.Text = e.NewValue.ToString("0");
        }

        private void SldTeam_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtTeamValue != null) TxtTeamValue.Text = e.NewValue.ToString("0");
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            MaxSelf = (int)SldSelf.Value;
            MaxTeam = (int)SldTeam.Value;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public bool WipeAll { get; private set; }

        private void BtnWipe_Click(object sender, RoutedEventArgs e)
        {
            WipeAll = true;
            DialogResult = true;
            Close();
        }
    }
}
