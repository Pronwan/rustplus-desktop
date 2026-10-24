using System;
using System.Reflection;

namespace RustPlusDesk.Helpers
{
    public static class VersionHelper
    {
        private static string? _cachedVersion;

        public static string GetClientVersion()
        {
            if (_cachedVersion != null) return _cachedVersion;

            try
            {
                var attr = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (attr != null && !string.IsNullOrWhiteSpace(attr.InformationalVersion))
                {
                    _cachedVersion = NormalizeVer(attr.InformationalVersion);
                    return _cachedVersion;
                }
            }
            catch { }

            _cachedVersion = NormalizeVer(
                Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0");
            return _cachedVersion;
        }

        private static string NormalizeVer(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "0.0.0";
            s = s.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s[1..];
            int dash = s.IndexOfAny(new[] { '-', '+' });
            if (dash > 0) s = s[..dash];
            return s;
        }
    }
}
