using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.AiCompanion
{
    /// <summary>
    /// Translating with the model the user already configured, instead of with Google.
    ///
    /// Worth offering because the text this app translates is game chat: short, misspelt, full
    /// of abbreviations and in-game words, and often missing the context a sentence would carry.
    /// A general translator does badly on exactly that, and a model does well on it.
    ///
    /// Any provider can do this — it is text in and text out, with no audio or images involved —
    /// so unlike the speech setting, the only thing it needs is a key.
    /// </summary>
    public static class AiTranslation
    {
        /// <summary>Whether translating this way is possible right now: a key, any provider.</summary>
        public static bool Available => AiCompanionStore.HasKey;

        /// <summary>
        /// The translation, and nothing else around it.
        ///
        /// The whole standing companion prompt is replaced rather than added to. Left in place,
        /// the model answers as the in-game companion — it remarks on the line instead of
        /// translating it — and every request re-reads the raid table and the recipe list to do
        /// it, which is paid for by the word.
        /// </summary>
        /// <exception cref="AiRequestException">The provider refused, or there is no key.</exception>
        public static async Task<string> TranslateAsync(string text, string targetCulture, CancellationToken ct)
        {
            var key = AiCompanionStore.ReadKey();

            if (string.IsNullOrEmpty(key))
            {
                throw new AiRequestException(
                    "No key is stored for the chosen provider, so there is nothing to translate with.");
            }

            string language = LanguageName(targetCulture);

            var question = new AiQuestion
            {
                Text = text,

                Instructions =
                    $"You are a translator. Translate the user's text into {language} and reply " +
                    "with the translation only — no quotes, no explanation, no notes about the " +
                    "original, no alternatives. Work out the source language yourself. " +
                    "The text is chat from the game Rust: expect misspellings, abbreviations, " +
                    "slang and game jargon, and translate what was meant rather than word by " +
                    "word. Leave player names, server names and item names that have no ordinary " +
                    "translation as they are. If the text is already in " + language + ", reply " +
                    "with it unchanged.",

                // Both off, or the companion's own language handling fights the instruction
                // above for which language the answer comes back in.
                MatchQuestionLanguage = false,
                Language = language,
            };

            var provider = AiProviderFactory.For(AiCompanionStore.Current.Provider);
            var result = await provider.AskAsync(question, key!, null, ct).ConfigureAwait(false);

            return result.Answer.Trim();
        }

        /// <summary>
        /// The language's name in English, which is what the instruction above is written in.
        ///
        /// Without the country: "German" is what is wanted, not "German (Germany)", and for the
        /// pair this app ships twice over — Portuguese and Chinese — the country is the whole
        /// difference, so those keep it.
        /// </summary>
        private static string LanguageName(string culture)
        {
            try
            {
                var info = new CultureInfo(culture);

                bool countryMatters = culture.StartsWith("pt", StringComparison.OrdinalIgnoreCase)
                                   || culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

                return countryMatters ? info.EnglishName : info.Parent.EnglishName;
            }
            catch
            {
                // An unknown code is still better sent than swallowed: the model will make more
                // of "ru-RU" than this code can.
                return culture;
            }
        }
    }
}
