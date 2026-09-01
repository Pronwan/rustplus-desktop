using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views
{
    public partial class MainWindow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // --- Clan Info Properties ---
        private long _clanId;
        public long ClanId
        {
            get => _clanId;
            set { if (_clanId == value) return; _clanId = value; OnPropertyChanged(nameof(ClanId)); }
        }

        private string _clanName = "";
        public string ClanName
        {
            get => _clanName;
            set { if (_clanName == value) return; _clanName = value; OnPropertyChanged(nameof(ClanName)); }
        }

        private string _clanMotd = "";
        public string ClanMotd
        {
            get => _clanMotd;
            set 
            { 
                if (_clanMotd == value) return; 
                _clanMotd = value; 
                OnPropertyChanged(nameof(ClanMotd)); 
                OnPropertyChanged(nameof(HasClanMotd));
            }
        }

        public bool HasClanMotd => !string.IsNullOrWhiteSpace(_clanMotd);

        private string _clanCreatedText = "";
        public string ClanCreatedText
        {
            get => _clanCreatedText;
            set { if (_clanCreatedText == value) return; _clanCreatedText = value; OnPropertyChanged(nameof(ClanCreatedText)); }
        }

        private string _clanCreatorText = "";
        public string ClanCreatorText
        {
            get => _clanCreatorText;
            set { if (_clanCreatorText == value) return; _clanCreatorText = value; OnPropertyChanged(nameof(ClanCreatorText)); }
        }

        private string _clanMotdAuthorText = "";
        public string ClanMotdAuthorText
        {
            get => _clanMotdAuthorText;
            set { if (_clanMotdAuthorText == value) return; _clanMotdAuthorText = value; OnPropertyChanged(nameof(ClanMotdAuthorText)); }
        }

        private ImageSource? _clanLogoImage;
        public ImageSource? ClanLogoImage
        {
            get => _clanLogoImage;
            set 
            { 
                if (_clanLogoImage == value) return; 
                _clanLogoImage = value; 
                OnPropertyChanged(nameof(ClanLogoImage)); 
                OnPropertyChanged(nameof(HasClanLogo));
            }
        }

        public bool HasClanLogo => ClanLogoImage != null;

        private SolidColorBrush _clanColorBrush = new(Color.FromRgb(30, 90, 180));
        public SolidColorBrush ClanColorBrush
        {
            get => _clanColorBrush;
            set { if (_clanColorBrush == value) return; _clanColorBrush = value; OnPropertyChanged(nameof(ClanColorBrush)); }
        }

        private int _clanMaxMemberCount;
        public int ClanMaxMemberCount
        {
            get => _clanMaxMemberCount;
            set { if (_clanMaxMemberCount == value) return; _clanMaxMemberCount = value; OnPropertyChanged(nameof(ClanMaxMemberCount)); }
        }

        private int _clanMemberCount;
        public int ClanMemberCount
        {
            get => _clanMemberCount;
            set { if (_clanMemberCount == value) return; _clanMemberCount = value; OnPropertyChanged(nameof(ClanMemberCount)); }
        }

        private int _clanOnlineCount;
        public int ClanOnlineCount
        {
            get => _clanOnlineCount;
            set { if (_clanOnlineCount == value) return; _clanOnlineCount = value; OnPropertyChanged(nameof(ClanOnlineCount)); }
        }

        private string _clanMembersRatio = "";
        public string ClanMembersRatio
        {
            get => _clanMembersRatio;
            set { if (_clanMembersRatio == value) return; _clanMembersRatio = value; OnPropertyChanged(nameof(ClanMembersRatio)); }
        }

        private string _clanScoreText = "";
        public string ClanScoreText
        {
            get => _clanScoreText;
            set { if (_clanScoreText == value) return; _clanScoreText = value; OnPropertyChanged(nameof(ClanScoreText)); }
        }

        private string _clanRolesSummary = "";
        public string ClanRolesSummary
        {
            get => _clanRolesSummary;
            set { if (_clanRolesSummary == value) return; _clanRolesSummary = value; OnPropertyChanged(nameof(ClanRolesSummary)); }
        }

        private int _clanInvitesCount;
        public int ClanInvitesCount
        {
            get => _clanInvitesCount;
            set 
            { 
                if (_clanInvitesCount == value) return; 
                _clanInvitesCount = value; 
                OnPropertyChanged(nameof(ClanInvitesCount)); 
                OnPropertyChanged(nameof(HasClanInvites));
            }
        }

        public bool HasClanInvites => ClanInvitesCount > 0;

        private string _lastClanPullTime = "";
        public string LastClanPullTime
        {
            get => _lastClanPullTime;
            set { if (_lastClanPullTime == value) return; _lastClanPullTime = value; OnPropertyChanged(nameof(LastClanPullTime)); }
        }

        private bool _hasClanInfo;
        public bool HasClanInfo
        {
            get => _hasClanInfo;
            set { if (_hasClanInfo == value) return; _hasClanInfo = value; OnPropertyChanged(nameof(HasClanInfo)); }
        }

        private bool _isClanListView;
        public bool IsClanListView
        {
            get => _isClanListView;
            set { if (_isClanListView == value) return; _isClanListView = value; OnPropertyChanged(nameof(IsClanListView)); }
        }

        public ObservableCollection<ClanMemberVM> ClanMembers { get; } = new();
        public ObservableCollection<ClanRoleVM> ClanRoles { get; } = new();
        public ObservableCollection<ClanInviteVM> ClanInvites { get; } = new();

        private DateTime _lastClanPoll = DateTime.MinValue;

        private async Task<string> GetSteamNameAsync(ulong steamId)
        {
            if (steamId == 0) return "";
            if (_steamNames.TryGetValue(steamId, out var name)) return name;

            try
            {
                using var http = new HttpClient();
                var xml = await http.GetStringAsync($"https://steamcommunity.com/profiles/{steamId}?xml=1");
                var mName = Regex.Match(xml, @"<steamID><!\[CDATA\[(.*?)\]\]></steamID>", RegexOptions.IgnoreCase);
                if (mName.Success)
                {
                    var fetchedName = mName.Groups[1].Value;
                    _steamNames[steamId] = fetchedName;
                    return fetchedName;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[clan-steam-name] {steamId}: {ex.Message}");
            }
            return steamId.ToString();
        }

        public async Task LoadClanAsync()
        {
            if (_real is null) return;

            try
            {
                var clan = await _real.GetClanInfoAsync();
                if (clan is null) return;

                ClanId = clan.ClanId;
                ClanName = clan.Name;
                ClanMotd = clan.Motd;
                ClanCreatedText = clan.Created != default ? clan.Created.ToString("dd/MM/yyyy") : "-";
                ClanMaxMemberCount = clan.MaxMemberCount ?? 100;
                ClanMemberCount = clan.Members.Count;
                int onlineCount = clan.Members.Count(m => m.IsOnline);
                ClanOnlineCount = onlineCount;
                ClanMembersRatio = $"{onlineCount} Online · {ClanMemberCount} / {ClanMaxMemberCount}";
                ClanScoreText = clan.Score?.ToString() ?? "-";
                LastClanPullTime = DateTime.Now.ToString("HH:mm:ss");
                HasClanInfo = true;

                // Logo
                if (clan.Logo != null && clan.Logo.Length > 0)
                {
                    ClanLogoImage = BytesToImage(clan.Logo);
                }
                else
                {
                    ClanLogoImage = null;
                }

                // Clan Banner Color (Facepunch RGBA hex uint32: byte3=R, byte2=G, byte1=B, byte0=A)
                if (clan.Color.HasValue && clan.Color.Value != 0)
                {
                    uint c = (uint)clan.Color.Value;
                    byte r = (byte)((c >> 24) & 0xFF);
                    byte g = (byte)((c >> 16) & 0xFF);
                    byte b = (byte)((c >> 8) & 0xFF);
                    byte a = (byte)(c & 0xFF);
                    if (a == 0) a = 255;
                    ClanColorBrush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
                }
                else
                {
                    ClanColorBrush = new SolidColorBrush(Color.FromRgb(30, 90, 180));
                }

                // Roles
                ClanRoles.Clear();
                foreach (var r in clan.Roles.OrderBy(r => r.Rank))
                {
                    ClanRoles.Add(new ClanRoleVM
                    {
                        RoleId = r.RoleId,
                        Rank = r.Rank,
                        Name = r.Name,
                        CanSetMotd = r.CanSetMotd,
                        CanSetLogo = r.CanSetLogo,
                        CanInvite = r.CanInvite,
                        CanKick = r.CanKick,
                        CanPromote = r.CanPromote,
                        CanDemote = r.CanDemote,
                        CanSetPlayerNotes = r.CanSetPlayerNotes,
                        CanAccessLogs = r.CanAccessLogs,
                        CanAccessScoreEvents = r.CanAccessScoreEvents
                    });
                }
                ClanRolesSummary = string.Join(", ", ClanRoles.Select(r => r.Name));

                // Invites
                ClanInvites.Clear();
                foreach (var inv in clan.Invites)
                {
                    var invVm = new ClanInviteVM
                    {
                        SteamId = inv.SteamId,
                        Recruiter = inv.Recruiter,
                        Timestamp = inv.Timestamp
                    };
                    ClanInvites.Add(invVm);

                    _ = Task.Run(async () =>
                    {
                        var name = await GetSteamNameAsync(inv.SteamId);
                        var recName = await GetSteamNameAsync(inv.Recruiter);
                        App.Current.Dispatcher.Invoke(() =>
                        {
                            invVm.Name = name;
                            invVm.RecruiterName = recName;
                        });
                    });
                }
                ClanInvitesCount = ClanInvites.Count;

                // Load Creator / Founder Name
                _ = Task.Run(async () =>
                {
                    var founder = await GetSteamNameAsync(clan.Creator);
                    App.Current.Dispatcher.Invoke(() => ClanCreatorText = founder);
                });

                // Load MOTD Author Name
                if (clan.MotdAuthor.HasValue && clan.MotdAuthor.Value != 0)
                {
                    _ = Task.Run(async () =>
                    {
                        var author = await GetSteamNameAsync(clan.MotdAuthor.Value);
                        var dateStr = clan.MotdTimestamp?.ToString("dd/MM/yyyy") ?? "";
                        App.Current.Dispatcher.Invoke(() => ClanMotdAuthorText = $"Set by {author} on {dateStr}");
                    });
                }
                else
                {
                    ClanMotdAuthorText = "";
                }

                // Sync: remove members that are no longer in the clan
                var currentClanIds = clan.Members.Select(m => m.SteamId).ToHashSet();
                for (int i = ClanMembers.Count - 1; i >= 0; i--)
                {
                    if (!currentClanIds.Contains(ClanMembers[i].SteamId))
                    {
                        ClanMembers.RemoveAt(i);
                    }
                }

                var avatarTasks = new List<Task>();

                foreach (var m in clan.Members)
                {
                    var sid = m.SteamId;
                    if (sid == 0) continue;

                    var vm = ClanMembers.FirstOrDefault(c => c.SteamId == sid);
                    if (vm == null)
                    {
                        vm = new ClanMemberVM { SteamId = sid };
                        ClanMembers.Add(vm);
                    }

                    var role = clan.Roles.FirstOrDefault(r => r.RoleId == m.RoleId);

                    vm.RoleId = m.RoleId;
                    vm.RoleName = m.RoleName;
                    vm.Rank = m.Rank;
                    vm.Joined = m.Joined;
                    vm.LastSeen = m.LastSeen;
                    vm.Notes = m.Notes;
                    vm.IsOnline = m.IsOnline;
                    vm.IsInTeam = TeamMembers.Any(tm => tm.SteamId == sid);

                    if (role != null)
                    {
                        var perms = new List<string>();
                        if (role.CanSetMotd) perms.Add("MOTD");
                        if (role.CanSetLogo) perms.Add("Logo");
                        if (role.CanInvite) perms.Add("Invite");
                        if (role.CanKick) perms.Add("Kick");
                        if (role.CanPromote) perms.Add("Promote");
                        if (role.CanDemote) perms.Add("Demote");
                        if (role.CanSetPlayerNotes) perms.Add("Notes");
                        if (role.CanAccessLogs) perms.Add("Logs");
                        if (role.CanAccessScoreEvents) perms.Add("Score");
                        vm.RolePermissions = perms.Count > 0 ? string.Join(", ", perms) : "None";
                    }

                    // Fetch avatar and SteamID name in the background
                    if (vm.Avatar == null || vm.Name == "(player)")
                    {
                        avatarTasks.Add(LoadClanMemberProfileAsync(vm));
                    }
                }

                if (avatarTasks.Count > 0)
                {
                    _ = Task.WhenAll(avatarTasks);
                }

                ReconcileClanRolePermissions(clan);
            }
            catch (Exception ex)
            {
                AppendLog($"[clan-load] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Brings the saved per-role command permissions in line with the clan as it is now.
        ///
        /// Roles are the clan's, not ours: they get created, renamed and deleted while the app is
        /// closed. So the saved list is treated as an answer to "which role ids may use commands"
        /// and everything else — the names, the order, which roles exist at all — is taken fresh
        /// from the server. A role that disappeared drops out rather than lingering as a granted
        /// permission nobody can see any more, and a new one arrives switched off.
        /// </summary>
        private void ReconcileClanRolePermissions(ClanInfoModel clan)
        {
            var profile = _vm?.Selected;
            if (profile == null || clan.Roles == null) return;

            var allowedIds = profile.ClanRolePermissions
                .Where(r => r.Allowed)
                .Select(r => r.RoleId)
                .ToHashSet();

            var rebuilt = clan.Roles
                .OrderBy(r => r.Rank)
                .Select(r => new ClanRolePermission
                {
                    RoleId = r.RoleId,
                    Name = string.IsNullOrWhiteSpace(r.Name) ? $"Role {r.RoleId}" : r.Name,
                    Rank = r.Rank,
                    Allowed = allowedIds.Contains(r.RoleId),
                })
                .ToList();

            // Only touch the collection when something actually differs. It is bound to a list of
            // checkboxes, and rebuilding it on every clan pull would drop a tick the moment
            // someone set it.
            bool unchanged = rebuilt.Count == profile.ClanRolePermissions.Count
                && rebuilt.Zip(profile.ClanRolePermissions).All(p =>
                    p.First.RoleId == p.Second.RoleId
                    && p.First.Name == p.Second.Name
                    && p.First.Allowed == p.Second.Allowed);
            if (unchanged) return;

            profile.ClanRolePermissions = new ObservableCollection<ClanRolePermission>(rebuilt);
        }

        private async Task LoadClanMemberProfileAsync(ClanMemberVM vm)
        {
            try
            {
                if (vm.SteamId == 0) return;

                // 1. Try Cache First
                if (_avatarCache.TryGetValue(vm.SteamId, out var cachedImg) && cachedImg != null)
                {
                    vm.Avatar = cachedImg;
                    if (_steamNames.TryGetValue(vm.SteamId, out var cachedName))
                    {
                        vm.Name = cachedName;
                        return;
                    }
                }

                // 2. Fetch Steam profile details (XML contains both steamID/nickname and avatar link)
                using var http = new HttpClient();
                var xml = await http.GetStringAsync($"https://steamcommunity.com/profiles/{vm.SteamId}?xml=1");

                // Parse Steam ID Name (Nickname)
                var mName = Regex.Match(xml, @"<steamID><!\[CDATA\[(.*?)\]\]></steamID>", RegexOptions.IgnoreCase);
                if (mName.Success)
                {
                    var name = mName.Groups[1].Value;
                    _steamNames[vm.SteamId] = name;
                    vm.Name = name;
                }

                // Parse & Load Avatar
                var img = await AvatarLoader.GetOrLoadAvatarAsync(vm.SteamId);
                if (img != null)
                {
                    _avatarCache[vm.SteamId] = img;
                    vm.Avatar = img;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[clan-avatar] {vm.SteamId}: {ex.Message}");
            }
        }

        private void Clan_OpenProfile_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if ((sender as System.Windows.FrameworkElement)?.DataContext is ClanMemberVM vm)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = $"https://steamcommunity.com/profiles/{vm.SteamId}",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        private void Clan_CopySteamId_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if ((sender as System.Windows.FrameworkElement)?.DataContext is ClanMemberVM vm)
            {
                try
                {
                    System.Windows.Clipboard.SetText(vm.SteamId.ToString());
                }
                catch { }
            }
        }

        private async void BtnRefreshClan_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await LoadClanAsync();
        }

        private void BtnToggleClanView_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            IsClanListView = !IsClanListView;
        }

        private async void BtnOpenClanChat_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            TabClanChat.IsChecked = true;
            await OpenChatOverlayAsync();
        }

        private void UpdateClanMembersTeamStatus()
        {
            var teamIds = TeamMembers.Select(tm => tm.SteamId).ToHashSet();
            foreach (var vm in ClanMembers)
            {
                vm.IsInTeam = teamIds.Contains(vm.SteamId);
            }
        }
    }

    public sealed class ClanMemberVM : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ulong SteamId { get; init; }

        private bool _isInTeam;
        public bool IsInTeam
        {
            get => _isInTeam;
            set { if (_isInTeam == value) return; _isInTeam = value; OnChanged(nameof(IsInTeam)); }
        }

        private string _name = "(player)";
        public string Name
        {
            get => _name;
            set { if (_name == value) return; _name = value; OnChanged(nameof(Name)); }
        }

        private string _roleName = "";
        public string RoleName
        {
            get => _roleName;
            set { if (_roleName == value) return; _roleName = value; OnChanged(nameof(RoleName)); }
        }

        private int _roleId;
        public int RoleId
        {
            get => _roleId;
            set { if (_roleId == value) return; _roleId = value; OnChanged(nameof(RoleId)); }
        }

        private int _rank;
        public int Rank
        {
            get => _rank;
            set { if (_rank == value) return; _rank = value; OnChanged(nameof(Rank)); }
        }

        private string _rolePermissions = "";
        public string RolePermissions
        {
            get => _rolePermissions;
            set { if (_rolePermissions == value) return; _rolePermissions = value; OnChanged(nameof(RolePermissions)); }
        }

        private bool _isOnline;
        public bool IsOnline
        {
            get => _isOnline;
            set { if (_isOnline == value) return; _isOnline = value; OnChanged(nameof(IsOnline)); }
        }

        private DateTime _joined;
        public DateTime Joined
        {
            get => _joined;
            set { if (_joined == value) return; _joined = value; OnChanged(nameof(Joined)); OnChanged(nameof(JoinedText)); }
        }

        public string JoinedText => (Joined == default || Joined == DateTime.MinValue) ? "-" : Joined.ToString("d");

        private DateTime _lastSeen;
        public DateTime LastSeen
        {
            get => _lastSeen;
            set 
            { 
                if (_lastSeen == value) return; 
                _lastSeen = value; 
                OnChanged(nameof(LastSeen)); 
                OnChanged(nameof(LastSeenText)); 
            }
        }

        public string LastSeenText => (LastSeen == default || LastSeen == DateTime.MinValue) ? "Never" : LastSeen.ToString("g");

        private string _notes = "";
        public string Notes
        {
            get => _notes;
            set 
            { 
                if (_notes == value) return; 
                _notes = value; 
                OnChanged(nameof(Notes)); 
                OnChanged(nameof(HasNotes)); 
            }
        }

        public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

        private ImageSource? _avatar;
        public ImageSource? Avatar
        {
            get => _avatar;
            set { if (_avatar == value) return; _avatar = value; OnChanged(nameof(Avatar)); }
        }
    }

    public sealed class ClanRoleVM : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public int RoleId { get; init; }
        public int Rank { get; init; }
        public string Name { get; init; } = "";
        public bool CanSetMotd { get; init; }
        public bool CanSetLogo { get; init; }
        public bool CanInvite { get; init; }
        public bool CanKick { get; init; }
        public bool CanPromote { get; init; }
        public bool CanDemote { get; init; }
        public bool CanSetPlayerNotes { get; init; }
        public bool CanAccessLogs { get; init; }
        public bool CanAccessScoreEvents { get; init; }

        public string PermissionsSummary
        {
            get
            {
                var perms = new List<string>();
                if (CanSetMotd) perms.Add("MOTD");
                if (CanSetLogo) perms.Add("Logo");
                if (CanInvite) perms.Add("Invite");
                if (CanKick) perms.Add("Kick");
                if (CanPromote) perms.Add("Promote");
                if (CanDemote) perms.Add("Demote");
                if (CanSetPlayerNotes) perms.Add("Notes");
                if (CanAccessLogs) perms.Add("Logs");
                if (CanAccessScoreEvents) perms.Add("Score");
                return perms.Count > 0 ? string.Join(", ", perms) : "None";
            }
        }
    }

    public sealed class ClanInviteVM : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ulong SteamId { get; init; }
        public ulong Recruiter { get; init; }
        public DateTime Timestamp { get; init; }

        private string _name = "(player)";
        public string Name
        {
            get => _name;
            set { if (_name == value) return; _name = value; OnChanged(nameof(Name)); }
        }

        private string _recruiterName = "";
        public string RecruiterName
        {
            get => _recruiterName;
            set { if (_recruiterName == value) return; _recruiterName = value; OnChanged(nameof(RecruiterName)); }
        }

        public string TimestampText => Timestamp != default ? Timestamp.ToString("g") : "-";
    }
}
