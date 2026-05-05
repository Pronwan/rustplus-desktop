using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services;

/// <summary>
/// Shares the tracked-players directory + groups across teammates by piggy-backing on the
/// existing overlay-sync server (the one Devices Export/Import uses). Pull is on a timer
/// (driven by MainWindow); push is coalesced after local mutations.
/// </summary>
public static class TeamSyncService
{
    // ─── Server config (matches the existing device sync) ───────────────────
    private const string ServerBaseUrl = "http://85.214.193.250:5000";
    private const string SecretHex =
        "23c5a7dbf02b63543da043ca7d6de1fbf706a080c899e334a8cd599206e13fde";
    private const int MaxPayloadBytes = 350_000;
    private const string SyncKeySuffix = ":tracker:v1"; // appended to GetServerKey() to keep tracker
                                                       // blobs in their own server slot.

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    static TeamSyncService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "RustPlusDesk-Ryyott/1.0 (team-sync)");
    }

    // ─── Context (set by MainWindow during init) ────────────────────────────
    /// <summary>Returns the local user's SteamID64. 0 if unknown.</summary>
    public static Func<ulong>? GetMySteamId;
    /// <summary>Returns "host-port" for the currently-selected server, or empty if none.</summary>
    public static Func<string>? GetServerKey;
    /// <summary>Returns the live list of teammate SteamID64s (excluding self).</summary>
    public static Func<IReadOnlyList<ulong>>? GetTeammateSteamIds;

    // ─── Status (for the UI status line) ────────────────────────────────────
    public static DateTime? LastUploadAt { get; private set; }
    public static DateTime? LastPullAt { get; private set; }
    public static int LastPullChangeCount { get; private set; }
    public static event Action<string>? OnSyncStatus;

    private static void Status(string s) => OnSyncStatus?.Invoke(s);

    // ─── Coalesced upload trigger ───────────────────────────────────────────
    private static DispatcherTimer? _coalesceTimer;
    private static int _coalesceArmed;

    /// <summary>Call from any mutation handler. Multiple calls within ~2 s collapse into one upload.</summary>
    public static void NotifyLocalChanged()
    {
        if (!TrackingService.TeamSyncEnabled) return;

        // First call arms the timer; subsequent calls just mark dirty.
        Interlocked.Exchange(ref _coalesceArmed, 1);
        if (_coalesceTimer == null)
        {
            _coalesceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _coalesceTimer.Tick += async (_, __) =>
            {
                _coalesceTimer.Stop();
                if (Interlocked.Exchange(ref _coalesceArmed, 0) == 1)
                {
                    try { await UploadAsync(); }
                    catch (Exception ex) { Status($"upload failed: {ex.Message}"); }
                }
            };
        }
        _coalesceTimer.Stop();
        _coalesceTimer.Start();
    }

    // ─── Upload ─────────────────────────────────────────────────────────────
    public static async Task UploadAsync(CancellationToken ct = default)
    {
        if (!TrackingService.TeamSyncEnabled) return;
        var steamId = GetMySteamId?.Invoke() ?? 0;
        var serverKey = GetServerKey?.Invoke() ?? "";
        if (steamId == 0 || string.IsNullOrEmpty(serverKey) || serverKey == "unknown-server")
            return;

        Status("uploading…");
        var payload = BuildLocalSnapshot();
        var json = JsonSerializer.Serialize(payload);
        var rawBytes = Encoding.UTF8.GetBytes(json);
        if (rawBytes.Length > MaxPayloadBytes)
            throw new InvalidOperationException($"Tracker sync payload too big ({rawBytes.Length} bytes > {MaxPayloadBytes}).");

        var b64 = Convert.ToBase64String(rawBytes);
        var trackerKey = serverKey + SyncKeySuffix;
        var ts = UnixNow().ToString();
        var sig = HmacSha256Hex(SecretHex, $"{steamId}|{trackerKey}|{ts}|{b64}");

        var body = new
        {
            steamId = steamId.ToString(),
            serverKey = trackerKey,
            ts,
            overlayJsonB64 = b64,
            sig
        };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await _http.PostAsync(ServerBaseUrl + "/upload", content, ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"upload HTTP {(int)resp.StatusCode}");

        LastUploadAt = DateTime.UtcNow;
        Status($"uploaded {rawBytes.Length} B");
    }

    // ─── Pull + merge ───────────────────────────────────────────────────────
    public static async Task<int> PullAndMergeAsync(CancellationToken ct = default)
    {
        if (!TrackingService.TeamSyncEnabled) return 0;
        var mySteam = GetMySteamId?.Invoke() ?? 0;
        var serverKey = GetServerKey?.Invoke() ?? "";
        var teammates = GetTeammateSteamIds?.Invoke() ?? Array.Empty<ulong>();
        if (mySteam == 0 || string.IsNullOrEmpty(serverKey) || serverKey == "unknown-server")
            return 0;
        if (teammates.Count == 0) { LastPullAt = DateTime.UtcNow; return 0; }

        var trackerKey = serverKey + SyncKeySuffix;
        int totalChanges = 0;

        int teammatesWithData = 0;
        int teammatesEmpty = 0;
        int teammatesErrored = 0;

        foreach (var theirSteam in teammates.Where(id => id != 0 && id != mySteam))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var (snap, hadData) = await TryFetchSnapshotAsync(theirSteam, trackerKey, ct);
                if (!hadData) { teammatesEmpty++; continue; }
                if (snap == null) { teammatesEmpty++; continue; }
                teammatesWithData++;
                totalChanges += MergeSnapshotIntoLocal(snap);
            }
            catch (Exception ex)
            {
                teammatesErrored++;
                // Per-teammate failures are common (they may not have ticked Team sync yet, or
                // their slot is cold). Log only — don't promote to the persistent status line.
                System.Diagnostics.Debug.WriteLine($"[team-sync] pull from {theirSteam} failed: {ex.Message}");
            }
        }

        if (totalChanges > 0)
        {
            TrackingService.PersistAfterRemoteMerge();
            PlayerGroupsService.PersistAfterRemoteMerge();
        }

        LastPullAt = DateTime.UtcNow;
        LastPullChangeCount = totalChanges;
        // Friendly status: only mention errors if EVERY teammate failed (otherwise transient flake).
        if (totalChanges > 0)
            Status($"merged {totalChanges} change(s)");
        else if (teammatesWithData == 0 && teammatesErrored > 0 && teammatesEmpty == 0)
            Status("server unreachable — will retry");
        else
            Status("up to date");
        return totalChanges;
    }

    public static async Task ForceSyncAsync(CancellationToken ct = default)
    {
        if (!TrackingService.TeamSyncEnabled) return;
        try { await UploadAsync(ct); } catch (Exception ex) { Status($"upload failed: {ex.Message}"); }
        try { await PullAndMergeAsync(ct); } catch (Exception ex) { Status($"pull failed: {ex.Message}"); }
    }

    // ─── Snapshot (DTO that travels over the wire) ──────────────────────────
    private class SyncSnapshot
    {
        public int v { get; set; } = 1;
        public long uploadedAtUnix { get; set; }
        public List<TrackedDto> trackedPlayers { get; set; } = new();
        public List<PlayerGroup> groups { get; set; } = new();
        public TombstoneBag tombstones { get; set; } = new();
    }

    private class TrackedDto
    {
        public string BMId { get; set; } = "";
        public string Name { get; set; } = "";
        public string LastServerName { get; set; } = "";
        public long LastModifiedUnix { get; set; }
    }

    private class TombstoneBag
    {
        public List<TrackedTombstoneDto> trackedPlayerBMIds { get; set; } = new();
        public List<GroupTombstoneDto> groupIds { get; set; } = new();
    }

    private class TrackedTombstoneDto
    {
        public string BMId { get; set; } = "";
        public long DeletedAtUnix { get; set; }
    }

    private class GroupTombstoneDto
    {
        public string Id { get; set; } = "";
        public long DeletedAtUnix { get; set; }
    }

    private static SyncSnapshot BuildLocalSnapshot()
    {
        var snap = new SyncSnapshot { uploadedAtUnix = UnixNow() };

        // Tracked players (without per-user session histories).
        foreach (var p in TrackingService.GetTrackedPlayers())
        {
            snap.trackedPlayers.Add(new TrackedDto
            {
                BMId = p.BMId,
                Name = p.Name,
                LastServerName = p.LastServerName,
                LastModifiedUnix = p.LastModifiedUnix
            });
        }

        // Groups: send them whole (Name/Color/Notify/BMIds/MapPins/LastModifiedUnix).
        foreach (var g in PlayerGroupsService.Groups)
            snap.groups.Add(g);

        foreach (var t in TrackingService.TrackerTombstones)
            snap.tombstones.trackedPlayerBMIds.Add(new TrackedTombstoneDto
            { BMId = t.BMId, DeletedAtUnix = t.DeletedAtUnix });
        foreach (var t in PlayerGroupsService.Tombstones)
            snap.tombstones.groupIds.Add(new GroupTombstoneDto
            { Id = t.Id, DeletedAtUnix = t.DeletedAtUnix });

        return snap;
    }

    private static int MergeSnapshotIntoLocal(SyncSnapshot snap)
    {
        int changes = 0;

        // Apply tombstones first so we don't accept revived rows from the same payload.
        foreach (var t in snap.tombstones.trackedPlayerBMIds)
            if (TrackingService.ApplyRemoteTrackerTombstone(t.BMId, t.DeletedAtUnix)) changes++;
        foreach (var t in snap.tombstones.groupIds)
            if (PlayerGroupsService.ApplyRemoteGroupTombstone(t.Id, t.DeletedAtUnix)) changes++;

        foreach (var p in snap.trackedPlayers)
        {
            var asTracked = new TrackedPlayer
            {
                BMId = p.BMId,
                Name = p.Name,
                LastServerName = p.LastServerName,
                LastModifiedUnix = p.LastModifiedUnix
            };
            if (TrackingService.ApplyRemoteTrackedPlayer(asTracked)) changes++;
        }

        foreach (var g in snap.groups)
            if (PlayerGroupsService.ApplyRemoteGroup(g)) changes++;

        return changes;
    }

    // ─── Network helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Returns (snapshot, hadData). hadData=false means the server has nothing for that
    /// teammate yet (404 / empty) — that's a normal, expected state for new teammates and
    /// should NOT be reported as an error. Throws only on real network/parse failures.
    /// </summary>
    private static async Task<(SyncSnapshot? snap, bool hadData)> TryFetchSnapshotAsync(
        ulong theirSteam, string trackerKey, CancellationToken ct)
    {
        var ts = UnixNow().ToString();
        var sig = HmacSha256Hex(SecretHex, $"{theirSteam}|{trackerKey}|{ts}");
        var url = $"{ServerBaseUrl}/fetch" +
                  $"?steamId={theirSteam}" +
                  $"&serverKey={Uri.EscapeDataString(trackerKey)}" +
                  $"&ts={ts}" +
                  $"&sig={sig}";

        using var resp = await _http.GetAsync(url, ct);

        // 404 / 204 = "we have nothing for this teammate yet" — not an error.
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound ||
            resp.StatusCode == System.Net.HttpStatusCode.NoContent)
            return (null, false);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}");

        var json = await resp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json)) return (null, false);

        using var doc = JsonDocument.Parse(json);
        string? b64 = null;
        if (doc.RootElement.TryGetProperty("overlayJsonB64", out var b1) && b1.ValueKind == JsonValueKind.String)
            b64 = b1.GetString();
        else if (doc.RootElement.TryGetProperty("data", out var dat) &&
                 dat.TryGetProperty("overlayJsonB64", out var b2) && b2.ValueKind == JsonValueKind.String)
            b64 = b2.GetString();
        if (string.IsNullOrEmpty(b64)) return (null, false);

        try
        {
            var rawBytes = Convert.FromBase64String(b64);
            var rawJson = Encoding.UTF8.GetString(rawBytes);
            var snap = JsonSerializer.Deserialize<SyncSnapshot>(rawJson);
            return (snap, snap != null);
        }
        catch
        {
            // Parse failure is real — bubble so the caller logs it.
            throw new InvalidOperationException("malformed snapshot");
        }
    }

    private static string HmacSha256Hex(string hexKey, string data)
    {
        var keyBytes = HexToBytes(hexKey);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static byte[] HexToBytes(string hex)
    {
        if (hex.Length % 2 != 0) throw new ArgumentException("invalid hex length");
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < hex.Length; i += 2)
            bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
        return bytes;
    }

    private static long UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
