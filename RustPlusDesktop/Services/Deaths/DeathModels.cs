namespace RustPlusDesk.Services.Deaths
{
    /// <summary>
    /// One detected team-member death, ready to store locally and (for premium
    /// accounts) POST to the cloud death log. DeathTime is unix seconds from
    /// Rust+ so teammates' reports of the same death dedupe on the backend.
    /// </summary>
    public sealed record DeathRecord(
        ulong SteamId,
        string? Name,
        long DeathTime,
        long? SpawnTime,
        double? X,
        double? Y,
        string? Grid,
        string LocationType,   // "monument" | "base" | "open"
        string? LocationName);

    /// <summary>A circular zone on the map used to classify a death location.</summary>
    public readonly record struct DeathZone(double X, double Y, double Radius, string Name);
}
