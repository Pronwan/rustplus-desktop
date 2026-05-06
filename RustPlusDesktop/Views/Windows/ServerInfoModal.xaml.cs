using System.Windows;

namespace RustPlusDesk.Views
{
    public partial class ServerInfoModal : Window
    {
        public ServerInfoModal(string serverName, string description)
        {
            InitializeComponent();
            Title = serverName;
            TxtDescription.Text = string.IsNullOrWhiteSpace(description) 
                ? "No detailed description available for this server." 
                : description;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // Allow dragging the window
        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }
    }
}
