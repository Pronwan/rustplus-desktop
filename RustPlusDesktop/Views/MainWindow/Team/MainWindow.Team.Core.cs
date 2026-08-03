using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private System.Windows.Threading.DispatcherTimer? _teamTimer;
    private System.Windows.Threading.DispatcherTimer? _afkTimer;
    public ObservableCollection<TeamMemberVM> TeamMembers { get; } = new();

    private readonly Dictionary<ulong, ImageSource> _avatarCache = new();

    // Death log: detects team-member deaths across successive team-info snapshots.
    private readonly RustPlusDesk.Services.Deaths.DeathTracker _deathTracker = new();
    private string? _deathTrackerServerKey;

    private RustPlusClientReal? _real => _rust as RustPlusClientReal;

    public sealed class TeamMemberVM : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public int MissingCount { get; set; }
        public ulong SteamId { get; init; }
        private bool _afkAlertSent;
        public bool HasReturnedFromAfk { get; set; }
        public TimeSpan ReturnedAfkDuration { get; set; }

        private string _name = "(player)";
        public string Name
        {
            get => _name;
            set 
            { 
                if (_name == value) return; 
                _name = value; 
                OnChanged(nameof(Name)); 
                OnChanged(nameof(DisplayName)); 
            }
        }

        private bool _abbreviate;
        public bool Abbreviate
        {
            get => _abbreviate;
            set
            {
                if (_abbreviate == value) return;
                _abbreviate = value;
                OnChanged(nameof(Abbreviate));
                OnChanged(nameof(DisplayName));
                OnChanged(nameof(DisplaySteamId));
            }
        }

        public string DisplayName
        {
            get
            {
                if (!Abbreviate || string.IsNullOrWhiteSpace(Name)) return Name;
                return Name.Length > 0 ? Name.Substring(0, 1) + "..." : Name;
            }
        }

        public string DisplaySteamId
        {
            get
            {
                var s = SteamId.ToString();
                if (!Abbreviate) return s;
                if (s.Length <= 3) return s + "...";
                return s.Substring(0, 3) + "...";
            }
        }

        private bool _isLeader;
        public bool IsLeader
        {
            get => _isLeader;
            set { if (_isLeader == value) return; _isLeader = value; OnChanged(nameof(IsLeader)); }
        }

        private bool _isOnline;
        public bool IsOnline
        {
            get => _isOnline;
            set
            {
                if (_isOnline == value) return;
                _isOnline = value;
                OnChanged(nameof(IsOnline));
                OnChanged(nameof(IsOnlineAndAlive));
            }
        }

        private bool _isDead;
        public bool IsDead
        {
            get => _isDead;
            set
            {
                if (_isDead == value) return;
                _isDead = value;
                OnChanged(nameof(IsDead));
                OnChanged(nameof(IsOnlineAndAlive));
            }
        }

        public bool IsOnlineAndAlive => IsOnline && !IsDead;

        public double? X { get; set; }
        public double? Y { get; set; }

        private DateTime _lastMoveTime = DateTime.UtcNow;
        public DateTime LastMoveTime => _lastMoveTime;

        private bool _isAfk;
        public bool IsAfk
        {
            get => _isAfk;
            set { if (_isAfk == value) return; _isAfk = value; OnChanged(nameof(IsAfk)); }
        }

        private string _afkText = string.Empty;
        public string AfkText
        {
            get => _afkText;
            set { if (_afkText == value) return; _afkText = value; OnChanged(nameof(AfkText)); }
        }

        public void SetPosition(double? x, double? y)
        {
            if (x == null || y == null)
            {
                X = x;
                Y = y;
                return;
            }
            if (X != null && Y != null)
            {
                double dx = X.Value - x.Value;
                double dy = Y.Value - y.Value;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist > 0.05)
                {
                    if (_afkAlertSent)
                    {
                        HasReturnedFromAfk = true;
                        ReturnedAfkDuration = DateTime.UtcNow - _lastMoveTime;
                        _afkAlertSent = false;
                    }
                    _lastMoveTime = DateTime.UtcNow;
                }
            }
            else
            {
                _lastMoveTime = DateTime.UtcNow;
            }
            X = x;
            Y = y;
        }

        public bool UpdateAfkState(DateTime now, int alertThresholdMinutes)
        {
            if (!IsOnline || IsDead)
            {
                _lastMoveTime = now;
                IsAfk = false;
                AfkText = string.Empty;
                _afkAlertSent = false;
                HasReturnedFromAfk = false;
                return false;
            }

            var elapsed = now - _lastMoveTime;
            if (elapsed.TotalMinutes >= 5)
            {
                IsAfk = true;
                int totalSecs = (int)elapsed.TotalSeconds;
                int mins = totalSecs / 60;
                int secs = totalSecs % 60;
                AfkText = $"AFK: {mins}:{secs:D2}";
            }
            else
            {
                IsAfk = false;
                AfkText = string.Empty;
            }

            if (elapsed.TotalMinutes >= alertThresholdMinutes)
            {
                if (!_afkAlertSent)
                {
                    _afkAlertSent = true;
                    return true;
                }
            }
            else
            {
                _afkAlertSent = false;
            }

            return false;
        }

        private ImageSource? _avatar;
        public ImageSource? Avatar
        {
            get => _avatar;
            set { if (_avatar == value) return; _avatar = value; OnChanged(nameof(Avatar)); }
        }

        private bool _showMarkers = true;
        public bool ShowMarkers
        {
            get => _showMarkers;
            set { if (_showMarkers == value) return; _showMarkers = value; OnChanged(nameof(ShowMarkers)); }
        }
    }

    private readonly Dictionary<ulong, (double x, double y, string name)> _lastPlayersBySid = new();
    private readonly Dictionary<ulong, (bool online, bool dead)> _lastPresence = new();
    private ulong _mySteamId => (ulong.TryParse(_vm?.SteamId64, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0UL);

    private readonly Dictionary<ulong, string> _steamNames = new();
    private DateTime _lastTeamRefresh = DateTime.MinValue;
    private string? _lastCloudPresenceSignature;
    private DateTime _lastPresenceUploadTime = DateTime.MinValue;
    private bool _hasCriticalPresenceChange;

    private void StartTeamPolling()
    {
        _teamConnectionSessionId++;
        if (_teamTimer != null) return;
        _teamTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _teamTimer.Tick += TeamTimer_Tick;
        // Stagger start by 3s to avoid timer burst at connect time
        _ = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ => Dispatcher.Invoke(() => _teamTimer?.Start()));

        _afkTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _afkTimer.Tick += AfkTimer_Tick;
        _afkTimer.Start();
    }

    private void StopTeamPolling()
    {
        NotifyTeamFeatureServerDisconnected(_teamConnectionSessionId);

        var t = _teamTimer;
        if (t != null)
        {
            t.Tick -= TeamTimer_Tick;
            t.Stop();
            _teamTimer = null;
        }

        var at = _afkTimer;
        if (at != null)
        {
            at.Tick -= AfkTimer_Tick;
            at.Stop();
            _afkTimer = null;
        }

        _lastCloudPresenceSignature = null;
        ResetTeamFeatureMasterSyncState();
        Dispatcher.Invoke(() => ClearTeamMapNotes());
    }

    private void AfkTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        foreach (var m in TeamMembers)
        {
            if (m.UpdateAfkState(now, TrackingService.AfkAlertMinutes))
            {
                if (_announceSpawns && TrackingService.AnnouncePlayerAfk)
                {
                    string dispName = GetDisplayPlayerName(m.Name);
                    string chatText = AlertTemplateService.GetFormattedAlert("AlertPlayerAfk", dispName, TrackingService.AfkAlertMinutes);
                    string discordText = $"💤 {chatText}";
                    _ = SendTeamChatSafeAsync(chatText, discordText: discordText);
                }
            }

            if (m.HasReturnedFromAfk)
            {
                m.HasReturnedFromAfk = false;
                if (_announceSpawns && TrackingService.AnnouncePlayerAfkReturn)
                {
                    string dispName = GetDisplayPlayerName(m.Name);
                    string durationStr = $"{(int)m.ReturnedAfkDuration.TotalHours:D2}:{m.ReturnedAfkDuration.Minutes:D2}";
                    string chatText = AlertTemplateService.GetFormattedAlert("AlertPlayerAfkReturn", dispName, durationStr);
                    string discordText = $"🏃 {chatText}";
                    _ = SendTeamChatSafeAsync(chatText, discordText: discordText);
                }
            }
        }
    }

    private int _teamPollBusy = 0;

    private async void TeamTimer_Tick(object? sender, EventArgs e)
    {
        if (System.Threading.Interlocked.Exchange(ref _teamPollBusy, 1) == 1) return;
        try
        {
            await LoadTeamAsync();
            await EvaluateDeviceAutomationAsync();

            if (DateTime.UtcNow - _lastClanPoll > TimeSpan.FromSeconds(15))
            {
                _lastClanPoll = DateTime.UtcNow;
                await LoadClanAsync();
            }
        }
        finally { System.Threading.Interlocked.Exchange(ref _teamPollBusy, 0); }
        CenterMiniMapOnPlayer();
    }

    private async Task EnsureAvatarAsync(TeamMemberVM vm)
    {
        if (!_avatarLoading.Add(vm.SteamId)) return;
        try
        {
            await LoadAvatarAsync(vm).ConfigureAwait(false);

            if (vm.Avatar != null)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    foreach (var kv in _dynEls.ToList())
                    {
                        if (kv.Value is FrameworkElement fe &&
                            fe.Tag is PlayerMarkerTag t &&
                            t.SteamId == vm.SteamId)
                        {
                            var el = fe;
                            UpdatePlayerMarker(ref el, kv.Key, vm.SteamId, vm.Name, vm.IsOnline, vm.IsDead);
                            ApplyCurrentOverlayScale(el);
                        }
                    }

                    if (vm.SteamId == _mySteamId)
                    {
                        _vm.MyAvatar = vm.Avatar;
                    }
                    RedrawDeathPins();
                });

                _avatarNextTry.Remove(vm.SteamId);
            }
            else
            {
                _avatarNextTry[vm.SteamId] = DateTime.UtcNow + AvatarRetryInterval;
            }
        }
        catch
        {
            _avatarNextTry[vm.SteamId] = DateTime.UtcNow + AvatarRetryInterval;
        }
        finally
        {
            _avatarLoading.Remove(vm.SteamId);
        }
    }

    // Feed each team-info snapshot to the death tracker and record what it finds:
    // always to the local log, and to the shared cloud log for premium accounts.
    private async Task ProcessDeathsAsync(RustPlusClientReal.TeamInfo team)
    {
        var serverKey = GetServerKey();
        if (string.IsNullOrEmpty(serverKey))
            return;

        // Switching servers starts a fresh baseline so old state can't leak across.
        if (serverKey != _deathTrackerServerKey)
        {
            _deathTracker.Reset();
            _deathTrackerServerKey = serverKey;
        }

        // Monuments compare in world space; bases are resolved in overlay-pixel
        // space (ResolveBaseAt); grid reuses the app's shared math.
        var classifier = new RustPlusDesk.Services.Deaths.DeathLocationClassifier(
            BuildMonumentZones(), (x, y) => ResolveBaseAt(x, y), (x, y) => GetGridLabel(x, y));

        foreach (var death in _deathTracker.Observe(team, classifier))
        {
            try
            {
                await RustPlusDesk.Services.Deaths.DeathReporter.ReportAsync(death, serverKey);
            }
            catch
            {
                // Reporting must never break the team refresh.
            }
        }
    }

    // Radius (world units) around a base marker that still counts as "at base".
    private const double BaseRadiusWorld = 75.0;

    // Resolve whether a death happened at one of my base markers. Base icons are
    // overlay elements in pixel space, so instead of inverting the map transform
    // we project the death's world position into pixels with WorldToImagePx and
    // compare there. Returns the base's note/label, or null when not at a base.
    private string? ResolveBaseAt(double worldX, double worldY)
    {
        var data = BuildCurrentOverlaySaveDataForMe();
        if (data.Icons.Count == 0)
            return null;

        var deathPx = WorldToImagePx(worldX, worldY);
        // Convert the world radius to pixels at the current map scale.
        var edgePx = WorldToImagePx(worldX + BaseRadiusWorld, worldY);
        double radiusPx = Math.Abs(edgePx.X - deathPx.X);
        if (radiusPx <= 0)
            return null;

        string? best = null;
        double bestDistance = double.MaxValue;

        foreach (var icon in data.Icons)
        {
            if (!RustPlusDesk.Services.Data.OverlayDataModule.IsBaseIconPath(icon.IconPath))
                continue;

            double cx = icon.X + (icon.Width / 2.0);
            double cy = icon.Y + (icon.Height / 2.0);
            double dx = deathPx.X - cx;
            double dy = deathPx.Y - cy;
            double distance = Math.Sqrt((dx * dx) + (dy * dy));
            if (distance <= radiusPx && distance < bestDistance)
            {
                bestDistance = distance;
                best = string.IsNullOrWhiteSpace(icon.Note) ? "Base" : icon.Note;
            }
        }

        return best;
    }

    // Monuments come from the map (GetMapWithMonumentsAsync) in world coordinates,
    // the same space as death positions, so they compare directly. The radius is
    // generous per monument so "died approaching the monument" still counts.
    private IReadOnlyList<RustPlusDesk.Services.Deaths.DeathZone> BuildMonumentZones()
    {
        var zones = new List<RustPlusDesk.Services.Deaths.DeathZone>(_monData.Count);
        foreach (var (x, y, name) in _monData)
        {
            // Canon strips the raw i18n token (e.g. "fishing_village_display_name")
            // down to a clean lowercase name; title-case it for display.
            var canon = Canon(name);
            if (string.IsNullOrWhiteSpace(canon))
                continue;

            var display = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(canon);
            zones.Add(new RustPlusDesk.Services.Deaths.DeathZone(x, y, MonumentRadiusFor(canon), display));
        }

        return zones;
    }

    // Approximate monument footprints (world units). Big monuments get a wider
    // radius; everything else uses a generous default of ~one grid cell.
    private static double MonumentRadiusFor(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("launch")) return 250.0;
        if (n.Contains("airfield")) return 200.0;
        if (n.Contains("power plant") || n.Contains("water treatment") ||
            n.Contains("train yard") || n.Contains("military base") || n.Contains("arctic"))
            return 180.0;
        if (n.Contains("harbor") || n.Contains("harbour")) return 160.0;
        if (n.Contains("oil rig")) return 120.0;
        return 130.0;
    }

    private async Task LoadTeamAsync()
    {
        if (_real is null) return;

        try
        {
            var team = await _real.GetTeamInfoAsync();
            if (team is null) return;

            _lastTeamInfo = team;

            // Detect + record any team-member deaths in this snapshot (fire-and-forget).
            _ = ProcessDeathsAsync(team);

            var leaderId = team.LeaderSteamId;
            foreach (var m in TeamMembers) m.MissingCount++;

            var avatarTasks = new List<Task>();
            foreach (var m in team.Members)
            {
                var sid = m.SteamId;
                if (sid == 0) continue;

                var vm = TeamMembers.FirstOrDefault(t => t.SteamId == sid);
                if (vm == null)
                {
                    vm = new TeamMemberVM { SteamId = sid, Abbreviate = _abbreviateNames };
                    vm.PropertyChanged += TeamMember_PropertyChanged;
                    TeamMembers.Add(vm);
                    _hasCriticalPresenceChange = true;
                }
                else
                {
                    vm.PropertyChanged -= TeamMember_PropertyChanged;
                    vm.PropertyChanged += TeamMember_PropertyChanged;
                }

                if (vm.Avatar == null)
                {
                    avatarTasks.Add(LoadAvatarAsync(vm));
                    if (CanTryAvatar(sid))
                    {
                        avatarTasks.Add(EnsureAvatarAsync(vm));
                    }
                }

                vm.MissingCount = 0;

                var hadPrev = _lastPresence.TryGetValue(sid, out var prev);

                vm.Name = string.IsNullOrWhiteSpace(m.Name) ? "(player)" : m.Name!;
                vm.IsLeader = leaderId != 0 && sid == leaderId;
                vm.IsOnline = m.Online;
                vm.IsDead = m.Dead;
                vm.SetPosition(m.X, m.Y);

                var now = (m.Online, m.Dead);
                _lastPresence[sid] = now;

                if (sid == _mySteamId)
                {
                    _vm.MyAvatar = vm.Avatar;
                }

                if (hadPrev && prev != now)
                {
                    _hasCriticalPresenceChange = true;
                    _ = AnnouncePresenceChangeAsync(vm, prev, now);
                }
            }

            for (int i = TeamMembers.Count - 1; i >= 0; i--)
                if (TeamMembers[i].MissingCount > 2)
                {
                    TeamMembers[i].PropertyChanged -= TeamMember_PropertyChanged;
                    TeamMembers.RemoveAt(i);
                    _hasCriticalPresenceChange = true;
                }

            if (_vm.FollowingSteamId.HasValue && !TeamMembers.Any(t => t.SteamId == _vm.FollowingSteamId.Value))
            {
                Dispatcher.Invoke(() => StopTracking());
            }
            else if (!_vm.FollowingSteamId.HasValue && !string.IsNullOrEmpty(GetServerKey()) && 
                     Services.TrackingService.Settings.ServerFollowingSteamId.TryGetValue(GetServerKey(), out var savedSteamId))
            {
                var member = TeamMembers.FirstOrDefault(t => t.SteamId == savedSteamId);
                if (member != null)
                {
                    _vm.FollowingSteamId = savedSteamId;
                    _vm.FollowingPlayerName = member.Name;
                    _vm.FollowingPlayerAvatar = member.Avatar;
                }
            }

            // Cleanup subscriptions of players who left the team on the UI thread
            var currentTeamIds = TeamMembers.Select(tm => tm.SteamId).ToHashSet();
            await Dispatcher.InvokeAsync(() =>
            {
                var toRemoveSubs = _visibleOverlayOwners.Where(id => id != _mySteamId && !currentTeamIds.Contains(id)).ToList();
                if (toRemoveSubs.Count > 0)
                {
                    foreach (var id in toRemoveSubs)
                    {
                        _visibleOverlayOwners.Remove(id);
                        _teammatePollStates.Remove(id);
                        if (_playerOverlayElements.TryGetValue(id, out var listToHide))
                        {
                            foreach (var fe in listToHide)
                                Overlay.Children.Remove(fe);
                            _playerOverlayElements.Remove(id);
                        }
                    }
                    RebuildOverlayTeamBar();
                    UpdateSubscriptionDock();
                    UpdateSavedSubscriptionsInProfile();
                }
            });

            var cloudTeamMembers = TeamMembers.Select(t => new RustPlusDesk.Services.Auth.SupabaseAuthManager.CloudTeamMemberDto
                {
                    SteamId = t.SteamId.ToString(),
                    Name = t.Name,
                    IsOnline = t.IsOnline,
                    IsDead = t.IsDead,
                    IsLeader = t.IsLeader
                }).ToList();

            var serverKey = GetServerKey();
            var serverName = _vm.Selected?.Name;
            var cloudPresenceSignature = BuildCloudPresenceSignature(serverKey, serverName, cloudTeamMembers);
            var timeSinceLast = DateTime.UtcNow - _lastPresenceUploadTime;
            bool forcePeriodicUpload = timeSinceLast >= CloudTrafficPolicy.PresenceInterval(WindowState == WindowState.Minimized);
            if (cloudPresenceSignature != _lastCloudPresenceSignature || forcePeriodicUpload)
            {
                if (_hasCriticalPresenceChange || forcePeriodicUpload || timeSinceLast.TotalSeconds >= 15)
                {
                    _lastCloudPresenceSignature = cloudPresenceSignature;
                    _lastPresenceUploadTime = DateTime.UtcNow;
                    _hasCriticalPresenceChange = false;
                    _ = RustPlusDesk.Services.Auth.SupabaseAuthManager.UpdatePresenceAsync(
                        serverKey,
                        serverName,
                        cloudTeamMembers);
                }
            }

            if (ShouldSyncTeamFeatureMasterForCurrentState(cloudPresenceSignature))
                _ = SyncTeamFeatureMasterAsync();

            if (avatarTasks.Count > 0)
            {
                try { await Task.WhenAll(avatarTasks); } catch { }
            }

            await Dispatcher.InvokeAsync(() =>
            {
                RedrawTeamMapNotes(team);
            });
        }
        catch (Exception ex)
        {
            AppendLog("[team] " + ex.Message);
        }

        await Dispatcher.InvokeAsync(() =>
        {
            RedrawDeathPins();
        });

        if (_overlayToolsVisible)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                RebuildOverlayTeamBar();
            });
        }
    }

    private static string BuildCloudPresenceSignature(
        string? serverKey,
        string? serverName,
        IReadOnlyCollection<RustPlusDesk.Services.Auth.SupabaseAuthManager.CloudTeamMemberDto> teamMembers)
    {
        var team = string.Join(";",
            teamMembers
                .OrderBy(t => t.SteamId, StringComparer.Ordinal)
                .Select(t => string.Join("|",
                    t.SteamId,
                    t.Name ?? "",
                    t.IsOnline ? "1" : "0",
                    t.IsDead ? "1" : "0",
                    t.IsLeader ? "1" : "0")));

        return $"{serverKey ?? ""}#{serverName ?? ""}#{team}";
    }

    private async Task AnnouncePresenceChangeAsync(TeamMemberVM vm, (bool online, bool dead) prev, (bool online, bool dead) now)
    {
        try
        {
            if (prev.online != now.online && _announceSpawns)
            {
                bool isSelf = vm.SteamId == _mySteamId;
                bool shouldAnnounce = now.online ? TrackingService.AnnouncePlayerOnline : TrackingService.AnnouncePlayerOffline;

                if (shouldAnnounce)
                {
                    var where = (vm.X.HasValue && vm.Y.HasValue) ? GetGridLabel(vm.X.Value, vm.Y.Value) : Properties.Resources.Unknown;
                    var dispName = GetDisplayPlayerName(vm.Name);
                    var txt = now.online ? AlertTemplateService.GetFormattedAlert("AlertPlayerOnlineWithPos", dispName, where) : AlertTemplateService.GetFormattedAlert("AlertPlayerOffline", dispName);
                    await SendTeamChatSafeAsync(txt);
                }
            }

            if (prev.dead != now.dead)
            {
                double? px = vm.X, py = vm.Y;
                if ((!px.HasValue || !py.HasValue) && TryResolvePosFromDynMarkers(vm.SteamId, out var dx, out var dy))
                {
                    px = dx;
                    py = dy;
                }

                if (_announceSpawns)
                {
                    bool isSelf = vm.SteamId == _mySteamId;
                    bool shouldAnnounce = false;

                    if (prev.dead != now.dead)
                    {
                        if (now.dead) shouldAnnounce = isSelf ? TrackingService.AnnouncePlayerDeathSelf : TrackingService.AnnouncePlayerDeathTeam;
                        else shouldAnnounce = isSelf ? TrackingService.AnnouncePlayerRespawnSelf : TrackingService.AnnouncePlayerRespawnTeam;
                    }

                    if (shouldAnnounce)
                    {
                        var where = (px.HasValue && py.HasValue) ? GetGridLabel(px.Value, py.Value) : Properties.Resources.Unknown;
                        var dispName = GetDisplayPlayerName(vm.Name);
                        var txt = now.dead ? AlertTemplateService.GetFormattedAlert("AlertPlayerDied", dispName, where) : AlertTemplateService.GetFormattedAlert("AlertPlayerRespawned", dispName, where);
                        await SendTeamChatSafeAsync(txt);
                    }
                }

                if (now.dead && px.HasValue && py.HasValue)
                {
                    if (_vm?.Selected != null)
                    {
                        var list = _vm.Selected.DeathMarkers;
                        bool isSelf = vm.SteamId == _mySteamId;

                        var newMarker = new Models.DeathMarkerData
                        {
                            Id = Guid.NewGuid(),
                            SteamId = vm.SteamId,
                            OriginalName = vm.Name,
                            TimeOfDeath = DateTime.Now,
                            X = px.Value,
                            Y = py.Value
                        };
                        
                        list.Add(newMarker);
                        
                        // Apply limits
                        int selfMax = TrackingService.MaxSelfDeathMarkers;
                        int teamMax = TrackingService.MaxTeamDeathMarkers;
                        
                        var myMarkers = list.Where(m => m.SteamId == _mySteamId).OrderByDescending(m => m.TimeOfDeath).ToList();
                        while (myMarkers.Count > selfMax)
                        {
                            var oldest = myMarkers.Last();
                            list.Remove(oldest);
                            myMarkers.Remove(oldest);
                        }

                        var teamGroups = list.Where(m => m.SteamId != _mySteamId).GroupBy(m => m.SteamId);
                        foreach (var group in teamGroups)
                        {
                            var teamMarkers = group.OrderByDescending(m => m.TimeOfDeath).ToList();
                            while (teamMarkers.Count > teamMax)
                            {
                                var oldest = teamMarkers.Last();
                                list.Remove(oldest);
                                teamMarkers.Remove(oldest);
                            }
                        }

                        _vm.Save();
                        RedrawDeathPins();
                    }
                }
            }
        }
        catch
        {
        }
    }

    private static ImageSource? BytesToImage(byte[] bytes)
    {
        try
        {
            var bi = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<ImageSource?> FetchSteamAvatarAsync(ulong steamId)
    {
        if (steamId == 0) return null;
        try
        {
            using var http = new HttpClient();
            var xml = await http.GetStringAsync($"https://steamcommunity.com/profiles/{steamId}?xml=1");
            string url = "";
            var mFull = Regex.Match(xml, @"<avatarFull><!\[CDATA\[(.*?)\]\]></avatarFull>", RegexOptions.IgnoreCase);
            var mMedium = Regex.Match(xml, @"<avatarMedium><!\[CDATA\[(.*?)\]\]></avatarMedium>", RegexOptions.IgnoreCase);
            if (mFull.Success) url = mFull.Groups[1].Value;
            else if (mMedium.Success) url = mMedium.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(url)) return null;

            var bytes = await http.GetByteArrayAsync(url);
            return BytesToImage(bytes);
        }
        catch
        {
            return null;
        }
    }

    private async Task LoadAvatarAsync(TeamMemberVM vm)
    {
        try
        {
            if (vm.SteamId == 0 || vm.Avatar != null) return;

            if (_avatarCache.TryGetValue(vm.SteamId, out var cached) && cached != null)
            {
                vm.Avatar = cached;
                return;
            }

            var img = await FetchSteamAvatarAsync(vm.SteamId);
            if (img != null)
            {
                _avatarCache[vm.SteamId] = img;
                vm.Avatar = img;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[avatar] {vm.SteamId}: {ex.Message}");
        }
    }

    private void TeamItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TeamMemberVM vm)
            CenterOnMember(vm);
    }

    private void Team_Center_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TeamMemberVM vm)
            CenterOnMember(vm);
    }

    private void Team_Follow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TeamMemberVM vm)
        {
            StartFollowing(vm.SteamId, vm.DisplayName);
        }
    }

    private void StartFollowing(ulong steamId, string name)
    {
        if (_vm.FollowingSteamId == steamId)
        {
            StopTracking();
            return;
        }

        _vm.FollowingSteamId = steamId;
        _vm.FollowingPlayerName = name;
        
        var member = TeamMembers.FirstOrDefault(t => t.SteamId == steamId);
        _vm.FollowingPlayerAvatar = member?.Avatar;

        AppendLog($"Following {name} on map.");
        
        if (!string.IsNullOrEmpty(GetServerKey()))
        {
            Services.TrackingService.Settings.ServerFollowingSteamId[GetServerKey()] = steamId;
            Services.TrackingService.SaveDB();
        }
        
        // Immediate center
        if (TryResolvePosFromDynMarkers(steamId, out var x, out var y))
        {
            CenterMapOnWorld(x, y, true);
        }
        else if (TeamMembers.FirstOrDefault(t => t.SteamId == steamId) is { X: { } tx, Y: { } ty })
        {
            CenterMapOnWorld(tx, ty, true);
        }
    }

    private void Team_OpenProfile_Click(object sender, RoutedEventArgs e)
    {
        var vm = VMFromSender(sender);
        if (vm == null) return;
        try
        {
            var url = $"https://steamcommunity.com/profiles/{vm.SteamId}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private bool IAmLeaderNow() => TeamMembers.Any(t => t.SteamId == _mySteamId && t.IsLeader);

    private TeamMemberVM? VMFromSender(object sender)
        => (sender as FrameworkElement)?.DataContext as TeamMemberVM ?? TeamList?.SelectedItem as TeamMemberVM;

    private async void Team_Promote_Click(object sender, RoutedEventArgs e)
    {
        var vm = VMFromSender(sender);
        if (vm == null) return;
        if (!IAmLeaderNow()) { AppendLog("Only Leader can promote."); return; }
        if (vm.SteamId == _mySteamId) return;
        try { if (_real is RustPlusClientReal real) await real.PromoteToLeaderAsync(vm.SteamId); }
        catch (Exception ex) { AppendLog("[team] promote error: " + ex.Message); }
    }

    private void CenterOnMember(TeamMemberVM vm)
    {
        if (vm.X.HasValue && vm.Y.HasValue)
        {
            CenterMapOnWorld(vm.X.Value, vm.Y.Value);
            return;
        }
        if (TryResolvePosFromDynMarkers(vm.SteamId, out var x, out var y))
        {
            CenterMapOnWorld(x, y);
            return;
        }
        MessageBox.Show(Properties.Resources.NoPositionAvailable);    }

    private bool TryResolvePosFromDynMarkers(ulong sid, out double x, out double y)
    {
        if (_lastPlayersBySid.TryGetValue(sid, out var pos))
        {
            x = pos.x;
            y = pos.y;
            return true;
        }

        x = y = 0;
        return false;
    }

    private async void Team_Kick_Click(object sender, RoutedEventArgs e)
    {
        var vm = VMFromSender(sender);
        if (vm == null) return;
        if (!IAmLeaderNow()) { AppendLog("Only Leader can kick."); return; }
        if (vm.SteamId == _mySteamId) return;
        try { if (_real is RustPlusClientReal real) await real.KickTeamMemberAsync(vm.SteamId); }
        catch (Exception ex) { AppendLog("[team] kick error: " + ex.Message); }
    }

    private string ResolvePlayerName(RustPlusClientReal.DynMarker m)
    {
        if (!string.IsNullOrWhiteSpace(m.Name)) return m.Name;
        if (!string.IsNullOrWhiteSpace(m.Label)) return m.Label;

        if (m.SteamId != 0 && _steamNames.TryGetValue(m.SteamId, out var n) && !string.IsNullOrWhiteSpace(n))
            return n;

        if (DateTime.UtcNow - _lastTeamRefresh > TimeSpan.FromSeconds(5))
            _ = RefreshTeamNamesAsync();

        return "(player)";
    }

    private async Task RefreshTeamNamesAsync()
    {
        _lastTeamRefresh = DateTime.UtcNow;

        if (_rust is not RustPlusClientReal real) return;

        try
        {
            var team = await real.GetTeamInfoAsync();
            if (team?.Members != null)
            {
                foreach (var m in team.Members)
                {
                    if (m.SteamId != 0 && !string.IsNullOrWhiteSpace(m.Name))
                        _steamNames[m.SteamId] = m.Name!;
                }
            }
        }
        catch
        {
        }
    }

    private void TeamMember_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TeamMemberVM.ShowMarkers) || e.PropertyName == nameof(TeamMemberVM.Avatar))
        {
            if (e.PropertyName == nameof(TeamMemberVM.Avatar) && sender is TeamMemberVM vm)
            {
                if (_vm.FollowingSteamId == vm.SteamId)
                {
                    _vm.FollowingPlayerAvatar = vm.Avatar;
                }
            }

            if (_lastTeamInfo != null)
            {
                Dispatcher.Invoke(() => RedrawTeamMapNotes(_lastTeamInfo));
            }
            if (_lastMarkers != null)
            {
                Dispatcher.Invoke(() => UpdateDynUI(_lastMarkers));
            }
        }
    }

    private void TeamCheckBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox chk && chk.DataContext is TeamMemberVM vm)
        {
            vm.ShowMarkers = !vm.ShowMarkers;
            e.Handled = true;
        }
    }
}
