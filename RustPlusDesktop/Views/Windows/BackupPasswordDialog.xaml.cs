using System.Windows;

namespace RustPlusDesk.Views
{
    public partial class BackupPasswordDialog : Window
    {
        public string Password => PbPassword.Password;

        public BackupPasswordDialog()
        {
            InitializeComponent();
            PbPassword.Focus();
        }

        public void SetMode(bool isDecrypting)
        {
            if (isDecrypting)
            {
                TxtTitle.Text = Properties.Resources.RestoreBackupTitle;
                TxtDescription.Text = Properties.Resources.RestoreBackupDesc;
                BtnConfirm.Content = Properties.Resources.Decrypt;
            }
            else
            {
                TxtTitle.Text = Properties.Resources.BackupProtectionTitle;
                TxtDescription.Text = Properties.Resources.BackupProtectionDesc;
                BtnConfirm.Content = Properties.Resources.Encrypt;
            }
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // Allow dragging the window from empty spaces
        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}
