using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services.Camera;

namespace RustPlusDesktop.Tests;

/// <summary>The camera "seen players" log: placeholder names, merging of one visit, filtering and persistence.</summary>
[TestClass]
[DoNotParallelize]
public class CameraSeenPlayersTests
{
    private string _file = "";
    private string _original = "";

    [TestInitialize]
    public void Init()
    {
        _original = CameraSeenPlayers.FilePath;
        _file = Path.Combine(Path.GetTempPath(), "rpd-seen-" + Guid.NewGuid().ToString("N") + ".json");
        CameraSeenPlayers.FilePath = _file;
        CameraSeenPlayers.ResetForTests();
    }

    [TestCleanup]
    public void Cleanup()
    {
        CameraSeenPlayers.FilePath = _original;
        CameraSeenPlayers.ResetForTests();
        try { File.Delete(_file); } catch { }
    }

    [TestMethod]
    public void PlaceholderNames_AreNotKept()
    {
        Assert.IsTrue(CameraSeenPlayers.IsPlaceholderName(null));
        Assert.IsTrue(CameraSeenPlayers.IsPlaceholderName(" "));
        Assert.IsTrue(CameraSeenPlayers.IsPlaceholderName("player"));
        Assert.IsTrue(CameraSeenPlayers.IsPlaceholderName("Player"));
        Assert.IsTrue(CameraSeenPlayers.IsPlaceholderName("(player)"));
        Assert.IsFalse(CameraSeenPlayers.IsPlaceholderName("Player One"));

        CameraSeenPlayers.Record("S", "CAM1", new[] { ("player", false), ("", false) }, DateTime.UtcNow);
        Assert.AreEqual(0, CameraSeenPlayers.Snapshot().Count);
    }

    [TestMethod]
    public void OneVisit_IsOneSighting_AndAGapStartsANewOne()
    {
        var t0 = new DateTime(2026, 11, 27, 18, 0, 0, DateTimeKind.Utc);
        CameraSeenPlayers.Record("S", "CAM1", new[] { ("Alice", false) }, t0);
        CameraSeenPlayers.Record("S", "CAM1", new[] { ("Alice", false) }, t0.AddMinutes(2));
        CameraSeenPlayers.Record("S", "CAM1", new[] { ("Alice", false) }, t0.AddMinutes(4));

        var one = CameraSeenPlayers.Snapshot("S").Single();
        Assert.AreEqual(t0, one.FirstSeenUtc);
        Assert.AreEqual(t0.AddMinutes(4), one.LastSeenUtc);

        CameraSeenPlayers.Record("S", "CAM1", new[] { ("Alice", false) }, t0.AddMinutes(20));
        Assert.AreEqual(2, CameraSeenPlayers.Snapshot("S").Count);
    }

    [TestMethod]
    public void Sightings_AreKeptPerCameraAndServer_AndSurviveARestart()
    {
        var t0 = DateTime.UtcNow;
        CameraSeenPlayers.Record("S1", "CAM1", new[] { ("Alice", true) }, t0);
        CameraSeenPlayers.Record("S1", "CAM2", new[] { ("Alice", false) }, t0);
        CameraSeenPlayers.Record("S2", "CAM1", new[] { ("Bob", false) }, t0);
        CameraSeenPlayers.Flush();

        CameraSeenPlayers.ResetForTests();   // read back from the file
        Assert.AreEqual(2, CameraSeenPlayers.Snapshot("S1").Count);
        Assert.AreEqual(3, CameraSeenPlayers.Snapshot().Count);
        Assert.IsTrue(CameraSeenPlayers.Snapshot("S1").Single(s => s.CameraId == "CAM1").Team);

        CameraSeenPlayers.Clear("S1");
        Assert.AreEqual("Bob", CameraSeenPlayers.Snapshot().Single().Name);
    }
}
