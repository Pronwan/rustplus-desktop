using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.GeneticsLab
{
    /// <summary>A Genetics Lab build on disk that the WebView can be pointed at.</summary>
    public sealed record GeneticsLabContent(string Folder, string? BuildId, Version? Version, bool IsBundled);

    /// <summary>An update in progress (<c>downloading</c>) or installed and waiting (<c>ready</c>).</summary>
    public sealed record GeneticsLabUpdateStatus(string State, string? Version, JsonElement? Changelog);

    /// <summary>
    /// Keeps the embedded Genetics Lab current without waiting for a desktop release.
    /// </summary>
    /// <remarks>
    /// Every desktop build ships a copy of Genetics Lab (<c>Features/GeneticsLab/dist</c>). That copy
    /// is only the starting point: in the background this service reads the manifest of the latest
    /// Genetics Lab release -- from the repository's <c>dist</c> branch on GitHub, or from the hosted
    /// site when GitHub cannot be reached -- and when it describes a different build it assembles
    /// that build under <c>%LocalAppData%\RustPlusDesk\GeneticsLab\versions</c>.
    ///
    /// Updates are partial. A file whose SHA-256 already exists locally -- in the bundled copy or
    /// in the previous download -- is hard-linked (or copied) instead of downloaded, so a typical
    /// release fetches a few changed JS chunks rather than the whole 40 MB build.
    ///
    /// A new build is staged in its own folder, every file is checked against its hash, and only
    /// then does <c>active.json</c> switch over, so a failed or interrupted update leaves the
    /// working copy untouched. The page that is already open keeps its folder until the user
    /// reloads or reopens Genetics Lab.
    /// </remarks>
    public sealed class GeneticsLabContentService
    {
        public static GeneticsLabContentService Instance { get; } = new();

        // Released builds, published by rust-genetics-lab's release workflow. The manifest names
        // the immutable dist-vX.Y.Z tag its files are read from.
        private const string GitHubRawRoot = "https://raw.githubusercontent.com/JawadYzbk/rust-genetics-lab/";
        private const string GitHubDistBranch = "dist";
        // Fallback: the Coolify deployment serves the same files next to its manifest.
        private const string SiteBaseUrl = "https://genetics.rustplusdesktop.cloud/";
        // Lets a tester point the desktop at a staging deployment instead of both sources.
        private const string BaseUrlEnvironmentVariable = "RUSTPLUSDESK_GENETICSLAB_URL";
        private const string ActivePointerName = "active.json";
        private const string StagingMarker = ".partial-";
        private const int MaxManifestBytes = 2 * 1024 * 1024;
        private const int MaxParallelDownloads = 4;
        // Floor between checks, however often they are asked for (start-up, opening the tab, the
        // periodic timer). Also how long a failed check waits before the next try.
        private static readonly TimeSpan MinCheckInterval = TimeSpan.FromMinutes(15);
        // How often a running app looks again, so a release that lands mid-session is noticed
        // without a restart.
        private static readonly TimeSpan PeriodicCheckInterval = TimeSpan.FromMinutes(30);
        private Timer? _periodicTimer;

        private readonly string _rootDir;
        private readonly string _versionsDir;
        private readonly IReadOnlyList<UpdateSource> _sources;
        private readonly SemaphoreSlim _checkGate = new(1, 1);
        // Folders a WebView has been pointed at this session. Pruning never deletes these, because
        // the open page still loads lazy chunks, images and OCR data from them.
        private readonly ConcurrentDictionary<string, byte> _foldersInUse = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastCheckUtc = DateTime.MinValue;

        /// <summary>Where a manifest is read from, and where the files it lists are downloaded from.</summary>
        private sealed record UpdateSource(string Name, Uri ManifestUri, Func<GeneticsLabManifest, Uri> FileBase);

        /// <summary>The update in progress or waiting to be applied; null when there is none.</summary>
        public GeneticsLabUpdateStatus? Status { get; private set; }

        /// <summary>Raised on a worker thread whenever <see cref="Status"/> changes.</summary>
        public event EventHandler<GeneticsLabUpdateStatus?>? StatusChanged;

        private GeneticsLabContentService()
        {
            _rootDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RustPlusDesk",
                "GeneticsLab");
            _versionsDir = Path.Combine(_rootDir, "versions");

            string? configured = Environment.GetEnvironmentVariable(BaseUrlEnvironmentVariable);
            if (Uri.TryCreate(configured, UriKind.Absolute, out Uri? custom) && custom.Scheme == Uri.UriSchemeHttps)
            {
                _sources = new[] { SiteSource("custom", EnsureTrailingSlash(custom)) };
            }
            else
            {
                var raw = new Uri(GitHubRawRoot);
                _sources = new[]
                {
                    new UpdateSource(
                        "GitHub",
                        new Uri(raw, $"{GitHubDistBranch}/{GeneticsLabManifest.FileName}"),
                        manifest => new Uri(raw, $"{manifest.DistRef ?? GitHubDistBranch}/")),
                    SiteSource("genetics.rustplusdesktop.cloud", new Uri(SiteBaseUrl)),
                };
            }
        }

        private static UpdateSource SiteSource(string name, Uri baseUri) =>
            new(name, new Uri(baseUri, GeneticsLabManifest.FileName), _ => baseUri);

        private void SetStatus(GeneticsLabUpdateStatus? status)
        {
            Status = status;
            StatusChanged?.Invoke(this, status);
        }

        private static Version HostVersion
        {
            get
            {
                Version v = typeof(GeneticsLabContentService).Assembly.GetName().Version ?? new Version(0, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
            }
        }

        // ── Choosing what to load ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the newest usable build -- the downloaded one unless the bundled copy is newer --
        /// and records its folder as in use. Null when no build exists at all.
        /// </summary>
        public GeneticsLabContent? ResolveContent()
        {
            GeneticsLabContent? best = PickBest(LoadBundled(), LoadActiveDownload());
            if (best != null) _foldersInUse.TryAdd(best.Folder, 0);
            return best;
        }

        /// <summary>
        /// Records <paramref name="content"/> as the build the WebView is about to show.
        /// <c>Changed</c> is true when it differs from the build shown last time, so the caller can
        /// drop cached responses of the old one; <c>PreviousVersion</c> is that build's version, for
        /// the page's "What's new".
        /// </summary>
        public (bool Changed, string? PreviousVersion) MarkLoaded(GeneticsLabContent content)
        {
            try
            {
                string marker = Path.Combine(_rootDir, "last-loaded.txt");
                string id = content.BuildId ?? content.Folder;
                string[] previous = File.Exists(marker) ? File.ReadAllLines(marker) : Array.Empty<string>();
                string? previousId = previous.Length > 0 ? previous[0].Trim() : null;
                string? previousVersion = previous.Length > 1 && previous[1].Trim().Length > 0 ? previous[1].Trim() : null;
                if (previousId == id) return (false, null);

                Directory.CreateDirectory(_rootDir);
                File.WriteAllLines(marker, new[] { id, content.Version?.ToString(3) ?? "" });
                return (previousId != null, previousVersion);
            }
            catch
            {
                return (false, null);
            }
        }

        /// <summary>Lets pruning delete a folder once no WebView loads from it any more.</summary>
        public void ReleaseContent(GeneticsLabContent content) => _foldersInUse.TryRemove(content.Folder, out _);

        /// <summary>
        /// The build <see cref="ResolveContent"/> would pick now, when it differs from
        /// <paramref name="current"/>; otherwise null. Does not mark anything as in use.
        /// </summary>
        public GeneticsLabContent? FindNewerThan(GeneticsLabContent? current)
        {
            GeneticsLabContent? best = PickBest(LoadBundled(), LoadActiveDownload());
            if (best == null || current == null) return best;
            return string.Equals(best.Folder, current.Folder, StringComparison.OrdinalIgnoreCase) && best.BuildId == current.BuildId
                ? null
                : best;
        }

        private static GeneticsLabContent? PickBest(GeneticsLabContent? bundled, GeneticsLabContent? downloaded)
        {
            if (downloaded == null) return bundled;
            if (bundled == null) return downloaded;

            // The bundled copy wins only when it is strictly newer, e.g. right after a desktop update
            // that shipped a later Genetics Lab than the site had when this machine last checked.
            if (bundled.Version != null && (downloaded.Version == null || bundled.Version > downloaded.Version))
                return bundled;
            return downloaded;
        }

        private static GeneticsLabContent? LoadBundled()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, "Features", "GeneticsLab", "dist"),
                // Running from bin\<Configuration>\<tfm> in a development checkout.
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Features", "GeneticsLab", "dist")),
            };

            string? folder = candidates.FirstOrDefault(Directory.Exists);
            if (folder == null) return null;

            // Builds from before the manifest existed still load; they just never win a comparison.
            GeneticsLabManifest? manifest = ReadManifest(folder);
            return new GeneticsLabContent(folder, manifest?.BuildId, manifest?.ParsedVersion, IsBundled: true);
        }

        private GeneticsLabContent? LoadActiveDownload()
        {
            try
            {
                string pointer = Path.Combine(_rootDir, ActivePointerName);
                if (!File.Exists(pointer)) return null;

                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(pointer));
                string? name = doc.RootElement.TryGetProperty("folder", out JsonElement f) ? f.GetString() : null;
                if (string.IsNullOrEmpty(name) || !GeneticsLabManifest.IsSafeRelativePath(name) || name.Contains('/'))
                    return null;

                string folder = Path.Combine(_versionsDir, name);
                GeneticsLabManifest? manifest = ReadManifest(folder);
                if (manifest == null || !IsHostCompatible(manifest)) return null;

                // Cheap completeness check; hashes were verified when the folder was assembled.
                foreach (GeneticsLabManifestFile file in manifest.Files)
                {
                    if (!File.Exists(LocalPath(folder, file.Path))) return null;
                }

                return new GeneticsLabContent(folder, manifest.BuildId, manifest.ParsedVersion, IsBundled: false);
            }
            catch (Exception ex)
            {
                Log($"Ignoring downloaded copy: {ex.Message}");
                return null;
            }
        }

        private static GeneticsLabManifest? ReadManifest(string folder)
        {
            try
            {
                string path = Path.Combine(folder, GeneticsLabManifest.FileName);
                if (!File.Exists(path)) return null;
                return GeneticsLabManifest.TryParse(File.ReadAllText(path), out _);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsHostCompatible(GeneticsLabManifest manifest)
        {
            Version? required = GeneticsLabManifest.ParseVersion(manifest.MinDesktopVersion);
            return required == null || HostVersion >= required;
        }

        // ── Checking for updates ────────────────────────────────────────────────────────────────

        /// <summary>Starts a throttled update check without waiting for it.</summary>
        /// <summary>
        /// Checks again every 30 minutes for as long as the app runs. Safe to call more than
        /// once; only the first call starts the timer.
        /// </summary>
        public void StartPeriodicChecks()
        {
            if (_periodicTimer != null) return;
            _periodicTimer = new Timer(
                _ => CheckForUpdatesInBackground(),
                null,
                PeriodicCheckInterval,
                PeriodicCheckInterval);
        }

        /// <summary>
        /// Checks for a newer build without blocking the caller. Unless <paramref name="force"/>
        /// is set, a check within 15 minutes of the last one is skipped.
        /// </summary>
        public void CheckForUpdatesInBackground(TimeSpan delay = default, bool force = false)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (delay > TimeSpan.Zero) await Task.Delay(delay).ConfigureAwait(false);
                    await CheckForUpdatesAsync(force, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log($"Update check failed: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Installs the hosted build if it differs from the one in use. Returns the new content when
        /// an update was installed, otherwise null.
        /// </summary>
        public async Task<GeneticsLabContent?> CheckForUpdatesAsync(bool force, CancellationToken cancellationToken)
        {
            if (!await _checkGate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return null;
            try
            {
                if (!force && DateTime.UtcNow - _lastCheckUtc < MinCheckInterval) return null;
                _lastCheckUtc = DateTime.UtcNow;

                using var http = new HttpClient(new TrafficTrackingHttpMessageHandler("Genetics Lab"))
                {
                    Timeout = TimeSpan.FromMinutes(5)
                };

                // GitHub first, the hosted site only when GitHub is unreachable or its files fail
                // to download. A source that answers "nothing to do" ends the check.
                foreach (UpdateSource source in _sources)
                {
                    (GeneticsLabManifest remote, string remoteJson)? fetched;
                    try
                    {
                        fetched = await FetchRemoteManifestAsync(http, source.ManifestUri, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        Log($"{source.Name}: manifest unavailable ({ex.Message}).");
                        continue;
                    }
                    if (fetched == null) continue;
                    (GeneticsLabManifest remote, string remoteJson) = fetched.Value;

                    if (!IsHostCompatible(remote))
                    {
                        Log($"Genetics Lab {remote.Version} needs desktop {remote.MinDesktopVersion}; keeping the current copy.");
                        return null;
                    }

                    GeneticsLabContent? bundled = LoadBundled();
                    GeneticsLabContent? active = LoadActiveDownload();
                    GeneticsLabContent? current = PickBest(bundled, active);

                    if (current?.BuildId == remote.BuildId) return null;
                    if (current?.Version != null && remote.ParsedVersion < current.Version)
                    {
                        Log($"{source.Name} has Genetics Lab {remote.Version}, older than {current.Version}; not downgrading.");
                        return null;
                    }

                    try
                    {
                        SetStatus(new GeneticsLabUpdateStatus("downloading", remote.Version, remote.Changelog));
                        string folder = await InstallAsync(http, remote, remoteJson, source.FileBase(remote), new[] { active, bundled }, cancellationToken).ConfigureAwait(false);
                        SetActiveFolder(Path.GetFileName(folder));
                        PruneOldVersions(keep: folder);

                        var installed = new GeneticsLabContent(folder, remote.BuildId, remote.ParsedVersion, IsBundled: false);
                        SetStatus(new GeneticsLabUpdateStatus("ready", remote.Version, remote.Changelog));
                        return installed;
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        Log($"{source.Name}: installing Genetics Lab {remote.Version} failed ({ex.Message}).");
                        SetStatus(null);
                    }
                }

                return null;
            }
            finally
            {
                _checkGate.Release();
            }
        }

        private static async Task<(GeneticsLabManifest, string)?> FetchRemoteManifestAsync(HttpClient http, Uri manifestUri, CancellationToken cancellationToken)
        {
            // The query defeats any CDN cache sitting in front of the source.
            var uri = new Uri($"{manifestUri.GetLeftPart(UriPartial.Path)}?t={DateTime.UtcNow.Ticks}");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));

            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log($"No update manifest at {uri.GetLeftPart(UriPartial.Path)} ({(int)response.StatusCode}).");
                return null;
            }

            if (response.Content.Headers.ContentLength > MaxManifestBytes) return null;
            byte[] body = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
            if (body.Length > MaxManifestBytes) return null;

            string json = Encoding.UTF8.GetString(body);
            GeneticsLabManifest? manifest = GeneticsLabManifest.TryParse(json, out string? error);
            if (manifest == null)
            {
                Log($"Rejected update manifest: {error}");
                return null;
            }

            return (manifest, json);
        }

        // ── Installing a build ──────────────────────────────────────────────────────────────────

        private async Task<string> InstallAsync(
            HttpClient http,
            GeneticsLabManifest remote,
            string remoteJson,
            Uri fileBase,
            IEnumerable<GeneticsLabContent?> localCopies,
            CancellationToken cancellationToken)
        {
            string finalFolder = Path.Combine(_versionsDir, remote.BuildId[..16]);
            if (ReadManifest(finalFolder)?.BuildId == remote.BuildId &&
                remote.Files.All(f => File.Exists(LocalPath(finalFolder, f.Path))))
            {
                return finalFolder;
            }

            Dictionary<string, string> reusable = IndexLocalFiles(localCopies);
            string staging = finalFolder + StagingMarker + Guid.NewGuid().ToString("N")[..8];
            Directory.CreateDirectory(staging);

            try
            {
                long downloadedBytes = 0;
                int downloadedFiles = 0;
                var toDownload = new List<GeneticsLabManifestFile>();

                foreach (GeneticsLabManifestFile file in remote.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string target = LocalPath(staging, file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                    if (reusable.TryGetValue(file.Sha256, out string? source) && TryReuse(source, target, file))
                        continue;

                    toDownload.Add(file);
                }

                await Parallel.ForEachAsync(
                    toDownload,
                    new ParallelOptions { MaxDegreeOfParallelism = MaxParallelDownloads, CancellationToken = cancellationToken },
                    async (file, ct) =>
                    {
                        await DownloadVerifiedAsync(http, fileBase, file, LocalPath(staging, file.Path), ct).ConfigureAwait(false);
                        Interlocked.Add(ref downloadedBytes, file.Size);
                        Interlocked.Increment(ref downloadedFiles);
                    }).ConfigureAwait(false);

                // The manifest goes in last: a folder without one is never treated as installed.
                await File.WriteAllTextAsync(Path.Combine(staging, GeneticsLabManifest.FileName), remoteJson, cancellationToken).ConfigureAwait(false);

                if (Directory.Exists(finalFolder) && !_foldersInUse.ContainsKey(finalFolder))
                    Directory.Delete(finalFolder, recursive: true);
                Directory.Move(staging, finalFolder);

                Log($"Installed Genetics Lab {remote.Version} (build {remote.BuildId[..12]}): " +
                    $"downloaded {downloadedFiles} of {remote.Files.Count} files ({downloadedBytes / 1024.0:F0} KB), reused the rest.");
                return finalFolder;
            }
            catch
            {
                TryDeleteDirectory(staging);
                throw;
            }
        }

        /// <summary>Maps SHA-256 to a local file that has it, across the copies already on disk.</summary>
        private static Dictionary<string, string> IndexLocalFiles(IEnumerable<GeneticsLabContent?> copies)
        {
            var index = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (GeneticsLabContent? copy in copies)
            {
                if (copy == null) continue;
                GeneticsLabManifest? manifest = ReadManifest(copy.Folder);
                if (manifest == null) continue;

                foreach (GeneticsLabManifestFile file in manifest.Files)
                {
                    string path = LocalPath(copy.Folder, file.Path);
                    if (!index.ContainsKey(file.Sha256) && File.Exists(path)) index[file.Sha256] = path;
                }
            }
            return index;
        }

        /// <summary>
        /// Places an existing local file at <paramref name="target"/>. The source is re-hashed first:
        /// its manifest says what it should contain, not what it contains now.
        /// </summary>
        private static bool TryReuse(string source, string target, GeneticsLabManifestFile file)
        {
            try
            {
                if (new FileInfo(source).Length != file.Size || HashFile(source) != file.Sha256) return false;

                // Files are never modified in place, only replaced, so sharing one copy between the
                // bundled build and any number of downloaded ones is safe and costs no disk space.
                if (!CreateHardLink(target, source, IntPtr.Zero))
                    File.Copy(source, target, overwrite: true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task DownloadVerifiedAsync(HttpClient http, Uri fileBase, GeneticsLabManifestFile file, string target, CancellationToken cancellationToken)
        {
            // The hash in the query gives each file version its own URL, so a CDN cannot answer
            // with an older copy of an unhashed name such as index.html or the OCR worker.
            string escaped = string.Join('/', file.Path.Split('/').Select(Uri.EscapeDataString));
            var uri = new Uri(fileBase, $"{escaped}?v={file.Sha256[..16]}");

            using HttpResponseMessage response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long written = 0;
            await using (Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                byte[] buffer = new byte[81920];
                int read;
                while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    written += read;
                    if (written > file.Size) throw new InvalidDataException($"{file.Path} is larger than its manifest entry.");
                    sha.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            string actual = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
            if (written != file.Size || actual != file.Sha256)
                throw new InvalidDataException($"{file.Path} does not match its manifest hash (deploy in progress?).");
        }

        private void SetActiveFolder(string folderName)
        {
            Directory.CreateDirectory(_rootDir);
            string pointer = Path.Combine(_rootDir, ActivePointerName);
            string temp = pointer + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new { folder = folderName }));
            File.Move(temp, pointer, overwrite: true);
        }

        private void PruneOldVersions(string keep)
        {
            try
            {
                foreach (string dir in Directory.EnumerateDirectories(_versionsDir))
                {
                    if (string.Equals(dir, keep, StringComparison.OrdinalIgnoreCase)) continue;
                    if (_foldersInUse.ContainsKey(dir)) continue;
                    TryDeleteDirectory(dir);
                }
            }
            catch (Exception ex)
            {
                Log($"Pruning old builds failed: {ex.Message}");
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────────

        private static string LocalPath(string folder, string manifestPath)
        {
            string root = Path.GetFullPath(folder);
            string full = Path.GetFullPath(Path.Combine(root, manifestPath.Replace('/', Path.DirectorySeparatorChar)));
            // Already guaranteed by manifest validation; kept so a bad path can never escape.
            if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Path escapes the install folder: {manifestPath}");
            return full;
        }

        private static string HashFile(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private static Uri EnsureTrailingSlash(Uri uri) =>
            uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");

        private static void TryDeleteDirectory(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }

        private static void Log(string message) => Debug.WriteLine($"[GeneticsLab] {message}");

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
    }
}
