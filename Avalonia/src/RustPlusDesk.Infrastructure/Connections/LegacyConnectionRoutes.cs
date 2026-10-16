using System.Globalization;
using System.Text.Json;
using RustPlusApi;
using RustPlusDesk.Core.Connections;
using RustPlusDesk.Infrastructure.Profiles;

namespace RustPlusDesk.Infrastructure.Connections;

internal sealed record ConnectionRoutes(IReadOnlyList<RustPlusConnection> Routes, bool CompanionLoginMissing);

internal static class LegacyConnectionRoutes
{
    public static async Task<ConnectionRoutes> ReadAsync(string profilesPath, string profileKey, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(profilesPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new ServerConnectionException(ConnectionFailure.ProfileUnavailable);

            foreach (var profile in document.RootElement.EnumerateArray())
            {
                if (profile.ValueKind != JsonValueKind.Object) continue;
                string? host = Text(profile, "Host");
                int port = profile.TryGetProperty("Port", out var portValue) && portValue.TryGetInt32(out var number) ? number : 28082;
                string? steamIdText = Text(profile, "SteamId64");
                if (string.IsNullOrWhiteSpace(host) || LegacyProfileIdentity.Create(host, port, steamIdText) != profileKey)
                    continue;

                if (port is < 1 or > 65535 || !ulong.TryParse(steamIdText, NumberStyles.Any, CultureInfo.InvariantCulture, out var steamId)
                    || steamId == 0 || !int.TryParse(Text(profile, "PlayerToken"), NumberStyles.Any, CultureInfo.InvariantCulture, out var playerToken))
                    throw new ServerConnectionException(ConnectionFailure.InvalidCredentials);

                var routes = new List<RustPlusConnection>();
                bool hasServerId = Guid.TryParse(Text(profile, "ServerId"), out var serverId);
                string? authToken = hasServerId ? await ReadAuthTokenAsync(profilesPath, ct).ConfigureAwait(false) : null;
                if (hasServerId && !string.IsNullOrWhiteSpace(authToken))
                    routes.Add(new RustPlusConnection(host, port, steamId, playerToken) { ServerId = serverId, AuthToken = authToken });
                routes.Add(new RustPlusConnection(host, port, steamId, playerToken));
                return new ConnectionRoutes(routes, hasServerId && string.IsNullOrWhiteSpace(authToken));
            }
            throw new ServerConnectionException(ConnectionFailure.ProfileUnavailable);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            throw new ServerConnectionException(ConnectionFailure.ProfileUnavailable);
        }
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static async Task<string?> ReadAuthTokenAsync(string profilesPath, CancellationToken ct)
    {
        string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(profilesPath))!, "rustplusjs-config.json");
        try
        {
            await using var stream = File.OpenRead(path);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            return document.RootElement.ValueKind == JsonValueKind.Object ? Text(document.RootElement, "rustplus_auth_token") : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
}
