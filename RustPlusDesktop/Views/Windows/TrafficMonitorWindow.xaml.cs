using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views.Windows
{
    public partial class TrafficMonitorWindow : Window, INotifyPropertyChanged
    {
        private readonly NetworkTrafficMonitor _monitor = NetworkTrafficMonitor.Instance;
        private readonly ICollectionView _entriesView;
        private readonly DispatcherTimer _uiUpdateTimer;
        private string _searchFilterText = "";
        private bool _useMegabits = true;

        public string CurrentDownloadSpeedDisplay => _useMegabits 
            ? _monitor.FormattedDownloadSpeedBits 
            : _monitor.FormattedDownloadSpeed;

        public string CurrentDownloadSpeedSubDisplay => _useMegabits
            ? $"({_monitor.FormattedDownloadSpeed})"
            : $"({_monitor.FormattedDownloadSpeedBits})";

        public string CurrentUploadSpeedDisplay => _useMegabits
            ? _monitor.FormattedUploadSpeedBits
            : _monitor.FormattedUploadSpeed;

        public string CurrentUploadSpeedSubDisplay => _useMegabits
            ? $"({_monitor.FormattedUploadSpeed})"
            : $"({_monitor.FormattedUploadSpeedBits})";

        public string TotalInboundDisplay => _monitor.FormattedTotalInbound;
        public string TotalInboundBitsDisplay => $"({_monitor.FormattedTotalInboundBits})";

        public string TotalOutboundDisplay => _monitor.FormattedTotalOutbound;
        public string TotalOutboundBitsDisplay => $"({_monitor.FormattedTotalOutboundBits})";

        public string TotalRequestsDisplay => $"{_monitor.TotalRequestsCount:N0}";

        public bool IsFeatureEnabled
        {
            get => _monitor.IsEnabled;
            set
            {
                if (_monitor.IsEnabled != value)
                {
                    _monitor.IsEnabled = value;
                    TrackingService.TrafficMonitorEnabled = value;
                    if (value)
                    {
                        if (!_uiUpdateTimer.IsEnabled) _uiUpdateTimer.Start();
                    }
                    else
                    {
                        _uiUpdateTimer.Stop();
                    }
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFeatureEnabled)));
                    RefreshUiStats();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public TrafficMonitorWindow()
        {
            InitializeComponent();
            DataContext = this;

            _entriesView = CollectionViewSource.GetDefaultView(_monitor.Entries);
            _entriesView.Filter = FilterTrafficEntry;

            TrafficGrid.ItemsSource = _entriesView;
            CategoryBreakdownItems.ItemsSource = _monitor.CategoryBreakdown;

            _monitor.EntryAdded += Monitor_EntryAdded;

            _uiUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _uiUpdateTimer.Tick += UiUpdateTimer_Tick;
            if (_monitor.IsEnabled)
            {
                _uiUpdateTimer.Start();
            }

            Closed += TrafficMonitorWindow_Closed;
        }

        private void ToggleFeatureEnable_Checked(object sender, RoutedEventArgs e)
        {
            IsFeatureEnabled = true;
        }

        private void ToggleFeatureEnable_Unchecked(object sender, RoutedEventArgs e)
        {
            IsFeatureEnabled = false;
        }

        private void UnitRadio_Checked(object sender, RoutedEventArgs e)
        {
            _useMegabits = RbUnitBits?.IsChecked == true;
            RefreshUiStats();
        }

        private void TrafficMonitorWindow_Closed(object? sender, EventArgs e)
        {
            _uiUpdateTimer.Stop();
            _monitor.EntryAdded -= Monitor_EntryAdded;
        }

        private void UiUpdateTimer_Tick(object? sender, EventArgs e)
        {
            RefreshUiStats();
        }

        private void RefreshUiStats()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentDownloadSpeedDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentDownloadSpeedSubDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentUploadSpeedDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentUploadSpeedSubDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalInboundDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalInboundBitsDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalOutboundDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalOutboundBitsDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalRequestsDisplay)));
        }

        private void Monitor_EntryAdded(object? sender, TrafficEntry e)
        {
            if (ChkAutoScroll.IsChecked == true && TrafficGrid.Items.Count > 0)
            {
                var lastItem = TrafficGrid.Items[TrafficGrid.Items.Count - 1];
                TrafficGrid.ScrollIntoView(lastItem);
            }
        }

        private bool FilterTrafficEntry(object item)
        {
            if (item is not TrafficEntry entry) return false;

            // 1. Direction Filter
            var dirSelected = (CmbDirectionFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
            if (dirSelected.Contains("Inbound") && entry.Direction != TrafficDirection.Inbound && entry.Direction != TrafficDirection.Both)
                return false;
            if (dirSelected.Contains("Outbound") && entry.Direction != TrafficDirection.Outbound && entry.Direction != TrafficDirection.Both)
                return false;

            // 2. Category Filter
            var catSelected = (CmbCategoryFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Categories";
            if (!catSelected.StartsWith("All", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(entry.Category, catSelected, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // 3. Search Filter
            if (!string.IsNullOrWhiteSpace(_searchFilterText))
            {
                var query = _searchFilterText.Trim();
                bool matches = entry.Endpoint.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                               entry.Details.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                               entry.Status.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                               entry.Category.Contains(query, StringComparison.OrdinalIgnoreCase);
                if (!matches) return false;
            }

            return true;
        }

        private void FilterChanged(object sender, SelectionChangedEventArgs e)
        {
            _entriesView?.Refresh();
        }

        private void TxtSearchFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchFilterText = TxtSearchFilter.Text;
            _entriesView?.Refresh();
        }

        private void BtnRecordToggle_Click(object sender, RoutedEventArgs e)
        {
            _monitor.IsRecording = !_monitor.IsRecording;
            if (_monitor.IsRecording)
            {
                RecDot.Fill = new SolidColorBrush(Color.FromRgb(255, 85, 85));
                TxtRecordStatus.Text = "Pause Recording";
            }
            else
            {
                RecDot.Fill = new SolidColorBrush(Color.FromRgb(139, 148, 158));
                TxtRecordStatus.Text = "Resume Recording";
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _monitor.Clear();
            InspectorDetailsGrid.Visibility = Visibility.Collapsed;
            TxtInspectorPlaceholder.Visibility = Visibility.Visible;
            _entriesView.Refresh();
        }

        private async void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv",
                FileName = $"traffic_dump_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                Title = "Export Traffic Log to CSV"
            };

            if (sfd.ShowDialog(this) == true)
            {
                bool ok = await _monitor.ExportToCsvAsync(sfd.FileName);
                if (ok)
                {
                    MessageBox.Show(this, $"Traffic log successfully saved to:\n{sfd.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(this, "Failed to write CSV file.", "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnExportJson_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "JSON File (*.json)|*.json",
                FileName = $"traffic_dump_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                Title = "Export Traffic Log to JSON"
            };

            if (sfd.ShowDialog(this) == true)
            {
                bool ok = await _monitor.ExportToJsonAsync(sfd.FileName);
                if (ok)
                {
                    MessageBox.Show(this, $"Traffic log successfully saved to:\n{sfd.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(this, "Failed to write JSON file.", "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void TrafficGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TrafficGrid.SelectedItem is TrafficEntry entry)
            {
                TxtInspectorPlaceholder.Visibility = Visibility.Collapsed;
                InspectorDetailsGrid.Visibility = Visibility.Visible;

                InspTimestamp.Text = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} (ID: #{entry.Id})";
                InspEndpoint.Text = $"[{entry.Category}] {entry.Endpoint}";
                InspSize.Text = $"{entry.DirectionText} | Total: {entry.FormattedSize} ({NetworkTrafficMonitor.FormatBits(entry.TotalBytes)}) [In: {NetworkTrafficMonitor.FormatBytes(entry.BytesIn)}, Out: {NetworkTrafficMonitor.FormatBytes(entry.BytesOut)}]";
                InspStatus.Text = $"{entry.Status} | Latency: {(entry.DurationMs > 0 ? $"{entry.DurationMs} ms" : "N/A")}";
                InspDetails.Text = string.IsNullOrWhiteSpace(entry.Details) ? "(No additional payload metadata)" : entry.Details;
            }
            else
            {
                InspectorDetailsGrid.Visibility = Visibility.Collapsed;
                TxtInspectorPlaceholder.Visibility = Visibility.Visible;
            }
        }

        private void BtnCopyDetails_Click(object sender, RoutedEventArgs e)
        {
            if (TrafficGrid.SelectedItem is TrafficEntry entry)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"ID: #{entry.Id}");
                sb.AppendLine($"Timestamp: {entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}");
                sb.AppendLine($"Category: {entry.Category}");
                sb.AppendLine($"Direction: {entry.DirectionText}");
                sb.AppendLine($"Endpoint: {entry.Endpoint}");
                sb.AppendLine($"Size: {entry.FormattedSize} ({NetworkTrafficMonitor.FormatBits(entry.TotalBytes)}) [In: {entry.BytesIn} B, Out: {entry.BytesOut} B]");
                sb.AppendLine($"Latency: {entry.DurationMs} ms");
                sb.AppendLine($"Status: {entry.Status}");
                sb.AppendLine($"Details: {entry.Details}");

                Clipboard.SetText(sb.ToString());
            }
        }
    }
}
