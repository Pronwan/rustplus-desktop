using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.ModelPacks
{
    /// <summary>A pack as it stands on this machine.</summary>
    public sealed record ModelPackStatus(string Id, string Title, string Description, long PackBytes, long MissingBytes, int MissingFiles, bool Installed);

    /// <summary>
    /// Keeps the model packs on this machine in step with the published models.
    /// </summary>
    /// <remarks>
    /// Each app release embeds the manifest it was built with. Models can also change between releases:
    /// <c>latest.json</c> names the newest published set, and installed packs follow it, fetching only
    /// files whose hash changed. Small changes arrive quietly; a large one waits for the prompt the next
    /// time a viewer opens. Only a lite install follows along, because a full install's bundled files
    /// would overwrite newer ones each time the viewer folder is refreshed.
    /// </remarks>
    public sealed class ModelPackService
    {
        private const string FilesRoot = "https://raw.githubusercontent.com/JawadYzbk/rustplus-models/";
        private const string ReleasesRoot = "https://github.com/JawadYzbk/rustplus-models/releases/download/";
        private const string LatestPath = "master/latest.json";
        private const string ActiveManifestName = ".active-manifest.json";
        private const string StaleCacheMarker = ".viewer-cache-stale";
        private const string FirstRunMarker = ".first-run-offered";
        private const long QuietUpdateLimit = 25L * 1024 * 1024;

        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
        private static readonly Regex ContentIdPattern = new("^[0-9a-f]{16}$", RegexOptions.CultureInvariant);
        private static readonly HttpClient Http = new(new TrafficTrackingHttpMessageHandler("3D models")) { Timeout = TimeSpan.FromMinutes(30) };

        private sealed record ActiveManifest(string BasedOn, string Json);
        private sealed record Latest(int schema, string contentId);

        public static ModelPackService Instance { get; } = new();

        private const int ReportIntervalMs = 200;

        // One writer at a time: a download and the background update share the store.
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Dictionary<string, Task> _running = new();
        private readonly Stopwatch _sinceReport = Stopwatch.StartNew();
        private CancellationTokenSource _cancel = new();

        /// <summary>Raised on a worker thread as a download moves, and when it ends.</summary>
        public event EventHandler? DownloadChanged;

        /// <summary>Null when nothing is downloading.</summary>
        public ModelDownloadProgress? CurrentProgress { get; private set; }

        /// <summary>Why the last download failed; null when it did not.</summary>
        public string? LastError { get; private set; }

        public bool IsDownloading
        {
            get { lock (_running) return _running.Count > 0; }
        }

        private static string StoreRoot => ModelStoreService.StoreRoot;

        private static string? BundledFolder => ModelStoreService.Instance.ViewerSourceRoot is { } root
            ? Path.Combine(root, "Rust_Assets", "FP")
            : null;

        /// <summary>True when this install ships without models, and so depends on the packs.</summary>
        public bool IsLite => BundledFolder is { } folder && !Directory.Exists(Path.Combine(folder, "assets"));

        public async Task<IReadOnlyList<ModelPackStatus>> GetStatusAsync(IReadOnlyCollection<string> packs)
        {
            await ModelStoreService.Instance.WaitForAdoptionAsync().ConfigureAwait(false);

            // Only reads the store, so a download in progress does not hold it up. The hash index is
            // saved only when nothing else is writing it.
            return await Task.Run(() =>
            {
                ModelManifest? manifest = LoadActive();
                if (manifest == null) return (IReadOnlyList<ModelPackStatus>)Array.Empty<ModelPackStatus>();

                var store = new ModelStore(StoreRoot);
                var result = packs.Where(manifest.Packs.ContainsKey).Select(id =>
                {
                    ModelPack pack = manifest.Packs[id];
                    var missing = store.Missing(manifest, id);
                    return new ModelPackStatus(id, pack.Title, pack.Description, pack.Bytes, missing.Sum(file => file.Value.Size), missing.Count, store.HasAny(manifest, id));
                }).ToList();

                if (_gate.Wait(0))
                {
                    try { store.Save(); }
                    finally { _gate.Release(); }
                }
                return result;
            }).ConfigureAwait(false);
        }

        /// <summary>Starts the packs that are not already downloading. The job outlives any window watching it.</summary>
        public Task DownloadAsync(IReadOnlyCollection<string> packs)
        {
            lock (_running)
            {
                var fresh = packs.Where(pack => !_running.ContainsKey(pack)).ToList();
                if (fresh.Count > 0)
                {
                    if (_running.Count == 0) _cancel = new CancellationTokenSource();
                    LastError = null;
                    Task job = RunAsync(fresh, _cancel.Token);
                    foreach (string pack in fresh) _running[pack] = job;
                }
                return Task.WhenAll(packs.Select(pack => _running[pack]).Distinct());
            }
        }

        /// <summary>Waits for whichever of these packs are downloading right now.</summary>
        public Task WaitForAsync(IReadOnlyCollection<string> packs)
        {
            lock (_running) return Task.WhenAll(packs.Where(_running.ContainsKey).Select(pack => _running[pack]).Distinct());
        }

        public bool IsDownloadingPack(string pack)
        {
            lock (_running) return _running.ContainsKey(pack);
        }

        public void CancelDownload()
        {
            lock (_running) _cancel.Cancel();
        }

        private async Task RunAsync(List<string> packs, CancellationToken cancellationToken)
        {
            await Task.Yield();
            try
            {
                await InstallAsync(packs, new ProgressRelay(Report), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ex is not OperationCanceledException) LastError = ex.Message;
                throw;
            }
            finally
            {
                lock (_running)
                {
                    foreach (string pack in packs) _running.Remove(pack);
                    if (_running.Count == 0) CurrentProgress = null;
                }
                DownloadChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Report(ModelDownloadProgress progress)
        {
            CurrentProgress = progress;
            if (_sinceReport.ElapsedMilliseconds < ReportIntervalMs) return;
            _sinceReport.Restart();
            DownloadChanged?.Invoke(this, EventArgs.Empty);
        }

        private sealed class ProgressRelay : IProgress<ModelDownloadProgress>
        {
            private readonly Action<ModelDownloadProgress> _report;
            public ProgressRelay(Action<ModelDownloadProgress> report) => _report = report;
            public void Report(ModelDownloadProgress value) => _report(value);
        }

        private async Task InstallAsync(IReadOnlyCollection<string> packs, IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken)
        {
            await ModelStoreService.Instance.WaitForAdoptionAsync().ConfigureAwait(false);
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ModelManifest manifest = LoadActive() ?? throw new InvalidOperationException("The list of 3D models is missing. Reinstall RustPlus Desktop to restore it.");
                ModelDownloadResult result = await Downloader().DownloadAsync(new ModelStore(StoreRoot), manifest, packs, progress, cancellationToken).ConfigureAwait(false);
                if (result.Replaced > 0) MarkViewerCacheStale();
            }
            finally { _gate.Release(); }
        }

        /// <summary>Looks for newer models once the app has settled. Failure only means trying next start.</summary>
        public async Task CheckForUpdatesAfterStartAsync()
        {
            try
            {
                await ModelStoreService.Instance.WaitForAdoptionAsync().ConfigureAwait(false);
                if (!IsLite) return;
                await Task.Delay(StartupDelay).ConfigureAwait(false);
                await CheckForUpdatesAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Models] Update check failed: " + ex.Message);
            }
        }

        private async Task CheckForUpdatesAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ModelManifest? manifest = LoadActive();
                if (manifest == null) return;

                string latestJson = await Http.GetStringAsync(new Uri(new Uri(FilesRoot), LatestPath), cancellationToken).ConfigureAwait(false);
                Latest? latest = JsonSerializer.Deserialize<Latest>(latestJson);
                if (latest == null || latest.schema != ModelManifest.SupportedSchema || !ContentIdPattern.IsMatch(latest.contentId ?? "")) return;

                if (latest.contentId != manifest.ContentId)
                {
                    var uri = new Uri(new Uri(FilesRoot), $"models-{latest.contentId}/{ModelManifest.FileName}");
                    string json = await Http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
                    ModelManifest? newer = ModelManifest.TryParse(json);
                    if (newer == null || newer.ContentId != latest.contentId) return;
                    SaveActive(json);
                    manifest = newer;
                }

                var store = new ModelStore(StoreRoot);
                var installed = manifest.Packs.Keys.Where(pack => store.HasAny(manifest, pack)).ToList();
                long missing = installed.Sum(pack => store.Missing(manifest, pack).Sum(file => file.Value.Size));
                store.Save();
                if (missing == 0 || missing > QuietUpdateLimit) return;

                ModelDownloadResult result = await Downloader().DownloadAsync(store, manifest, installed, null, cancellationToken).ConfigureAwait(false);
                int removed = store.RemoveUnlisted(manifest);
                store.Save();
                if (result.Replaced > 0 || removed > 0) MarkViewerCacheStale();
                Debug.WriteLine($"[Models] {manifest.ContentId}: {result}, removed {removed}");
            }
            finally { _gate.Release(); }
        }

        /// <summary>True once per machine, when a lite install has no models yet.</summary>
        public async Task<bool> TakeFirstRunOfferAsync()
        {
            await ModelStoreService.Instance.WaitForAdoptionAsync().ConfigureAwait(false);
            string marker = Path.Combine(StoreRoot, FirstRunMarker);
            if (!IsLite || File.Exists(marker)) return false;

            Directory.CreateDirectory(StoreRoot);
            File.WriteAllText(marker, "");
            ModelManifest? manifest = LoadActive();
            if (manifest == null) return false;
            var store = new ModelStore(StoreRoot);
            return !manifest.Packs.Keys.Any(pack => store.HasAny(manifest, pack));
        }

        /// <summary>
        /// True once after a model was replaced. WebView2 caches models by URL and the URL has not
        /// changed, so the viewer has to drop its cache before it can show the new version.
        /// </summary>
        public bool TakeViewerCacheStale()
        {
            string marker = Path.Combine(StoreRoot, StaleCacheMarker);
            if (!File.Exists(marker)) return false;
            try { File.Delete(marker); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return true;
        }

        private static void MarkViewerCacheStale() => File.WriteAllText(Path.Combine(StoreRoot, StaleCacheMarker), "");

        private static ModelPackDownloader Downloader() => new(Http, new Uri(FilesRoot), new Uri(ReleasesRoot));

        private static ModelManifest? LoadBundled()
        {
            if (BundledFolder is not { } folder) return null;
            string path = Path.Combine(folder, ModelManifest.FileName);
            return File.Exists(path) ? ModelManifest.TryParse(File.ReadAllText(path)) : null;
        }

        // A downloaded manifest only outranks the app it was fetched for. An app update brings its own,
        // which is at least as new, and the next check moves on from there.
        private static ModelManifest? LoadActive()
        {
            ModelManifest? bundled = LoadBundled();
            string path = Path.Combine(StoreRoot, ActiveManifestName);
            try
            {
                if (bundled != null && File.Exists(path)
                    && JsonSerializer.Deserialize<ActiveManifest>(File.ReadAllText(path)) is { } active
                    && active.BasedOn == bundled.ContentId
                    && ModelManifest.TryParse(active.Json) is { } manifest)
                {
                    return manifest;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Debug.WriteLine("[Models] Ignoring the saved manifest: " + ex.Message);
            }
            return bundled;
        }

        private static void SaveActive(string json)
        {
            Directory.CreateDirectory(StoreRoot);
            string path = Path.Combine(StoreRoot, ActiveManifestName);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new ActiveManifest(LoadBundled()?.ContentId ?? "", json)));
            File.Move(path + ".tmp", path, overwrite: true);
        }
    }
}
