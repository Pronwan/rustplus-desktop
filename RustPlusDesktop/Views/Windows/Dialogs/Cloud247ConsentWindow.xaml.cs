using System.Windows;

namespace RustPlusDesk.Views.Windows.Dialogs
{
    public partial class Cloud247ConsentWindow : Wpf.Ui.Controls.FluentWindow
    {
        public bool Accepted { get; private set; }

        public Cloud247ConsentWindow(Window? owner = null)
        {
            InitializeComponent();
            if (owner != null)
            {
                Owner = owner;
            }
        }

        private void BtnDecline_Click(object sender, RoutedEventArgs e)
        {
            Accepted = false;
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
