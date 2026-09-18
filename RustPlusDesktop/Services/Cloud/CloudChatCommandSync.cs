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

            var payload = new
            {
                server_key = serverKey,
                prefix = "!",
                words = cleaned,
                device_mappings = mappings,
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
