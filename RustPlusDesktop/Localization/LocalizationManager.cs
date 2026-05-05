using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace RustPlusDesk.Localization;

/// <summary>
/// Loads JSON language files from the Languages/ directory next to the executable
/// and exposes translations through an indexer so XAML bindings refresh on language change.
/// </summary>
public class LocalizationManager : INotifyPropertyChanged
{
    private const string FallbackLanguage = "en";
    private const string MissingValueMarker = "@@TODO";

    private static readonly Lazy<LocalizationManager> _instance =
        new(() => new LocalizationManager());

    public static LocalizationManager Instance => _instance.Value;

    private Dictionary<string, string> _current = new();
    private Dictionary<string, string> _fallback = new();
    private string _currentLanguage = FallbackLanguage;

    private readonly Dictionary<string, LanguageMetadata> _available = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private LocalizationManager()
    {
        DiscoverLanguages();
        LoadLanguage(FallbackLanguage, isFallback: true);
        _currentLanguage = FallbackLanguage;
    }

    /// <summary>
    /// XAML-friendly indexer. Returns translated string for the given key, with full fallback chain.
    /// </summary>
    public string this[string key]
    {
        get => T(key);
    }

    /// <summary>
    /// C# helper. Returns translated string with optional formatting arguments.
    /// </summary>
    public string T(string key, params object[] args)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        string value;
        if (_current.TryGetValue(key, out var v) && !IsTodo(v))
        {
            value = v;
        }
        else if (_fallback.TryGetValue(key, out var f) && !IsTodo(f))
        {
            value = f;
        }
        else
        {
            // Visible marker so missing keys are obvious during dev.
            return $"[{key}]";
        }

        if (args.Length == 0) return value;
        try
        {
            return string.Format(CultureInfo.CurrentCulture, value, args);
        }
        catch (FormatException)
        {
            return value;
        }
    }

    private static bool IsTodo(string s) =>
        string.IsNullOrEmpty(s) || s.StartsWith(MissingValueMarker, StringComparison.Ordinal);

    public string CurrentLanguage => _currentLanguage;

    public IReadOnlyList<LanguageMetadata> AvailableLanguages =>
        _available.Values.OrderBy(m => m.Code).ToList();

    /// <summary>
    /// Switch to a new language. No-op if the code is unknown. Triggers refresh of all
    /// bindings that resolve through the indexer.
    /// </summary>
    public void SetLanguage(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        if (string.Equals(code, _currentLanguage, StringComparison.OrdinalIgnoreCase)) return;
        if (!_available.ContainsKey(code) && !string.Equals(code, FallbackLanguage, StringComparison.OrdinalIgnoreCase))
            return;

        if (LoadLanguage(code, isFallback: false))
        {
            _currentLanguage = code;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        }
    }

    private void DiscoverLanguages()
    {
        var dir = LanguagesDirectory();
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            var code = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrEmpty(code)) continue;

            try
            {
                using var stream = File.OpenRead(file);
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;
                string display = code;
                string english = code;
                if (root.TryGetProperty("__meta", out var meta))
                {
                    if (meta.TryGetProperty("display_name", out var dn) && dn.ValueKind == JsonValueKind.String)
                        display = dn.GetString() ?? code;
                    if (meta.TryGetProperty("english_name", out var en) && en.ValueKind == JsonValueKind.String)
                        english = en.GetString() ?? code;
                }
                _available[code] = new LanguageMetadata(code, display, english);
            }
            catch
            {
                // Malformed file: skip silently rather than crash on startup.
            }
        }
    }

    private bool LoadLanguage(string code, bool isFallback)
    {
        var dict = ReadLanguageFile(code);
        if (dict is null) return false;

        if (isFallback)
        {
            _fallback = dict;
            _current = dict;
        }
        else
        {
            _current = dict;
        }
        return true;
    }

    private static Dictionary<string, string>? ReadLanguageFile(string code)
    {
        var dir = LanguagesDirectory();
        var path = Path.Combine(dir, code + ".json");
        if (!File.Exists(path)) return null;

        try
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.StartsWith("__", StringComparison.Ordinal)) continue;
                if (prop.Value.ValueKind == JsonValueKind.String)
                    dict[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }
            return dict;
        }
        catch
        {
            return null;
        }
    }

    private static string LanguagesDirectory()
    {
        var exe = Assembly.GetEntryAssembly()?.Location;
        var baseDir = !string.IsNullOrEmpty(exe)
            ? Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory
            : AppContext.BaseDirectory;
        return Path.Combine(baseDir, "Languages");
    }
}

public sealed record LanguageMetadata(string Code, string DisplayName, string EnglishName)
{
    public override string ToString() =>
        string.Equals(DisplayName, EnglishName, StringComparison.Ordinal)
            ? DisplayName
            : $"{DisplayName} ({EnglishName})";
}
