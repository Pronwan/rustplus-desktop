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

    private int _unreadNotificationsCount;
    public int UnreadNotificationsCount
    {
        get => _unreadNotificationsCount;
        set { _unreadNotificationsCount = value; OnPropertyChanged(); }
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

        NotificationCenterService.UnreadCountChanged += (s, count) => {
            UnreadNotificationsCount = count;
        };
        UnreadNotificationsCount = NotificationCenterService.UnreadCount;
    }

    private void TickClock()
    {
        UpdateServerWipe();

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
        set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStartPairing)); OnPropertyChanged(nameof(ShowLoginOverlay)); OnPropertyChanged(nameof(IsGlobalBusyOverlayVisible)); }
    }

    private bool _isConnectionLoading;
    public bool IsConnectionLoading
    {
        get => _isConnectionLoading;
        set { _isConnectionLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsGlobalBusyOverlayVisible)); }
    }

    private bool _isDeviceStatusChecking;
    public bool IsDeviceStatusChecking
    {
        get => _isDeviceStatusChecking;
        set { if (_isDeviceStatusChecking != value) { _isDeviceStatusChecking = value; OnPropertyChanged(); } }
    }

    private bool _isDeviceSubscribePriming;
    public bool IsDeviceSubscribePriming
    {
        get => _isDeviceSubscribePriming;
        set { if (_isDeviceSubscribePriming != value) { _isDeviceSubscribePriming = value; OnPropertyChanged(); } }
    }

    private int _deviceSubscribeProgress;
    public int DeviceSubscribeProgress
    {
        get => _deviceSubscribeProgress;
        set { if (_deviceSubscribeProgress != value) { _deviceSubscribeProgress = value; OnPropertyChanged(); OnPropertyChanged(nameof(DeviceSubscribeProgressText)); } }
    }

    private int _deviceSubscribeMax = 1;
    public int DeviceSubscribeMax
    {
        get => _deviceSubscribeMax;
        set { if (_deviceSubscribeMax != value) { _deviceSubscribeMax = value; OnPropertyChanged(); OnPropertyChanged(nameof(DeviceSubscribeProgressText)); } }
    }

    private string _deviceSubscribeText = "";
    public string DeviceSubscribeText
    {
        get => _deviceSubscribeText;
        set { if (_deviceSubscribeText != value) { _deviceSubscribeText = value; OnPropertyChanged(); } }
    }

    public string DeviceSubscribeProgressText => $"{DeviceSubscribeProgress} / {DeviceSubscribeMax}";

    private int _deviceStatusProgress;
    public int DeviceStatusProgress
    {
        get => _deviceStatusProgress;
        set { if (_deviceStatusProgress != value) { _deviceStatusProgress = value; OnPropertyChanged(); OnPropertyChanged(nameof(DeviceStatusProgressText)); } }
    }

    private int _deviceStatusMax = 1;
    public int DeviceStatusMax
    {
        get => _deviceStatusMax;
        set { if (_deviceStatusMax != value) { _deviceStatusMax = value; OnPropertyChanged(); OnPropertyChanged(nameof(DeviceStatusProgressText)); } }
    }

    private string _deviceStatusText = "";
    public string DeviceStatusText
    {
        get => _deviceStatusText;
        set { if (_deviceStatusText != value) { _deviceStatusText = value; OnPropertyChanged(); } }
    }

    public string DeviceStatusProgressText => $"{DeviceStatusProgress} / {DeviceStatusMax}";

    public bool IsGlobalBusyOverlayVisible => IsBusy && !IsConnectionLoading;

    private bool _isCloudConnected;
    public bool IsCloudConnected
    {
        get => _isCloudConnected;
        set { _isCloudConnected = value; OnPropertyChanged(); }
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
        set { _isDownloadingUpdate = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanControlUpdateDownload)); }
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

    private string _currentDownloadFile = "";
    public string CurrentDownloadFile
    {
        get => _currentDownloadFile;
        set { _currentDownloadFile = value; OnPropertyChanged(); }
    }

    private bool _isDownloadPaused;
    public bool IsDownloadPaused
    {
        get => _isDownloadPaused;
        set { _isDownloadPaused = value; OnPropertyChanged(); }
    }

    private string _pauseResumeButtonText = "Pause";
    public string PauseResumeButtonText
    {
        get => _pauseResumeButtonText;
        set { _pauseResumeButtonText = value; OnPropertyChanged(); }
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

    private bool _forceShowLoginOverlay;
    public bool ForceShowLoginOverlay
    {
        get => _forceShowLoginOverlay;
        set { _forceShowLoginOverlay = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowLoginOverlay)); }
    }

    private string _loginOverlayMessage = "";
    public string LoginOverlayMessage
    {
        get => _loginOverlayMessage;
        set 
        { 
            _loginOverlayMessage = value; 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(HasLoginOverlayMessage)); 
        }
    }

    public bool HasLoginOverlayMessage => !string.IsNullOrEmpty(_loginOverlayMessage);

    public void NotifyFcmChanged()
    {
        bool hasValidToken = TrackingService.IsFcmConfigured() &&
                            (!TrackingService.FcmExpiresAt.HasValue || TrackingService.FcmExpiresAt.Value >= DateTime.Now);
        if (hasValidToken)
        {
            _forceShowLoginOverlay = false;
            _loginOverlayMessage = "";
            OnPropertyChanged(nameof(ForceShowLoginOverlay));
            OnPropertyChanged(nameof(LoginOverlayMessage));
            OnPropertyChanged(nameof(HasLoginOverlayMessage));
        }

        OnPropertyChanged(nameof(FcmExpiryText));
        OnPropertyChanged(nameof(FcmExpiryDays));
        OnPropertyChanged(nameof(ShowLoginOverlay));
    }

    public bool ShowLoginOverlay
    {
        get
        {
            if (ForceShowLoginOverlay) return true;

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
            if (_selected != null) _selected.PropertyChanged -= SelectedProfile_PropertyChanged;
            _selected = value; 
            if (_selected != null) _selected.PropertyChanged += SelectedProfile_PropertyChanged;
            OnPropertyChanged();                   // "Selected"
            OnPropertyChanged(nameof(CurrentDevices));
            UpdateServerWipe();
        }
    }

    private string _updateStatusText = "Check for updates";
    public string UpdateStatusText
    {
        get => _updateStatusText;
        set { _updateStatusText = value; OnPropertyChanged(); }
    }

    private bool _isUpdateStatusExpanded;
    public bool IsUpdateStatusExpanded
    {
        get => _isUpdateStatusExpanded;
        set { _isUpdateStatusExpanded = value; OnPropertyChanged(); }
    }

    private bool _isUpdateProcessing;
    public bool IsUpdateProcessing
    {
        get => _isUpdateProcessing;
        set { _isUpdateProcessing = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanControlUpdateDownload)); }
    }

    public bool CanControlUpdateDownload => IsDownloadingUpdate && !IsUpdateProcessing;

    private void SelectedProfile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ServerProfile.WipeTime))
            UpdateServerWipe();
    }

    public sealed class StorageSnapshot
    {
        public bool IsToolCupboard { get; init; }
        public int? UpkeepSeconds { get; init; }        // nur TC
        public DateTime SnapshotUtc { get; init; } = DateTime.UtcNow;
        public List<StorageItemVM> Items { get; init; } = new();
    }

    public sealed class StorageItemVM
    {
        public int ItemId { get; init; }
        public string? ShortName { get; init; }
        public int Amount { get; init; }
        public int? MaxStack { get; init; }

        public string Display => MainWindow.ResolveItemName(ItemId, ShortName);
        public ImageSource? Icon => MainWindow.ResolveItemIcon(ItemId, ShortName, 32);
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
    public string ServerWipe
    {
        get => _serverWipe;
        set
        {
            if (_serverWipe == value) return;
            _serverWipe = value;
            OnPropertyChanged();
        }
    }

    public void UpdateServerWipe()
    {
        if (Selected?.WipeTime is not DateTime wipeTime)
        {
            ServerWipe = "-";
            return;
        }

        var local = (wipeTime.Kind == DateTimeKind.Utc ? wipeTime : wipeTime.ToUniversalTime()).ToLocalTime();
        var days = Math.Max(0, (int)Math.Floor((DateTime.Now - local).TotalDays));
        ServerWipe = $"{local.ToString("g", System.Globalization.CultureInfo.CurrentCulture)} ({days}d ago)";
    }

    // NEU: Abgeleitete Binding-Quelle für die Liste
    public ObservableCollection<SmartDevice>? CurrentDevices
        => Selected?.Devices;

    private IReadOnlyDictionary<string, List<long>>? _currentHotkeys;
    public IReadOnlyDictionary<string, List<long>>? CurrentHotkeys
    {
        get => _currentHotkeys;
        set { _currentHotkeys = value; OnPropertyChanged(); }
    }

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

    public void NotifyHotkeysChanged()
        => OnPropertyChanged(nameof(CurrentHotkeys));
}
