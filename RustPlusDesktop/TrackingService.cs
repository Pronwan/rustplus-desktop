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
    public List<PlayerSession> Sessions { get; set; } = new();
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
    public int LastPort { get; set; } = 0;
    public string LastServerName { get; set; } = string.Empty;
    public bool BackgroundTrackingEnabled { get; set; } = false;
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
    public bool AnnounceCargoDocking { get; set; } = false;
    public bool AnnounceCargoEgress { get; set; } = false;
    public bool AnnounceCargoArrival { get; set; } = false;
    public Dictionary<string, int> LearnedDockingDurations { get; set; } = new();
    public Dictionary<string, int> LearnedCargoFullLifeMinutes { get; set; } = new();
    public Dictionary<string, int> LearnedCargoTravelMinutes { get; set; } = new();
    public Dictionary<string, List<HarborInfo>> ServerHarbors { get; set; } = new();
    public Dictionary<string, Dictionary<string, CargoTriggerPoint>> ServerCargoTriggers { get; set; } = new();
    public bool AnnounceSpawnsMaster { get; set; } = false;
    public bool SaveAlertSelection { get; set; } = true;
    public string LastSeenVersion { get; set; } = "";
}


public class PlayerSession
{
    public DateTime ConnectTime { get; set; }
    public DateTime? DisconnectTime { get; set; }
    /// <summary>BM server ID this session is on. Null on legacy entries written before the field existed.</summary>
    public string? ServerId { get; set; }
    /// <summary>"local" for sessions observed via local 2-min poll, "bm" for sessions imported from BattleMetrics.</summary>
    public string Source { get; set; } = "local";
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
        "RustPlusDesk-Ryyott", "tracked_players.json");
    private static readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RustPlusDesk-Ryyott", "tracking_settings.json");
    
    private static Dictionary<string, TrackedPlayer> _trackedPlayers = new();
    private static TrackingSettings _settings = new();
    private static Timer? _trackingTimer;
    private static string? _lastServerHost;
    private static int _lastServerPort;
    private static string? _lastServerName;

    public static event Action? OnOnlinePlayersUpdated;
    public static event Action<string>? OnServerInfoUpdated;
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

            var jsonP = JsonSerializer.Serialize(_trackedPlayers.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
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
                "RustPlusDesk-Ryyott", "tracking_log.txt");
            var dir = Path.GetDirectoryName(logPath);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public static void TrackPlayer(string bmId, string name, string serverName, PlayerSession? initialSession = null)
    {
        TrackedPlayer? player;
        if (!_trackedPlayers.TryGetValue(bmId, out player))
        {
            player = new TrackedPlayer { BMId = bmId, Name = name, LastServerName = serverName };
            _trackedPlayers[bmId] = player;
        }
        else
        {
            player.LastServerName = serverName;
            // Update name if we got a real one
            if (name != "Unknown Player") player.Name = name;
        }

        if (initialSession != null)
        {
            // Only add if we don't have overlapping sessions already
            if (!player.Sessions.Any(s => s.ConnectTime == initialSession.ConnectTime))
            {
                player.Sessions.Add(initialSession);
                player.Sessions = player.Sessions.OrderBy(s => s.ConnectTime).ToList();
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
        if (_trackedPlayers.Remove(bmId))
        {
            SaveDB();
            if (_trackedPlayers.Count == 0)
            {
                StopPolling();
            }
            OnOnlinePlayersUpdated?.Invoke();
        }
    }
    
    public static string? CurrentServerBMId => _foundServerId;

    public static void RenameTrackedPlayer(string bmId, string newName)
    {
        if (_trackedPlayers.TryGetValue(bmId, out var player))
        {
            player.Name = newName;
            SaveDB();
            OnOnlinePlayersUpdated?.Invoke();
        }
    }
    public static List<TrackedPlayer> GetTrackedPlayers() => _trackedPlayers.Values.ToList();
    public static bool IsTracked(string bmId) => _trackedPlayers.ContainsKey(bmId);

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
    public static bool AnnounceSpawnsMaster
    {
        get => _settings.AnnounceSpawnsMaster;
        set { _settings.AnnounceSpawnsMaster = value; SaveDB(); }
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

            string appName = "RustPlusDesk-Ryyott";
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

    public static async Task<string> FetchPlayerNameAsync(string bmId)
    {
        try
        {
            // 1. Try direct player endpoint
            var url = $"https://api.battlemetrics.com/players/{bmId}";
            var response = await _http.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("name").GetString() ?? "Unknown Player";
            }
            
            Log($"[API] FetchPlayerName direct failed for {bmId}: {response.StatusCode}. Trying session fallback...");
            
            // 2. Fallback: Try most recent session to get name
            var sUrl = $"https://api.battlemetrics.com/sessions?filter[players]={bmId}&page[size]=1";
            var sResponse = await _http.GetAsync(sUrl);
            if (sResponse.IsSuccessStatusCode)
            {
                var sJson = await sResponse.Content.ReadAsStringAsync();
                using var sDoc = JsonDocument.Parse(sJson);
                if (sDoc.RootElement.TryGetProperty("data", out var sData) && sData.ValueKind == JsonValueKind.Array && sData.GetArrayLength() > 0)
                {
                    return sData[0].GetProperty("attributes").GetProperty("name").GetString() ?? "Unknown Player";
                }
            }
            
            return "Unknown Player";
        }
        catch (Exception ex) 
        { 
            Log($"[API] Error fetching name for {bmId}: {ex.Message}");
            return "Unknown Player"; 
        }
    }

    public static async Task<DateTime?> FetchPlayerLastSeenAsync(string bmId)
    {
        if (string.IsNullOrEmpty(_foundServerId)) return null;
        try
        {
            var url = $"https://api.battlemetrics.com/players/{bmId}/servers/{_foundServerId}";
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("data", out var data) && 
                data.TryGetProperty("attributes", out var attr))
            {
                if (attr.TryGetProperty("lastSeen", out var stopProp) && stopProp.ValueKind == JsonValueKind.String)
                {
                    if (DateTimeOffset.TryParse(stopProp.GetString(), out var stop))
                    {
                        return stop.UtcDateTime;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    public static void LoadDemoData()
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

        SaveDB();
        OnOnlinePlayersUpdated?.Invoke();
    }

    public static async Task<PlayerSession?> FetchPlayerLastSessionAsync(string bmId)
    {
        if (string.IsNullOrEmpty(_foundServerId)) return null;
        try
        {
            // Try to fetch the most recent session for this player on this server
            var url = $"https://api.battlemetrics.com/sessions?filter[players]={bmId}&filter[servers]={_foundServerId}&page[size]=1";
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
            {
                var sessionObj = data[0];
                var attr = sessionObj.GetProperty("attributes");
                
                DateTime? start = null;
                DateTime? stop = null;

                if (attr.TryGetProperty("start", out var sProp) && sProp.ValueKind == JsonValueKind.String)
                {
                    if (DateTimeOffset.TryParse(sProp.GetString(), out var s)) start = s.UtcDateTime;
                }
                if (attr.TryGetProperty("stop", out var eProp) && eProp.ValueKind == JsonValueKind.String)
                {
                    if (DateTimeOffset.TryParse(eProp.GetString(), out var e)) stop = e.UtcDateTime;
                }

                if (start.HasValue)
                {
                    return new PlayerSession
                    {
                        ConnectTime = start.Value,
                        DisconnectTime = stop,
                        ServerId = _foundServerId,
                        Source = "bm"
                    };
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Pull the player's session history from BattleMetrics (paginated, filtered by server)
    /// and merge it into the local tracking DB. De-dupes against any existing session whose
    /// ConnectTime is within 60 seconds of an incoming one. Returns the count of newly-added
    /// sessions. Caller is responsible for choosing whether to persist via SaveDB().
    /// </summary>
    /// <remarks>
    /// BM's /sessions endpoint auth-gates the filter[servers] parameter for anonymous clients,
    /// so we fetch ALL of the player's recent sessions (no server filter on the wire) and apply
    /// the server filter client-side via relationships.server.data.id. page[size]=10 is the
    /// largest size BM allows anonymously. A heavy player playing on multiple servers can take
    /// 10–30 pages to span 90 days; expect 5–30 s of network round-trips on first open.
    /// </remarks>
    public static async Task<int> BackfillSessionsFromBMAsync(
        string bmId, string serverId, int daysBack = 90, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(bmId) || string.IsNullOrEmpty(serverId)) return 0;

        var cutoff = DateTime.UtcNow.AddDays(-daysBack);
        var imported = new List<PlayerSession>();
        int totalScanned = 0, matched = 0;

        const int pageSize = 10;
        const int maxPages = 60;     // safety ceiling: 600 sessions across all servers

        // Fetch the player's recent sessions across ALL servers; we filter to `serverId` client-side.
        string? url = $"https://api.battlemetrics.com/sessions" +
                      $"?filter[players]={Uri.EscapeDataString(bmId)}" +
                      $"&page[size]={pageSize}";

        int pages = 0;

        while (!string.IsNullOrEmpty(url) && pages < maxPages)
        {
            ct.ThrowIfCancellationRequested();
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Log($"[backfill] HTTP {(int)resp.StatusCode} on page {pages + 1} for {bmId} (URL: {url})");
                if ((int)resp.StatusCode == 401 || (int)resp.StatusCode == 403)
                {
                    Log("[backfill] BM is auth-gating this request. The report will use only locally-collected sessions for this player.");
                }
                break;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                break;

            DateTime? oldestOnPage = null;
            foreach (var item in data.EnumerateArray())
            {
                totalScanned++;
                if (!item.TryGetProperty("attributes", out var attr)) continue;

                DateTime? start = null, stop = null;
                if (attr.TryGetProperty("start", out var sEl) && sEl.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(sEl.GetString(), null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var sv))
                    start = sv.ToUniversalTime();
                if (attr.TryGetProperty("stop", out var stEl) && stEl.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(stEl.GetString(), null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var stv))
                    stop = stv.ToUniversalTime();

                if (!start.HasValue) continue;
                if (oldestOnPage == null || start < oldestOnPage) oldestOnPage = start;

                // Client-side server filter: parse relationships.server.data.id.
                string? sessionServerId = null;
                if (item.TryGetProperty("relationships", out var rels) &&
                    rels.TryGetProperty("server", out var srvRel) &&
                    srvRel.TryGetProperty("data", out var srvData) &&
                    srvData.TryGetProperty("id", out var srvIdEl) &&
                    srvIdEl.ValueKind == JsonValueKind.String)
                {
                    sessionServerId = srvIdEl.GetString();
                }
                if (!string.Equals(sessionServerId, serverId, StringComparison.OrdinalIgnoreCase))
                    continue;

                matched++;
                imported.Add(new PlayerSession
                {
                    ConnectTime = start.Value,
                    DisconnectTime = stop,
                    ServerId = serverId,
                    Source = "bm"
                });
            }

            pages++;

            // Stop if the oldest session on this page is past our cutoff — older pages are even older.
            if (oldestOnPage.HasValue && oldestOnPage.Value < cutoff) break;

            // Follow cursor if present.
            url = null;
            if (root.TryGetProperty("links", out var links) &&
                links.TryGetProperty("next", out var next) &&
                next.ValueKind == JsonValueKind.String)
            {
                url = next.GetString();
            }
        }

        // Trim anything older than the cutoff (last page may include older entries).
        imported = imported.Where(s => s.ConnectTime >= cutoff).ToList();

        // Merge into the tracked-players DB (creating an entry if the player isn't tracked yet).
        if (!_trackedPlayers.TryGetValue(bmId, out var tp))
        {
            tp = new TrackedPlayer { BMId = bmId };
            _trackedPlayers[bmId] = tp;
        }

        int added = 0;
        var existingTimes = tp.Sessions
            .Select(s => s.ConnectTime.ToUniversalTime().Ticks)
            .ToHashSet();
        const long toleranceTicks = TimeSpan.TicksPerSecond * 60; // 60 seconds

        foreach (var s in imported)
        {
            var t = s.ConnectTime.ToUniversalTime().Ticks;
            bool dup = existingTimes.Any(e => Math.Abs(e - t) <= toleranceTicks);
            if (dup) continue;
            tp.Sessions.Add(s);
            existingTimes.Add(t);
            added++;
        }

        if (added > 0)
        {
            // Keep sessions chronologically ordered for any code that scans them.
            tp.Sessions = tp.Sessions.OrderBy(s => s.ConnectTime).ToList();
            try { SaveDB(); } catch { /* tolerant */ }
        }

        Log($"[backfill] {bmId} on server {serverId}: scanned {totalScanned} sessions across all servers, {matched} matched this server, imported {added} new (after dedupe), pages {pages}.");
        return added;
    }

    /// <summary>
    /// Build an ad-hoc HTML report for a single BM ID, fetching their name and last session
    /// from BattleMetrics if we don't already have them in the tracked-players DB. Doesn't persist.
    /// </summary>
    public static async Task<string> GetAnalysisReportForBMIdAsync(string bmId, string fallbackName = "", string? serverIdFilter = null)
    {
        if (string.IsNullOrEmpty(bmId)) return GetAnalysisReport();
        if (_trackedPlayers.ContainsKey(bmId))
            return GetAnalysisReport(bmId, null, serverIdFilter);

        string name;
        try
        {
            name = await FetchPlayerNameAsync(bmId);
            if (string.IsNullOrEmpty(name) || name == "Unknown Player")
                name = string.IsNullOrEmpty(fallbackName) ? "Unknown Player" : fallbackName;
        }
        catch { name = string.IsNullOrEmpty(fallbackName) ? "Unknown Player" : fallbackName; }

        PlayerSession? last = null;
        try { last = await FetchPlayerLastSessionAsync(bmId); } catch { /* tolerant */ }

        var sessions = new List<PlayerSession>();
        if (last != null) sessions.Add(last);

        var adhoc = new TrackedPlayer
        {
            BMId = bmId,
            Name = name,
            LastServerName = LastServer.name ?? string.Empty,
            Sessions = sessions
        };
        return GetAnalysisReport(bmId, new[] { adhoc }, serverIdFilter);
    }

    public static string GetAnalysisReport(string? targetBmId = null, IEnumerable<TrackedPlayer>? adHocPlayers = null, string? serverIdFilter = null)
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
        
        // Combine tracked players with any ad-hoc ones (for untracked-player report views).
        // De-dupe by BMId, preferring tracked entries since they have richer session history.
        IEnumerable<TrackedPlayer> allPlayers = _trackedPlayers.Values;
        if (adHocPlayers != null)
        {
            var trackedIds = new HashSet<string>(_trackedPlayers.Keys);
            allPlayers = allPlayers.Concat(adHocPlayers.Where(p => !trackedIds.Contains(p.BMId)));
        }

        var playersToReport = targetBmId == null
            ? allPlayers.ToList()
            : allPlayers.Where(p => p.BMId == targetBmId).ToList();

        if (!playersToReport.Any())
        {
            sb.AppendLine("<p>No data available for this player. They may have no recent BattleMetrics history.</p>");
        }

        var groupedPlayers = playersToReport.GroupBy(p => string.IsNullOrEmpty(p.LastServerName) ? "Global / Legacy" : p.LastServerName);

        foreach(var group in groupedPlayers)
        {
            sb.AppendLine($"<div class='section-title' style='color:#58a6ff; font-size:16px; margin-top:40px; border-bottom: 2px solid #30363d;'>{group.Key}</div>");
            
            foreach(var p in group)
            {
                var totalTime = TimeSpan.Zero;
                var past7Days = TimeSpan.Zero;
                var now = DateTime.UtcNow;

                // Per-hour MINUTES of play (more accurate than the legacy "++ per hour touched").
                double[] hourMinutes = new double[24];
                Dictionary<DateTime, int> dailyActivity = new Dictionary<DateTime, int>();

                // Filter to the requested server if a filter is in play. Sessions with null ServerId
                // (legacy data, written before the field existed) are kept so old DBs still report.
                IEnumerable<PlayerSession> sessionsForReport = p.Sessions;
                if (!string.IsNullOrEmpty(serverIdFilter))
                {
                    sessionsForReport = p.Sessions.Where(s =>
                        string.IsNullOrEmpty(s.ServerId) ||
                        string.Equals(s.ServerId, serverIdFilter, StringComparison.OrdinalIgnoreCase));
                }
                var filteredSessions = sessionsForReport.ToList();

                foreach (var session in filteredSessions)
                {
                    var end = session.DisconnectTime ?? now;
                    if (end <= session.ConnectTime) continue;
                    var dur = end - session.ConnectTime;
                    totalTime += dur;
                    if (session.ConnectTime > now.AddDays(-7)) past7Days += dur;

                    var date = session.ConnectTime.ToLocalTime().Date;
                    if (!dailyActivity.ContainsKey(date)) dailyActivity[date] = 0;
                    dailyActivity[date] += (int)dur.TotalMinutes;

                    // Distribute the session's duration across local-time hour buckets.
                    var localStart = session.ConnectTime.ToLocalTime();
                    var localEnd = end.ToLocalTime();
                    var iter = localStart;
                    while (iter < localEnd)
                    {
                        var nextHour = new DateTime(iter.Year, iter.Month, iter.Day, iter.Hour, 0, 0).AddHours(1);
                        var sliceEnd = nextHour < localEnd ? nextHour : localEnd;
                        hourMinutes[iter.Hour] += (sliceEnd - iter).TotalMinutes;
                        iter = sliceEnd;
                    }
                }

                double avgSessionMins = filteredSessions.Count > 0 ? totalTime.TotalMinutes / filteredSessions.Count : 0;
                var isOnline = filteredSessions.Count > 0 && !filteredSessions[filteredSessions.Count - 1].DisconnectTime.HasValue;
                var themeClass = isOnline ? "theme-online" : "theme-offline";

                sb.AppendLine($"<div class='player-card {themeClass}'>");
                sb.AppendLine($"<h2>{p.Name}</h2>");
                
                var statusClass = isOnline ? "badge-online" : "badge-offline";
                var statusText = isOnline ? "Online" : "Offline";
                sb.AppendLine($"<div style='margin-bottom:20px;'><span class='badge {statusClass}'>{statusText}</span></div>");

                var lastS = filteredSessions.Count > 0 ? filteredSessions[filteredSessions.Count - 1] : null;
                string lastConnectedStr = lastS != null ? lastS.ConnectTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "Never";
                string lastSeenStr = lastS != null ? (lastS.DisconnectTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "Active Now") : "Never";

                sb.AppendLine("<div class='stat-grid'>");
                sb.AppendLine("<div class='stat-item'><div class='stat-label'>Last Connected</div><div class='stat-value'>" + lastConnectedStr + "</div></div>");
                sb.AppendLine("<div class='stat-item'><div class='stat-label'>Last Seen</div><div class='stat-value'>" + lastSeenStr + "</div></div>");
                sb.AppendLine("<div class='stat-item'><div class='stat-label'>Total Tracked Time</div><div class='stat-value'>" + $"{(int)totalTime.TotalHours}h {totalTime.Minutes}m" + "</div></div>");
                sb.AppendLine("<div class='stat-item'><div class='stat-label'>Last 7 Days</div><div class='stat-value'>" + $"{(int)past7Days.TotalHours}h {past7Days.Minutes}m" + "</div></div>");
                sb.AppendLine("<div class='stat-item'><div class='stat-label'>Session Count</div><div class='stat-value'>" + filteredSessions.Count + "</div></div>");
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
            double maxHm = hourMinutes.Length > 0 ? hourMinutes.Max() : 0;
            double meanHm = hourMinutes.Sum() / 24.0;
            for(int i=0; i<24; i++)
            {
                double hVal = maxHm > 0 ? hourMinutes[i] / maxHm * 100 : 5;
                string activeClass = hourMinutes[i] > (maxHm * 0.4) ? "active" : "";
                sb.AppendLine($"<div class='hour-bar {activeClass}' style='height:{hVal:0.##}%' title='{i:00}:00 - {(int)hourMinutes[i]} min'></div>");
            }
            sb.AppendLine("</div>");
            sb.AppendLine("<div class='hour-labels'>");
            sb.AppendLine("<span>00:00</span><span>04:00</span><span>08:00</span><span>12:00</span><span>16:00</span><span>20:00</span><span>23:00</span>");
            sb.AppendLine("</div>");
            sb.AppendLine("</div>");

            // ─── Predictions ─────────────────────────────────────────────────
            // Most likely to play: 3-hour window with the highest sum of minutes (wraps midnight).
            int playStart = 0;
            double bestPlaySum = -1;
            const int playWindow = 3;
            for (int h = 0; h < 24; h++)
            {
                double sum = 0;
                for (int k = 0; k < playWindow; k++) sum += hourMinutes[(h + k) % 24];
                if (sum > bestPlaySum) { bestPlaySum = sum; playStart = h; }
            }
            int playEnd = (playStart + playWindow) % 24;

            // Most likely to sleep: longest contiguous run of hours with minutes <= mean*0.25.
            // Try every starting hour and walk forward up to 24 to find the longest run; this naturally
            // handles wrap-around (e.g. sleep window 22:00 → 06:00).
            double sleepThreshold = meanHm * 0.25;
            int sleepStart = -1, sleepLen = 0;
            for (int start = 0; start < 24; start++)
            {
                int len = 0;
                for (int k = 0; k < 24; k++)
                {
                    int h = (start + k) % 24;
                    if (hourMinutes[h] <= sleepThreshold) len++;
                    else break;
                }
                if (len > sleepLen) { sleepLen = len; sleepStart = start; }
            }

            // Confidence
            int sessionCount = filteredSessions.Count;
            int distinctDays = dailyActivity.Count;
            double totalHours = totalTime.TotalHours;
            string confidence;
            if (sessionCount >= 30 && distinctDays >= 14 && totalHours >= 20) confidence = "HIGH";
            else if (sessionCount >= 9 && distinctDays >= 4) confidence = "MEDIUM";
            else confidence = "LOW";

            string playStr = bestPlaySum > 0 ? $"{playStart:00}:00 - {playEnd:00}:00" : "—";
            string sleepStr;
            if (sleepLen >= 24) sleepStr = "always quiet (no signal)";
            else if (sleepLen <= 0) sleepStr = "—";
            else
            {
                int sleepEnd = (sleepStart + sleepLen) % 24;
                sleepStr = $"{sleepStart:00}:00 - {sleepEnd:00}:00";
            }

            sb.AppendLine("<div class='insight-box'>");
            sb.AppendLine("<div class='insight-item'><span class='insight-icon'>⚡</span> Most likely to play: <b>" + playStr + "</b></div>");
            sb.AppendLine("<div class='insight-item'><span class='insight-icon'>💤</span> Most likely to sleep: <b>" + sleepStr + "</b></div>");

            if (confidence == "LOW")
            {
                sb.AppendLine("<div class='warning'><b>Data Confidence: LOW</b><br/>Few sessions or short observation window. Predictions are early estimates — more activity will tighten them.</div>");
            }
            else
            {
                sb.AppendLine($"<div style='color: #8b949e; font-size: 11px; margin-top: 10px;'>Forecast based on {sessionCount} sessions across {distinctDays} day{(distinctDays == 1 ? "" : "s")} · Confidence: <b>{confidence}</b></div>");
            }
            sb.AppendLine("</div>");

            sb.AppendLine("</div>");
        }
    }
        
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string? _foundServerId;

    public static void StartPolling(string host, int port, string name)
    {
        _lastServerHost = host;
        _lastServerPort = port;
        _lastServerName = name;
        _foundServerId = null; // Reset to force new lookup

        _settings.LastHost = host;
        _settings.LastPort = port;
        _settings.LastServerName = name;
        SaveDB();

        // Poll every 2 minutes only if we have players
        _trackingTimer?.Dispose();
        if (_trackedPlayers.Count > 0)
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

    private static async Task PollOnceAsync()
    {
        if (string.IsNullOrEmpty(_lastServerHost)) return;

        try
        {
            // 1. Get BM Server ID
            if (string.IsNullOrEmpty(_foundServerId))
            {
                StatusMessage = "Looking up server...";

                // --- SCHRITT A: Suche über IP-Adresse (Port ignorieren, da oft unterschiedlich) ---
                var searchUrlAddr = $"https://api.battlemetrics.com/servers?filter[address]={Uri.EscapeDataString(_lastServerHost)}&filter[game]=rust";

                using var responseAddr = await _http.GetAsync(searchUrlAddr);
                if (responseAddr.IsSuccessStatusCode)
                {
                    var resAddr = await responseAddr.Content.ReadAsStringAsync();
                    using var docAddr = JsonDocument.Parse(resAddr);
                    var dataArr = docAddr.RootElement.GetProperty("data");

                    foreach (var serverObj in dataArr.EnumerateArray())
                    {
                        var attr = serverObj.GetProperty("attributes");
                        var foundIp = attr.GetProperty("ip").GetString();
                        var foundName = attr.GetProperty("name").GetString() ?? "";

                        // CRITICAL: Wir nehmen den Server nur, wenn die IP EXAKT stimmt
                        if (foundIp == _lastServerHost)
                        {
                            if (attr.TryGetProperty("details", out var details) && details.TryGetProperty("rust_description", out var desc))
                            {
                                OnServerInfoUpdated?.Invoke(desc.GetString() ?? "");
                            }

                            // Wenn wir mehrere Server auf einer IP haben (Shared Hosting), 
                            // nehmen wir den, dessen Name am besten passt.
                            if (string.IsNullOrEmpty(_lastServerName) || foundName.Contains(_lastServerName, StringComparison.OrdinalIgnoreCase))
                            {
                                _foundServerId = serverObj.GetProperty("id").GetString();
                                break;
                            }
                        }
                    }
                }

                // --- SCHRITT B: Fallback über Namen (falls IP bei Battlemetrics anders gelistet ist) ---
                if (string.IsNullOrEmpty(_foundServerId) && !string.IsNullOrEmpty(_lastServerName))
                {
                    var searchUrlName = $"https://api.battlemetrics.com/servers?filter[game]=rust&filter[search]={Uri.EscapeDataString(_lastServerName)}";
                    using var responseName = await _http.GetAsync(searchUrlName);
                    if (responseName.IsSuccessStatusCode)
                    {
                        var resName = await responseName.Content.ReadAsStringAsync();
                        using var docName = JsonDocument.Parse(resName);
                        var dataArr = docName.RootElement.GetProperty("data");

                        foreach (var serverObj in dataArr.EnumerateArray())
                        {
                            var attr = serverObj.GetProperty("attributes");
                            var foundIp = attr.TryGetProperty("ip", out var vIp) ? vIp.GetString() : "";
                            var foundName = attr.GetProperty("name").GetString() ?? "";

                            // Wenn der Name exakt passt, nehmen wir die ID, auch wenn die IP leicht abweicht 
                            // (manche Server haben unterschiedliche IPs für Game und Websocket)
                            if (foundName.Equals(_lastServerName, StringComparison.OrdinalIgnoreCase))
                            {
                                _foundServerId = serverObj.GetProperty("id").GetString();
                                break;
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(_foundServerId))
            {
                StatusMessage = $"Server not found on Battlemetrics ({_lastServerHost}:{_lastServerPort})";
                OnOnlinePlayersUpdated?.Invoke();
                return;
            }

            // 2. Get Players using the ID we found
            StatusMessage = "Fetching players...";
            var reqUrl = $"https://api.battlemetrics.com/servers/{_foundServerId}?include=session";
            using var responsePlayers = await _http.GetAsync(reqUrl);
            if (!responsePlayers.IsSuccessStatusCode)
            {
                StatusMessage = $"Fetch Error: {(int)responsePlayers.StatusCode} {responsePlayers.ReasonPhrase}";
                OnOnlinePlayersUpdated?.Invoke();
                return;
            }

            var pRes = await responsePlayers.Content.ReadAsStringAsync();
            using var pDoc = JsonDocument.Parse(pRes);

            if (pDoc.RootElement.TryGetProperty("data", out var serverData))
            {
                var attr = serverData.GetProperty("attributes");
                if (attr.TryGetProperty("details", out var details) && details.TryGetProperty("rust_description", out var desc))
                {
                    OnServerInfoUpdated?.Invoke(desc.GetString() ?? "");
                }
            }
            
            var onlineList = new List<OnlinePlayerBM>();
            var currentlyOnlineInfo = new Dictionary<string, (DateTime start, string name)>();

            if (pDoc.RootElement.TryGetProperty("included", out var included))
            {
                foreach (var inc in included.EnumerateArray())
                {
                    string type = inc.TryGetProperty("type", out var tProp) ? tProp.GetString() ?? "" : "";
                    if (type == "session")
                    {
                        var attr = inc.GetProperty("attributes");
                        var name = attr.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "Unknown" : "Unknown";
                        var bmId = "";
                        
                        if (inc.TryGetProperty("relationships", out var rel) && 
                            rel.TryGetProperty("player", out var pRel) &&
                            pRel.TryGetProperty("data", out var pData))
                        {
                            bmId = pData.GetProperty("id").GetString() ?? "";
                        }
                        
                        if (string.IsNullOrEmpty(bmId)) continue;

                        int seconds = 0;
                        DateTime actualStart = DateTime.UtcNow;
                        if (attr.TryGetProperty("start", out var sProp) && sProp.ValueKind == JsonValueKind.String)
                        {
                            if (DateTimeOffset.TryParse(sProp.GetString(), out var start))
                            {
                                actualStart = start.UtcDateTime;
                                seconds = (int)(DateTimeOffset.UtcNow - start).TotalSeconds;
                            }
                        }

                        onlineList.Add(new OnlinePlayerBM
                        {
                            BMId = bmId,
                            Name = name,
                            SessionStartTimeUtc = actualStart,
                            Duration = TimeSpan.FromSeconds(Math.Max(0, seconds)),
                            IsTracked = _trackedPlayers.ContainsKey(bmId)
                        });
                        currentlyOnlineInfo[bmId] = (actualStart, name);
                    }
                }
            }

            if (onlineList.Count == 0)
            {
                StatusMessage = "No online players found on Battlemetrics.";
            }
            else
            {
                StatusMessage = "";
            }

            LastOnlinePlayers = onlineList.OrderByDescending(x => x.Duration).ToList();
            LastPullTime = DateTime.Now;
            OnOnlinePlayersUpdated?.Invoke();

            // 3. Update Tracking stats
            await UpdateTrackingStatsAsync(currentlyOnlineInfo);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection Error: {ex.Message}";
            OnOnlinePlayersUpdated?.Invoke();
        }
    }

    private static async Task UpdateTrackingStatsAsync(Dictionary<string, (DateTime start, string name)> currentlyOnlineInfo)
    {
        bool changed = false;
        var now = DateTime.UtcNow;

        foreach (var tp in _trackedPlayers.Values)
        {
            bool isOnline = currentlyOnlineInfo.TryGetValue(tp.BMId, out var info);
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
                    tp.Sessions.Add(new PlayerSession { ConnectTime = actualConnectTime, DisconnectTime = null, ServerId = _foundServerId, Source = "local" });
                    Log($"[SESSION] {tp.Name} ({tp.BMId}) connected at {actualConnectTime:yyyy-MM-dd HH:mm:ss} UTC (detected at {now:HH:mm})");
                    changed = true;
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
                        tp.Sessions.Add(new PlayerSession { ConnectTime = actualConnectTime, DisconnectTime = null, ServerId = _foundServerId, Source = "local" });
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
        if (string.IsNullOrEmpty(_foundServerId)) return DateTime.MinValue;

        try
        {
            // Fetch server-specific player information (free endpoint)
            var url = $"https://api.battlemetrics.com/players/{bmId}/servers/{_foundServerId}";
            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("data", out var data) && 
                data.TryGetProperty("attributes", out var attr))
            {
                if (attr.TryGetProperty("lastSeen", out var stopProp) && stopProp.ValueKind == JsonValueKind.String)
                {
                    if (DateTimeOffset.TryParse(stopProp.GetString(), out var stop))
                    {
                        return stop.UtcDateTime;
                    }
                }
            }
        }
        catch { }
        return DateTime.MinValue;
    }
}
