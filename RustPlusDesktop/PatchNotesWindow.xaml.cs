using System.Windows;

namespace RustPlusDesk.Views
{
    public partial class PatchNotesWindow : Window
    {
        public PatchNotesWindow()
        {
            InitializeComponent();
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch { /* Fehler beim Öffnen des Browsers abfangen */ }
        }
    }
}
