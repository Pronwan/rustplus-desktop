using RustPlusDesk.Models;
using RustPlusDesk.Services;
using RustPlusDesk.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace RustPlusDesk.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<ServerProfile> Servers { get; } = new();

    private bool _isBusy;
    private bool _isPairingRunning;
    public bool IsPairingRunning
    {
        get => _isPairingRunning;
        set { _isPairingRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStartPairing)); }
    }

    public bool CanStartPairing => !_isPairingRunning && !IsBusy;

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    private string _busyText = "Bitte warten …";
    public string BusyText
    {
        get => _busyText;
        set { _busyText = value; OnPropertyChanged(); }
    }

    private string _steamId64 = "";
    public string SteamId64
    {
        get => _steamId64;
        set { _steamId64 = value; OnPropertyChanged(); }
    }

    private ServerProfile? _selected;
    public ServerProfile? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value; 
            OnPropertyChanged();                   // "Selected"
            OnPropertyChanged(nameof(CurrentDevices));
        }
    }

    public sealed class StorageSnapshot
    {
        public bool IsToolCupboard { get; init; }
        public int? UpkeepSeconds { get; init; }        // nur TC
        public DateTime SnapshotUtc { get; init; } = DateTime.UtcNow;
        public List<StorageItemVM> Items { get; init; } = new();
    }

    public sealed class StorageItemVM : INotifyPropertyChanged
    {
        public int ItemId { get; init; }
        public string? ShortName { get; init; }
        public int Amount { get; init; }
        public int? MaxStack { get; init; }

        public string Display => MainWindow.ResolveItemName(ItemId, ShortName);
        public ImageSource? Icon => MainWindow.ResolveItemIcon(ItemId, ShortName, 32);
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private string _serverPlayers = "-/-";
    public string ServerPlayers { get => _serverPlayers; set { _serverPlayers = value; OnPropertyChanged(); } }

    private string _serverQueue = "-";
    public string ServerQueue { get => _serverQueue; set { _serverQueue = value; OnPropertyChanged(); } }

    private string _serverTime = "-";
    public string ServerTime { get => _serverTime; set { _serverTime = value; OnPropertyChanged(); } }

    private string _serverWipe = "-";
    public string ServerWipe { get => _serverWipe; set { _serverWipe = value; OnPropertyChanged(); } }

    // NEU: Abgeleitete Binding-Quelle für die Liste
    public ObservableCollection<SmartDevice>? CurrentDevices
        => Selected?.Devices;

    // Auswahl im UI
    private SmartDevice? _selectedDevice;
    public SmartDevice? SelectedDevice
    {
        get => _selectedDevice;
        set { _selectedDevice = value; OnPropertyChanged(); }
    }

    public void AddServer(ServerProfile p) => Servers.Add(p);

    public void Load()
    {
        Servers.Clear();
        foreach (var p in StorageService.LoadProfiles())
        {
            p.Devices ??= new ObservableCollection<SmartDevice>(); // niemals null
            p.CameraIds ??= new ObservableCollection<string>();      // NEU: ebenso niemals null
            Servers.Add(p);
        }

        // WICHTIG: Vorauswahl, sonst bleibt CurrentDevices=null
        if (Servers.Count > 0 && Selected == null)
            Selected = Servers[0];
    }


    public void NotifyCamerasChanged() => OnPropertyChanged(nameof(Selected));
    public void Save() => StorageService.SaveProfiles(Servers);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // HILFSMETHODE: UI anstupsen, wenn Devices in-place aktualisiert wurden
    public void NotifyDevicesChanged()
        => OnPropertyChanged(nameof(CurrentDevices));
}
