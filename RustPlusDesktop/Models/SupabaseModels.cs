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
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Column("server_key")]
        public string ServerKey { get; set; }

        [Column("steam_id")]
        public string SteamId { get; set; }

        [Column("overlay_data")]
        public string OverlayData { get; set; } // We can store the JSON string directly

        [Column("created_at", NullValueHandling = NullValueHandling.Ignore)]
        public DateTime? CreatedAt { get; set; }

        [Column("uncompressed_size")]
        public int UncompressedSize { get; set; }

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

        [Column("created_at", NullValueHandling = NullValueHandling.Ignore)]
        public DateTime? CreatedAt { get; set; }

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

        [Column("premium_until")]
        public DateTime? PremiumUntil { get; set; }

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
        public string GuildId { get; set; }

        [Column("owner_steam_id")]
        public string OwnerSteamId { get; set; }

        [Column("commands_enabled")]
        public bool CommandsEnabled { get; set; } = true;

        [Column("allowed_command_role_ids")]
        public string AllowedCommandRoleIds { get; set; } = "";

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
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
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Column("guild_id")]
        public string GuildId { get; set; }

        [Column("notification_type")]
        public string NotificationType { get; set; }

        [Column("channel_id")]
        public string ChannelId { get; set; }

        [Column("tts_enabled")]
        public bool TtsEnabled { get; set; }

        [Column("audio_alert_enabled")]
        public bool AudioAlertEnabled { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }

    [Table("bot_commands_queue")]
    public class BotCommandsQueueModel : BaseModel
    {
        [PrimaryKey("id", true)]
        [Column("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Column("guild_id")]
        public string GuildId { get; set; }

        [Column("command_type")]
        public string CommandType { get; set; }

        [Column("payload")]
        public Newtonsoft.Json.Linq.JObject? Payload { get; set; } // JSONB column

        [Column("status")]
        public string Status { get; set; } = "pending";

        [Column("response_payload")]
        public Newtonsoft.Json.Linq.JObject? ResponsePayload { get; set; } // JSONB column

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}


