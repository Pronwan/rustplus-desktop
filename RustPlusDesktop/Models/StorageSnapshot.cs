using RustPlusDesk.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace RustPlusDesk.Models
{
    public sealed class StorageSnapshot : INotifyPropertyChanged
    {
        public ObservableCollection<StorageItemVM> Items { get; } = new();

        public StorageSnapshot()
        {
            Items.CollectionChanged += (_, __) => OnProp(nameof(ItemsCount));
        }

        public int ItemsCount => Items?.Count ?? 0;

        private int? _upkeepSeconds;
        public int? UpkeepSeconds
        {
            get => _upkeepSeconds;
            set { if (_upkeepSeconds != value) { _upkeepSeconds = value; OnProp(); } }
        }

        private bool _isToolCupboard;
        public bool IsToolCupboard
        {
            get => _isToolCupboard;
            set { if (_isToolCupboard != value) { _isToolCupboard = value; OnProp(); } }
        }

        private DateTime _snapshotUtc = DateTime.UtcNow;
        public DateTime SnapshotUtc
        {
            get => _snapshotUtc;
            set { if (_snapshotUtc != value) { _snapshotUtc = value; OnProp(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnProp(string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class StorageItemVM : INotifyPropertyChanged
    {
        public int ItemId { get; init; }
        public string? ShortName { get; init; }
        public int Amount { get; init; }
        public int? MaxStack { get; init; }

        // Diese zwei Properties füttern das UI; Auflösung übernimmt MainWindow (siehe Punkt 2)
        public string Display => MainWindow.ResolveItemName(ItemId, ShortName);
        [JsonIgnore]
        public System.Windows.Media.ImageSource? Icon => MainWindow.ResolveItemIcon(ItemId, ShortName, 32);

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
