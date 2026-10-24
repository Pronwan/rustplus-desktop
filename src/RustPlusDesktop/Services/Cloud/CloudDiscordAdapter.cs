using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.Cloud
{
    /// <summary>
    /// Translates the legacy <c>discord-bot/*</c> edge-function calls to the cloud
    /// Discord routes. This is not a straight route swap: the API wraps responses in a
    /// <c>data</c> envelope, derives the owner from the authenticated user, and keys
    /// guilds/channels by UUID rather than the Discord snowflake, so guild ids are
    /// resolved before the underlying call.
    /// </summary>
    internal static class CloudDiscordAdapter
    {
        /// <summary>True when this adapter handles the given legacy function name.</summary>
        public static bool Handles(string functionName) =>
            functionName is "discord-bot/settings" or "discord-bot/channels";

        public static Task<string> CallAsync(
            string functionName,
            HttpMethod method,
            object? payload,
            IDictionary<string, string>? queryParams)
        {
            return functionName == "discord-bot/settings"
                ? SettingsAsync(method, payload, queryParams)
                : ChannelsAsync(method, payload, queryParams);
        }

        private static async Task<string> SettingsAsync(HttpMethod method, object? payload, IDictionary<string, string>? queryParams)
        {
            // GET — the client expects a bare array of guild settings.
            if (method == HttpMethod.Get)
                return Unwrap(await CloudApiClient.CallApiAsync("discord/guilds", HttpMethod.Get));

            // POST — upsert; the API takes the owner from the bearer token.
            if (method == HttpMethod.Post)
                return Unwrap(await CloudApiClient.CallApiAsync("discord/guilds", HttpMethod.Post, null, payload));

            if (method == HttpMethod.Delete)
            {
                var guildId = Param(queryParams, "guild_id");
                var uuid = await ResolveGuildUuidAsync(guildId)
                    ?? throw new InvalidOperationException($"Discord guild '{guildId}' is not linked to this account.");

                return await CloudApiClient.CallApiAsync($"discord/guilds/{uuid}", HttpMethod.Delete);
            }

            throw new NotSupportedException($"discord-bot/settings does not support {method.Method}.");
        }

        private static async Task<string> ChannelsAsync(HttpMethod method, object? payload, IDictionary<string, string>? queryParams)
        {
            // The channel payload/query carries the Discord snowflake; resolve it first.
            var guildId = Param(queryParams, "guild_id") ?? PayloadValue(payload, "guild_id");
            var uuid = await ResolveGuildUuidAsync(guildId)
                ?? throw new InvalidOperationException($"Discord guild '{guildId}' is not linked to this account.");

            if (method == HttpMethod.Get)
                return Unwrap(await CloudApiClient.CallApiAsync($"discord/guilds/{uuid}/channels", HttpMethod.Get));

            if (method == HttpMethod.Post)
                return Unwrap(await CloudApiClient.CallApiAsync($"discord/guilds/{uuid}/channels", HttpMethod.Post, null, payload));

            if (method == HttpMethod.Delete)
            {
                var type = Param(queryParams, "notification_type");
                var channelUuid = await ResolveChannelUuidAsync(uuid, type);
                if (channelUuid == null)
                    return "{\"status\":\"success\"}"; // Already absent — deleting is a no-op.

                return await CloudApiClient.CallApiAsync($"discord/channels/{channelUuid}", HttpMethod.Delete);
            }

            throw new NotSupportedException($"discord-bot/channels does not support {method.Method}.");
        }

        /// <summary>Map a Discord guild snowflake to its platform UUID for the current user.</summary>
        private static async Task<string?> ResolveGuildUuidAsync(string? guildId)
        {
            if (string.IsNullOrWhiteSpace(guildId)) return null;

            var body = await CloudApiClient.CallApiAsync("discord/guilds", HttpMethod.Get);
            foreach (var guild in EnumerateData(body))
            {
                if (StringProp(guild, "guild_id") == guildId)
                    return StringProp(guild, "id");
            }

            return null;
        }

        private static async Task<string?> ResolveChannelUuidAsync(string guildUuid, string? notificationType)
        {
            if (string.IsNullOrWhiteSpace(notificationType)) return null;

            var body = await CloudApiClient.CallApiAsync($"discord/guilds/{guildUuid}/channels", HttpMethod.Get);
            foreach (var channel in EnumerateData(body))
            {
                if (StringProp(channel, "notification_type") == notificationType)
                    return StringProp(channel, "id");
            }

            return null;
        }

        /// <summary>Strip the API's <c>data</c> envelope so legacy deserialisation still works.</summary>
        private static string Unwrap(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("data", out var data))
                {
                    return data.GetRawText();
                }
            }
            catch (JsonException)
            {
                // Not JSON (e.g. an empty 204 body) — hand back as-is.
            }

            return body;
        }

        private static IEnumerable<JsonElement> EnumerateData(string body)
        {
            List<JsonElement> items = new();
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data))
                    root = data;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                        items.Add(item.Clone());
                }
            }
            catch (JsonException)
            {
                // Unparseable body — treat as empty.
            }

            return items;
        }

        private static string? StringProp(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static string? Param(IDictionary<string, string>? queryParams, string key) =>
            queryParams != null && queryParams.TryGetValue(key, out var value) ? value : null;

        /// <summary>Read a property off an anonymous payload object via its JSON form.</summary>
        private static string? PayloadValue(object? payload, string name)
        {
            if (payload == null) return null;

            try
            {
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
                return StringProp(doc.RootElement, name);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Safely extracts channel configurations from arbitrary JSON formats
        /// (array of guilds with 'channels' or 'discord_channels_config', direct array of channels, or single object).
        /// </summary>
        public static List<RustPlusDesk.Models.DiscordChannelsConfigModel> ExtractChannelsConfig(string? json, string? targetGuildId = null)
        {
            var list = new List<RustPlusDesk.Models.DiscordChannelsConfigModel>();
            if (string.IsNullOrWhiteSpace(json)) return list;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    // Check if array items are directly channel config objects (have "notification_type")
                    bool isDirectChannelList = false;
                    foreach (var el in root.EnumerateArray())
                    {
                        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("notification_type", out _))
                        {
                            isDirectChannelList = true;
                            break;
                        }
                    }

                    if (isDirectChannelList)
                    {
                        return JsonSerializer.Deserialize<List<RustPlusDesk.Models.DiscordChannelsConfigModel>>(
                            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? list;
                    }

                    // Otherwise elements are guild objects
                    foreach (var guildEl in root.EnumerateArray())
                    {
                        if (guildEl.ValueKind != JsonValueKind.Object) continue;

                        if (!string.IsNullOrEmpty(targetGuildId) &&
                            guildEl.TryGetProperty("guild_id", out var gId) &&
                            gId.GetString() != targetGuildId)
                        {
                            continue;
                        }

                        ExtractChannelsFromGuildElement(guildEl, list);
                        if (list.Count > 0 || !string.IsNullOrEmpty(targetGuildId))
                            break;
                    }
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("notification_type", out _))
                    {
                        var single = JsonSerializer.Deserialize<RustPlusDesk.Models.DiscordChannelsConfigModel>(
                            root.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (single != null) list.Add(single);
                    }
                    else
                    {
                        ExtractChannelsFromGuildElement(root, list);
                    }
                }
            }
            catch (JsonException)
            {
                // Unparseable JSON — treat as empty.
            }

            return list;
        }

        private static void ExtractChannelsFromGuildElement(JsonElement guildEl, List<RustPlusDesk.Models.DiscordChannelsConfigModel> list)
        {
            if (guildEl.TryGetProperty("channels", out var channelsEl) && channelsEl.ValueKind == JsonValueKind.Array)
            {
                var items = JsonSerializer.Deserialize<List<RustPlusDesk.Models.DiscordChannelsConfigModel>>(
                    channelsEl.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (items != null) list.AddRange(items);
            }
            else if (guildEl.TryGetProperty("discord_channels_config", out var configEl) && configEl.ValueKind == JsonValueKind.Array)
            {
                var items = JsonSerializer.Deserialize<List<RustPlusDesk.Models.DiscordChannelsConfigModel>>(
                    configEl.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (items != null) list.AddRange(items);
            }
        }
    }
}
