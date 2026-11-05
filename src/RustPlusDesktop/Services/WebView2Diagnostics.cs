using System;
using System.Text;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace RustPlusDesk.Services;

/// <summary>
/// Turns a refused embedded browser into a sentence somebody can act on.
///
/// The failure this exists for says only "the group or resource is not in the
/// correct state to perform the requested operation (0x8007139F)" — which names
/// nothing, suggests nothing, and is identical whether the runtime is missing,
/// blocked, or merely unhappy. Establishing what it meant once took an afternoon
/// and three people: Windows had placed a compatibility override on Microsoft's
/// own WebView2 executable, something it does unprompted after an application has
/// crashed a few times, and the browser gave up when it could not then set its own
/// DPI mode.
///
/// Nothing here runs unless something has already gone wrong. It costs two
/// registry reads and one version query, and it answers the question the HRESULT
/// refuses to.
/// </summary>
internal static class WebView2Diagnostics
{
    private const string LayersKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    /// <summary>
    /// A log line describing why the browser would not start, as specifically as
    /// the machine allows.
    ///
    /// Never throws. A diagnosis that fails must not replace the failure it was
    /// meant to describe — the caller is already handling something worse.
    /// </summary>
    public static string Explain(Exception? ex)
    {
        var sb = new StringBuilder();

        try
        {
            if (ex != null)
            {
                var root = ex.GetBaseException();
                sb.Append($"{root.GetType().Name}: {root.Message} (HRESULT 0x{root.HResult:X8})");
            }

            var runtime = RuntimeVersionOrNull();
            sb.Append(runtime == null
                ? " | WebView2 runtime is not installed — get it from https://developer.microsoft.com/microsoft-edge/webview2/"
                : $" | WebView2 runtime {runtime}");

            if (CompatibilityOverride() is string layers)
            {
                sb.Append($" | Windows has a compatibility override on the WebView2 browser ({layers}), " +
                          "which stops it starting. Clear it: right-click msedgewebview2.exe in " +
                          @"C:\Program Files (x86)\Microsoft\EdgeWebView\Application\<version>\ -> Properties " +
                          "-> Compatibility -> Change high DPI settings, and untick the override.");
            }
        }
        catch (Exception diagnosisFailed)
        {
            sb.Append($" | (diagnosis unavailable: {diagnosisFailed.Message})");
        }

        return sb.ToString();
    }

    /// <summary>The installed runtime version, or null when there is none to find.</summary>
    private static string? RuntimeVersionOrNull()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch
        {
            // The documented way of saying "no runtime here" is to throw.
            return null;
        }
    }

    /// <summary>
    /// Any compatibility layer Windows has pinned onto the WebView2 executable,
    /// or null when it has left it alone.
    ///
    /// Both hives are read: the per-user one is where the compatibility assistant
    /// writes, the machine one is where an administrator or a policy would.
    /// </summary>
    private static string? CompatibilityOverride()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = hive.OpenSubKey(LayersKey);
                if (key == null) continue;

                foreach (var name in key.GetValueNames())
                {
                    if (name.IndexOf("msedgewebview2", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    return $"{key.GetValue(name)}";
                }
            }
            catch
            {
                // A hive we may not read tells us nothing either way.
            }
        }

        return null;
    }
}
