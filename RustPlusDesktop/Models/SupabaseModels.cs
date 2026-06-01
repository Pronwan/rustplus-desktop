using System;
using Postgrest.Attributes;
using Postgrest.Models;
using Newtonsoft.Json;

namespace RustPlusDesk.Models
{
    [Table("map_overlays")]
    public class MapOverlayModel : BaseModel
    {
        [PrimaryKey("id", true)]
        [Column("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Column("server_key")]
        public string ServerKey { get; set; }

        [Column("steam_id")]
        public string SteamId { get; set; }

        [Column("overlay_data")]
        public string OverlayData { get; set; } // We can store the JSON string directly

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }

    [Table("smart_devices")]
    public class SmartDeviceModel : BaseModel
    {
        [PrimaryKey("id", true)]
        [Column("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Column("server_key")]
        public string ServerKey { get; set; }

        [Column("steam_id")]
        public string SteamId { get; set; }

        [Column("device_data")]
        public string DeviceData { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
    
    [Table("user_profiles")]
    public class UserProfileModel : BaseModel
    {
        [PrimaryKey("steam_id", false)]
        [Column("steam_id")]
        public string SteamId { get; set; }

        [Column("discord_id")]
        public string DiscordId { get; set; }

        [Column("user_id")]
        public string UserId { get; set; }

        [Column("subscription_tier")]
        public string SubscriptionTier { get; set; }

        [Column("discord_name")]
        public string DiscordName { get; set; }

        [Column("sync_accepted")]
        public bool SyncAccepted { get; set; }

        [Column("is_manual_supporter")]
        public bool IsManualSupporter { get; set; }

        [Column("manual_premium_at")]
        public DateTime? ManualPremiumAt { get; set; }

        [Column("last_active_at")]
        public DateTime LastActiveAt { get; set; }

        [Column("is_online")]
        public bool IsOnline { get; set; }

        [Column("current_server_key")]
        public string CurrentServerKey { get; set; }

        [Column("current_server_name")]
        public string CurrentServerName { get; set; }

        [Column("team_member_count")]
        public int TeamMemberCount { get; set; }

        [Column("team_members_json")]
        public string TeamMembersJson { get; set; }
    }
}

