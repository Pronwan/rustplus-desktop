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
    public partial class MainWindow
    {
        public ObservableCollection<ClanMemberVM> ClanMembers { get; } = new();

        private DateTime _lastClanPoll = DateTime.MinValue;

        public async Task LoadClanAsync()
        {
            if (_real is null) return;

            try
            {
                var clan = await _real.GetClanInfoAsync();
                if (clan is null) return;

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

                    vm.RoleId = m.RoleId;
                    vm.RoleName = m.RoleName;
                    vm.Rank = m.Rank;
                    vm.Joined = m.Joined;
                    vm.LastSeen = m.LastSeen;
                    vm.Notes = m.Notes;
                    vm.IsOnline = m.IsOnline;

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
            }
            catch (Exception ex)
            {
                AppendLog($"[clan-load] Error: {ex.Message}");
            }
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

                // Parse Avatar
                string url = "";
                var mFull = Regex.Match(xml, @"<avatarFull><!\[CDATA\[(.*?)\]\]></avatarFull>", RegexOptions.IgnoreCase);
                var mMedium = Regex.Match(xml, @"<avatarMedium><!\[CDATA\[(.*?)\]\]></avatarMedium>", RegexOptions.IgnoreCase);
                if (mFull.Success) url = mFull.Groups[1].Value;
                else if (mMedium.Success) url = mMedium.Groups[1].Value;

                if (!string.IsNullOrWhiteSpace(url))
                {
                    var bytes = await http.GetByteArrayAsync(url);
                    var img = BytesToImage(bytes);
                    if (img != null)
                    {
                        _avatarCache[vm.SteamId] = img;
                        vm.Avatar = img;
                    }
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
    }

    public sealed class ClanMemberVM : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ulong SteamId { get; init; }

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
            set { if (_joined == value) return; _joined = value; OnChanged(nameof(Joined)); }
        }

        private DateTime _lastSeen;
        public DateTime LastSeen
        {
            get => _lastSeen;
            set { if (_lastSeen == value) return; _lastSeen = value; OnChanged(nameof(LastSeen)); }
        }

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
}
