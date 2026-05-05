using System.Windows;
using System.Windows.Controls;
using RustPlusDesk.Localization;
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
            PopulateLanguages();
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

        private void PopulateLanguages()
        {
            CboLanguage.Items.Clear();
            var current = TrackingService.Language;
            int selectedIndex = 0;
            int i = 0;
            foreach (var lang in LocalizationManager.Instance.AvailableLanguages)
            {
                CboLanguage.Items.Add(lang);
                if (string.Equals(lang.Code, current, System.StringComparison.OrdinalIgnoreCase))
                    selectedIndex = i;
                i++;
            }
            if (CboLanguage.Items.Count > 0)
                CboLanguage.SelectedIndex = selectedIndex;
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

        private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            if (CboLanguage.SelectedItem is LanguageMetadata lang)
            {
                TrackingService.Language = lang.Code;
                LocalizationManager.Instance.SetLanguage(lang.Code);
            }
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
