using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Core.Servers;
using RustPlusDesk.Infrastructure.Profiles;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class ServerProfileEditorTests
{
    [TestMethod]
    public async Task Save_ChangesOnlyEditableFields_BackupContainsExactOriginal()
    {
        using var fixture = new Fixture();
        string original = await File.ReadAllTextAsync(fixture.Path);
        var server = (await new LegacyServerCatalog().LoadAsync(fixture.Path))[0];
        var saved = await new LegacyServerProfileEditor().SaveAsync(fixture.Path, server, " New name ", new("new.example.test", 29082), CancellationToken.None);
        Assert.AreEqual("New name", saved.Name);
        Assert.AreEqual("new.example.test", saved.Host);
        Assert.AreNotEqual(server.ProfileKey, saved.ProfileKey);
        Assert.AreNotEqual(server.SourceRevision, saved.SourceRevision);
        var before = JsonNode.Parse(original)!.AsArray();
        var after = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Path))!.AsArray();
        foreach (string field in new[] { "Name", "Host", "Port" }) before[0]![field] = after[0]![field]!.DeepClone();
        Assert.IsTrue(JsonNode.DeepEquals(before, after), "Unknown fields, secrets, nested devices and other profiles must survive.");
        string backup = Directory.GetFiles(fixture.Directory, "*.bak").Single();
        Assert.AreEqual(original, await File.ReadAllTextAsync(backup));
        Assert.AreEqual(0, Directory.GetFiles(fixture.Directory, "*.tmp").Length);
    }

    [TestMethod]
    public async Task Save_RejectsChangedFileWithoutReplacingOrCreatingBackup()
    {
        using var fixture = new Fixture();
        var server = (await new LegacyServerCatalog().LoadAsync(fixture.Path))[0];
        string changed = (await File.ReadAllTextAsync(fixture.Path)) + "\n";
        await File.WriteAllTextAsync(fixture.Path, changed);
        await Assert.ThrowsExceptionAsync<ProfileChangedException>(() => new LegacyServerProfileEditor().SaveAsync(
            fixture.Path, server, "Changed", new("new.example.test", 29082), CancellationToken.None));
        Assert.AreEqual(changed, await File.ReadAllTextAsync(fixture.Path));
        Assert.AreEqual(1, Directory.GetFiles(fixture.Directory).Length);
    }

    [TestMethod]
    public async Task Save_RejectsDuplicateIdentityAndInvalidAddressWithoutChangingSource()
    {
        using var fixture = new Fixture();
        string original = await File.ReadAllTextAsync(fixture.Path);
        var server = (await new LegacyServerCatalog().LoadAsync(fixture.Path))[0];
        var editor = new LegacyServerProfileEditor();
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => editor.SaveAsync(fixture.Path, server, "Name", new("other.test", 28082), CancellationToken.None));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => editor.SaveAsync(fixture.Path, server, "Name", new("bad host", 0), CancellationToken.None));
        Assert.AreEqual(original, await File.ReadAllTextAsync(fixture.Path));
        Assert.AreEqual(1, Directory.GetFiles(fixture.Directory).Length);
    }

    [TestMethod]
    public async Task Save_CanceledBeforeWrite_PreservesFileAndBlankNameKeepsExistingName()
    {
        using var fixture = new Fixture();
        string original = await File.ReadAllTextAsync(fixture.Path);
        var server = (await new LegacyServerCatalog().LoadAsync(fixture.Path))[0];
        using var lifetime = new CancellationTokenSource();
        lifetime.Cancel();
        try
        {
            await new LegacyServerProfileEditor().SaveAsync(fixture.Path, server, "Name", new("new.test", 28082), lifetime.Token);
            Assert.Fail("Canceled writes must not save.");
        }
        catch (OperationCanceledException) { }
        Assert.AreEqual(original, await File.ReadAllTextAsync(fixture.Path));
        Assert.AreEqual(1, Directory.GetFiles(fixture.Directory).Length);
        var saved = await new LegacyServerProfileEditor().SaveAsync(fixture.Path, server, "  ", new("new.test", 28082), CancellationToken.None);
        Assert.AreEqual(server.Name, saved.Name);
    }

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rustplus-editor-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Directory, "profiles.json");
        public Fixture()
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(Path, """
                [{"Name":"Saved","Host":"old.test","Port":28082,"SteamId64":"76561198000000001",
                  "PlayerToken":"private-token","Devices":[{"EntityId":1,"Kind":"SmartSwitch","Code":"private-code","Children":[]}],
                  "FutureSetting":{"nested":[1,true,null]},"ServerId":"fixture-id"},
                 {"Name":"Other","Host":"other.test","Port":28082,"SteamId64":"76561198000000001","Unknown":false}]
                """);
        }
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
