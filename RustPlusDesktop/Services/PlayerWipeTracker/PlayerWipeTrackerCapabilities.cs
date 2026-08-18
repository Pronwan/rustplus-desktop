using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using RustPlusDesk.Services.Data;

namespace RustPlusDesk.Services.PlayerWipeTracker;

public sealed record PlayerWipeTrackerCapabilities(
    string PlanCode,
    bool IsTrackerAvailable,
    bool CanTrackTeam,
    bool CanUseCloudSync,
    bool CanUseAdvancedViews,
    bool CanUseRouteReplay,
    bool CanExport,
    int MaxTrackedPlayers,
    int RetainedWipes,
    int CloudRetentionDays,
    DateTime FetchedUtc)
{
    public static PlayerWipeTrackerCapabilities Free(DateTime? fetchedUtc = null) => new(
        "free", true, false, false, false, false, false, 1, 1, 0, fetchedUtc ?? DateTime.UtcNow);

    public static PlayerWipeTrackerCapabilities Development(DateTime? fetchedUtc = null) => new(
        "development", true, true, true, true, true, true, 32, 12, 365, fetchedUtc ?? DateTime.UtcNow);

    public bool CanTrackPlayer(ulong steamId, ulong ownSteamId)
        => steamId != 0 && (steamId == ownSteamId || CanTrackTeam) &&
           (steamId == ownSteamId || MaxTrackedPlayers > 1);
}

/// <summary>Fail-closed interpreter and 72-hour offline cache for backend limits.</summary>
public sealed class PlayerWipeTrackerCapabilityService
{
    // TEMPORARY DEV OVERRIDE — the backend plan-limit rows for `player_wipe_tracker` are not
    // merged to main yet, so production `client/bootstrap` reports the feature off and this
    // service fails closed to the free tier. While set to true, every build (Debug *and*
    // Release) reports the full premium capability set so the tracker can be developed.
    // REVERT to false before shipping / once the backend seeder is merged.
    public static bool ForceDevelopmentCapabilities = true;

    private const string CacheKey = "player_wipe_tracker_capabilities";
    private static readonly TimeSpan OfflineGrace = TimeSpan.FromHours(72);
    private PlayerWipeTrackerCapabilities _lastSuccessful = PlayerWipeTrackerCapabilities.Free();

    public PlayerWipeTrackerCapabilityService()
    {
        _lastSuccessful = DataManager.LoadCache<PlayerWipeTrackerCapabilities>(CacheKey) ?? PlayerWipeTrackerCapabilities.Free();
    }

    public PlayerWipeTrackerCapabilities Current
    {
        get
        {
            if (ForceDevelopmentCapabilities)
                return PlayerWipeTrackerCapabilities.Development();
#if DEBUG
            var development = PlayerWipeTrackerCapabilities.Development();
            System.Diagnostics.Debug.Assert(
                development.IsTrackerAvailable && development.CanTrackTeam && development.CanUseCloudSync &&
                development.CanUseAdvancedViews && development.CanUseRouteReplay && development.CanExport);
            return development;
#else
            return Effective(DateTime.UtcNow);
#endif
        }
    }

    public PlayerWipeTrackerCapabilities Update(JsonElement bootstrap)
    {
        try
        {
            var data = bootstrap.TryGetProperty("data", out var wrapped) ? wrapped : bootstrap;
            var plan = data.TryGetProperty("plan_code", out var planValue) && planValue.ValueKind == JsonValueKind.String
                ? planValue.GetString() ?? "free" : "free";
            var limits = data.TryGetProperty("limits", out var limitValue) && limitValue.ValueKind == JsonValueKind.Object
                ? limitValue : default;

            bool Enabled(string key) => TryEnabled(limits, key);
            int Value(string key, int fallback) => TryValue(limits, key, fallback);

            _lastSuccessful = new PlayerWipeTrackerCapabilities(
                string.IsNullOrWhiteSpace(plan) ? "free" : plan,
                Enabled("access"),
                Enabled("team_tracking"),
                Enabled("cloud_sync"),
                Enabled("advanced_views"),
                Enabled("route_replay"),
                Enabled("export"),
                Math.Max(1, Value("max_tracked_players", 1)),
                Math.Max(1, Value("retained_wipes", 1)),
                Math.Max(0, Value("cloud_retention_days", 0)),
                DateTime.UtcNow);
            DataManager.SaveCache(CacheKey, _lastSuccessful);
        }
        catch
        {
            // Preserve the last known good answer; a malformed response cannot grant access.
        }

        return Current;
    }

    public PlayerWipeTrackerCapabilities Effective(DateTime nowUtc)
    {
        if (_lastSuccessful.PlanCode.Equals("free", StringComparison.OrdinalIgnoreCase))
            return _lastSuccessful;
        return nowUtc - _lastSuccessful.FetchedUtc <= OfflineGrace
            ? _lastSuccessful
            : PlayerWipeTrackerCapabilities.Free(_lastSuccessful.FetchedUtc);
    }

    private static bool TryEnabled(JsonElement limits, string key)
    {
        if (limits.ValueKind != JsonValueKind.Object || !limits.TryGetProperty("player_wipe_tracker", out var feature) ||
            feature.ValueKind != JsonValueKind.Object || !feature.TryGetProperty(key, out var value) ||
            value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("enabled", out var enabled) ||
            enabled.ValueKind != JsonValueKind.True && enabled.ValueKind != JsonValueKind.False)
            return false;
        return enabled.GetBoolean();
    }

    private static int TryValue(JsonElement limits, string key, int fallback)
    {
        if (limits.ValueKind != JsonValueKind.Object || !limits.TryGetProperty("player_wipe_tracker", out var feature) ||
            feature.ValueKind != JsonValueKind.Object || !feature.TryGetProperty(key, out var value) ||
            value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("value", out var number) ||
            number.ValueKind != JsonValueKind.Number || !number.TryGetInt32(out var result))
            return fallback;
        return result;
    }
}
