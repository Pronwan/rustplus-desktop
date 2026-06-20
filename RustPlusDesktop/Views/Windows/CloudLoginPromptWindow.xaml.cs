using System;
using System.Windows;
using System.Windows.Input;

namespace RustPlusDesk.Views.Windows
{
    public partial class CloudLoginPromptWindow : Window
    {
        private MainWindow? _owner;

        private readonly bool _sessionExpired;

        public CloudLoginPromptWindow(MainWindow owner, bool sessionExpired = false)
        {
            InitializeComponent();
            _owner = owner;
            _sessionExpired = sessionExpired;
            Owner = owner;
            ApplyLocalizedText();
        }

        private static string T(string key, string fallback)
        {
            return RustPlusDesk.Properties.Resources.ResourceManager.GetString(key) ?? fallback;
        }

        private void ApplyLocalizedText()
        {
            if (_sessionExpired)
            {
                Title = T("SessionExpiredTitle", "Session expired");
                TxtTitle.Text = T("SessionExpiredHeading", "Please sign in again");
                TxtDescription.Text = T(
                    "SessionExpiredDescription",
                    "Your account session has expired. Sign in again with email or Discord to continue using account features.");
            }
            else
            {
                Title = T("CloudLoginPromptTitle", "Cloud Sync");
                TxtTitle.Text = T("CloudLoginPromptHeading", "Account required for Cloud Sync");
                TxtDescription.Text = T(
                    "CloudLoginPromptDescription",
                    "Cloud services like synchronization and backup storage require a free account. Choose email or Discord to continue syncing your devices, overlays and backups.");
            }
            TxtEmailLogin.Text = T("CloudLoginPromptEmailButton", "Sync via email");
            TxtDiscordLogin.Text = T("CloudLoginPromptDiscordButton", "Sync via Discord");
            BtnSkip.Content = T("CloudLoginPromptNoThanksButton", "No thanks");
        }

        private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private async void BtnDiscord_Click(object sender, RoutedEventArgs e)
        {
            BtnDiscord.IsEnabled = false;
            BtnEmail.IsEnabled = false;
            Close();

            bool success = await Services.Auth.SupabaseAuthManager.LoginWithDiscordAsync();
            if (success)
            {
                _owner?.AppendLog("[Cloud] Discord login successful.");
                _owner?.UpdateCloudSyncUI();
                if (_owner?.AppSettingsPanel != null)
                {
                    _owner.AppSettingsPanel.LoadSettings();
                }
            }
            else
            {
                _owner?.AppendLog("[Cloud] Discord login canceled or failed.");
            }
        }

        private void BtnEmail_Click(object sender, RoutedEventArgs e)
        {
            Close();
            var emailWin = new EmailLoginWindow { Owner = _owner };
            if (emailWin.ShowDialog() == true && emailWin.LoginSuccessful)
            {
                _owner?.AppendLog("[Cloud] Email login successful via Cloud prompt.");
                _owner?.UpdateCloudSyncUI();
                if (_owner?.AppSettingsPanel != null)
                {
                    _owner.AppSettingsPanel.LoadSettings();
                }
            }
        }

        private void BtnSkip_Click(object sender, RoutedEventArgs e) => Close();
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
