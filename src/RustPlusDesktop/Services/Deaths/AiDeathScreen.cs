using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RustPlusDesk.Services.AiCompanion;

namespace RustPlusDesk.Services.Deaths
{
    /// <summary>
    /// Reading the death screen with the configured model instead of with Windows.
    ///
    /// Windows' recogniser only knows the scripts it has a language installed for, which for most
    /// people is one: a machine set up in German reads Latin letters and nothing else. Rust is
    /// played by people whose names are in Cyrillic, Greek, Chinese and Arabic, and to that
    /// recogniser those names are not badly read — they are not there at all. The group simply
    /// vanishes, and the weapon beside it becomes the first thing left, which is how a rock ends
    /// up recorded as a killer.
    ///
    /// A model reads all of it, and works out which box is which from the picture rather than
    /// from the gaps. It costs the user's own API credit, so it is off until somebody turns it
    /// on, and it falls back to Windows the moment there is no key.
    /// </summary>
    public static class AiDeathScreen
    {
        /// <summary>Whether this is available: any provider with a key, since all of them take images.</summary>
        public static bool Available => AiCompanionStore.HasKey;

        /// <summary>
        /// Asks the model who is named on the death screen.
        ///
        /// One line back, with a bar between the two answers, because anything more conversational
        /// has to be parsed out of a sentence that changes shape between providers. Names are
        /// copied exactly — a name is not a word to be corrected, and a model that tidies
        /// "KoHЧaHue" into something spellable has produced a different player.
        /// </summary>
        public static async Task<(string? Killer, string? Weapon)> ReadAsync(
            string imagePath, CancellationToken ct = default)
        {
            var key = AiCompanionStore.ReadKey();
            if (string.IsNullOrEmpty(key) || !File.Exists(imagePath)) return (null, null);

            // The providers label an attached picture as a JPEG, so it had better be one.
            var jpeg = AsJpeg(imagePath);
            if (jpeg == null) return (null, null);

            try
            {
                var question = new AiQuestion
                {
                    ScreenshotPath = jpeg,

                    Instructions =
                        "This picture is the top strip of the death screen from the game Rust. " +
                        "It shows up to four boxes in a row: how long the player was alive, who " +
                        "killed them, what weapon was used, and from what distance. Some of them " +
                        "may be missing. " +
                        "Answer with the killer's name, then a vertical bar, then the weapon, " +
                        "like this: Name | Weapon. Nothing else — no labels, no explanation, " +
                        "no quotes. Put the bar in even when there is no weapon box, with " +
                        "nothing after it. Answer with nothing at all if there is no name. " +
                        "Copy the name exactly as it is written, character for character, in its " +
                        "own alphabet: it is a player's name, not a word to be corrected or " +
                        "transliterated. Never answer with the duration or the distance.",

                    MatchQuestionLanguage = false,
                    Language = "English",
                };

                var provider = AiProviderFactory.For(AiCompanionStore.Current.Provider);
                var result = await provider.AskAsync(question, key!, null, ct).ConfigureAwait(false);

                // A bar, a tab or a line break. The bar is what it asks for, because a smaller
                // model drops a tab and hands back one run of words; the other two are here
                // because some answer in their own shape whatever they were asked.
                var parts = result.Answer
                    .Split('|', '\t', '\n')
                    .Select(p => p.Trim())
                    .Where(p => p.Length > 0)
                    .ToList();

                if (parts.Count == 0) return (null, null);

                return (parts[0], parts.Count > 1 ? parts[1] : null);
            }
            finally
            {
                try { File.Delete(jpeg); } catch { }
            }
        }

        /// <summary>
        /// The crop as a JPEG, at a quality that keeps small letters legible.
        ///
        /// The crop itself is a PNG because the character recogniser reads it, and JPEG spends
        /// its error budget on exactly the edges letters are made of. The same reasoning sets
        /// this high rather than at the encoder's default: the model is reading a name in a font
        /// it has never seen, at a hundred pixels wide.
        /// </summary>
        private static string? AsJpeg(string pngPath)
        {
            try
            {
                var path = Path.Combine(Path.GetDirectoryName(pngPath)!, "death-screen-ai.jpg");

                using var bitmap = new Bitmap(pngPath);

                var encoder = ImageCodecInfo.GetImageEncoders()
                    .FirstOrDefault(e => e.FormatID == ImageFormat.Jpeg.Guid);

                if (encoder == null) return null;

                using var parameters = new EncoderParameters(1);
                parameters.Param[0] = new EncoderParameter(Encoder.Quality, 95L);

                bitmap.Save(path, encoder, parameters);
                return path;
            }
            catch
            {
                return null;
            }
        }
    }
}
