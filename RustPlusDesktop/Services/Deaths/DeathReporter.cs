using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RustPlusDesk.Services.Auth;
using RustPlusDesk.Services.Cloud;

namespace RustPlusDesk.Services.Deaths
{
    /// <summary>
    /// Persists a detected death. Every account keeps a local JSON-lines log
    /// (the free tier); premium accounts on the platform backend also push it to
    /// the shared team death log. Cloud failures are swallowed — the local record
    /// is the source of truth and a later sync/backfill can reconcile.
    /// </summary>
    public static class DeathReporter
    {
        public static async Task ReportAsync(DeathRecord death, string serverKey)
        {
            AppendLocal(death, serverKey);

            if (!CloudBackend.UsePlatform || !SupabaseAuthManager.IsPremium)
                return;

            try
            {
                await CloudApiClient.TryCallApiAsync("sync/deaths", HttpMethod.Post, payload: new
                {
                    server_key = serverKey,
                    victim_steam_id = death.SteamId.ToString(CultureInfo.InvariantCulture),
                    victim_name = death.Name,
                    pos_x = death.X,
                    pos_y = death.Y,
                    grid = death.Grid,
                    location_type = death.LocationType,
                    location_name = death.LocationName,
                    died_at = ToIso(death.DeathTime),
                    spawn_at = death.SpawnTime.HasValue ? ToIso(death.SpawnTime.Value) : null,
                });
            }
            catch
            {
                // Best-effort: the local log already has it.
            }
        }

        /// <summary>Flush the team's cloud death log for a server (called on wipe). Premium-only.</summary>
        public static async Task ClearCloudAsync(string? serverKey)
        {
            if (string.IsNullOrEmpty(serverKey) || !CloudBackend.UsePlatform || !SupabaseAuthManager.IsPremium)
                return;

            try
            {
                await CloudApiClient.TryCallApiAsync(
                    "sync/deaths?server_key=" + Uri.EscapeDataString(serverKey),
                    HttpMethod.Delete);
            }
            catch
            {
                // Best-effort: the local log is already cleared.
            }
        }

        private static readonly object _migrationLock = new();
        private static bool _migrated;

        /// <summary>Directory holding the per-server local death logs.</summary>
        public static string LogDirectory
        {
            get
            {
                EnsureMigrated();
                return TargetLogDirectory;
            }
        }

        public static string TargetLogDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RustPlusDesk", "deaths");

        public static string LegacyLogDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RustPlusDesktop", "deaths");

        public static void EnsureMigrated()
        {
            if (_migrated) return;
            lock (_migrationLock)
            {
                if (_migrated) return;
                try
                {
                    MigrateLegacyDeaths();
                }
                catch
                {
                    // Migration is best-effort and must never crash.
                }
                finally
                {
                    _migrated = true;
                }
            }
        }

        private static void MigrateLegacyDeaths()
        {
            var legacyDir = LegacyLogDirectory;
            if (!Directory.Exists(legacyDir))
                return;

            var targetDir = TargetLogDirectory;
            Directory.CreateDirectory(targetDir);

            var files = Directory.GetFiles(legacyDir, "*.jsonl");
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var destPath = Path.Combine(targetDir, fileName);

                if (!File.Exists(destPath))
                {
                    File.Move(file, destPath);
                }
                else
                {
                    // Merge if file already exists in target
                    var legacyLines = File.ReadAllLines(file, Encoding.UTF8);
                    if (legacyLines.Length > 0)
                    {
                        var targetLines = new System.Collections.Generic.HashSet<string>(File.ReadAllLines(destPath, Encoding.UTF8));
                        using var writer = File.AppendText(destPath);
                        foreach (var line in legacyLines)
                        {
                            if (!string.IsNullOrWhiteSpace(line) && targetLines.Add(line))
                            {
                                writer.WriteLine(line);
                            }
                        }
                    }
                    File.Delete(file);
                }
            }

            try
            {
                if (Directory.GetFileSystemEntries(legacyDir).Length == 0)
                {
                    Directory.Delete(legacyDir, false);
                }

                var legacyParent = Path.GetDirectoryName(legacyDir);
                if (!string.IsNullOrEmpty(legacyParent) && Directory.Exists(legacyParent))
                {
                    if (Directory.GetFileSystemEntries(legacyParent).Length == 0)
                    {
                        Directory.Delete(legacyParent, false);
                    }
                }
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        /// <summary>Local JSON-lines death log for a server (shared with the reader).</summary>
        public static string LogPathFor(string serverKey) =>
            Path.Combine(LogDirectory, SafeFileName(serverKey) + ".jsonl");

        private static string ToIso(long unixSeconds) =>
            DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime.ToString("o", CultureInfo.InvariantCulture);

        private static void AppendLocal(DeathRecord death, string serverKey)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);

                var file = LogPathFor(serverKey);
                var line = JsonSerializer.Serialize(new
                {
                    steam_id = death.SteamId.ToString(CultureInfo.InvariantCulture),
                    name = death.Name,
                    died_at = death.DeathTime,
                    spawn_at = death.SpawnTime,
                    x = death.X,
                    y = death.Y,
                    grid = death.Grid,
                    location_type = death.LocationType,
                    location_name = death.LocationName,
                });

                File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // A local write failure must never break the team-info refresh.
            }
        }

        private static string SafeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? "server" : cleaned;
        }
    }
}
