using System.Threading.Tasks;
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

        private static string T(string key, string fallback)
        {
            return Properties.Resources.ResourceManager.GetString(key) ?? fallback;
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
            
            // Cloud Sync Setting load
            ChkCloudSync.IsChecked = TrackingService.CloudSyncEnabled;

            // Team marker settings
            ChkShowProfileMarkers.IsChecked  = TrackingService.MapShowSteamMarkers;
            ChkShowPlayerArrows.IsChecked    = TrackingService.MapShowPlayerArrows;
            ChkShowDeathMarkers.IsChecked    = TrackingService.MapShowDeathTags;
            ChkStreamerModeMarkers.IsChecked  = TrackingService.MapAbbreviateNames;
            SliderPlayerIconScaleOverlay.Value = TrackingService.MapPlayerIconScale;


            // Auth connection state
            bool isDiscord = Services.Auth.SupabaseAuthManager.IsDiscordAuthenticated;
            bool isEmail   = Services.Auth.SupabaseAuthManager.IsEmailAuthenticated;
            bool connected = isDiscord || isEmail;
            bool isPremium = Services.Auth.SupabaseAuthManager.IsPremium;

            if (isDiscord)
            {
                TxtDiscordBtnLabel.Text = T("AuthDiscordDisconnectButton", "Disconnect Discord");
                BtnDiscordConnect.Appearance = Wpf.Ui.Controls.ControlAppearance.Caution;

                int maxBytes = Services.Auth.SupabaseAuthManager.GetMaxOverlayBytes();
                string maxOverlay = maxBytes == int.MaxValue ? "unlimited" : $"{maxBytes / 1024} KB";
                int maxDevices = Services.Auth.SupabaseAuthManager.GetMaxDevices();
                string maxDevs = maxDevices == int.MaxValue ? "unlimited" : maxDevices.ToString();
                int maxBases = Services.Auth.SupabaseAuthManager.GetMaxBases();
                string maxBs = maxBases == int.MaxValue ? "unlimited" : maxBases.ToString();

                int currentOverlayKb = ParentWindow != null ? Math.Max(1, (int)Math.Ceiling(ParentWindow.GetCurrentOverlaySizeBytes() / 1024.0)) : 0;
                int currentDevices = ParentWindow != null ? ParentWindow.GetCurrentDevicesCount() : 0;
                int currentBases = ParentWindow != null ? ParentWindow.GetCurrentBaseCount() : 0;

                string baseText = string.Format(T("AuthDiscordConnectedFormat", "Discord connected - Tier: {0}"), Services.Auth.SupabaseAuthManager.CurrentTier.ToUpper());
                TxtAuthStatus.Text = $"{baseText}\nLimits Usage:\n• Overlay size: {currentOverlayKb} KB / {maxOverlay}\n• Devices: {currentDevices} / {maxDevs}\n• Bases: {currentBases} / {maxBs}";
                TxtAuthStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50));
            }
            else if (isEmail)
            {
                var email = Services.Auth.SupabaseAuthManager.Client?.Auth?.CurrentUser?.Email ?? "";
                TxtDiscordBtnLabel.Text = "Discord";
                BtnDiscordConnect.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;

                int maxBytes = Services.Auth.SupabaseAuthManager.GetMaxOverlayBytes();
                string maxOverlay = maxBytes == int.MaxValue ? "unlimited" : $"{maxBytes / 1024} KB";
                int maxDevices = Services.Auth.SupabaseAuthManager.GetMaxDevices();
                string maxDevs = maxDevices == int.MaxValue ? "unlimited" : maxDevices.ToString();
                int maxBases = Services.Auth.SupabaseAuthManager.GetMaxBases();
                string maxBs = maxBases == int.MaxValue ? "unlimited" : maxBases.ToString();

                int currentOverlayKb = ParentWindow != null ? Math.Max(1, (int)Math.Ceiling(ParentWindow.GetCurrentOverlaySizeBytes() / 1024.0)) : 0;
                int currentDevices = ParentWindow != null ? ParentWindow.GetCurrentDevicesCount() : 0;
                int currentBases = ParentWindow != null ? ParentWindow.GetCurrentBaseCount() : 0;

                string baseText = string.Format(T("AuthEmailConnectedFormat", "Email connected: {0} - Tier: {1}"), email, Services.Auth.SupabaseAuthManager.CurrentTier.ToUpper());
                TxtAuthStatus.Text = $"{baseText}\nLimits Usage:\n• Overlay size: {currentOverlayKb} KB / {maxOverlay}\n• Devices: {currentDevices} / {maxDevs}\n• Bases: {currentBases} / {maxBs}";
                TxtAuthStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50));
            }
            else
            {
                TxtDiscordBtnLabel.Text = "Discord";
                BtnDiscordConnect.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
                TxtAuthStatus.Text = T("AuthNotConnectedStatus", "Not connected - sign in to use Cloud Sync and backups");
                TxtAuthStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
            }

            BrdSupporterSettings.IsEnabled = connected && isPremium;
            BrdSupporterSettings.Opacity = (connected && isPremium) ? 1.0 : 0.5;
            BtnEmailConnect.Content = T("EmailAccountButton", "Email / Account");

            if (connected && isPremium)
            {
                _ = LoadDiscordBotSettingsAsync();
            }
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
            
            // Save Cloud Sync setting
            if (sender == ChkCloudSync)
            {
                if (ChkCloudSync.IsChecked == true)
                {
                    if (ParentWindow != null)
                    {
                        var dlg = new CloudDisclaimerWindow { Owner = ParentWindow };
                        dlg.ShowDialog();
                        if (dlg.CloudSyncAccepted)
                        {
                            TrackingService.CloudSyncEnabled = true;
                            TrackingService.UploadConsentGiven = true;
                            _ = Services.Auth.SupabaseAuthManager.UpdateCloudSyncConsentAsync(true);
                        }
                        else
                        {
                            _isSettingsInitialized = false;
                            ChkCloudSync.IsChecked = false;
                            _isSettingsInitialized = true;
                            TrackingService.CloudSyncEnabled = false;
                            TrackingService.UploadConsentGiven = false;
                            _ = Services.Auth.SupabaseAuthManager.UpdateCloudSyncConsentAsync(false);
                        }
                    }
                    else
                    {
                        TrackingService.CloudSyncEnabled = true;
                        TrackingService.UploadConsentGiven = true;
                        _ = Services.Auth.SupabaseAuthManager.UpdateCloudSyncConsentAsync(true);
                    }
                }
                else
                {
                    TrackingService.CloudSyncEnabled = false;
                    TrackingService.UploadConsentGiven = false;
                    _ = Services.Auth.SupabaseAuthManager.UpdateCloudSyncConsentAsync(false);
                }
            }
            else
            {
                TrackingService.CloudSyncEnabled = ChkCloudSync.IsChecked == true;
            }

            ParentWindow?.ApplySettings();
            ParentWindow?.UpdateCloudSyncUI();
        }

        private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
        {
            Visibility = Visibility.Collapsed;
            ParentWindow?.ApplySettings();
        }

        private void OnMarkerSettingChanged(object sender, RoutedEventArgs e)
        {
            if (!_isSettingsInitialized) return;

            TrackingService.MapShowSteamMarkers  = ChkShowProfileMarkers.IsChecked == true;
            TrackingService.MapShowPlayerArrows  = ChkShowPlayerArrows.IsChecked == true;
            TrackingService.MapShowDeathTags     = ChkShowDeathMarkers.IsChecked == true;
            TrackingService.MapAbbreviateNames   = ChkStreamerModeMarkers.IsChecked == true;
            TrackingService.MapPlayerIconScale   = SliderPlayerIconScaleOverlay.Value;

            ParentWindow?.SyncPlayerSettingsFromTrackingService();
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

            var dialog = new BackupPasswordDialog { Owner = ParentWindow };
            dialog.SetMode(false); // Encryption mode

            if (dialog.ShowDialog() == true)
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
                        RustPlusDesk.Services.Data.BackupDataModule.CreateBackup(sfd.FileName, dialog.Password);
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

            var ask = MessageBox.Show(
                Properties.Resources.RestoreConfirmMessage,
                Properties.Resources.RestoreConfirmTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (ask != MessageBoxResult.Yes) return;

            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "ZIP Archives (*.zip)|*.zip",
                Title = Properties.Resources.RestoreApplicationDataTitle
            };

            if (ofd.ShowDialog() == true)
            {
                string password = "";
                if (RustPlusDesk.Services.Data.BackupDataModule.IsBackupEncrypted(ofd.FileName))
                {
                    var dialog = new BackupPasswordDialog { Owner = ParentWindow };
                    dialog.SetMode(true); // Decryption mode

                    if (dialog.ShowDialog() == true)
                    {
                        password = dialog.Password;
                    }
                    else
                    {
                        // User canceled decryption prompt, abort restore
                        return;
                    }
                }

                try
                {
                    RustPlusDesk.Services.Data.BackupDataModule.RestoreBackup(ofd.FileName, password);
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
        }

        private void BtnCompareCloud_Click(object sender, RoutedEventArgs e)
        {
            var cloudWindow = new RustPlusDesk.Views.Windows.CloudFeaturesWindow();
            cloudWindow.Owner = ParentWindow ?? Window.GetWindow(this);
            cloudWindow.ShowDialog();
        }

        private async void BtnDiscordConnect_Click(object sender, RoutedEventArgs e)
        {
            BtnDiscordConnect.IsEnabled = false;
            BtnEmailConnect.IsEnabled = false;

            if (Services.Auth.SupabaseAuthManager.IsDiscordAuthenticated)
            {
                // Disconnect Discord
                TxtDiscordBtnLabel.Text = T("AuthDisconnectingStatus", "Disconnecting...");
                await Services.Auth.SupabaseAuthManager.LogoutAsync();
                ParentWindow?.AppendLog("[Cloud] Discord disconnected.");
            }
            else
            {
                // Connect Discord
                ParentWindow?.AppendLog("[Cloud] Starting Discord OAuth login...");
                TxtDiscordBtnLabel.Text = T("AuthConnectingStatus", "Connecting...");
                bool success = await Services.Auth.SupabaseAuthManager.LoginWithDiscordAsync();

                if (success)
                {
                    ParentWindow?.AppendLog("[Cloud] Discord connected. Syncing roles...");
                    var tier = Services.Auth.SupabaseAuthManager.CurrentTier;
                    ParentWindow?.AppendLog($"[Cloud] Rollen-Sync abgeschlossen. Tier: {tier.ToUpper()}");
                }
                else
                {
                    ParentWindow?.AppendLog("[Cloud] Discord login failed or canceled.");
                }
            }

            BtnDiscordConnect.IsEnabled = true;
            BtnEmailConnect.IsEnabled = true;
            LoadSettings();
            ParentWindow?.UpdateCloudSyncUI();
        }

        private void BtnEmailConnect_Click(object sender, RoutedEventArgs e)
        {
            // If email-authenticated, offer logout
            if (Services.Auth.SupabaseAuthManager.IsEmailAuthenticated)
            {
                var result = System.Windows.MessageBox.Show(
                    T("EmailLogoutConfirmMessage", "Sign out of the email account?"),
                    T("CloudAccountTitle", "Cloud Account"),
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    _ = Services.Auth.SupabaseAuthManager.LogoutAsync();
                    ParentWindow?.AppendLog("[Cloud] Email account signed out.");
                    LoadSettings();
                    ParentWindow?.UpdateCloudSyncUI();
                }
                return;
            }

            // Open email login window
            var win = new Views.Windows.EmailLoginWindow { Owner = ParentWindow };
            if (win.ShowDialog() == true && win.LoginSuccessful)
            {
                ParentWindow?.AppendLog("[Cloud] Email login successful.");
                LoadSettings();
                ParentWindow?.UpdateCloudSyncUI();
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

        private void PremiumFeature_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!Services.Auth.SupabaseAuthManager.IsPremium)
            {
                e.Handled = true;
                if (ParentWindow != null)
                {
                    var win = new Views.Windows.PremiumInfoWindow("") { Owner = ParentWindow };
                    win.ShowDialog();
                }
            }
        }

        private void TxtSettingsDiscordWebhook_TextChanged(object sender, TextChangedEventArgs e)
        {
            var vm = ParentWindow?.DataContext as RustPlusDesk.ViewModels.MainViewModel;
            if (vm?.Selected != null)
            {
                ParentWindow.SyncAlertMenuItems();
            }
        }

        private void BtnClearSettingsWebhook_Click(object sender, RoutedEventArgs e)
        {
            var vm = ParentWindow?.DataContext as RustPlusDesk.ViewModels.MainViewModel;
            if (vm?.Selected != null)
            {
                vm.Selected.DiscordWebhookChatAlertsUrl = string.Empty;
                ParentWindow.SyncAlertMenuItems();
            }
        }

        private void BtnDiscordWebhookHelp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://support.discord.com/hc/en-us/articles/228383668-Intro-to-Webhooks",
                    UseShellExecute = true
                });
            }
            catch { }
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

        private void BtnInviteDiscordBot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://discord.com/oauth2/authorize?client_id=1511865399971545199&permissions=39584569300992&integration_type=0&scope=bot+applications.commands",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ParentWindow?.AppendLog($"[DiscordBot] Failed to open invite link: {ex.Message}");
            }
        }

        private async void BtnSaveDiscordGuild_Click(object sender, RoutedEventArgs e)
        {
            if (Services.Auth.SupabaseAuthManager.Client == null) return;
            var vm = ParentWindow?.DataContext as RustPlusDesk.ViewModels.MainViewModel;
            var steamId = vm?.SteamId64;
            if (string.IsNullOrEmpty(steamId)) return;

            var guildId = TxtDiscordGuildId.Text?.Trim();
            if (string.IsNullOrEmpty(guildId))
            {
                try
                {
                    BtnSaveDiscordGuild.IsEnabled = false;
                    await Services.Auth.SupabaseAuthManager.Client.From<RustPlusDesk.Models.DiscordBotSettingsModel>()
                        .Filter("owner_steam_id", Postgrest.Constants.Operator.Equals, steamId)
                        .Delete();
                    
                    MessageBox.Show("Discord Server unlinked successfully. The bot will no longer interact with your server.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    _ = LoadDiscordBotSettingsAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to unlink Discord Server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    BtnSaveDiscordGuild.IsEnabled = true;
                }
                return;
            }

            try
            {
                BtnSaveDiscordGuild.IsEnabled = false;

                var args = new Dictionary<string, object?>
                {
                    ["p_guild_id"] = guildId,
                    ["p_owner_steam_id"] = steamId,
                    ["p_commands_enabled"] = ChkDiscordCommandsEnabled.IsChecked != false,
                    ["p_allowed_command_role_ids"] = NormalizeDiscordRoleIds(TxtDiscordAllowedRoleIds.Text)
                };

                var result = await Services.Auth.SupabaseAuthManager.Client.Rpc<List<RustPlusDesk.Models.DiscordBotRegistrationResult>>(
                    "register_discord_bot_settings",
                    args);

                var registration = result?.FirstOrDefault();
                if (registration == null || !registration.Success)
                {
                    MessageBox.Show(registration?.Message ?? "Failed to link Discord Server.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                MessageBox.Show("Discord Server linked successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                _ = LoadDiscordBotSettingsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to link Discord Server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnSaveDiscordGuild.IsEnabled = true;
            }
        }

        private async void BtnSaveChannels_Click(object sender, RoutedEventArgs e)
        {
            if (Services.Auth.SupabaseAuthManager.Client == null) return;
            var guildId = TxtDiscordGuildId.Text?.Trim();
            if (string.IsNullOrEmpty(guildId))
            {
                MessageBox.Show("Please save a Discord Server ID first.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                BtnSaveChannels.IsEnabled = false;

                await SaveDiscordCommandPermissionsAsync(guildId);

                var existingRes = await Services.Auth.SupabaseAuthManager.Client
                    .From<RustPlusDesk.Models.DiscordChannelsConfigModel>()
                    .Filter("guild_id", Postgrest.Constants.Operator.Equals, guildId)
                    .Get();

                var existingList = existingRes.Models ?? new System.Collections.Generic.List<RustPlusDesk.Models.DiscordChannelsConfigModel>();

                async Task SaveChannelAsync(string type, string channelId, bool tts, bool audio)
                {
                    if (string.IsNullOrWhiteSpace(channelId))
                    {
                        var modelToDelete = existingList.FirstOrDefault(c => c.NotificationType == type);
                        if (modelToDelete != null)
                        {
                            await Services.Auth.SupabaseAuthManager.Client.From<RustPlusDesk.Models.DiscordChannelsConfigModel>()
                                .Filter("guild_id", Postgrest.Constants.Operator.Equals, guildId)
                                .Filter("notification_type", Postgrest.Constants.Operator.Equals, type)
                                .Delete();
                        }
                        return;
                    }

                    var model = existingList.FirstOrDefault(c => c.NotificationType == type) ?? new RustPlusDesk.Models.DiscordChannelsConfigModel
                    {
                        GuildId = guildId,
                        NotificationType = type,
                        CreatedAt = DateTime.UtcNow
                    };

                    model.ChannelId = channelId.Trim();
                    model.TtsEnabled = tts;
                    model.AudioAlertEnabled = audio;

                    await Services.Auth.SupabaseAuthManager.Client.From<RustPlusDesk.Models.DiscordChannelsConfigModel>().Upsert(model);
                }

                await SaveChannelAsync("raid", TxtChannelRaid.Text, ChkRaidTTS.IsChecked == true, ChkRaidAudio.IsChecked == true);
                await SaveChannelAsync("events", TxtChannelEvents.Text, ChkEventsTTS.IsChecked == true, ChkEventsAudio.IsChecked == true);
                await SaveChannelAsync("chat", TxtChannelChat.Text, ChkChatTTS.IsChecked == true, ChkChatAudio.IsChecked == true);
                await SaveChannelAsync("shop", TxtChannelShop.Text, ChkShopTTS.IsChecked == true, ChkShopAudio.IsChecked == true);

                MessageBox.Show("Channels configuration saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save channels: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnSaveChannels.IsEnabled = true;
            }
        }

        private async Task LoadDiscordBotSettingsAsync()
        {
            if (Services.Auth.SupabaseAuthManager.Client == null || !Services.Auth.SupabaseAuthManager.IsPremium) return;

            try
            {
                var vm = ParentWindow?.DataContext as RustPlusDesk.ViewModels.MainViewModel;
                var steamId = vm?.SteamId64;
                if (string.IsNullOrEmpty(steamId)) return;

                var guildRes = await Services.Auth.SupabaseAuthManager.Client
                    .From<RustPlusDesk.Models.DiscordBotSettingsModel>()
                    .Filter("owner_steam_id", Postgrest.Constants.Operator.Equals, steamId)
                    .Get();

                var guildSetting = guildRes.Models?.FirstOrDefault();
                if (guildSetting != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtDiscordGuildId.Text = guildSetting.GuildId;
                        ChkDiscordCommandsEnabled.IsChecked = guildSetting.CommandsEnabled;
                        TxtDiscordAllowedRoleIds.Text = guildSetting.AllowedCommandRoleIds ?? string.Empty;
                    });

                    var channelsRes = await Services.Auth.SupabaseAuthManager.Client
                        .From<RustPlusDesk.Models.DiscordChannelsConfigModel>()
                        .Filter("guild_id", Postgrest.Constants.Operator.Equals, guildSetting.GuildId)
                        .Get();

                    Dispatcher.Invoke(() =>
                    {
                        TxtChannelRaid.Text = string.Empty;
                        ChkRaidTTS.IsChecked = ChkRaidAudio.IsChecked = false;
                        TxtChannelEvents.Text = string.Empty;
                        ChkEventsTTS.IsChecked = ChkEventsAudio.IsChecked = false;
                        TxtChannelChat.Text = string.Empty;
                        ChkChatTTS.IsChecked = ChkChatAudio.IsChecked = false;

                        if (channelsRes.Models != null)
                        {
                            foreach (var ch in channelsRes.Models)
                            {
                                switch (ch.NotificationType)
                                {
                                    case "raid":
                                        TxtChannelRaid.Text = ch.ChannelId;
                                        ChkRaidTTS.IsChecked = ch.TtsEnabled;
                                        ChkRaidAudio.IsChecked = ch.AudioAlertEnabled;
                                        break;
                                    case "events":
                                        TxtChannelEvents.Text = ch.ChannelId;
                                        ChkEventsTTS.IsChecked = ch.TtsEnabled;
                                        ChkEventsAudio.IsChecked = ch.AudioAlertEnabled;
                                        break;
                                    case "chat":
                                        TxtChannelChat.Text = ch.ChannelId;
                                        ChkChatTTS.IsChecked = ch.TtsEnabled;
                                        ChkChatAudio.IsChecked = ch.AudioAlertEnabled;
                                        break;
                                    case "shop":
                                        TxtChannelShop.Text = ch.ChannelId;
                                        ChkShopTTS.IsChecked = ch.TtsEnabled;
                                        ChkShopAudio.IsChecked = ch.AudioAlertEnabled;
                                        break;
                                }
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                ParentWindow?.AppendLog($"[DiscordBot] Error loading settings: {ex.Message}");
            }
        }

        private static string NormalizeDiscordRoleIds(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var ids = raw
                .Split(new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim())
                .Where(id => id.All(char.IsDigit))
                .Distinct()
                .ToArray();

            return string.Join(",", ids);
        }

        private async Task SaveDiscordCommandPermissionsAsync(string guildId)
        {
            var vm = ParentWindow?.DataContext as RustPlusDesk.ViewModels.MainViewModel;
            var steamId = vm?.SteamId64;
            if (string.IsNullOrWhiteSpace(steamId)) return;

            var settings = new RustPlusDesk.Models.DiscordBotSettingsModel
            {
                GuildId = guildId,
                OwnerSteamId = steamId,
                CommandsEnabled = ChkDiscordCommandsEnabled.IsChecked != false,
                AllowedCommandRoleIds = NormalizeDiscordRoleIds(TxtDiscordAllowedRoleIds.Text),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await Services.Auth.SupabaseAuthManager.Client
                .From<RustPlusDesk.Models.DiscordBotSettingsModel>()
                .Upsert(settings);
        }
    }
}
