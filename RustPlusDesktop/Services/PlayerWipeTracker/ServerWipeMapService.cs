using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using RustPlusDesk.Services.Cloud;
using RustPlusDesk.Services.Data;

namespace RustPlusDesk.Services.PlayerWipeTracker;

/// <summary>
/// Owns uploading server wipe maps + their extra monuments, decoupled from the
/// player wipe tracker. Local persistence still lives in the tracker store (for
/// previews); this service only talks to the cloud, checking existing state
/// first so it never re-spends quota. All operations are best-effort and silent.
/// </summary>
public sealed class ServerWipeMapService
{
    private readonly PlayerWipeTrackerStore _store;
    private readonly ServerWipeMapCloudClient _client = new();

    public ServerWipeMapService(PlayerWipeTrackerStore? store = null)
    {
        // Own store instance pointing at the shared wipe directory — the markers
        // and map files are plain files, so this coexists with the tracker's store.
        _store = store ?? new PlayerWipeTrackerStore(Path.Combine(DataManager.AppDir, "player-wipes"));
    }

    /// <summary>
    /// Upload the base wipe map (image + geometry + primary monuments) once, if
    /// the server doesn't already have it. Fire-and-forget.
    /// </summary>
    public void QueueUploadWipeMap(string serverKey, string wipeKey, TrackerWipeMap map, DateTime? wipeStartedAtUtc = null)
        => _ = TryUploadWipeMapAsync(serverKey, wipeKey, map, wipeStartedAtUtc);

    /// <summary>
    /// Upload the 3D-parsed extra monuments once the base map exists. Fire-and-forget.
    /// Safe to call after the base map has already been marked uploaded.
    /// </summary>
    public void QueueUploadExtraMonuments(string serverKey, string wipeKey, IReadOnlyList<TrackerMonument> extraMonuments)
        => _ = TryUploadExtraMonumentsAsync(serverKey, wipeKey, extraMonuments);

    private async Task TryUploadWipeMapAsync(string serverKey, string wipeKey, TrackerWipeMap map, DateTime? wipeStartedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(serverKey) || string.IsNullOrWhiteSpace(wipeKey))
            return;

        if (_store.IsWipeMapUploaded(serverKey, wipeKey))
            return;

        if (!CloudAuthManager.IsAuthenticated)
            return;

        try
        {
            // Ask the server what it already has before spending quota.
            var status = await _client.GetStatusAsync(serverKey, wipeKey).ConfigureAwait(false);
            if (status.HasImage)
            {
                _store.MarkWipeMapUploaded(serverKey, wipeKey);
                return;
            }

            var code = await _client.UploadWipeMapAsync(serverKey, wipeKey, map.PngBytes, map, wipeStartedAtUtc).ConfigureAwait(false);
            if (code is 200 or 201 or 409)
            {
                _store.MarkWipeMapUploaded(serverKey, wipeKey);
            }
        }
        catch
        {
            // Silent — a failed upload is retried on the next save.
        }
    }

    private async Task TryUploadExtraMonumentsAsync(string serverKey, string wipeKey, IReadOnlyList<TrackerMonument> extraMonuments)
    {
        if (string.IsNullOrWhiteSpace(serverKey) || string.IsNullOrWhiteSpace(wipeKey) || extraMonuments is not { Count: > 0 })
            return;

        if (_store.IsExtraMonumentsUploaded(serverKey, wipeKey))
            return;

        if (!CloudAuthManager.IsAuthenticated)
            return;

        try
        {
            var status = await _client.GetStatusAsync(serverKey, wipeKey).ConfigureAwait(false);

            // Extras require the base wipe map first; wait for the next attempt if missing.
            if (!status.Exists || !status.HasImage)
                return;

            if (status.HasExtraMonuments)
            {
                _store.MarkExtraMonumentsUploaded(serverKey, wipeKey);
                return;
            }

            var code = await _client.UploadExtraMonumentsAsync(serverKey, wipeKey, extraMonuments).ConfigureAwait(false);
            if (code is 200 or 201)
            {
                _store.MarkExtraMonumentsUploaded(serverKey, wipeKey);
            }
        }
        catch
        {
            // Silent — retried when the 3D map is (re)parsed.
        }
    }
}
