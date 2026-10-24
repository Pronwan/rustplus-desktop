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
}
