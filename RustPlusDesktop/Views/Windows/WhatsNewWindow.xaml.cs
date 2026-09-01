using System.Windows;
using System.Windows.Input;

namespace RustPlusDesk.Views.Windows
{
    public partial class WhatsNewWindow : Window
    {
        public bool DontShowAgain => ChkDontShowAgain.IsChecked == true;

        public WhatsNewWindow()
        {
            InitializeComponent();
            
            // Allow dragging the window
            MouseLeftButtonDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                    this.DragMove();
            };
        }

        private void BtnPatchNotes_Click(object sender, RoutedEventArgs e)
        {
            var pnw = new PatchNotesWindow { Owner = this.Owner ?? this };
            pnw.ShowDialog();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
