using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Core.Servers;
using RustPlusDesk.Infrastructure.Profiles;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class DeviceProfileEditorTests
{
    [TestMethod]
    public async Task Rename_NestedDuplicateEntity_ChangesOnlySelectedAlias_AndBlankRestoresOriginalName()
    {
        using var file = new Fixture();
        var original = await file.ServerAsync();
        byte[] before = await File.ReadAllBytesAsync(file.Path);
        var editor = new LegacyDeviceProfileEditor();
        var saved = await editor.RenameAsync(file.Path, original, "0/0", " New alias ", CancellationToken.None);
        Assert.AreEqual("New alias", saved.Server.Devices[0].Children[0].Name);
        Assert.AreEqual("Duplicate", saved.Server.Devices[1].Name);
        Assert.AreEqual("0/0", saved.DeviceKey);
        var expected = JsonNode.Parse(before)!;
        expected[0]!["Devices"]![0]!["Children"]![0]!["Alias"] = "New alias";
        Assert.IsTrue(JsonNode.DeepEquals(expected, await file.JsonAsync()));
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(Directory.GetFiles(file.Directory, "*.bak").Single()));
        saved = await editor.RenameAsync(file.Path, saved.Server, saved.DeviceKey, "  ", CancellationToken.None);
        Assert.AreEqual("Switch", saved.Server.Devices[0].Children[0].Name);
        Assert.IsNull(saved.Server.Devices[0].Children[0].Alias);
    }

    [TestMethod]
    public async Task Group_WrapsWholeSubtree_WithoutLosingSettings_AndUsesUniqueHighBitId()
    {
        using var file = new Fixture();
        var original = await file.ServerAsync();
        var before = await file.JsonAsync();
        var saved = await new LegacyDeviceProfileEditor().GroupAsync(file.Path, original, "0", " Outer ", CancellationToken.None);
        var group = saved.Server.Devices[0];
        Assert.AreEqual("Outer", group.Name);
        Assert.AreEqual("0", saved.DeviceKey);
        Assert.IsTrue(group.EntityId >= 0x80000000);
        Assert.AreNotEqual(original.Devices[0].EntityId, group.EntityId);
        Assert.AreEqual("0/0/0", group.Children[0].Children[0].DeviceKey);
        var after = await file.JsonAsync();
        Assert.IsTrue(JsonNode.DeepEquals(before[0]!["Devices"]![0], after[0]!["Devices"]![0]!["Children"]![0]));
        Assert.IsTrue(JsonNode.DeepEquals(before[1], after[1]));
    }

    [TestMethod]
    public async Task Move_ToGroupAndRootAndReorder_PreservesUnknownFieldsAndEmptyGroupMetadata()
    {
        using var file = new Fixture();
        var editor = new LegacyDeviceProfileEditor();
        var before = await file.JsonAsync();
        var moved = await editor.MoveAsync(file.Path, await file.ServerAsync(), "1", "0", 0, CancellationToken.None);
        Assert.AreEqual("0/0", moved.DeviceKey);
        Assert.AreEqual("Duplicate", moved.Server.Devices[0].Children[0].Name);
        var movedAgain = await editor.MoveAsync(file.Path, moved.Server, "0/0", null, 0, CancellationToken.None);
        Assert.AreEqual("0", movedAgain.DeviceKey);
        Assert.AreEqual("Duplicate", movedAgain.Server.Devices[0].Name);
        var ungrouped = await editor.MoveAsync(file.Path, movedAgain.Server, "1/0", null, null, CancellationToken.None);
        Assert.AreEqual("2", ungrouped.DeviceKey);
        Assert.AreEqual(0, ungrouped.Server.Devices[1].Children.Count);
        var after = await file.JsonAsync();
        Assert.IsTrue(JsonNode.DeepEquals(before[0]!["Devices"]![1], after[0]!["Devices"]![0]));
        Assert.IsTrue(JsonNode.DeepEquals(before[0]!["Devices"]![0]!["Children"]![0], after[0]!["Devices"]![2]));
        Assert.AreEqual("keep-empty-group", after[0]!["Devices"]![1]!["UnknownGroupSetting"]!.GetValue<string>());
        Assert.IsTrue(JsonNode.DeepEquals(before[0]!["Automation"], after[0]!["Automation"]));
    }

    [TestMethod]
    [DataRow("0", "0", null)]
    [DataRow("0", "0/0", null)]
    [DataRow("1", "0/0", null)]
    [DataRow("1", null, -1)]
    [DataRow("1", null, 100)]
    public async Task InvalidMove_LeavesExactFileAndNoBackup(string key, string? parent, int? index)
    {
        using var file = new Fixture();
        byte[] before = await File.ReadAllBytesAsync(file.Path);
        var original = await file.ServerAsync();
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => new LegacyDeviceProfileEditor().MoveAsync(
            file.Path, original, key, parent, index, CancellationToken.None));
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(file.Path));
        Assert.AreEqual(1, Directory.GetFiles(file.Directory).Length);
    }

    [TestMethod]
    public async Task ChangedRevision_MalformedKey_AndCancellation_DoNotSave()
    {
        using var file = new Fixture();
        var original = await file.ServerAsync();
        var editor = new LegacyDeviceProfileEditor();
        foreach (string key in new[] { "", "-1", "99", "0/99", "0//0", "99999999999999" })
            await Assert.ThrowsExceptionAsync<ProfileChangedException>(() => editor.RenameAsync(file.Path, original, key, "New", CancellationToken.None));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try { await editor.GroupAsync(file.Path, original, "0", "New", canceled.Token); Assert.Fail("Must cancel"); }
        catch (OperationCanceledException) { }
        string updated = await File.ReadAllTextAsync(file.Path) + "\n";
        await File.WriteAllTextAsync(file.Path, updated);
        await Assert.ThrowsExceptionAsync<ProfileChangedException>(() => editor.RenameAsync(file.Path, original, "0", "New", CancellationToken.None));
        Assert.AreEqual(updated, await File.ReadAllTextAsync(file.Path));
        Assert.AreEqual(1, Directory.GetFiles(file.Directory).Length);
    }

    [TestMethod]
    public async Task IdenticalRename_DoesNotRewriteOrCreateBackup()
    {
        using var file = new Fixture();
        byte[] before = await File.ReadAllBytesAsync(file.Path);
        var saved = await new LegacyDeviceProfileEditor().RenameAsync(file.Path, await file.ServerAsync(), "0/0", " Alias ", CancellationToken.None);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(file.Path));
        Assert.AreEqual(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(before)), saved.Server.SourceRevision);
        Assert.AreEqual(1, Directory.GetFiles(file.Directory).Length);
    }

    [TestMethod]
    public async Task AlarmTitle_ValidatesDeviceKind_TrimsAndClears_WithoutChangingOtherFields()
    {
        using var file = new Fixture();
        var originalJson = await file.JsonAsync();
        originalJson[0]!["Devices"]!.AsArray().Add(new JsonObject
        { ["EntityId"] = 99, ["Kind"] = "Smart Alarm", ["Name"] = "Alarm", ["FutureSetting"] = true });
        await File.WriteAllTextAsync(file.Path, originalJson.ToJsonString());
        var editor = new LegacyDeviceProfileEditor();
        var original = await file.ServerAsync();
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => editor.SaveAlarmTitleAsync(file.Path, original, "0/0", "Bad target", CancellationToken.None));
        var saved = await editor.SaveAlarmTitleAsync(file.Path, original, "2", " Base under attack ", CancellationToken.None);
        Assert.AreEqual("Base under attack", saved.Server.Devices[2].InGameAlarmTitle);
        originalJson[0]!["Devices"]![2]!["InGameAlarmTitle"] = "Base under attack";
        Assert.IsTrue(JsonNode.DeepEquals(originalJson, await file.JsonAsync()));
        saved = await editor.SaveAlarmTitleAsync(file.Path, saved.Server, "2", " ", CancellationToken.None);
        Assert.IsNull(saved.Server.Devices[2].InGameAlarmTitle);
    }

    internal sealed class Fixture : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rustplus-device-editor-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Directory, "profiles.json");
        public Fixture()
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(Path, """
                [{"Name":"Saved","Host":"server.test","SteamId64":"76561198000000001","PlayerToken":"private-token",
                  "Devices":[{"EntityId":2147483649,"Kind":"Group","IsGroup":true,"Alias":"Saved group","UnknownGroupSetting":"keep-empty-group",
                    "Children":[{"EntityId":7,"Name":"Switch","Alias":"Alias","Kind":"Smart Switch","Code":"private-code","Unknown":{"a":[1,true]}}]},
                    {"EntityId":7,"Name":"Duplicate","Kind":"SmartSwitch","FutureSetting":false}],"Automation":{"rules":[1]},"FutureProfile":true},
                 {"Name":"Other","Host":"other.test","Devices":[{"EntityId":7,"Alias":"Other server"}]}]
                """);
        }
        public async Task<ServerSummary> ServerAsync() => (await new LegacyServerCatalog().LoadAsync(Path))[0];
        public async Task<JsonNode> JsonAsync() => JsonNode.Parse(await File.ReadAllTextAsync(Path))!;
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
