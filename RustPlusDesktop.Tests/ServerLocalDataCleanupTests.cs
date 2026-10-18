using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services.Data;

namespace RustPlusDesktop.Tests;

[TestClass]
public class ServerLocalDataCleanupTests
{
    private static readonly (string Host, int Port)[] Servers =
    {
        ("1.2.3.4", 28082),
        ("1.2.3.4", 280821), // same host, a port that starts with the deleted one
        ("5.6.7.8", 28015)
    };

    private string _root = string.Empty;
    private string AppDir => Path.Combine(_root, "roaming");
    private string LocalDir => Path.Combine(_root, "local");
    private string OwnFolder => Path.Combine(AppDir, "3DMaps", "Own_profile_folder");

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "rpd-cleanup-" + Guid.NewGuid().ToString("N"));

        foreach (var (host, port) in Servers)
        {
            string dash = $"{host}-{port}";
            string under = $"{host}_{port}";
            Touch(Path.Combine(AppDir, "deaths", dash + ".jsonl"));
            Touch(Path.Combine(AppDir, "deaths", dash + ".killers.jsonl"));
            Touch(Path.Combine(AppDir, "player-wipes", dash, "wipe", "1.bin"));
            Touch(Path.Combine(AppDir, "Overlays", dash, "765.json"));
            Touch(Path.Combine(AppDir, "cache", under + "_markers.json"));
            Touch(Path.Combine(AppDir, "cache", under + "_team.json"));
            Touch(Path.Combine(LocalDir, "chat", under + ".json"));
            Touch(Path.Combine(LocalDir, "chat", under + "_clan.json"));
            Touch(Path.Combine(LocalDir, "map_cache", under + ".json"));
            Touch(Path.Combine(LocalDir, "map_cache", under + ".png"));
            Touch(Path.Combine(AppDir, "3DMaps", under + "_wipe_a", "map3d_manifest.json"),
                $"{{\"Host\":\"{host}\",\"Port\":{port}}}");
        }

        Touch(Path.Combine(AppDir, "cache", "minimap_settings.json"));
        Touch(Path.Combine(OwnFolder, "map_data.json"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private static void Touch(string path, string content = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [TestMethod]
    public void DeletedServer_LosesAllItsData_AndOnlyItsData()
    {
        int removed = ServerLocalDataCleanup.DeleteFor("1.2.3.4", 28082, keepServerData: false, OwnFolder, AppDir, LocalDir);

        Assert.IsFalse(Directory.Exists(OwnFolder));
        Assert.IsFalse(File.Exists(Path.Combine(AppDir, "deaths", "1.2.3.4-28082.jsonl")));
        Assert.IsFalse(File.Exists(Path.Combine(AppDir, "deaths", "1.2.3.4-28082.killers.jsonl")));
        Assert.IsFalse(Directory.Exists(Path.Combine(AppDir, "player-wipes", "1.2.3.4-28082")));
        Assert.IsFalse(Directory.Exists(Path.Combine(AppDir, "Overlays", "1.2.3.4-28082")));
        Assert.IsFalse(File.Exists(Path.Combine(AppDir, "cache", "1.2.3.4_28082_markers.json")));
        Assert.IsFalse(File.Exists(Path.Combine(LocalDir, "chat", "1.2.3.4_28082_clan.json")));
        Assert.IsFalse(File.Exists(Path.Combine(LocalDir, "map_cache", "1.2.3.4_28082.png")));
        Assert.IsFalse(Directory.Exists(Path.Combine(AppDir, "3DMaps", "1.2.3.4_28082_wipe_a")));

        Assert.IsTrue(File.Exists(Path.Combine(AppDir, "cache", "1.2.3.4_280821_markers.json")));
        Assert.IsTrue(File.Exists(Path.Combine(AppDir, "deaths", "1.2.3.4-280821.jsonl")));
        Assert.IsTrue(Directory.Exists(Path.Combine(AppDir, "3DMaps", "1.2.3.4_280821_wipe_a")));
        Assert.IsTrue(File.Exists(Path.Combine(LocalDir, "chat", "5.6.7.8_28015.json")));
        Assert.IsTrue(Directory.Exists(Path.Combine(AppDir, "3DMaps", "5.6.7.8_28015_wipe_a")));
        Assert.IsTrue(File.Exists(Path.Combine(AppDir, "cache", "minimap_settings.json")));
        Assert.AreEqual(12, removed);
    }

    [TestMethod]
    public void ServerStillUsed_OnlyLosesTheProfilesOwnMapFolder()
    {
        int removed = ServerLocalDataCleanup.DeleteFor("1.2.3.4", 28082, keepServerData: true, OwnFolder, AppDir, LocalDir);

        Assert.AreEqual(1, removed);
        Assert.IsFalse(Directory.Exists(OwnFolder));
        Assert.IsTrue(File.Exists(Path.Combine(AppDir, "deaths", "1.2.3.4-28082.jsonl")));
        Assert.IsTrue(File.Exists(Path.Combine(LocalDir, "chat", "1.2.3.4_28082.json")));
        Assert.IsTrue(Directory.Exists(Path.Combine(AppDir, "3DMaps", "1.2.3.4_28082_wipe_a")));
    }

    [TestMethod]
    public void MissingFolders_AreFine()
    {
        Assert.AreEqual(0, ServerLocalDataCleanup.DeleteFor("9.9.9.9", 1, keepServerData: false, null,
            Path.Combine(_root, "nothing"), Path.Combine(_root, "nothing-local")));
    }
}
