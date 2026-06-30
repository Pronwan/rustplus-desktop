using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Net.Http;
using RustPlusDesk.Models;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace RustPlusDesk.Services
{
    public class UpdateService
    {
        private const string RepoOwner = "JawadYzbk";
        private const string RepoName = "rustplus-desktop";
        private const string PendingVelopackUpdateMarker = "velopack-pending";
        private const string InstallerAssetName = "RustPlusDesk-Setup.exe";

        private readonly UpdateManager _updateManager;
        private UpdateInfo? _pendingUpdateInfo;
        private readonly bool _isVelopackSupported;

        public UpdateService()
        {
            _updateManager = new UpdateManager(
                new GithubSource($"https://github.com/{RepoOwner}/{RepoName}", accessToken: null, prerelease: false));

            try
            {
                _isVelopackSupported = _updateManager.IsInstalled;
            }
            catch
            {
                _isVelopackSupported = false;
            }

            if (_isVelopackSupported && _updateManager.UpdatePendingRestart != null)
            {
                PendingInstallerPath = PendingVelopackUpdateMarker;
            }
        }

        public static string LatestReleaseUrl => $"https://github.com/{RepoOwner}/{RepoName}/releases/latest";

        public string? PendingInstallerPath { get; set; }

        public string VersionRaw
        {
            get
            {
                var attr = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (attr != null && !string.IsNullOrWhiteSpace(attr.InformationalVersion))
                    return attr.InformationalVersion;

                var path = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(path))
                {
                    try
                    {
                        var fvi = FileVersionInfo.GetVersionInfo(path);
                        if (!string.IsNullOrWhiteSpace(fvi.ProductVersion))
                            return fvi.ProductVersion;
                    }
                    catch { }
                }

                return Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";
            }
        }

        public string VersionShort => NormalizeVer(VersionRaw);

        public Version VersionForCompare =>
            Version.TryParse(VersionShort, out var v) ? v : new Version(0, 0, 0);

        public async Task<(Version latest, string tag, string? downloadUrl)?> GetLatestReleaseAsync()
        {
            if (_isVelopackSupported)
            {
                try
                {
                    _pendingUpdateInfo = await _updateManager.CheckForUpdatesAsync();
                    if (_pendingUpdateInfo == null)
                    {
                        return (VersionForCompare, $"v{VersionShort}", null);
                    }

                    string version = _pendingUpdateInfo.TargetFullRelease.Version.ToString();
                    if (!Version.TryParse(NormalizeVer(version), out var latest))
                    {
                        return null;
                    }

                    return (latest, $"v{version}", PendingVelopackUpdateMarker);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Velopack CheckForUpdatesAsync failed: {ex.Message}. Falling back to GitHub Releases API.");
                }
            }

            return await GetLatestReleaseFromGitHubAsync();
        }

        private async Task<(Version latest, string tag, string? downloadUrl)?> GetLatestReleaseFromGitHubAsync()
        {
            try
            {
                using var http = new System.Net.Http.HttpClient();
                http.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("RustPlusDesk", VersionForCompare.ToString()));
                http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

                var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
                using var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    return null;
                }

                using var stream = await resp.Content.ReadAsStreamAsync();
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream);
                var root = doc.RootElement;

                var tag = root.GetProperty("tag_name").GetString() ?? "";
                var assets = root.GetProperty("assets").EnumerateArray();

                string? dl = null;
                foreach (var a in assets)
                {
                    var name = a.GetProperty("name").GetString() ?? "";
                    if (string.Equals(name, InstallerAssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        dl = a.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }

                var v = NormalizeVer(tag);
                if (!Version.TryParse(v, out var latest))
                {
                    return null;
                }
                return (latest, tag, dl);
            }
            catch
            {
                return null;
            }
        }

        public async Task<string?> DownloadInstallerAsync(string url, IProgress<DownloadReport>? progress = null)
        {
            if (string.Equals(url, PendingVelopackUpdateMarker, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var updateInfo = _pendingUpdateInfo ?? await _updateManager.CheckForUpdatesAsync();
                    if (updateInfo == null) return null;

                    await _updateManager.DownloadUpdatesAsync(updateInfo, percent =>
                    {
                        progress?.Report(new DownloadReport
                        {
                            Progress = percent / 100.0,
                            Percentage = $"{percent}%",
                            BytesReceived = "Downloaded",
                            TotalBytes = "Velopack package",
                            Speed = string.Empty
                        });
                    });

                    PendingInstallerPath = PendingVelopackUpdateMarker;
                    return PendingInstallerPath;
                }
                catch
                {
                    return null;
                }
            }

            return await DownloadExeInstallerAsync(url, progress);
        }

        private const int _downloadChunksCount = 4;
        private bool _isDownloadPaused = false;
        private CancellationTokenSource? _downloadCts = null;
        public string CurrentDownloadFile { get; private set; } = string.Empty;

        public bool IsDownloadPaused => _isDownloadPaused;

        public void PauseDownload()
        {
            _isDownloadPaused = true;
            _downloadCts?.Cancel();
        }

        public void ResumeDownload()
        {
            _isDownloadPaused = false;
        }

        public void CancelDownload()
        {
            _isDownloadPaused = false;
            _downloadCts?.Cancel();
            CleanupPartFiles();
        }

        public void CleanupPartFiles()
        {
            var target = Path.Combine(Path.GetTempPath(), InstallerAssetName);
            for (int i = 0; i < _downloadChunksCount; i++)
            {
                string partPath = $"{target}.part{i}";
                if (File.Exists(partPath))
                {
                    try { File.Delete(partPath); } catch { }
                }
            }
            if (File.Exists(target))
            {
                try { File.Delete(target); } catch { }
            }
        }

        private async Task<string?> DownloadExeInstallerAsync(string url, IProgress<DownloadReport>? progress = null)
        {
            var target = Path.Combine(Path.GetTempPath(), InstallerAssetName);
            CurrentDownloadFile = InstallerAssetName;
            _downloadCts = new CancellationTokenSource();
            var token = _downloadCts.Token;

            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("RustPlusDesk", VersionForCompare.ToString()));

                long totalBytes;
                using (var headResp = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), token))
                {
                    headResp.EnsureSuccessStatusCode();
                    totalBytes = headResp.Content.Headers.ContentLength ?? throw new Exception("Failed to get content length.");
                }

                int chunksCount = _downloadChunksCount;
                long chunkLength = totalBytes / chunksCount;

                var tasks = new Task[chunksCount];
                long[] downloadedBytes = new long[chunksCount];

                for (int i = 0; i < chunksCount; i++)
                {
                    string partPath = $"{target}.part{i}";
                    if (File.Exists(partPath))
                    {
                        downloadedBytes[i] = new FileInfo(partPath).Length;
                    }
                }

                var sw = Stopwatch.StartNew();
                long lastReportedBytes = downloadedBytes.Sum();
                var lastReportTime = sw.ElapsedMilliseconds;

                var progressTask = Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(250, token);
                        long currentTotal = downloadedBytes.Sum();
                        long nowTime = sw.ElapsedMilliseconds;
                        double seconds = (nowTime - lastReportTime) / 1000.0;
                        if (seconds <= 0) seconds = 0.25;

                        long speedBytes = (long)((currentTotal - lastReportedBytes) / seconds);
                        lastReportedBytes = currentTotal;
                        lastReportTime = nowTime;

                        progress?.Report(new DownloadReport
                        {
                            Progress = (double)currentTotal / totalBytes,
                            Percentage = $"{((double)currentTotal / totalBytes):P0}",
                            BytesReceived = FormatBytes(currentTotal),
                            TotalBytes = FormatBytes(totalBytes),
                            Speed = FormatBytes(speedBytes) + "/s"
                        });

                        if (currentTotal >= totalBytes) break;
                    }
                }, token);

                for (int i = 0; i < chunksCount; i++)
                {
                    int chunkIndex = i;
                    long start = chunkIndex * chunkLength;
                    long end = (chunkIndex == chunksCount - 1) ? totalBytes - 1 : (chunkIndex + 1) * chunkLength - 1;

                    tasks[chunkIndex] = DownloadChunkAsync(url, target, chunkIndex, start, end, downloadedBytes, token);
                }

                await Task.WhenAll(tasks);
                try { await progressTask; } catch { }

                using (var outputStream = File.Create(target))
                {
                    for (int i = 0; i < chunksCount; i++)
                    {
                        string partPath = $"{target}.part{i}";
                        using (var partStream = File.OpenRead(partPath))
                        {
                            await partStream.CopyToAsync(outputStream);
                        }
                        File.Delete(partPath);
                    }
                }

                PendingInstallerPath = target;
                return target;
            }
            catch (OperationCanceledException)
            {
                return _isDownloadPaused ? "PAUSED" : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Multi-part download failed: {ex.Message}");
                return null;
            }
        }

        private async Task DownloadChunkAsync(string url, string target, int chunkIndex, long start, long end, long[] downloadedBytes, CancellationToken token)
        {
            string partPath = $"{target}.part{chunkIndex}";
            long currentStart = start + downloadedBytes[chunkIndex];

            if (currentStart >= end)
            {
                return;
            }

            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("RustPlusDesk", VersionForCompare.ToString()));

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(currentStart, end);

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            using var responseStream = await response.Content.ReadAsStreamAsync(token);
            using var fileStream = new FileStream(partPath, FileMode.Append, FileAccess.Write, FileShare.None, 4096, true);

            var buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, token);
                downloadedBytes[chunkIndex] += bytesRead;
            }
        }

        public void StartInstaller(string installerPath)
        {
            if (string.Equals(installerPath, PendingVelopackUpdateMarker, StringComparison.OrdinalIgnoreCase))
            {
                var pending = _updateManager.UpdatePendingRestart ?? _pendingUpdateInfo?.TargetFullRelease;
                if (pending != null)
                {
                    _updateManager.ApplyUpdatesAndRestart(pending);
                    return;
                }
            }

            if (!string.IsNullOrEmpty(installerPath) && File.Exists(installerPath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
            }
        }

        private static string NormalizeVer(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "0.0.0";
            s = s.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s[1..];
            int dash = s.IndexOfAny(new[] { '-', '+' });
            if (dash > 0) s = s[..dash];
            return s;
        }

        private static string FormatBytes(long bytes)
        {
            string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dblSByte = bytes;
            for (i = 0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
            }
            return $"{dblSByte:0.##} {Suffix[i]}";
        }
    }
}

