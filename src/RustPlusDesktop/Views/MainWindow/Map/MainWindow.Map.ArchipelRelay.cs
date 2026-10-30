using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Web.WebView2.Core;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    // The 3D viewer's Bases browser reads Archipel (api.archipel.gg), which sends no CORS headers
    // for https://rustplus3d.local, so the app relays requests via an unmapped API origin.
    // WebView2 does not fire WebResourceRequested for virtual-host folder mappings.
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
    private const string ArchipelRelayHeaders = "Access-Control-Allow-Origin: https://rustplus3d.local\r\nCache-Control: no-store";

    internal static bool TryGetArchipelRelayPath(Uri uri, string method, out string pathAndQuery)
    {
        pathAndQuery = "";
        const string prefix = "/archipel/";
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || !uri.IsDefaultPort ||
            (uri.Host != "rustplus-api.invalid" && uri.Host != "rustplus3d.local") ||
            !string.IsNullOrEmpty(uri.UserInfo) || !uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal) ||
            !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)) return false;
        string apiPath = uri.AbsolutePath[prefix.Length..];
        if (!ArchipelRelayPath.IsMatch(apiPath)) return false;
        pathAndQuery = apiPath + uri.Query;
        return true;
    }

    private bool TryRelayArchipelRequest(CoreWebView2WebResourceRequestedEventArgs args, Uri uri, CoreWebView2Environment? environment = null, Func<bool>? isActive = null)
    {
        const string prefix = "/archipel/";
        if (uri.Scheme != "https" || (uri.Host != "rustplus-api.invalid" && uri.Host != "rustplus3d.local")) return false;
        if (!uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) return false;
        environment ??= _map3DWebView?.CoreWebView2?.Environment;
        if (environment is null) return false;
        var requestView = _map3DWebView;
        isActive ??= () => requestView != null && ReferenceEquals(requestView, _map3DWebView);

        if (!TryGetArchipelRelayPath(uri, args.Request.Method, out string pathAndQuery))
        {
            args.Response = environment.CreateWebResourceResponse(new MemoryStream(), 404, "Not Found", ArchipelRelayHeaders);
            return true;
        }

        var deferral = args.GetDeferral();
        _ = RelayArchipelAsync(args, deferral, pathAndQuery, environment, isActive);
        return true;
    }

    private async System.Threading.Tasks.Task RelayArchipelAsync(CoreWebView2WebResourceRequestedEventArgs args, CoreWebView2Deferral deferral, string pathAndQuery, CoreWebView2Environment environment, Func<bool> isActive)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            using var response = await _archipelHttpClient.GetAsync(pathAndQuery, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.Content.Headers.ContentLength > ArchipelRelayMaxBytes) throw new InvalidDataException("Archipel response too large.");
            using var bodyStream = new MemoryStream();
            using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[81920];
            int count;
            while ((count = await source.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (bodyStream.Length + count > ArchipelRelayMaxBytes) throw new InvalidDataException("Archipel response too large.");
                bodyStream.Write(buffer, 0, count);
            }
            byte[] body = bodyStream.ToArray();
            string contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
            await Dispatcher.InvokeAsync(() =>
            {
                if (!isActive()) return;
                args.Response = environment.CreateWebResourceResponse(
                    new MemoryStream(body), (int)response.StatusCode, response.ReasonPhrase ?? "",
                    $"Content-Type: {contentType}\r\nContent-Length: {body.Length}\r\n{ArchipelRelayHeaders}");
            });
        }
        catch (Exception ex)
        {
            AppendLog($"[map3d] Archipel request failed: {ex.Message}");
            await Dispatcher.InvokeAsync(() =>
            {
                if (!isActive()) return;
                args.Response = environment.CreateWebResourceResponse(
                    new MemoryStream(), 502, "Bad Gateway", ArchipelRelayHeaders);
            });
        }
        finally
        {
            await Dispatcher.InvokeAsync(deferral.Complete);
        }
    }
}
