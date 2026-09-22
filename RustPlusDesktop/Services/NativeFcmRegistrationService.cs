using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Registration;

namespace RustPlusDesk.Services
{
    /// <summary>
    /// Native FCM and Rust+ registration using RustPlusApi.Fcm.Registration.
    /// </summary>
    public static class NativeFcmRegistrationService
    {
        /// <summary>
        /// Attempts a native registration and writes the config on success.
        /// </summary>
        /// <param name="configPath">Where to write the resulting rustplusjs-config.json.</param>
        /// <param name="log">Log sink.</param>
        /// <param name="browserPath">
        /// Optional explicit browser executable to drive for the Steam login (via CHROME_PATH).
        /// When null, the shared locator picks one (Chrome first). Callers that must force a
        /// specific browser (e.g. the "Listen with Edge" path) pass it here.
        /// </param>
        /// <param name="browserName">Human-readable name of <paramref name="browserPath"/>, for logging.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><see langword="true"/> if credentials were acquired and the config written.</returns>
        public static async Task<bool> TryRegisterAsync(
            string configPath,
            Action<string> log,
            string? browserPath = null,
            string? browserName = null,
            CancellationToken ct = default)
        {
            // CHROME_PATH overrides the library's browser discovery.
            var browser = browserPath ?? ChromiumBrowserLocator.Find(out browserName);
            if (browser == null)
            {
                log("[fcm-native] No Chromium-based browser found for the Steam login step.");
                return false;
            }

            log($"[fcm-native] Starting native registration using {browserName} for the login window.");

            var previousChromePath = Environment.GetEnvironmentVariable("CHROME_PATH");
            Environment.SetEnvironmentVariable("CHROME_PATH", browser);
            try
            {
                var steamLoginPort = GetFreeLoopbackPort();
                var registration = new FcmRegistration(steamLoginPort: steamLoginPort);

                log("[fcm-native] Acquiring FCM/GCM/Expo credentials …");
                Credentials credentials = await registration.AcquireCredentialsAsync(ct).ConfigureAwait(false);

                log("[fcm-native] Linking Steam account with Rust+ (confirm the login in the browser) …");
                // beta.8 hands the Steam login URL to the caller before opening a browser, so it
                // can be recovered when the browser never appears — which is most of what pairing
                // support has ever been about.
                // beta.8 changed two things here. The login URL is handed to the caller before a
                // browser is opened, so it can be recovered when none appears — which is most of
                // what pairing support has ever been about. And the call now returns the Steam
                // identity rather than a bare token, carrying the Steam64 ID alongside it.
                var login = await registration.RegisterWithRustPlusAsync(
                    credentials,
                    url => log($"[fcm-native] Steam login URL (open it by hand if no browser opened): {url}"),
                    ct).ConfigureAwait(false);

                string rustPlusAuthToken = login.Token;
                log($"[fcm-native] Signed in as {login.SteamId}.");

                WriteConfig(configPath, credentials, rustPlusAuthToken);
                log("[fcm-native] Native registration completed and config written.");
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log($"[fcm-native] Native registration failed ({ex.Message}).");
                return false;
            }
            finally
            {
                Environment.SetEnvironmentVariable("CHROME_PATH", previousChromePath);
            }
        }

        /// <summary>
        /// Serializes the native credentials. Metadata is added afterwards by the listener.
        /// </summary>
        internal static void WriteConfig(string configPath, Credentials credentials, string rustPlusAuthToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

            var config = new
            {
                fcm_credentials = new
                {
                    gcm = new
                    {
                        androidId = credentials.Gcm.AndroidId.ToString(CultureInfo.InvariantCulture),
                        securityToken = credentials.Gcm.SecurityToken.ToString(CultureInfo.InvariantCulture),
                    },
                    fcm = new
                    {
                        token = credentials.Fcm?.Token ?? string.Empty,
                    },
                },
                expo_push_token = credentials.ExpoPushToken ?? string.Empty,
                rustplus_auth_token = rustPlusAuthToken,
            };

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json);
        }

        /// <summary>
        /// Adds steam_id / issue_date / expiry_date to an existing config and pushes the result to
        /// the cloud. Split out from the listener so the repair path stamps its renewed credentials
        /// exactly the way a fresh registration does.
        /// </summary>
        internal static void StampConfigMetadata(
            string configPath, DateTime issuedAt, DateTime expiresAt, string? steamId, Action<string> log)
        {
            try
            {
                if (!File.Exists(configPath)) return;
                var json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                using var ms = new MemoryStream();
                using var wtr = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });
                wtr.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject()) prop.WriteTo(wtr);
                wtr.WriteString("steam_id", steamId ?? "");
                wtr.WriteString("issue_date", issuedAt.ToString("o"));
                wtr.WriteString("expiry_date", expiresAt.ToString("o"));
                wtr.WriteEndObject();
                wtr.Flush();
                File.WriteAllBytes(configPath, ms.ToArray());
                _ = FcmSyncService.SyncFcmCredentialsAsync();
            }
            catch (Exception ex)
            {
                log($"[fcm-native] could not enrich config: {ex.Message}");
            }
        }

        /// <summary>Binds a loopback TCP socket to port 0 to obtain a free port for the Steam login callback.</summary>
        private static int GetFreeLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
