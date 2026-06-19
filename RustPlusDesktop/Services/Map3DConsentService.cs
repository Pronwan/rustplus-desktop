using System;
using RustPlusDesk.Services.Data;

namespace RustPlusDesk.Services;

public sealed class Map3DConsentSettings
{
    public bool Accepted { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
}

public static class Map3DConsentService
{
    private const string CacheKey = "map3d_consent";

    public static bool HasRememberedConsent()
    {
        var settings = DataManager.LoadCache<Map3DConsentSettings>(CacheKey);
        return settings?.Accepted == true;
    }

    public static void RememberConsent()
    {
        DataManager.SaveCache(CacheKey, new Map3DConsentSettings
        {
            Accepted = true,
            AcceptedAtUtc = DateTime.UtcNow
        });
    }
}

