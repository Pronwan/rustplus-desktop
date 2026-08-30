using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RustPlusDesk.Helpers;

/// <summary>One language the app ships, as the settings picker and the LFG filter both see it.</summary>
public sealed record AppLanguage(string Code, string Name)
{
    public string FlagPath => $"pack://application:,,,/Assets/Flags/{AppLanguages.FlagFile(Code)}.png";
}

/// <summary>
/// The languages the app actually ships, in one place.
///
/// There used to be three lists: the settings picker, the LFG filter, and the flag lookup on a
/// listing. They disagreed — the filter offered seven of thirty-two, so a player running the app
/// in Italian could be listed but never found. A list that decides who is reachable is not the
/// kind of list to keep three copies of.
///
/// Codes match the folders under Properties/lang, which is where Crowdin writes the satellites.
/// Names are in the language itself: somebody looking for their own language reads it in their
/// own language, not in English.
/// </summary>
public static class AppLanguages
{
    public static IReadOnlyList<AppLanguage> All { get; } = new AppLanguage[]
    {
        new("af-ZA", "Afrikaans"),
        new("ar-SA", "العربية"),
        new("ca-ES", "Català"),
        new("cs-CZ", "Čeština"),
        new("da-DK", "Dansk"),
        new("de-DE", "Deutsch"),
        new("el-GR", "Ελληνικά"),
        new("en-US", "English"),
        new("es-ES", "Español"),
        new("fi-FI", "Suomi"),
        new("fr-FR", "Français"),
        new("he-IL", "עברית"),
        new("hu-HU", "Magyar"),
        new("it-IT", "Italiano"),
        new("ja-JP", "日本語"),
        new("ko-KR", "한국어"),
        new("nl-NL", "Nederlands"),
        new("no-NO", "Norsk"),
        new("pl-PL", "Polski"),
        new("pt-BR", "Português (BR)"),
        new("pt-PT", "Português (PT)"),
        new("ro-RO", "Română"),
        new("ru-RU", "Русский"),
        new("sr-Latn-RS", "Srpski"),
        new("sv-SE", "Svenska"),
        new("tr-TR", "Türkçe"),
        new("uk-UA", "Українська"),
        new("vi-VN", "Tiếng Việt"),
        new("zh-CN", "简体中文"),
        new("zh-Hans", "简体中文 (Hans)"),
        new("zh-Hant", "繁體中文 (Hant)"),
        new("zh-TW", "繁體中文"),
    };

    /// <summary>
    /// The flag we ship for a code. Most are the language half; the exceptions are where two
    /// cultures share a language or the file is named for the region instead.
    /// </summary>
    public static string FlagFile(string code) => code switch
    {
        "es-ES" => "es-ES",
        "pt-BR" => "pt-BR",
        "pt-PT" => "pt-PT",
        "sv-SE" => "sv-SE",
        "zh-CN" => "zh-CN",
        "zh-TW" => "zh-TW",
        "zh-Hans" => "zh-Hans",
        "zh-Hant" => "zh-Hant",
        "sr-Latn-RS" => "sr",
        _ => code.Split('-')[0],
    };

    /// <summary>
    /// The shipped code closest to a culture, or null when we ship nothing for it.
    ///
    /// A Windows set to de-AT resolves to de-DE rather than to nothing: the point of reporting a
    /// language is being findable by people who speak it, and Austrian German finds German.
    /// </summary>
    public static string? Resolve(CultureInfo? culture)
    {
        var name = culture?.Name;
        if (string.IsNullOrWhiteSpace(name)) return null;

        var exact = All.FirstOrDefault(l => string.Equals(l.Code, name, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact.Code;

        var part = name!.Split('-')[0];

        // Bokmål and Nynorsk are what Windows reports; the folder is the older two-letter code.
        if (part.Equals("nb", StringComparison.OrdinalIgnoreCase)
            || part.Equals("nn", StringComparison.OrdinalIgnoreCase))
            return "no-NO";

        return All.FirstOrDefault(l =>
            l.Code.Split('-')[0].Equals(part, StringComparison.OrdinalIgnoreCase))?.Code;
    }

    /// <summary>
    /// The language this install is running in. Reads the culture the app resolved at startup
    /// rather than the stored setting, because the stored setting is empty for "system default"
    /// — which is most people.
    /// </summary>
    public static string? Current()
        => Resolve(Properties.Resources.Culture ?? CultureInfo.InstalledUICulture);
}
