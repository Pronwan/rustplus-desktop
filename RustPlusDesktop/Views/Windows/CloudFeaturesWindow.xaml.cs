using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace RustPlusDesk.Views.Windows
{
    public partial class CloudFeaturesWindow : Window
    {
        public CloudFeaturesWindow()
        {
            InitializeComponent();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void BtnOpenPortal_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://rustplusdesktop.cloud/dashboard") { UseShellExecute = true });
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
