using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusApi;
using RustPlusDesk.Core.Connections;
using RustPlusDesk.Core.Devices;
using RustPlusDesk.Core.Servers;
using RustPlusDesk.Infrastructure.Connections;
using RustPlusDesk.Infrastructure.Profiles;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class PairingMatcherTests
{
    [TestMethod]
    public async Task LegacyPortChange_WithSingleEmptyHostMatch_PreservesSettingsAndCapturesServerId()
    {
        using var files = new PairingFiles();
        await File.WriteAllTextAsync(files.Profiles, """
            [{"Host":"fixture.test","Port":28080,"SteamId64":"76561198000000001","Name":"Old display name",
              "PlayerToken":"111","Automation":{"keep":true},"Devices":[]}]
            """);
        var result = await LegacyPairingWriter.SaveAsync(files.Profiles, PairingWriterTests.Pairing, CancellationToken.None,
            (_, _, _) => throw new AssertFailedException("Empty profiles need no device probe."));
        Assert.AreEqual(28082, result.Server.Port);
        Assert.IsNotNull(result.PreviousProfileKey);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(files.Profiles))!.AsArray();
        Assert.AreEqual(1, saved.Count);
        Assert.IsTrue(saved[0]!["Automation"]!["keep"]!.GetValue<bool>());
        Assert.AreEqual(PairingWriterTests.Pairing.Id.ToString("D"), saved[0]!["ServerId"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task MultipleLegacyCandidates_ChoosePositiveNestedDeviceMatch_AndPreserveOtherServer()
    {
        using var files = new PairingFiles();
        await File.WriteAllTextAsync(files.Profiles, Candidates);
        var probes = new List<uint>();
        var result = await LegacyPairingWriter.SaveAsync(files.Profiles, PairingWriterTests.Pairing, CancellationToken.None, (_, ids, _) =>
        { probes.AddRange(ids); return Task.FromResult(ids.Contains(22u)); });
        CollectionAssert.AreEqual(new uint[] { 11, 22 }, probes);
        Assert.AreEqual(28082, result.Server.Port);
        Assert.AreEqual(22u, result.Server.Devices[0].Children[0].EntityId);
        var after = JsonNode.Parse(await File.ReadAllTextAsync(files.Profiles))!.AsArray();
        Assert.AreEqual(2, after.Count);
        Assert.AreEqual(28080, after[0]!["Port"]!.GetValue<int>());
        Assert.AreEqual("First", after[0]!["Name"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task AmbiguousUnverifiedCandidates_AndRevisionChangedDuringProbe_AreNeverOverwritten()
    {
        using var files = new PairingFiles();
        await File.WriteAllTextAsync(files.Profiles, Candidates);
        await Assert.ThrowsExceptionAsync<ProfileChangedException>(() => LegacyPairingWriter.SaveAsync(files.Profiles,
            PairingWriterTests.Pairing, CancellationToken.None, (_, _, _) => Task.FromResult(false)));
        Assert.AreEqual(Candidates, await File.ReadAllTextAsync(files.Profiles));
        await Assert.ThrowsExceptionAsync<ProfileChangedException>(() => LegacyPairingWriter.SaveAsync(files.Profiles,
            PairingWriterTests.Pairing, CancellationToken.None, async (_, _, _) =>
            { await File.WriteAllTextAsync(files.Profiles, Candidates + "\n"); return true; }));
        Assert.AreEqual(Candidates + "\n", await File.ReadAllTextAsync(files.Profiles));
        Assert.AreEqual(0, Directory.GetFiles(files.Directory, "*.bak").Length);
    }

    [TestMethod]
    public async Task DirectVerification_UsesFreshPairingCredentials_ProbesWithoutSwitching_AndDisposes()
    {
        RustPlusConnection? route = null;
        var transport = new Transport { Device = new TestDeviceClient { Read = (device, _) => Task.FromResult(new DeviceSnapshot(device.EntityId)) } };
        bool matched = await PairingConnectionVerifier.VerifyAsync(PairingWriterTests.Pairing with { PlayerToken = "-321" }, [11, 22],
            connection => { route = connection; return transport; }, CancellationToken.None);
        Assert.IsTrue(matched);
        Assert.IsNotNull(route);
        Assert.AreEqual(28082, route.Port);
        Assert.AreEqual("fixture.test", route.Server);
        Assert.AreEqual(-321, route.PlayerToken);
        Assert.IsNull(route.ServerId);
        Assert.IsNull(route.AuthToken);
        Assert.IsTrue(transport.Disposed);
    }

    [TestMethod]
    public async Task DirectVerification_MissingDevicesFail_AndCancellationAwaitsDisposal()
    {
        int reads = 0;
        var missing = new Transport { Device = new TestDeviceClient { Read = (device, _) => { reads++; return Task.FromResult(new DeviceSnapshot(device.EntityId, IsMissing: true)); } } };
        Assert.IsFalse(await PairingConnectionVerifier.VerifyAsync(PairingWriterTests.Pairing, Enumerable.Range(1, 10).Select(id => (uint)id).ToArray(), _ => missing, CancellationToken.None));
        Assert.AreEqual(5, reads);
        Assert.IsTrue(missing.Disposed);
        using var cancel = new CancellationTokenSource();
        var pending = new Transport { Initialize = async ct => { cancel.Cancel(); await Task.Delay(Timeout.Infinite, ct); } };
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => PairingConnectionVerifier.VerifyAsync(PairingWriterTests.Pairing, [1], _ => pending, cancel.Token));
        Assert.IsTrue(pending.Disposed);
    }

    private const string Candidates = """
        [{"Host":"fixture.test","Port":28080,"SteamId64":"76561198000000001","Name":"First","PlayerToken":"111",
          "Devices":[{"EntityId":11,"Kind":"SmartSwitch"}]},
         {"Host":"fixture.test","Port":28081,"SteamId64":"76561198000000001","Name":"Second","PlayerToken":"222",
          "Devices":[{"IsGroup":true,"Children":[{"EntityId":22,"Kind":"SmartAlarm","Alias":"Keep alias"}]}]}]
        """;

    private sealed class Transport : IRustTransport
    {
        public IDeviceClient Device { get; init; } = new TestDeviceClient();
        public IDeviceClient Devices => Device;
        public ServerStatus InitialStatus { get; } = new("Fixture", 1, 100, 0);
        public Task Disconnected { get; } = new TaskCompletionSource().Task;
        public bool Disposed { get; private set; }
        public Func<CancellationToken, Task>? Initialize { get; init; }
        public Task InitializeAsync(CancellationToken cancellationToken) => Initialize?.Invoke(cancellationToken) ?? Task.CompletedTask;
        public Task<ServerStatus> ReadStatusAsync(CancellationToken cancellationToken) => Task.FromResult(InitialStatus);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
