using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using RustPlusDesk.Services.Auth;

namespace RustPlusDesk.Services.Cloud
{
    /// <summary>
    /// Audits and synchronizes user consent events with the cloud platform API (/api/v1/me/consents).
    /// </summary>
    public static class CloudConsentService
    {
        public const string TypeCloud247 = "cloud_247";
        public const string TypeOfflineIntegrations = "offline_integrations";
        public const string TypeFcmSync = "fcm_sync";
        public const string TypeSmartDevices = "smart_devices";
        public const string TypeDataUpload = "data_upload";
        public const string TypeSocialChat = "social_chat";
        public const string TypeSocialDm = "social_dm";
        public const string TypeSocialLfg = "social_lfg";
        public const string TypeTranslation = "translation";
        public const string TypeMap3D = "map_3d";
        public const string TypeWipeTrackerBackup = "wipe_tracker_backup";
        public const string TypeProfileSync = "profile_sync";

        /// <summary>
        /// Record a user consent action (granted or revoked) to the central platform audit log.
        /// </summary>
        public static async Task<bool> RecordConsentAsync(string consentType, bool granted, Dictionary<string, object>? metadata = null)
        {
            if (!CloudBackend.UsePlatform || !CloudAuthManager.IsAuthenticated)
            {
                return false;
            }

            try
            {
                metadata ??= new Dictionary<string, object>();
                metadata["client_version"] = Helpers.VersionHelper.GetClientVersion();
                metadata["recorded_at_utc"] = DateTime.UtcNow.ToString("o");

                var payload = new
                {
                    consent_type = consentType,
                    granted = granted,
                    metadata = metadata,
                };

                await CloudApiClient.CallApiAsync("me/consents", HttpMethod.Post, payload: payload);
                SupabaseAuthManager.AppendLog($"[Consent] Recorded '{consentType}' -> {(granted ? "GRANTED" : "REVOKED")}");
                return true;
            }
            catch (Exception ex)
            {
                SupabaseAuthManager.AppendLog($"[Consent] Failed to record consent '{consentType}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Backfill consents configured locally to ensure the central backend audit table matches.
        /// </summary>
        public static async Task BackfillLocalConsentsAsync()
        {
            if (!CloudBackend.UsePlatform || !CloudAuthManager.IsAuthenticated)
            {
                return;
            }

            try
            {
                if (CloudSessionsApi.GlobalConsentEnabled)
                {
                    await RecordConsentAsync(TypeCloud247, true, new Dictionary<string, object> { ["source"] = "local_backfill" });
                }

                if (TrackingService.OfflineIntegrationsConsented)
                {
                    await RecordConsentAsync(TypeOfflineIntegrations, true, new Dictionary<string, object> { ["source"] = "local_backfill" });
                    await RecordConsentAsync(TypeFcmSync, true, new Dictionary<string, object> { ["source"] = "local_backfill" });
                }

                if (TrackingService.CloudSyncEnabled || TrackingService.UploadConsentGiven)
                {
                    await RecordConsentAsync(TypeSmartDevices, true, new Dictionary<string, object> { ["source"] = "local_backfill" });
                    await RecordConsentAsync(TypeDataUpload, true, new Dictionary<string, object> { ["source"] = "local_backfill" });
                    await RecordConsentAsync(TypeProfileSync, true, new Dictionary<string, object> { ["source"] = "local_backfill" });
                }

                if (TrackingService.TranslationConsentGiven)
                {
                    await RecordConsentAsync(TypeTranslation, true, new Dictionary<string, object> { ["source"] = "local_backfill" });
                }

                if (TrackingService.PlayerWipeTrackerCloudBackupEnabled)
                {
                    await RecordConsentAsync(TypeWipeTrackerBackup, true, new Dictionary<string, object> { ["source"] = "local_backfill" });
                }

                if (Map3DConsentService.HasRememberedConsent())
                {
                    await RecordConsentAsync(TypeMap3D, true, new Dictionary<string, object> { ["source"] = "local_backfill" });
                }
            }
            catch (Exception ex)
            {
                SupabaseAuthManager.AppendLog($"[Consent] Local backfill warning: {ex.Message}");
            }
        }
    }
}
