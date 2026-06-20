using System.Windows;

namespace RustPlusDesk.Views.Windows
{
    public partial class Version7NoticeWindow : Window
    {
        public bool DontShowAgain { get; private set; }

        public Version7NoticeWindow()
        {
            InitializeComponent();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DontShowAgain = ChkDontShowAgain.IsChecked == true;
            this.Close();
        }
    }
}
