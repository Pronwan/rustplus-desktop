using System;
using System.Text.Json.Serialization;
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
        [JsonProperty("id")]
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Column("server_key")]
        [JsonProperty("server_key")]
        [JsonPropertyName("server_key")]
        public string ServerKey { get; set; }

        [Column("steam_id")]
        [JsonProperty("steam_id")]
        [JsonPropertyName("steam_id")]
        public string SteamId { get; set; }

        [Column("overlay_data")]
        [JsonProperty("overlay_data")]
        [JsonPropertyName("overlay_data")]
        public string OverlayData { get; set; } // We can store the JSON string directly

        [Column("created_at", NullValueHandling = NullValueHandling.Ignore)]
        [JsonProperty("created_at")]
        [JsonPropertyName("created_at")]
        public DateTime? CreatedAt { get; set; }

        [Column("uncompressed_size")]
        [JsonProperty("uncompressed_size")]
        [JsonPropertyName("uncompressed_size")]
        public int UncompressedSize { get; set; }

        [Column("updated_at")]
        [JsonProperty("updated_at")]
        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }

    [Table("smart_devices")]
    public class SmartDeviceModel : BaseModel
    {
        [PrimaryKey("id", true)]
        [Column("id")]
        [JsonProperty("id")]
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Column("server_key")]
        [JsonProperty("server_key")]
        [JsonPropertyName("server_key")]
        public string ServerKey { get; set; }

        [Column("steam_id")]
        [JsonProperty("steam_id")]
        [JsonPropertyName("steam_id")]
        public string SteamId { get; set; }

        [Column("device_data")]
        [JsonProperty("device_data")]
        [JsonPropertyName("device_data")]
        public string DeviceData { get; set; }

        [Column("created_at", NullValueHandling = NullValueHandling.Ignore)]
        [JsonProperty("created_at")]
        [JsonPropertyName("created_at")]
        public DateTime? CreatedAt { get; set; }

        [Column("updated_at")]
        [JsonProperty("updated_at")]
        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
    
    [Table("user_profiles")]
    public class UserProfileModel : BaseModel
    {
        [PrimaryKey("steam_id", false)]
        [Column("steam_id")]
        [JsonProperty("steam_id")]
        [JsonPropertyName("steam_id")]
        public string SteamId { get; set; }

        [Column("discord_id")]
        [JsonProperty("discord_id")]
        [JsonPropertyName("discord_id")]
        public string DiscordId { get; set; }

        [Column("user_id")]
        [JsonProperty("user_id")]
        [JsonPropertyName("user_id")]
        public string UserId { get; set; }

        [Column("subscription_tier")]
        [JsonProperty("subscription_tier")]
        [JsonPropertyName("subscription_tier")]
        public string SubscriptionTier { get; set; }

        [Column("discord_name")]
        [JsonProperty("discord_name")]
        [JsonPropertyName("discord_name")]
        public string DiscordName { get; set; }

        [Column("sync_accepted")]
        [JsonProperty("sync_accepted")]
        [JsonPropertyName("sync_accepted")]
        public bool SyncAccepted { get; set; }

        [Column("is_manual_supporter")]
        [JsonProperty("is_manual_supporter")]
        [JsonPropertyName("is_manual_supporter")]
        public bool IsManualSupporter { get; set; }

        [Column("premium_until")]
        [JsonProperty("premium_until")]
        [JsonPropertyName("premium_until")]
        public DateTime? PremiumUntil { get; set; }

        [Column("manual_premium_at")]
        [JsonProperty("manual_premium_at")]
        [JsonPropertyName("manual_premium_at")]
        public DateTime? ManualPremiumAt { get; set; }

        [Column("last_active_at")]
        [JsonProperty("last_active_at")]
        [JsonPropertyName("last_active_at")]
        public DateTime LastActiveAt { get; set; }

        [Column("is_online")]
        [JsonProperty("is_online")]
        [JsonPropertyName("is_online")]
        public bool IsOnline { get; set; }

        [Column("current_server_key")]
        [JsonProperty("current_server_key")]
        [JsonPropertyName("current_server_key")]
        public string CurrentServerKey { get; set; }

        [Column("current_server_name")]
        [JsonProperty("current_server_name")]
        [JsonPropertyName("current_server_name")]
        public string CurrentServerName { get; set; }

        [Column("team_member_count")]
        [JsonProperty("team_member_count")]
        [JsonPropertyName("team_member_count")]
        public int TeamMemberCount { get; set; }

        [Column("team_members_json")]
        [JsonProperty("team_members_json")]
        [JsonPropertyName("team_members_json")]
        public string TeamMembersJson { get; set; }
    }

    public class TeamFeatureMasterState
    {
        [JsonProperty("server_key")]
        [JsonPropertyName("server_key")]
        public string ServerKey { get; set; } = "";

        [JsonProperty("server_name")]
        [JsonPropertyName("server_name")]
        public string ServerName { get; set; } = "";

        [JsonProperty("team_key")]
        [JsonPropertyName("team_key")]
        public string TeamKey { get; set; } = "";

        [JsonProperty("master_steam_id")]
        [JsonPropertyName("master_steam_id")]
        public string? MasterSteamId { get; set; }

        [JsonProperty("master_name")]
        [JsonPropertyName("master_name")]
        public string MasterName { get; set; } = "";

        [JsonProperty("master_is_premium")]
        [JsonPropertyName("master_is_premium")]
        public bool MasterIsPremium { get; set; }

        [JsonProperty("premium_sponsor_steam_id")]
        [JsonPropertyName("premium_sponsor_steam_id")]
        public string? PremiumSponsorSteamId { get; set; }

        [JsonProperty("controls_chat_alerts")]
        [JsonPropertyName("controls_chat_alerts")]
        public bool ControlsChatAlerts { get; set; }

        [JsonProperty("controls_chat_commands")]
        [JsonPropertyName("controls_chat_commands")]
        public bool ControlsChatCommands { get; set; }

        [JsonProperty("elected_at")]
        [JsonPropertyName("elected_at")]
        public DateTime? ElectedAt { get; set; }

        [JsonProperty("updated_at")]
        [JsonPropertyName("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        [JsonProperty("expires_at")]
        [JsonPropertyName("expires_at")]
        public DateTime? ExpiresAt { get; set; }
    }

    [Table("discord_bot_settings")]
    public class DiscordBotSettingsModel : BaseModel
    {
        [PrimaryKey("guild_id", false)]
        [Column("guild_id")]
        [JsonProperty("guild_id")]
        [JsonPropertyName("guild_id")]
        public string GuildId { get; set; }

        [Column("owner_steam_id")]
        [JsonProperty("owner_steam_id")]
        [JsonPropertyName("owner_steam_id")]
        public string OwnerSteamId { get; set; }

        [Column("commands_enabled")]
        [JsonProperty("commands_enabled")]
        [JsonPropertyName("commands_enabled")]
        public bool CommandsEnabled { get; set; } = true;

        [Column("allowed_command_role_ids")]
        [JsonProperty("allowed_command_role_ids")]
        [JsonPropertyName("allowed_command_role_ids")]
        public string AllowedCommandRoleIds { get; set; } = "";

        [Column("created_at")]
        [JsonProperty("created_at")]
        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        [JsonProperty("updated_at")]
        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }

    public class DiscordBotRegistrationResult
    {
        [JsonProperty("success")]
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonProperty("guild_id")]
        [JsonPropertyName("guild_id")]
        public string GuildId { get; set; } = "";
    }

    [Table("discord_channels_config")]
    public class DiscordChannelsConfigModel : BaseModel
    {
        [PrimaryKey("id", true)]
        [Column("id")]
        [JsonProperty("id")]
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Column("guild_id")]
        [JsonProperty("guild_id")]
        [JsonPropertyName("guild_id")]
        public string GuildId { get; set; }

        [Column("notification_type")]
        [JsonProperty("notification_type")]
        [JsonPropertyName("notification_type")]
        public string NotificationType { get; set; }

        [Column("channel_id")]
        [JsonProperty("channel_id")]
        [JsonPropertyName("channel_id")]
        public string ChannelId { get; set; }

        [Column("tts_enabled")]
        [JsonProperty("tts_enabled")]
        [JsonPropertyName("tts_enabled")]
        public bool TtsEnabled { get; set; }

        [Column("audio_alert_enabled")]
        [JsonProperty("audio_alert_enabled")]
        [JsonPropertyName("audio_alert_enabled")]
        public bool AudioAlertEnabled { get; set; }

        [Column("created_at")]
        [JsonProperty("created_at")]
        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }
    }

    [Table("bot_commands_queue")]
    public class BotCommandsQueueModel : BaseModel
    {
        [PrimaryKey("id", true)]
        [Column("id")]
        [JsonProperty("id")]
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Column("guild_id")]
        [JsonProperty("guild_id")]
        [JsonPropertyName("guild_id")]
        public string GuildId { get; set; }

        [Column("command_type")]
        [JsonProperty("command_type")]
        [JsonPropertyName("command_type")]
        public string CommandType { get; set; }

        [Column("payload")]
        [JsonProperty("payload")]
        [JsonPropertyName("payload")]
        public Newtonsoft.Json.Linq.JObject? Payload { get; set; } // JSONB column

        [Column("status")]
        [JsonProperty("status")]
        [JsonPropertyName("status")]
        public string Status { get; set; } = "pending";

        [Column("response_payload")]
        [JsonProperty("response_payload")]
        [JsonPropertyName("response_payload")]
        public Newtonsoft.Json.Linq.JObject? ResponsePayload { get; set; } // JSONB column

        [Column("created_at")]
        [JsonProperty("created_at")]
        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        [JsonProperty("updated_at")]
        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }

    [Table("tier_limits")]
    public class TierLimitModel : BaseModel
    {
        [PrimaryKey("tier_code", false)]
        [Column("tier_code")]
        [JsonProperty("tier_code")]
        [JsonPropertyName("tier_code")]
        public string? TierCode { get; set; }

        [Column("max_overlay_kb")]
        [JsonProperty("max_overlay_kb")]
        [JsonPropertyName("max_overlay_kb")]
        public int? MaxOverlayKb { get; set; }

        [Column("max_devices")]
        [JsonProperty("max_devices")]
        [JsonPropertyName("max_devices")]
        public int? MaxDevices { get; set; }

        [Column("max_bases")]
        [JsonProperty("max_bases")]
        [JsonPropertyName("max_bases")]
        public int? MaxBases { get; set; }

        [Column("max_screenshots_per_base")]
        [JsonProperty("max_screenshots_per_base")]
        [JsonPropertyName("max_screenshots_per_base")]
        public int? MaxScreenshotsPerBase { get; set; }
    }

    [Table("base_markers")]
    public class BaseMarkerModel : BaseModel
    {
        [PrimaryKey("id", true)]
        [Column("id")]
        [JsonProperty("id")]
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Column("server_key")]
        [JsonProperty("server_key")]
        [JsonPropertyName("server_key")]
        public string ServerKey { get; set; }

        [Column("steam_id")]
        [JsonProperty("steam_id")]
        [JsonPropertyName("steam_id")]
        public string SteamId { get; set; }

        [Column("marker_data")]
        [JsonProperty("marker_data")]
        [JsonPropertyName("marker_data")]
        public string MarkerData { get; set; }

        [Column("created_at", NullValueHandling = NullValueHandling.Ignore)]
        [JsonProperty("created_at")]
        [JsonPropertyName("created_at")]
        public DateTime? CreatedAt { get; set; }

        [Column("updated_at")]
        [JsonProperty("updated_at")]
        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}
