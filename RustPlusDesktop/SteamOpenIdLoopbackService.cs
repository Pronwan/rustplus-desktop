// Services/SteamOpenIdLoopbackService.cs
using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace RustPlusDesk.Services;

public class SteamOpenIdLoopbackService
{
    private const string SteamOpenId = "https://steamcommunity.com/openid/login";

    public async Task<string> SignInAsync(int port = 51713)
    {
        var returnTo = $"http://127.0.0.1:{port}/steam/openid/return";
        var realm = $"http://127.0.0.1:{port}/";

        var q = HttpUtility.ParseQueryString(string.Empty);
        q["openid.ns"] = "http://specs.openid.net/auth/2.0";
        q["openid.mode"] = "checkid_setup";
        q["openid.return_to"] = returnTo;
        q["openid.realm"] = realm;
        q["openid.claimed_id"] = "http://specs.openid.net/auth/2.0/identifier_select";
        q["openid.identity"] = "http://specs.openid.net/auth/2.0/identifier_select";
        var openIdUrl = $"{SteamOpenId}?{q}";

        using var listener = new HttpListener();
        listener.Prefixes.Add(realm); // z.B. http://127.0.0.1:51713/
        listener.Start();

        // Im Default-Browser öffnen
        Process.Start(new ProcessStartInfo(openIdUrl) { UseShellExecute = true });

        // Auf Callback warten
        var ctx = await listener.GetContextAsync();
        var req = ctx.Request;

        // SteamID64 extrahieren
        var claimed = req.QueryString.Get("openid.claimed_id");
        var sid = "";
        if (!string.IsNullOrEmpty(claimed))
        {
            var i = claimed.LastIndexOf('/');
            if (i >= 0 && i < claimed.Length - 1)
                sid = claimed[(i + 1)..];
        }

        // Nutzerfreundliche Antwort im Browser
        var html = @"<html><body style='font-family:sans-serif'>
                      <h2>Mit Steam verbunden. </h2>
                      <p>Deine Steam ID steht in der RustPlus Desktop-App, du Hund. Schließe das Browser-Fenster!</p>
                     </body></html>";
        var buf = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = buf.Length;
        await ctx.Response.OutputStream.WriteAsync(buf, 0, buf.Length);
        ctx.Response.Close();
        listener.Stop();

        if (string.IsNullOrEmpty(sid))
            throw new InvalidOperationException("SteamID64 konnte nicht gelesen werden.");

        return sid;
    }
}