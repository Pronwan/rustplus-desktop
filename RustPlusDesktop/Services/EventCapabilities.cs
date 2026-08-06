using System;
using System.Collections.Generic;
using System.Linq;

namespace RustPlusDesk.Services;

/// <summary>Every dynamic world event the app can talk about.</summary>
public enum RustEventKind
{
    Cargo,
    PatrolHeli,
    Chinook,
    TravellingVendor,
    DeepSea,
    OilRig,
}

/// <summary>Where a server's event information comes from.</summary>
public enum ServerEventSource
{
    /// <summary>Not decided yet — the first shop poll has not answered.</summary>
    Unknown,

    /// <summary>The server still sends vending and dynamic event markers over Rust+.</summary>
    RustApi,

    /// <summary>
    /// The server sends neither, so events come from audio detection shared through our
    /// backend. Fewer events, less precision, no positions.
    /// </summary>
    Cloud,
}

/// <summary>
/// The single source of truth for "which events exist right now".
///
/// Event Dock, chat alert menu, chat command menu, alert templates and the chat master all
/// need the same answer. Asking this class rather than each deciding for itself is what keeps
/// them from drifting apart — a command that answers for an event the dock no longer shows,
/// or an alert that fires for something nothing can detect, is exactly the failure mode this
/// exists to prevent.
/// </summary>
public static class EventCapabilities
{
    // Everything the Rust+ API used to carry. Oil Rig is absent on purpose: it was never
    // delivered directly, only inferred from Chinook movement.
    private static readonly RustEventKind[] ApiEvents =
    {
        RustEventKind.Cargo,
        RustEventKind.PatrolHeli,
        RustEventKind.Chinook,
        RustEventKind.TravellingVendor,
        RustEventKind.DeepSea,
    };

    // What audio detection can carry. Patrol Heli, Chinook and Travelling Vendor have no
    // server-wide cue, so they are genuinely gone rather than merely degraded — showing them
    // would promise something that can never arrive. Oil Rig is new: audio gives it directly
    // for the first time.
    private static readonly RustEventKind[] CloudEvents =
    {
        RustEventKind.Cargo,
        RustEventKind.DeepSea,
        RustEventKind.OilRig,
    };

    private static ServerEventSource _source = ServerEventSource.Unknown;

    /// <summary>Raised when the source changes. Consumers rebuild their view from here.</summary>
    public static event Action? Changed;

    public static ServerEventSource Source => _source;

    /// <summary>
    /// Unknown behaves like Cloud on purpose: the reduced view is the safe default. Promising
    /// events that never arrive is worse than briefly hiding ones that do, and the first shop
    /// poll corrects it within seconds.
    /// </summary>
    public static IReadOnlyList<RustEventKind> Trackable =>
        _source == ServerEventSource.RustApi ? ApiEvents : CloudEvents;

    public static bool IsTrackable(RustEventKind kind) => Trackable.Contains(kind);

    /// <summary>
    /// Docking warnings, harbour names and departure countdowns all need the ship's position,
    /// which only the API provides. Audio can say a cargo spawned and nothing more.
    /// </summary>
    public static bool SupportsCargoRouting => _source == ServerEventSource.RustApi;

    /// <summary>True while events come from other players' clients rather than the server.</summary>
    public static bool IsCloudSourced => _source != ServerEventSource.RustApi;

    public static void SetSource(ServerEventSource source)
    {
        if (_source == source) return;
        _source = source;
        Changed?.Invoke();
    }

    /// <summary>Maps a Rust+ dynamic marker type onto its event, or null if it is not one.</summary>
    public static RustEventKind? FromMarkerType(int markerType) => markerType switch
    {
        4 => RustEventKind.Chinook,
        5 => RustEventKind.Cargo,
        6 => RustEventKind.TravellingVendor,
        8 => RustEventKind.PatrolHeli,
        _ => null,
    };

    /// <summary>
    /// Event key as used by the backend and the audio listener. Only the three that audio can
    /// detect have one — the rest never cross that boundary.
    /// </summary>
    public static string? ToBackendKey(RustEventKind kind) => kind switch
    {
        RustEventKind.Cargo => "cargo",
        RustEventKind.DeepSea => "deep-sea",
        RustEventKind.OilRig => "oil-rig",
        _ => null,
    };

    /// <summary>
    /// Tutorials that walk the user through something the current server cannot offer. The
    /// shops chapter highlights the shop pill, the shop panel and the refresh button — all
    /// hidden without vending data — so it would point at nothing and force open a panel that
    /// is deliberately blocked.
    ///
    /// The events chapter deliberately stays: the dock still exists, it just carries fewer
    /// entries.
    /// </summary>
    private static readonly HashSet<string> CloudUnavailableTutorials =
        new(StringComparer.Ordinal) { "shops-vending" };

    public static bool IsTutorialAvailable(string tutorialId) =>
        !IsCloudSourced || !CloudUnavailableTutorials.Contains(tutorialId);

    /// <summary>
    /// Alert templates that can never be sent on a server without event markers. Editing them
    /// is not forbidden — a player may well be on another server tomorrow — but they are shown
    /// greyed so nobody spends time wording a message that will not fire.
    ///
    /// The crate-unlock timers belong here too: the fifteen-minute countdown came from the
    /// API, and audio only tells us a crate exists, never when it opens.
    /// </summary>
    private static readonly HashSet<string> CloudUnavailableAlerts = new(StringComparer.Ordinal)
    {
        "AlertNewShop", "AlertSuspiciousShop", "AlertShopMatch",
        "AlertCargoDocked", "AlertCargoExpectedDock", "AlertCargoDeparting",
        "AlertEventSpawned", "AlertHeliCrashFalseAlarm", "AlertHeliShotDown",
        "AlertCrateUnlocksIn10Min", "AlertCrateUnlocksIn5Min",
    };

    /// <summary>
    /// The crate countdowns came from the API and were dimmed with it. A Logic Engine rule
    /// driven by an RF receiver can start the same countdown by hand, so where one exists
    /// they are earned back — the timer is the player's own, and it is exact.
    /// </summary>
    private static readonly HashSet<string> OilRigTimerAlerts = new(StringComparer.Ordinal)
    {
        "AlertCrateUnlocksIn15Min", "AlertCrateUnlocksIn10Min", "AlertCrateUnlocksIn5Min",
    };

    private static bool _oilRigTimers;

    /// <summary>True while at least one enabled rule can start an oil rig hack timer.</summary>
    public static bool OilRigTimersConfigured => _oilRigTimers;

    public static void SetOilRigTimers(bool configured)
    {
        if (_oilRigTimers == configured) return;
        _oilRigTimers = configured;
        Changed?.Invoke();
    }

    public static bool IsAlertAvailable(string alertKey)
    {
        if (!IsCloudSourced) return true;
        if (_oilRigTimers && OilRigTimerAlerts.Contains(alertKey)) return true;
        return !CloudUnavailableAlerts.Contains(alertKey);
    }

    /// <summary>
    /// How long an event stays interesting once its cue has been heard.
    ///
    /// Mirrors the durations in report_server_event, and is only used when the backend has
    /// said nothing — a report it refused, or one that never left the machine. Normally the
    /// stored expiry wins, so the two agreeing matters less than it looks; keep them in step
    /// anyway, because a client that outlives the server's own row would show a stale event
    /// after everyone else has dropped it.
    /// </summary>
    public static TimeSpan NominalDuration(RustEventKind kind) => kind switch
    {
        RustEventKind.DeepSea => TimeSpan.FromHours(3),
        RustEventKind.Cargo => TimeSpan.FromMinutes(75),
        RustEventKind.OilRig => TimeSpan.FromMinutes(80),
        _ => TimeSpan.FromMinutes(30),
    };

    public static RustEventKind? FromBackendKey(string? key) => key switch
    {
        "cargo" => RustEventKind.Cargo,
        "deep-sea" => RustEventKind.DeepSea,
        "oil-rig" => RustEventKind.OilRig,
        _ => null,
    };
}
