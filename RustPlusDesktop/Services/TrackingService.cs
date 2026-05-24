using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services;

public class TrackedPlayer
{
    public string BMId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LastServerName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string GroupColor { get; set; } = string.Empty;
    public List<PlayerSession> Sessions { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsOnline { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string PlayTimeStr { get; set; } = string.Empty;

    public bool IsBMOnly { get; set; } = false;

    public TrackedPlayer CloneWithSnapshots()
    {
        lock (Sessions) // Extra safety for the list itself
        {
            return new TrackedPlayer
            {
                BMId = this.BMId,
                Name = this.Name,
                LastServerName = this.LastServerName,
                GroupName = this.GroupName,
                GroupColor = this.GroupColor,
                IsBMOnly = this.IsBMOnly,
                Sessions = this.Sessions.ToList() // Take snapshot of sessions
            };
        }
    }
}

public class HarborInfo
{
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
}

public class CargoTriggerPoint
{
    public double X { get; set; }
    public double Y { get; set; }
}

public class TrackingSettings
{
    public string LastHost { get; set; } = string.Empty;
    public int LastPort { get; set; }
    public string LastServerName { get; set; } = string.Empty;
    public string? LastBMId { get; set; } = null;
    public bool MapShowSteamMarkers { get; set; } = true;
    public bool MapShowDeathTags { get; set; } = false;
    public bool MapAbbreviateNames { get; set; } = false;
    public double MapPlayerIconScale { get; set; } = 1.0;
    public bool MapUseMonumentText { get; set; } = false;
    public int MapMonumentDisplayMode { get; set; } = 0;
    public double MapMonumentScale { get; set; } = 1.0;
    public double MapMonumentOpacity { get; set; } = 1.0;
    public bool BackgroundTrackingEnabled { get; set; } = true;
    public bool CloseToTrayEnabled { get; set; } = false;
    public bool StartMinimizedEnabled { get; set; } = false;
    public bool AutoConnectEnabled { get; set; } = false;
    public bool AutoStartEnabled { get; set; } = false;
    public bool AutoLoadShops { get; set; } = true;
    public bool HideConsole { get; set; } = false;
    public double SidebarWidth { get; set; } = 600;
    public string SteamId64 { get; set; } = string.Empty;
    public bool AnnounceCargo { get; set; } = false;
    public bool AnnounceHeli { get; set; } = false;
    public bool AnnounceChinook { get; set; } = false;
    public bool AnnounceVendor { get; set; } = false;
    public bool AnnounceOilRig { get; set; } = false;
    public bool AnnounceDeepSea { get; set; } = false;
    public bool AnnouncePlayerOnline { get; set; } = false;
    public bool AnnouncePlayerOffline { get; set; } = false;
    public bool AnnouncePlayerDeathSelf { get; set; } = false;
    public bool AnnouncePlayerDeathTeam { get; set; } = false;
    public bool AnnouncePlayerRespawnSelf { get; set; } = false;
    public bool AnnouncePlayerRespawnTeam { get; set; } = false;
    public bool AnnounceNewShops { get; set; } = false;
    public bool AnnounceSuspiciousShops { get; set; } = false;
    public bool AnnounceTradeAlerts { get; set; } = false;
    public string SelectedLanguage { get; set; } = "";
    public Dictionary<string, bool> GroupStates { get; set; } = new();
    public Dictionary<string, List<string>> GroupOrder { get; set; } = new();
    public bool AnnounceCargoDocking { get; set; } = false;
    public bool AnnounceCargoEgress { get; set; } = false;
    public bool AnnounceCargoArrival { get; set; } = false;
    public bool AnnounceSmartAlerts { get; set; } = false;
    public Dictionary<string, int> LearnedDockingDurations { get; set; } = new();
    public Dictionary<string, int> LearnedCargoFullLifeMinutes { get; set; } = new();
    public Dictionary<string, int> LearnedCargoTravelMinutes { get; set; } = new();
    public Dictionary<string, List<HarborInfo>> ServerHarbors { get; set; } = new();
    public Dictionary<string, Dictionary<string, CargoTriggerPoint>> ServerCargoTriggers { get; set; } = new();
    public bool AnnounceSpawnsMaster { get; set; } = false;
    public bool SaveAlertSelection { get; set; } = true;
    public string LastSeenVersion { get; set; } = "";
    public DateTime? FcmIssuedAt { get; set; }
    public DateTime? FcmExpiresAt { get; set; }
    public bool AnnounceTracking { get; set; } = false;
    public Dictionary<string, int> LearnedQueryPorts { get; set; } = new();
    public bool TranslationConsentGiven { get; set; } = false;
    public bool UploadConsentGiven { get; set; } = false;
}


public class PlayerSession
{
    public DateTime ConnectTime { get; set; }
    public DateTime? DisconnectTime { get; set; }
}

public class OnlinePlayerBM
{
    public string Name { get; set; } = string.Empty;
    public string BMId { get; set; } = string.Empty;
    public DateTime SessionStartTimeUtc { get; set; }
    public TimeSpan Duration { get; set; }
    public bool IsTracked { get; set; }
    public string PlayTimeStr => $"{(int)Duration.TotalHours:D2}:{Duration.Minutes:D2}";
}

public static class TrackingService
{
    private static readonly HttpClient _http = new();
    private static readonly string _dbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "RustPlusDesk", "tracked_players.json");
    private static readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RustPlusDesk", "tracking_settings.json");

    private static readonly string _fcmConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RustPlusDesk", "rustplusjs-config.json");

    public static bool IsFcmConfigured()
        => File.Exists(_fcmConfigPath) && new FileInfo(_fcmConfigPath).Length > 50;

    /// <summary>
    /// Reads steam_id, issue_date, expiry_date from rustplusjs-config.json and seeds
    /// the in-memory TrackingSettings if those values are missing.  Call this on startup
    /// and after every pairing event.
    /// </summary>
    public static void ReadFcmConfig()
    {
        try
        {
            if (!File.Exists(_fcmConfigPath)) return;
            var json = File.ReadAllText(_fcmConfigPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("steam_id", out var sid) && sid.ValueKind == JsonValueKind.String)
            {
                var s = sid.GetString() ?? "";
                if (!string.IsNullOrEmpty(s) && string.IsNullOrEmpty(_settings.SteamId64))
                    _settings.SteamId64 = s;
            }

            if (root.TryGetProperty("issue_date", out var iss) && iss.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(iss.GetString(), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                {
                    if (_settings.FcmIssuedAt == null)
                        _settings.FcmIssuedAt = dt.ToLocalTime();
                }
            }

            if (root.TryGetProperty("expiry_date", out var exp) && exp.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(exp.GetString(), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                {
                    if (_settings.FcmExpiresAt == null)
                        _settings.FcmExpiresAt = dt.ToLocalTime();
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Patches only the steam_id field in rustplusjs-config.json without
    /// touching the rest of the file.  Safe to call after pairing.
    /// </summary>
    public static void PatchFcmConfigSteamId(string steamId)
    {
        try
        {
            if (!File.Exists(_fcmConfigPath) || string.IsNullOrEmpty(steamId)) return;
            var json = File.ReadAllText(_fcmConfigPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            using var ms  = new System.IO.MemoryStream();
            using var wtr = new System.Text.Json.Utf8JsonWriter(ms,
                new JsonWriterOptions { Indented = true });
            wtr.WriteStartObject();
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name == "steam_id") continue; // skip old value
                prop.WriteTo(wtr);
            }
            wtr.WriteString("steam_id", steamId);
            wtr.WriteEndObject();
            wtr.Flush();
            File.WriteAllBytes(_fcmConfigPath, ms.ToArray());
        }
        catch { }
    }
    
    private static readonly object _dbLock = new();
    private static Dictionary<string, TrackedPlayer> _trackedPlayers = new();
    private static TrackingSettings _settings = new();
    private static Timer? _trackingTimer;
    private static string? _lastServerHost;
    private static int _lastServerPort;
    private static string? _lastServerName;

    public static event Action? OnOnlinePlayersUpdated;
    public static event Action<string>? OnServerInfoUpdated;
    public static event Action<string, string>? OnTrackingNotification;
    public static string StatusMessage { get; private set; } = "";
    public static List<OnlinePlayerBM> LastOnlinePlayers { get; private set; } = new();
    public static DateTime? LastPullTime { get; private set; }
    public static bool IsTracking => _trackingTimer != null;

    static TrackingService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "RustPlusDesk/1.0");
        LoadDB();
    }

    private static void LoadDB()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                var json = File.ReadAllText(_dbPath);
                var list = JsonSerializer.Deserialize<List<TrackedPlayer>>(json);
                if (list != null) _trackedPlayers = list.ToDictionary(p => p.BMId);
            }
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                _settings = JsonSerializer.Deserialize<TrackingSettings>(json) ?? new();
            }
        }
        catch { }
    }

    public static void SaveDB()
    {
        try
        {
            var dir = Path.GetDirectoryName(_dbPath);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string jsonP;
            lock (_dbLock)
            {
                var cutoff = DateTime.UtcNow.AddDays(-84); // 12 weeks
                foreach (var p in _trackedPlayers.Values)
                {
                    p.Sessions.RemoveAll(s => s.ConnectTime < cutoff);
                }
                jsonP = JsonSerializer.Serialize(_trackedPlayers.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
            }
            File.WriteAllText(_dbPath, jsonP);

            var jsonS = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, jsonS);
        }
        catch { }
    }

    private static void Log(string message)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RustPlusDesk", "tracking_log.txt");
            var dir = Path.GetDirectoryName(logPath);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public static void TrackPlayer(string bmId, string name, string serverName, PlayerSession? initialSession = null, bool isBMOnly = false)
    {
        lock (_dbLock)
        {
            if (!_trackedPlayers.TryGetValue(bmId, out var p))
            {
                p = new TrackedPlayer { BMId = bmId, Name = name, LastServerName = serverName, IsBMOnly = isBMOnly };
                _trackedPlayers[bmId] = p;
                
                // Add initial session if provided and not already present
                if (initialSession != null)
                {
                    p.Sessions.Add(initialSession);
                }
            }
            else
            {
                p.LastServerName = serverName;
                if (name != "Unknown Player") p.Name = name;
                if (isBMOnly) p.IsBMOnly = true;

                if (initialSession != null && !p.Sessions.Any(s => s.ConnectTime == initialSession.ConnectTime))
                {
                    p.Sessions.Add(initialSession);
                    p.Sessions = p.Sessions.OrderBy(s => s.ConnectTime).ToList();
                }
            }
        }

        SaveDB();

        // Auto-start tracking if we have a server but no timer yet
        if (_trackingTimer == null && !string.IsNullOrEmpty(_settings.LastHost))
        {
            StartPolling(_settings.LastHost, _settings.LastPort, _settings.LastServerName);
        }
        OnOnlinePlayersUpdated?.Invoke();
    }
    
    public static void UntrackPlayer(string bmId)
    {
        bool removed = false;
        lock (_dbLock)
        {
            removed = _trackedPlayers.Remove(bmId);
        }

        if (removed)
        {
            SaveDB();
            if (GetTrackedPlayers().Count == 0)
            {
                StopPolling();
            }
            OnOnlinePlayersUpdated?.Invoke();
        }
    }
    
    public static string? CurrentServerBMId => _foundServerId;

    public static void RenameTrackedPlayer(string bmId, string newName)
    {
        lock (_dbLock)
        {
            if (_trackedPlayers.TryGetValue(bmId, out var player))
            {
                player.Name = newName;
            }
            else return;
        }
        SaveDB();
        OnOnlinePlayersUpdated?.Invoke();
    }

    public static void MigrateTrackedPlayer(string oldBmId, string newBmId, string newName)
    {
        lock (_dbLock)
        {
            if (_trackedPlayers.TryGetValue(oldBmId, out var player))
            {
                _trackedPlayers.Remove(oldBmId);
                player.BMId = newBmId;
                player.Name = newName;
                _trackedPlayers[newBmId] = player;
            }
            else return;
        }
        SaveDB();
        OnOnlinePlayersUpdated?.Invoke();
    }
    public static void SetPlayerGroup(string bmId, string groupName, string groupColor)
    {
        lock (_dbLock)
        {
            if (_trackedPlayers.TryGetValue(bmId, out var player))
            {
                player.GroupName = groupName;
                player.GroupColor = groupColor;
            }
            else return;
        }
        SaveDB();
        OnOnlinePlayersUpdated?.Invoke();
    }
    public static List<TrackedPlayer> GetTrackedPlayers() 
    {
        lock (_dbLock)
        {
            return _trackedPlayers.Values.Select(p => p.CloneWithSnapshots()).ToList();
        }
    }
    public static bool IsTracked(string bmId)
    {
        lock (_dbLock)
        {
            return _trackedPlayers.ContainsKey(bmId);
        }
    }

    public static bool GetGroupState(string serverName, string groupName)
    {
        var key = $"{serverName}|{groupName}";
        if (_settings.GroupStates.TryGetValue(key, out var expanded)) return expanded;
        return true; // Default to expanded
    }

    public static void SetGroupState(string serverName, string groupName, bool expanded)
    {
        var key = $"{serverName}|{groupName}";
        _settings.GroupStates[key] = expanded;
        SaveDB();
    }

    public static List<string> GetGroupOrder(string serverName)
    {
        if (_settings.GroupOrder.TryGetValue(serverName, out var order)) return order;
        return new List<string>();
    }

    public static void SetGroupOrder(string serverName, List<string> order)
    {
        _settings.GroupOrder[serverName] = order;
        SaveDB();
    }

    public static bool IsBackgroundTrackingEnabled
    {
        get => _settings.BackgroundTrackingEnabled;
        set { _settings.BackgroundTrackingEnabled = value; SaveDB(); }
    }

    public static bool CloseToTrayEnabled
    {
        get => _settings.CloseToTrayEnabled;
        set { _settings.CloseToTrayEnabled = value; SaveDB(); }
    }

    public static bool StartMinimizedEnabled
    {
        get => _settings.StartMinimizedEnabled;
        set { _settings.StartMinimizedEnabled = value; SaveDB(); }
    }

    public static bool AutoConnectEnabled
    {
        get => _settings.AutoConnectEnabled;
        set { _settings.AutoConnectEnabled = value; SaveDB(); }
    }

    public static bool AutoStartEnabled
    {
        get => _settings.AutoStartEnabled;
        set 
        { 
            if (_settings.AutoStartEnabled == value) return;
            _settings.AutoStartEnabled = value; 
            SetAutoStart(value);
            SaveDB(); 
        }
    }

    public static bool AutoLoadShops
    {
        get => _settings.AutoLoadShops;
        set { _settings.AutoLoadShops = value; SaveDB(); }
    }

    public static bool HideConsole
    {
        get => _settings.HideConsole;
        set { _settings.HideConsole = value; SaveDB(); }
    }

    public static double SidebarWidth
    {
        get => _settings.SidebarWidth;
        set { _settings.SidebarWidth = value; SaveDB(); }
    }

    public static string SteamId64
    {
        get => _settings.SteamId64;
        set { _settings.SteamId64 = value; SaveDB(); }
    }

    public static DateTime? FcmIssuedAt
    {
        get => _settings.FcmIssuedAt;
        set { _settings.FcmIssuedAt = value; SaveDB(); }
    }

    public static DateTime? FcmExpiresAt
    {
        get => _settings.FcmExpiresAt;
        set { _settings.FcmExpiresAt = value; SaveDB(); }
    }

    public static bool AnnounceCargo
    {
        get => _settings.AnnounceCargo;
        set { _settings.AnnounceCargo = value; SaveDB(); }
    }
    public static bool AnnounceHeli
    {
        get => _settings.AnnounceHeli;
        set { _settings.AnnounceHeli = value; SaveDB(); }
    }
    public static bool AnnounceChinook
    {
        get => _settings.AnnounceChinook;
        set { _settings.AnnounceChinook = value; SaveDB(); }
    }
    public static bool AnnounceVendor
    {
        get => _settings.AnnounceVendor;
        set { _settings.AnnounceVendor = value; SaveDB(); }
    }
    public static bool AnnounceOilRig
    {
        get => _settings.AnnounceOilRig;
        set { _settings.AnnounceOilRig = value; SaveDB(); }
    }
    public static bool AnnounceDeepSea
    {
        get => _settings.AnnounceDeepSea;
        set { _settings.AnnounceDeepSea = value; SaveDB(); }
    }
    public static bool AnnouncePlayerOnline
    {
        get => _settings.AnnouncePlayerOnline;
        set { _settings.AnnouncePlayerOnline = value; SaveDB(); }
    }
    public static bool AnnounceTracking
    {
        get => _settings.AnnounceTracking;
        set { _settings.AnnounceTracking = value; SaveDB(); }
    }
    public static bool AnnouncePlayerOffline
    {
        get => _settings.AnnouncePlayerOffline;
        set { _settings.AnnouncePlayerOffline = value; SaveDB(); }
    }
    public static bool AnnouncePlayerDeathSelf
    {
        get => _settings.AnnouncePlayerDeathSelf;
        set { _settings.AnnouncePlayerDeathSelf = value; SaveDB(); }
    }
    public static bool AnnouncePlayerDeathTeam
    {
        get => _settings.AnnouncePlayerDeathTeam;
        set { _settings.AnnouncePlayerDeathTeam = value; SaveDB(); }
    }
    public static bool AnnouncePlayerRespawnSelf
    {
        get => _settings.AnnouncePlayerRespawnSelf;
        set { _settings.AnnouncePlayerRespawnSelf = value; SaveDB(); }
    }
    public static bool AnnouncePlayerRespawnTeam
    {
        get => _settings.AnnouncePlayerRespawnTeam;
        set { _settings.AnnouncePlayerRespawnTeam = value; SaveDB(); }
    }
    public static bool AnnounceNewShops
    {
        get => _settings.AnnounceNewShops;
        set { _settings.AnnounceNewShops = value; SaveDB(); }
    }
    public static bool AnnounceSuspiciousShops
    {
        get => _settings.AnnounceSuspiciousShops;
        set { _settings.AnnounceSuspiciousShops = value; SaveDB(); }
    }
    public static bool AnnounceTradeAlerts
    {
        get => _settings.AnnounceTradeAlerts;
        set { _settings.AnnounceTradeAlerts = value; SaveDB(); }
    }

    public static string SelectedLanguage
    {
        get => _settings.SelectedLanguage;
        set { _settings.SelectedLanguage = value; SaveDB(); }
    }

    public static bool AnnounceSpawnsMaster
    {
        get => _settings.AnnounceSpawnsMaster;
        set { _settings.AnnounceSpawnsMaster = value; SaveDB(); }
    }

    public static bool TranslationConsentGiven
    {
        get => _settings.TranslationConsentGiven;
        set { _settings.TranslationConsentGiven = value; SaveDB(); }
    }

    public static bool UploadConsentGiven
    {
        get => _settings.UploadConsentGiven;
        set { _settings.UploadConsentGiven = value; SaveDB(); }
    }

    public static bool AnnounceCargoDocking
    {
        get => _settings.AnnounceCargoDocking;
        set { _settings.AnnounceCargoDocking = value; SaveDB(); }
    }
    public static bool AnnounceCargoEgress
    {
        get => _settings.AnnounceCargoEgress;
        set { _settings.AnnounceCargoEgress = value; SaveDB(); }
    }
    public static int GetLearnedDockingDuration(string host)
    {
        if (_settings.LearnedDockingDurations.TryGetValue(host, out var d)) return d;
        return 8; // Default 8 minutes (before server-specific value is learned)
    }
    public static void SetLearnedDockingDuration(string host, int minutes)
    {
        if (minutes < 1 || minutes > 60) return;
        _settings.LearnedDockingDurations[host] = minutes;
        SaveDB();
    }
    public static bool AnnounceCargoArrival
    {
        get => _settings.AnnounceCargoArrival;
        set { _settings.AnnounceCargoArrival = value; SaveDB(); }
    }
    public static bool AnnounceSmartAlerts
    {
        get => _settings.AnnounceSmartAlerts;
        set { _settings.AnnounceSmartAlerts = value; SaveDB(); }
    }
    public static string LastServerName
    {
        get => _settings.LastServerName;
        set { _settings.LastServerName = value; SaveDB(); }
    }

    public static bool MapShowSteamMarkers
    {
        get => _settings.MapShowSteamMarkers;
        set { _settings.MapShowSteamMarkers = value; SaveDB(); }
    }
    public static bool MapShowDeathTags
    {
        get => _settings.MapShowDeathTags;
        set { _settings.MapShowDeathTags = value; SaveDB(); }
    }
    public static bool MapAbbreviateNames
    {
        get => _settings.MapAbbreviateNames;
        set { _settings.MapAbbreviateNames = value; SaveDB(); }
    }
    public static double MapPlayerIconScale
    {
        get => _settings.MapPlayerIconScale;
        set { _settings.MapPlayerIconScale = value; SaveDB(); }
    }
    public static bool MapUseMonumentText
    {
        get => _settings.MapMonumentDisplayMode == 1;
        set { _settings.MapMonumentDisplayMode = value ? 1 : 0; SaveDB(); }
    }
    public static int MapMonumentDisplayMode
    {
        get => _settings.MapMonumentDisplayMode;
        set { _settings.MapMonumentDisplayMode = value; SaveDB(); }
    }
    public static double MapMonumentScale
    {
        get => _settings.MapMonumentScale;
        set { _settings.MapMonumentScale = value; SaveDB(); }
    }
    public static double MapMonumentOpacity
    {
        get => _settings.MapMonumentOpacity;
        set { _settings.MapMonumentOpacity = value; SaveDB(); }
    }
    public static string LastSeenVersion
    {
        get => _settings.LastSeenVersion;
        set { _settings.LastSeenVersion = value; SaveDB(); }
    }
    public static int GetLearnedCargoFullLife(string host)
    {
        if (_settings.LearnedCargoFullLifeMinutes.TryGetValue(host, out var d)) return d;
        return 0; 
    }
    public static void SetLearnedCargoFullLife(string host, int minutes)
    {
        if (minutes < 10 || minutes > 120) return;
        _settings.LearnedCargoFullLifeMinutes[host] = minutes;
        SaveDB();
    }
    public static int GetLearnedCargoTravelTime(string host)
    {
        if (_settings.LearnedCargoTravelMinutes.TryGetValue(host, out var d)) return d;
        return 0;
    }
    public static void SetLearnedCargoTravelTime(string host, int minutes)
    {
        if (minutes < 1 || minutes > 30) return;
        _settings.LearnedCargoTravelMinutes[host] = minutes;
        SaveDB();
    }

    public static List<HarborInfo> GetServerHarbors(string host)
    {
        if (_settings.ServerHarbors.TryGetValue(host, out var list)) return list;
        return new();
    }

    public static void SetServerHarbors(string host, List<HarborInfo> harbors)
    {
        _settings.ServerHarbors[host] = harbors;
        _settings.ServerCargoTriggers.Remove(host); // Wipe detected -> Clear triggers
        SaveDB();
    }

    public static CargoTriggerPoint? GetCargoTriggerPoint(string host, string harborName)
    {
        if (_settings.ServerCargoTriggers.TryGetValue(host, out var dict))
        {
            if (dict.TryGetValue(harborName, out var p)) return p;
        }
        return null;
    }

    public static void SetCargoTriggerPoint(string host, string harborName, double x, double y)
    {
        if (!_settings.ServerCargoTriggers.ContainsKey(host))
            _settings.ServerCargoTriggers[host] = new();
        _settings.ServerCargoTriggers[host][harborName] = new CargoTriggerPoint { X = x, Y = y };
        SaveDB();
    }

    public static bool HasAnyCargoTrigger(string host)
    {
        return _settings.ServerCargoTriggers.TryGetValue(host, out var dict) && dict.Count > 0;
    }

    public static bool SaveAlertSelection
    {
        get => _settings.SaveAlertSelection;
        set { _settings.SaveAlertSelection = value; SaveDB(); }
    }

    private static void SetAutoStart(bool enabled)
    {
        try
        {
            const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKey, true);
            if (key == null) return;

            string appName = "RustPlusDesk";
            if (enabled)
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;
                key.SetValue(appName, $"\"{exePath}\" --background");
            }
            else
            {
                key.DeleteValue(appName, false);
            }
        }
        catch { }
    }

    public static (string host, int port, string name) LastServer => (_settings.LastHost, _settings.LastPort, _settings.LastServerName);
    public static string? LastBMId => _settings.LastBMId;

    public static async Task<string> FetchPlayerNameAsync(string bmId)
    {
        if (bmId.Length == 17 && bmId.StartsWith("7656") && ulong.TryParse(bmId, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            try
            {
                var xml = await _http.GetStringAsync($"https://steamcommunity.com/profiles/{bmId}?xml=1");
                var m = System.Text.RegularExpressions.Regex.Match(xml, @"<steamID><!\[CDATA\[(.*?)\]\]></steamID>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success) return m.Groups[1].Value.Trim();
            }
            catch { }
        }
        return await Task.FromResult(bmId);
    }

    public static async Task<DateTime?> FetchPlayerLastSeenAsync(string bmId)
    {
        lock (_dbLock)
        {
            if (_trackedPlayers.TryGetValue(bmId, out var tp) && tp.Sessions.Any())
            {
                var last = tp.Sessions.Last();
                if (last.DisconnectTime.HasValue) return last.DisconnectTime;
            }
        }
        return await Task.FromResult<DateTime?>(null);
    }

    public static void LoadDemoData()
    {
        lock (_dbLock)
        {
            _trackedPlayers.Clear();
            var now = DateTime.UtcNow;

            // 1. The Night Owl (Plays 00:00 - 06:00)
            var owl = new TrackedPlayer { BMId = "demo_1", Name = "NightOwl_X" };
            for (int d = 0; d < 14; d++) {
                var date = now.Date.AddDays(-d).AddHours(1); // 01:00
                owl.Sessions.Add(new PlayerSession { ConnectTime = date, DisconnectTime = date.AddHours(4) });
            }
            _trackedPlayers[owl.BMId] = owl;

            // 2. The Grinder (Huge playtime, active 12:00 - 02:00)
            var grinder = new TrackedPlayer { BMId = "demo_2", Name = "IndustrialPvP" };
            for (int d = 0; d < 7; d++) {
                var date = now.Date.AddDays(-d).AddHours(12); // Noon
                grinder.Sessions.Add(new PlayerSession { ConnectTime = date, DisconnectTime = date.AddHours(14) }); // Until 02:00
            }
            _trackedPlayers[grinder.BMId] = grinder;

            // 3. The Weekend Warrior (Only Sat/Sun)
            var weekend = new TrackedPlayer { BMId = "demo_3", Name = "CasualFriday" };
            for (int d = 0; d < 30; d++) {
                var date = now.Date.AddDays(-d);
                if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) {
                    weekend.Sessions.Add(new PlayerSession { ConnectTime = date.AddHours(10), DisconnectTime = date.AddHours(18) });
                }
            }
            _trackedPlayers[weekend.BMId] = weekend;
        }

        SaveDB();
        OnOnlinePlayersUpdated?.Invoke();
    }

    public static async Task<PlayerSession?> FetchPlayerLastSessionAsync(string bmId)
    {
        lock (_dbLock)
        {
            if (_trackedPlayers.TryGetValue(bmId, out var tp) && tp.Sessions.Any())
            {
                return tp.Sessions.Last();
            }
        }
        return await Task.FromResult<PlayerSession?>(null);
    }
    public static string GetAnalysisReport(string? targetBmId = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'>");
        sb.AppendLine("<style>");
        // Root styles
        sb.AppendLine("body { background: #0d1117; color: #c9d1d9; font-family: -apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif; margin: 30px; line-height: 1.5; }");
        sb.AppendLine(".player-card { background: #161b22; border: 1px solid #30363d; border-radius: 8px; padding: 24px; margin-bottom: 30px; box-shadow: 0 8px 24px rgba(0,0,0,0.2); }");
        sb.AppendLine("h1 { color: #f0f6fc; font-size: 28px; font-weight: 600; margin-bottom: 30px; letter-spacing: -0.5px; }");

        // Theme variables (to be overridden per card)
        sb.AppendLine(".theme-online { --theme-accent: #3fb950; --theme-accent-soft: rgba(63, 185, 80, 0.1); --theme-accent-border: rgba(63, 185, 80, 0.3); --cell-lv1: #0e4429; --cell-lv2: #006d32; --cell-lv3: #26a641; --cell-lv4: #39d353; }");
        sb.AppendLine(".theme-offline { --theme-accent: #8b949e; --theme-accent-soft: rgba(139, 148, 158, 0.1); --theme-accent-border: rgba(139, 148, 158, 0.3); --cell-lv1: #161b22; --cell-lv2: #21262d; --cell-lv3: #30363d; --cell-lv4: #484f58; }");

        sb.AppendLine("h2 { color: var(--theme-accent); margin: 0 0 16px 0; font-size: 22px; border-bottom: 1px solid #21262d; padding-bottom: 8px; }");
        sb.AppendLine(".stat-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 15px; margin-bottom: 20px; }");
        sb.AppendLine(".stat-item { background: #0d1117; padding: 12px; border-radius: 6px; border: 1px solid #21262d; }");
        sb.AppendLine(".stat-label { font-size: 11px; color: #8b949e; text-transform: uppercase; font-weight: 600; }");
        sb.AppendLine(".stat-value { font-size: 16px; color: #f0f6fc; font-weight: 600; margin-top: 4px; }");
        
        sb.AppendLine(".badge { padding: 4px 10px; border-radius: 4px; font-size: 12px; font-weight: 600; text-transform: uppercase; }");
        sb.AppendLine(".badge-online { background: rgba(63, 185, 80, 0.1); color: #3fb950; border: 1px solid rgba(63, 185, 80, 0.4); }");
        sb.AppendLine(".badge-offline { background: rgba(139, 148, 158, 0.05); color: #8b949e; border: 1px solid rgba(139, 148, 158, 0.2); }");
        
        sb.AppendLine(".section-title { font-size: 13px; font-weight: 600; color: #8b949e; margin: 25px 0 10px 0; display: flex; align-items: center; }");
        sb.AppendLine(".section-title::after { content: ''; flex: 1; height: 1px; background: #21262d; margin-left: 10px; }");

        // GitHub style grid
        sb.AppendLine(".grid-container { display: grid; grid-template-columns: repeat(12, 1fr); gap: 10px; margin-top: 10px; }");
        sb.AppendLine(".grid-week { display: grid; grid-template-rows: repeat(7, 10px); gap: 2px; }");
        sb.AppendLine(".grid-cell { width: 10px; height: 10px; border-radius: 2px; background: #21262d; }");
        sb.AppendLine(".grid-cell.lv1 { background: var(--cell-lv1); }");
        sb.AppendLine(".grid-cell.lv2 { background: var(--cell-lv2); }");
        sb.AppendLine(".grid-cell.lv3 { background: var(--cell-lv3); }");
        sb.AppendLine(".grid-cell.lv4 { background: var(--cell-lv4); }");

        // Hourly heat
        sb.AppendLine(".hourly-wrap { background: #0d1117; padding: 15px; border-radius: 6px; border: 1px solid #21262d; }");
        sb.AppendLine(".hourly-container { display: flex; height: 60px; gap: 2px; align-items: flex-end; }");
        sb.AppendLine(".hour-bar { flex: 1; background: #21262d; border-radius: 2px 2px 0 0; position: relative; }");
        sb.AppendLine(".hour-bar.active { background: var(--theme-accent); }");
        sb.AppendLine(".hour-labels { display: flex; justify-content: space-between; margin-top: 8px; font-size: 10px; color: #8b949e; font-family: monospace; }");
        
        sb.AppendLine(".insight-box { background: var(--theme-accent-soft); border: 1px solid var(--theme-accent-border); padding: 16px; margin-top: 20px; border-radius: 8px; }");
        sb.AppendLine(".insight-item { margin: 8px 0; font-size: 14px; display: flex; align-items: center; }");
        sb.AppendLine(".insight-icon { margin-right: 10px; font-size: 18px; }");
        sb.AppendLine(".warning { background: rgba(210, 153, 34, 0.1); border: 1px solid rgba(210, 153, 34, 0.2); color: #d29922; padding: 10px; border-radius: 6px; font-size: 12px; margin-top: 15px; }");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<h1>Activity Intelligence Report</h1>");
        
        List<TrackedPlayer> playersToReport;
        lock (_dbLock)
        {
            playersToReport = targetBmId == null 
                ? _trackedPlayers.Values.ToList() 
                : _trackedPlayers.Values.Where(p => p.BMId == targetBmId).ToList();
        }

        if (!playersToReport.Any())
        {
            sb.AppendLine("<p>No players in tracking database. Start by tracking players from the server list.</p>");
        }

        var groupedPlayers = playersToReport.GroupBy(p => string.IsNullOrEmpty(p.LastServerName) ? "Global / Legacy" : p.LastServerName);

        foreach(var group in groupedPlayers)
        {
            sb.AppendLine($"<div class='section-title' style='color:#58a6ff; font-size:16px; margin-top:40px; border-bottom: 2px solid #30363d;'>{group.Key}</div>");
            
            foreach(var p in group)
            {
                if (p.IsBMOnly)
                {
                    sb.AppendLine($"<div class='player-card theme-offline' style='padding: 15px; display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px;'>");
                    sb.AppendLine($"<h2 style='margin: 0;'>{p.Name}</h2>");
                    sb.AppendLine($"<a href='https://www.battlemetrics.com/players/{p.BMId}' target='_blank' style='background-color: #58a6ff; color: #ffffff; padding: 8px 16px; text-decoration: none; border-radius: 4px; font-weight: bold; cursor: pointer;'>View on BattleMetrics</a>");
                    sb.AppendLine("</div>");
                    continue;
                }

                var totalTime = TimeSpan.Zero;
                var past7Days = TimeSpan.Zero;
                var now = DateTime.UtcNow;
                
                int[] hourActivity = new int[24];
                Dictionary<DateTime, int> dailyActivity = new Dictionary<DateTime, int>();

                List<PlayerSession> sessionsSnapshot;
                lock (_dbLock)
                {
                    sessionsSnapshot = p.Sessions.ToList();
                }

                foreach (var session in sessionsSnapshot)
                {
                    var end = session.DisconnectTime ?? now;
                    var dur = end - session.ConnectTime;
                    totalTime += dur;
                    if (session.ConnectTime > now.AddDays(-7)) past7Days += dur;

                    var date = session.ConnectTime.Date;
                    if (!dailyActivity.ContainsKey(date)) dailyActivity[date] = 0;
                    dailyActivity[date] += (int)dur.TotalMinutes;

                    var iter = session.ConnectTime;
                    while (iter < end)
                    {
                        hourActivity[iter.ToLocalTime().Hour]++;
                        iter = iter.AddHours(1);
                    }
                }

                double avgSessionMins = p.Sessions.Any() ? totalTime.TotalMinutes / p.Sessions.Count : 0;
                var isOnline = sessionsSnapshot.Any() && !sessionsSnapshot.Last().DisconnectTime.HasValue;
                var themeClass = isOnline ? "theme-online" : "theme-offline";

                sb.AppendLine($"<div class='player-card {themeClass}'>");
                sb.AppendLine($"<h2>{p.Name}</h2>");
                
                var statusClass = isOnline ? "badge-online" : "badge-offline";
                var statusText = isOnline ? "Online" : "Offline";
                sb.AppendLine($"<div style='margin-bottom:20px;'><span class='badge {statusClass}'>{statusText}</span></div>");

                var lastS = sessionsSnapshot.LastOrDefault();
                string lastConnectedStr = lastS != null ? lastS.ConnectTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "Never";
                string lastSeenStr = lastS != null ? (lastS.DisconnectTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "Active Now") : "Never";

                sb.AppendLine("<div class='stat-grid'>");
                sb.AppendLine("<div class='stat-item'><div class='stat-label'>Last Connected</div><div class='stat-value'>" + lastConnectedStr + "</div></div>");
                sb.AppendLine("<div class='stat-item'><div class='stat-label'>Last Seen</div><div class='stat-value'>" + lastSeenStr + "</div></div>");
                sb.AppendLine("<div class='stat-item'><div class='stat-label'>Total Tracked Time</div><div class='stat-value'>" + $"{(int)totalTime.TotalHours}h {totalTime.Minutes}m" + "</div></div>");
                sb.AppendLine("<div class='stat-item'><div class='stat-label'>Last 7 Days</div><div class='stat-value'>" + $"{(int)past7Days.TotalHours}h {past7Days.Minutes}m" + "</div></div>");
                sb.AppendLine("<div class='stat-item'><div class='stat-label'>Session Count</div><div class='stat-value'>" + p.Sessions.Count + "</div></div>");
                sb.AppendLine("<div class='stat-item'><div class='stat-label'>Avg Session</div><div class='stat-value'>" + $"{(int)avgSessionMins} min" + "</div></div>");
                sb.AppendLine("</div>");

            // GitHub Style Grid Section
            sb.AppendLine("<div class='section-title'>12-WEEK ACTIVITY INTENSITY</div>");
            sb.AppendLine("<div class='grid-container'>");
            var startDate = now.Date.AddDays(-83); // 12 weeks
            for (int w = 0; w < 12; w++)
            {
                sb.AppendLine("<div class='grid-week'>");
                for (int d = 0; d < 7; d++)
                {
                    var cur = startDate.AddDays(w * 7 + d);
                    int mins = dailyActivity.ContainsKey(cur) ? dailyActivity[cur] : 0;
                    string lv = "";
                    if (mins > 0) lv = "lv1";
                    if (mins > 120) lv = "lv2";
                    if (mins > 300) lv = "lv3";
                    if (mins > 600) lv = "lv4";
                    sb.AppendLine($"<div class='grid-cell {lv}' title='{cur:yyyy-MM-dd}: {mins} min'></div>");
                }
                sb.AppendLine("</div>");
            }
            sb.AppendLine("</div>");

            // 24h Heatmap Section
            sb.AppendLine("<div class='section-title'>24H ACTIVITY FORECAST</div>");
            sb.AppendLine("<div class='hourly-wrap'>");
            sb.AppendLine("<div class='hourly-container'>");
            int maxH = hourActivity.Any() ? hourActivity.Max() : 0;
            for(int i=0; i<24; i++)
            {
                double hVal = maxH > 0 ? (double)hourActivity[i] / maxH * 100 : 5;
                string activeClass = hourActivity[i] > (maxH * 0.4) ? "active" : "";
                sb.AppendLine($"<div class='hour-bar {activeClass}' style='height:{hVal}%' title='{i:00}:00 - {hourActivity[i]} occurrences'></div>");
            }
            sb.AppendLine("</div>");
            sb.AppendLine("<div class='hour-labels'>");
            sb.AppendLine("<span>00:00</span><span>04:00</span><span>08:00</span><span>12:00</span><span>16:00</span><span>20:00</span><span>23:00</span>");
            sb.AppendLine("</div>");
            sb.AppendLine("</div>");

            // AI Insights Box
            int peakPlay = 0; int maxPlayVal = -1;
            int peakSleep = 0; int minPlayVal = int.MaxValue;
            for(int i=0; i<24; i++) {
                if (hourActivity[i] > maxPlayVal) { maxPlayVal = hourActivity[i]; peakPlay = i; }
                if (hourActivity[i] < minPlayVal) { minPlayVal = hourActivity[i]; peakSleep = i; }
            }

            sb.AppendLine("<div class='insight-box'>");
            sb.AppendLine("<div class='insight-item'><span class='insight-icon'>⚡</span> Most likely to play: <b>" + $"{peakPlay:00}:00 - {(peakPlay + 3) % 24:00}:00" + "</b></div>");
            sb.AppendLine("<div class='insight-item'><span class='insight-icon'>💤</span> Most likely to sleep: <b>" + $"{peakSleep:00}:00 - {(peakSleep + 5) % 24:00}:00" + "</b></div>");
            if (p.Sessions.Count < 5) {
                sb.AppendLine("<div class='warning'><b>Data Confidence: LOW</b><br/>More sessions needed for accurate pattern recognition. Predictions currenty represent early observations.</div>");
            } else {
                sb.AppendLine("<div style='color: #8b949e; font-size: 11px; margin-top: 10px;'>Forecast based on " + p.Sessions.Count + " recorded sessions.</div>");
            }
            sb.AppendLine("</div>");

            sb.AppendLine("</div>");
        }
    }
        
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string? _foundServerId;

    public static void StartPolling(string host, int port, string name, string? bmId = null)
    {
        if (_lastServerHost != host || _lastServerPort != port)
        {
            LastOnlinePlayers = new List<OnlinePlayerBM>();
            OnOnlinePlayersUpdated?.Invoke();
        }

        _lastServerHost = host;
        _lastServerPort = port;
        _lastServerName = name;
        _foundServerId = bmId; // Use provided ID if available, otherwise it stays null for auto-lookup

        _settings.LastHost = host;
        _settings.LastPort = port;
        _settings.LastServerName = name;
        _settings.LastBMId = bmId;
        SaveDB();

        // Poll every 2 minutes only if we have *real* (non-BM-only) tracked players.
        // BM-only shortcuts don't need any UDP queries.
        _trackingTimer?.Dispose();
        if (GetTrackedPlayers().Any(p => !p.IsBMOnly))
        {
            _trackingTimer = new Timer(async _ => await PollOnceAsync(), null, 0, 120_000);
        }
        else
        {
            _trackingTimer = null;
        }
    }

    public static void StopPolling()
    {
        _trackingTimer?.Dispose();
        _trackingTimer = null;
    }

    public static async Task FetchOnlinePlayersNowAsync()
    {
        await PollOnceAsync();
    }

    private static async Task<int?> AutoDiscoverQueryPortAsync(string host, int appPort)
    {
        // Ask Steam which query ports exist for this IP.
        // The API is authoritative — no need to probe with A2S first.
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var json = await _http.GetStringAsync(
                $"https://api.steampowered.com/ISteamApps/GetServersAtAddress/v1?addr={host}",
                cts.Token);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("response", out var resp) && 
                resp.TryGetProperty("servers", out var servers) && 
                servers.ValueKind == JsonValueKind.Array)
            {
                // Find all Rust (appid 252490) ports for this IP
                var rustPorts = new List<int>();
                foreach (var s in servers.EnumerateArray())
                {
                    if (s.TryGetProperty("appid", out var appid) && appid.GetInt32() == 252490)
                    {
                        if (s.TryGetProperty("addr", out var addrStr))
                        {
                            var parts = addrStr.GetString()?.Split(':');
                            if (parts?.Length == 2 && int.TryParse(parts[1], out int sp))
                                rustPorts.Add(sp);
                        }
                    }
                }

                if (rustPorts.Count == 1)
                {
                    // Exactly one Rust server on this IP — trust it immediately, no probe needed
                    Log($"[AutoDiscover] Steam API: single Rust port {rustPorts[0]} for {host}");
                    return rustPorts[0];
                }

                if (rustPorts.Count > 1)
                {
                    // Multiple servers on same IP: pick the one closest to appPort
                    var best = rustPorts.OrderBy(p => Math.Abs(p - appPort)).First();
                    Log($"[AutoDiscover] Steam API: multiple ports, chose {best} for {host}");
                    return best;
                }
            }
        }
        catch { }

        // Steam API gave nothing — caller will try common port offsets in the main query
        return null;
    }

    private static async Task PollOnceAsync()
    {
        var serversToPoll = new HashSet<(string Host, int Port, string Name)>();
        
        if (!string.IsNullOrEmpty(_lastServerHost))
        {
            serversToPoll.Add((_lastServerHost, _lastServerPort, _lastServerName ?? ""));
        }

        var trackedPlayers = GetTrackedPlayers().Where(p => !p.IsBMOnly).ToList();
        if (trackedPlayers.Count > 0)
        {
            var profiles = StorageService.LoadProfiles();
            foreach (var p in trackedPlayers)
            {
                var match = profiles.FirstOrDefault(prof => prof.Name == p.LastServerName);
                if (match != null && !string.IsNullOrEmpty(match.Host))
                {
                    serversToPoll.Add((match.Host, match.Port, match.Name ?? ""));
                }
            }
        }

        bool updatedUi = false;

        foreach (var server in serversToPoll)
        {
            try
            {
                await PollSingleServerAsync(server.Host, server.Port, server.Name);
                if (server.Host == _lastServerHost && server.Port == _lastServerPort)
                {
                    updatedUi = true;
                }
            }
            catch (Exception)
            {
                if (server.Host == _lastServerHost && server.Port == _lastServerPort)
                {
                    StatusMessage = "Server offline (A2S Query Timeout)";
                    OnOnlinePlayersUpdated?.Invoke();
                }
                Log($"[A2S] Failed to background poll {server.Name} ({server.Host}:{server.Port}) - Timeout/Offline");
            }
        }
        
        LastPullTime = DateTime.Now;
        OnOnlinePlayersUpdated?.Invoke();
    }

    private static async Task PollSingleServerAsync(string host, int port, string serverName)
    {
        if (string.IsNullOrEmpty(host)) return;

        string hostKey = $"{host}:{port}";
        int queryPort = port; // default to AppPort if not learned
        bool isCurrentServer = host == _lastServerHost && port == _lastServerPort;

        try
        {
            if (_settings.LearnedQueryPorts.TryGetValue(hostKey, out int learned))
            {
                queryPort = learned;
            }
            else
            {
                if (isCurrentServer)
                {
                    StatusMessage = "Auto-Discovering Query Port via Steam API...";
                    OnOnlinePlayersUpdated?.Invoke();
                }
                
                var discovered = await AutoDiscoverQueryPortAsync(host, port);
                if (discovered.HasValue)
                {
                    queryPort = discovered.Value;
                    // Save immediately so subsequent calls skip discovery
                    _settings.LearnedQueryPorts[hostKey] = queryPort;
                    SaveDB();
                }
                // If discovery failed, queryPort stays at appPort.
                // We'll try common offsets below in the multi-port fallback.
            }

            if (isCurrentServer)
            {
                StatusMessage = "Fetching players via Steam Query...";
                OnOnlinePlayersUpdated?.Invoke();
            }
            var onlineList = new List<OnlinePlayerBM>();
            var currentlyOnlineInfo = new Dictionary<string, (DateTime start, string name)>();

            // Try the discovered/learned port first, then fall back to common offsets
            // if the port was not learned from the API (only probing with a 8s timeout each).
            var portCandidates = new List<int> { queryPort };
            bool portWasLearned = _settings.LearnedQueryPorts.ContainsKey(hostKey);
            if (!portWasLearned)
            {
                // API gave nothing; try the most common Rust query ports
                foreach (var fb in new[] { port - 67, 28015, port - 1, port })
                    if (fb > 0 && fb != queryPort) portCandidates.Add(fb);
            }

            List<A2SPlayer>? playersResult = null;
            int successPort = queryPort;
            foreach (var tryPort in portCandidates)
            {
                try
                {
                    playersResult = await A2SClient.QueryPlayersAsync(host, tryPort, 8000);
                    if (playersResult != null)
                    {
                        successPort = tryPort;
                        if (tryPort != queryPort)
                        {
                            // Learned a new port mid-fallback — save it
                            _settings.LearnedQueryPorts[hostKey] = tryPort;
                            SaveDB();
                            Log($"[A2S] Fallback port {tryPort} succeeded for {host}");
                        }
                        break;
                    }
                }
                catch { }
            }
                    
                    int totalFetched = playersResult?.Count ?? 0;
                    int validNames = 0;
                    
                    Log($"[A2S] Fetched {totalFetched} players from query port {queryPort}");

                    if (playersResult != null)
                    {
                        foreach (var player in playersResult)
                        {
                            if (!string.IsNullOrWhiteSpace(player.Name))
                            {
                                validNames++;
                                string bmId = player.Name;
                                int seconds = (int)player.Duration;
                                DateTime actualStart = DateTime.UtcNow.AddSeconds(-seconds);

                                bool isTr = false;
                                lock (_dbLock) isTr = _trackedPlayers.ContainsKey(bmId);

                                onlineList.Add(new OnlinePlayerBM
                                {
                                    BMId = bmId,
                                    Name = player.Name,
                                    SessionStartTimeUtc = actualStart,
                                    Duration = TimeSpan.FromSeconds(Math.Max(0, seconds)),
                                    IsTracked = isTr
                                });
                                currentlyOnlineInfo[bmId] = (actualStart, player.Name);
                            }
                        }
                    }

            if (isCurrentServer)
            {
                if (onlineList.Count == 0)
                {
                    StatusMessage = "No online players found.";
                }
                else
                {
                    StatusMessage = "";
                }

                LastOnlinePlayers = onlineList.OrderByDescending(x => x.Duration).ToList();
            }

            // 3. Update Tracking stats
            await UpdateTrackingStatsAsync(currentlyOnlineInfo, serverName);
        }
        catch (Exception ex)
        {
            throw;
        }
    }

    private static async Task UpdateTrackingStatsAsync(Dictionary<string, (DateTime start, string name)> currentlyOnlineInfo, string serverName)
    {
        bool changed = false;
        var now = DateTime.UtcNow;

        var players = GetTrackedPlayers();
        foreach (var cloneTp in players)
        {
            if (cloneTp.LastServerName != serverName) continue;
            TrackedPlayer tp;
            lock (_dbLock)
            {
                if (!_trackedPlayers.TryGetValue(cloneTp.BMId, out tp)) continue;
            }
            bool isOnline = currentlyOnlineInfo.TryGetValue(tp.BMId, out var info);
            if (!isOnline && tp.BMId != tp.Name)
            {
                isOnline = currentlyOnlineInfo.TryGetValue(tp.Name, out info);
            }
            
            var lastSession = tp.Sessions.LastOrDefault();

            if (isOnline)
            {
                // Update name if it was previously unknown or empty
                if (tp.Name == "Unknown Player" || string.IsNullOrEmpty(tp.Name))
                {
                    tp.Name = info.name;
                    changed = true;
                }

                var actualConnectTime = info.start;
                if (lastSession == null || lastSession.DisconnectTime.HasValue)
                {
                    // Newly connected or we just started tracking/opened the app
                    tp.Sessions.Add(new PlayerSession { ConnectTime = actualConnectTime, DisconnectTime = null });
                    Log($"[SESSION] {tp.Name} ({tp.BMId}) connected at {actualConnectTime:yyyy-MM-dd HH:mm:ss} UTC (detected at {now:HH:mm})");
                    changed = true;
                    if (AnnounceTracking)
                    {
                        var groupStr = string.IsNullOrWhiteSpace(tp.GroupName) ? "" : $" [{tp.GroupName}]";
                        OnTrackingNotification?.Invoke(string.Format(Properties.Resources.AlertTrackingOnline, tp.Name, groupStr), serverName);
                    }
                }
                else
                {
                    // If we have an open session, but the connect time is different (e.g. app was closed and they rejoined)
                    // BattleMetrics session ID would change, but here we track by server session.
                    // If the actualConnectTime is NEWER than our last recorded ConnectTime, they must have reconnected 
                    // while we were closed.
                    if (actualConnectTime > lastSession.ConnectTime.AddMinutes(5))
                    {
                        // They reconnected. Close old session at their last seen or roughly before this connect?
                        // For simplicity, we close the old one at actualConnectTime - 1 second and start new one.
                        lastSession.DisconnectTime = actualConnectTime.AddSeconds(-1);
                        tp.Sessions.Add(new PlayerSession { ConnectTime = actualConnectTime, DisconnectTime = null });
                        Log($"[SESSION] {tp.Name} reconnected (missed disconnect). New session start: {actualConnectTime:yyyy-MM-dd HH:mm:ss} UTC");
                        changed = true;
                    }
                    else if (Math.Abs((lastSession.ConnectTime - actualConnectTime).TotalMinutes) > 1)
                    {
                        // Small correction of start time
                        lastSession.ConnectTime = actualConnectTime;
                        changed = true;
                    }
                }
            }
            else
            {
                if (lastSession != null && !lastSession.DisconnectTime.HasValue)
                {
                    // Newly disconnected. Or did they change their name?
                    var possibleNameChange = currentlyOnlineInfo.FirstOrDefault(kvp => 
                        !players.Any(p => p.BMId == kvp.Key || p.Name == kvp.Key) &&
                        Math.Abs((kvp.Value.start - lastSession.ConnectTime).TotalSeconds) <= 1 &&
                        (now - lastSession.ConnectTime).TotalSeconds > 60);

                    if (possibleNameChange.Key != null)
                    {
                        string oldName = tp.Name;
                        string newName = possibleNameChange.Value.name;
                        Log($"[NAME_CHANGE] {oldName} -> {newName} (Session start matched: {lastSession.ConnectTime:HH:mm:ss} vs {possibleNameChange.Value.start:HH:mm:ss})");
                        
                        if (tp.BMId.Length == 17 && tp.BMId.StartsWith("7656"))
                        {
                            // If it's a SteamID tracked player, just update the Name, keep BMId
                            RenameTrackedPlayer(tp.BMId, newName);
                        }
                        else
                        {
                            MigrateTrackedPlayer(tp.BMId, possibleNameChange.Key, newName);
                        }
                        
                        if (AnnounceTracking)
                        {
                            var groupStr = string.IsNullOrWhiteSpace(tp.GroupName) ? "" : $" [{tp.GroupName}]";
                            OnTrackingNotification?.Invoke(string.Format(Properties.Resources.AlertTrackingRenamed, oldName, groupStr, newName), serverName);
                        }
                        
                        continue; // Skip the disconnect logic
                    }

                    // Newly disconnected. Fetch actual last seen/stop time.
                    var actualDisconnectTime = await FetchLastSeenTimeAsync(tp.BMId);
                    if (actualDisconnectTime == DateTime.MinValue)
                    {
                        actualDisconnectTime = now;
                        Log($"[SESSION] {tp.Name} disconnected. API stop time fetch failed, using fallback: {now:yyyy-MM-dd HH:mm:ss} UTC");
                    }
                    else
                    {
                        Log($"[SESSION] {tp.Name} disconnected at {actualDisconnectTime:yyyy-MM-dd HH:mm:ss} UTC");
                    }
                    
                    lastSession.DisconnectTime = actualDisconnectTime;
                    changed = true;
                    if (AnnounceTracking)
                    {
                        var groupStr = string.IsNullOrWhiteSpace(tp.GroupName) ? "" : $" [{tp.GroupName}]";
                        OnTrackingNotification?.Invoke(string.Format(Properties.Resources.AlertTrackingOffline, tp.Name, groupStr), serverName);
                    }
                }
            }
        }

        if (changed)
        {
            SaveDB();
        }
    }

    private static async Task<DateTime> FetchLastSeenTimeAsync(string bmId)
    {
        return await Task.FromResult(DateTime.UtcNow);
    }
}
