using System;

namespace RustPlusDesk.Services.Cloud
{
    /// <summary>Which cloud backend the client talks to.</summary>
    public enum CloudBackendMode
    {
        /// <summary>Legacy third-party backend, retained as the rollback path.</summary>
        Supabase,

        /// <summary>Self-hosted cloud platform (REST + token auth + realtime).</summary>
        Platform,
    }

    /// <summary>
    /// Single source of truth for the active cloud backend during the cutover to the
    /// self-hosted platform. Kept free of WPF/service dependencies so the routing
    /// logic is unit-testable in isolation.
    /// </summary>
    public static class CloudBackend
    {
        /// <summary>The backend this build targets. Flip to <c>Supabase</c> to roll back.</summary>
        public static CloudBackendMode Mode { get; } = CloudBackendMode.Platform;

        /// <summary>True when the client should route cloud traffic to the Cloud API.</summary>
        public static bool UsePlatform => Mode == CloudBackendMode.Platform;

        /// <summary>
        /// Translate a legacy Supabase Edge Function name (as passed to
        /// <c>SupabaseAuthManager.CallEdgeFunctionAsync</c>) into the equivalent platform
        /// <c>/api/v1</c> route path. Returns <c>null</c> when the operation has no direct
        /// 1:1 route (e.g. <c>user-profile/claim</c>, which the platform resolves
        /// server-side) — callers handle those cases explicitly in later slices.
        /// </summary>
        public static string? MapEdgeFunctionToRoute(string edgeFunction, string? httpMethod = null)
        {
            if (string.IsNullOrWhiteSpace(edgeFunction))
                return null;

            var name = edgeFunction.Trim().Trim('/');

            // The legacy /overlay contract is preserved server-side for GET/POST, but
            // deleting a server's sync data lives on the versioned sync route.
            if (name == "overlay")
                return string.Equals(httpMethod, "DELETE", StringComparison.OrdinalIgnoreCase) ? "sync/overlay" : "overlay";

            return name switch
            {
                "overlay/purge-orphaned" => "overlay/purge-orphaned",
                "user-profile/limits" => "me/limits",
                "user-profile/presence" => "profile/presence",
                "user-profile/consent" => "profile/consent",
                "user-profile/touch" => "profile/touch",
                "user-profile" => "profile",
                "discord-roles" => "me/discord/sync-roles",
                // A teammate's overlay/devices (team-membership authorized), used by
                // the device-import flow. The plain "overlay" GET is self-scoped.
                "team-member-overlay" => "sync/team-member-overlay",
                // The whole team's devices, de-duplicated server-side, for import.
                "team-devices" => "sync/team-devices",
                "team-feature/heartbeat" => "team-feature/heartbeat",
                "team-feature/master" => "team-feature/master",
                "team-feature/has-master" => "team-feature/has-master",
                // The desktop admin panel is steam-id keyed; the dashboard's
                // entitlement routes are UUID keyed, so these get their own surface.
                "admin/users" => "admin/desktop-users",
                "admin/set-supporter" => "admin/desktop-users/supporter",
                "server-events" => "server-events",
                "server-events/report" => "server-events/report",
                _ => null,
            };
        }

        /// <summary>Absolute URL for an <c>/api/v1</c> route path.</summary>
        public static string ApiUrl(string cloudBaseUrl, string routePath) =>
            $"{cloudBaseUrl.TrimEnd('/')}/api/v1/{routePath.TrimStart('/')}";
    }
}
