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
    /// The outcome of a translation, with the failure kept separate from the result.
    ///
    /// Returning only the text meant a caller could not tell "already in this language" from
    /// "the service refused us" — both came back as the original, and the app then guessed, and
    /// guessed wrong. Google rate-limits this endpoint with a 429 often enough that the two need
    /// to be told apart.
    /// </summary>
    public sealed record TranslationResult(bool Ok, string Text, bool Unchanged);

    public static async Task<TranslationResult> TranslateAsync(string text, string? targetLanguage = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return new TranslationResult(true, text, true);

        var target = string.IsNullOrWhiteSpace(targetLanguage) ? CurrentLanguage : targetLanguage!;

        try
        {
            var url = "https://translate.googleapis.com/translate_a/single"
                + $"?client=gtx&sl=auto&tl={Uri.EscapeDataString(target)}&dt=t&q={Uri.EscapeDataString(text)}";

            using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new TranslationResult(false, text, true);

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
            if (string.IsNullOrWhiteSpace(translated))
                return new TranslationResult(false, text, true);

            return new TranslationResult(true, translated, string.Equals(translated, text, StringComparison.Ordinal));
        }
        catch
        {
            return new TranslationResult(false, text, true);
        }
    }
}
