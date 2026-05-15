using System.Windows;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views
{
    public partial class SettingsModal : Window
    {
        private bool _isInitialized = false;

        public SettingsModal()
        {
            InitializeComponent();
            ChineseLocalizationService.ApplyTo(this);
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

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public string RequestAction { get; private set; }

        private void BtnModifyChatAlerts_Click(object sender, RoutedEventArgs e)
        {
            RequestAction = "ModifyChatAlerts";
            Close();
        }

        private void BtnChatCommands_Click(object sender, RoutedEventArgs e)
        {
            RequestAction = "ChatCommands";
            Close();
        }

        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            DragMove();
        }
    }
}
