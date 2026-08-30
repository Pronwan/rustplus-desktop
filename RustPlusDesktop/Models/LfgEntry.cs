using System;
using System.Windows.Media;

namespace RustPlusDesk.Models;

/// <summary>
/// One row in a Looking for Group list.
///
/// Flattened from the API's nested shape on purpose: a WPF template that has to reach through
/// three levels of null-checkable objects to draw a name ends up doing that logic in bindings,
/// where nothing can be read and nothing can be tested.
/// </summary>
public sealed class LfgEntry
{
    public string UserId { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public string? AvatarUrl { get; init; }

    public string? SteamId { get; init; }

    /// <summary>Two-letter code from the client's UI language, used to pick a flag.</summary>
    public string? Language { get; init; }

    public bool IsOnline { get; init; }

    public bool IsSupporter { get; init; }

    public string? TeamName { get; init; }

    public int TeamSize { get; init; }

    public string? Blurb { get; init; }

    /// <summary>Green when online, muted red otherwise — the one thing read at a glance.</summary>
    public Brush StatusBrush => IsOnline
        ? new SolidColorBrush(Color.FromRgb(0x4C, 0xC3, 0x8A))
        : new SolidColorBrush(Color.FromRgb(0x9A, 0x54, 0x54));

    /// <summary>"3" for a team of three, "—" when nobody is grouped up.</summary>
    public string TeamSizeLabel => TeamSize > 0 ? TeamSize.ToString() : "—";

    /// <summary>
    /// Flag image for the player's language, or null when we have no flag for it. Null means the
    /// column stays empty rather than showing a placeholder that says nothing.
    /// </summary>
    public string? FlagPath => string.IsNullOrWhiteSpace(Language)
        ? null
        : $"pack://application:,,,/Assets/Flags/{FlagFile(Language!)}.png";

    /// <summary>
    /// Maps a culture to the flag we ship. Most are the language half of the code; the ones that
    /// are not are the cases where two cultures share a language or the file is named for the
    /// region.
    /// </summary>
    private static string FlagFile(string language) => language switch
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
        _ => language.Split('-')[0],
    };
}
