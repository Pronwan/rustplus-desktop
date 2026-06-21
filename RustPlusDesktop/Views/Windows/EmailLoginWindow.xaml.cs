using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;

namespace RustPlusDesk.Views.Windows
{
    public partial class EmailLoginWindow : Window
    {
        public bool LoginSuccessful { get; private set; }

        private CancellationTokenSource? _pollCts;
        private string? _pendingEmail;
        private string? _pendingPassword;

        public EmailLoginWindow()
        {
            InitializeComponent();
            ApplyLocalizedText();
        }

        private static string T(string key, string fallback)
        {
            return RustPlusDesk.Properties.Resources.ResourceManager.GetString(key) ?? fallback;
        }

        private void ApplyLocalizedText()
        {
            Title = T("EmailLoginWindowTitle", "Cloud Account Login");
            TxtHeaderTitle.Text = T("CloudAccountTitle", "Cloud Account");
            TabSignIn.Content = T("EmailSignInTab", "Sign in");
            TabSignUp.Content = T("EmailSignUpTab", "Create account");
            LblSignInEmail.Text = T("EmailAddressLabel", "Email address");
            LblSignInPassword.Text = T("PasswordLabel", "Password");
            LblSignUpEmail.Text = T("EmailAddressLabel", "Email address");
            LblSignUpPassword.Text = T("PasswordMinCharsLabel", "Password (min. 6 characters)");
            LblConfirmPassword.Text = T("ConfirmPasswordLabel", "Confirm password");
            BtnSignIn.Content = T("EmailSignInButton", "Sign in");
            BtnSignUp.Content = T("EmailCreateAccountButton", "Create account");
            TxtLoadingMsg.Text = T("PleaseWait", "Please wait...");
            TxtAwaitTitle.Text = T("EmailConfirmationSentTitle", "Confirmation email sent!");
            TxtAwaitMsg.Text = T("EmailConfirmationSentMessage", "Click the link in your email. The app checks automatically every few seconds...");
            TxtPollCount.Text = T("EmailWaitingForConfirmation", "Waiting for confirmation...");
            BtnCancelPolling.Content = T("Cancel", "Cancel");
            TxtProblems.Text = T("EmailProblemsPrefix", "Problems? ");
            TxtOpenBrowser.Text = T("EmailOpenInBrowser", "Open in browser");
        }

        /// <summary>Open the window pre-selected on the Sign Up tab.</summary>
        public void ShowSignUp()
        {
            TabSignUp.IsChecked = true;
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (TabSignIn == null || PanelSignIn == null || PanelSignUp == null) return; // not loaded yet
            bool signIn = TabSignIn.IsChecked == true;
            PanelSignIn.Visibility = signIn ? Visibility.Visible : Visibility.Collapsed;
            PanelSignUp.Visibility = signIn ? Visibility.Collapsed : Visibility.Visible;
            HideStatus();
        }

        // ──────────────────────────────────────────────────────────
        // Sign In
        // ──────────────────────────────────────────────────────────

        private async void BtnSignIn_Click(object sender, RoutedEventArgs e)
        {
            var email = TxtSignInEmail.Text.Trim();
            var password = PwdSignIn.Password;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                ShowError(T("EmailPasswordRequiredError", "Please enter email and password."));
                return;
            }

            SetBusy(true, T("EmailSigningInStatus", "Signing in..."));

            var (success, error) = await Services.Auth.SupabaseAuthManager.LoginWithEmailAsync(email, password);

            SetBusy(false);

            if (success)
            {
                LoginSuccessful = true;
                ShowSuccess(T("EmailSignedInSuccess", "Successfully signed in!"));
                await System.Threading.Tasks.Task.Delay(800);
                Close();
            }
            else
            {
                ShowError(error ?? T("EmailSignInFailed", "Sign-in failed."));
            }
        }

        private void PwdSignIn_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                BtnSignIn_Click(sender, e);
        }

        private async void LnkForgotPassword_Click(object sender, RoutedEventArgs e)
        {
            var email = TxtSignInEmail.Text.Trim();
            if (string.IsNullOrWhiteSpace(email))
            {
                ShowError(T("EmailRequiredForResetError", "Please enter your email address to reset password."));
                return;
            }

            SetBusy(true, T("EmailResettingPasswordStatus", "Sending reset email..."));

            var (success, error) = await Services.Auth.SupabaseAuthManager.SendPasswordResetEmailAsync(email);

            SetBusy(false);

            if (success)
            {
                ShowSuccess(T("EmailResetPasswordSent", "Password reset email sent. Check your inbox."));
            }
            else
            {
                ShowError(error ?? T("EmailResetPasswordFailed", "Failed to send reset email."));
            }
        }

        // ──────────────────────────────────────────────────────────
        // Sign Up + Email Confirmation Polling
        // ──────────────────────────────────────────────────────────

        private async void BtnSignUp_Click(object sender, RoutedEventArgs e)
        {
            var email = TxtSignUpEmail.Text.Trim();
            var pw1 = PwdSignUp.Password;
            var pw2 = PwdSignUpConfirm.Password;

            if (string.IsNullOrWhiteSpace(email))
            {
                ShowError(T("EmailRequiredError", "Please enter an email address."));
                return;
            }
            if (pw1.Length < 6)
            {
                ShowError(T("EmailPasswordTooShortError", "Password must be at least 6 characters long."));
                return;
            }
            if (pw1 != pw2)
            {
                ShowError(T("EmailPasswordsDoNotMatchError", "The passwords do not match."));
                return;
            }

            SetBusy(true, T("EmailRegistrationRunningStatus", "Registration running..."));

            var (success, error) = await Services.Auth.SupabaseAuthManager.SignUpWithEmailAsync(email, pw1);

            SetBusy(false);

            if (!success)
            {
                ShowError(error ?? T("EmailRegistrationFailed", "Registration failed."));
                return;
            }

            // Registration OK → switch to polling UI
            _pendingEmail = email;
            _pendingPassword = pw1;
            StartPolling();
        }

        private void StartPolling()
        {
            PanelSignUp.Visibility = Visibility.Collapsed;
            PanelStatus.Visibility = Visibility.Collapsed;
            PanelAwaitConfirm.Visibility = Visibility.Visible;

            _pollCts = new CancellationTokenSource();
            int tick = 0;

            _ = Services.Auth.SupabaseAuthManager.PollEmailConfirmedAsync(
                _pendingEmail!,
                _pendingPassword!,
                onProgress: () =>
                {
                    tick++;
                    Dispatcher.Invoke(() =>
                    {
                        TxtPollCount.Text = string.Format(T("EmailWaitingForConfirmationFormat", "Waiting for confirmation... ({0}s)"), tick * 4);
                    });
                },
                cancellationToken: _pollCts.Token
            ).ContinueWith(t =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (t.Result)
                    {
                        LoginSuccessful = true;
                        Close();
                    }
                    else if (!(_pollCts?.IsCancellationRequested ?? true))
                    {
                        PanelAwaitConfirm.Visibility = Visibility.Collapsed;
                        ShowError(T("EmailConfirmationTimeout", "Timeout: The email was not confirmed within 5 minutes. You can sign in later."));
                        PanelSignIn.Visibility = Visibility.Visible;
                        TabSignIn.IsChecked = true;
                        TxtSignInEmail.Text = _pendingEmail ?? "";
                    }
                });
            });
        }

        private void BtnCancelPolling_Click(object sender, RoutedEventArgs e)
        {
            _pollCts?.Cancel();
            PanelAwaitConfirm.Visibility = Visibility.Collapsed;
            PanelSignUp.Visibility = Visibility.Visible;
            HideStatus();
        }

        // ──────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────

        private void SetBusy(bool busy, string? msg = null)
        {
            BtnSignIn.IsEnabled = !busy;
            BtnSignUp.IsEnabled = !busy;

            if (busy && msg != null)
            {
                PanelStatus.Visibility = Visibility.Visible;
                PanelLoading.Visibility = Visibility.Visible;
                TxtLoadingMsg.Text = msg;
                TxtStatusMsg.Text = "";
                TxtStatusMsg.Foreground = System.Windows.Media.Brushes.Transparent;
            }
            else if (!busy)
            {
                PanelLoading.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowError(string msg)
        {
            PanelStatus.Visibility = Visibility.Visible;
            PanelLoading.Visibility = Visibility.Collapsed;
            TxtStatusMsg.Text = msg;
            TxtStatusMsg.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xE5, 0x53, 0x3D));
        }

        private void ShowSuccess(string msg)
        {
            PanelStatus.Visibility = Visibility.Visible;
            PanelLoading.Visibility = Visibility.Collapsed;
            TxtStatusMsg.Text = msg;
            TxtStatusMsg.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50));
        }

        private void HideStatus()
        {
            PanelStatus.Visibility = Visibility.Collapsed;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_Closed(object sender, EventArgs e)
        {
            _pollCts?.Cancel();
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
