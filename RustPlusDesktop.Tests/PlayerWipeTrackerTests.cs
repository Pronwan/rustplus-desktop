using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services.PlayerWipeTracker;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class PlayerWipeTrackerTests
{
    private static PlayerObservation Observation(
        DateTime timestamp,
        bool online = true,
        bool dead = false,
        bool afk = false,
        double? x = 0,
        double? y = 0,
        TrackerLocationType location = TrackerLocationType.Open,
        string? locationName = null,
        string session = "s1")
        => new(76561198000000001, "Player", timestamp.ToUniversalTime(), session, true, true, online, dead, afk, x, y, location, locationName, "A1", null, null);

    [TestMethod]
    public void FirstSnapshot_EstablishesBaselineWithoutElapsedTime()
    {
        var engine = new PlayerWipeTrackerEngine();

        engine.Observe(Observation(DateTime.UtcNow));

        Assert.AreEqual(TimeSpan.Zero, engine.Summarize().Coverage);
    }

    [TestMethod]
    public void StatePriority_UnknownOfflineDeadAfkMovingAndStationary()
    {
        var now = DateTime.UtcNow;
        Assert.AreEqual(PlayerActivityState.Unknown, PlayerWipeTrackerEngine.Classify(Observation(now) with { SnapshotValid = false }));
        Assert.AreEqual(PlayerActivityState.Offline, PlayerWipeTrackerEngine.Classify(Observation(now, online: false)));
        Assert.AreEqual(PlayerActivityState.Dead, PlayerWipeTrackerEngine.Classify(Observation(now, dead: true)));
        Assert.AreEqual(PlayerActivityState.Afk, PlayerWipeTrackerEngine.Classify(Observation(now, afk: true)));
        Assert.AreEqual(PlayerActivityState.Stationary, PlayerWipeTrackerEngine.Classify(Observation(now)));
        Assert.AreEqual(PlayerActivityState.Moving, PlayerWipeTrackerEngine.Classify(Observation(now, x: 20), 20));
    }

    [TestMethod]
    public void ReconnectGap_IsUnknownAndDoesNotAddDistance()
    {
        var engine = new PlayerWipeTrackerEngine();
        var start = DateTime.UtcNow;
        engine.Observe(Observation(start, x: 0));
        engine.Observe(Observation(start.AddSeconds(5), x: 5));
        engine.Observe(Observation(start.AddMinutes(2), x: 500, session: "s2"));

        var summary = engine.Summarize();
        Assert.IsTrue(summary.Unknown >= TimeSpan.FromMinutes(1));
        Assert.IsTrue(summary.EstimatedDistance < 100);
    }

    [TestMethod]
    public void MapProjection_AlignsWorldCornersWithPaddedUniformImage()
    {
        var projection = new TrackerMapProjection(
            ViewWidth: 800,
            ViewHeight: 500,
            ImageWidth: 1000,
            ImageHeight: 1000,
            WorldRectX: 100,
            WorldRectY: 100,
            WorldRectWidth: 800,
            WorldRectHeight: 800,
            WorldSize: 4000);

        var northWest = projection.Project(0, 4000);
        var southEast = projection.Project(4000, 0);

        Assert.AreEqual(200, northWest.X, 0.001);
        Assert.AreEqual(50, northWest.Y, 0.001);
        Assert.AreEqual(600, southEast.X, 0.001);
        Assert.AreEqual(450, southEast.Y, 0.001);
    }

    [TestMethod]
    public async Task JsonLinesStore_SkipsCorruptLinesAndDeduplicates()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tracker-{Guid.NewGuid():N}");
        await using var store = new PlayerWipeTrackerStore(directory);
        var observation = Observation(DateTime.UtcNow);
        var item = new TrackerPersistedObservation(1, "observation", observation);
        Assert.IsTrue(store.Append("server", "wipe", observation.SteamId, item));
        Assert.IsTrue(store.Append("server", "wipe", observation.SteamId, item));
        await store.FlushAsync();

        var path = Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.AllDirectories).Single();
        await File.AppendAllTextAsync(path, "not json\n");

        var loaded = store.Load("server", "wipe", observation.SteamId);
        Assert.AreEqual(1, loaded.Count);
        store.DeleteAll();
        Assert.AreEqual(0, store.StorageBytes);
    }

    [TestMethod]
    public void Insights_DeriveFavouriteSpotBlindGapAndCurrentState()
    {
        var engine = new PlayerWipeTrackerEngine();
        var observations = new List<PlayerObservation>();
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        void Observe(int seconds, double x, TrackerLocationType location, string? name)
        {
            var observation = Observation(start.AddSeconds(seconds), x: x, y: 100, location: location, locationName: name);
            observations.Add(observation);
            engine.Observe(observation);
        }

        // ~60s inside a monument, moving each snapshot so every one persists.
        Observe(0, 100, TrackerLocationType.Monument, "Launch Site");
        Observe(10, 112, TrackerLocationType.Monument, "Launch Site");
        Observe(20, 124, TrackerLocationType.Monument, "Launch Site");
        Observe(30, 136, TrackerLocationType.Monument, "Launch Site");
        Observe(40, 148, TrackerLocationType.Monument, "Launch Site");
        // Step out into the open (two snapshots close the monument visit).
        Observe(50, 400, TrackerLocationType.Open, null);
        Observe(60, 412, TrackerLocationType.Open, null);
        // A 120s gap in Rust+ visibility becomes an Unknown (blind) segment.
        Observe(180, 420, TrackerLocationType.Open, null);
        Observe(190, 432, TrackerLocationType.Open, null);

        var insights = TrackerInsightsBuilder.Build(observations, engine.Segments, engine.Summarize(), start.AddSeconds(200));

        Assert.AreEqual(start, insights.FirstSeenUtc);
        Assert.AreEqual("Launch Site", insights.TopMonument);
        Assert.AreEqual(1, insights.TopMonumentVisits);
        Assert.IsTrue(insights.TopMonumentDuration >= TimeSpan.FromSeconds(30), $"visit was {insights.TopMonumentDuration}");
        Assert.IsTrue(insights.LongestBlindGap >= TimeSpan.FromSeconds(100), $"gap was {insights.LongestBlindGap}");
        Assert.AreEqual(PlayerActivityState.Stationary, insights.CurrentState);
        Assert.IsNotNull(insights.PeakHourLocal);
    }

    [TestMethod]
    public void Insights_WithoutObservationsAreEmpty()
    {
        var summary = new PlayerWipeTrackerEngine().Summarize();
        Assert.AreEqual(TrackerInsights.Empty, TrackerInsightsBuilder.Build(
            System.Array.Empty<PlayerObservation>(), System.Array.Empty<TrackerSegment>(), summary, DateTime.UtcNow));
    }

    [TestMethod]
    public async Task Store_KeepsMapInsideItsWipeDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tracker-map-{Guid.NewGuid():N}");
        await using var store = new PlayerWipeTrackerStore(directory);
        var expected = new TrackerWipeMap(new byte[] { 1, 2, 3 }, 4500, 10, 20, 900, 900);

        store.SaveWipeMap("server", "wipe-a", expected);

        var actual = store.LoadWipeMap("server", "wipe-a");
        Assert.IsNotNull(actual);
        CollectionAssert.AreEqual(expected.PngBytes, actual.PngBytes);
        Assert.AreEqual(expected.WorldSize, actual.WorldSize);
        Assert.AreEqual(expected.WorldRectWidth, actual.WorldRectWidth);
        Assert.IsNull(store.LoadWipeMap("server", "wipe-b"));
        store.DeleteAll();
    }
}
