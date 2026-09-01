using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace RustPlusDesk.Services
{
    public enum TrafficDirection
    {
        Inbound,
        Outbound,
        Both
    }

    public class TrafficEntry : INotifyPropertyChanged
    {
        public long Id { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public string TimestampFormatted => Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        public string Category { get; init; } = "General";
        public TrafficDirection Direction { get; init; } = TrafficDirection.Inbound;
        
        public string DirectionText => Direction switch
        {
            TrafficDirection.Inbound => "↓ IN",
            TrafficDirection.Outbound => "↑ OUT",
            _ => "⇅"
        };

        public string Endpoint { get; init; } = "";
        public long BytesIn { get; init; }
        public long BytesOut { get; init; }
        public long TotalBytes => BytesIn + BytesOut;
        public string FormattedSize => NetworkTrafficMonitor.FormatBytes(TotalBytes);
        public long DurationMs { get; init; }
        public string Status { get; init; } = "OK";
        public string Details { get; init; } = "";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class CategoryBandwidthStat : INotifyPropertyChanged
    {
        public string Category { get; set; } = "";
        public long TotalBytesIn { get; set; }
        public long TotalBytesOut { get; set; }
        public long TotalBytes => TotalBytesIn + TotalBytesOut;
        public string FormattedTotalBytes => NetworkTrafficMonitor.FormatBytes(TotalBytes);
        public string FormattedBytesIn => NetworkTrafficMonitor.FormatBytes(TotalBytesIn);
        public string FormattedBytesOut => NetworkTrafficMonitor.FormatBytes(TotalBytesOut);
        public long RequestCount { get; set; }
        public double PercentageOfTotal { get; set; }
        public string FormattedPercentage => $"{PercentageOfTotal:F1}%";

        public event PropertyChangedEventHandler? PropertyChanged;
        public void NotifyChanges() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public sealed class NetworkTrafficMonitor : INotifyPropertyChanged
    {
        private static readonly Lazy<NetworkTrafficMonitor> _instance = new(() => new NetworkTrafficMonitor());
        public static NetworkTrafficMonitor Instance => _instance.Value;

        private const int MaxLogEntries = 2500;
        private readonly object _lock = new();
        private long _nextId = 1;

        // Cumulative Totals
        private long _totalInboundBytes;
        private long _totalOutboundBytes;
        private long _totalRequestsCount;

        // Rolling Speed Meter (last 1-2 seconds)
        private readonly ConcurrentQueue<(DateTime Time, long InBytes, long OutBytes)> _speedHistory = new();
        private double _currentDownloadSpeedBps;
        private double _currentUploadSpeedBps;
        private readonly DispatcherTimer _speedTimer;

        // Category Aggregations
        private readonly Dictionary<string, CategoryBandwidthStat> _categoryStats = new(StringComparer.OrdinalIgnoreCase);

        // UI Observable Collections
        public ObservableCollection<TrafficEntry> Entries { get; } = new();
        public ObservableCollection<CategoryBandwidthStat> CategoryBreakdown { get; } = new();

        public bool IsRecording { get; set; } = true;

        public long TotalInboundBytes
        {
            get => Interlocked.Read(ref _totalInboundBytes);
            private set => SetField(ref _totalInboundBytes, value);
        }

        public long TotalOutboundBytes
        {
            get => Interlocked.Read(ref _totalOutboundBytes);
            private set => SetField(ref _totalOutboundBytes, value);
        }

        public long TotalBytes => TotalInboundBytes + TotalOutboundBytes;

        public long TotalRequestsCount
        {
            get => Interlocked.Read(ref _totalRequestsCount);
            private set => SetField(ref _totalRequestsCount, value);
        }

        public double CurrentDownloadSpeedBps
        {
            get => _currentDownloadSpeedBps;
            private set
            {
                if (Math.Abs(_currentDownloadSpeedBps - value) > 0.1)
                {
                    _currentDownloadSpeedBps = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FormattedDownloadSpeed));
                    OnPropertyChanged(nameof(BandwidthSummaryBadge));
                }
            }
        }

        public double CurrentUploadSpeedBps
        {
            get => _currentUploadSpeedBps;
            private set
            {
                if (Math.Abs(_currentUploadSpeedBps - value) > 0.1)
                {
                    _currentUploadSpeedBps = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FormattedUploadSpeed));
                    OnPropertyChanged(nameof(BandwidthSummaryBadge));
                }
            }
        }

        public string FormattedTotalInbound => FormatBytes(TotalInboundBytes);
        public string FormattedTotalOutbound => FormatBytes(TotalOutboundBytes);
        public string FormattedTotalBytes => FormatBytes(TotalBytes);
        public string FormattedDownloadSpeed => FormatBytes((long)_currentDownloadSpeedBps) + "/s";
        public string FormattedUploadSpeed => FormatBytes((long)_currentUploadSpeedBps) + "/s";

        public string FormattedDownloadSpeedBits => FormatBits((long)_currentDownloadSpeedBps);
        public string FormattedUploadSpeedBits => FormatBits((long)_currentUploadSpeedBps);
        public string FormattedDownloadSpeedCombined => $"{FormatBits((long)_currentDownloadSpeedBps)} ({FormatBytes((long)_currentDownloadSpeedBps)}/s)";
        public string FormattedUploadSpeedCombined => $"{FormatBits((long)_currentUploadSpeedBps)} ({FormatBytes((long)_currentUploadSpeedBps)}/s)";

        public string FormattedTotalInboundBits => FormatBits(TotalInboundBytes);
        public string FormattedTotalOutboundBits => FormatBits(TotalOutboundBytes);

        public string BandwidthSummaryBadge => $"↓ {FormatBits((long)_currentDownloadSpeedBps)}  ↑ {FormatBits((long)_currentUploadSpeedBps)}";

        public event EventHandler<TrafficEntry>? EntryAdded;
        public event PropertyChangedEventHandler? PropertyChanged;

        private NetworkTrafficMonitor()
        {
            _speedTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _speedTimer.Tick += SpeedTimer_Tick;
            _speedTimer.Start();
        }

        private void SpeedTimer_Tick(object? sender, EventArgs e)
        {
            var now = DateTime.UtcNow;
            var windowStart = now.AddSeconds(-1.5);

            while (_speedHistory.TryPeek(out var item) && item.Time < windowStart)
            {
                _speedHistory.TryDequeue(out _);
            }

            var snapshot = _speedHistory.ToArray();
            long totalIn = 0;
            long totalOut = 0;

            foreach (var s in snapshot)
            {
                totalIn += s.InBytes;
                totalOut += s.OutBytes;
            }

            double seconds = 1.5;
            CurrentDownloadSpeedBps = totalIn / seconds;
            CurrentUploadSpeedBps = totalOut / seconds;
        }

        public void Record(
            string category,
            TrafficDirection direction,
            string endpoint,
            long bytesIn,
            long bytesOut,
            long durationMs = 0,
            string status = "OK",
            string details = "")
        {
            if (!IsRecording) return;

            var now = DateTime.UtcNow;
            _speedHistory.Enqueue((now, bytesIn, bytesOut));

            Interlocked.Add(ref _totalInboundBytes, bytesIn);
            Interlocked.Add(ref _totalOutboundBytes, bytesOut);
            Interlocked.Increment(ref _totalRequestsCount);

            var entry = new TrafficEntry
            {
                Id = Interlocked.Increment(ref _nextId),
                Timestamp = DateTime.Now,
                Category = string.IsNullOrWhiteSpace(category) ? "General" : category,
                Direction = direction,
                Endpoint = endpoint,
                BytesIn = Math.Max(0, bytesIn),
                BytesOut = Math.Max(0, bytesOut),
                DurationMs = durationMs,
                Status = status,
                Details = details
            };

            lock (_lock)
            {
                if (!_categoryStats.TryGetValue(entry.Category, out var catStat))
                {
                    catStat = new CategoryBandwidthStat { Category = entry.Category };
                    _categoryStats[entry.Category] = catStat;
                }

                catStat.TotalBytesIn += entry.BytesIn;
                catStat.TotalBytesOut += entry.BytesOut;
                catStat.RequestCount++;
            }

            // Dispatch to UI
            var app = Application.Current;
            if (app != null && app.Dispatcher != null && !app.Dispatcher.HasShutdownStarted)
            {
                app.Dispatcher.BeginInvoke(() =>
                {
                    lock (_lock)
                    {
                        if (Entries.Count >= MaxLogEntries)
                        {
                            Entries.RemoveAt(0);
                        }
                        Entries.Add(entry);
                        RefreshCategoryBreakdownUi();
                    }

                    OnPropertyChanged(nameof(TotalInboundBytes));
                    OnPropertyChanged(nameof(TotalOutboundBytes));
                    OnPropertyChanged(nameof(TotalBytes));
                    OnPropertyChanged(nameof(TotalRequestsCount));
                    OnPropertyChanged(nameof(FormattedTotalInbound));
                    OnPropertyChanged(nameof(FormattedTotalOutbound));
                    OnPropertyChanged(nameof(FormattedTotalBytes));

                    EntryAdded?.Invoke(this, entry);
                }, DispatcherPriority.Background);
            }
        }

        public void RecordInbound(string category, string endpoint, long bytesIn, long durationMs = 0, string status = "OK", string details = "")
        {
            Record(category, TrafficDirection.Inbound, endpoint, bytesIn, 0, durationMs, status, details);
        }

        public void RecordOutbound(string category, string endpoint, long bytesOut, long durationMs = 0, string status = "Sent", string details = "")
        {
            Record(category, TrafficDirection.Outbound, endpoint, 0, bytesOut, durationMs, status, details);
        }

        public void Clear()
        {
            lock (_lock)
            {
                _categoryStats.Clear();
                CategoryBreakdown.Clear();
                Entries.Clear();
            }

            Interlocked.Exchange(ref _totalInboundBytes, 0);
            Interlocked.Exchange(ref _totalOutboundBytes, 0);
            Interlocked.Exchange(ref _totalRequestsCount, 0);

            while (_speedHistory.TryDequeue(out _)) { }
            CurrentDownloadSpeedBps = 0;
            CurrentUploadSpeedBps = 0;

            OnPropertyChanged(nameof(TotalInboundBytes));
            OnPropertyChanged(nameof(TotalOutboundBytes));
            OnPropertyChanged(nameof(TotalBytes));
            OnPropertyChanged(nameof(TotalRequestsCount));
            OnPropertyChanged(nameof(FormattedTotalInbound));
            OnPropertyChanged(nameof(FormattedTotalOutbound));
            OnPropertyChanged(nameof(FormattedTotalBytes));
            OnPropertyChanged(nameof(FormattedDownloadSpeed));
            OnPropertyChanged(nameof(FormattedUploadSpeed));
            OnPropertyChanged(nameof(BandwidthSummaryBadge));
        }

        private void RefreshCategoryBreakdownUi()
        {
            long grandTotal = TotalBytes;
            if (grandTotal <= 0) grandTotal = 1;

            foreach (var kvp in _categoryStats)
            {
                var stat = kvp.Value;
                stat.PercentageOfTotal = (double)stat.TotalBytes / grandTotal * 100.0;
                stat.NotifyChanges();

                if (!CategoryBreakdown.Contains(stat))
                {
                    CategoryBreakdown.Add(stat);
                }
            }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double val = bytes;
            int unitIdx = 0;

            while (val >= 1024 && unitIdx < units.Length - 1)
            {
                val /= 1024;
                unitIdx++;
            }

            if (unitIdx == 0) return $"{bytes} B";
            return $"{val:0.#} {units[unitIdx]}";
        }

        public static string FormatBits(long bytes)
        {
            if (bytes <= 0) return "0 bps";
            double bits = bytes * 8.0;
            string[] units = { "bps", "Kbps", "Mbps", "Gbps", "Tbps" };
            int unitIdx = 0;

            while (bits >= 1000.0 && unitIdx < units.Length - 1)
            {
                bits /= 1000.0;
                unitIdx++;
            }

            if (unitIdx == 0) return $"{bits:0} bps";
            return $"{bits:0.##} {units[unitIdx]}";
        }

        public async Task<bool> ExportToCsvAsync(string filePath)
        {
            try
            {
                TrafficEntry[] snapshot;
                lock (_lock)
                {
                    snapshot = Entries.ToArray();
                }

                var sb = new StringBuilder();
                sb.AppendLine("ID,Timestamp,Category,Direction,Endpoint,BytesIn,BytesOut,TotalBytes,DurationMs,Status,Details");

                foreach (var item in snapshot)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},\"{1}\",\"{2}\",\"{3}\",\"{4}\",{5},{6},{7},{8},\"{9}\",\"{10}\"",
                        item.Id,
                        item.TimestampFormatted,
                        EscapeCsv(item.Category),
                        item.DirectionText,
                        EscapeCsv(item.Endpoint),
                        item.BytesIn,
                        item.BytesOut,
                        item.TotalBytes,
                        item.DurationMs,
                        EscapeCsv(item.Status),
                        EscapeCsv(item.Details)));
                }

                await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> ExportToJsonAsync(string filePath)
        {
            try
            {
                TrafficEntry[] snapshot;
                lock (_lock)
                {
                    snapshot = Entries.ToArray();
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(snapshot, options);
                await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string EscapeCsv(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\"", "\"\"");
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                OnPropertyChanged(propertyName);
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
