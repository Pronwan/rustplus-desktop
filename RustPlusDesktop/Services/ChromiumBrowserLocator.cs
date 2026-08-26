using System;
using System.IO;
using System.Linq;

namespace RustPlusDesk.Services
{
    /// <summary>
    /// Locates a Chromium-family browser Puppeteer (Node path) or the native DevTools
    /// registration (RustPlusApi.Fcm.Registration) can drive for the Steam login step.
    ///
    /// Both registration paths need the same thing: a Chrome/Chromium/Edge/Brave/… binary.
    /// Puppeteer only looks for Chrome in its default location and the native library only
    /// auto-detects a handful of well-known installs, so users without Chrome saw a console
    /// window flash and nothing else. Registry first (App Paths is where installers record
    /// themselves), then the usual on-disk locations.
    /// </summary>
    public static class ChromiumBrowserLocator
    {
        /// <summary>
        /// Any Chromium-family browser the registration flow can drive, with the name of the one found.
        /// Order is preference, not availability: Chrome is what the flows were written against,
        /// Edge is on every Windows machine, the rest are courtesy.
        /// </summary>
        /// <param name="browserName">Human-readable name of the browser found, or "" when none.</param>
        /// <param name="onlyThese">Optional filter: restrict discovery to these executable names.</param>
        public static string? Find(out string browserName, params string[] onlyThese)
        {
            var candidates = new (string Exe, string Name, string[] Paths)[]
            {
                ("chrome.exe",  "Google Chrome",  new[] { @"Google\Chrome\Application\chrome.exe" }),
                ("msedge.exe",  "Microsoft Edge", new[] { @"Microsoft\Edge\Application\msedge.exe" }),
                ("brave.exe",   "Brave",          new[] { @"BraveSoftware\Brave-Browser\Application\brave.exe" }),
                ("vivaldi.exe", "Vivaldi",        new[] { @"Vivaldi\Application\vivaldi.exe" }),
                ("opera.exe",   "Opera",          new[] { @"Opera\opera.exe" }),
                ("chrome.exe",  "Chromium",       new[] { @"Chromium\Application\chrome.exe" }),
            };

            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            };

            foreach (var (exe, name, relatives) in candidates)
            {
                if (onlyThese.Length > 0 && !onlyThese.Contains(exe, StringComparer.OrdinalIgnoreCase))
                    continue;

                var fromRegistry = LookUpAppPath(exe);
                if (fromRegistry != null) { browserName = name; return fromRegistry; }

                foreach (var root in roots)
                {
                    if (string.IsNullOrEmpty(root)) continue;

                    foreach (var relative in relatives)
                    {
                        var full = Path.Combine(root, relative);
                        if (File.Exists(full)) { browserName = name; return full; }
                    }
                }
            }

            browserName = "";
            return null;
        }

        /// <summary>Convenience: locate Microsoft Edge specifically.</summary>
        public static string? FindEdge() => Find(out _, "msedge.exe");

        /// <summary>Reads HKCU/HKLM App Paths, where Windows installers register executables.</summary>
        private static string? LookUpAppPath(string exeName)
        {
            foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
            {
                try
                {
                    using var key = hive.OpenSubKey(
                        $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}");
                    if (key?.GetValue(null) is string path)
                    {
                        path = path.Trim('"');
                        if (File.Exists(path)) return path;
                    }
                }
                catch { }
            }

            return null;
        }
    }
}
