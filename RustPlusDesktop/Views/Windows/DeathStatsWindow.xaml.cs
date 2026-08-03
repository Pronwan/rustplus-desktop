using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RustPlusDesk.Services.Deaths;

namespace RustPlusDesk.Views.Windows
{
    /// <summary>
    /// Death stats for a server, read from the local death log. Themed with the
    /// app's brushes; supports live search + location/time filtering, recomputing
    /// the whole view (stats + lists) from the filtered subset.
    /// </summary>
    public partial class DeathStatsWindow : Window
    {
        private readonly string? _serverKey;
        private List<DeathEntry> _allEntries = new();
        private bool _ready;

        public DeathStatsWindow(string? serverKey)
        {
            InitializeComponent();
            _serverKey = serverKey;
            Reload();
        }

        private void Reload()
        {
            _allEntries = DeathLogStore.LoadEntries(_serverKey);
            _ready = true;
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            if (!_ready)
                return;

            IEnumerable<DeathEntry> query = _allEntries;

            var search = SearchBox?.Text?.Trim();
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(e =>
                    e.Victim.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    e.Location.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (e.Grid ?? string.Empty).Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            var type = (TypeFilter?.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
            if (type != "all")
                query = query.Where(e => e.Type == type);

            var range = (RangeFilter?.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
            if (range == "24h" || range == "7d")
            {
                long cutoff = DateTimeOffset.UtcNow.AddDays(range == "24h" ? -1 : -7).ToUnixTimeSeconds();
                query = query.Where(e => e.DiedAt >= cutoff);
            }

            DataContext = DeathLogStore.Summarize(query.ToList());
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilters();

        private void Filter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilters();

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
