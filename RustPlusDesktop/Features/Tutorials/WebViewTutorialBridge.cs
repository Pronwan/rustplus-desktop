using Microsoft.Web.WebView2.Wpf;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace RustPlusDesk.Features.Tutorials;

public sealed class WebViewTutorialBridge(Func<WebView2?> getWebView) : IWebViewTutorialBridge
{
    private sealed record BoundsResponse(double Left, double Top, double Width, double Height, bool Visible);

    public async Task<Rect?> GetTargetBoundsAsync(string targetId, FrameworkElement relativeTo, CancellationToken cancellationToken = default)
    {
        WebView2? webView = getWebView();
        if (webView?.CoreWebView2 is null || !webView.IsVisible) return null;

        string idJson = JsonSerializer.Serialize(targetId);
        string script = $$"""
            (() => {
              const id = {{idJson}};
              const el = document.querySelector(`[data-tutorial-id="${CSS.escape(id)}"]`);
              if (!el) return null;
              const r = el.getBoundingClientRect();
              const s = getComputedStyle(el);
              return { left:r.left, top:r.top, width:r.width, height:r.height,
                visible:r.width>0 && r.height>0 && s.visibility!=='hidden' && s.display!=='none' };
            })()
            """;

        cancellationToken.ThrowIfCancellationRequested();
        string raw = await webView.ExecuteScriptAsync(script);
        if (raw == "null") return null;
        var response = JsonSerializer.Deserialize<BoundsResponse>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (response is null || !response.Visible) return null;

        Point hostOrigin = webView.TransformToAncestor(relativeTo).Transform(new Point());
        return new Rect(hostOrigin.X + response.Left, hostOrigin.Y + response.Top, response.Width, response.Height);
    }
}
