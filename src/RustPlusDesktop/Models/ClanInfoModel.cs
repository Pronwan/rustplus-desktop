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
        public List<ClanRoleModel> Roles { get; set; } = new();
        public List<ClanInviteModel> Invites { get; set; } = new();

        public DateTime Created { get; set; }
        public ulong Creator { get; set; }
        public DateTime? MotdTimestamp { get; set; }
        public ulong? MotdAuthor { get; set; }
        public byte[]? Logo { get; set; }
        public int? Color { get; set; }
        public int? MaxMemberCount { get; set; }
        public long? Score { get; set; }
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

    public sealed class ClanRoleModel
    {
        public int RoleId { get; set; }
        public int Rank { get; set; }
        public string Name { get; set; } = "";
        public bool CanSetMotd { get; set; }
        public bool CanSetLogo { get; set; }
        public bool CanInvite { get; set; }
        public bool CanKick { get; set; }
        public bool CanPromote { get; set; }
        public bool CanDemote { get; set; }
        public bool CanSetPlayerNotes { get; set; }
        public bool CanAccessLogs { get; set; }
        public bool CanAccessScoreEvents { get; set; }
    }

    public sealed class ClanInviteModel
    {
        public ulong SteamId { get; set; }
        public ulong Recruiter { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
