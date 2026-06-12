using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace RustPlusDesk.Services
{
    public static class AlertTemplateService
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RustPlusDesk",
            "custom_alerts.json"
        );

        // Map of Culture Name -> (Resource Key -> Custom Template)
        private static Dictionary<string, Dictionary<string, string>> _overrides = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new();

        static AlertTemplateService()
        {
            Load();
        }

        public static void Load()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(FilePath))
                    {
                        string json = File.ReadAllText(FilePath);
                        var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);
                        if (data != null)
                        {
                            _overrides = new Dictionary<string, Dictionary<string, string>>(data, StringComparer.OrdinalIgnoreCase);
                        }
                    }
                }
                catch (Exception)
                {
                    // Fallback to empty if there's any corruption
                    _overrides = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        public static void Save()
        {
            lock (_lock)
            {
                try
                {
                    string dir = Path.GetDirectoryName(FilePath)!;
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    string json = JsonSerializer.Serialize(_overrides, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(FilePath, json);
                }
                catch (Exception)
                {
                    // Ignore write errors to prevent crashes, or log them
                }
            }
        }

        public static string GetAlertTemplate(string key)
        {
            string culture = Thread.CurrentThread.CurrentUICulture.Name;
            lock (_lock)
            {
                if (_overrides.TryGetValue(culture, out var cultureOverrides) && cultureOverrides.TryGetValue(key, out string? customTemplate))
                {
                    return customTemplate;
                }
            }

            // Fallback to resource manager
            return Properties.Resources.ResourceManager.GetString(key) ?? string.Empty;
        }

        public static string GetFormattedAlert(string key, params object[] args)
        {
            string template = GetAlertTemplate(key);
            if (string.IsNullOrEmpty(template))
            {
                return string.Empty;
            }

            try
            {
                return string.Format(template, args);
            }
            catch (FormatException)
            {
                // If the template is malformed (e.g. invalid placeholders), fall back to the default translation
                string fallbackTemplate = Properties.Resources.ResourceManager.GetString(key) ?? string.Empty;
                try
                {
                    return string.Format(fallbackTemplate, args);
                }
                catch (Exception)
                {
                    return template; // Return template as-is if fallback also fails
                }
            }
        }

        public static void SetOverride(string key, string template)
        {
            string culture = Thread.CurrentThread.CurrentUICulture.Name;
            lock (_lock)
            {
                if (!_overrides.TryGetValue(culture, out var cultureOverrides))
                {
                    cultureOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    _overrides[culture] = cultureOverrides;
                }
                cultureOverrides[key] = template;
            }
            Save();
        }

        public static void RemoveOverride(string key)
        {
            string culture = Thread.CurrentThread.CurrentUICulture.Name;
            lock (_lock)
            {
                if (_overrides.TryGetValue(culture, out var cultureOverrides))
                {
                    cultureOverrides.Remove(key);
                    if (cultureOverrides.Count == 0)
                    {
                        _overrides.Remove(culture);
                    }
                }
            }
            Save();
        }

        public static bool HasOverride(string key)
        {
            string culture = Thread.CurrentThread.CurrentUICulture.Name;
            lock (_lock)
            {
                return _overrides.TryGetValue(culture, out var cultureOverrides) && cultureOverrides.ContainsKey(key);
            }
        }
    }
}
