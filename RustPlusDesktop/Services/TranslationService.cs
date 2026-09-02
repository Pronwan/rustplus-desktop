using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services;

/// <summary>
/// Machine translation into the language the app is running in.
///
/// The same endpoint the patch notes have used for a long time, lifted out of that window so the
/// chat can use it too. Consent is checked by callers rather than here: the patch notes ask with
/// an overlay and the chat with a dialog, and neither wants the other's answer imposed on it.
/// </summary>
public static class TranslationService
{
    private static readonly HttpClient Http = new(new TrafficTrackingHttpMessageHandler("Translation API"))
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    static TranslationService()
    {
        Http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
    }

    /// <summary>The app's current language as a two-letter code, or "en" when it cannot be told.</summary>
    public static string CurrentLanguage
    {
        get
        {
            var code = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return code == "iv" ? "en" : code;
        }
    }

    /// <summary>
    /// Translates one piece of text, returning the original when anything goes wrong.
    ///
    /// Failing back to the original is deliberate: a chat line that silently stays in its own
    /// language is a far better outcome than an error where the message used to be.
    /// </summary>
    public static async Task<string> TranslateAsync(string text, string? targetLanguage = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var target = string.IsNullOrWhiteSpace(targetLanguage) ? CurrentLanguage : targetLanguage!;

        try
        {
            var url = "https://translate.googleapis.com/translate_a/single"
                + $"?client=gtx&sl=auto&tl={Uri.EscapeDataString(target)}&dt=t&q={Uri.EscapeDataString(text)}";

            using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return text;

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);

            // The response is a nested array; the first element holds one entry per sentence, and
            // the translated sentence is the first item of each.
            var segments = document.RootElement[0];
            var builder = new StringBuilder();
            foreach (var segment in segments.EnumerateArray())
            {
                builder.Append(segment[0].GetString());
            }

            var translated = builder.ToString();
            return string.IsNullOrWhiteSpace(translated) ? text : translated;
        }
        catch
        {
            return text;
        }
    }
}
