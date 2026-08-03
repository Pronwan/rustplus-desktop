using System.Windows;
using RustPlusDesk.Services.Deaths;

namespace RustPlusDesk.Views.Windows
{
    /// <summary>
    /// Death stats for a server, read from the local death log. Themed with the
    /// app's brushes; data is bound from a <see cref="DeathStatsSummary"/>.
    /// </summary>
    public partial class DeathStatsWindow : Window
    {
        private readonly string? _serverKey;

        public DeathStatsWindow(string? serverKey)
        {
            InitializeComponent();
            _serverKey = serverKey;
            Reload();
        }

        private void Reload()
        {
            DataContext = DeathLogStore.LoadForServer(_serverKey);
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "Clear the local death log for this server? This cannot be undone.",
                "Clear death log",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            DeathLogStore.Clear(_serverKey);
            Reload();
        }
    }
}
