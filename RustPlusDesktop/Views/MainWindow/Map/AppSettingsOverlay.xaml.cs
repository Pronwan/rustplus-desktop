using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Navigation;
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
            public string? ImagePath { get; set; }
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
                new() { Name = "System Default", Code = "", ImagePath = null },
                new() { Name = "English", Code = "en", ImagePath = "pack://application:,,,/Assets/Flags/en.png" },
                new() { Name = "Deutsch", Code = "de", ImagePath = "pack://application:,,,/Assets/Flags/de.png" },
                new() { Name = "Français", Code = "fr", ImagePath = "pack://application:,,,/Assets/Flags/fr.png" },
                new() { Name = "Español", Code = "es-ES", ImagePath = "pack://application:,,,/Assets/Flags/es-ES.png" },
                new() { Name = "Italiano", Code = "it", ImagePath = "pack://application:,,,/Assets/Flags/it.png" },
                new() { Name = "Polski", Code = "pl", ImagePath = "pack://application:,,,/Assets/Flags/pl.png" },
                new() { Name = "Русский", Code = "ru", ImagePath = "pack://application:,,,/Assets/Flags/ru.png" },
                new() { Name = "Türkçe", Code = "tr", ImagePath = "pack://application:,,,/Assets/Flags/tr.png" },
                new() { Name = "Português (BR)", Code = "pt-BR", ImagePath = "pack://application:,,,/Assets/Flags/pt-BR.png" },
                new() { Name = "Português (PT)", Code = "pt-PT", ImagePath = "pack://application:,,,/Assets/Flags/pt-PT.png" },
                new() { Name = "Nederlands", Code = "nl", ImagePath = "pack://application:,,,/Assets/Flags/nl.png" },
                new() { Name = "Dansk", Code = "da", ImagePath = "pack://application:,,,/Assets/Flags/da.png" },
                new() { Name = "Norsk", Code = "no", ImagePath = "pack://application:,,,/Assets/Flags/no.png" },
                new() { Name = "Svenska", Code = "sv-SE", ImagePath = "pack://application:,,,/Assets/Flags/sv-SE.png" },
                new() { Name = "Suomi", Code = "fi", ImagePath = "pack://application:,,,/Assets/Flags/fi.png" },
                new() { Name = "Čeština", Code = "cs", ImagePath = "pack://application:,,,/Assets/Flags/cs.png" },
                new() { Name = "Magyar", Code = "hu", ImagePath = "pack://application:,,,/Assets/Flags/hu.png" },
                new() { Name = "Română", Code = "ro", ImagePath = "pack://application:,,,/Assets/Flags/ro.png" },
                new() { Name = "Srpski", Code = "sr", ImagePath = "pack://application:,,,/Assets/Flags/sr.png" },
                new() { Name = "Ελληνικά", Code = "el", ImagePath = "pack://application:,,,/Assets/Flags/el.png" },
                new() { Name = "Українська", Code = "uk", ImagePath = "pack://application:,,,/Assets/Flags/uk.png" },
                new() { Name = "Tiếng Việt", Code = "vi", ImagePath = "pack://application:,,,/Assets/Flags/vi.png" },
                new() { Name = "العربية", Code = "ar", ImagePath = "pack://application:,,,/Assets/Flags/ar.png" },
                new() { Name = "עברית", Code = "he", ImagePath = "pack://application:,,,/Assets/Flags/he.png" },
                new() { Name = "日本語", Code = "ja", ImagePath = "pack://application:,,,/Assets/Flags/ja.png" },
                new() { Name = "한국어", Code = "ko", ImagePath = "pack://application:,,,/Assets/Flags/ko.png" },
                new() { Name = "简体中文", Code = "zh-CN", ImagePath = "pack://application:,,,/Assets/Flags/zh-CN.png" },
                new() { Name = "繁體中文", Code = "zh-TW", ImagePath = "pack://application:,,,/Assets/Flags/zh-TW.png" },
                new() { Name = "Català", Code = "ca", ImagePath = "pack://application:,,,/Assets/Flags/ca.png" },
                new() { Name = "Afrikaans", Code = "af", ImagePath = "pack://application:,,,/Assets/Flags/af.png" },
                // Alias / generic locale folders
                new() { Name = "Español (es)", Code = "es", ImagePath = "pack://application:,,,/Assets/Flags/es.png" },
                new() { Name = "Português (pt)", Code = "pt", ImagePath = "pack://application:,,,/Assets/Flags/pt.png" },
                new() { Name = "Português (BR) [pt_BR]", Code = "pt_BR", ImagePath = "pack://application:,,,/Assets/Flags/pt_BR.png" },
                new() { Name = "Svenska (sv)", Code = "sv", ImagePath = "pack://application:,,,/Assets/Flags/sv.png" },
                new() { Name = "简体中文 (Hans)", Code = "zh-Hans", ImagePath = "pack://application:,,,/Assets/Flags/zh-Hans.png" },
                new() { Name = "繁體中文 (Hant)", Code = "zh-Hant", ImagePath = "pack://application:,,,/Assets/Flags/zh-Hant.png" }
            };

            CmbLanguage.ItemsSource = langs.OrderBy(l => l.Code == "" ? 1 : 0).ThenBy(l => l.Name).ToList();
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
            CmbMonumentDisplayMode.SelectedIndex = Math.Clamp(TrackingService.MapMonumentDisplayMode, 0, 1);
            ChkHideConsole.IsChecked = TrackingService.HideConsole;
            ChkStreamerMode.IsChecked = TrackingService.MapAbbreviateNames;
            SliderMonumentScale.Value = TrackingService.MapMonumentScale;
            SliderMonumentOpacity.Value = TrackingService.MapMonumentOpacity;
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
            if (CmbMonumentDisplayMode != null && CmbMonumentDisplayMode.SelectedIndex >= 0)
            {
                TrackingService.MapMonumentDisplayMode = CmbMonumentDisplayMode.SelectedIndex;
            }
            TrackingService.HideConsole = ChkHideConsole.IsChecked == true;
            TrackingService.MapAbbreviateNames = ChkStreamerMode.IsChecked == true;
            TrackingService.MapMonumentScale = SliderMonumentScale.Value;
            TrackingService.MapMonumentOpacity = SliderMonumentOpacity.Value;

            ParentWindow?.ApplySettings();
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

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch { }
            e.Handled = true;
        }
    }
}
