using System.Windows;

namespace RustPlusDesk.Views.Windows.Dialogs
{
    public partial class FcmConsentWindow : Wpf.Ui.Controls.FluentWindow
    {
        public bool Accepted { get; private set; }

        public FcmConsentWindow()
        {
            InitializeComponent();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnAccept_Click(object sender, RoutedEventArgs e)
        {
            Accepted = true;
            DialogResult = true;
            Close();
        }
    }
}
