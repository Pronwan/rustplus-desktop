using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services;

namespace RustPlusDesktop.Tests;

/// <summary>
/// Each parse writes into its own attempt folder. A failed parse must never find an earlier
/// attempt's output there and pass it off as its own, which is how a stale map got cached as
/// the player's selection.
/// </summary>
[TestClass]
public class Map3DParseAttemptTests
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "map3d-attempts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_folder, true); } catch { }
    }

    [TestMethod]
    public void TwoMapsWithTheSameFileNameGetTheirOwnAttemptFolders()
    {
        var first = Path.Combine(_folder, "wipe-1", "proceduralmap.4500.map");
        var second = Path.Combine(_folder, "wipe-2", "proceduralmap.4500.map");

        var a = Map3DLocalBuildService.AttemptDirectory(_folder, first);
        var b = Map3DLocalBuildService.AttemptDirectory(_folder, second);

        Assert.AreNotEqual(a, b);
        Assert.AreEqual(a, Map3DLocalBuildService.AttemptDirectory(_folder, first), "the same map keeps its folder");
        StringAssert.StartsWith(Path.GetFileName(a), "proceduralmap.4500_");
    }

    [TestMethod]
    public void ARetryStartsFromAnEmptyFolder()
    {
        var map = Path.Combine(_folder, "server.map");
        var earlier = Map3DLocalBuildService.PrepareAttemptDirectory(_folder, map);
        File.WriteAllText(Path.Combine(earlier, "map_resolved.json"), "[]");
        File.WriteAllText(Path.Combine(earlier, "map_data.json"), "{}");

        var retry = Map3DLocalBuildService.PrepareAttemptDirectory(_folder, map);

        Assert.AreEqual(earlier, retry);
        Assert.IsTrue(Directory.Exists(retry));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(retry).Length, "a failing retry would otherwise find the old output");
    }

    [TestMethod]
    public void AnotherMapsOutputIsNeverInTheWay()
    {
        var other = Map3DLocalBuildService.PrepareAttemptDirectory(_folder, Path.Combine(_folder, "old", "server.map"));
        File.WriteAllText(Path.Combine(other, "map_resolved.json"), "[]");

        var picked = Map3DLocalBuildService.PrepareAttemptDirectory(_folder, Path.Combine(_folder, "new", "server.map"));

        Assert.IsFalse(File.Exists(Path.Combine(picked, "map_resolved.json")));
        Assert.IsTrue(File.Exists(Path.Combine(other, "map_resolved.json")), "preparing one map leaves the other alone");
    }

    [TestMethod]
    public void AHandPickedMapIsTakenOnlyWhenItParsedThisTime()
    {
        var map = Path.Combine(_folder, "server.map");

        Assert.IsTrue(Map3DLocalBuildService.AcceptsManualSelection(map, 1, pickedMapParsed: true));
        Assert.IsFalse(Map3DLocalBuildService.AcceptsManualSelection(map, 1, pickedMapParsed: false));
        Assert.IsFalse(Map3DLocalBuildService.AcceptsManualSelection(null, 1, pickedMapParsed: true), "automatic search needs a monument match");
    }
}
