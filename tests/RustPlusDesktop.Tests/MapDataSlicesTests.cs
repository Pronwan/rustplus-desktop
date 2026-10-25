using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services.Map;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class MapDataSlicesTests
{
    private const string MapData =
        "{\"size\":4500,\"terrainHeightMapB64\":\"AAAA\",\"prefabs\":[{\"i\":\"water_well_a\",\"x\":1.5}]," +
        "\"heatmaps\":{\"ores\":\"AA==\"},\"cellLayerResolution\":2048,\"cellHeatmapsB64\":{\"ores\":\"AQ==\"}," +
        "\"rockClusters\":[],\"biomes\":[1,2,3],\"keycardSites\":[{\"n\":\"a \\\"quoted\\\" name\"}]}";

    private static string NewFolder(string json)
    {
        string folder = Path.Combine(Path.GetTempPath(), "rpd-slices-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "map_data.json"), json);
        return folder;
    }

    private static JsonElement Parse(string path) => JsonDocument.Parse(File.ReadAllText(path)).RootElement;

    [TestMethod]
    public void CoreSlice_HoldsTheOverlayPropertiesAndNothingElse()
    {
        string folder = NewFolder(MapData);
        try
        {
            var core = Parse(MapDataSlices.EnsureCore(folder)!);

            Assert.AreEqual(4500, core.GetProperty("size").GetInt32());
            Assert.AreEqual(1.5, core.GetProperty("prefabs")[0].GetProperty("x").GetDouble());
            Assert.AreEqual("a \"quoted\" name", core.GetProperty("keycardSites")[0].GetProperty("n").GetString());
            Assert.IsTrue(core.TryGetProperty("rockClusters", out _));
            Assert.IsFalse(core.TryGetProperty("terrainHeightMapB64", out _));
            Assert.IsFalse(core.TryGetProperty("heatmaps", out _));
            Assert.IsFalse(core.TryGetProperty("biomes", out _));
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public void HeatmapSlice_HoldsWhatTheRasterReaderNeeds()
    {
        string folder = NewFolder(MapData);
        try
        {
            var heat = Parse(MapDataSlices.EnsureHeatmaps(folder)!);

            Assert.AreEqual("AA==", heat.GetProperty("heatmaps").GetProperty("ores").GetString());
            Assert.AreEqual("AQ==", heat.GetProperty("cellHeatmapsB64").GetProperty("ores").GetString());
            Assert.AreEqual(2048, heat.GetProperty("cellLayerResolution").GetInt32());
            Assert.IsFalse(heat.TryGetProperty("prefabs", out _));
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public void Slices_AreCutAgainWhenTheMapIsParsedAgain()
    {
        string folder = NewFolder("{\"size\":3000}");
        try
        {
            Assert.AreEqual(3000, MapDataSlices.ReadWorldSize(folder));

            string path = Path.Combine(folder, "map_data.json");
            File.WriteAllText(path, "{\"size\":4250}");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

            Assert.AreEqual(4250, MapDataSlices.ReadWorldSize(folder));
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public void NoMapData_NoSlice()
    {
        string folder = Path.Combine(Path.GetTempPath(), "rpd-slices-missing-" + Guid.NewGuid().ToString("N"));
        Assert.IsNull(MapDataSlices.EnsureCore(folder));
        Assert.AreEqual(0, MapDataSlices.ReadWorldSize(folder));
    }

    [TestMethod]
    public void Rewrite_KeepsEverythingElseAndReplacesOrAddsTheOverrides()
    {
        string folder = NewFolder("{\"size\":4500,\"mapTextureSource\":\"old\",\"biomes\":[1,2,3]}");
        try
        {
            string target = Path.Combine(folder, "viewer.json");
            MapDataSlices.RewriteWithOverrides(Path.Combine(folder, "map_data.json"), target,
                new Dictionary<string, Action<Utf8JsonWriter>>
                {
                    ["mapTextureSource"] = w => w.WriteNullValue(),
                    ["mapTexturePaddingWorld"] = w => w.WriteNumberValue(2000),
                });

            var root = Parse(target);
            Assert.AreEqual(4500, root.GetProperty("size").GetInt32());
            Assert.AreEqual(3, root.GetProperty("biomes").GetArrayLength());
            Assert.AreEqual(JsonValueKind.Null, root.GetProperty("mapTextureSource").ValueKind);
            Assert.AreEqual(2000, root.GetProperty("mapTexturePaddingWorld").GetInt32());
        }
        finally { Directory.Delete(folder, true); }
    }
}
