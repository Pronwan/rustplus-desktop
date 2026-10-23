using System.Text.Json;

namespace RustPlusDesk.Infrastructure.Accounts;

internal sealed record CompanionToken(string SteamId = "", DateTimeOffset? ExpiresAt = null)
{
    public static CompanionToken ReadClaims(string token)
    {
        try
        {
            string[] parts = token.Split('.');
            // Facepunch signed tokens have two segments; standard JWTs have three.
            string payload = parts.Length == 2 ? parts[0] : parts.Length == 3 ? parts[1] : "";
            payload = payload.Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            var root = document.RootElement;
            string id = root.TryGetProperty("steamId", out var steamId) ? steamId.ToString() : "";
            if (!ulong.TryParse(id, out ulong number) || number == 0) id = "";
            DateTimeOffset? expiry = root.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out long seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
            return new(id, expiry);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or ArgumentOutOfRangeException)
        { return new(); }
    }
}
