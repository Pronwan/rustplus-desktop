using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.Social;

/// <summary>What Steam is willing to say about an account without an API key.</summary>
public sealed record SteamProfile(
    string SteamId,
    string? PersonaName,
    string? AvatarUrl,
    string? MemberSince,
    string? Location,
    bool VacBanned,
    bool IsPrivate,
    string? RustHours)
{
    public string ProfileUrl => $"https://steamcommunity.com/profiles/{SteamId}";
}

/// <summary>
/// Reads the public Steam community profile for a 64-bit id.
///
/// The ?xml=1 view rather than the Web API, because the Web API needs a key and a key on a
/// desktop client is a key in everybody's hands. This is the same source the team and clan lists
/// already use, so nothing new is being asked of Steam.
///
/// Everything here is best-effort. A private profile, a rate limit or a rewritten page all mean
/// the same thing to the caller: no card. That is why nothing throws.
/// </summary>
public static class SteamProfileService
{
    private static readonly HttpClient Http = new(new TrafficTrackingHttpMessageHandler("Steam Community")) { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>
    /// Answers are cached for the session. A profile page is opened repeatedly while scrolling a
    /// list, and Steam rate-limits a client that asks for the same page thirty times a minute.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SteamProfile> Cache = new(StringComparer.Ordinal);

    private static string? Field(string xml, string tag)
    {
        var match = Regex.Match(xml, $@"<{tag}><!\[CDATA\[(.*?)\]\]></{tag}>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (match.Success) return Trim(match.Groups[1].Value);

        // Steam omits the CDATA wrapper on some fields; take the plain form too rather than
        // silently showing nothing for half the card.
        match = Regex.Match(xml, $@"<{tag}>(.*?)</{tag}>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? Trim(match.Groups[1].Value) : null;
    }

    private static string? ExtractRustHours(string xml)
    {
        var match = Regex.Match(xml, @"<mostPlayedGame>\s*<gameName>(?:<!\[CDATA\[)?Rust(?:\]\]>)?</gameName>.*?<hoursOnRecord>(.*?)</hoursOnRecord>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (match.Success)
        {
            var text = Trim(match.Groups[1].Value);
            if (!string.IsNullOrEmpty(text))
                return $"{text} hrs";
        }
        return null;
    }

    private static string? Trim(string value)
    {
        var text = value.Trim();
        return text.Length == 0 ? null : text;
    }

    public static async Task<SteamProfile?> GetAsync(string? steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId)) return null;
        if (!ulong.TryParse(steamId, out _)) return null;

        if (Cache.TryGetValue(steamId!, out var cached)) return cached;

        try
        {
            var xml = await Http.GetStringAsync($"https://steamcommunity.com/profiles/{steamId}?xml=1")
                .ConfigureAwait(false);

            // A private profile still returns a document, with a name and an avatar and nothing
            // else. Saying so is more use than an empty card that looks broken.
            var isPrivate = Field(xml, "privacyState") is { } privacy
                && !privacy.Equals("public", StringComparison.OrdinalIgnoreCase);

            var rustHours = ExtractRustHours(xml);

            if (rustHours == null && !isPrivate)
            {
                try
                {
                    var statsXml = await Http.GetStringAsync($"https://steamcommunity.com/profiles/{steamId}/stats/appid/252490/?xml=1")
                        .ConfigureAwait(false);
                    var hoursMatch = Regex.Match(statsXml, @"<hoursPlayed>(.*?)</hoursPlayed>", RegexOptions.IgnoreCase);
                    if (hoursMatch.Success)
                    {
                        var val = Trim(hoursMatch.Groups[1].Value);
                        if (val != null && double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var h) && h > 0)
                        {
                            rustHours = $"{h:N0} hrs";
                        }
                    }
                }
                catch { }
            }

            var profile = new SteamProfile(
                steamId!,
                Field(xml, "steamID"),
                Field(xml, "avatarFull") ?? Field(xml, "avatarMedium"),
                Field(xml, "memberSince"),
                Field(xml, "location"),
                Field(xml, "vacBanned") == "1",
                isPrivate,
                rustHours);

            Cache[steamId!] = profile;
            return profile;
        }
        catch
        {
            // Private, rate-limited, offline, or Steam changed the page. All the same from here.
            return null;
        }
    }
}
