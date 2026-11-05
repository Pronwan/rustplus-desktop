// Services/SteamLoginService.cs
using System;
using System.Web;

namespace RustPlusDesk.Services;

public class SteamLoginService
{
    // WICHTIG: https, nicht http
    private const string SteamOpenId = "https://steamcommunity.com/openid/login";
    private const string ReturnTo = "https://localhost/steam/openid/return";
    private const string Realm = "https://localhost/";

    public Uri BuildOpenIdRequestUri()
    {
        var q = HttpUtility.ParseQueryString(string.Empty);
        q["openid.ns"] = "http://specs.openid.net/auth/2.0";
        q["openid.mode"] = "checkid_setup";
        q["openid.return_to"] = ReturnTo;
        q["openid.realm"] = Realm;
        q["openid.claimed_id"] = "http://specs.openid.net/auth/2.0/identifier_select";
        q["openid.identity"] = "http://specs.openid.net/auth/2.0/identifier_select";
        return new Uri($"{SteamOpenId}?{q}");
    }

    public bool TryExtractSteamId64FromReturnUrl(string url, out string steamId64)
    {
        steamId64 = "";
        if (!url.StartsWith(ReturnTo, StringComparison.OrdinalIgnoreCase)) return false;
        var uri = new Uri(url);
        var q = HttpUtility.ParseQueryString(uri.Query);
        var claimed = q.Get("openid.claimed_id");
        if (string.IsNullOrEmpty(claimed)) return false;
        var lastSlash = claimed.LastIndexOf('/');
        if (lastSlash < 0 || lastSlash == claimed.Length - 1) return false;
        steamId64 = claimed[(lastSlash + 1)..];
        return true;
    }
}
