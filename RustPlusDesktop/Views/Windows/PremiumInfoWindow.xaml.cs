using System.Windows;
using System.Windows.Input;

namespace RustPlusDesk.Views.Windows
{
    public partial class PremiumInfoWindow : Window
    {
        public PremiumInfoWindow(string message)
        {
            InitializeComponent();
            TxtDescription.Text = message;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }
        
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            this.DragMove();
        }
    }
}
