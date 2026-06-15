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
    private RustPlusClientReal? _real => _rust as RustPlusClientReal;

    public sealed class TeamMemberVM : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public int MissingCount { get; set; }
        public ulong SteamId { get; init; }

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

        // Set when the player moves after being AFK (= how long they were idle). Consumed by AfkTimer to announce the return.
        public TimeSpan? AfkReturnDuration { get; set; }
        // Set when the player goes offline; used to report how long they were gone when they come back.
        public DateTime? OfflineSince { get; set; }
        // Set when the player (re)spawns alive; used to report how long they were alive when they die.
        public DateTime? AliveSince { get; set; }

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
                    if (_isAfk)
                    {
                        // they're back — remember how long they were idle so we can announce it
                        AfkReturnDuration = DateTime.UtcNow - _lastMoveTime;
                        IsAfk = false;
                        AfkText = string.Empty;
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

        public bool UpdateAfkState(DateTime now, int thresholdMinutes = 5)
        {
            if (!IsOnline || IsDead)
            {
                _lastMoveTime = now;
                IsAfk = false;
                AfkText = string.Empty;
                AfkReturnDuration = null; // don't announce an AFK return if they went offline/died
                return false;
            }

            var elapsed = now - _lastMoveTime;
            if (elapsed.TotalMinutes >= thresholdMinutes)
            {
                bool becameAfk = !_isAfk;
                IsAfk = true;
                int totalSecs = (int)elapsed.TotalSeconds;
                int mins = totalSecs / 60;
                int secs = totalSecs % 60;
                AfkText = $"AFK: {mins}:{secs:D2}";
                return becameAfk;
            }
            else
            {
                IsAfk = false;
                AfkText = string.Empty;
                return false;
            }
        }

        private ImageSource? _avatar;
        public ImageSource? Avatar
        {
            get => _avatar;
            set { if (_avatar == value) return; _avatar = value; OnChanged(nameof(Avatar)); }
        }
    }

    private readonly Dictionary<ulong, (double x, double y, string name)> _lastPlayersBySid = new();
    private readonly Dictionary<ulong, (bool online, bool dead)> _lastPresence = new();
    private ulong _mySteamId => (ulong.TryParse(_vm?.SteamId64, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0UL);

    private readonly Dictionary<ulong, string> _steamNames = new();
    private DateTime _lastTeamRefresh = DateTime.MinValue;
    private bool _teamRosterInitialized; // skip announcing the initial roster as joins
    private string? _lastCloudPresenceSignature;
    private DateTime _lastPresenceUploadTime = DateTime.MinValue;
    private bool _hasCriticalPresenceChange;

    private void StartTeamPolling()
    {
        if (_teamTimer != null) return;
        _teamRosterInitialized = false; // re-baseline roster
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
        NotifyTeamFeatureServerDisconnected();

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
    }

    private void AfkTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        int afkThreshold = _vm?.Selected?.AfkThresholdMinutes ?? 5;
        foreach (var m in TeamMembers)
        {
            // true only on transition into AFK
            bool becameAfk = m.UpdateAfkState(now, afkThreshold);

            if (!_announceSpawns)
            {
                m.AfkReturnDuration = null; // announcements disabled — nothing to send
                continue;
            }

            var dispName = GetDisplayPlayerName(m.Name);
            if (becameAfk)
            {
                // just went AFK — include how long they've been idle (≈ the threshold)
                var txt = AlertTemplateService.GetFormattedAlert("AlertPlayerAfk", dispName, FormatAgo(now - m.LastMoveTime));
                if (!string.IsNullOrWhiteSpace(txt))
                    _ = SendTeamChatSafeAsync(txt);
            }
            else if (m.AfkReturnDuration.HasValue && m.IsOnline)
            {
                // came back — report how long they were AFK
                var txt = AlertTemplateService.GetFormattedAlert("AlertPlayerAfkBack", dispName, FormatAgo(m.AfkReturnDuration.Value));
                if (!string.IsNullOrWhiteSpace(txt))
                    _ = SendTeamChatSafeAsync(txt);
                m.AfkReturnDuration = null;
            }
        }
    }

    private int _teamPollBusy = 0;

    private async void TeamTimer_Tick(object? sender, EventArgs e)
    {
        if (System.Threading.Interlocked.Exchange(ref _teamPollBusy, 1) == 1) return;
        try { await LoadTeamAsync(); }
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

    private async Task LoadTeamAsync()
    {
        if (_real is null) return;

        try
        {
            var team = await _real.GetTeamInfoAsync();
            if (team is null) return;

            var leaderId = team.LeaderSteamId;
            foreach (var m in TeamMembers) m.MissingCount++;

            // track this pass's roster delta so we can tell a single teammate change
            // apart from YOU switching teams (mass change)
            int prevTeammateCount = TeamMembers.Count(t => t.SteamId != _mySteamId);
            var joinedThisPass = new List<TeamMemberVM>();
            var leftThisPass = new List<TeamMemberVM>();

            var avatarTasks = new List<Task>();
            foreach (var m in team.Members)
            {
                var sid = m.SteamId;
                if (sid == 0) continue;

                var vm = TeamMembers.FirstOrDefault(t => t.SteamId == sid);
                if (vm == null)
                {
                    vm = new TeamMemberVM { SteamId = sid, Abbreviate = _abbreviateNames };
                    TeamMembers.Add(vm);
                    _hasCriticalPresenceChange = true;
                    if (sid != _mySteamId) joinedThisPass.Add(vm);
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
                // Best-effort seed for players already alive when first seen (no spawn event to
                // anchor to). Real respawns reset this accurately in AnnouncePresenceChangeAsync.
                if (!m.Dead && !vm.AliveSince.HasValue) vm.AliveSince = DateTime.UtcNow;
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
                    var left = TeamMembers[i];
                    if (left.SteamId != _mySteamId) leftThisPass.Add(left);
                    TeamMembers.RemoveAt(i);
                    _hasCriticalPresenceChange = true;
                }

            int curTeammateCount = TeamMembers.Count(t => t.SteamId != _mySteamId);

            // Announce join/leave (spaced out to avoid API flood); handle YOUR own team-switches specially.
            if (_teamRosterInitialized && _announceSpawns)
                _ = AnnounceRosterDelta(joinedThisPass, leftThisPass, prevTeammateCount, curTeammateCount, leaderId);

            // baseline set; later passes announce joins/leaves
            _teamRosterInitialized = true;

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
            bool forcePeriodicUpload = timeSinceLast.TotalSeconds >= 290;
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

    // Announces team join/leave, distinguishing YOUR own team-switches from normal teammate changes,
    // and spaces messages out so we never burst the Rust+ API (which can get the player kicked).
    private async Task AnnounceRosterDelta(List<TeamMemberVM> joined, List<TeamMemberVM> left,
                                          int prevTeammateCount, int curTeammateCount, ulong leaderId)
    {
        const int gapMs = 2500; // spacing between consecutive chat messages

        // Build all messages first (sync), then send with delays.
        var messages = new List<string>();

        // ----- JOINS -----
        if (joined.Count > 0)
        {
            bool iAmLeader = leaderId != 0 && leaderId == _mySteamId;
            if (!iAmLeader && prevTeammateCount == 0)
            {
                // I went from solo to being in someone else's team => I joined them.
                // Announce only me once, not every member who was already there.
                var meName = GetDisplayPlayerName(TeamMembers.FirstOrDefault(t => t.SteamId == _mySteamId)?.Name ?? "");
                var txt = AlertTemplateService.GetFormattedAlert("AlertPlayerJoinedTeam", meName);
                if (!string.IsNullOrWhiteSpace(txt)) messages.Add(txt);
            }
            else
            {
                // It's my team (I'm leader) or a teammate joined the team I'm already in => announce each.
                foreach (var vm in joined)
                {
                    var txt = AlertTemplateService.GetFormattedAlert("AlertPlayerJoinedTeam", GetDisplayPlayerName(vm.Name));
                    if (!string.IsNullOrWhiteSpace(txt)) messages.Add(txt);
                }
            }
        }

        // ----- LEAVES ----- (suppress entirely if the roster collapsed to just me: I left / team dissolved)
        if (left.Count > 0 && curTeammateCount >= 1)
        {
            foreach (var vm in left)
            {
                var txt = AlertTemplateService.GetFormattedAlert("AlertPlayerLeftTeam", GetDisplayPlayerName(vm.Name));
                if (!string.IsNullOrWhiteSpace(txt)) messages.Add(txt);
            }
        }

        for (int i = 0; i < messages.Count; i++)
        {
            if (i > 0) await Task.Delay(gapMs);
            await SendTeamChatSafeAsync(messages[i]);
        }
    }

    private async Task AnnouncePresenceChangeAsync(TeamMemberVM vm, (bool online, bool dead) prev, (bool online, bool dead) now)
    {
        try
        {
            if (prev.online != now.online)
            {
                if (_announceSpawns)
                {
                    bool shouldAnnounce = now.online ? TrackingService.AnnouncePlayerOnline : TrackingService.AnnouncePlayerOffline;

                    if (shouldAnnounce)
                    {
                        var where = (vm.X.HasValue && vm.Y.HasValue) ? GetGridLabel(vm.X.Value, vm.Y.Value) : Properties.Resources.Unknown;
                        var dispName = GetDisplayPlayerName(vm.Name);
                        string txt;
                        if (now.online)
                        {
                            txt = AlertTemplateService.GetFormattedAlert("AlertPlayerOnlineWithPos", dispName, where);
                            // tack on how long they were offline, if we tracked it
                            if (vm.OfflineSince.HasValue)
                            {
                                var suffix = AlertTemplateService.GetFormattedAlert("AlertPlayerOfflineDuration", FormatAgo(DateTime.UtcNow - vm.OfflineSince.Value));
                                if (!string.IsNullOrWhiteSpace(suffix)) txt += " " + suffix;
                            }
                        }
                        else
                        {
                            txt = AlertTemplateService.GetFormattedAlert("AlertPlayerOffline", dispName);
                        }
                        await SendTeamChatSafeAsync(txt);
                    }
                }

                // Track offline timing regardless of the announce setting, so the duration is
                // correct even if alerts were toggled on only after the player went offline.
                vm.OfflineSince = now.online ? (DateTime?)null : DateTime.UtcNow;
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
                        string txt;
                        if (now.dead)
                        {
                            txt = AlertTemplateService.GetFormattedAlert("AlertPlayerDied", dispName, where);
                            // tack on how long they were alive, if we tracked their spawn
                            if (vm.AliveSince.HasValue)
                            {
                                var suffix = AlertTemplateService.GetFormattedAlert("AlertPlayerAliveDuration", FormatAgo(DateTime.UtcNow - vm.AliveSince.Value));
                                if (!string.IsNullOrWhiteSpace(suffix)) txt += " " + suffix;
                            }
                        }
                        else
                        {
                            txt = AlertTemplateService.GetFormattedAlert("AlertPlayerRespawned", dispName, where);
                        }
                        await SendTeamChatSafeAsync(txt);
                    }
                }

                // Track alive time independently of the announce setting: reset the clock on
                // respawn, clear it on death (read above before clearing).
                vm.AliveSince = now.dead ? (DateTime?)null : DateTime.UtcNow;

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
        _vm.FollowingSteamId = steamId;
        _vm.FollowingPlayerName = name;
        
        var member = TeamMembers.FirstOrDefault(t => t.SteamId == steamId);
        _vm.FollowingPlayerAvatar = member?.Avatar;

        AppendLog($"Following {name} on map.");
        
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
        try { await (_real as RustPlusClientReal)?.PromoteToLeaderAsync(vm.SteamId); }
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
        try { await (_real as RustPlusClientReal)?.KickTeamMemberAsync(vm.SteamId); }
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
}
