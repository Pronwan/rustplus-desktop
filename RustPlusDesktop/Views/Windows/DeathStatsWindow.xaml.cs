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
        public DeathStatsWindow(string? serverKey)
        {
            InitializeComponent();
            DataContext = DeathLogStore.LoadForServer(serverKey);
        }
    }
}
