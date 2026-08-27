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
    /// Native (Node-free) FCM + Rust+ registration using RustPlusApi.Fcm.Registration.
    ///
    /// Runs the full credential chain in-process (GCM check-in → Firebase → FCM → Expo →
    /// Steam login → Rust Companion) and writes the result into the same
    /// <c>rustplusjs-config.json</c> the Node <c>fcm-listen</c> reads, so listening is
    /// unchanged. This is the primary registration path; the Node <c>fcm-register</c> CLI
    /// remains as an automatic fallback when the native flow fails.
    ///
    /// The Node listener only consumes <c>fcm_credentials.gcm.androidId</c> and
    /// <c>securityToken</c> (see @liamcottle/rustplus.js cli), which the native
    /// <see cref="Credentials"/> supplies via <see cref="Gcm"/> — so the converted config
    /// is sufficient to drive it.
    /// </summary>
    public static class NativeFcmRegistrationService
    {
        /// <summary>
        /// Attempts a native registration, writing the Node-compatible config to
        /// <paramref name="configPath"/> on success.
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
            // Steam login drives Chrome/Chromium via the DevTools protocol, exactly like the
            // Node path drives it via Puppeteer. Point the library at whatever browser we can
            // find (CHROME_PATH overrides its own discovery), reusing the shared locator unless
            // the caller forced a specific browser.
            var browser = browserPath ?? ChromiumBrowserLocator.Find(out browserName);
            if (browser == null)
            {
                log("[fcm-native] No Chromium-based browser found for the Steam login step. "
                  + "Falling back to the Node registration path.");
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
                string rustPlusAuthToken = await registration.RegisterWithRustPlusAsync(credentials, ct).ConfigureAwait(false);

                WriteNodeCompatibleConfig(configPath, credentials, rustPlusAuthToken);
                log("[fcm-native] Native registration completed and config written.");
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log($"[fcm-native] Native registration failed ({ex.Message}). Falling back to the Node registration path.");
                return false;
            }
            finally
            {
                Environment.SetEnvironmentVariable("CHROME_PATH", previousChromePath);
            }
        }

        /// <summary>
        /// Serializes the native <see cref="Credentials"/> into the @liamcottle/rustplus.js
        /// config shape. androidId/securityToken are written as strings to match what the Node
        /// <c>PushReceiverClient</c> is given by the original <c>fcm-register</c>. steam_id and
        /// issue/expiry dates are added afterwards by the caller (EnrichFcmConfig).
        /// </summary>
        internal static void WriteNodeCompatibleConfig(string configPath, Credentials credentials, string rustPlusAuthToken)
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
