using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services.PlayerWipeTracker;

namespace RustPlusDesktop.Tests;

/// <summary>
/// Storage and open-time figures for a monthly wipe. Opt-in (PWT_BENCH=1) because it writes
/// tens of megabytes and takes a while; PWT_REAL_DIR additionally converts a copy of a real
/// player-wipes folder and reports what it saves.
/// </summary>
[TestClass]
public sealed class PlayerWipeTrackerBenchmark
{
    private const ulong SteamId = 76561198000000001;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [TestMethod]
    [TestCategory("Benchmark")]
    public async Task MonthlyWipe_StorageAndOpenTime()
    {
        if (Environment.GetEnvironmentVariable("PWT_BENCH") != "1")
            Assert.Inconclusive("Set PWT_BENCH=1 to run.");

        var directory = Path.Combine(Path.GetTempPath(), $"tracker-bench-{Guid.NewGuid():N}");
        var wipeDirectory = Path.Combine(directory, "server", "wipe");
        Directory.CreateDirectory(wipeDirectory);
        var legacyPath = Path.Combine(wipeDirectory, $"{SteamId}.jsonl");

        // The old writer stored nearly every poll, so the synthetic poll stream is the old file.
        var polls = SyntheticWipe().ToList();
        await File.WriteAllLinesAsync(legacyPath, polls.Select(poll =>
            JsonSerializer.Serialize(new TrackerPersistedObservation(1, "observation", poll), Json)));
        var legacyBytes = new FileInfo(legacyPath).Length;

        // What one full read of the old file cost; the old tracker did several per open.
        var watch = Stopwatch.StartNew();
        var parsed = File.ReadLines(legacyPath).Select(line => JsonSerializer.Deserialize<TrackerPersistedObservation>(line, Json)).Count();
        var legacyReadMs = watch.ElapsedMilliseconds;

        await using (var store = new PlayerWipeTrackerStore(directory))
        {
            watch.Restart();
            store.Load("server", "wipe", SteamId);
            var migrateMs = watch.ElapsedMilliseconds;
            var recordPath = Path.Combine(wipeDirectory, $"{SteamId}.pwt");
            var recordBytes = new FileInfo(recordPath).Length;

            // What opening the tracker now costs: read the file, replay it, derive the panels.
            watch.Restart();
            var observations = store.Load("server", "wipe", SteamId).Select(item => item.Observation).ToArray();
            var engine = new PlayerWipeTrackerEngine();
            foreach (var observation in observations)
                engine.Replay(observation);
            var summary = engine.Summarize();
            var segments = engine.Segments;
            var insights = TrackerInsightsBuilder.Build(observations, segments, summary, DateTime.UtcNow);
            var openMs = watch.ElapsedMilliseconds;

            // The same on the old file, for the before figure (one read; the old view did several).
            var legacyEngine = new PlayerWipeTrackerEngine();
            foreach (var poll in polls)
                legacyEngine.Observe(poll);
            var legacySummary = legacyEngine.Summarize();

            Console.WriteLine($"polls (old records): {polls.Count:N0} / parsed {parsed:N0}");
            Console.WriteLine($"old file: {legacyBytes:N0} B = {legacyBytes / (double)polls.Count:N0} B/record; one full JSON read {legacyReadMs} ms");
            Console.WriteLine($"new file: {recordBytes:N0} B, {observations.Length:N0} records = {recordBytes / (double)observations.Length:N1} B/record ({legacyBytes / (double)recordBytes:N0}x smaller)");
            Console.WriteLine($"one-time conversion: {migrateMs} ms");
            Console.WriteLine($"open (load + replay + summary + insights): {openMs} ms; segments {segments.Count:N0}");
            Console.WriteLine($"new: coverage {summary.Coverage}, unknown {summary.Unknown}, moving {summary.Moving}, stationary {summary.Stationary}, distance {summary.EstimatedDistance:N0}, deaths {summary.Deaths}, sessions {insights.SessionCount}");
            Console.WriteLine($"live: coverage {legacySummary.Coverage}, unknown {legacySummary.Unknown}, moving {legacySummary.Moving}, stationary {legacySummary.Stationary}, distance {legacySummary.EstimatedDistance:N0}, deaths {legacySummary.Deaths}");
            store.DeleteAll();
        }

        var realDirectory = Environment.GetEnvironmentVariable("PWT_REAL_DIR");
        if (!string.IsNullOrWhiteSpace(realDirectory) && Directory.Exists(realDirectory))
            await ConvertRealCopyAsync(realDirectory);
    }

    private static async Task ConvertRealCopyAsync(string source)
    {
        var copy = Path.Combine(Path.GetTempPath(), $"tracker-real-{Guid.NewGuid():N}");
        long before = 0, records = 0;
        var players = new List<(string Server, string Wipe, ulong SteamId)>();
        foreach (var file in Directory.EnumerateFiles(source, "*.jsonl", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(copy, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
            before += new FileInfo(file).Length;
            records += File.ReadLines(file).LongCount();
            var parts = relative.Split(Path.DirectorySeparatorChar);
            players.Add((parts[0], parts[1], ulong.Parse(Path.GetFileNameWithoutExtension(file))));
        }

        await using var store = new PlayerWipeTrackerStore(copy);
        var watch = Stopwatch.StartNew();
        long kept = 0;
        foreach (var (server, wipe, steamId) in players)
            kept += store.Load(server, wipe, steamId).Count;
        var convertMs = watch.ElapsedMilliseconds;
        var after = Directory.EnumerateFiles(copy, "*.pwt", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length);

        watch.Restart();
        foreach (var (server, wipe, steamId) in players)
            store.Load(server, wipe, steamId);
        var reloadMs = watch.ElapsedMilliseconds;

        Console.WriteLine($"real data: {players.Count} player files, {records:N0} -> {kept:N0} records, {before:N0} -> {after:N0} B ({before / (double)Math.Max(1, after):N0}x smaller)");
        Console.WriteLine($"real data: conversion {convertMs} ms total, reading all converted files again {reloadMs} ms");
        store.DeleteAll();
        Directory.Delete(copy, recursive: true);
    }

    /// <summary>
    /// About 207 hours of five-second polls shaped like the reported wipe: roughly half offline,
    /// a third standing at base, 13% moving, the rest AFK, 138 deaths and regular Deep Sea trips.
    /// </summary>
    private static IEnumerable<PlayerObservation> SyntheticWipe()
    {
        var random = new Random(7);
        var time = new DateTime(2026, 10, 1, 18, 0, 0, DateTimeKind.Utc);
        var count = 0;
        var session = Guid.NewGuid().ToString("N");
        double x = 2000, y = 2000;
        long spawn = 1790000000;
        var deaths = 0;
        while (count < 150_000)
        {
            var roll = random.NextDouble();
            var (state, minutes) = roll switch
            {
                < 0.10 => ("offline", random.Next(60, 300)),
                < 0.55 => ("stationary", random.Next(5, 60)),
                < 0.85 => ("moving", random.Next(3, 20)),
                _ => ("afk", random.Next(5, 40)),
            };
            if (state == "offline")
            {
                time = time.AddMinutes(minutes);
                session = Guid.NewGuid().ToString("N");
                continue;
            }

            var deepSea = state == "moving" && random.NextDouble() < 0.08;
            if (deepSea) { x = -4000; y = 1500; }
            var heading = random.NextDouble() * Math.PI * 2;
            for (var t = 0; t < minutes * 12; t++, count++)
            {
                if (state == "moving")
                {
                    heading += (random.NextDouble() - 0.5) * 0.4;
                    x += Math.Cos(heading) * 27;
                    y = Math.Clamp(y + Math.Sin(heading) * 27, 0, 4000);
                    if (!deepSea) x = Math.Clamp(x, 0, 4000);
                }
                yield return new PlayerObservation(SteamId, "Pronwan", time, session, true, true, true, false, state == "afk",
                    x, y, TrackerLocationType.Open, null, "M12", spawn, null);
                time = time.AddSeconds(5);
            }

            if (deepSea) { x = 2000; y = 100; }
            if (deaths < 138 && random.NextDouble() < 0.12)
            {
                deaths++;
                yield return new PlayerObservation(SteamId, "Pronwan", time, session, true, true, true, true, false,
                    x, y, TrackerLocationType.Open, null, "M12", null, spawn + 10);
                time = time.AddSeconds(45);
                spawn += 100;
                x = random.Next(200, 3800);
                y = random.Next(200, 3800);
            }
        }
    }
}
