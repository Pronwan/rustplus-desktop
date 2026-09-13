using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace RustPlusDesk.Services.AiCompanion
{
    /// <summary>
    /// Windows' own speech recognition, used when the provider will not take audio at all.
    ///
    /// This is the worse path and the settings say so where the provider is chosen. It runs on
    /// the desktop recogniser that ships with Windows, which was built for dictation by one
    /// trained speaker in a quiet room — not for a headset mic over a firefight. It is here
    /// because the alternative for Claude is no spoken questions at all.
    /// </summary>
    public static class LocalTranscriber
    {
        /// <summary>
        /// Turns a recording into words. Empty when nothing was recognised, which for this
        /// recogniser is a perfectly ordinary outcome.
        /// </summary>
        /// <exception cref="AiRequestException">No recogniser is installed for any language.</exception>
        public static string Transcribe(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";

            var wav = WavTools.ToSpeechWav(path);

            try
            {
                if (WavTools.IsSilent(wav)) return "";

                using var engine = CreateEngine();
                engine.SetInputToWaveFile(wav);
                engine.LoadGrammar(new System.Speech.Recognition.DictationGrammar());

                var text = new StringBuilder();

                // One call per utterance, until the file runs out. RecognizeAsync would need a
                // wait handle and a completion event for no gain — nothing is listening live.
                while (true)
                {
                    System.Speech.Recognition.RecognitionResult? result;
                    try { result = engine.Recognize(); }
                    catch { break; }

                    if (result == null) break;

                    if (text.Length > 0) text.Append(" ");
                    text.Append(result.Text);
                }

                return text.ToString().Trim();
            }
            finally
            {
                if (wav != path && WavTools.IsTemporaryCopy(wav))
                {
                    try { File.Delete(wav); } catch { }
                }
            }
        }

        /// <summary>
        /// The language this will listen in, or null when Windows has no recogniser at all.
        ///
        /// Worth showing beside a transcript. A recogniser only knows the language it was
        /// built for, and given another one it does not fail — it returns the nearest words
        /// it does know, confidently. Someone reading "what did that player just say" needs
        /// to be able to see that the machine was listening for English.
        /// </summary>
        public static CultureInfo? RecognizerLanguage => PickRecognizer()?.Culture;

        /// <summary>
        /// The recogniser to use: the user's own language first.
        ///
        /// A recogniser for the wrong language produces confident nonsense rather than
        /// nothing, which is far harder to spot in an answer.
        /// </summary>
        private static System.Speech.Recognition.RecognizerInfo? PickRecognizer()
        {
            try
            {
                var installed = System.Speech.Recognition.SpeechRecognitionEngine.InstalledRecognizers();
                if (installed.Count == 0) return null;

                var exact = CultureInfo.CurrentUICulture.Name;
                var family = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

                return installed.FirstOrDefault(r => r.Culture.Name == exact)
                    ?? installed.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == family)
                    ?? installed.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "en")
                    ?? installed[0];
            }
            catch
            {
                // Speech is an optional Windows component and enumerating it can throw on a
                // machine where it was never installed.
                return null;
            }
        }

        private static System.Speech.Recognition.SpeechRecognitionEngine CreateEngine()
        {
            var pick = PickRecognizer();

            if (pick == null)
            {
                throw new AiRequestException(
                    "Windows has no speech recognition installed, so a spoken question cannot be " +
                    "turned into text for this provider. Add a speech language under Windows " +
                    "settings, or choose a provider that takes audio directly.");
            }

            return new System.Speech.Recognition.SpeechRecognitionEngine(pick);
        }
    }
}
