using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Auth;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.Cloud
{
    /// <summary>
    /// Send this server's chat command words up, so the cloud answers the same way.
    ///
    /// The command words live in the local <see cref="ServerProfile"/> and always
    /// have. That was fine while this app was the only thing that could answer them.
    /// Now the cloud answers too, whenever the app is closed — and if it answers to
    /// different words, or with different replies, teammates get two different bots
    /// depending on who happens to have the app running.
    ///
    /// Sent rather than fetched: the app is where the user edits them, so it is the
    /// source of truth. The reverse direction exists only to restore a
    /// reinstalled app, and arrives through the pairing import.
    /// </summary>
    public static class CloudChatCommandSync
    {
        /// <summary>Hashes of what was last sent, so an unchanged profile costs nothing.</summary>
        private static readonly Dictionary<string, int> LastSent = new();

        /// <summary>
        /// Push the command config for one server.
        ///
        /// Skipped silently when nothing has changed since the last push: this runs
        /// on connect, and re-sending an identical payload every time somebody
        /// reconnects would be traffic for its own sake.
        /// </summary>
        public static async Task SyncAsync(ServerProfile profile, string serverKey)
        {
            if (!CloudBackend.UsePlatform || !CloudAuthManager.IsAuthenticated) return;
            if (profile == null || string.IsNullOrWhiteSpace(serverKey)) return;

            var words = new Dictionary<string, string?>
            {
                ["pop"] = profile.CmdPop,
                ["time"] = profile.CmdTime,
                ["afk"] = profile.CmdAfk,
                ["promote"] = profile.CmdPromote,
                ["list"] = profile.CmdList,
                ["upkeep"] = profile.CmdUpkeepDetail,
                ["base_codes"] = profile.CmdBaseCodes,
            };

            // Only the words the user actually set. Sending empty keys would let a
            // blank command word match an empty message in chat.
            var cleaned = words
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value!);

            var mappings = profile.SwitchCommandMappings
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Command) && m.EntityId != 0)
                .Select(m => new { command = m.Command, entity_id = m.EntityId })
                .ToList();

            // The user's real prefix, not a hard-coded "!". Sending the wrong one
            // left the cloud deaf to the word their team actually types while the
            // app answered it - two bots disagreeing in front of the team, which is
            // the exact failure syncing this config exists to prevent.
            var prefix = string.IsNullOrWhiteSpace(profile.ChatCommandPrefix)
                ? "!"
                : profile.ChatCommandPrefix;

            var payload = new
            {
                server_key = serverKey,
                prefix,
                // Somebody who switched commands off in the app meant it, and the
                // cloud answering during the hours the app is shut would be the
                // feature overriding that rather than extending it.
                commands_enabled = profile.ChatCommandsEnabled,
                use_clan_channel = profile.ChatAlertsUseClanChannel,
                words = cleaned,
                device_mappings = mappings,
                alerts = new Dictionary<string, bool>
                {
                    ["player_online"] = TrackingService.AnnouncePlayerOnline,
                    ["player_offline"] = TrackingService.AnnouncePlayerOffline,
                    ["player_afk"] = TrackingService.AnnouncePlayerAfk,
                    ["player_afk_return"] = TrackingService.AnnouncePlayerAfkReturn,
                    ["player_death_self"] = TrackingService.AnnouncePlayerDeathSelf,
                    ["player_death_team"] = TrackingService.AnnouncePlayerDeathTeam,
                    ["player_respawn_self"] = TrackingService.AnnouncePlayerRespawnSelf,
                    ["player_respawn_team"] = TrackingService.AnnouncePlayerRespawnTeam,
                    // A smart alarm firing, and the special case of one the user
                    // set as an oil rig trigger — which means a crate is being
                    // hacked rather than somebody being in their base.
                    ["smart_alerts"] = TrackingService.AnnounceSmartAlerts,
                    ["event_oil_rig"] = TrackingService.AnnounceOilRig,
                    // The master switch, sent as its own value rather than folded
                    // into the others. Folding it in would read on the web as the
                    // user having turned every alert off one by one, and turning
                    // the master back on would not restore what they actually had.
                    ["announce_master"] = TrackingService.AnnounceSpawnsMaster,
                },
            };

            var signature = System.Text.Json.JsonSerializer.Serialize(payload).GetHashCode();

            lock (LastSent)
            {
                if (LastSent.TryGetValue(serverKey, out var previous) && previous == signature)
                    return;
            }

            try
            {
                await CloudApiClient.CallApiAsync("sync/chat-commands", HttpMethod.Put, payload: payload);

                lock (LastSent)
                {
                    LastSent[serverKey] = signature;
                }
            }
            catch (Exception ex)
            {
                // Not fatal: the cloud falls back to defaults, which is worse than
                // matching but far better than refusing to connect over it.
                SupabaseAuthManager.AppendLog($"[Cloud commands] Could not sync command words: {ex.Message}");
            }
        }

        /// <summary>Forget what was sent, e.g. after signing out.</summary>
        public static void Reset()
        {
            lock (LastSent)
            {
                LastSent.Clear();
            }
        }
    }
}
