using System;
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
        new PlayerWipeTrackerCapabilityService());

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

    private async Task DisposePlayerWipeTrackerAsync()
    {
        await _playerWipeTracker.DisposeAsync().ConfigureAwait(false);
    }

    private void ObservePlayerWipeTracker(RustPlusClientReal.TeamInfo team)
    {
        if (!Services.TrackingService.PlayerWipeTrackerEnabled || _playerWipeTracker.CurrentSessionId is null)
            return;

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
