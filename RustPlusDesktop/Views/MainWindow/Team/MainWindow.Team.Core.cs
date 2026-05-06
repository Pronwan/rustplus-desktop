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
            set { if (_name == value) return; _name = value; OnChanged(nameof(Name)); }
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

        private ImageSource? _avatar;
        public ImageSource? Avatar
        {
            get => _avatar;
            set { if (_avatar == value) return; _avatar = value; OnChanged(nameof(Avatar)); }
        }
    }

    private readonly Dictionary<ulong, (double x, double y, string name)> _lastPlayersBySid = new();
    private readonly Dictionary<ulong, (bool online, bool dead)> _lastPresence = new();
    private ulong _mySteamId => (ulong.TryParse(_vm?.SteamId64, out var v) ? v : 0UL);

    private readonly Dictionary<ulong, string> _steamNames = new();
    private DateTime _lastTeamRefresh = DateTime.MinValue;

    private void StartTeamPolling()
    {
        if (_teamTimer != null) return;
        _teamTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _teamTimer.Tick += TeamTimer_Tick;
        _teamTimer.Start();
    }

    private void StopTeamPolling()
    {
        var t = _teamTimer;
        if (t == null) return;
        t.Tick -= TeamTimer_Tick;
        t.Stop();
        _teamTimer = null;
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

            foreach (var m in team.Members)
            {
                var sid = m.SteamId;
                if (sid == 0) continue;

                var vm = TeamMembers.FirstOrDefault(t => t.SteamId == sid);
                if (vm == null)
                {
                    vm = new TeamMemberVM { SteamId = sid };
                    TeamMembers.Add(vm);
                    _ = LoadAvatarAsync(vm);
                    if (vm.Avatar == null && CanTryAvatar(sid))
                    {
                        _ = EnsureAvatarAsync(vm);
                    }
                }

                vm.MissingCount = 0;

                var hadPrev = _lastPresence.TryGetValue(sid, out var prev);

                vm.Name = string.IsNullOrWhiteSpace(m.Name) ? "(player)" : m.Name!;
                vm.IsLeader = leaderId != 0 && sid == leaderId;
                vm.IsOnline = m.Online;
                vm.IsDead = m.Dead;
                vm.X = m.X;
                vm.Y = m.Y;

                var now = (m.Online, m.Dead);
                _lastPresence[sid] = now;

                if (hadPrev && prev != now)
                    _ = AnnouncePresenceChangeAsync(vm, prev, now);
            }

            for (int i = TeamMembers.Count - 1; i >= 0; i--)
                if (TeamMembers[i].MissingCount > 2)
                    TeamMembers.RemoveAt(i);
        }
        catch (Exception ex)
        {
            AppendLog("[team] " + ex.Message);
        }

        if (_overlayToolsVisible)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                RebuildOverlayTeamBar();
            });
        }
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
                    var where = (vm.X.HasValue && vm.Y.HasValue) ? GetGridLabel(vm.X.Value, vm.Y.Value) : "unknown";
                    var txt = now.online ? $"{vm.Name} came online @ {where}" : $"{vm.Name} went offline";
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
                        var where = (px.HasValue && py.HasValue) ? GetGridLabel(px.Value, py.Value) : "unknown";
                        var txt = now.dead ? $"{vm.Name} died @ {where}" : $"{vm.Name} respawned @ {where}";
                        await SendTeamChatSafeAsync(txt);
                    }
                }

                if (_showDeathMarkers && now.dead && px.HasValue && py.HasValue)
                {
                    PlaceOrMoveDeathPin(vm.SteamId, px.Value, py.Value, vm.Name);
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
        MessageBox.Show("Keine Position verfugbar (offline oder nicht gespawnt).");
    }

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
