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
    public async Task RecordStore_SkipsTornTailAndDeduplicates()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tracker-{Guid.NewGuid():N}");
        await using var store = new PlayerWipeTrackerStore(directory);
        var observation = Observation(DateTime.UtcNow);
        var item = new TrackerPersistedObservation(1, "observation", observation);
        Assert.IsTrue(store.Append("server", "wipe", observation.SteamId, item));
        Assert.IsTrue(store.Append("server", "wipe", observation.SteamId, item));
        await store.FlushAsync();

        // A crash halfway through a record leaves a partial one at the end.
        var path = Directory.EnumerateFiles(directory, "*.pwt", SearchOption.AllDirectories).Single();
        await using (var stream = new FileStream(path, FileMode.Append))
            await stream.WriteAsync(new byte[] { 2, 0x80 });

        var loaded = store.Load("server", "wipe", observation.SteamId);
        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual(observation, loaded[0].Observation);

        // The next append cuts the torn record away instead of hiding behind it.
        var later = observation with { TimestampUtc = observation.TimestampUtc.AddMinutes(1), X = 50 };
        store.Append("server", "wipe", observation.SteamId, new TrackerPersistedObservation(1, "observation", later));
        await store.FlushAsync();
        Assert.AreEqual(2, store.Load("server", "wipe", observation.SteamId).Count);

        store.DeleteAll();
        Assert.AreEqual(0, store.StorageBytes);
    }

    [TestMethod]
    public async Task LegacyJsonLines_AreConvertedThinnedAndStillReadable()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tracker-legacy-{Guid.NewGuid():N}");
        var wipeDirectory = Path.Combine(directory, "server", "wipe");
        Directory.CreateDirectory(wipeDirectory);
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };

        // Ten minutes standing still, polled every five seconds, the way the old writer stored it.
        var lines = Enumerable.Range(0, 120)
            .Select(i => System.Text.Json.JsonSerializer.Serialize(
                new TrackerPersistedObservation(1, "observation", Observation(start.AddSeconds(i * 5), x: 100, y: 100)), json))
            // Then walking off, which closes the stationary stretch.
            .Append(System.Text.Json.JsonSerializer.Serialize(
                new TrackerPersistedObservation(1, "observation", Observation(start.AddSeconds(600), x: 130, y: 100)), json))
            .Append("not json");
        var legacyPath = Path.Combine(wipeDirectory, "76561198000000001.jsonl");
        await File.WriteAllLinesAsync(legacyPath, lines);

        await using var store = new PlayerWipeTrackerStore(directory);
        var loaded = store.Load("server", "wipe", 76561198000000001);

        Assert.IsFalse(File.Exists(legacyPath));
        Assert.IsTrue(loaded.Count <= 12, $"kept {loaded.Count} of 121");
        Assert.AreEqual(start, loaded[0].Observation.TimestampUtc);
        Assert.AreEqual(start.AddSeconds(600), loaded[^1].Observation.TimestampUtc);

        // The thinned history still reads as ten unbroken stationary minutes.
        var engine = new PlayerWipeTrackerEngine();
        foreach (var item in loaded)
            engine.Replay(item.Observation);
        var summary = engine.Summarize();
        Assert.AreEqual(TimeSpan.FromSeconds(600), summary.Stationary);
        Assert.AreEqual(TimeSpan.Zero, summary.Unknown);
        CollectionAssert.AreEqual(new ulong[] { 76561198000000001 }, store.LoadPlayerIds("server", "wipe").ToArray());
        store.DeleteAll();
    }

    [TestMethod]
    public void LiveThinning_ReloadsToTheSameTimeline()
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var live = new PlayerWipeTrackerEngine();
        var stored = new List<PlayerObservation>();
        var x = 100d;
        for (var i = 0; i < 2000; i++)
        {
            // Alternate ten-minute stretches of running and standing, with a reconnect halfway.
            var moving = i / 120 % 2 == 0;
            if (moving) x += 25;
            var observation = Observation(start.AddSeconds(i * 5), x: x, y: 100, session: i < 1000 ? "s1" : "s2");
            if (live.Observe(observation))
            {
                if (live.TakePendingTail() is { } tail) stored.Add(tail);
                stored.Add(observation);
            }
        }

        var reloaded = new PlayerWipeTrackerEngine();
        foreach (var observation in stored)
            reloaded.Replay(observation);

        var a = live.Summarize();
        var b = reloaded.Summarize();
        Assert.IsTrue(stored.Count < 2000 * 0.6, $"stored {stored.Count}");
        Assert.AreEqual(a.Moving, b.Moving);
        Assert.AreEqual(a.Stationary, b.Stationary);
        Assert.AreEqual(a.Unknown, b.Unknown);
        Assert.AreEqual(a.EstimatedDistance, b.EstimatedDistance, 1);
    }

    [TestMethod]
    public void Teleports_BreakTheRouteAndAddNoDistance()
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var engine = new PlayerWipeTrackerEngine();
        engine.Observe(Observation(start, x: 1000, y: 20));
        engine.Observe(Observation(start.AddSeconds(5), x: 1020, y: 20));
        // Into the Deep Sea, drawn far off the map's west edge.
        engine.Observe(Observation(start.AddSeconds(10), x: -4000, y: 1500));
        engine.Observe(Observation(start.AddSeconds(15), x: -3980, y: 1500));
        // Killed there, and respawned on a beach.
        engine.Observe(Observation(start.AddSeconds(20), x: -3980, y: 1500, dead: true));
        engine.Observe(Observation(start.AddSeconds(60), x: 2500, y: 3000));

        Assert.AreEqual(40, engine.Summarize().EstimatedDistance, 0.001);
        Assert.AreEqual(1, engine.Summarize().Deaths);

        var a = Observation(start, x: 1000, y: 20);
        Assert.IsTrue(TrackerRouteContinuity.IsJump(a, Observation(start.AddSeconds(5), x: -4000, y: 1500)));
        Assert.IsTrue(TrackerRouteContinuity.IsJump(a, Observation(start.AddSeconds(5), x: 1900, y: 20)));
        Assert.IsFalse(TrackerRouteContinuity.IsJump(a, Observation(start.AddSeconds(5), x: 1030, y: 20)));
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
