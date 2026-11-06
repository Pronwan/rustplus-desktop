using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services.ModelPacks;

namespace RustPlusDesktop.Tests;

[TestClass]
public class ModelStoreTests
{
    private const string Wall = "assets/prefabs/Building Core/wall/wall.glb";
    private const string Rock = "assets/bundled/prefabs/autospawn/decor/rock.glb";

    private string _folder = "";
    private string Source => Path.Combine(_folder, "current", "FP");
    private string Store => Path.Combine(_folder, "store", "FP");

    [TestInitialize]
    public void Create() => _folder = Path.Combine(Path.GetTempPath(), "RustPlusDesk-ModelStore-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Remove()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [TestMethod]
    public void Adopt_CopiesMatchingModels_AndTheySurviveTheInstallFolderBeingReplaced()
    {
        Write(Source, Wall, "wall");
        Write(Source, Rock, "rock");
        ModelManifest manifest = Manifest((Wall, "wall"), (Rock, "rock"));

        ModelAdoptionResult result = new ModelStore(Store).Adopt(manifest, Source);

        Assert.AreEqual(new ModelAdoptionResult(2, 0, 0, 0), result);
        Directory.Delete(Path.GetDirectoryName(Source)!, recursive: true);
        Assert.AreEqual("wall", Read(Store, Wall));
        Assert.AreEqual(new ModelAdoptionResult(0, 2, 0, 0), new ModelStore(Store).Adopt(manifest, Source));
    }

    [TestMethod]
    public void Adopt_ReplacesAnOutdatedModel_WithTheInstalledVersion()
    {
        Write(Store, Wall, "old wall!");
        Write(Source, Wall, "new wall!");

        ModelAdoptionResult result = new ModelStore(Store).Adopt(Manifest((Wall, "new wall!")), Source);

        Assert.AreEqual(1, result.Adopted);
        Assert.AreEqual("new wall!", Read(Store, Wall));
    }

    [TestMethod]
    public void Adopt_LeavesAStoreFileAlone_WhenTheInstalledCopyDoesNotMatchTheManifest()
    {
        Write(Store, Wall, "old wall");
        Write(Source, Wall, "bad wall");

        ModelAdoptionResult result = new ModelStore(Store).Adopt(Manifest((Wall, "new wall")), Source);

        Assert.AreEqual(new ModelAdoptionResult(0, 0, 1, 0), result);
        Assert.AreEqual("old wall", Read(Store, Wall));
    }

    [TestMethod]
    public void Adopt_CountsAMissingModel_AsUnavailable()
    {
        Directory.CreateDirectory(Source);

        ModelAdoptionResult result = new ModelStore(Store).Adopt(Manifest((Wall, "wall")), Source);

        Assert.AreEqual(new ModelAdoptionResult(0, 0, 1, 0), result);
    }

    [TestMethod]
    public void Adopt_DefersAModelAViewerHasOpen_AndFinishesItOnTheNextPass()
    {
        Write(Store, Wall, "old wall");
        Write(Source, Wall, "new wall");
        ModelManifest manifest = Manifest((Wall, "new wall"));

        using (new FileStream(Path.Combine(Store, Wall), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.AreEqual(1, new ModelStore(Store).Adopt(manifest, Source).Deferred);

        Assert.AreEqual(1, new ModelStore(Store).Adopt(manifest, Source).Adopted);
        Assert.AreEqual("new wall", Read(Store, Wall));
    }

    [TestMethod]
    public void Has_NoticesAFileChangedSinceItWasChecked()
    {
        Write(Store, Wall, "wall");
        ModelManifest manifest = Manifest((Wall, "wall"));
        ModelFile file = manifest.Files[Wall];
        var store = new ModelStore(Store);
        Assert.IsTrue(store.Has(Wall, file));

        Write(Store, Wall, "ruin");
        File.SetLastWriteTimeUtc(Path.Combine(Store, Wall), DateTime.UtcNow.AddMinutes(1));

        Assert.IsFalse(store.Has(Wall, file));
    }

    [DataTestMethod]
    [DataRow("assets/prefabs/wall.glb", true)]
    [DataRow("assets/prefabs/Building Core/wall.glb", true)]
    [DataRow("../assets/wall.glb", false)]
    [DataRow("assets/../../wall.glb", false)]
    [DataRow("assets//wall.glb", false)]
    [DataRow(@"assets\wall.glb", false)]
    [DataRow("C:/assets/wall.glb", false)]
    [DataRow("models-manifest.json", false)]
    public void IsSafePath_KeepsFilesInsideTheAssetsTree(string path, bool safe)
    {
        Assert.AreEqual(safe, ModelManifest.IsSafePath(path));
    }

    [TestMethod]
    public void TryParse_RejectsAManifestItCannotTrust()
    {
        string valid = Json(Hash("wall"), Wall, "builder", schema: 1);

        Assert.IsNotNull(ModelManifest.TryParse(valid));
        Assert.IsNull(ModelManifest.TryParse(Json(Hash("wall"), Wall, "builder", schema: 2)));
        Assert.IsNull(ModelManifest.TryParse(Json("not-a-hash", Wall, "builder", schema: 1)));
        Assert.IsNull(ModelManifest.TryParse(Json(Hash("wall"), "../wall.glb", "builder", schema: 1)));
        Assert.IsNull(ModelManifest.TryParse(Json(Hash("wall"), Wall, "no-such-pack", schema: 1)));
        Assert.IsNull(ModelManifest.TryParse("not json"));
    }

    [TestMethod]
    public void TryParse_AcceptsTheManifestMapParserGenerates()
    {
        string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "vendor", "MapParser", "Rust_Assets", "FP", ModelManifest.FileName));
        if (!File.Exists(path)) Assert.Inconclusive("MapParser is not checked out.");

        ModelManifest? manifest = ModelManifest.TryParse(File.ReadAllText(path));

        Assert.IsNotNull(manifest);
        Assert.IsTrue(manifest.Packs.ContainsKey("builder"));
        Assert.AreEqual(manifest.Files.Count, manifest.Packs.Values.Sum(pack => pack.Files));
    }

    private static string Json(string sha256, string path, string pack, int schema) => JsonSerializer.Serialize(new
    {
        schema,
        contentId = "test",
        packs = new Dictionary<string, object> { ["builder"] = new { title = "Building models" } },
        files = new Dictionary<string, object> { [path] = new { sha256, size = 4, pack } }
    });

    private static ModelManifest Manifest(params (string Path, string Content)[] files) => new()
    {
        Schema = ModelManifest.SupportedSchema,
        ContentId = "test",
        Files = files.ToDictionary(file => file.Path, file => new ModelFile
        {
            Sha256 = Hash(file.Content),
            Size = Encoding.UTF8.GetByteCount(file.Content),
            Pack = "builder"
        })
    };

    private static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static void Write(string root, string relativePath, string content)
    {
        string path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Read(string root, string relativePath) => File.ReadAllText(Path.Combine(root, relativePath));
}
