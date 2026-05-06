using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace RustPlusDesk.Views
{
    public partial class ChatWindow : Window
    {
        private readonly Func<string, Task> _sendAsync;

        public ChatWindow(Func<string, Task> sendAsync)
        {
            InitializeComponent();
            _sendAsync = sendAsync ?? (_ => Task.CompletedTask);
        }

        public void AddIncoming(string author, string text, DateTime? ts = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var time = (ts ?? DateTime.Now).ToString("HH:mm");
            TxtHistory.Text += (TxtHistory.Text.Length > 0 ? Environment.NewLine : "") + $"{time} {author}: {text}";
        }

        private async Task SendAsync()
        {
            var text = TxtChat.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            try
            {
                await _sendAsync(text);
                //TxtHistory.Text += (TxtHistory.Text.Length > 0 ? Environment.NewLine : "") + $"Du: {text}";
                TxtChat.Clear();
                TxtChat.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Senden fehlgeschlagen: " + ex.Message, "Chat", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BtnSend_Click(object sender, RoutedEventArgs e) => await SendAsync();
        private async void TxtChat_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                await SendAsync();
            }
        }
    }
}
