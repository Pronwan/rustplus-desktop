using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        /// Where the row of boxes sits, as fractions of the screen.
        ///
        /// Measured off three real death screens rather than guessed. The killer's name box
        /// came out at 1113,65–1365,118 on a 1440p screen at 0.9 interface scale, at
        /// 1093,70–1372,130 on the same screen at 1.0, and at 836,45–1025,86 on a 1080p one.
        /// As fractions those are left 0.427–0.435, top 0.042–0.049, width 0.098–0.109 and
        /// height 0.037–0.042 — near enough identical, which is what makes fractions the right
        /// unit here and a pixel rectangle the wrong one.
        ///
        /// The band taken is wider than the name box on purpose: it reaches from the survival
        /// timer on the left to past the weapon on the right, because all three sit on one line
        /// and the parser below tells them apart by where the words are rather than by what
        /// they say. Room to spare costs nothing, and a longer name pushes the boxes outwards —
        /// a crop fitted tightly around one player's name cuts the next player's in half.
        /// </summary>
        public const double DefaultLeft = 0.180;
        public const double DefaultTop = 0.020;
        public const double DefaultWidth = 0.640;
        public const double DefaultHeight = 0.034;

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

            var words = await ReadWordsAsync(path).ConfigureAwait(false);
            var (killer, weapon) = Parse(words);

            // Rebuilt into rows for the preview in the settings, which is read by a person
            // checking the region rather than by the parser.
            var lines = words
                .GroupBy(w => Math.Round(w.MiddleY / 12))
                .OrderBy(row => row.Key)
                .Select(row => string.Join(" ", row.OrderBy(w => w.Left).Select(w => w.Text)))
                .ToList();

            return new DeathScreenText(killer, weapon, lines, path);
        }

        /// <summary>One recognised word, and where on the crop it was.</summary>
        private readonly record struct Word(string Text, double Left, double Right, double MiddleY)
        {
            public double Width => Right - Left;
        }

        /// <summary>
        /// Every word in the crop, with its position.
        ///
        /// Positions rather than lines, because the three things on this row — how long you
        /// survived, who killed you, and what with — are one line of text as far as a
        /// recogniser is concerned. "1m22s iris war iris Rock" cannot be split by reading it.
        /// It can be split by noticing the gaps.
        /// </summary>
        private static async Task<IReadOnlyList<Word>> ReadWordsAsync(string path)
        {
            var engine = Engine();
            if (engine == null || !File.Exists(path)) return Array.Empty<Word>();

            try
            {
                using var stream = File.OpenRead(path);

                var decoder = await Windows.Graphics.Imaging.BitmapDecoder
                    .CreateAsync(stream.AsRandomAccessStream());

                using var bitmap = await decoder.GetSoftwareBitmapAsync();
                var result = await engine.RecognizeAsync(bitmap);

                return result.Lines
                    .SelectMany(line => line.Words)
                    .Select(w => new Word(
                        w.Text.Trim(),
                        w.BoundingRect.Left,
                        w.BoundingRect.Left + w.BoundingRect.Width,
                        w.BoundingRect.Top + w.BoundingRect.Height / 2))
                    .Where(w => w.Text.Length > 0)
                    .OrderBy(w => w.Left)
                    .ToList();
            }
            catch
            {
                // A recogniser that is present but refuses an image is not worth an exception
                // on the way back from a button press.
                return Array.Empty<Word>();
            }
        }

        /// <summary>
        /// Picks the killer and the weapon out of the words and where they sat.
        ///
        /// Rust puts up to four boxes on this row: how long you were alive, who killed you,
        /// what with, and how far off they were. Their labels are localised, so nothing here
        /// reads them. What is not localised is the layout — groups of words with clear
        /// space between them, the name before the weapon,
        /// in that order — so the words are grouped by the gaps, the groups shaped like a
        /// duration or a distance are dropped, and what is left is the name and then the
        /// weapon.
        ///
        /// The bottom row only, for when the crop caught the labels above the boxes as well:
        /// those come out as their own row of words higher up.
        /// </summary>
        private static (string? Killer, string? Weapon) Parse(IReadOnlyList<Word> words)
        {
            if (words.Count == 0) return (null, null);

            // Rows first. Words of one row share a middle within a few pixels, and the labels
            // sit a row above the boxes — grouped in with them, they would be read as a name.
            double lineHeight = Math.Max(6, words.Average(w => w.Width) * 1.6);

            var row = words
                .GroupBy(w => Math.Round(w.MiddleY / lineHeight))
                .OrderByDescending(g => g.Key)
                .First()
                .OrderBy(w => w.Left)
                .ToList();

            // Then the gaps. Inside a name the words are a space apart; between the boxes they
            // are a good deal more. The threshold sits between the two and is measured in the
            // crop's own units rather than in pixels, because the crop's size follows the
            // screen's.
            double typical = row.Average(w => w.Width);
            double split = Math.Max(12, typical * 1.2);

            var groups = new List<List<Word>> { new() { row[0] } };

            for (int i = 1; i < row.Count; i++)
            {
                if (row[i].Left - row[i - 1].Right > split) groups.Add(new List<Word>());
                groups[^1].Add(row[i]);
            }

            var read = groups
                .Select(g => Tidy(string.Join(" ", g.Select(w => w.Text))))
                .Where(t => t.Length >= 2)
                .ToList();

            // The word DEAD is on this row too, out at the left, and a band wide enough for a
            // long name reaches it. It is localised, so it cannot be recognised by name — but
            // it is always left of the survival timer, and the timer can be recognised by its
            // shape. So everything up to and including the timer is dropped, and what follows
            // is the row proper. Without a timer in the crop there is nothing to anchor to and
            // the groups are taken as they come.
            // The duration only. Anchoring on either shape would, in a crop that starts right
            // of the timer, find the distance at the end instead and throw away the name and
            // the weapon in front of it.
            int timer = read.FindIndex(IsDuration);
            if (timer >= 0) read = read.Skip(timer + 1).ToList();

            // What is left can still hold the distance, which is the same kind of thing and
            // sits on the other side.
            var text = read.Where(t => !IsNotAName(t)).ToList();

            if (text.Count == 0) return (null, null);

            return (text[0], text.Count > 1 ? text[1] : null);
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
        /// Whether a group of words is one of the boxes that is never a name.
        ///
        /// Rust puts up to four boxes on this row and only one of them is the killer: how
        /// long you were alive, who killed you, what with, and how far away they were. Which
        /// of them are present depends on how you died — a bear brings no weapon, a fall
        /// brings no name at all — so they cannot be told apart by counting them. The labels
        /// above them are localised and no use either.
        ///
        /// What can be relied on is that a duration and a distance have shapes a name does
        /// not, so those two are recognised and dropped, and whatever is left is the name and
        /// then the weapon.
        /// </summary>
        private static bool IsNotAName(string text) => IsDuration(text) || IsDistance(text);

        /// <summary>
        /// 1m22s, 45s, 2h, 3d — one or more counts, each with its unit.
        ///
        /// Told apart from a distance because this one is an anchor and that one is not: the
        /// survival time is always the first box on the row, so anything before it belongs to
        /// the screen rather than to the row. The distance is last and anchors nothing.
        /// </summary>
        private static bool IsDuration(string text) => Regex.IsMatch(
            AsDigits(text), @"^\d+\s*[smhd](\s*\d+\s*[smhd])*$", RegexOptions.IgnoreCase);

        /// <summary>0.4m, 24m, 137.5m — how far away they were.</summary>
        private static bool IsDistance(string text) => Regex.IsMatch(
            AsDigits(text), @"^\d+([.,]\d+)?\s*m$", RegexOptions.IgnoreCase);

        /// <summary>
        /// Puts back the digits a recogniser turned into letters, for the shape tests only.
        ///
        /// This is what let a death at one minute and one second through as a killer called
        /// "lmls": every character of "1m1s" is a one, and a one is the digit most often read
        /// as an l or an I. The test that was meant to catch it gave up at the first step,
        /// because by then there were no digits left in it to find.
        ///
        /// Never applied to what is shown or saved — a player called lOl keeps their name.
        /// </summary>
        private static string AsDigits(string text) => text
            .Replace('l', '1').Replace('I', '1').Replace('|', '1').Replace('!', '1')
            .Replace('O', '0').Replace('o', '0')
            .Replace('S', '5').Replace('B', '8')
            .Trim();

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
