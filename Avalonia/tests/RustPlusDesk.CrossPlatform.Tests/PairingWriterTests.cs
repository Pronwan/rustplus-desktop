using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusApi.Fcm.Data;
using RustPlusDesk.Core.Servers;
using RustPlusDesk.Infrastructure.Profiles;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class PairingWriterTests
{
    internal static Body Pairing => new()
    {
        Id = Guid.Parse("8e6a4a23-5e71-4aa8-90fc-c716b2ff7f11"),
        Ip = "fixture.test",
        Port = 28082,
        PlayerId = 76561198000000001,
        PlayerToken = "12345",
        Name = "Fixture server",
        Type = "server"
    };

    [TestMethod]
    public async Task Pairing_CreatesMissingProfiles_AndReturnsOnlySafeSummary()
    {
        using var fixture = new PairingFiles();
        var server = await LegacyPairingWriter.SaveAsync(fixture.Profiles, Pairing, CancellationToken.None);
        Assert.AreEqual("Fixture server", server.Server.Name);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Profiles))!.AsArray();
        Assert.AreEqual("12345", saved[0]!["PlayerToken"]!.GetValue<string>());
        Assert.AreEqual(Pairing.Id.ToString("D"), saved[0]!["ServerId"]!.GetValue<string>());
        string display = JsonSerializer.Serialize(server.Server);
        Assert.IsFalse(display.Contains("PlayerToken", StringComparison.Ordinal));
        Assert.IsFalse(display.Contains("SteamId64", StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.GetFiles(fixture.Directory, "*.bak").Length);
    }

    [TestMethod]
    public async Task RePair_StableServerId_PreservesNestedDevicesAliasesAndUnknownFields()
    {
        using var fixture = new PairingFiles();
        await LegacyPairingWriter.SaveAsync(fixture.Profiles, Pairing, CancellationToken.None);
        var original = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Profiles))!.AsArray();
        original[0]!["Automation"] = new JsonObject { ["future"] = true };
        original[0]!["Devices"] = JsonNode.Parse("""
            [{"IsGroup":true,"Name":"Base","Children":[{"EntityId":99,"Kind":"SmartAlarm","Name":"Old alarm",
              "Alias":"My custom name","Code":"fixture-device-code","IsMissing":true,"Future":[1,2]}]}]
            """);
        await File.WriteAllTextAsync(fixture.Profiles, original.ToJsonString());
        string before = await File.ReadAllTextAsync(fixture.Profiles);
        var server = await LegacyPairingWriter.SaveAsync(fixture.Profiles, Pairing with
        { Ip = "moved.fixture.test", Port = 29082, PlayerToken = "-9876", EntityId = 99, EntityName = "New alarm", EntityType = 1 }, CancellationToken.None);
        var after = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Profiles))!.AsArray();
        Assert.AreEqual(1, after.Count);
        Assert.AreEqual("moved.fixture.test", server.Server.Host);
        Assert.IsNotNull(server.PreviousProfileKey);
        Assert.AreNotEqual(server.PreviousProfileKey, server.Server.ProfileKey);
        Assert.AreEqual("-9876", after[0]!["PlayerToken"]!.GetValue<string>());
        Assert.IsTrue(after[0]!["Automation"]!["future"]!.GetValue<bool>());
        var device = after[0]!["Devices"]![0]!["Children"]![0]!;
        Assert.AreEqual("My custom name", device["Alias"]!.GetValue<string>());
        Assert.AreEqual("fixture-device-code", device["Code"]!.GetValue<string>());
        Assert.AreEqual("SmartAlarm", device["Kind"]!.GetValue<string>());
        Assert.AreEqual("New alarm", device["Name"]!.GetValue<string>());
        Assert.IsFalse(device["IsMissing"]!.GetValue<bool>());
        Assert.AreEqual(2, device["Future"]!.AsArray().Count);
        Assert.AreEqual(before, await File.ReadAllTextAsync(Directory.GetFiles(fixture.Directory, "*.bak").Single()));
    }

    [TestMethod]
    public async Task DuplicatePairing_DoesNotRewriteOrCreateAnotherBackup()
    {
        using var fixture = new PairingFiles();
        await LegacyPairingWriter.SaveAsync(fixture.Profiles, Pairing, CancellationToken.None);
        string before = await File.ReadAllTextAsync(fixture.Profiles);
        await LegacyPairingWriter.SaveAsync(fixture.Profiles, Pairing, CancellationToken.None);
        Assert.AreEqual(before, await File.ReadAllTextAsync(fixture.Profiles));
        Assert.AreEqual(0, Directory.GetFiles(fixture.Directory, "*.bak").Length);
    }

    [TestMethod]
    public async Task DevicePairing_AddsStorageAndKeepsOtherAccountsSeparate()
    {
        using var fixture = new PairingFiles();
        await LegacyPairingWriter.SaveAsync(fixture.Profiles, Pairing with { PlayerId = 76561198000000002 }, CancellationToken.None);
        var server = await LegacyPairingWriter.SaveAsync(fixture.Profiles, Pairing with { EntityId = 101, EntityName = "Tool Cupboard", EntityType = 3 }, CancellationToken.None);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Profiles))!.AsArray();
        Assert.AreEqual(2, saved.Count);
        Assert.AreEqual("StorageMonitor", saved[1]!["Devices"]![0]!["Kind"]!.GetValue<string>());
        Assert.IsNull(saved[1]!["Devices"]![0]!["IsOn"]);
        Assert.AreEqual(1, server.Server.Devices.Count);
    }

    [TestMethod]
    public async Task InvalidOrAmbiguousPairing_NeverReplacesExistingData()
    {
        using var fixture = new PairingFiles();
        await LegacyPairingWriter.SaveAsync(fixture.Profiles, Pairing, CancellationToken.None);
        string before = await File.ReadAllTextAsync(fixture.Profiles);
        foreach (var bad in new[] { Pairing with { PlayerId = 0 }, Pairing with { PlayerToken = "not-a-token" },
            Pairing with { Port = 0 }, Pairing with { EntityId = (ulong)uint.MaxValue + 1 } })
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => LegacyPairingWriter.SaveAsync(fixture.Profiles, bad, CancellationToken.None));
        await Assert.ThrowsExceptionAsync<ProfileChangedException>(() => LegacyPairingWriter.SaveAsync(fixture.Profiles,
            Pairing with { Id = Guid.NewGuid() }, CancellationToken.None));
        Assert.AreEqual(before, await File.ReadAllTextAsync(fixture.Profiles));
        Assert.AreEqual(0, Directory.GetFiles(fixture.Directory, "*.bak").Length);
    }

    [TestMethod]
    public async Task CorruptProfiles_AndCanceledSave_KeepOriginalBytes()
    {
        using var fixture = new PairingFiles();
        await File.WriteAllTextAsync(fixture.Profiles, "invalid fixture");
        await Assert.ThrowsExceptionAsync<JsonException>(() => LegacyPairingWriter.SaveAsync(fixture.Profiles, Pairing, CancellationToken.None));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => LegacyPairingWriter.SaveAsync(fixture.Profiles, Pairing, cancel.Token));
        Assert.AreEqual("invalid fixture", await File.ReadAllTextAsync(fixture.Profiles));
    }

    [TestMethod]
    public async Task ConcurrentPairings_AreSerializedWithoutLosingDevices_AndInvalidateStaleEditor()
    {
        using var fixture = new PairingFiles();
        var original = await LegacyPairingWriter.SaveAsync(fixture.Profiles, Pairing, CancellationToken.None);
        await Task.WhenAll(Enumerable.Range(1, 12).Select(id => LegacyPairingWriter.SaveAsync(fixture.Profiles,
            Pairing with { EntityId = (uint)id, EntityType = 1 }, CancellationToken.None)));
        var server = (await new LegacyServerCatalog().LoadAsync(fixture.Profiles))[0];
        Assert.AreEqual(12, server.Devices.Count);
        await Assert.ThrowsExceptionAsync<ProfileChangedException>(() => new LegacyServerProfileEditor().SaveAsync(
            fixture.Profiles, original.Server, "Stale edit", new("other.test", 28082), CancellationToken.None));
    }

    [TestMethod]
    [DataRow("Smart Switch", "SmartSwitch")]
    [DataRow("TC", "StorageMonitor")]
    [DataRow("Tool Cupboard", "StorageMonitor")]
    [DataRow("Oil Rig Alarm", "SmartAlarm")]
    public async Task DevicePairing_WithoutNumericType_RecognizesDeviceNamesWithoutConfusingSwitchWithTC(string name, string expected)
    {
        using var fixture = new PairingFiles();
        await LegacyPairingWriter.SaveAsync(fixture.Profiles, Pairing with { EntityId = 1, EntityName = name, Type = "entity", EntityType = null }, CancellationToken.None);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Profiles))!;
        Assert.AreEqual(expected, saved[0]!["Devices"]![0]!["Kind"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task LegacyEmptyServerGuid_IsBackfilledByNewPairing()
    {
        using var fixture = new PairingFiles();
        await LegacyPairingWriter.SaveAsync(fixture.Profiles, Pairing, CancellationToken.None);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Profiles))!;
        saved[0]!["ServerId"] = Guid.Empty.ToString();
        await File.WriteAllTextAsync(fixture.Profiles, saved.ToJsonString());
        await LegacyPairingWriter.SaveAsync(fixture.Profiles, Pairing, CancellationToken.None);
        saved = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Profiles))!;
        Assert.AreEqual(Pairing.Id.ToString(), saved[0]!["ServerId"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task AlarmMetadata_BackfillsServerIdAndDescriptionWithoutChangingTokensOrCustomData()
    {
        using var fixture = new PairingFiles();
        await File.WriteAllTextAsync(fixture.Profiles, """
            [{"Host":"fixture.test","Port":28082,"SteamId64":"76561198000000001","Name":"Custom name",
              "PlayerToken":"old-fixture","Future":true,"Devices":[{"EntityId":1,"Alias":"Keep"}]}]
            """);
        var original = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Profiles))!;
        Assert.IsTrue(await LegacyPairingWriter.UpdateMetadataAsync(fixture.Profiles, Pairing with { Desc = " Server description " }, CancellationToken.None));
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Profiles))!;
        original[0]!["ServerId"] = Pairing.Id.ToString("D");
        original[0]!["Description"] = "Server description";
        Assert.IsTrue(JsonNode.DeepEquals(original, saved));
        Assert.IsFalse(await LegacyPairingWriter.UpdateMetadataAsync(fixture.Profiles, Pairing with { Desc = "Other description" }, CancellationToken.None));
        Assert.AreEqual(1, Directory.GetFiles(fixture.Directory, "*.bak").Length);
    }

    [TestMethod]
    public async Task Metadata_FromDifferentKnownServer_DoesNotOverwriteProfile_AndDoesNotCreateMissingProfiles()
    {
        using var fixture = new PairingFiles();
        Assert.IsFalse(await LegacyPairingWriter.UpdateMetadataAsync(fixture.Profiles, Pairing, CancellationToken.None));
        Assert.IsFalse(File.Exists(fixture.Profiles));
        await LegacyPairingWriter.SaveAsync(fixture.Profiles, Pairing, CancellationToken.None);
        string before = await File.ReadAllTextAsync(fixture.Profiles);
        Assert.IsFalse(await LegacyPairingWriter.UpdateMetadataAsync(fixture.Profiles, Pairing with { Id = Guid.NewGuid(), Desc = "Different server" }, CancellationToken.None));
        Assert.AreEqual(before, await File.ReadAllTextAsync(fixture.Profiles));
    }
}

internal sealed class PairingFiles : IDisposable
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "rustplus-pairing-tests-" + Guid.NewGuid().ToString("N"));
    public string Profiles => Path.Combine(Directory, "profiles.json");
    public string Config => Path.Combine(Directory, "rustplusjs-config.json");
    public PairingFiles() => System.IO.Directory.CreateDirectory(Directory);
    public void Dispose() => System.IO.Directory.Delete(Directory, true);
}
