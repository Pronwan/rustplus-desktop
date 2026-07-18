using System.Windows;
using System.Windows.Input;

namespace RustPlusDesk.Views.Windows.Dialogs
{
    public partial class FcmConsentWindow : Window
    {
        public bool Accepted { get; private set; }

        public FcmConsentWindow()
        {
            InitializeComponent();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) return; // Prevent maximizing
            DragMove();
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
