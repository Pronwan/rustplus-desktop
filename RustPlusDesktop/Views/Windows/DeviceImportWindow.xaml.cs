using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views
{
    public partial class DeviceImportWindow : WpfUi.FluentWindow, INotifyPropertyChanged
    {
        public ObservableCollection<DeviceImportItem> Devices { get; } = new();
        private readonly Func<uint, Task<EntityProbeResult>> _probe;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnProp([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private string _sourceDescription = "";
        public string SourceDescription
        {
            get => _sourceDescription;
            set { if (_sourceDescription != value) { _sourceDescription = value; OnProp(); } }
        }

        private string _summaryText = "";
        public string SummaryText
        {
            get => _summaryText;
            set { if (_summaryText != value) { _summaryText = value; OnProp(); } }
        }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnProp();
                    var view = CollectionViewSource.GetDefaultView(Devices);
                    view.Refresh();
                    HasVisibleDevices = view.Cast<object>().Any();
                }
            }
        }

        private bool _isProbing;
        public bool IsProbing
        {
            get => _isProbing;
            set 
            { 
                if (_isProbing != value) 
                { 
                    _isProbing = value; 
                    OnProp(); 
                    OnProp(nameof(IsNotProbing)); 
                    OnProp(nameof(CanImport)); 
                } 
            }
        }
        public bool IsNotProbing => !IsProbing;

        private int _probingProgress;
        public int ProbingProgress
        {
            get => _probingProgress;
            set { if (_probingProgress != value) { _probingProgress = value; OnProp(); OnProp(nameof(ProbingProgressText)); } }
        }

        private int _probingMax = 100;
        public int ProbingMax
        {
            get => _probingMax;
            set { if (_probingMax != value) { _probingMax = value; OnProp(); OnProp(nameof(ProbingProgressText)); } }
        }

        private string _probingStatus = "";
        public string ProbingStatus
        {
            get => _probingStatus;
            set { if (_probingStatus != value) { _probingStatus = value; OnProp(); } }
        }

        public string ProbingProgressText => $"{ProbingProgress} / {ProbingMax}";

        private bool _hasVisibleDevices = true;
        public bool HasVisibleDevices
        {
            get => _hasVisibleDevices;
            set { if (_hasVisibleDevices != value) { _hasVisibleDevices = value; OnProp(); OnProp(nameof(NoDevicesVisible)); } }
        }
        public bool NoDevicesVisible => !HasVisibleDevices;

        public bool CanImport => SelectedItems.Count > 0 && !IsProbing;

        public DeviceImportWindow(
            List<DeviceImportItem> devices,
            Func<uint, Task<EntityProbeResult>> probe)
        {
            InitializeComponent();

            try
            {
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(this);
            }
            catch { }

            // Copy list to ObservableCollection and subscribe to changes
            Devices.Clear();
            foreach (var d in devices)
            {
                Devices.Add(d);
                d.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(DeviceImportItem.IsSelected) || e.PropertyName == nameof(DeviceImportItem.ExistsState))
                    {
                        UpdateSummaryText();
                        OnProp(nameof(CanImport));
                        OnProp(nameof(SelectedItems));
                    }
                };
            }

            _probe = probe;

            // Generate source description dynamically
            var serverName = Devices.Select(d => d.ServerName).FirstOrDefault(s => !string.IsNullOrEmpty(s)) ?? "Current Server";
            var owners = Devices.Select(d => d.OwnerName).Where(o => !string.IsNullOrEmpty(o)).Distinct().ToList();
            SourceDescription = $"Source: Cloud sync / local backups for " + (owners.Count > 0 ? string.Join(", ", owners) : "teammates") + $" on {serverName}";

            // Setup collection view filter
            var view = CollectionViewSource.GetDefaultView(Devices);
            view.Filter = item =>
            {
                if (item is not DeviceImportItem d) return false;
                if (string.IsNullOrWhiteSpace(SearchText)) return true;
                var search = SearchText.Trim().ToLowerInvariant();
                return (d.DisplayName != null && d.DisplayName.ToLowerInvariant().Contains(search))
                    || (d.OwnerName != null && d.OwnerName.ToLowerInvariant().Contains(search))
                    || (d.Kind != null && d.Kind.ToLowerInvariant().Contains(search))
                    || (d.EntityId.ToString().Contains(search));
            };

            UpdateSummaryText();
            DataContext = this;
        }

        private void UpdateSummaryText()
        {
            int total = Devices.Count;
            int present = Devices.Count(d => d.AlreadyPresent);
            int selected = Devices.Count(d => d.IsSelected && !d.AlreadyPresent);
            SummaryText = $"{total} devices found • {present} already present • {selected} selected for import";
        }

        private async void BtnCheckStatus_Click(object sender, RoutedEventArgs e)
        {
            if (IsProbing) return;

            try
            {
                // 1) Group all devices by EntityId to avoid duplicate probes
                var groupsToCheck = Devices
                    .GroupBy(d => d.EntityId)
                    .ToList();

                // Cache for results
                var cache = new Dictionary<uint, EntityProbeResult>();
                int probedCount = 0;

                IsProbing = true;
                ProbingMax = groupsToCheck.Count;
                ProbingProgress = 0;

                foreach (var group in groupsToCheck)
                {
                    var id = group.Key;
                    
                    var firstItem = group.FirstOrDefault();
                    var deviceName = firstItem != null 
                        ? (!string.IsNullOrEmpty(firstItem.Alias) ? firstItem.Alias : firstItem.Name) 
                        : id.ToString();
                    ProbingStatus = $"Probing device #{id} ({deviceName ?? "Unknown"})...";

                    EntityProbeResult result;

                    if (!cache.TryGetValue(id, out result))
                    {
                        try
                        {
                            result = await _probe(id);
                        }
                        catch
                        {
                            result = new EntityProbeResult(false, null, null);
                        }

                        cache[id] = result;
                        probedCount++;

                        await Task.Delay(80);

                        if (probedCount % 5 == 0)
                            await Task.Delay(250);
                    }

                    var state = result.Exists ? "ok" : "missing";

                    foreach (var item in group)
                    {
                        item.ExistsState = state;
                    }

                    ProbingProgress++;
                }
            }
            finally
            {
                IsProbing = false;
                UpdateSummaryText();
                OnProp(nameof(CanImport));
                OnProp(nameof(SelectedItems));
            }
        }

        public IReadOnlyList<DeviceImportItem> SelectedItems =>
            Devices.Where(d => d.IsSelected && !d.AlreadyPresent).ToList();

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var it in Devices)
            {
                if (!it.AlreadyPresent)
                    it.IsSelected = true;
            }
            UpdateSummaryText();
            OnProp(nameof(CanImport));
            OnProp(nameof(SelectedItems));
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var it in Devices)
                it.IsSelected = false;
            UpdateSummaryText();
            OnProp(nameof(CanImport));
            OnProp(nameof(SelectedItems));
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            if (!SelectedItems.Any())
            {
                DialogResult = false;
                return;
            }

            DialogResult = true;
        }
    }
}
