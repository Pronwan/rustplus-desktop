using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading.Tasks;
using RustPlusDesk.Services.Auth;

namespace RustPlusDesk.Services.Cloud
{
    /// <summary>
    /// Reports what the Rust server said about itself so the cloud record is more
    /// than a bare key.
    ///
    /// Sent once per server per session: the details only change on a wipe, and a
    /// reconnect re-reports anyway. The address is deliberately not sent — the
    /// server derives it from the server key, which is the value the data is
    /// already filed under, so a client cannot file data under one address while
    /// claiming another.
    /// </summary>
    public static class CloudServerInfo
    {
        private static readonly ConcurrentDictionary<string, byte> Reported = new();
        private static readonly ConcurrentDictionary<string, byte> Paired = new();

        /// <summary>
        /// Report the connected server's details. Failures are swallowed and the
        /// server is left unmarked so the next connection retries: this is
        /// descriptive metadata and must never disturb connecting.
        /// </summary>
        public static async Task ReportOnceAsync(string serverKey, string? name, int mapSize, DateTime? wipeAt)
        {
            if (!CloudBackend.UsePlatform) return;
            if (string.IsNullOrWhiteSpace(serverKey)) return;
            if (!CloudAuthManager.IsAuthenticated) return;
            if (!Reported.TryAdd(serverKey, 0)) return;

            try
            {
                await CloudApiClient.CallApiAsync("me/servers/info", HttpMethod.Post, payload: new
                {
                    server_key = serverKey,
                    name = string.IsNullOrWhiteSpace(name) ? null : name,
                    map_size = mapSize > 0 ? mapSize : (int?)null,
                    wipe_at = wipeAt?.ToUniversalTime().ToString("O"),
                });
            }
            catch (Exception ex)
            {
                Reported.TryRemove(serverKey, out _);
                SupabaseAuthManager.AppendLog($"[Cloud/Debug] Server info not reported: {ex.Message}");
            }
        }

        /// <summary>
        /// Register the connected server as a per-user pairing (with its encrypted
        /// player token) so it shows up in the web dashboard and its smart devices can
        /// be controlled from the cloud. Previously this only happened when linking
        /// Alexa, so servers paired after the cloud migration never became controllable.
        ///
        /// Idempotent and sent once per server per session; a token is required (there
        /// is nothing to control without one). Failures are swallowed and the server is
        /// left unmarked so the next connection retries.
        /// </summary>
        public static async Task EnsurePairedOnceAsync(string serverKey, string? host, int port, string? name, string? playerToken, ulong steamId)
        {
            if (!CloudBackend.UsePlatform) return;
            if (string.IsNullOrWhiteSpace(serverKey)) return;
            if (string.IsNullOrWhiteSpace(host)) return;
            if (!CloudAuthManager.IsAuthenticated) return;
            if (string.IsNullOrWhiteSpace(playerToken)) return;
            if (steamId == 0) return;
            if (!Paired.TryAdd(serverKey, 0)) return;

            try
            {
                await CloudAlexaAdapter.PairServerAsync(steamId.ToString(), host, port, name, playerToken);
            }
            catch (Exception ex)
            {
                Paired.TryRemove(serverKey, out _);
                SupabaseAuthManager.AppendLog($"[Cloud/Debug] Server pairing not registered: {ex.Message}");
            }
        }

        /// <summary>Forget what has been reported, e.g. after signing out.</summary>
        public static void Reset()
        {
            Reported.Clear();
            Paired.Clear();
        }
    }
}
