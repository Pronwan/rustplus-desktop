using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace RustPlusDesk.Services.AiCompanion
{
    /// <summary>
    /// Reads the answer out loud: the provider's own voice where it has one, Windows' otherwise.
    ///
    /// Both paths go through one queue, because the streaming case hands this a sentence at a
    /// time as they arrive. Spoken as they land rather than queued, an answer comes out as
    /// overlapping halves — and the provider path makes that worse, since a network round trip
    /// means the pieces do not even finish in order.
    ///
    /// A failure anywhere here falls through to Windows and then to silence. Nothing in this
    /// file is allowed to cost the user the answer they already paid for.
    /// </summary>
    public static class AiVoice
    {
        /// <summary>Whether anything at all can read an answer aloud.</summary>
        public static bool IsAvailable =>
            AiSpeech.IsAvailable(AiCompanionStore.Current.Provider) || HasWindowsVoice;

        private static readonly BlockingCollection<string> Queue = new();
        private static CancellationTokenSource _cancel = new();
        private static Task? _worker;
        private static readonly object Gate = new();

        /// <summary>
        /// Queues a piece of the answer.
        ///
        /// Returns at once. The synthesis is a network call on the provider path and would
        /// otherwise stall the stream that is feeding it.
        /// </summary>
        public static void Speak(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            lock (Gate)
            {
                if (_cancel.IsCancellationRequested)
                {
                    _cancel.Dispose();
                    _cancel = new CancellationTokenSource();
                }

                var token = _cancel.Token;
                _worker ??= Task.Run(() => Pump(token));

                try { Queue.Add(text); }
                catch { /* the queue is finished; nothing more is being read */ }
            }
        }

        /// <summary>Cuts the voice off — a new question, or the panel being dismissed.</summary>
        public static void Stop()
        {
            lock (Gate)
            {
                try { _cancel.Cancel(); } catch { }

                // Drain what has not been spoken yet. The worker checks the token between
                // pieces, but a queue left full would be read out by the next question.
                while (Queue.TryTake(out _)) { }

                try { _synth?.SpeakAsyncCancelAll(); } catch { }

                _worker = null;
            }
        }

        /// <summary>
        /// Speaks one piece at a time, in order, until it is cancelled.
        ///
        /// Serial on purpose. The whole reason this queue exists is that two voices at once are
        /// neither of them audible.
        /// </summary>
        private static void Pump(CancellationToken ct)
        {
            try
            {
                foreach (var text in Queue.GetConsumingEnumerable(ct))
                {
                    if (ct.IsCancellationRequested) return;

                    byte[]? audio = null;

                    try { audio = AiSpeech.SynthesizeAsync(text, ct).GetAwaiter().GetResult(); }
                    catch { /* falls through to Windows below */ }

                    if (ct.IsCancellationRequested) return;

                    if (audio != null && Play(audio, ct)) continue;

                    SpeakWithWindows(text, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Stop() was called. Nothing to clean up that Stop has not already done.
            }
            catch
            {
                // A voice that has gone away mid-session is not worth taking the answer with it;
                // the panel still has the text.
            }
        }

        /// <summary>Plays one clip through to the end. False when it could not be played at all.</summary>
        private static bool Play(byte[] wav, CancellationToken ct)
        {
            try
            {
                using var stream = new MemoryStream(wav);
                using var reader = new WaveFileReader(stream);
                using var output = new WaveOutEvent();

                output.Init(reader);
                output.Play();

                while (output.PlaybackState == PlaybackState.Playing)
                {
                    if (ct.IsCancellationRequested)
                    {
                        output.Stop();
                        return true;
                    }

                    Thread.Sleep(40);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        // ── Windows' own voice ──────────────────────────────────────────────────

        private static SpeechSynthesizer? _synth;
        private static bool _checked;
        private static bool _hasVoice;

        private static bool HasWindowsVoice
        {
            get
            {
                if (_checked) return _hasVoice;
                _checked = true;

                try
                {
                    using var probe = new SpeechSynthesizer();
                    _hasVoice = probe.GetInstalledVoices().Any(v => v.Enabled);
                }
                catch
                {
                    _hasVoice = false;
                }

                return _hasVoice;
            }
        }

        private static void SpeakWithWindows(string text, CancellationToken ct)
        {
            if (!HasWindowsVoice || ct.IsCancellationRequested) return;

            try
            {
                _synth ??= CreateWindowsVoice();

                // Blocking, because this runs on the queue's own worker and the next piece must
                // not start until this one has finished.
                _synth.Speak(text);
            }
            catch
            {
                // Silence is the last fallback. The answer is on screen either way.
            }
        }

        private static SpeechSynthesizer CreateWindowsVoice()
        {
            var synth = new SpeechSynthesizer();
            synth.SetOutputToDefaultAudioDevice();

            var culture = Resolve(AiCompanionStore.Current.TtsLanguage);

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
            Stop();

            try
            {
                _synth?.Dispose();
            }
            catch { }

            _synth = null;
        }
    }
}
