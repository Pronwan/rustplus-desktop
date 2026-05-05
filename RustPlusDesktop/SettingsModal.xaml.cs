using System.Windows;
using System.Windows.Media;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views
{
    public partial class SettingsModal : Window
    {
        private bool _isInitialized = false;

        public SettingsModal()
        {
            InitializeComponent();
            LoadSettings();
            _isInitialized = true;
        }

        private void LoadSettings()
        {
            ChkAutoStart.IsChecked = TrackingService.AutoStartEnabled;
            ChkStartMinimized.IsChecked = TrackingService.StartMinimizedEnabled;
            ChkAutoConnect.IsChecked = TrackingService.AutoConnectEnabled;
            ChkCloseToTray.IsChecked = TrackingService.CloseToTrayEnabled;
            ChkBackgroundTracking.IsChecked = TrackingService.IsBackgroundTrackingEnabled;
            ChkAutoLoadShops.IsChecked = TrackingService.AutoLoadShops;
            ChkHideConsole.IsChecked = TrackingService.HideConsole;
            TxtDiscordUrl.Text = TrackingService.DiscordWebhookUrl;
            UpdateDiscordStatus();
        }

        private void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            TrackingService.AutoStartEnabled = ChkAutoStart.IsChecked == true;
            TrackingService.StartMinimizedEnabled = ChkStartMinimized.IsChecked == true;
            TrackingService.AutoConnectEnabled = ChkAutoConnect.IsChecked == true;
            TrackingService.CloseToTrayEnabled = ChkCloseToTray.IsChecked == true;
            TrackingService.IsBackgroundTrackingEnabled = ChkBackgroundTracking.IsChecked == true;
            TrackingService.AutoLoadShops = ChkAutoLoadShops.IsChecked == true;
            TrackingService.HideConsole = ChkHideConsole.IsChecked == true;
        }

        private void TxtDiscordUrl_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            var url = (TxtDiscordUrl.Text ?? "").Trim();
            TrackingService.DiscordWebhookUrl = url;
            UpdateDiscordStatus();
        }

        private async void BtnDiscordTest_Click(object sender, RoutedEventArgs e)
        {
            // Save whatever's in the textbox first so the test uses the latest value.
            var url = (TxtDiscordUrl.Text ?? "").Trim();
            TrackingService.DiscordWebhookUrl = url;

            if (string.IsNullOrEmpty(url))
            {
                SetStatus("Paste a webhook URL first.", isError: true);
                return;
            }

            BtnDiscordTest.IsEnabled = false;
            SetStatus("Sending test message…", isError: false);
            var ok = await DiscordWebhookService.SendTestAsync();
            BtnDiscordTest.IsEnabled = true;

            if (ok) SetStatus("Test message sent. Check your Discord channel.", isError: false);
            else SetStatus("Test failed. Check the URL and your network.", isError: true);
        }

        private void UpdateDiscordStatus()
        {
            var url = TrackingService.DiscordWebhookUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                SetStatus("Not configured.", isError: false);
                return;
            }
            // Don't echo the URL itself; show only that it's set + a length hint.
            SetStatus($"URL set ({url.Length} chars).", isError: false);
        }

        private void SetStatus(string message, bool isError)
        {
            TxtDiscordStatus.Text = message;
            TxtDiscordStatus.Foreground = isError
                ? new SolidColorBrush(Color.FromRgb(0xCE, 0x42, 0x2B))
                : (Brush)FindResource("TextSubtle");
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            DragMove();
        }
    }
}
