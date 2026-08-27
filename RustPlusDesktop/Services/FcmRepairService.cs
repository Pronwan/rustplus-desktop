using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RustPlusApi.Fcm.Registration;
using RustPlusApi.Fcm.Registration.Steps;

namespace RustPlusDesk.Services
{
    /// <summary>
    /// Re-registers the push token without a browser and without a Steam login.
    ///
    /// When Google invalidates a push registration, the socket keeps connecting and the stored
    /// expiry date keeps looking healthy, but nothing is delivered any more. Until now the only
    /// way out was Reset + Re-pair, which deletes the config and drags the user back through the
    /// Steam login window — the single biggest source of "I can't pair servers" reports.
    ///
    /// Almost none of that is actually necessary. The Steam login exists to obtain the Rust+ auth
    /// token, and that token is already on disk and stays valid far longer than the push
    /// registration does. What has gone stale is only the FCM/Expo side, and re-acquiring that is
    /// pure HTTP: <see cref="FcmRegistration.AcquireCredentialsAsync"/> performs the GCM check-in,
    /// the FCM register and the Expo token exchange without opening anything, and
    /// <see cref="RustCompanionClient"/> then points Facepunch at the new token.
    ///
    /// Only when Facepunch rejects the stored auth token is the real re-pairing unavoidable.
    /// </summary>
    public static class FcmRepairService
    {
        /// <summary>
        /// Matches the device id the native registration path registers under, so Facepunch
        /// replaces that entry instead of accumulating a second one for the same account.
        /// </summary>
        private const string DeviceId = "RustPlusApi";

        public enum RepairOutcome
        {
            /// <summary>New push credentials are registered and written to the config.</summary>
            Repaired,

            /// <summary>The stored Rust+ auth token is gone or refused — only a full re-pair helps.</summary>
            NeedsFullRePair,

            /// <summary>Something transient got in the way. Worth trying again later.</summary>
            Failed,
        }

        public sealed record RepairReport(RepairOutcome Outcome, string Detail);

        /// <summary>
        /// Acquires a fresh push registration and hands it to Facepunch under the existing account.
        /// The caller is responsible for restarting the listener afterwards — the new credentials
        /// only take effect on the next connect.
        /// </summary>
        public static async Task<RepairReport> TryRepairAsync(Action<string> log, CancellationToken ct = default)
        {
            var configPath = NativeFcmListener.ConfigPath;

            var (authToken, steamId) = ReadStoredIdentity(configPath);
            if (string.IsNullOrWhiteSpace(authToken))
            {
                log("[fcm-repair] No stored Rust+ auth token — a full re-pair is required.");
                return new RepairReport(RepairOutcome.NeedsFullRePair, "no stored auth token");
            }

            try
            {
                log("[fcm-repair] Acquiring fresh push credentials …");
                var registration = new FcmRegistration();
                var credentials = await registration.AcquireCredentialsAsync(ct).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(credentials.ExpoPushToken))
                {
                    log("[fcm-repair] Credential exchange returned no Expo token.");
                    return new RepairReport(RepairOutcome.Failed, "no expo token returned");
                }

                log("[fcm-repair] Registering the new token with Rust+ …");
                var companion = new RustCompanionClient();
                await companion.RegisterAsync(authToken!, credentials.ExpoPushToken!, DeviceId, ct)
                    .ConfigureAwait(false);

                // Only persist once Facepunch has accepted the new token. Writing earlier would
                // trade a stale registration for one that is not registered at all.
                var issuedAt = DateTime.Now;
                var expiresAt = issuedAt.AddDays(15);
                NativeFcmRegistrationService.WriteNodeCompatibleConfig(configPath, credentials, authToken!);
                NativeFcmRegistrationService.StampConfigMetadata(configPath, issuedAt, expiresAt, steamId, log);

                TrackingService.FcmIssuedAt = issuedAt;
                TrackingService.FcmExpiresAt = expiresAt;

                log("[fcm-repair] ✔ Push registration renewed.");
                return new RepairReport(RepairOutcome.Repaired, "renewed");
            }
            catch (OperationCanceledException) { throw; }
            catch (System.Net.Http.HttpRequestException ex) when (IsAuthRejection(ex))
            {
                // The auth token itself is dead. This is the one case the user cannot be spared.
                log("[fcm-repair] Rust+ rejected the stored auth token — a full re-pair is required.");
                return new RepairReport(RepairOutcome.NeedsFullRePair, ex.Message);
            }
            catch (Exception ex)
            {
                log($"[fcm-repair] Repair failed: {ex.Message}");
                return new RepairReport(RepairOutcome.Failed, ex.Message);
            }
        }

        /// <summary>
        /// Distinguishes "your account link is gone" from "the network hiccupped". Only the former
        /// justifies sending the user through the Steam login again.
        /// </summary>
        private static bool IsAuthRejection(System.Net.Http.HttpRequestException ex) =>
            ex.StatusCode is System.Net.HttpStatusCode.Unauthorized
                          or System.Net.HttpStatusCode.Forbidden
                          or System.Net.HttpStatusCode.BadRequest;

        private static (string? authToken, string? steamId) ReadStoredIdentity(string configPath)
        {
            try
            {
                if (!File.Exists(configPath)) return (null, null);
                using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                var root = doc.RootElement;
                return (
                    root.TryGetProperty("rustplus_auth_token", out var a) ? a.GetString() : null,
                    root.TryGetProperty("steam_id", out var s) ? s.GetString() : null);
            }
            catch
            {
                return (null, null);
            }
        }
    }
}
