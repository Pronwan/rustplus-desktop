using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views
{
    public partial class AppSettingsOverlay : UserControl
    {
        public MainWindow? ParentWindow { get; set; }
        private bool _isSettingsInitialized = false;

        public class LanguageOption
        {
            public string Name { get; set; } = "";
            public string Code { get; set; } = "";
        }

        public AppSettingsOverlay()
        {
            InitializeComponent();
            Loaded += AppSettingsOverlay_Loaded;
        }

        private void AppSettingsOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isSettingsInitialized) return;
            
            PopulateLanguages();
            LoadSettings();
            _isSettingsInitialized = true;
        }

        private void PopulateLanguages()
        {
            var langs = new List<LanguageOption>
            {
                new() { Name = "System Default", Code = "" },
                new() { Name = "English", Code = "en" },
                new() { Name = "Deutsch", Code = "de" },
                new() { Name = "Français", Code = "fr" },
                new() { Name = "Español", Code = "es-ES" },
                new() { Name = "Italiano", Code = "it" },
                new() { Name = "Polski", Code = "pl" },
                new() { Name = "Русский", Code = "ru" },
                new() { Name = "Türkçe", Code = "tr" },
                new() { Name = "Português (BR)", Code = "pt-BR" },
                new() { Name = "Português (PT)", Code = "pt-PT" },
                new() { Name = "Nederlands", Code = "nl" },
                new() { Name = "Dansk", Code = "da" },
                new() { Name = "Norsk", Code = "no" },
                new() { Name = "Svenska", Code = "sv-SE" },
                new() { Name = "Suomi", Code = "fi" },
                new() { Name = "Čeština", Code = "cs" },
                new() { Name = "Magyar", Code = "hu" },
                new() { Name = "Română", Code = "ro" },
                new() { Name = "Srpski", Code = "sr" },
                new() { Name = "Ελληνικά", Code = "el" },
                new() { Name = "Українська", Code = "uk" },
                new() { Name = "Tiếng Việt", Code = "vi" },
                new() { Name = "العربية", Code = "ar" },
                new() { Name = "עברית", Code = "he" },
                new() { Name = "日本語", Code = "ja" },
                new() { Name = "한국어", Code = "ko" },
                new() { Name = "简体中文", Code = "zh-CN" },
                new() { Name = "繁體中文", Code = "zh-TW" },
                new() { Name = "Català", Code = "ca" },
                new() { Name = "Afrikaans", Code = "af" },
                // Alias / generic locale folders
                new() { Name = "Español (es)", Code = "es" },
                new() { Name = "Português (pt)", Code = "pt" },
                new() { Name = "Português (BR) [pt_BR]", Code = "pt_BR" },
                new() { Name = "Svenska (sv)", Code = "sv" },
                new() { Name = "简体中文 (Hans)", Code = "zh-Hans" },
                new() { Name = "繁體中文 (Hant)", Code = "zh-Hant" }
            };

            CmbLanguage.ItemsSource = langs.OrderBy(l => l.Name).ToList();
        }

        public void LoadSettings()
        {
            CmbLanguage.SelectedValue = TrackingService.SelectedLanguage;
            
            ChkAutoStart.IsChecked = TrackingService.AutoStartEnabled;
            ChkStartMinimized.IsChecked = TrackingService.StartMinimizedEnabled;
            ChkAutoConnect.IsChecked = TrackingService.AutoConnectEnabled;
            ChkCloseToTray.IsChecked = TrackingService.CloseToTrayEnabled;
            ChkBackgroundTracking.IsChecked = TrackingService.IsBackgroundTrackingEnabled;
            ChkAutoLoadShops.IsChecked = TrackingService.AutoLoadShops;
            ChkHideConsole.IsChecked = TrackingService.HideConsole;
            ChkStreamerMode.IsChecked = TrackingService.MapAbbreviateNames;
        }

        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isSettingsInitialized) return;
            var code = CmbLanguage.SelectedValue as string;
            if (code != null)
            {
                TrackingService.SelectedLanguage = code;
                
                // Try to apply it immediately
                if (Application.Current is App app)
                {
                    app.SetLanguage();
                }
            }
        }

        private void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            if (!_isSettingsInitialized) return;

            TrackingService.AutoStartEnabled = ChkAutoStart.IsChecked == true;
            TrackingService.StartMinimizedEnabled = ChkStartMinimized.IsChecked == true;
            TrackingService.AutoConnectEnabled = ChkAutoConnect.IsChecked == true;
            TrackingService.CloseToTrayEnabled = ChkCloseToTray.IsChecked == true;
            TrackingService.IsBackgroundTrackingEnabled = ChkBackgroundTracking.IsChecked == true;
            TrackingService.AutoLoadShops = ChkAutoLoadShops.IsChecked == true;
            TrackingService.HideConsole = ChkHideConsole.IsChecked == true;
            TrackingService.MapAbbreviateNames = ChkStreamerMode.IsChecked == true;
        }

        private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
        {
            Visibility = Visibility.Collapsed;
            ParentWindow?.ApplySettings();
        }

        private void BtnModifyChatAlerts_Click(object sender, RoutedEventArgs e)
        {
            Visibility = Visibility.Collapsed;
            ParentWindow?.ApplySettings();
            ParentWindow?.OpenChatAlertsFromSettings();
        }

        private void BtnChatCommands_Click(object sender, RoutedEventArgs e)
        {
            Visibility = Visibility.Collapsed;
            ParentWindow?.ApplySettings();
            ParentWindow?.OpenChatCommandsFromSettings();
        }
    }
}
