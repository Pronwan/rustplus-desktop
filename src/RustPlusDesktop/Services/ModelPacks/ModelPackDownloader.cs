using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.ModelPacks
{
    /// <summary>Bytes fetched so far, out of everything this download will fetch.</summary>
    public sealed record ModelDownloadProgress(long DoneBytes, long TotalBytes);

    /// <summary>What a download changed in the store.</summary>
    /// <param name="Added">Files that were not on this machine.</param>
    /// <param name="Replaced">Files that took the place of an older version.</param>
    /// <param name="Deferred">Files that could not be moved in, usually because a viewer has them open.</param>
    public sealed record ModelDownloadResult(int Added, int Replaced, int Deferred);

    /// <summary>
    /// Fetches the files a pack is missing, and only those.
    /// </summary>
    /// <remarks>
    /// A handful of changed models come one by one from the tag's raw files, which GitHub rate limits.
    /// A new pack, or an update that touches much of one, comes as the release's zip instead.
    ///
    /// A pack is complete before any of it enters the store: everything is staged and checked against
    /// its hash first, so a cancelled or failed download leaves the store as it was. Files are taken
    /// from a zip by the name the manifest gives them, never by the name the zip gives them.
    /// </remarks>
    public sealed class ModelPackDownloader
    {
        public const int MaxSingleFiles = 50;
        private const int ParallelDownloads = 4;
        private const int BufferSize = 81920;

        private readonly HttpClient _http;
        private readonly Uri _filesRoot;
        private readonly Uri _releasesRoot;

        public ModelPackDownloader(HttpClient http, Uri filesRoot, Uri releasesRoot)
        {
            _http = http;
            _filesRoot = filesRoot;
            _releasesRoot = releasesRoot;
        }

        public static string RefOf(ModelManifest manifest) => "models-" + manifest.ContentId;

        public static bool ShouldUseZip(int missingFiles, long missingBytes, long packBytes) =>
            missingFiles > MaxSingleFiles || missingBytes * 2 > packBytes;

        public async Task<ModelDownloadResult> DownloadAsync(ModelStore store, ModelManifest manifest, IReadOnlyCollection<string> packs,
            IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken)
        {
            var plans = packs.Select(pack => (Pack: pack, Files: store.Missing(manifest, pack))).Where(plan => plan.Files.Count > 0).ToList();
            var tracker = new Tracker(progress, plans.Sum(plan => PlannedBytes(manifest, plan.Pack, plan.Files)));
            int added = 0, replaced = 0, deferred = 0;

            string staging = store.StagingFolder;
            Directory.CreateDirectory(staging);
            try
            {
                foreach ((string pack, List<KeyValuePair<string, ModelFile>> files) in plans)
                {
                    await StageAsync(manifest, pack, files, staging, tracker, cancellationToken).ConfigureAwait(false);

                    // Some models are byte-for-byte copies under another path. They share one staged
                    // file, so all but the last take a copy of it.
                    var uses = files.GroupBy(file => file.Value.Sha256).ToDictionary(group => group.Key, group => group.Count());
                    foreach ((string relativePath, ModelFile file) in files)
                    {
                        try
                        {
                            string staged = Path.Combine(staging, file.Sha256);
                            if (--uses[file.Sha256] > 0)
                            {
                                File.Copy(staged, staged + ".copy", overwrite: true);
                                staged += ".copy";
                            }
                            if (store.Accept(staged, relativePath, file)) replaced++;
                            else added++;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            deferred++;
                        }
                    }
                }
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                store.Save();
            }
            return new ModelDownloadResult(added, replaced, deferred);
        }

        private static long PlannedBytes(ModelManifest manifest, string pack, List<KeyValuePair<string, ModelFile>> files)
        {
            long missing = files.Sum(file => file.Value.Size);
            long packBytes = manifest.Packs[pack].Bytes;
            return ShouldUseZip(files.Count, missing, packBytes) ? packBytes : missing;
        }

        private async Task StageAsync(ModelManifest manifest, string pack, List<KeyValuePair<string, ModelFile>> files, string staging, Tracker tracker, CancellationToken cancellationToken)
        {
            string gitRef = RefOf(manifest);
            if (ShouldUseZip(files.Count, files.Sum(file => file.Value.Size), manifest.Packs[pack].Bytes))
            {
                await StageFromZipAsync(gitRef, pack, files, staging, tracker, cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                await StageOneByOneAsync(gitRef, files, staging, tracker, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // Rate limited, most likely. Release downloads are not.
                await StageFromZipAsync(gitRef, pack, files, staging, tracker, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task StageOneByOneAsync(string gitRef, List<KeyValuePair<string, ModelFile>> files, string staging, Tracker tracker, CancellationToken cancellationToken)
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = ParallelDownloads, CancellationToken = cancellationToken };
            await Parallel.ForEachAsync(files.DistinctBy(file => file.Value.Sha256), options, async (entry, token) =>
            {
                string escaped = string.Join('/', entry.Key.Split('/').Select(Uri.EscapeDataString));
                var uri = new Uri(_filesRoot, $"{gitRef}/{escaped}");
                using HttpResponseMessage response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                using Stream body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await WriteVerifiedAsync(body, Path.Combine(staging, entry.Value.Sha256), entry.Value, tracker, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        private async Task StageFromZipAsync(string gitRef, string pack, List<KeyValuePair<string, ModelFile>> files, string staging, Tracker tracker, CancellationToken cancellationToken)
        {
            string zipPath = Path.Combine(staging, pack + ".zip");
            var uri = new Uri(_releasesRoot, $"{gitRef}/{pack}.zip");
            using (HttpResponseMessage response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using FileStream output = new(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    tracker.Add(read);
                }
            }

            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                foreach ((string relativePath, ModelFile file) in files.DistinctBy(file => file.Value.Sha256))
                {
                    ZipArchiveEntry entry = zip.GetEntry(relativePath) ?? throw new InvalidDataException($"The {pack} pack does not contain {relativePath}.");
                    using Stream body = entry.Open();
                    await WriteVerifiedAsync(body, Path.Combine(staging, file.Sha256), file, null, cancellationToken).ConfigureAwait(false);
                }
            }
            File.Delete(zipPath);
        }

        private static async Task WriteVerifiedAsync(Stream source, string target, ModelFile file, Tracker? tracker, CancellationToken cancellationToken)
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long written = 0;
            using (FileStream output = new(target, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    written += read;
                    if (written > file.Size) break;
                    sha.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    tracker?.Add(read);
                }
            }

            if (written != file.Size || Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant() != file.Sha256)
                throw new InvalidDataException($"A downloaded model did not match its hash ({file.Sha256[..12]}).");
        }

        private sealed class Tracker
        {
            private readonly IProgress<ModelDownloadProgress>? _progress;
            private readonly long _total;
            private long _done;

            public Tracker(IProgress<ModelDownloadProgress>? progress, long total)
            {
                _progress = progress;
                _total = total;
            }

            public void Add(long bytes) => _progress?.Report(new ModelDownloadProgress(Interlocked.Add(ref _done, bytes), _total));
        }
    }
}
