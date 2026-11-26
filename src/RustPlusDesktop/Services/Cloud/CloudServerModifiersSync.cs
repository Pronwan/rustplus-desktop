using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Auth;

namespace RustPlusDesk.Services.Cloud
{
    /// <summary>
    /// A modded server's upkeep cost and oil rig crate time, kept the same here, on the website
    /// and in the cloud.
    ///
    /// Set in the app, they go up at once (PATCH me/servers/{key}); one that cannot go up yet
    /// stays marked and goes up with the next pairing import, which until then leaves the local
    /// values alone. Set on the website, they come down with that import.
    /// </summary>
    public static class CloudServerModifiersSync
    {
        /// <summary>Sends this server's modifiers to the platform; true when it took them.</summary>
        public static async Task<bool> PushAsync(ServerProfile profile)
        {
            if (profile == null || profile.IsFakeSandbox) return false;
            if (!CloudBackend.UsePlatform || !CloudAuthManager.IsAuthenticated) return false;

            var key = profile.PlatformServerKey;
            if (string.IsNullOrWhiteSpace(key)) return false;

            try
            {
                await CloudApiClient.CallApiAsync($"me/servers/{Uri.EscapeDataString(key)}", new HttpMethod("PATCH"), payload: new
                {
                    upkeep_percent = profile.UpkeepPercent,
                    oil_rig_crate_minutes = profile.OilRigCrateMinutes,
                });

                profile.ModifiersPushPending = false;
                SupabaseAuthManager.AppendLog($"[Server modifiers] '{profile.Name}': upkeep {profile.UpkeepPercent} %, oil rig crate {profile.OilRigCrateMinutes} min saved to the cloud.");
                return true;
            }
            catch (Exception ex)
            {
                // A server the platform does not have yet (not synced, or never paired there):
                // kept pending and sent again with the next import.
                SupabaseAuthManager.AppendLog($"[Server modifiers] '{profile.Name}' not saved to the cloud yet: {ex.Message}");
                return false;
            }
        }

        /// <summary>Sends every server whose modifiers changed here and did not reach the platform.</summary>
        public static async Task RetryPendingAsync(IEnumerable<ServerProfile> profiles)
        {
            foreach (var profile in profiles.Where(p => !p.IsFakeSandbox && p.ModifiersPushPending).ToList())
                await PushAsync(profile);
        }

        /// <summary>
        /// Takes the platform's modifiers for a server from a pairing import entry, unless a change
        /// made here has not gone up yet. Returns whether anything changed.
        /// </summary>
        internal static bool Apply(ServerProfile profile, JsonElement entry)
        {
            if (profile.ModifiersPushPending) return false;
            if (!entry.TryGetProperty("modifiers", out var modifiers) || modifiers.ValueKind != JsonValueKind.Object) return false;

            int upkeep = ReadInt(modifiers, "upkeep_percent", ServerProfile.VanillaUpkeepPercent);
            int crate = ReadInt(modifiers, "oil_rig_crate_minutes", ServerProfile.VanillaOilRigCrateMinutes);
            if (profile.UpkeepPercent == upkeep && profile.OilRigCrateMinutes == crate) return false;

            profile.UpkeepPercent = upkeep;
            profile.OilRigCrateMinutes = crate;
            SupabaseAuthManager.AppendLog($"[Server modifiers] '{profile.Name}': upkeep {upkeep} %, oil rig crate {crate} min taken from the cloud.");
            return true;
        }

        private static int ReadInt(JsonElement obj, string name, int fallback)
            => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : fallback;
    }
}
