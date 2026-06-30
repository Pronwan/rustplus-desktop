using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using RustPlusDesk.Models;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace RustPlusDesk.Services
{
    public class UpdateService
    {
        private const string RepoOwner = "Pronwan";
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

        private async Task<string?> DownloadExeInstallerAsync(string url, IProgress<DownloadReport>? progress = null)
        {
            var target = Path.Combine(Path.GetTempPath(), InstallerAssetName);
            try
            {
                using var http = new System.Net.Http.HttpClient();
                http.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("RustPlusDesk", VersionForCompare.ToString()));

                using var resp = await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();

                var total = resp.Content.Headers.ContentLength;
                using var input = await resp.Content.ReadAsStreamAsync();
                using var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[81920];
                long readTotal = 0;
                int read;
                var sw = Stopwatch.StartNew();
                var lastReport = sw.ElapsedMilliseconds;

                while ((read = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await file.WriteAsync(buffer, 0, read);
                    readTotal += read;

                    var now = sw.ElapsedMilliseconds;
                    if (now - lastReport > 200 || readTotal == total)
                    {
                        lastReport = now;
                        var report = new DownloadReport
                        {
                            Progress = total.HasValue ? (double)readTotal / total.Value : 0,
                            Percentage = total.HasValue ? $"{(double)readTotal / total.Value:P0}" : "0%",
                            BytesReceived = FormatBytes(readTotal),
                            TotalBytes = total.HasValue ? FormatBytes(total.Value) : "Unknown",
                            Speed = FormatBytes((long)(readTotal / (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 1))) + "/s"
                        };
                        progress?.Report(report);
                    }
                }
                PendingInstallerPath = target;
                return target;
            }
            catch
            {
                return null;
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

