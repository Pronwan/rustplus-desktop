using System.Security.Cryptography;
using System.Text;

namespace RustPlusDesk.Infrastructure.Profiles;

internal static class LegacyProfileIdentity
{
    // Match the legacy Host:Port|SteamId64 identity without exposing the account ID to presentation.
    public static string Create(string host, int port, string? steamId)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{host}:{port}|{steamId}")));
}
