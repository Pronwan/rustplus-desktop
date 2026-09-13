using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.Deaths
{
    /// <summary>What was read off the death screen, and the picture it was read from.</summary>
    /// <param name="Killer">The name at the top, or null when nothing legible was there.</param>
    /// <param name="Weapon">What they did it with, where a second line was found.</param>
    /// <param name="Lines">Every line the recogniser returned, for the settings preview.</param>
    /// <param name="ImagePath">The crop itself, so the region can be checked by eye.</param>
    public sealed record DeathScreenText(
        string? Killer, string? Weapon, IReadOnlyList<string> Lines, string? ImagePath);

    /// <summary>
    /// Reads the name and weapon off Rust's death screen.
    ///
    /// This is a copy of what the display is already showing — the same thing a screen recorder
    /// takes — cropped, and handed to the character recogniser that ships with Windows. Nothing
    /// reads from the game's process, nothing is injected into it, and nothing happens without
    /// the player pressing the button: what is automated here is the typing, not the playing.
    ///
    /// Windows' own recogniser rather than the one the genetics lab uses. That one is Tesseract
    /// inside a browser, with tens of megabytes of language data behind it, which is a lot of
    /// machinery to start up for two words. This one is part of the operating system and is
    /// already there.
    /// </summary>
    public static class DeathScreenReader
    {
        /// <summary>
        /// Where the name sits, as fractions of the screen: the top middle.
        ///
        /// A starting point rather than a measurement. Rust's death screen puts the killer's
        /// name across the top centre, and the band below is wide enough to take the weapon line
        /// with it; how much of it is name and how much is padding differs with resolution and
        /// aspect ratio, which is why this is four numbers somebody can drag rather than a
        /// constant.
        /// </summary>
        public const double DefaultLeft = 0.30;
        public const double DefaultTop = 0.055;
        public const double DefaultWidth = 0.40;
        public const double DefaultHeight = 0.075;

        /// <summary>Whether this machine has a recogniser at all.</summary>
        public static bool Available => Engine() != null;

        /// <summary>The language Windows will read in, for the settings to name.</summary>
        public static string? RecognizerLanguage => Engine()?.RecognizerLanguage?.DisplayName;

        /// <summary>
        /// Takes the picture and reads it.
        ///
        /// Deliberately hands back everything it saw, not only its best guess. The region is a
        /// setting, and somebody tuning it needs to see what the recogniser actually got rather
        /// than a name that may have come from the wrong half of the screen.
        /// </summary>
        public static async Task<DeathScreenText> ReadAsync(
            double left, double top, double width, double height, IReadOnlyList<IntPtr>? exclude = null)
        {
            var path = await AiCompanion.GameScreenshot
                .CaptureRegionAsync(left, top, width, height, exclude)
                .ConfigureAwait(false);

            if (path == null) return new DeathScreenText(null, null, Array.Empty<string>(), null);

            var lines = await ReadLinesAsync(path).ConfigureAwait(false);
            var (killer, weapon) = Parse(lines);

            return new DeathScreenText(killer, weapon, lines, path);
        }

        /// <summary>Every line of text in one image, top to bottom.</summary>
        private static async Task<IReadOnlyList<string>> ReadLinesAsync(string path)
        {
            var engine = Engine();
            if (engine == null || !File.Exists(path)) return Array.Empty<string>();

            try
            {
                using var stream = File.OpenRead(path);

                var decoder = await Windows.Graphics.Imaging.BitmapDecoder
                    .CreateAsync(stream.AsRandomAccessStream());

                using var bitmap = await decoder.GetSoftwareBitmapAsync();
                var result = await engine.RecognizeAsync(bitmap);

                return result.Lines
                    .Select(line => string.Join(" ", line.Words.Select(w => w.Text)).Trim())
                    .Where(line => line.Length > 0)
                    .ToList();
            }
            catch
            {
                // A recogniser that is present but refuses an image is not worth an exception
                // on the way back from a button press.
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Picks the name and the weapon out of what was read.
        ///
        /// The death screen puts the killer's name first and what they used under it, so this
        /// takes them in that order. Lines short enough to be noise are dropped, and so is the
        /// survival box that shares the top of the screen — it is localised, so it is spotted by
        /// shape (a duration) rather than by the word in front of it.
        /// </summary>
        public static (string? Killer, string? Weapon) Parse(IReadOnlyList<string> lines)
        {
            var useful = lines
                .Select(Tidy)
                .Where(line => line.Length >= 2 && !LooksLikeDuration(line))
                .ToList();

            if (useful.Count == 0) return (null, null);

            return (useful[0], useful.Count > 1 ? useful[1] : null);
        }

        /// <summary>
        /// Strips what the recogniser adds around the edges of a crop.
        ///
        /// A band cut out of a screen has half-letters at its edges, and those come back as
        /// stray punctuation. A player's name can contain almost anything, so only the
        /// characters that are never part of one are taken off.
        /// </summary>
        private static string Tidy(string line) =>
            line.Trim().Trim('|', '/', '\\', '"', '\'', '.', ',', ';', ':', '—', '-', '_').Trim();

        /// <summary>
        /// Whether a line is a clock rather than a name.
        ///
        /// The "alive for" box next to the name is localised — in German it says something else
        /// entirely — so the words in it are no use. The duration beside them has the same shape
        /// in every language, and that is what this looks for.
        /// </summary>
        private static bool LooksLikeDuration(string line)
        {
            int digits = line.Count(char.IsDigit);
            if (digits == 0) return false;

            bool clock = line.Contains(':') && digits >= 3;
            bool units = System.Text.RegularExpressions.Regex.IsMatch(
                line, @"\d+\s*[smhd]\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Mostly digits, or shaped like 12:34, or "5m 20s". A name that merely contains a
            // number — and plenty do — keeps its place.
            return clock || units || digits > line.Length / 2;
        }

        private static Windows.Media.Ocr.OcrEngine? Engine()
        {
            try
            {
                // The user's own display language first, then anything installed. For a player's
                // name the language barely matters — no dictionary has it — but the script does,
                // and every recogniser Windows ships reads Latin letters.
                return Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()
                    ?? Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages
                        .Select(Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage)
                        .FirstOrDefault(e => e != null);
            }
            catch
            {
                // Speech and OCR are optional Windows components; asking for one that was never
                // installed throws rather than returning nothing.
                return null;
            }
        }
    }
}
