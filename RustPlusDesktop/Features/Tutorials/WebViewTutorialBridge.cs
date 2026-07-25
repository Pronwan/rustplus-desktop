using Microsoft.Web.WebView2.Wpf;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace RustPlusDesk.Features.Tutorials;

public sealed class WebViewTutorialBridge(Func<FrameworkElement?> getWebView) : IWebViewTutorialBridge
{
    private sealed record BoundsResponse(double Left, double Top, double Width, double Height, bool Visible);

    public async Task<Rect?> GetTargetBoundsAsync(string targetId, FrameworkElement relativeTo, CancellationToken cancellationToken = default)
    {
        FrameworkElement? control = getWebView();
        if (control is null || !control.IsVisible || control.ActualWidth <= 0 || control.ActualHeight <= 0) return null;

        var coreWebView2 = (control as WebView2)?.CoreWebView2;
        if (coreWebView2 is not null)
        {
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
            try
            {
                string raw = await coreWebView2.ExecuteScriptAsync(script);
                if (raw != "null" && !string.IsNullOrEmpty(raw))
                {
                    var response = JsonSerializer.Deserialize<BoundsResponse>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (response is not null && response.Visible)
                    {
                        Point hostOrigin = control.TransformToAncestor(relativeTo).Transform(new Point());
                        return new Rect(hostOrigin.X + response.Left, hostOrigin.Y + response.Top, response.Width, response.Height);
                    }
                }
            }
            catch { }
        }

        try
        {
            Point origin = control.TransformToAncestor(relativeTo).Transform(new Point());
            return new Rect(origin.X, origin.Y, control.ActualWidth, control.ActualHeight);
        }
        catch
        {
            return null;
        }
    }
}
