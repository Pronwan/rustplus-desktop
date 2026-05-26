using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Navigation;
using RustPlusDesk.Services;
using RustPlusDesk.Models;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views
{
    public partial class AppSettingsOverlay : UserControl
    {
        public MainWindow? ParentWindow { get; set; }
        private bool _isSettingsInitialized = false;
        private bool _isUpdatingProfiles = false;

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
                // Region-specific codes matching %locale% Crowdin-generated folders
                new() { Name = "English",           Code = "en-US",  ImagePath = "pack://application:,,,/Assets/Flags/en.png" },
                new() { Name = "Deutsch",            Code = "de-DE",  ImagePath = "pack://application:,,,/Assets/Flags/de.png" },
                new() { Name = "Français",           Code = "fr-FR",  ImagePath = "pack://application:,,,/Assets/Flags/fr.png" },
                new() { Name = "Español",            Code = "es-ES",  ImagePath = "pack://application:,,,/Assets/Flags/es-ES.png" },
                new() { Name = "Italiano",           Code = "it-IT",  ImagePath = "pack://application:,,,/Assets/Flags/it.png" },
                new() { Name = "Polski",             Code = "pl-PL",  ImagePath = "pack://application:,,,/Assets/Flags/pl.png" },
                new() { Name = "Русский",            Code = "ru-RU",  ImagePath = "pack://application:,,,/Assets/Flags/ru.png" },
                new() { Name = "Türkçe",             Code = "tr-TR",  ImagePath = "pack://application:,,,/Assets/Flags/tr.png" },
                new() { Name = "Português (BR)",     Code = "pt-BR",  ImagePath = "pack://application:,,,/Assets/Flags/pt-BR.png" },
                new() { Name = "Português (PT)",     Code = "pt-PT",  ImagePath = "pack://application:,,,/Assets/Flags/pt-PT.png" },
                new() { Name = "Nederlands",         Code = "nl-NL",  ImagePath = "pack://application:,,,/Assets/Flags/nl.png" },
                new() { Name = "Dansk",              Code = "da-DK",  ImagePath = "pack://application:,,,/Assets/Flags/da.png" },
                new() { Name = "Norsk",              Code = "no-NO",  ImagePath = "pack://application:,,,/Assets/Flags/no.png" },
                new() { Name = "Svenska",            Code = "sv-SE",  ImagePath = "pack://application:,,,/Assets/Flags/sv-SE.png" },
                new() { Name = "Suomi",              Code = "fi-FI",  ImagePath = "pack://application:,,,/Assets/Flags/fi.png" },
                new() { Name = "Čeština",            Code = "cs-CZ",  ImagePath = "pack://application:,,,/Assets/Flags/cs.png" },
                new() { Name = "Magyar",             Code = "hu-HU",  ImagePath = "pack://application:,,,/Assets/Flags/hu.png" },
                new() { Name = "Română",             Code = "ro-RO",  ImagePath = "pack://application:,,,/Assets/Flags/ro.png" },
                new() { Name = "Srpski",             Code = "sr-SP",  ImagePath = "pack://application:,,,/Assets/Flags/sr.png" },
                new() { Name = "Ελληνικά",           Code = "el-GR",  ImagePath = "pack://application:,,,/Assets/Flags/el.png" },
                new() { Name = "Українська",         Code = "uk-UA",  ImagePath = "pack://application:,,,/Assets/Flags/uk.png" },
                new() { Name = "Tiếng Việt",         Code = "vi-VN",  ImagePath = "pack://application:,,,/Assets/Flags/vi.png" },
                new() { Name = "العربية",             Code = "ar-SA",  ImagePath = "pack://application:,,,/Assets/Flags/ar.png" },
                new() { Name = "עברית",              Code = "he-IL",  ImagePath = "pack://application:,,,/Assets/Flags/he.png" },
                new() { Name = "日本語",              Code = "ja-JP",  ImagePath = "pack://application:,,,/Assets/Flags/ja.png" },
                new() { Name = "한국어",              Code = "ko-KR",  ImagePath = "pack://application:,,,/Assets/Flags/ko.png" },
                new() { Name = "简体中文",            Code = "zh-CN",  ImagePath = "pack://application:,,,/Assets/Flags/zh-CN.png" },
                new() { Name = "繁體中文",            Code = "zh-TW",  ImagePath = "pack://application:,,,/Assets/Flags/zh-TW.png" },
                new() { Name = "简体中文 (Hans)",     Code = "zh-Hans", ImagePath = "pack://application:,,,/Assets/Flags/zh-Hans.png" },
                new() { Name = "繁體中文 (Hant)",     Code = "zh-Hant", ImagePath = "pack://application:,,,/Assets/Flags/zh-Hant.png" },
                new() { Name = "Català",             Code = "ca-ES",  ImagePath = "pack://application:,,,/Assets/Flags/ca.png" },
                new() { Name = "Afrikaans",          Code = "af-ZA",  ImagePath = "pack://application:,,,/Assets/Flags/af.png" },
            };

            CmbLanguage.ItemsSource = langs.OrderBy(l => l.Code == "" ? 1 : 0).ThenBy(l => l.Name).ToList();
        }

        public void LoadSettings()
        {
            LoadProfiles();
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

            BuildMarkerFilterPanel();
        }

        private void LoadProfiles()
        {
            if (CmbProfile == null) return;
            _isUpdatingProfiles = true;
            try
            {
                CmbProfile.ItemsSource = ProfileManager.GetAllProfiles();
                CmbProfile.SelectedItem = ProfileManager.CurrentProfile;
            }
            finally
            {
                _isUpdatingProfiles = false;
            }
            UpdateProfilePathDisplay();
        }

        private void UpdateProfilePathDisplay()
        {
            if (TxtProfilePath == null) return;
            var profile = ProfileManager.CurrentProfile;
            if (profile != null)
                TxtProfilePath.Text = $"Folder: {profile.FolderPath}";
        }

        private async void CmbProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingProfiles) return;

            if (CmbProfile.SelectedItem is Profile profile)
            {
                if (ParentWindow != null)
                {
                    ParentWindow.SaveCurrentProfileBeforeRestart();
                }
                App.IsRestartingForProfileSwitch = true;
                ProfileManager.SwitchToProfile(profile.Id);
                await App.RestartAsync();
            }
        }

        private async void BtnNewProfile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("New Profile", "Enter profile name:", "New Account");
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Result))
            {
                _isUpdatingProfiles = true;
                try
                {
                    if (ParentWindow != null)
                    {
                        ParentWindow.SaveCurrentProfileBeforeRestart();
                    }
                    App.IsRestartingForProfileSwitch = true;
                    ProfileManager.CreateProfile(dialog.Result.Trim());
                }
                finally
                {
                    _isUpdatingProfiles = false;
                }

                await App.RestartAsync();
            }
        }

        private void BtnRenameProfile_Click(object sender, RoutedEventArgs e)
        {
            var current = ProfileManager.CurrentProfile;
            if (current == null) return;
            var dialog = new InputDialog("Rename Profile", "Enter new name:", current.Name);
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Result))
            {
                ProfileManager.UpdateProfile(current.Id, dialog.Result.Trim());
                LoadProfiles();
            }
        }

        private async void BtnDeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            var current = ProfileManager.CurrentProfile;
            if (current == null) return;
            var result = MessageBox.Show($"Delete profile '{current.DisplayName}'?\n\nThis will permanently remove all data for this profile.",
                "Delete Profile", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                _isUpdatingProfiles = true;
                try
                {
                    App.IsRestartingForProfileSwitch = true;
                    ProfileManager.DeleteProfile(current.Id);
                }
                finally
                {
                    _isUpdatingProfiles = false;
                }

                await App.RestartAsync();
            }
        }

        private void BuildMarkerFilterPanel()
        {
            PopulateSizeCategoryCombo();
            PanelMarkerFilters.Children.Clear();

            var allNames = TrackingService.GetUniqueMarkerNames().OrderBy(n => n).ToList();

            string search = TxtMarkerSearch?.Text?.Trim() ?? "";
            string selectedCategory = (CmbSizeCategory?.SelectedItem as string) ?? "All";

            IEnumerable<string> filteredNames = allNames;
            if (selectedCategory != "All")
            {
                var catMarkers = TrackingService.GetAllCustomMarkers()
                    .Where(m => (m.SizeCategory ?? "") == selectedCategory)
                    .Select(m => m.Name)
                    .Distinct()
                    .ToHashSet();
                filteredNames = allNames.Where(n => catMarkers.Contains(n));
            }

            if (!string.IsNullOrEmpty(search))
                filteredNames = filteredNames.Where(n => n.Contains(search, StringComparison.OrdinalIgnoreCase));

            var markerNames = filteredNames.ToList();

            if (markerNames.Count == 0)
            {
                TxtMarkerCount.Text = string.IsNullOrEmpty(search) && selectedCategory == "All"
                    ? ""
                    : Properties.Resources.FilterByMarker;
                return;
            }

            TxtMarkerCount.Text = string.Format(Properties.Resources.CustomMarkerCount,
                TrackingService.GetCustomMarkers().Count, markerNames.Count);

            var headerRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var selectAllBtn = new WpfUi.Button
            {
                Content = Properties.Resources.SelectAll,
                Appearance = WpfUi.ControlAppearance.Secondary,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 8, 0)
            };
            selectAllBtn.Click += (s, e) =>
            {
                TrackingService.CustomMarkersEnabled = true;
                TrackingService.EnabledCustomMarkerNames = new HashSet<string>(markerNames);
                BuildMarkerFilterPanel();
                ParentWindow?.ApplySettings();
            };
            System.Windows.Controls.Grid.SetColumn(selectAllBtn, 0);

            var deselectAllBtn = new WpfUi.Button
            {
                Content = Properties.Resources.DeselectAll,
                Appearance = WpfUi.ControlAppearance.Secondary,
                Padding = new Thickness(8, 4, 8, 4)
            };
            deselectAllBtn.Click += (s, e) =>
            {
                TrackingService.CustomMarkersEnabled = false;
                TrackingService.EnabledCustomMarkerNames.Clear();
                BuildMarkerFilterPanel();
                ParentWindow?.ApplySettings();
            };
            System.Windows.Controls.Grid.SetColumn(deselectAllBtn, 2);

            headerRow.Children.Add(selectAllBtn);
            headerRow.Children.Add(deselectAllBtn);
            PanelMarkerFilters.Children.Add(headerRow);

            foreach (var name in markerNames)
            {
                var chk = new System.Windows.Controls.CheckBox
                {
                    Content = name,
                    IsChecked = TrackingService.CustomMarkersEnabled &&
                        (TrackingService.EnabledCustomMarkerNames.Count == 0 || TrackingService.EnabledCustomMarkerNames.Contains(name)),
                    Margin = new Thickness(0, 4, 0, 4),
                    Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary")
                };

                var markerName = name;
                chk.Checked += (s, e) =>
                {
                    TrackingService.CustomMarkersEnabled = true;
                    TrackingService.EnabledCustomMarkerNames.Add(markerName);
                    ParentWindow?.ApplySettings();
                };
                chk.Unchecked += (s, e) =>
                {
                    TrackingService.EnabledCustomMarkerNames.Remove(markerName);
                    if (TrackingService.EnabledCustomMarkerNames.Count == 0)
                        TrackingService.CustomMarkersEnabled = false;
                    ParentWindow?.ApplySettings();
                };

                PanelMarkerFilters.Children.Add(chk);
            }
        }

        private void TxtMarkerSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            BuildMarkerFilterPanel();
        }

        private void CmbSizeCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isSettingsInitialized) return;
            BuildMarkerFilterPanel();
        }

        private void PopulateSizeCategoryCombo()
        {
            if (CmbSizeCategory == null) return;
            var categories = TrackingService.GetUniqueSizeCategories();
            var current = CmbSizeCategory.SelectedItem as string;
            CmbSizeCategory.SelectionChanged -= CmbSizeCategory_SelectionChanged;
            CmbSizeCategory.Items.Clear();
            CmbSizeCategory.Items.Add("All");
            foreach (var cat in categories)
                CmbSizeCategory.Items.Add(cat);
            if (current != null && CmbSizeCategory.Items.Contains(current))
                CmbSizeCategory.SelectedItem = current;
            else
                CmbSizeCategory.SelectedIndex = 0;
            CmbSizeCategory.SelectionChanged += CmbSizeCategory_SelectionChanged;
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

        private void BtnShowResetDialog_Click(object sender, RoutedEventArgs e)
        {
            if (ParentWindow == null) return;
            var dialog = new ResetDataWindow { Owner = ParentWindow };
            if (dialog.ShowDialog() == true)
            {
                _ = ParentWindow.PerformGranularResetAsync(
                    dialog.ResetConnection,
                    dialog.ResetProfiles,
                    dialog.ResetSteam,
                    dialog.ResetPairing,
                    dialog.ResetCrosshairs,
                    dialog.ResetCache
                );
            }
        }

        private void BtnBackupData_Click(object sender, RoutedEventArgs e)
        {
            if (ParentWindow == null) return;

            var profiles = ProfileManager.GetAllProfiles();
            if (profiles.Count == 0)
            {
                MessageBox.Show("No profiles to backup.", "Backup", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var profileDialog = new BackupProfileDialog(profiles) { Owner = ParentWindow };
            if (profileDialog.ShowDialog() != true || profileDialog.SelectedProfileIds.Count == 0)
                return;

            var passwordDialog = new BackupPasswordDialog { Owner = ParentWindow };
            passwordDialog.SetMode(false);

            if (passwordDialog.ShowDialog() == true)
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "ZIP Archives (*.zip)|*.zip",
                    FileName = "RustPlusDesk_Backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip",
                    Title = Properties.Resources.BackupApplicationDataTitle
                };

                if (sfd.ShowDialog() == true)
                {
                    try
                    {
                        RustPlusDesk.Services.Data.BackupDataModule.CreateBackup(sfd.FileName, passwordDialog.Password, profileDialog.SelectedProfileIds);
                        ParentWindow.AppendLog(string.Format(Properties.Resources.BackupSuccessLog, sfd.FileName));
                        MessageBox.Show(Properties.Resources.BackupSuccessMessage, Properties.Resources.BackupSuccessTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        ParentWindow.AppendLog(string.Format(Properties.Resources.BackupErrorLog, ex.Message));
                        MessageBox.Show(string.Format(Properties.Resources.BackupErrorMessage, ex.Message), Properties.Resources.BackupFailedTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnRestoreData_Click(object sender, RoutedEventArgs e)
        {
            if (ParentWindow == null) return;

            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "ZIP Archives (*.zip)|*.zip",
                Title = Properties.Resources.RestoreApplicationDataTitle
            };

            if (ofd.ShowDialog() != true) return;

            string password = "";
            if (RustPlusDesk.Services.Data.BackupDataModule.IsBackupEncrypted(ofd.FileName))
            {
                var dialog = new BackupPasswordDialog { Owner = ParentWindow };
                dialog.SetMode(true);

                if (dialog.ShowDialog() == true)
                {
                    password = dialog.Password;
                }
                else
                {
                    return;
                }
            }

            try
            {
                var profilesInBackup = RustPlusDesk.Services.Data.BackupDataModule.GetProfilesFromBackup(ofd.FileName, password);
                if (profilesInBackup.Count == 0)
                {
                    MessageBox.Show("No profiles found in backup.", Properties.Resources.RestoreFailedTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var existingProfiles = ProfileManager.GetAllProfiles();
                bool hasExistingProfiles = existingProfiles.Count > 0;

                var restoreDialog = new RestoreProfileDialog(profilesInBackup, hasExistingProfiles) { Owner = ParentWindow };
                if (restoreDialog.ShowDialog() != true || restoreDialog.SelectedProfileIds.Count == 0)
                    return;

                RustPlusDesk.Services.Data.BackupDataModule.RestoreBackup(ofd.FileName, password, restoreDialog.SelectedProfileIds, restoreDialog.Mode);
                ParentWindow.ReloadApplicationData();
                MessageBox.Show(Properties.Resources.RestoreSuccessMessage, Properties.Resources.RestoreSuccessTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                ParentWindow.AppendLog(Properties.Resources.RestorePasswordErrorLog);
                MessageBox.Show(Properties.Resources.RestorePasswordErrorMessage, Properties.Resources.RestoreFailedTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                ParentWindow.AppendLog(string.Format(Properties.Resources.RestoreErrorLog, ex.Message));
                MessageBox.Show(string.Format(Properties.Resources.RestoreErrorMessage, ex.Message), Properties.Resources.RestoreFailedTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

        private void BtnLoadCustomMarkers_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = Properties.Resources.LoadCustomMarkersTitle
            };

            if (ofd.ShowDialog() == true)
            {
                try
                {
                    var json = File.ReadAllText(ofd.FileName);
                    List<CustomMapMarker>? markers = null;

                    try
                    {
                        var data = System.Text.Json.JsonSerializer.Deserialize<CustomMarkerFile>(json);
                        if (data != null && data.Markers.Count > 0)
                            markers = data.Markers;
                    }
                    catch { }

                    if (markers == null)
                    {
                        try
                        {
                            markers = System.Text.Json.JsonSerializer.Deserialize<List<CustomMapMarker>>(json);
                        }
                        catch { }
                    }

                    if (markers != null && markers.Count > 0)
                    {
                        TrackingService.CustomMarkersJson = json;
                        TrackingService.CustomMarkersEnabled = true;
                        var names = markers.Select(m => m.Name).Distinct().ToList();
                        TrackingService.EnabledCustomMarkerNames = new HashSet<string>(names);
                        BuildMarkerFilterPanel();
                        ParentWindow?.ApplySettings();
                        MessageBox.Show(string.Format(Properties.Resources.CustomMarkersLoaded, markers.Count, names.Count),
                            Properties.Resources.Success, MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show(Properties.Resources.InvalidMarkerFile, Properties.Resources.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format(Properties.Resources.ErrorLoadingMarkers, ex.Message),
                        Properties.Resources.Error, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
