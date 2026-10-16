using System.Globalization;
using System.Text.Json.Nodes;
using RustPlusApi;
using RustPlusApi.Fcm.Data;
using RustPlusDesk.Core.Devices;
using RustPlusDesk.Core.Servers;
using RustPlusDesk.Infrastructure.Connections;
using RustPlusDesk.Infrastructure.Storage;

namespace RustPlusDesk.Infrastructure.Profiles;

internal static class LegacyPairingMatcher
{
    public static async Task<ServerSummary?> FindAsync(string path, Body body,
        Func<Body, IReadOnlyList<uint>, CancellationToken, Task<bool>> verify, CancellationToken ct)
    {
        byte[]? bytes = await AtomicPrivateFile.ReadAsync(path, ct).ConfigureAwait(false);
        if (bytes is null) return null;
        var servers = LegacyServerCatalog.Parse(bytes);
        var nodes = JsonNode.Parse(bytes)!.AsArray();
        string steam = body.PlayerId.ToString(CultureInfo.InvariantCulture);
        var account = servers.Select((server, index) => (Server: server, Node: nodes[index]!.AsObject()))
            .Where(pair => pair.Node["SteamId64"]?.GetValue<string>() == steam).ToArray();
        bool SameHost(ServerSummary server) => string.Equals(server.Host, body.Ip.Trim(), StringComparison.OrdinalIgnoreCase);
        bool HasId(JsonObject node, out Guid id) => Guid.TryParse(node["ServerId"]?.GetValue<string>(), out id) && id != Guid.Empty;
        if (account.Any(pair => (body.Id != Guid.Empty && HasId(pair.Node, out var id) && id == body.Id)
            || (SameHost(pair.Server) && pair.Server.Port == body.Port))) return null;
        var candidates = account.Where(pair =>
            (body.Id == Guid.Empty || !HasId(pair.Node, out var id) || id == body.Id)
            && (SameHost(pair.Server) || (!string.IsNullOrWhiteSpace(body.Name) && string.Equals(pair.Server.Name, body.Name, StringComparison.OrdinalIgnoreCase))))
            .Select(pair => pair.Server).ToArray();
        foreach (var candidate in candidates)
        {
            uint[] devices = candidate.Devices.SelectMany(device => device.DescendantsAndSelf())
                .Where(device => device.Kind != DeviceKind.Group && device.EntityId > 0).Select(device => device.EntityId).Distinct().Take(5).ToArray();
            if (devices.Length > 0 && await verify(body, devices, ct).ConfigureAwait(false)) return candidate;
        }
        // Preserve the reference app's single-host fallback; multiple candidates need positive device evidence.
        if (candidates.Length == 1 && SameHost(candidates[0]))
        {
            var candidate = candidates[0];
            bool sameName = !string.IsNullOrWhiteSpace(body.Name) &&
                (candidate.Name.Contains(body.Name, StringComparison.OrdinalIgnoreCase) || body.Name.Contains(candidate.Name, StringComparison.OrdinalIgnoreCase));
            bool hasDevices = candidate.Devices.SelectMany(device => device.DescendantsAndSelf()).Any(device => device.Kind != DeviceKind.Group && device.EntityId > 0);
            if (sameName || !hasDevices || body.EntityId.HasValue) return candidate;
        }
        return null;
    }
}

internal static class PairingConnectionVerifier
{
    public static Task<bool> VerifyAsync(Body body, IReadOnlyList<uint> devices, CancellationToken ct)
        => VerifyAsync(body, devices, connection => new RustPlusTransport(connection), ct);

    internal static async Task<bool> VerifyAsync(Body body, IReadOnlyList<uint> devices,
        Func<RustPlusConnection, IRustTransport> create, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        // The direct address must be verified: a companion socket ignores the new host and port.
        await using var transport = create(new(body.Ip.Trim(), body.Port, body.PlayerId,
            int.Parse(body.PlayerToken, NumberStyles.Integer, CultureInfo.InvariantCulture)));
        try
        {
            await transport.InitializeAsync(timeout.Token).ConfigureAwait(false);
            foreach (uint id in devices.Take(5))
            {
                try
                {
                    // This performs a generic entity-info query for switches, alarms and storage monitors.
                    if (!(await transport.Devices.ReadAsync(new(id, "", DeviceKind.Switch), timeout.Token).ConfigureAwait(false)).IsMissing) return true;
                }
                catch (DeviceOperationException) { }
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested) { return false; }
        ct.ThrowIfCancellationRequested();
        return false;
    }
}
