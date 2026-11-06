using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services.ModelPacks;

namespace RustPlusDesktop.Tests;

[TestClass]
public class ModelPackDownloaderTests
{
    private const string Wall = "assets/prefabs/Building Core/wall/wall.glb";
    private const string Door = "assets/prefabs/Building/door/door.glb";
    private const string Ref = "models-test";

    private string _folder = "";
    private FakeServer _server = null!;
    private string Store => Path.Combine(_folder, "FP");

    [TestInitialize]
    public void Create()
    {
        _folder = Path.Combine(Path.GetTempPath(), "RustPlusDesk-ModelPack-" + Guid.NewGuid().ToString("N"));
        _server = new FakeServer();
    }

    [TestCleanup]
    public void Remove()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [TestMethod]
    public async Task ANewPack_ComesFromTheZip_AndEveryFileLands()
    {
        _server.Files[$"https://releases.test/{Ref}/builder.zip"] = Zip((Wall, "wall"), (Door, "door"));
        ModelManifest manifest = Manifest((Wall, "wall"), (Door, "door"));

        ModelDownloadResult result = await Download(manifest);

        Assert.AreEqual(new ModelDownloadResult(2, 0, 0), result);
        Assert.AreEqual("wall", Read(Wall));
        Assert.AreEqual("door", Read(Door));
        Assert.AreEqual(1, _server.Requests.Count);
        Assert.IsFalse(Directory.Exists(Path.Combine(Store, ".staging")));
    }

    [TestMethod]
    public async Task IdenticalModelsUnderTwoPaths_BothLand_FromAZip()
    {
        _server.Files[$"https://releases.test/{Ref}/builder.zip"] = Zip((Wall, "same"), (Door, "same"));

        ModelDownloadResult result = await Download(Manifest((Wall, "same"), (Door, "same")));

        Assert.AreEqual(new ModelDownloadResult(2, 0, 0), result);
        Assert.AreEqual("same", Read(Wall));
        Assert.AreEqual("same", Read(Door));
    }

    [TestMethod]
    public async Task IdenticalModelsUnderTwoPaths_DownloadOnce_AsSingleFiles()
    {
        ModelManifest manifest = Manifest((Wall, "same"), (Door, "same"));
        manifest.Packs["builder"].Bytes = 1000;
        _server.Files[$"https://files.test/{Ref}/assets/prefabs/Building%20Core/wall/wall.glb"] = Encoding.UTF8.GetBytes("same");
        _server.Files[$"https://files.test/{Ref}/assets/prefabs/Building/door/door.glb"] = Encoding.UTF8.GetBytes("same");

        ModelDownloadResult result = await Download(manifest);

        Assert.AreEqual(new ModelDownloadResult(2, 0, 0), result);
        Assert.AreEqual(1, _server.Requests.Count);
        Assert.AreEqual("same", Read(Door));
    }

    [TestMethod]
    public async Task AReplacedModel_DownloadsOnlyThatFile()
    {
        Write(Wall, "old!");
        Write(Door, "door");
        // A big pack, so one changed model is a small part of it.
        ModelManifest manifest = Manifest((Wall, "new!"), (Door, "door"));
        manifest.Packs["builder"].Bytes = 1000;
        _server.Files[$"https://files.test/{Ref}/assets/prefabs/Building%20Core/wall/wall.glb"] = Encoding.UTF8.GetBytes("new!");

        ModelDownloadResult result = await Download(manifest);

        Assert.AreEqual(new ModelDownloadResult(0, 1, 0), result);
        Assert.AreEqual("new!", Read(Wall));
        CollectionAssert.AreEqual(new[] { $"https://files.test/{Ref}/assets/prefabs/Building%20Core/wall/wall.glb" }, _server.Requests.ToArray());
    }

    [TestMethod]
    public async Task ARateLimitedSingleFile_FallsBackToTheZip()
    {
        Write(Door, "door");
        ModelManifest manifest = Manifest((Wall, "wall"), (Door, "door"));
        manifest.Packs["builder"].Bytes = 1000;
        _server.Files[$"https://releases.test/{Ref}/builder.zip"] = Zip((Wall, "wall"), (Door, "door"));

        ModelDownloadResult result = await Download(manifest);

        Assert.AreEqual(new ModelDownloadResult(1, 0, 0), result);
        Assert.AreEqual("wall", Read(Wall));
    }

    [TestMethod]
    public async Task ABadHash_FailsTheDownload_AndLeavesTheStoreAlone()
    {
        Write(Wall, "old!");
        _server.Files[$"https://releases.test/{Ref}/builder.zip"] = Zip((Wall, "evil"));

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Download(Manifest((Wall, "new!"))));

        Assert.AreEqual("old!", Read(Wall));
    }

    [TestMethod]
    public async Task ACancelledDownload_LeavesNothingBehind()
    {
        _server.Files[$"https://releases.test/{Ref}/builder.zip"] = Zip((Wall, "wall"));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => Download(Manifest((Wall, "wall")), cancel.Token));

        Assert.IsFalse(File.Exists(Path.Combine(Store, Wall)));
        Assert.IsFalse(new ModelStore(Store).HasAny(Manifest((Wall, "wall")), "builder"));
    }

    [TestMethod]
    public async Task AZip_CanOnlyWriteWhatTheManifestLists()
    {
        _server.Files[$"https://releases.test/{Ref}/builder.zip"] = Zip((Wall, "wall"), ("../../escaped.txt", "escaped"));

        await Download(Manifest((Wall, "wall")));

        Assert.IsFalse(File.Exists(Path.Combine(_folder, "escaped.txt")));
        Assert.IsFalse(File.Exists(Path.GetFullPath(Path.Combine(Store, "..", "..", "escaped.txt"))));
    }

    [TestMethod]
    public async Task AnUpToDatePack_DownloadsNothing()
    {
        Write(Wall, "wall");

        ModelDownloadResult result = await Download(Manifest((Wall, "wall")));

        Assert.AreEqual(new ModelDownloadResult(0, 0, 0), result);
        Assert.AreEqual(0, _server.Requests.Count);
    }

    [DataTestMethod]
    [DataRow(1, 10L, 1000L, false)]
    [DataRow(50, 10L, 1000L, false)]
    [DataRow(51, 10L, 1000L, true)]
    [DataRow(1, 600L, 1000L, true)]
    public void ShouldUseZip_WhenManyFilesOrMostOfThePackChanged(int files, long missingBytes, long packBytes, bool zip)
    {
        Assert.AreEqual(zip, ModelPackDownloader.ShouldUseZip(files, missingBytes, packBytes));
    }

    [TestMethod]
    public void RemoveUnlisted_DeletesOnlyModelsTheManifestDropped()
    {
        Write(Wall, "wall");
        Write(Door, "door");
        var store = new ModelStore(Store);

        int removed = store.RemoveUnlisted(Manifest((Wall, "wall")));

        Assert.AreEqual(1, removed);
        Assert.IsTrue(File.Exists(Path.Combine(Store, Wall)));
        Assert.IsFalse(File.Exists(Path.Combine(Store, Door)));
    }

    private Task<ModelDownloadResult> Download(ModelManifest manifest, CancellationToken cancellationToken = default)
    {
        var downloader = new ModelPackDownloader(new HttpClient(_server), new Uri("https://files.test/"), new Uri("https://releases.test/"));
        return downloader.DownloadAsync(new ModelStore(Store), manifest, new[] { "builder" }, null, cancellationToken);
    }

    private static ModelManifest Manifest(params (string Path, string Content)[] files)
    {
        var entries = files.ToDictionary(file => file.Path, file => new ModelFile
        {
            Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file.Content))).ToLowerInvariant(),
            Size = Encoding.UTF8.GetByteCount(file.Content),
            Pack = "builder"
        });
        return new ModelManifest
        {
            Schema = ModelManifest.SupportedSchema,
            ContentId = "test",
            Packs = new() { ["builder"] = new ModelPack { Files = files.Length, Bytes = entries.Values.Sum(file => file.Size) } },
            Files = entries
        };
    }

    private static byte[] Zip(params (string Path, string Content)[] files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, string content) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path, CompressionLevel.NoCompression).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }
        return memory.ToArray();
    }

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(Store, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string Read(string relativePath) => File.ReadAllText(Path.Combine(Store, relativePath));

    private sealed class FakeServer : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Files { get; } = new();
        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string url = request.RequestUri!.AbsoluteUri;
            lock (Requests) Requests.Add(url);
            return Task.FromResult(Files.TryGetValue(url, out byte[]? bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        }
    }
}
