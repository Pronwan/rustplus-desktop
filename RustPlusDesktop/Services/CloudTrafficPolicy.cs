using System;

namespace RustPlusDesk.Services;

internal static class CloudTrafficPolicy
{
    private static volatile bool _isMinimized;

    internal static bool IsMinimized
    {
        get => _isMinimized;
        set => _isMinimized = value;
    }

    internal static TimeSpan TeamHeartbeatInterval(bool minimized) =>
        TimeSpan.FromSeconds(minimized ? 120 : 60);

    internal static TimeSpan ProfileTouchInterval(bool minimized) =>
        TimeSpan.FromMinutes(minimized ? 30 : 15);

    internal static TimeSpan PresenceInterval(bool minimized) =>
        TimeSpan.FromMinutes(minimized ? 10 : 5);

    internal static bool IsUpgradeBlockedVersion(string? minimumVersion, string? blockedVersion, string currentVersion)
    {
        if (TryParseVersion(minimumVersion, out var minimum) && TryParseVersion(currentVersion, out var current))
            return current < minimum;

        return string.Equals(blockedVersion, currentVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        var normalized = value?.Trim().TrimStart('v', 'V').Split('-', 2)[0];
        return Version.TryParse(normalized, out version!);
    }
}
