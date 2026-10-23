using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Infrastructure.Profiles;
using RustPlusDesk.Core.Devices;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class LegacyServerCatalogTests
{
    [TestMethod]
    public async Task LoadAsync_NestedDevices_PreservesKindsAliasesAndOrderWithoutTrustingSavedState()
    {
        const string json = """
            [{"Host":"localhost","Devices":[{"Name":"Base","IsGroup":true,"Children":[
              {"EntityId":12,"Kind":"Smart Switch","Name":"Switch","Alias":"Lights","IsOn":true},
              {"IsGroup":true,"Children":[{"EntityId":13,"Kind":"SmartAlarm"}]},
              {"EntityId":14,"Kind":"Storage Monitor","Children":null},
              {"EntityId":15,"Kind":"FutureDevice"}]}]}, {"Host":"other","Devices":null}]
            """;
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, json);
            var servers = await new LegacyServerCatalog().LoadAsync(path);
            var group = servers[0].Devices.Single();
            Assert.AreEqual(DeviceKind.Group, group.Kind);
            Assert.AreEqual("Lights", group.Children[0].Name);
            Assert.AreEqual(DeviceKind.Switch, group.Children[0].Kind);
            Assert.AreEqual(DeviceKind.Alarm, group.Children[1].Children.Single().Kind);
            Assert.AreEqual(DeviceKind.StorageMonitor, group.Children[2].Kind);
            Assert.AreEqual(DeviceKind.Unknown, group.Children[3].Kind);
            Assert.AreEqual(0, servers[1].Devices.Count);
            Assert.IsFalse(JsonSerializer.Serialize(servers).Contains("IsOn", StringComparison.Ordinal));
            Assert.AreEqual(json, await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task LoadAsync_LegacyProfiles_ProjectsDisplayFieldsWithoutChangingFile()
    {
        const string json = """
            [{"Name":"Rust server","Host":"localhost","Port":28083,"PlayerToken":"private-token",
              "SteamId64":"private-id","Devices":[{"Code":"private-code"}]},
             {"Name":null,"Host":"example.test"}]
            """;
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, json);
            var servers = await new LegacyServerCatalog().LoadAsync(path);

            Assert.AreEqual(2, servers.Count);
            Assert.AreEqual("Rust server", servers[0].Name);
            Assert.AreEqual(28083, servers[0].Port);
            Assert.AreEqual("example.test", servers[1].Name);
            Assert.AreEqual(28082, servers[1].Port);
            Assert.IsFalse(JsonSerializer.Serialize(servers).Contains("private-", StringComparison.Ordinal));
            Assert.AreEqual(json, await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("[null]")]
    [DataRow("[{\"Host\":\"\"}]")]
    [DataRow("[{\"Host\":\"localhost\",\"Port\":0}]")]
    [DataRow("[{\"Host\":\"localhost\",\"Port\":65536}]")]
    [DataRow("[{\"Host\":4}]")]
    [DataRow("[{\"Host\":\"localhost\"},null]")]
    [DataRow("not json")]
    [DataRow("[{\"Host\":\"localhost\",\"Devices\":[null]}]")]
    [DataRow("[{\"Host\":\"localhost\",\"Devices\":[{\"IsGroup\":true,\"Children\":[null]}]}]")]
    public async Task LoadAsync_InvalidDocument_RejectsWholeFileWithoutChangingIt(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, json);
            await Assert.ThrowsExceptionAsync<JsonException>(() => new LegacyServerCatalog().LoadAsync(path));
            Assert.AreEqual(json, await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task LoadAsync_EmptyArray_ReturnsEmptyList()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "[]");
            Assert.AreEqual(0, (await new LegacyServerCatalog().LoadAsync(path)).Count);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task LoadAsync_MissingFile_DoesNotCreateOne()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rustplus-missing-{Guid.NewGuid():N}.json");
        await Assert.ThrowsExceptionAsync<FileNotFoundException>(() => new LegacyServerCatalog().LoadAsync(path));
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public async Task LoadAsync_Canceled_DoesNotChangeFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "[]");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(
                () => new LegacyServerCatalog().LoadAsync(path, cancellation.Token));
            Assert.AreEqual("[]", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }
}
