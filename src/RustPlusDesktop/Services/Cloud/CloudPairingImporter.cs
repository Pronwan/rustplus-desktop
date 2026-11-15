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
        /// Versioned: before v2 an entry naming its server by Facepunch id alone was skipped
        /// while the cursor still moved past it, so those pairings were never retried. The
        /// new key starts empty, which makes the first import after the upgrade a full one.
        private const string CursorKey = "cloud_pairings_cursor_v2";

        /// <summary>Host and port of a profile for a server known only by Facepunch's id.</summary>
        internal const string CompanionSocketHost = "companion-socket";
        internal const int CompanionSocketPort = 28082;

        /// <summary>The cursor of the last import, sent with device uploads as <c>pairings_as_of</c>.</summary>
        public static string? Cursor
        {
            get
            {
                var cursor = StorageService.LoadCache<string>(CursorKey);
                return string.IsNullOrWhiteSpace(cursor) ? null : cursor;
            }
        }

        /// <summary>
        /// Servers removed in this app whose cloud pairing may still be live.
        ///
        /// The platform keeps a pairing until it is told otherwise, and a restore brings
        /// back everything it keeps. A removal made while signed out or offline would
        /// otherwise come straight back on the next restore. Each entry is held until
        /// the platform has confirmed the delete, and imports skip it meanwhile.
        /// </summary>
        private const string RemovedKey = "cloud_pairings_removed";

        /// <summary>What an import did, so the caller can say something useful.</summary>
        public sealed record ImportResult(int Added, int Updated, int DevicesAdded)
        {
            public bool ChangedAnything => Added > 0 || Updated > 0 || DevicesAdded > 0;
        }

        /// <summary>
        /// Pull down anything new and merge it into the profiles file.
        ///
        /// Returns null when the platform could not be reached — which is different
        /// from "nothing new", and the caller should not treat a network failure as
        /// proof that no servers were paired.
        /// </summary>
        public static Task<ImportResult?> ImportAsync(bool full = false)
        {
            var profiles = StorageService.LoadProfiles() ?? new List<ServerProfile>();
            return ImportIntoAsync(profiles, () => StorageService.SaveProfiles(profiles), full);
        }

        /// <summary>
        /// Merge into a list the caller owns, then let it save.
        ///
        /// The main window keeps its own copy of the profiles and writes that copy back
        /// whenever anything changes, so an import written straight to the file was undone
        /// by the next save. Merging into the live list is what makes a restore stick.
        /// The merge resumes on the caller's context, so a UI-thread caller can hand in a
        /// bound collection.
        ///
        /// <paramref name="full"/> ignores the cursor. That is the restore path: the cursor
        /// only says what the profiles file once held, and a lost or reset file holds
        /// nothing, so "only what changed" would bring back nothing.
        /// </summary>
        public static async Task<ImportResult?> ImportIntoAsync(IList<ServerProfile> profiles, Action save, bool full,
            IReadOnlyDictionary<string, IReadOnlyCollection<uint>>? removals = null)
        {
            if (!CloudBackend.UsePlatform || !CloudAuthManager.IsAuthenticated) return null;

            // Before reading: a removal still pending here would otherwise be imported
            // straight back by this very call.
            await RetryPendingRemovalsAsync(profiles);

            // Modifiers changed here that did not reach the platform yet go up first, so the
            // import below does not hand back the platform's older values.
            if (profiles.Any(p => p.ModifiersPushPending))
            {
                await CloudServerModifiersSync.RetryPendingAsync(profiles);
                save();
            }

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
                return Merge(body, profiles, save, removals);
            }
            catch (Exception ex)
            {
                // A malformed reply must not corrupt the profile file: the cursor is
                // left alone so the next run tries the same window again.
                SupabaseAuthManager.AppendLog($"[Cloud pairings] Could not apply pairings: {ex.Message}");
                return null;
            }
        }

        /// <param name="removals">
        /// Devices the website deleted, by platform server key, from the devices_changed event
        /// that announced it. Removed here only when the platform's list no longer has them.
        /// </param>
        internal static ImportResult Merge(string body, IList<ServerProfile> profiles, Action save,
            IReadOnlyDictionary<string, IReadOnlyCollection<uint>>? removals = null)
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return new ImportResult(0, 0, 0);

            int added = 0, updated = 0, devicesAdded = 0;
            var removed = LoadRemoved();

            foreach (var entry in data.EnumerateArray())
            {
                var host = Str(entry, "host");
                var port = Int(entry, "port");
                var token = Str(entry, "player_token");
                var serverId = Guid.TryParse(Str(entry, "facepunch_id"), out var fpId) ? fpId.ToString("D") : null;
                bool hasAddress = !string.IsNullOrWhiteSpace(host) && port > 0
                    && !host!.Contains(CompanionSocketHost, StringComparison.OrdinalIgnoreCase);

                // A server we can neither dial nor reach through the companion socket is not
                // worth a profile row. Since Rust's November 2026 update a pairing names its
                // server by Facepunch's id alone, which is enough for the socket.
                if ((!hasAddress && serverId == null) || string.IsNullOrWhiteSpace(token))
                    continue;

                // By Facepunch's id first: a profile for an id-only server has the placeholder
                // host, and two such servers would otherwise look like the same one.
                var existing = (serverId == null ? null : profiles.FirstOrDefault(p =>
                        string.Equals(p.ServerId, serverId, StringComparison.OrdinalIgnoreCase)))
                    ?? (hasAddress ? profiles.FirstOrDefault(p =>
                        string.Equals(p.Host, host, StringComparison.OrdinalIgnoreCase) && p.Port == port) : null);

                if (existing == null)
                {
                    // Removed here and not yet confirmed gone on the platform.
                    if (hasAddress && removed.ContainsKey(RemovalKey(host!, port))) continue;
                    if (!hasAddress && RemovedServerStore.IsRemovedServerPairing(
                            new PairingPayload { ServerId = serverId }, UpdatedAt(entry))) continue;

                    existing = new ServerProfile
                    {
                        Host = hasAddress ? host! : CompanionSocketHost,
                        Port = hasAddress ? port : CompanionSocketPort,
                        ServerId = serverId,
                        UseFacepunchProxy = !hasAddress,
                        // A newly paired server answers chat commands, as the cloud does by
                        // default. Only for a profile created here: an existing one keeps its choice.
                        ChatCommandsEnabled = true,
                        Name = Str(entry, "name") ?? (hasAddress ? host! : "Rust Server"),
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

                // The token belongs to one Steam account and the server checks the pair,
                // so a profile without it cannot connect. Only filled when missing: a
                // profile that has one already learned it from the pairing itself.
                var steamId = SteamId(entry);
                if (string.IsNullOrWhiteSpace(existing.SteamId64) && steamId != null)
                {
                    existing.SteamId64 = steamId;
                    updated++;
                }

                // Facepunch's server id, which the companion socket needs. Filled when the app
                // has none (a pairing it never saw, or one paired before ids were kept); never
                // overwrites one the app already learned from a push.
                var facepunchId = Str(entry, "facepunch_id");
                if (string.IsNullOrWhiteSpace(existing.ServerId) && Guid.TryParse(facepunchId, out var parsedId))
                {
                    existing.ServerId = parsedId.ToString("D");
                    updated++;
                }

                // Where an id-only server is, for display. Written to its own fields and nowhere
                // else: Host, Port and the proxy flag decide how the profile connects and how it is
                // matched, and an id-only server keeps connecting and matching by ServerId.
                var reportedHost = Str(entry, "reported_host");
                var reportedPort = Int(entry, "reported_port");
                if (!existing.HasRealAddress && !string.IsNullOrWhiteSpace(reportedHost) && reportedPort > 0
                    && (existing.ReportedHost != reportedHost || existing.ReportedPort != reportedPort))
                {
                    existing.ReportedHost = reportedHost;
                    existing.ReportedPort = reportedPort;
                    updated++;
                }

                var serverKey = Str(entry, "server_key");
                IReadOnlyCollection<uint>? removedHere = null;
                if (removals != null && serverKey != null) removals.TryGetValue(serverKey, out removedHere);

                var (devAdded, devChanged) = MergeDevices(existing, entry, removedHere);
                devicesAdded += devAdded;
                // Renames, icons, removals and command words change the profile too, and were
                // never saved when they were the only change: the next save of anything else
                // was what finally wrote them, or the next upload overwrote them.
                updated += devChanged;

                // A command word edited here and not pushed yet (the push follows the save a
                // moment later) wins: the platform still holds the old word, and taking it would
                // undo the edit. The push then settles the platform, and the next import agrees.
                // A modded server's upkeep cost and crate time, set on the website.
                if (CloudServerModifiersSync.Apply(existing, entry))
                    updated++;

                if (CloudChatCommandSync.HasPendingChanges(existing, existing.ServerKey))
                    SupabaseAuthManager.AppendLog($"[Cloud pairings] Kept unsent command changes for {existing.Name}; the cloud's copy is older.");
                else
                    updated += MergeChatCommands(existing, entry);
            }

            if (added > 0 || updated > 0 || devicesAdded > 0)
                save();

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
        private static (int Added, int Changed) MergeDevices(ServerProfile profile, JsonElement entry, IReadOnlyCollection<uint>? removedOnWeb = null)
        {
            if (!entry.TryGetProperty("devices", out var devices) || devices.ValueKind != JsonValueKind.Array)
                return (0, 0);

            int added = 0, changed = 0;
            var inCloud = new HashSet<uint>();

            foreach (var group in devices.EnumerateArray())
            {
                if (!group.TryGetProperty("device_data", out var list) || list.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var item in list.EnumerateArray())
                {
                    var entityId = UInt(item, "EntityId");
                    if (entityId == 0) continue;
                    inCloud.Add(entityId);

                    // A pairing the cloud recorded after the user deleted the device here is the
                    // device being paired again in game: the deletion no longer applies.
                    if (profile.RemovedDevices.TryGetValue(entityId, out var removedAt)
                        && CloudPairedAt(item) is DateTime pairedAt && pairedAt > removedAt)
                    {
                        profile.RemovedDevices.Remove(entityId);
                        changed++;
                    }

                    var name = Str(item, "Name");
                    var kind = Str(item, "Kind");
                    var alias = Str(item, "Alias");
                    var icon = Str(item, "CustomIconShortName");

                    // Searched through groups too: a device the user filed under a group is
                    // not at the top level, and was added a second time when only that was checked.
                    var device = FindDevice(profile.Devices, entityId);

                    if (device == null)
                    {
                        // Deleted here. The cloud row lingers when the upload that removes it
                        // failed or is still pending; pairing it again in game clears the
                        // record through the pairing push or the history sync.
                        if (profile.RemovedDevices.ContainsKey(entityId)) continue;

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
                        changed++;
                    }

                    if (Bool(item, "AliasFromWeb") && device.Alias != alias)
                    {
                        device.Alias = alias;
                        changed++;
                    }

                    if (Bool(item, "IconFromWeb") && device.CustomIconShortName != icon)
                    {
                        device.CustomIconShortName = icon;
                        // The id and the short name name the same picture, and a
                        // stale id would win when the icon is resolved.
                        device.CustomIconId = null;
                        changed++;
                    }
                }
            }

            // Deleted on the website. Only what the event named and the platform no longer
            // lists: a device missing from the list for any other reason (an upload still on
            // its way, an older platform) is left alone, as the merge always has. Recorded as
            // removed so Facepunch's notification history does not bring it straight back.
            if (removedOnWeb != null)
            {
                foreach (var entityId in removedOnWeb)
                {
                    if (entityId == 0 || inCloud.Contains(entityId)) continue;
                    var device = FindDevice(profile.Devices, entityId);
                    if (device == null) continue;

                    RemoveDevice(profile.Devices, device);
                    profile.MarkDeviceRemoved(device);
                    foreach (var m in profile.SwitchCommandMappings.Where(m => m.EntityId == entityId).ToList())
                        profile.SwitchCommandMappings.Remove(m);
                    foreach (var m in profile.UpkeepCommandMappings.Where(m => m.EntityId == entityId).ToList())
                        profile.UpkeepCommandMappings.Remove(m);
                    changed++;
                }
            }

            return (added, changed);
        }

        private static bool RemoveDevice(IList<SmartDevice>? devices, SmartDevice target)
        {
            if (devices == null) return false;
            if (devices.Remove(target)) return true;
            foreach (var d in devices)
                if (RemoveDevice(d.Children, target)) return true;
            return false;
        }

        /// <summary>When the cloud recorded a device's pairing (UTC), or null for any other device.</summary>
        private static DateTime? CloudPairedAt(JsonElement item)
            => DateTime.TryParse(Str(item, "CloudPairedAt"), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

        /// <summary>When a pairing entry last changed (UTC), or now when it does not say.</summary>
        private static DateTime UpdatedAt(JsonElement entry)
            => DateTime.TryParse(Str(entry, "updated_at"), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : DateTime.UtcNow;

        private static SmartDevice? FindDevice(IEnumerable<SmartDevice>? devices, uint entityId)
        {
            if (devices == null) return null;
            foreach (var d in devices)
            {
                if (!d.IsGroup && d.EntityId == entityId) return d;
                var nested = FindDevice(d.Children, entityId);
                if (nested != null) return nested;
            }
            return null;
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
        private static int MergeChatCommands(ServerProfile profile, JsonElement entry)
        {
            if (!entry.TryGetProperty("chat_commands", out var chat) || chat.ValueKind != JsonValueKind.Object)
                return 0;

            int changed = 0;
            var prefix = Str(chat, "prefix");
            if (!string.IsNullOrWhiteSpace(prefix) && profile.ChatCommandPrefix != prefix)
            {
                profile.ChatCommandPrefix = prefix!;
                changed++;
            }

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
                        // "upkeep" is where older versions kept the detail word.
                        case "upkeep": profile.CmdUpkeepDetail = value!; break;
                        case "upkeepdetail": profile.CmdUpkeepDetail = value!; break;
                        case "online": profile.CmdOnline = value!; break;
                        case "deaths": profile.CmdDeaths = value!; break;
                        case "oilrig": profile.CmdOilRig = value!; break;
                        case "base_codes": profile.CmdBaseCodes = value!; break;
                    }
                }
            }

            if (!chat.TryGetProperty("device_mappings", out var mappings) || mappings.ValueKind != JsonValueKind.Array)
                return changed;

            foreach (var mapping in mappings.EnumerateArray())
            {
                var entityId = UInt(mapping, "entity_id");
                var command = Str(mapping, "command");

                if (entityId == 0 || string.IsNullOrWhiteSpace(command)) continue;
                if (profile.RemovedDevices.ContainsKey(entityId)) continue;

                // The list carries upkeep words too; a cupboard's word stays an upkeep word.
                var existing = profile.SwitchCommandMappings.FirstOrDefault(m => m.EntityId == entityId)
                    ?? profile.UpkeepCommandMappings.FirstOrDefault(m => m.EntityId == entityId);

                if (existing == null)
                {
                    profile.SwitchCommandMappings.Add(new ChatCommandMapping
                    {
                        EntityId = entityId,
                        Command = command!,
                        Label = FindDevice(profile.Devices, entityId)?.PureName ?? "",
                    });
                    changed++;
                }
                else if (!string.Equals(existing.Command, command, StringComparison.Ordinal))
                {
                    existing.Command = command!;
                    changed++;
                }
            }

            return changed;
        }

        /// <summary>
        /// The user removed this server here: take it off the platform too.
        ///
        /// Deletes the pairing rather than hiding it, along with the devices, markers and
        /// overlays filed under it. That is what removing a server here has always meant,
        /// and a pairing left behind is exactly what a restore would bring back. Pairing
        /// the server again in game creates a fresh one.
        ///
        /// Recorded locally first, so a delete that cannot reach the platform now is
        /// retried before the next import instead of being lost.
        /// </summary>
        public static async Task<bool> ForgetServerAsync(string host, int port, string? platformKey = null)
        {
            if (string.IsNullOrWhiteSpace(host) || port <= 0) return false;

            var key = RemovalKey(host, port);
            var removed = LoadRemoved();
            removed[key] = DateTime.UtcNow;
            SaveRemoved(removed);

            // Otherwise re-adding the server and connecting in this same session would
            // never register it again: the once-per-session guard still remembers it.
            CloudServerInfo.ForgetPaired($"{host}-{port}");

            if (!CloudBackend.UsePlatform || !CloudAuthManager.IsAuthenticated) return false;

            return await DeleteOnPlatformAsync(key, platformKey);
        }

        /// <summary>
        /// Push through removals that could not reach the platform at the time.
        ///
        /// Two kinds are dropped instead of deleted. One the user has since added back
        /// here is theirs again. One the platform has touched since the removal was
        /// paired again: the cloud listener receives an in-game pairing while this app is
        /// closed and saves it onto the same row, which moves its updated_at. Deleting
        /// that would throw away the pairing the user just made. The removal time is this
        /// PC's clock and updated_at the server's, so only a re-pair within the clock skew
        /// of the removal could be misread, and the user is not re-pairing that fast.
        /// </summary>
        private static async Task RetryPendingRemovalsAsync(IList<ServerProfile> profiles)
        {
            var removed = LoadRemoved();
            if (removed.Count == 0) return;

            foreach (var key in removed.Keys.ToList())
            {
                if (profiles.Any(p => RemovalKey(p.Host, p.Port) == key))
                {
                    ClearRemoved(key);
                    removed.Remove(key);
                }
            }

            if (removed.Count == 0) return;

            var live = await FetchPairingTimesAsync();
            if (live == null) return; // unreachable: keep everything for the next attempt

            foreach (var (key, removedAtUtc) in removed)
            {
                if (!live.TryGetValue(key, out var updatedAtUtc))
                {
                    // Already gone, whoever removed it.
                    ClearRemoved(key);
                }
                else if (updatedAtUtc > removedAtUtc)
                {
                    SupabaseAuthManager.AppendLog($"[Cloud pairings] {key} was paired again after it was removed here; keeping it.");
                    ClearRemoved(key);
                }
                else
                {
                    await DeleteOnPlatformAsync(key);
                }
            }
        }

        /// <summary>When each live pairing last changed, by removal key. Null when unreachable.</summary>
        private static async Task<Dictionary<string, DateTime>?> FetchPairingTimesAsync()
        {
            try
            {
                var body = await CloudApiClient.CallApiAsync("me/pairings", HttpMethod.Get);
                using var doc = JsonDocument.Parse(body);

                var times = new Dictionary<string, DateTime>(StringComparer.Ordinal);
                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    return times;

                foreach (var entry in data.EnumerateArray())
                {
                    var host = Str(entry, "host");
                    var port = Int(entry, "port");
                    if (string.IsNullOrWhiteSpace(host) || port <= 0) continue;

                    // A pairing with no timestamp reads as unchanged, so the removal goes ahead.
                    var updatedAt = DateTime.TryParse(Str(entry, "updated_at"), CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
                        ? parsed
                        : DateTime.MinValue;

                    times[RemovalKey(host!, port)] = updatedAt;
                }

                return times;
            }
            catch (Exception ex)
            {
                SupabaseAuthManager.AppendLog($"[Cloud pairings] Could not check pending removals: {ex.Message}");
                return null;
            }
        }

        private static async Task<bool> DeleteOnPlatformAsync(string key, string? platformKey = null)
        {
            var sep = key.LastIndexOf(':');
            var host = key[..sep];
            var port = key[(sep + 1)..];
            // A server without an address is filed under Facepunch's id, not "companion-socket-28082".
            var serverKey = !string.IsNullOrWhiteSpace(platformKey) ? platformKey : $"{host}-{port}";

            try
            {
                // The server key the pairing was filed under; the route also accepts a
                // pairing that was never seen and answers 204 either way.
                await CloudApiClient.CallApiAsync("me/servers/" + Uri.EscapeDataString(serverKey), HttpMethod.Delete);
            }
            catch (Exception ex)
            {
                SupabaseAuthManager.AppendLog($"[Cloud pairings] Could not remove {host}:{port} from the cloud yet: {ex.Message}");
                return false;
            }

            ClearRemoved(key);
            return true;
        }

        private static string RemovalKey(string host, int port)
            => $"{host.Trim().ToLowerInvariant()}:{port}";

        /// <summary>Pending removals and when each was made (UTC).</summary>
        private static Dictionary<string, DateTime> LoadRemoved()
            => new(StorageService.LoadCache<Dictionary<string, DateTime>>(RemovedKey) ?? new Dictionary<string, DateTime>(), StringComparer.Ordinal);

        private static void ClearRemoved(string key)
        {
            var removed = LoadRemoved();
            if (removed.Remove(key)) SaveRemoved(removed);
        }

        private static void SaveRemoved(Dictionary<string, DateTime> removed)
            => StorageService.SaveCache(RemovedKey, removed);

        /// <summary>Forget where we got to, so the next import re-reads everything.</summary>
        public static void ResetCursor() => StorageService.SaveCache(CursorKey, string.Empty);

        private static string? Str(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        /// <summary>The pairing's Steam id, sent as a string or a number depending on the column.</summary>
        private static string? SteamId(JsonElement e)
        {
            if (!e.TryGetProperty("steam_id", out var v)) return null;

            var raw = v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            };

            return ulong.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id != 0
                ? id.ToString(CultureInfo.InvariantCulture)
                : null;
        }

        private static int Int(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

        private static bool Bool(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        private static uint UInt(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
                && v.TryGetUInt32(out var parsed) ? parsed : 0;
    }
}
