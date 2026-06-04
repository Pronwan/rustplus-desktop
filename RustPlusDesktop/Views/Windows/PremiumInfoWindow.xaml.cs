using System.Windows;
using System.Windows.Input;
using System.Diagnostics;

namespace RustPlusDesk.Views.Windows
{
    public partial class PremiumInfoWindow : Window
    {
        private const string PatreonUrl = "https://www.patreon.com/cw/Pronwan/membership";

        public PremiumInfoResult Result { get; private set; } = PremiumInfoResult.StayFree;

        public PremiumInfoWindow(string message)
        {
            InitializeComponent();
            TxtDescription.Text = message;
            BtnSupportNow.Content = GetText("PremiumSupportNow", "Support Now");
            BtnStayFree.Content = GetText("PremiumStayFree", "Stay Free");
            BtnStopSync.Content = GetText("PremiumStopSync", "Stop Sync");
        }

        private static string GetText(string key, string fallback)
        {
            return RustPlusDesk.Properties.Resources.ResourceManager.GetString(key) ?? fallback;
        }

        private void BtnSupportNow_Click(object sender, RoutedEventArgs e)
        {
            Result = PremiumInfoResult.SupportNow;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = PatreonUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Browser launch failure should not block closing the dialog.
            }

            DialogResult = true;
            Close();
        }

        private void BtnStayFree_Click(object sender, RoutedEventArgs e)
        {
            Result = PremiumInfoResult.StayFree;
            DialogResult = true;
            Close();
        }

        private void BtnStopSync_Click(object sender, RoutedEventArgs e)
        {
            Result = PremiumInfoResult.StopSync;
            DialogResult = true;
            Close();
        }
        
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            this.DragMove();
        }
    }

    public enum PremiumInfoResult
    {
        StayFree,
        StopSync,
        SupportNow
    }
}
