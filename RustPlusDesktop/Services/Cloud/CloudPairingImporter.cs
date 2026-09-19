using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Auth;
using RustPlusDesk.Services.Data;

namespace RustPlusDesk.Services.Cloud
{
    /// <summary>
    /// Pairings the cloud received while this app was closed.
    ///
    /// This app has always pushed pairings up. The cloud now holds the Facepunch
    /// push registration, so it also *receives* them — which creates a direction
    /// that never existed before: a server paired in game with the app shut lives
    /// on the platform and nowhere else. Without this the user pairs a server, sees
    /// it working on the website, opens the app, and finds it missing.
    ///
    /// The merge is deliberately one-sided. A server the app has never seen is
    /// created outright; a server it already knows keeps everything the platform has
    /// no business deciding — command words, layout, notes, alarm titles — and takes
    /// only the token, which is the part a re-pair actually changes. The platform
    /// wins where it is the source of truth and nowhere else.
    /// </summary>
    public static class CloudPairingImporter
    {
        /// <summary>
        /// Where the last successful import got to.
        ///
        /// Sent back as <c>since</c>, so the usual case on launch is an empty reply
        /// rather than re-reading and re-merging every server every time.
        /// </summary>
        private const string CursorKey = "cloud_pairings_cursor";

        /// <summary>What an import did, so the caller can say something useful.</summary>
        public sealed record ImportResult(int Added, int Updated, int DevicesAdded)
        {
            public bool ChangedAnything => Added > 0 || Updated > 0 || DevicesAdded > 0;
        }

        /// <summary>
        /// Pull down anything new and merge it into the local profiles.
        ///
        /// Returns null when the platform could not be reached — which is different
        /// from "nothing new", and the caller should not treat a network failure as
        /// proof that no servers were paired.
        /// </summary>
        public static async Task<ImportResult?> ImportAsync(bool full = false)
        {
            if (!CloudBackend.UsePlatform || !CloudAuthManager.IsAuthenticated) return null;

            string? cursor = full ? null : StorageService.LoadCache<string>(CursorKey);

            string body;
            try
            {
                var route = "me/pairings";
                if (!string.IsNullOrWhiteSpace(cursor))
                    route += "?since=" + Uri.EscapeDataString(cursor!);

                body = await CloudApiClient.CallApiAsync(route, HttpMethod.Get);
            }
            catch (Exception ex)
            {
                SupabaseAuthManager.AppendLog($"[Cloud pairings] Could not check for new pairings: {ex.Message}");
                return null;
            }

            try
            {
                return Merge(body);
            }
            catch (Exception ex)
            {
                // A malformed reply must not corrupt the profile file: the cursor is
                // left alone so the next run tries the same window again.
                SupabaseAuthManager.AppendLog($"[Cloud pairings] Could not apply pairings: {ex.Message}");
                return null;
            }
        }

        private static ImportResult Merge(string body)
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return new ImportResult(0, 0, 0);

            var profiles = StorageService.LoadProfiles() ?? new List<ServerProfile>();
            int added = 0, updated = 0, devicesAdded = 0;

            foreach (var entry in data.EnumerateArray())
            {
                var host = Str(entry, "host");
                var port = Int(entry, "port");
                var token = Str(entry, "player_token");

                // A server we cannot dial is not worth a profile row.
                if (string.IsNullOrWhiteSpace(host) || port <= 0 || string.IsNullOrWhiteSpace(token))
                    continue;

                var existing = profiles.FirstOrDefault(p =>
                    string.Equals(p.Host, host, StringComparison.OrdinalIgnoreCase) && p.Port == port);

                if (existing == null)
                {
                    existing = new ServerProfile
                    {
                        Host = host!,
                        Port = port,
                        Name = Str(entry, "name") ?? host!,
                        PlayerToken = token!,
                    };

                    profiles.Add(existing);
                    added++;
                }
                else if (!string.Equals(existing.PlayerToken, token, StringComparison.Ordinal))
                {
                    // The one field a re-pair actually changes, and the reason a wipe
                    // leaves the app unable to connect until this runs. Everything
                    // else on the profile is the user's and is left alone.
                    existing.PlayerToken = token!;
                    updated++;
                }

                devicesAdded += MergeDevices(existing, entry);
                MergeChatCommands(existing, entry);
            }

            if (added > 0 || updated > 0 || devicesAdded > 0)
                StorageService.SaveProfiles(profiles);

            // Only advance the cursor once the merge has been written. Moving it
            // first would silently skip a window if saving then failed.
            var asOf = root.TryGetProperty("meta", out var meta) ? Str(meta, "as_of") : null;
            if (!string.IsNullOrWhiteSpace(asOf))
                StorageService.SaveCache(CursorKey, asOf!);

            return new ImportResult(added, updated, devicesAdded);
        }

        /// <summary>
        /// Add devices the app does not have, and refresh the names of ones it does.
        ///
        /// Matched on entity id rather than name, because renaming is exactly what
        /// re-pairing a device does — matching on name would add a second row and
        /// leave chat commands with two things to answer to.
        ///
        /// The alias and the icon are taken only when the platform says they were
        /// set on the website. That flag is the whole reason the sync is two-way
        /// rather than the platform winning: without it, a name this app pushed
        /// would come straight back and overwrite a rename made here since.
        /// </summary>
        private static int MergeDevices(ServerProfile profile, JsonElement entry)
        {
            if (!entry.TryGetProperty("devices", out var devices) || devices.ValueKind != JsonValueKind.Array)
                return 0;

            int added = 0;

            foreach (var group in devices.EnumerateArray())
            {
                if (!group.TryGetProperty("device_data", out var list) || list.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var item in list.EnumerateArray())
                {
                    var entityId = UInt(item, "EntityId");
                    if (entityId == 0) continue;

                    var name = Str(item, "Name");
                    var kind = Str(item, "Kind");
                    var alias = Str(item, "Alias");
                    var icon = Str(item, "CustomIconShortName");

                    var device = profile.Devices.FirstOrDefault(d => d.EntityId == entityId);

                    if (device == null)
                    {
                        profile.Devices.Add(new SmartDevice
                        {
                            EntityId = entityId,
                            Name = name,
                            Kind = kind,
                            Alias = alias,
                            CustomIconShortName = icon,
                        });
                        added++;
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(name) && device.Name != name)
                    {
                        device.Name = name;
                    }

                    if (Bool(item, "AliasFromWeb") && device.Alias != alias)
                    {
                        device.Alias = alias;
                    }

                    if (Bool(item, "IconFromWeb") && device.CustomIconShortName != icon)
                    {
                        device.CustomIconShortName = icon;
                        // The id and the short name name the same picture, and a
                        // stale id would win when the icon is resolved.
                        device.CustomIconId = null;
                    }
                }
            }

            return added;
        }

        /// <summary>
        /// Take the command words and device bindings the platform is serving.
        ///
        /// The platform answers with the effective config — what the user last
        /// chose, wherever they chose it — so a word set on the website reaches the
        /// app here rather than only ever travelling app-to-cloud. A binding is
        /// matched on entity id, because the word is the thing being changed and
        /// matching on it would rename nothing and add a duplicate.
        /// </summary>
        private static void MergeChatCommands(ServerProfile profile, JsonElement entry)
        {
            if (!entry.TryGetProperty("chat_commands", out var chat) || chat.ValueKind != JsonValueKind.Object)
                return;

            var prefix = Str(chat, "prefix");
            if (!string.IsNullOrWhiteSpace(prefix))
                profile.ChatCommandPrefix = prefix!;

            if (chat.TryGetProperty("words", out var words) && words.ValueKind == JsonValueKind.Object)
            {
                foreach (var word in words.EnumerateObject())
                {
                    if (word.Value.ValueKind != JsonValueKind.String) continue;

                    var value = word.Value.GetString();
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    switch (word.Name)
                    {
                        case "pop": profile.CmdPop = value!; break;
                        case "time": profile.CmdTime = value!; break;
                        case "afk": profile.CmdAfk = value!; break;
                        case "promote": profile.CmdPromote = value!; break;
                        case "list": profile.CmdList = value!; break;
                        case "upkeep": profile.CmdUpkeepDetail = value!; break;
                        case "base_codes": profile.CmdBaseCodes = value!; break;
                    }
                }
            }

            if (!chat.TryGetProperty("device_mappings", out var mappings) || mappings.ValueKind != JsonValueKind.Array)
                return;

            foreach (var mapping in mappings.EnumerateArray())
            {
                var entityId = UInt(mapping, "entity_id");
                var command = Str(mapping, "command");

                if (entityId == 0 || string.IsNullOrWhiteSpace(command)) continue;

                var existing = profile.SwitchCommandMappings.FirstOrDefault(m => m.EntityId == entityId);

                if (existing == null)
                {
                    profile.SwitchCommandMappings.Add(new ChatCommandMapping
                    {
                        EntityId = entityId,
                        Command = command!,
                        Label = profile.Devices.FirstOrDefault(d => d.EntityId == entityId)?.PureName ?? "",
                    });
                }
                else if (!string.Equals(existing.Command, command, StringComparison.Ordinal))
                {
                    existing.Command = command!;
                }
            }
        }

        /// <summary>Forget where we got to, so the next import re-reads everything.</summary>
        public static void ResetCursor() => StorageService.SaveCache(CursorKey, string.Empty);

        private static string? Str(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static int Int(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

        private static bool Bool(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        private static uint UInt(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
                && v.TryGetUInt32(out var parsed) ? parsed : 0;
    }
}
