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

        private readonly UpdateManager _updateManager;
        private UpdateInfo? _pendingUpdateInfo;

        public UpdateService()
        {
            _updateManager = new UpdateManager(
                new GithubSource($"https://github.com/{RepoOwner}/{RepoName}", accessToken: null, prerelease: false));

            if (_updateManager.UpdatePendingRestart != null)
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
            catch (NotInstalledException)
            {
                return null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<string?> DownloadInstallerAsync(string url, IProgress<DownloadReport>? progress = null)
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
            catch (NotInstalledException)
            {
                return null;
            }
            catch
            {
                return null;
            }
        }

        public void StartInstaller(string installerPath)
        {
            var pending = _updateManager.UpdatePendingRestart ?? _pendingUpdateInfo?.TargetFullRelease;
            if (pending == null) return;

            _updateManager.ApplyUpdatesAndRestart(pending);
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
    }
}
