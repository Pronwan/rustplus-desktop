using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services;

namespace RustPlusDesktop.Tests;

[TestClass]
public class Map3DCacheReuseTests
{
    private string _folder = string.Empty;
    private string _mapFile = string.Empty;
    private string ManifestPath => Path.Combine(_folder, "map3d_manifest.json");

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "map3d-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _mapFile = Path.Combine(_folder, "server.map");
        File.WriteAllBytes(_mapFile, new byte[] { 1, 2, 3, 4 });
        File.WriteAllText(Path.Combine(_folder, "map_data.json"), "{\"size\":4000}");
        File.WriteAllText(Path.Combine(_folder, "map_resolved.json"), "[]");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_folder, true); } catch { }
    }

    private void WriteManifest(bool withStamp = true, string? selectedMap = null, string? textureHash = "abc")
    {
        var info = new FileInfo(_mapFile);
        var manifest = new Dictionary<string, object?>
        {
            ["RustMapsMapId"] = "some-other-id",
            ["MapTexture"] = "map_texture.png",
            ["MapTextureSha256"] = textureHash,
            ["SelectedMapFile"] = selectedMap ?? _mapFile
        };
        if (withStamp)
        {
            manifest["SelectedMapFileLength"] = info.Length;
            manifest["SelectedMapFileLastWriteUtc"] = info.LastWriteTimeUtc;
        }
        File.WriteAllText(ManifestPath, JsonSerializer.Serialize(manifest));
    }

    [TestMethod]
    public void CompleteParse_IsReused_EvenWithoutTextureFile()
    {
        WriteManifest();

        Assert.IsTrue(Map3DLocalBuildService.TryLoadReusableResult(_folder, ManifestPath, null, out var cached));
        Assert.AreEqual(_mapFile, cached.SelectedMapFile);
        Assert.AreEqual("abc", cached.TextureSha256);
    }

    [TestMethod]
    public void KnownMapPath_ReusesWhenItMatchesCachedMap()
    {
        WriteManifest();

        Assert.IsTrue(Map3DLocalBuildService.TryLoadReusableResult(_folder, ManifestPath, _mapFile, out _));
        Assert.IsFalse(Map3DLocalBuildService.TryLoadReusableResult(_folder, ManifestPath, Path.Combine(_folder, "other.map"), out _));
    }

    [TestMethod]
    public void MissingOrEmptyParseOutput_IsNotReused()
    {
        WriteManifest();
        File.WriteAllText(Path.Combine(_folder, "map_data.json"), string.Empty);
        Assert.IsFalse(Map3DLocalBuildService.TryLoadReusableResult(_folder, ManifestPath, null, out _));

        File.Delete(Path.Combine(_folder, "map_data.json"));
        Assert.IsFalse(Map3DLocalBuildService.TryLoadReusableResult(_folder, ManifestPath, null, out _));
    }

    [TestMethod]
    public void MissingManifestOrMapFile_IsNotReused()
    {
        Assert.IsFalse(Map3DLocalBuildService.TryLoadReusableResult(_folder, ManifestPath, null, out _));

        WriteManifest(selectedMap: Path.Combine(_folder, "gone.map"));
        Assert.IsFalse(Map3DLocalBuildService.TryLoadReusableResult(_folder, ManifestPath, null, out _));
    }

    [TestMethod]
    public void ChangedMapFile_IsNotReused()
    {
        WriteManifest();
        File.WriteAllBytes(_mapFile, new byte[] { 1, 2, 3, 4, 5, 6 });

        Assert.IsFalse(Map3DLocalBuildService.TryLoadReusableResult(_folder, ManifestPath, null, out _));
    }

    [TestMethod]
    public void OldManifestWithoutFileStamp_IsReused()
    {
        WriteManifest(withStamp: false, textureHash: null);

        Assert.IsTrue(Map3DLocalBuildService.TryLoadReusableResult(_folder, ManifestPath, null, out var cached));
        Assert.IsNull(cached.TextureSha256);
    }

    private void WriteParseStamp(string version) =>
        File.WriteAllText(Path.Combine(_folder, Map3DLocalBuildService.ParserVersionFileName), version + "\n");

    [TestMethod]
    public void ParseWithoutStamp_IsOutdated()
    {
        // Parses cached before stamps were kept, such as every route drawn around oil rigs.
        Assert.IsTrue(Map3DLocalBuildService.IsParseOutdated(_folder, "parser-v28"));
    }

    [TestMethod]
    public void ParseStamp_IsOutdatedOnlyWhenItDiffers()
    {
        WriteParseStamp("parser-v28");
        Assert.IsFalse(Map3DLocalBuildService.IsParseOutdated(_folder, "parser-v28"));

        WriteParseStamp("parser-v27");
        Assert.IsTrue(Map3DLocalBuildService.IsParseOutdated(_folder, "parser-v28"));
    }

    [TestMethod]
    public void UnknownParserVersionOrNoParse_IsNeverOutdated()
    {
        WriteParseStamp("parser-v27");
        Assert.IsFalse(Map3DLocalBuildService.IsParseOutdated(_folder, null), "without the parser's own stamp, a reparse proves nothing");

        File.Delete(Path.Combine(_folder, "map_data.json"));
        Assert.IsFalse(Map3DLocalBuildService.IsParseOutdated(_folder, "parser-v28"), "nothing parsed, nothing to redo");
    }

    [TestMethod]
    public void CurrentParserVersion_IsReadBesideTheParser()
    {
        string parserDir = Path.Combine(_folder, "MapParser");
        Directory.CreateDirectory(parserDir);
        string parser = Path.Combine(parserDir, "MapParser.dll");
        Assert.IsNull(Map3DLocalBuildService.CurrentParserVersion(parser));
        Assert.IsNull(Map3DLocalBuildService.CurrentParserVersion(null));

        File.WriteAllText(Path.Combine(parserDir, Map3DLocalBuildService.ParserVersionFileName), " parser-v28 \r\n");
        Assert.AreEqual("parser-v28", Map3DLocalBuildService.CurrentParserVersion(parser));
    }
}
