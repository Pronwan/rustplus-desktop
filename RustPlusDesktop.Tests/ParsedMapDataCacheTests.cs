using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services.Map;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class ParsedMapDataCacheTests
{
    private static string WriteMapData(string json)
    {
        string folder = Path.Combine(Path.GetTempPath(), "rpd-mapcache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "map_data.json"), json);
        return folder;
    }

    [TestMethod]
    public void KeepsOnlyTheOverlayProperties()
    {
        string folder = WriteMapData(
            "{\"size\":4000,\"prefabs\":[{\"i\":\"water_well_a\"}],\"keycardSites\":[]," +
            "\"terrainHeightMapB64\":\"AAAA\",\"heatmaps\":{\"ores\":\"AA==\"}}");
        try
        {
            var root = ParsedMapDataCache.Load(folder);

            Assert.IsNotNull(root);
            Assert.AreEqual(4000, root.Value.GetProperty("size").GetInt32());
            Assert.AreEqual(1, root.Value.GetProperty("prefabs").GetArrayLength());
            Assert.IsTrue(root.Value.TryGetProperty("keycardSites", out _));
            Assert.IsFalse(root.Value.TryGetProperty("terrainHeightMapB64", out _), "3D terrain layers are not kept.");
            Assert.IsFalse(root.Value.TryGetProperty("heatmaps", out _));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [TestMethod]
    public void ReusesTheParseUntilTheFileChanges()
    {
        string folder = WriteMapData("{\"size\":3000}");
        try
        {
            Assert.IsNull(ParsedMapDataCache.TryGetCached(folder));
            ParsedMapDataCache.Load(folder);
            Assert.AreEqual(3000, ParsedMapDataCache.TryGetCached(folder)!.Value.GetProperty("size").GetInt32());

            string path = Path.Combine(folder, "map_data.json");
            File.WriteAllText(path, "{\"size\":4500}");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

            Assert.IsNull(ParsedMapDataCache.TryGetCached(folder), "A rewritten file is not served from the old parse.");
            Assert.AreEqual(4500, ParsedMapDataCache.Load(folder)!.Value.GetProperty("size").GetInt32());
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [TestMethod]
    public void MissingFolderOrFileGivesNull()
    {
        Assert.IsNull(ParsedMapDataCache.TryGetCached(null));
        Assert.IsNull(ParsedMapDataCache.Load(Path.Combine(Path.GetTempPath(), "rpd-missing-" + Guid.NewGuid().ToString("N"))));
    }
}
