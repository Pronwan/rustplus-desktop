using System.Collections.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Cloud;

namespace RustPlusDesktop.Tests;

/// <summary>
/// Pairings the cloud saved while the app was closed, and device edits made on the website,
/// coming back into the app's profiles.
/// </summary>
[TestClass]
public class CloudPairingImportTests
{
    private const string ServerId = "089941d9-064e-4628-9c60-eaeb01a943e8";

    private static string Body(string entryJson) => "{\"data\":[" + entryJson + "],\"meta\":{\"as_of\":\"2026-11-14T10:00:00+00:00\"}}";

    private static string IdOnlyEntry(string devices = "[]", string? chat = null) =>
        "{\"server_key\":\"" + ServerId + "\",\"name\":\"Moose\",\"host\":null,\"port\":null," +
        "\"facepunch_id\":\"" + ServerId.ToUpperInvariant() + "\",\"steam_id\":\"76561190000000123\"," +
        "\"player_token\":\"555\",\"updated_at\":\"2026-11-14T09:59:00+00:00\"," +
        "\"devices\":[{\"device_key\":null,\"device_data\":" + devices + "}]" +
        (chat == null ? "" : ",\"chat_commands\":" + chat) + "}";

    [TestMethod]
    public void A_server_named_only_by_Facepunch_id_becomes_a_companion_socket_profile()
    {
        var profiles = new List<ServerProfile>();
        int saves = 0;

        var result = CloudPairingImporter.Merge(Body(IdOnlyEntry()), profiles, () => saves++);

        Assert.AreEqual(1, result.Added);
        Assert.AreEqual(1, saves);
        var profile = profiles.Single();
        Assert.AreEqual("companion-socket", profile.Host);
        Assert.AreEqual(28082, profile.Port);
        Assert.AreEqual(ServerId, profile.ServerId);
        Assert.IsTrue(profile.UseFacepunchProxy);
        Assert.AreEqual("555", profile.PlayerToken);
        Assert.IsTrue(profile.ChatCommandsEnabled, "a newly paired server answers chat commands");
        Assert.AreEqual("2026-11-14T10:00:00+00:00", CloudPairingImporter.Cursor);
    }

    [TestMethod]
    public void An_existing_id_only_profile_is_matched_by_its_server_id()
    {
        // Commands switched off by the user stay off through a re-pair.
        var mine = new ServerProfile { Host = "companion-socket", Port = 28082, ServerId = ServerId, PlayerToken = "old", ChatCommandsEnabled = false };
        var other = new ServerProfile { Host = "companion-socket", Port = 28082, ServerId = Guid.NewGuid().ToString("D"), PlayerToken = "x" };
        var profiles = new List<ServerProfile> { other, mine };

        var result = CloudPairingImporter.Merge(Body(IdOnlyEntry()), profiles, () => { });

        Assert.AreEqual(0, result.Added);
        Assert.AreEqual(2, profiles.Count);
        Assert.AreEqual("555", mine.PlayerToken, "the re-pair's token lands on the right server");
        Assert.AreEqual("x", other.PlayerToken);
        Assert.IsFalse(mine.ChatCommandsEnabled, "an explicit choice survives the import");
    }

    [TestMethod]
    public void Website_renames_and_command_words_are_saved_even_with_nothing_else_new()
    {
        var device = new SmartDevice { EntityId = 7, Name = "Gate", Kind = "SmartSwitch", Alias = "Gate" };
        var profile = new ServerProfile
        {
            Host = "companion-socket", Port = 28082, ServerId = ServerId, PlayerToken = "555",
            SteamId64 = "76561190000000123",
            Devices = new ObservableCollection<SmartDevice> { device },
        };
        int saves = 0;

        var result = CloudPairingImporter.Merge(Body(IdOnlyEntry(
            "[{\"EntityId\":7,\"Name\":\"Gate\",\"Alias\":\"Front gate\",\"AliasFromWeb\":true}]",
            "{\"prefix\":\"!\",\"device_mappings\":[{\"command\":\"gate\",\"entity_id\":7}]}")),
            new List<ServerProfile> { profile }, () => saves++);

        Assert.AreEqual("Front gate", device.Alias);
        Assert.AreEqual("gate", profile.SwitchCommandMappings.Single(m => m.EntityId == 7).Command);
        Assert.IsTrue(result.ChangedAnything);
        Assert.AreEqual(1, saves, "an edit that is the only change is still written");
    }

    [TestMethod]
    public void A_device_deleted_on_the_website_is_removed_here_when_the_event_named_it()
    {
        var keep = new SmartDevice { EntityId = 1, Name = "Lights", Kind = "SmartSwitch" };
        var gone = new SmartDevice { EntityId = 7, Name = "Gate", Kind = "SmartSwitch" };
        var group = new SmartDevice { IsGroup = true, Name = "Base", Children = new ObservableCollection<SmartDevice> { gone } };
        var profile = new ServerProfile
        {
            Host = "companion-socket", Port = 28082, ServerId = ServerId, PlayerToken = "555",
            SteamId64 = "76561190000000123",
            Devices = new ObservableCollection<SmartDevice> { keep, group },
        };
        var body = Body(IdOnlyEntry("[{\"EntityId\":1,\"Name\":\"Lights\"}]"));

        // Without the event's ids nothing is removed: a missing device may just be an upload on its way.
        CloudPairingImporter.Merge(body, new List<ServerProfile> { profile }, () => { });
        Assert.AreEqual(1, group.Children.Count);

        var removals = new Dictionary<string, IReadOnlyCollection<uint>> { [ServerId] = new uint[] { 7 } };
        CloudPairingImporter.Merge(body, new List<ServerProfile> { profile }, () => { }, removals);

        Assert.AreEqual(0, group.Children.Count);
        Assert.IsTrue(profile.RemovedDevices.ContainsKey(7), "recorded so history cannot bring it back");
        Assert.AreEqual(2, profile.Devices.Count);
    }

    [TestMethod]
    public void A_cloud_pairing_newer_than_a_local_deletion_brings_the_device_back()
    {
        var profile = new ServerProfile
        {
            Host = "companion-socket", Port = 28082, ServerId = ServerId, PlayerToken = "555",
            SteamId64 = "76561190000000123",
        };
        profile.RemovedDevices[7] = new DateTime(2026, 11, 14, 8, 0, 0, DateTimeKind.Utc);
        profile.RemovedDevices[8] = new DateTime(2026, 11, 14, 11, 0, 0, DateTimeKind.Utc);

        CloudPairingImporter.Merge(Body(IdOnlyEntry(
            "[{\"EntityId\":7,\"Name\":\"Gate\",\"CloudPairedAt\":\"2026-11-14T09:00:00+00:00\"}," +
            "{\"EntityId\":8,\"Name\":\"Old\",\"CloudPairedAt\":\"2026-11-14T09:00:00+00:00\"}]")),
            new List<ServerProfile> { profile }, () => { });

        Assert.IsTrue(profile.Devices.Any(d => d.EntityId == 7), "paired again after the deletion");
        Assert.IsFalse(profile.RemovedDevices.ContainsKey(7));
        Assert.IsFalse(profile.Devices.Any(d => d.EntityId == 8), "the deletion is newer than that pairing");
    }

    [TestMethod]
    public void A_reported_address_is_display_only_and_never_changes_route_or_matching()
    {
        var profile = new ServerProfile
        {
            Host = "companion-socket", Port = 28082, ServerId = ServerId, PlayerToken = "555",
            SteamId64 = "76561190000000123", UseFacepunchProxy = true,
        };
        var keyBefore = profile.ServerKey;
        var platformKeyBefore = profile.PlatformServerKey;
        var profiles = new List<ServerProfile> { profile };

        var entry = IdOnlyEntry().Replace("\"host\":null,\"port\":null,",
            "\"host\":null,\"port\":null,\"reported_host\":\"203.0.113.5\",\"reported_port\":28015,");
        CloudPairingImporter.Merge(Body(entry), profiles, () => { });

        Assert.AreEqual("203.0.113.5", profile.ReportedHost);
        Assert.AreEqual(28015, profile.ReportedPort);
        Assert.AreEqual("companion-socket", profile.Host, "the route stays the companion socket");
        Assert.AreEqual(28082, profile.Port);
        Assert.IsTrue(profile.UseFacepunchProxy);
        Assert.IsFalse(profile.HasRealAddress);
        Assert.AreEqual(keyBefore, profile.ServerKey, "keys do not move to the address");
        Assert.AreEqual(platformKeyBefore, profile.PlatformServerKey);

        // A different server at that address is not merged into this profile by host:port.
        var other = "{\"server_key\":\"203.0.113.5-28015\",\"name\":\"Other\",\"host\":\"203.0.113.5\",\"port\":28015," +
            "\"facepunch_id\":null,\"steam_id\":\"76561190000000123\",\"player_token\":\"777\",\"devices\":[]}";
        CloudPairingImporter.Merge(Body(other), profiles, () => { });

        Assert.AreEqual(2, profiles.Count);
        Assert.AreEqual("555", profile.PlayerToken, "the id-only profile kept its own token");
    }

    [TestMethod]
    public void Own_uploads_are_ignored_and_website_edits_trigger_an_import()
    {
        const string me = "instance-1";

        Assert.IsFalse(RustPlusDesk.Views.MainWindow.IsCloudDeviceListChange(
            JObject.Parse("{\"reason\":\"desktop_sync\",\"origin\":\"instance-1\"}"), me));
        Assert.IsTrue(RustPlusDesk.Views.MainWindow.IsCloudDeviceListChange(
            JObject.Parse("{\"reason\":\"desktop_sync\",\"origin\":\"instance-2\"}"), me));
        Assert.IsTrue(RustPlusDesk.Views.MainWindow.IsCloudDeviceListChange(
            JObject.Parse("{\"reason\":\"renamed\",\"origin\":\"web\"}"), me));
        Assert.IsTrue(RustPlusDesk.Views.MainWindow.IsCloudDeviceListChange(
            JObject.Parse("{\"reason\":\"paired\",\"origin\":\"cloud\",\"entity_id\":7}"), me));
        // A switch flipping is not a list change.
        Assert.IsFalse(RustPlusDesk.Views.MainWindow.IsCloudDeviceListChange(
            JObject.Parse("{\"entity_id\":7,\"value\":true}"), me));
        // An older platform without reasons: the old rule still applies.
        Assert.IsTrue(RustPlusDesk.Views.MainWindow.IsCloudDeviceListChange(JObject.Parse("{}"), me));
    }
}
