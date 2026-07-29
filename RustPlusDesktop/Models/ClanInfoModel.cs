using System;
using System.Collections.Generic;

namespace RustPlusDesk.Models
{
    public sealed class ClanInfoModel
    {
        public long ClanId { get; set; }
        public string Name { get; set; } = "";
        public string Motd { get; set; } = "";
        public List<ClanMemberModel> Members { get; set; } = new();
    }

    public sealed class ClanMemberModel
    {
        public ulong SteamId { get; set; }
        public int RoleId { get; set; }
        public string RoleName { get; set; } = "";
        public int Rank { get; set; }
        public DateTime Joined { get; set; }
        public DateTime LastSeen { get; set; }
        public string Notes { get; set; } = "";
        public bool IsOnline { get; set; }
    }
}
