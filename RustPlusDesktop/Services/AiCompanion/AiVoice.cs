using System;
using System.Globalization;
using System.Linq;
using System.Speech.Synthesis;

namespace RustPlusDesk.Services.AiCompanion
{
    /// <summary>
    /// Reads the answer out loud, using the voice Windows already has.
    ///
    /// Windows' own voice rather than the provider's: the answer has to start playing while the
    /// player is looking at the game, and a round trip to fetch generated speech adds seconds to
    /// exactly the case the companion exists for. It is plainly a synthesiser, and that is the
    /// trade — see <see cref="AiProviders.HasVoice"/> for where a provider's own voice would go.
    /// </summary>
    public static class AiVoice
    {
        private static SpeechSynthesizer? _synth;
        private static bool _checked;
        private static bool _available;

        /// <summary>Whether this machine has any voice at all to read with.</summary>
        public static bool IsAvailable
        {
            get
            {
                if (_checked) return _available;
                _checked = true;

                try
                {
                    using var probe = new SpeechSynthesizer();
                    _available = probe.GetInstalledVoices().Any(v => v.Enabled);
                }
                catch
                {
                    _available = false;
                }

                return _available;
            }
        }

        /// <summary>
        /// Queues a piece of the answer.
        ///
        /// Queued rather than spoken over: the streaming path hands this one sentence at a time
        /// as they arrive, and each has to wait its turn or the answer comes out as overlapping
        /// halves.
        /// </summary>
        public static void Speak(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || !IsAvailable) return;

            try
            {
                _synth ??= Create();
                _synth.SpeakAsync(text);
            }
            catch
            {
                // A voice that has gone away mid-session is not worth taking the answer with it;
                // the panel still has the text.
            }
        }

        /// <summary>Cuts the voice off — a new question, or the panel being dismissed.</summary>
        public static void Stop()
        {
            try { _synth?.SpeakAsyncCancelAll(); } catch { }
        }

        private static SpeechSynthesizer Create()
        {
            var synth = new SpeechSynthesizer();
            synth.SetOutputToDefaultAudioDevice();

            var wanted = AiCompanionStore.Current.TtsLanguage;
            var culture = Resolve(wanted);

            if (culture != null)
            {
                // Only when a voice for it exists: SelectVoiceByHints falls back silently, and an
                // English voice reading German is worse than no choice at all.
                var match = synth.GetInstalledVoices(culture).FirstOrDefault(v => v.Enabled);
                if (match != null) synth.SelectVoice(match.VoiceInfo.Name);
            }

            return synth;
        }

        private static CultureInfo? Resolve(string? language)
        {
            try
            {
                return string.IsNullOrWhiteSpace(language)
                    ? CultureInfo.CurrentUICulture
                    : new CultureInfo(language);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Called when the voice settings change, so the next answer uses the new one.
        ///
        /// The synthesiser picks its voice when it is built, and it is built once and kept —
        /// rebuilding per answer costs a noticeable pause before the first word.
        /// </summary>
        public static void Reset()
        {
            try
            {
                _synth?.SpeakAsyncCancelAll();
                _synth?.Dispose();
            }
            catch { }

            _synth = null;
        }
    }
}
