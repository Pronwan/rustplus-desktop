using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services;

namespace RustPlusDesktop.Tests;

/// <summary>
/// A map file is only ever taken for a server when the monuments in it sit where the server says
/// they are. Old wipes' files look just like the current one on disk and share its world size and
/// monument types, so anything looser picks them up.
/// </summary>
[TestClass]
public class Map3DMapVerificationTests
{
    private const int WorldSize = 4000;
    private const double Half = WorldSize / 2.0;

    private static readonly (string Name, double X, double Y)[] Layout =
    {
        ("harbor_display_name", 2361.68, 262.17),
        ("harbor_2_display_name", 3771.60, 1722.54),
        ("large_fishing_village_display_name", 355.67, 2472.68),
        ("lighthouse_display_name", 1200.25, 3300.75),
        ("gas_station", 2800.50, 900.25),
        ("supermarket", 900.75, 1500.50),
    };

    private string _folder = string.Empty;
    private string ResolvedPath => Path.Combine(_folder, "map_resolved.json");
    private string ManifestPath => Path.Combine(_folder, "map3d_manifest.json");

    private static List<Map3DReferenceMonument> ServerMonuments() =>
        Layout.Select(m => new Map3DReferenceMonument(m.X, m.Y, m.Name)).ToList();

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "map3d-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "map_data.json"), $"{{\"size\":{WorldSize}}}");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_folder, true); } catch { }
    }

    /// <summary>Writes a parse in the parser's centred coordinates, each monument moved by the given offset.</summary>
    private void WriteParse(double offsetX, double offsetY)
    {
        var points = Layout.Select(m => new Dictionary<string, object>
        {
            ["c"] = "monument",
            ["i"] = m.Name.Replace("_display_name", ""),
            ["x"] = m.X - Half + offsetX,
            ["y"] = m.Y - Half + offsetY,
        });
        File.WriteAllText(ResolvedPath, JsonSerializer.Serialize(points));
    }

    private void WriteManifest(bool? manualSelection)
    {
        var manifest = new Dictionary<string, object?> { ["SelectedMapFile"] = "old.map" };
        if (manualSelection != null) manifest["ManualSelection"] = manualSelection;
        File.WriteAllText(ManifestPath, JsonSerializer.Serialize(manifest));
    }

    [TestMethod]
    public void TheServersOwnMapMatches()
    {
        WriteParse(0, 0);

        var score = Map3DLocalBuildService.ScoreParsedMap(ResolvedPath, ServerMonuments(), WorldSize);

        Assert.AreEqual(Layout.Length, score.MatchedCount);
        Assert.IsTrue(Map3DLocalBuildService.IsGoodMatch(score, ServerMonuments()));
    }

    [TestMethod]
    public void AMapWithTheSameMonumentsNearbyDoesNotMatch()
    {
        // Within the old 300 unit radius, which is how other wipes' maps were being accepted.
        WriteParse(120, -90);

        var score = Map3DLocalBuildService.ScoreParsedMap(ResolvedPath, ServerMonuments(), WorldSize);

        Assert.AreEqual(0, score.MatchedCount);
        Assert.IsFalse(Map3DLocalBuildService.IsGoodMatch(score, ServerMonuments()));
    }

    [TestMethod]
    public void AHandfulOfMatchesIsNotEnoughOnAFullMap()
    {
        var refs = Enumerable.Range(0, 25).Select(i => new Map3DReferenceMonument(i, i, $"monument {i}")).ToList();

        Assert.IsFalse(Map3DLocalBuildService.IsGoodMatch(new Map3DLocalBuildService.MapMatchScore(3, 0), refs));
        Assert.IsFalse(Map3DLocalBuildService.IsGoodMatch(new Map3DLocalBuildService.MapMatchScore(8, 0), refs));
        Assert.IsTrue(Map3DLocalBuildService.IsGoodMatch(new Map3DLocalBuildService.MapMatchScore(9, 0), refs));
    }

    [TestMethod]
    public void TooFewServerMonumentsProveNothing()
    {
        var two = ServerMonuments().Take(2).ToList();

        Assert.IsFalse(Map3DLocalBuildService.CanVerify(two));
        Assert.IsFalse(Map3DLocalBuildService.CanVerify(null));
        Assert.IsFalse(Map3DLocalBuildService.IsGoodMatch(new Map3DLocalBuildService.MapMatchScore(2, 0), two), "two matches used to be enough");
    }

    [TestMethod]
    public void ACachedParseOfAnotherMapIsDiscardedWithEverythingWorkedOutFromIt()
    {
        WriteParse(120, -90);
        WriteManifest(manualSelection: null);
        var derived = new[] { "map_extra_monuments.json", "building_blocked.json", "map_data.2d.v1.json", "map_render_full.png" };
        foreach (var name in derived) File.WriteAllText(Path.Combine(_folder, name), "stale");
        string attempts = Path.Combine(_folder, "parser_attempts");
        Directory.CreateDirectory(attempts);

        Assert.IsTrue(Map3DLocalBuildService.DiscardParseOfAnotherMap(_folder, ServerMonuments(), WorldSize));

        Assert.AreEqual(0, Directory.GetFiles(_folder).Length, "old-wipe overlays would otherwise outlive the parse");
        Assert.IsTrue(Directory.Exists(attempts));
    }

    [TestMethod]
    public void ACachedParseOfTheRightMapStays()
    {
        WriteParse(0, 0);
        WriteManifest(manualSelection: null);

        Assert.IsFalse(Map3DLocalBuildService.DiscardParseOfAnotherMap(_folder, ServerMonuments(), WorldSize));
        Assert.IsTrue(File.Exists(ResolvedPath));
    }

    [TestMethod]
    public void AMapPickedByHandStaysEvenWhenItDoesNotMatch()
    {
        WriteParse(120, -90);
        WriteManifest(manualSelection: true);

        Assert.IsFalse(Map3DLocalBuildService.DiscardParseOfAnotherMap(_folder, ServerMonuments(), WorldSize));
        Assert.IsTrue(File.Exists(ResolvedPath));
    }

    [TestMethod]
    public void WithoutServerMonumentsACachedParseIsLeftAlone()
    {
        WriteParse(120, -90);
        WriteManifest(manualSelection: null);

        Assert.IsFalse(Map3DLocalBuildService.DiscardParseOfAnotherMap(_folder, new List<Map3DReferenceMonument>(), WorldSize));
        Assert.IsTrue(File.Exists(ResolvedPath));
    }

    [TestMethod]
    public void UnreadableParseOutputIsDiscarded()
    {
        File.WriteAllText(ResolvedPath, "{ not json");
        WriteManifest(manualSelection: null);

        Assert.IsTrue(Map3DLocalBuildService.DiscardParseOfAnotherMap(_folder, ServerMonuments(), WorldSize));
    }

    [TestMethod]
    public void ReferencesLeaveOutTunnelsWhileEnoughOthersRemain()
    {
        var refs = Map3DLocalBuildService.BuildReferenceMonuments(new (double, double, string)[]
        {
            (1, 1, "train_tunnel_display_name"), (2, 2, "harbor_display_name"), (3, 3, "lighthouse_display_name"),
            (4, 4, "supermarket"), (5, 5, " "),
        });

        CollectionAssert.AreEqual(new[] { "harbor_display_name", "lighthouse_display_name", "supermarket" }, refs.Select(r => r.Name).ToArray());
    }

    [TestMethod]
    public void ReferencesFallBackToTunnelsOnASparseMap()
    {
        var refs = Map3DLocalBuildService.BuildReferenceMonuments(new (double, double, string)[]
        {
            (1, 1, "train_tunnel_display_name"), (2, 2, "train_tunnel_display_name"), (3, 3, "harbor_display_name"),
        });

        Assert.AreEqual(3, refs.Count);
    }

    [TestMethod]
    public void CandidatesAreTriedNewestFirst()
    {
        var wipe = new DateTime(2026, 11, 5, 18, 0, 0, DateTimeKind.Utc);

        var order = Map3DLocalBuildService.OrderMapCandidates(new[]
        {
            ("last-wipe.map", wipe.AddDays(-14)),
            ("this-wipe.map", wipe.AddHours(1)),
            ("other-server.map", wipe.AddDays(-2)),
        }, wipe);

        CollectionAssert.AreEqual(new[] { "this-wipe.map", "other-server.map", "last-wipe.map" }, order);
    }

    [TestMethod]
    public void EveryFileSinceTheWipeIsTriedHoweverManyThereAre()
    {
        var wipe = new DateTime(2026, 11, 5, 18, 0, 0, DateTimeKind.Utc);
        var sinceWipe = Enumerable.Range(1, 30).Select(i => ($"new-{i}.map", wipe.AddMinutes(i)));
        var older = Enumerable.Range(1, 30).Select(i => ($"old-{i}.map", wipe.AddDays(-i)));

        var order = Map3DLocalBuildService.OrderMapCandidates(sinceWipe.Concat(older), wipe);

        Assert.AreEqual(30, order.Count(p => p.StartsWith("new-")));
        Assert.AreEqual(12, order.Count(p => p.StartsWith("old-")), "an older file can still be right after a same-map wipe");
    }

    [TestMethod]
    public void AnUnknownWipeStillGetsARecentSearch()
    {
        var now = new DateTime(2026, 11, 5, 18, 0, 0, DateTimeKind.Utc);

        var order = Map3DLocalBuildService.OrderMapCandidates(
            Enumerable.Range(1, 30).Select(i => ($"{i}.map", now.AddDays(-i))), wipeTime: null);

        Assert.AreEqual(12, order.Count);
        Assert.AreEqual("1.map", order[0]);
    }
}
