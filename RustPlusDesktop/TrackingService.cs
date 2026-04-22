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
    public string SteamId64 { get; set; } = string.Empty;
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

    public static void TrackPlayer(string bmId, string name, string serverName)
    {
        if (!_trackedPlayers.ContainsKey(bmId))
        {
            _trackedPlayers[bmId] = new TrackedPlayer { BMId = bmId, Name = name, LastServerName = serverName };
        }
        else
        {
            _trackedPlayers[bmId].LastServerName = serverName;
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

    public static string SteamId64
    {
        get => _settings.SteamId64;
        set { _settings.SteamId64 = value; SaveDB(); }
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

    public static async Task<string> FetchPlayerNameAsync(string bmId)
    {
        try
        {
            var json = await _http.GetStringAsync($"https://api.battlemetrics.com/players/{bmId}");
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("name").GetString() ?? "Unknown Player";
        }
        catch { return "Unknown Player"; }
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

    // Provide tracking analysis view - Rich HTML
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
        
        var playersToReport = targetBmId == null 
            ? _trackedPlayers.Values.ToList() 
            : _trackedPlayers.Values.Where(p => p.BMId == targetBmId).ToList();

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
                var totalTime = TimeSpan.Zero;
                var past7Days = TimeSpan.Zero;
                var now = DateTime.UtcNow;
                
                int[] hourActivity = new int[24];
                Dictionary<DateTime, int> dailyActivity = new Dictionary<DateTime, int>();

                foreach (var session in p.Sessions)
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
                var isOnline = p.Sessions.Any() && !p.Sessions.Last().DisconnectTime.HasValue;
                var themeClass = isOnline ? "theme-online" : "theme-offline";

                sb.AppendLine($"<div class='player-card {themeClass}'>");
                sb.AppendLine($"<h2>{p.Name}</h2>");
                
                var statusClass = isOnline ? "badge-online" : "badge-offline";
                var statusText = isOnline ? "Online" : "Offline";
                sb.AppendLine($"<div style='margin-bottom:20px;'><span class='badge {statusClass}'>{statusText}</span></div>");

                sb.AppendLine("<div class='stat-grid'>");
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
            var newOnlineIds = new HashSet<string>();

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
                        if (attr.TryGetProperty("start", out var sProp) && sProp.ValueKind == JsonValueKind.String)
                        {
                            if (DateTimeOffset.TryParse(sProp.GetString(), out var start))
                            {
                                seconds = (int)(DateTimeOffset.UtcNow - start).TotalSeconds;
                            }
                        }

                        onlineList.Add(new OnlinePlayerBM
                        {
                            BMId = bmId,
                            Name = name,
                            Duration = TimeSpan.FromSeconds(Math.Max(0, seconds)),
                            IsTracked = _trackedPlayers.ContainsKey(bmId)
                        });
                        newOnlineIds.Add(bmId);
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
            UpdateTrackingStats(newOnlineIds);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection Error: {ex.Message}";
            OnOnlinePlayersUpdated?.Invoke();
        }
    }

    private static void UpdateTrackingStats(HashSet<string> currentlyOnlineIds)
    {
        bool changed = false;
        var now = DateTime.UtcNow;

        foreach (var tp in _trackedPlayers.Values)
        {
            bool isOnline = currentlyOnlineIds.Contains(tp.BMId);
            var lastSession = tp.Sessions.LastOrDefault();

            if (isOnline)
            {
                if (lastSession == null || lastSession.DisconnectTime.HasValue)
                {
                    // Newly connected
                    tp.Sessions.Add(new PlayerSession { ConnectTime = now, DisconnectTime = null });
                    changed = true;
                }
            }
            else
            {
                if (lastSession != null && !lastSession.DisconnectTime.HasValue)
                {
                    // Newly disconnected
                    lastSession.DisconnectTime = now;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            SaveDB();
        }
    }
}
