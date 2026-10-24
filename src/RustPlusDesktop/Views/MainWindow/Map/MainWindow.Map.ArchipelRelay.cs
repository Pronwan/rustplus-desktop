using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    // The 3D viewer's Bases browser reads Archipel (api.archipel.gg), which sends no CORS headers
    // for https://rustplus3d.local, so the viewer asks for /archipel/... and the app fetches it.
    // Only the public, read-only browsing and build-data endpoints pass.
    private static readonly HttpClient _archipelHttpClient = new(new Services.TrafficTrackingHttpMessageHandler("Archipel"))
    {
        BaseAddress = new Uri("https://api.archipel.gg/"),
        Timeout = TimeSpan.FromSeconds(25)
    };

    private static readonly Regex ArchipelRelayPath = new(
        @"^(bases|bases/featured|bases/[0-9a-f]{24}/stats|trending/(day|week|month)|preview/data/[A-Za-z0-9_-]{1,40})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private const long ArchipelRelayMaxBytes = 60L * 1024 * 1024;

    private bool TryRelayArchipelRequest(CoreWebView2WebResourceRequestedEventArgs args, Uri uri)
    {
        const string prefix = "/archipel/";
        if (!uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) return false;

        string apiPath = uri.AbsolutePath[prefix.Length..];
        if (!string.Equals(args.Request.Method, "GET", StringComparison.OrdinalIgnoreCase) || !ArchipelRelayPath.IsMatch(apiPath))
        {
            args.Response = CreateMap3D404Response();
            return true;
        }

        var deferral = args.GetDeferral();
        _ = RelayArchipelAsync(args, deferral, apiPath + uri.Query);
        return true;
    }

    private async System.Threading.Tasks.Task RelayArchipelAsync(CoreWebView2WebResourceRequestedEventArgs args, CoreWebView2Deferral deferral, string pathAndQuery)
    {
        try
        {
            using var response = await _archipelHttpClient.GetAsync(pathAndQuery, HttpCompletionOption.ResponseHeadersRead);
            if (response.Content.Headers.ContentLength > ArchipelRelayMaxBytes) throw new InvalidDataException("Archipel response too large.");
            byte[] body = await response.Content.ReadAsByteArrayAsync();
            string contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
            await Dispatcher.InvokeAsync(() =>
            {
                if (_map3DWebView?.CoreWebView2 == null) return;
                args.Response = _map3DWebView.CoreWebView2.Environment.CreateWebResourceResponse(
                    new MemoryStream(body), (int)response.StatusCode, response.ReasonPhrase ?? "",
                    $"Content-Type: {contentType}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store");
            });
        }
        catch (Exception ex)
        {
            AppendLog($"[map3d] Archipel request failed: {ex.Message}");
            await Dispatcher.InvokeAsync(() =>
            {
                if (_map3DWebView?.CoreWebView2 == null) return;
                args.Response = _map3DWebView.CoreWebView2.Environment.CreateWebResourceResponse(
                    new MemoryStream(), 502, "Bad Gateway", "Cache-Control: no-store");
            });
        }
        finally
        {
            await Dispatcher.InvokeAsync(deferral.Complete);
        }
    }
}
