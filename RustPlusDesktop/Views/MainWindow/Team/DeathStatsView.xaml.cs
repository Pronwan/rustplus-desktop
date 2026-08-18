using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RustPlusDesk.Services.Deaths;

namespace RustPlusDesk.Views;

/// <summary>
/// Death stats for a server, read from the local death log. Embedded as a full-screen
/// workspace in MainWindow (like the Raid Calculator). Themed with the app's brushes;
/// supports live search + location/time filtering, recomputing the whole view
/// (stats + lists) from the filtered subset.
/// </summary>
public partial class DeathStatsView : UserControl
{
    private string? _serverKey;
    private List<DeathEntry> _allEntries = new();
    private bool _ready;

    public DeathStatsView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? CloseRequested;

    // Called by the host each time the workspace is opened, with the current server's key.
    public void Initialize(string? serverKey)
    {
        _serverKey = serverKey;
        Reload();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, e);

    private static readonly Brush SparkBrush = MakeFrozen(Color.FromRgb(0xEF, 0x6C, 0x33));
    private static readonly Brush SparkZeroBrush = MakeFrozen(Color.FromArgb(0x55, 0x9D, 0x9D, 0x9D));

    private static Brush MakeFrozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private void Reload()
    {
        _allEntries = DeathLogStore.LoadEntries(_serverKey);
        _ready = false; // suppress filter events while the player list repopulates
        PopulatePlayerFilter();
        _ready = true;
        ApplyFilters();
    }

    // Rebuild the player dropdown from the team players in the log, keeping the
    // current selection if that player is still present.
    private void PopulatePlayerFilter()
    {
        var previous = (PlayerFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";

        PlayerFilter.Items.Clear();
        PlayerFilter.Items.Add(new ComboBoxItem { Content = "All players", Tag = "all" });
        foreach (var name in _allEntries.Select(e => e.Victim).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            PlayerFilter.Items.Add(new ComboBoxItem { Content = name, Tag = name });

        PlayerFilter.SelectedItem = PlayerFilter.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(i => (i.Tag as string) == previous) ?? PlayerFilter.Items[0];
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

        var player = (PlayerFilter?.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        if (player != "all")
            query = query.Where(e => e.Victim == player);

        var type = (TypeFilter?.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        if (type != "all")
            query = query.Where(e => e.Type == type);

        var range = (RangeFilter?.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        if (range == "24h" || range == "7d")
        {
            long cutoff = DateTimeOffset.UtcNow.AddDays(range == "24h" ? -1 : -7).ToUnixTimeSeconds();
            query = query.Where(e => e.DiedAt >= cutoff);
        }

        var summary = DeathLogStore.Summarize(query.ToList());
        DataContext = summary;
        BuildSparkline(summary.DeathsPerDay);
    }

    // Tiny bar sparkline of deaths per day (bars grow from the bottom).
    private void BuildSparkline(IReadOnlyList<DayCount> days)
    {
        SparklineHost.Children.Clear();
        if (days.Count == 0)
            return;

        int max = Math.Max(1, days.Max(d => d.Count));
        const double fullHeight = 42.0;

        foreach (var d in days)
        {
            double barHeight = d.Count == 0 ? 2.0 : Math.Max(4.0, d.Count / (double)max * fullHeight);
            SparklineHost.Children.Add(new Border
            {
                Width = 10,
                Height = barHeight,
                Background = d.Count == 0 ? SparkZeroBrush : SparkBrush,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(2, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                ToolTip = $"{d.Day:ddd, MMM d}: {d.Count} death(s)",
            });
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilters();

    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilters();

    // Re-read the log so deaths recorded since the view opened show up.
    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => Reload();

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
