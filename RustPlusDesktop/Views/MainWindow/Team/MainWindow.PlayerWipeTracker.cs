using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RustPlusDesk.Services;
using RustPlusDesk.Services.PlayerWipeTracker;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private readonly PlayerWipeTrackerService _playerWipeTracker = new(
        new PlayerWipeTrackerStore(Path.Combine(RustPlusDesk.Services.Data.DataManager.AppDir, "player-wipes")),
        new PlayerWipeTrackerCapabilityService())
    {
        // Upload failures used to be swallowed whole. They repeat every minute when they happen,
        // so they belong in the console where someone will see them.
        Log = message => System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (System.Windows.Application.Current?.MainWindow is MainWindow window)
                window.AppendLog(message);
        }),

        ArchivesPruned = pruned => System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (System.Windows.Application.Current?.MainWindow is MainWindow window)
                window.ReportPrunedWipeArchives(pruned);
        }),

        ArchiveLimitNearlyReached = usage => System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (System.Windows.Application.Current?.MainWindow is MainWindow window)
                window.WarnWipeArchiveLimitNear(usage);
        }),
    };

    /// <summary>
    /// Warns while there is still a free slot, so the choice of which wipe to drop can be made
    /// deliberately rather than by the rule that fires when the next wipe arrives.
    /// </summary>
    internal void WarnWipeArchiveLimitNear(Services.PlayerWipeTracker.CloudArchiveUsage usage)
    {
        ShowInfoSnackbar(
            RustPlusDesk.Helpers.Loc.TextOrNull("WipeLimitNearTitle") ?? "One wipe backup slot left",
            string.Format(
                RustPlusDesk.Helpers.Loc.TextOrNull("WipeLimitNearMessage")
                    ?? "{0} of {1} wipe backups are in use. When the next wipe starts, the oldest is deleted automatically — pick a different one under Wipe Tracker · Cloud restore if you would rather keep it.",
                usage.Used, usage.Limit),
            Wpf.Ui.Controls.ControlAppearance.Info);
    }

    /// <summary>
    /// Tells the user which stored wipes were removed to make room for the current one.
    ///
    /// Named rather than counted: "an old backup was deleted" invites the question this answers,
    /// and the wipe date is the only thing that makes it recognisable.
    /// </summary>
    internal void ReportPrunedWipeArchives(IReadOnlyList<Services.PlayerWipeTracker.CloudPrunedArchive> pruned)
    {
        if (pruned.Count == 0) return;

        var names = pruned.Select(item =>
        {
            var server = string.IsNullOrWhiteSpace(item.ServerName)
                ? RustPlusDesk.Helpers.Loc.TextOrNull("WipePrunedUnknownServer") ?? "Unknown server"
                : item.ServerName!;
            return item.WipeStartedAtUtc is { } started
                ? $"{server} ({started.ToLocalTime():d})"
                : server;
        });

        ShowInfoSnackbar(
            RustPlusDesk.Helpers.Loc.TextOrNull("WipePrunedTitle") ?? "Oldest wipe backup removed",
            string.Format(
                RustPlusDesk.Helpers.Loc.TextOrNull("WipePrunedMessage")
                    ?? "Your plan keeps a limited number of wipe backups. To store the new wipe, the oldest was deleted: {0}",
                string.Join(", ", names)),
            Wpf.Ui.Controls.ControlAppearance.Caution);
    }

    // Wipe-map uploading is decoupled from the player wipe tracker: this owns the
    // network upload of the base map + monuments and the 3D-parsed extra monuments.
    private readonly ServerWipeMapService _serverWipeMaps = new();

    private void StartPlayerWipeTrackerSession()
    {
        var profile = _vm?.Selected;
        var serverKey = GetServerKey();
        if (profile is null || string.IsNullOrWhiteSpace(serverKey))
            return;

        _playerWipeTracker.Enabled = Services.TrackingService.PlayerWipeTrackerEnabled;
        _playerWipeTracker.CloudBackupEnabled = Services.TrackingService.PlayerWipeTrackerCloudBackupEnabled;
        _playerWipeTracker.StartConnection(
            serverKey,
            (profile.WipeTime ?? profile.RustMapsWipeTime)?.ToUniversalTime(),
            profile.RustMapsMapId,
            _mySteamId,
            serverName: profile.Name ?? serverKey);
        SaveCurrentPlayerWipeMap();
    }

    private void StopPlayerWipeTrackerSession()
    {
        try { _playerWipeTracker.Disconnect(); } catch { }
    }

    /// <summary>
    /// Re-syncs the tracker with the current settings after the user flips the
    /// setting mid-session. The session identity (server/wipe/session id and the
    /// local Steam id) is otherwise only captured at connect time, so enabling the
    /// tracker after connecting would leave it with no live session — or a session
    /// whose own-Steam-id was still unresolved (0) at connect, which silently drops
    /// every observation on the free plan. In those cases we rebuild the session.
    /// </summary>
    internal void RefreshPlayerWipeTrackerSession()
    {
        _playerWipeTracker.Enabled = Services.TrackingService.PlayerWipeTrackerEnabled;
        _playerWipeTracker.CloudBackupEnabled = Services.TrackingService.PlayerWipeTrackerCloudBackupEnabled;

        if (!Services.TrackingService.PlayerWipeTrackerEnabled)
            return;

        // Only (re)establish while actually connected — otherwise the next connect
        // will start the session as usual.
        var connected = _vm?.Servers.Any(s => s.IsConnected || s.IsFullConnected) == true;
        if (!connected)
            return;

        if (_playerWipeTracker.CurrentSessionId is null ||
            (_playerWipeTracker.CurrentOwnSteamId == 0 && _mySteamId != 0))
        {
            StartPlayerWipeTrackerSession();
        }
    }

    private async Task DisposePlayerWipeTrackerAsync()
    {
        await _playerWipeTracker.DisposeAsync().ConfigureAwait(false);
    }

    private void ObservePlayerWipeTracker(RustPlusClientReal.TeamInfo team)
    {
        if (!Services.TrackingService.PlayerWipeTrackerEnabled || _playerWipeTracker.CurrentSessionId is null)
            return;

        // Self-heal the connect-time race: if the local Steam id wasn't resolved
        // when the session started (own-id captured as 0), every observation is
        // silently dropped on the free plan. Re-snapshot once the id is known.
        if (_playerWipeTracker.CurrentOwnSteamId == 0 && _mySteamId != 0)
        {
            StartPlayerWipeTrackerSession();
            if (_playerWipeTracker.CurrentSessionId is null)
                return;
        }

        _playerWipeTracker.Enabled = true;
        _playerWipeTracker.CloudBackupEnabled = Services.TrackingService.PlayerWipeTrackerCloudBackupEnabled;

        var classifier = new Services.Deaths.DeathLocationClassifier(
            BuildMonumentZones(), (x, y) => ResolveBaseAt(team, x, y), (x, y) => GetGridLabel(x, y));
        var timestamp = DateTime.UtcNow;
        foreach (var member in team.Members)
        {
            if (member.SteamId == 0)
                continue;

            var vm = TeamMembers.FirstOrDefault(item => item.SteamId == member.SteamId);
            var location = member.X.HasValue && member.Y.HasValue
                ? classifier.Classify(member.X, member.Y)
                : ("unknown", (string?)null);
            _playerWipeTracker.Observe(new PlayerObservation(
                member.SteamId,
                member.Name ?? vm?.Name ?? "(player)",
                timestamp,
                _playerWipeTracker.CurrentSessionId,
                true,
                true,
                member.Online,
                member.Dead,
                vm?.IsAfk ?? false,
                member.X,
                member.Y,
                location.Item1 switch
                {
                    "monument" => TrackerLocationType.Monument,
                    "base" => TrackerLocationType.Base,
                    "open" => TrackerLocationType.Open,
                    _ => TrackerLocationType.Unknown,
                },
                location.Item2,
                member.X.HasValue && member.Y.HasValue ? classifier.Grid(member.X, member.Y) : null,
                member.SpawnTime,
                member.DeathTime));
        }
    }

    private async Task RefreshPlayerWipeTrackerCapabilitiesAsync()
    {
        try
        {
            var hasToken = !string.IsNullOrWhiteSpace(RustPlusDesk.Services.Cloud.CloudAuthManager.CurrentToken);
            if (!hasToken)
            {
                _playerWipeTracker.ResetCapabilities();
                _ = Dispatcher.BeginInvoke(() => PlayerWipeTrackerControl?.Refresh());
                AppendLog("[Player Wipe Tracker] Not signed in to the cloud backend, so premium tracker features cannot be unlocked. Sign in from the Cloud account window.");
                return;
            }

            var client = new PlayerWipeTrackerCloudClient();
            using var response = await client.GetBootstrapAsync().ConfigureAwait(false);
            if (response is null)
            {
                _ = Dispatcher.BeginInvoke(() => PlayerWipeTrackerControl?.Refresh());
                AppendLog("[Player Wipe Tracker] Capability sync failed: the cloud backend rejected the request. Premium features stay locked until the next successful sync.");
                return;
            }

            var caps = _playerWipeTracker.UpdateCapabilities(response.RootElement);
            _ = Dispatcher.BeginInvoke(() => PlayerWipeTrackerControl?.Refresh());
            AppendLog($"[Player Wipe Tracker] Plan '{caps.PlanCode}' · tracker {(caps.IsTrackerAvailable ? "on" : "off")}, team {(caps.CanTrackTeam ? "on" : "off")}, advanced views {(caps.CanUseAdvancedViews ? "on" : "off")}, cloud sync {(caps.CanUseCloudSync ? "on" : "off")}.");
        }
        catch (Exception ex)
        {
            // Capability outages must not affect Rust+ polling.
            AppendLog($"[Player Wipe Tracker] Capability refresh error: {ex.Message}");
        }
    }

    // Prepares the current wipe map context and hands it to the embedded workspace. Called when
    // the Player Wipe Tracker tab becomes active (mirrors the Raid Calculator overlay flow).
    private void OpenPlayerWipeTrackerWorkspace()
    {
        try
        {
            // Re-pull entitlements so a user who upgraded (or just signed in) sees premium
            // tabs unlock without reconnecting; the view re-reads capabilities as it refreshes.
            _ = RefreshPlayerWipeTrackerCapabilitiesAsync();
            SaveCurrentPlayerWipeMap();
            var storedMap = _playerWipeTracker.LoadCurrentWipeMap();
            var mapImage = ImgMap.Source ?? DecodeMap(storedMap?.PngBytes);
            var worldSize = _worldSizeS > 0 ? _worldSizeS : storedMap?.WorldSize ?? 0;
            var worldRect = _worldRectPx.Width > 0 && _worldRectPx.Height > 0
                ? _worldRectPx
                : storedMap is null
                    ? Rect.Empty
                    : new Rect(storedMap.WorldRectX, storedMap.WorldRectY, storedMap.WorldRectWidth, storedMap.WorldRectHeight);
            PlayerWipeTrackerControl.Initialize(
                _playerWipeTracker,
                _mySteamId,
                mapImage,
                worldSize,
                worldRect,
                storedMap?.Monuments);
        }
        catch { }
    }

    private void SaveCurrentPlayerWipeMap()
    {
        if (ImgMap.Source is not BitmapSource bitmap ||
            _worldSizeS <= 0 || _worldRectPx.Width <= 0 || _worldRectPx.Height <= 0)
            return;

        var serverKey = _playerWipeTracker.CurrentServerKey ?? GetServerKey();
        var wipeKey = _playerWipeTracker.CurrentWipeKey;

        if (string.IsNullOrWhiteSpace(wipeKey) && _vm?.Selected != null && !string.IsNullOrWhiteSpace(serverKey))
        {
            var wipeTime = (_vm.Selected.WipeTime ?? _vm.Selected.RustMapsWipeTime)?.ToUniversalTime();
            wipeKey = PlayerWipeTrackerService.BuildWipeKey(serverKey, wipeTime, _vm.Selected.RustMapsMapId);
        }

        if (string.IsNullOrWhiteSpace(serverKey) || string.IsNullOrWhiteSpace(wipeKey))
            return;

        try
        {
            using var stream = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
            var monuments = _monData
                .Where(m => !string.IsNullOrWhiteSpace(m.Name))
                .Select(m => new TrackerMonument(m.Name, m.X, m.Y))
                .ToList();

            var mapData = new TrackerWipeMap(
                stream.ToArray(),
                _worldSizeS,
                _worldRectPx.X,
                _worldRectPx.Y,
                _worldRectPx.Width,
                _worldRectPx.Height,
                GetCurrentMapPaddingWorld() / 2.0,
                monuments);

            // Persist locally (feeds the tracker preview), then upload via the
            // dedicated, decoupled service (base map + monuments).
            _playerWipeTracker.SaveWipeMap(serverKey, wipeKey, mapData, _vm?.Selected?.WipeTime);
            _serverWipeMaps.QueueUploadWipeMap(serverKey, wipeKey, mapData, _vm?.Selected?.WipeTime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppendLog($"[Server Wipe Map] Could not process wipe map: {ex.Message}");
        }
    }

    private static BitmapImage? DecodeMap(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 })
            return null;
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
