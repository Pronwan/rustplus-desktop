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
    private readonly System.Windows.Threading.DispatcherTimer _clockTimer;
    private ImageSource? _myAvatar;

    public ImageSource? MyAvatar
    {
        get => _myAvatar;
        set { _myAvatar = value; OnPropertyChanged(); }
    }

    public MainViewModel()
    {
        _clockTimer = new System.Windows.Threading.DispatcherTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (s, e) => TickClock();
        _clockTimer.Start();

        App.CultureChanged += () =>
        {
            OnPropertyChanged(nameof(BusyText));
            OnPropertyChanged(nameof(FcmExpiryText));
            if (_lastStatusGameTime.HasValue)
            {
                UpdateDisplayProperties(_lastStatusGameTime.Value);
            }
        };
    }

    private void TickClock()
    {
        if (_lastStatusRealTime.HasValue && _lastStatusGameTime.HasValue)
        {
            var now = DateTime.UtcNow;
            
            // Timeout: if no server update for > 2 mins, reset display
            if ((now - _lastStatusRealTime.Value).TotalMinutes > 2.0)
            {
                ServerTime = "–"; // This will clear baseline via UpdateInGameTimeProperties
                return;
            }

            double elapsedRealMins = (now - _lastStatusRealTime.Value).TotalMinutes;
            double currentHours = _lastStatusGameTime.Value;
            
            // Use observed speed to extrapolate
            double speed = (currentHours >= 8 && currentHours < 20) ? _observedDaySpeed : _observedNightSpeed;
            double extrapolatedHours = (currentHours + (elapsedRealMins * speed)) % 24;

            // Update display properties without triggering re-learning
            UpdateDisplayProperties(extrapolatedHours);
        }
    }

    private void UpdateDisplayProperties(double hours)
    {
        int h = (int)Math.Floor(hours);
        int m = (int)Math.Floor((hours - h) * 60);
        string newTime = $"{h:00}:{m:00}";

        // Update ServerTime string directly if it changed
        if (_serverTime != newTime && _serverTime != "-" && _serverTime != "–")
        {
            _serverTime = newTime;
            OnPropertyChanged(nameof(ServerTime));
        }

        // Update countdown
        if (hours >= 8 && hours < 20)
        {
            IsDay = true;
            double remainingGameHours = 20 - hours;
            double remainingRealMins = remainingGameHours / _observedDaySpeed;
            TimeUntilNextPhase = string.Format(Properties.Resources.UntilNight, FormatDuration(remainingRealMins / 60.0));
        }
        else
        {
            IsDay = false;
            double remainingGameHours;
            if (hours >= 20) remainingGameHours = (24 - hours) + 8;
            else remainingGameHours = 8 - hours;
            
            double remainingRealMins = remainingGameHours / _observedNightSpeed;
            TimeUntilNextPhase = string.Format(Properties.Resources.UntilDay, FormatDuration(remainingRealMins / 60.0));
        }
    }
    private int _iconsTotal;
    private int _iconsDownloaded;

    public int IconsTotal
    {
        get => _iconsTotal;
        set { _iconsTotal = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDownloadingIcons)); OnPropertyChanged(nameof(IconDownloadProgress)); }
    }

    public int IconsDownloaded
    {
        get => _iconsDownloaded;
        set { _iconsDownloaded = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDownloadingIcons)); OnPropertyChanged(nameof(IconDownloadProgress)); }
    }

    public bool IsDownloadingIcons => _iconsTotal > 0 && _iconsDownloaded < _iconsTotal;
    public double IconDownloadProgress => _iconsTotal > 0 ? (double)_iconsDownloaded / _iconsTotal * 100 : 0;

    public ObservableCollection<ServerProfile> Servers { get; } = new();

    private bool _isBusy;
    private bool _isPairingBusy;
    private bool _isPairingRunning;

    public bool IsPairingRunning
    {
        get => _isPairingRunning;
        set { _isPairingRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStartPairing)); }
    }

    public bool CanStartPairing => !_isPairingRunning && !IsBusy && !IsPairingBusy;

    private bool _isTrackingActive;
    public bool IsTrackingActive
    {
        get => _isTrackingActive;
        set { _isTrackingActive = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStartPairing)); OnPropertyChanged(nameof(ShowLoginOverlay)); }
    }

    private bool _isInitializing;
    public bool IsInitializing
    {
        get => _isInitializing;
        set { _isInitializing = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowLoginOverlay)); }
    }

    public bool IsPairingBusy
    {
        get => _isPairingBusy;
        set { _isPairingBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStartPairing)); }
    }

    private bool _isUpdateAvailable;
    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        set { _isUpdateAvailable = value; OnPropertyChanged(); }
    }

    private string _updateTag = "";
    public string UpdateTag
    {
        get => _updateTag;
        set { _updateTag = value; OnPropertyChanged(); }
    }

    private bool _isDownloadingUpdate;
    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        set { _isDownloadingUpdate = value; OnPropertyChanged(); }
    }

    private double _updateDownloadProgress;
    public double UpdateDownloadProgress
    {
        get => _updateDownloadProgress;
        set { _updateDownloadProgress = value; OnPropertyChanged(); }
    }

    private string _updateDownloadSpeed = "";
    public string UpdateDownloadSpeed
    {
        get => _updateDownloadSpeed;
        set { _updateDownloadSpeed = value; OnPropertyChanged(); }
    }

    private string _updateDownloadSize = "";
    public string UpdateDownloadSize
    {
        get => _updateDownloadSize;
        set { _updateDownloadSize = value; OnPropertyChanged(); }
    }

    private string _updateDownloadPercentage = "0%";
    public string UpdateDownloadPercentage
    {
        get => _updateDownloadPercentage;
        set { _updateDownloadPercentage = value; OnPropertyChanged(); }
    }

    private string _busyText = "";
    public string BusyText
    {
        get => string.IsNullOrEmpty(_busyText) ? Properties.Resources.PleaseWait : _busyText;
        set { _busyText = value; OnPropertyChanged(); }
    }

    private string _steamId64 = "";
    public string SteamId64
    {
        get => _steamId64;
        set { _steamId64 = value; OnPropertyChanged(); }
    }

    public string FcmExpiryText
    {
        get
        {
            if (TrackingService.FcmExpiresAt == null) return Properties.Resources.NoTokenRegistered;
            var remaining = TrackingService.FcmExpiresAt.Value - DateTime.Now;
            if (remaining.TotalDays < 0) return Properties.Resources.TokenExpired;
            return string.Format(Properties.Resources.ExpiresInDays, (int)remaining.TotalDays);
        }
    }

    public int FcmExpiryDays
    {
        get
        {
            if (TrackingService.FcmExpiresAt == null) return -1;
            return (int)(TrackingService.FcmExpiresAt.Value - DateTime.Now).TotalDays;
        }
    }

    public void NotifyFcmChanged()
    {
        OnPropertyChanged(nameof(FcmExpiryText));
        OnPropertyChanged(nameof(FcmExpiryDays));
        OnPropertyChanged(nameof(ShowLoginOverlay));
    }

    public bool ShowLoginOverlay
    {
        get
        {
            // Show overlay if no token registered and not currently busy/initializing
            // Show overlay if no valid token exists
            bool hasToken = TrackingService.IsFcmConfigured() &&
                            (!TrackingService.FcmExpiresAt.HasValue || TrackingService.FcmExpiresAt.Value >= DateTime.Now);
            
            // If they have servers loaded, hide the login overlay so they can access their restored profiles!
            if (Servers.Count > 0) return false;

            return !hasToken && !IsBusy && !IsInitializing;
        }
    }
    private ulong? _followingSteamId;
    public ulong? FollowingSteamId
    {
        get => _followingSteamId;
        set 
        { 
            _followingSteamId = value; 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(IsFollowing));
        }
    }

    public bool IsFollowing => _followingSteamId.HasValue;

    private string _followingPlayerName = "";
    public string FollowingPlayerName
    {
        get => _followingPlayerName;
        set { _followingPlayerName = value; OnPropertyChanged(); }
    }

    private ImageSource? _followingPlayerAvatar;
    public ImageSource? FollowingPlayerAvatar
    {
        get => _followingPlayerAvatar;
        set { _followingPlayerAvatar = value; OnPropertyChanged(); }
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
    public string ServerTime 
    { 
        get => _serverTime; 
        set 
        { 
            _serverTime = value; 
            OnPropertyChanged();
            UpdateInGameTimeProperties(value);
        } 
    }

    private bool _isDay;
    public bool IsDay { get => _isDay; set { _isDay = value; OnPropertyChanged(); } }

    private string _timeUntilNextPhase = "";
    public string TimeUntilNextPhase { get => _timeUntilNextPhase; set { _timeUntilNextPhase = value; OnPropertyChanged(); } }

    private DateTime? _lastStatusRealTime;
    private double? _lastStatusGameTime;
    private string? _lastConnectedServer;

    // Speeds in game-hours per real-minute. 
    // Defaults for Rust (Day ~50m real, Night ~10m real)
    private double _observedDaySpeed = 12.0 / 50.0;   
    private double _observedNightSpeed = 12.0 / 10.0; 

    private void UpdateInGameTimeProperties(string timeStr)
    {
        if (string.IsNullOrWhiteSpace(timeStr) || timeStr == "-" || timeStr == "–")
        {
            TimeUntilNextPhase = "";
            _lastStatusRealTime = null;
            _lastStatusGameTime = null;
            return;
        }

        // Reset learning if server changed
        string currentServer = Selected?.Host ?? "";
        if (currentServer != _lastConnectedServer)
        {
            _lastConnectedServer = currentServer;
            _lastStatusRealTime = null;
            _lastStatusGameTime = null;

            if (Selected != null)
            {
                _observedDaySpeed = Selected.LearnedDaySpeed > 0 ? Selected.LearnedDaySpeed : (12.0 / 50.0);
                _observedNightSpeed = Selected.LearnedNightSpeed > 0 ? Selected.LearnedNightSpeed : (12.0 / 10.0);
            }
            else
            {
                _observedDaySpeed = 12.0 / 50.0;
                _observedNightSpeed = 12.0 / 10.0;
            }
        }

        try
        {
            if (TimeSpan.TryParse(timeStr, out var ts))
            {
                double currentHours = ts.TotalHours;
                DateTime now = DateTime.UtcNow;

                if (_lastStatusRealTime.HasValue && _lastStatusGameTime.HasValue)
                {
                    double deltaRealMins = (now - _lastStatusRealTime.Value).TotalMinutes;
                    if (deltaRealMins > 0.05) // update every ~3 seconds is normal
                    {
                        double deltaGameHours = currentHours - _lastStatusGameTime.Value;
                        if (deltaGameHours < -12) deltaGameHours += 24; // midnight wrap

                        // Only learn if the change is positive and reasonable (avoid manual time sets)
                        if (deltaGameHours > 0 && deltaGameHours < 2) 
                        {
                            double speed = deltaGameHours / deltaRealMins;
                            
                            // Smooth the observation (exponential moving average)
                            if (currentHours >= 8 && currentHours < 20)
                            {
                                _observedDaySpeed = (_observedDaySpeed * 0.95) + (speed * 0.05);
                                if (Selected != null) Selected.LearnedDaySpeed = _observedDaySpeed;
                            }
                            else
                            {
                                _observedNightSpeed = (_observedNightSpeed * 0.95) + (speed * 0.05);
                                if (Selected != null) Selected.LearnedNightSpeed = _observedNightSpeed;
                            }
                        }
                    }
                }

                _lastStatusRealTime = now;
                _lastStatusGameTime = currentHours;

                // Immediately update display
                UpdateDisplayProperties(currentHours);
            }
        }
        catch { }
    }

    private string FormatDuration(double realHours)
    {
        double totalMins = realHours * 60;
        int m = (int)Math.Floor(totalMins);
        int s = (int)Math.Round((totalMins - m) * 60);
        if (s == 60) { m++; s = 0; }
        
        if (m > 0) return string.Format(Properties.Resources.DurationMinutesSeconds, m, s);
        return string.Format(Properties.Resources.DurationSeconds, s);
    }

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
            p.IsConnected = false; // Reset connection state on load
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
