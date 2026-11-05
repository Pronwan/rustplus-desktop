using System.Net;
using System.Text.RegularExpressions;

namespace RustPlusDesk.Models;

public readonly record struct TeamChatMessage(
    System.DateTime Timestamp,
    string Author,
     ulong SteamId,
    string Text,
    string? Ip = null,
    int? Port = null
)
{
    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    public string Text { get; init; } = RemoveHtmlTags(Text);

    public static string RemoveHtmlTags(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var decoded = WebUtility.HtmlDecode(text);
        var withoutTags = HtmlTagRegex.Replace(decoded, string.Empty);
        return WebUtility.HtmlDecode(withoutTags);
    }
}
