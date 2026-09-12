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
    /// Two stages rather than one. Fetching a clip and playing it are both slow, and done in
    /// turn every gap between sentences contains a full round trip to the provider — so one
    /// worker fetches ahead while another plays, and the pause between sentences is only the
    /// pause a person would leave.
    ///
    /// Which voice is reading is decided once per answer and then held. It used to be decided
    /// per clip, which meant a single rate-limited request in the middle of an answer switched
    /// voices mid-sentence — the answer starting in the provider's voice and finishing in
    /// Windows'.
    ///
    /// A failure anywhere falls through to Windows and then to silence. Nothing here is allowed
    /// to cost the user the answer they already paid for.
    /// </summary>
    public static class AiVoice
    {
        /// <summary>Whether anything at all can read an answer aloud.</summary>
        public static bool IsAvailable =>
            AiSpeech.IsAvailable(AiCompanionStore.Current.Provider) || HasWindowsVoice;

        private static readonly object Gate = new();

        private static BlockingCollection<string>? _toSpeak;
        private static BlockingCollection<Clip>? _toPlay;
        private static CancellationTokenSource? _cancel;

        /// <summary>A piece of the answer, fetched and waiting its turn.</summary>
        private sealed record Clip(string Text, byte[]? Audio);

        /// <summary>
        /// Which voice this answer is being read in.
        ///
        /// Null until the first clip settles it. Held for the rest of the answer so one refused
        /// request cannot change voices halfway through.
        /// </summary>
        private static bool? _useProviderVoice;

        /// <summary>
        /// Queues a piece of the answer.
        ///
        /// Returns at once: fetching is a network call on the provider path and would otherwise
        /// stall the stream that is feeding it.
        /// </summary>
        public static void Speak(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            lock (Gate)
            {
                if (_toSpeak == null || _cancel == null || _cancel.IsCancellationRequested) StartWorkers();

                try { _toSpeak!.Add(text); }
                catch { /* the queue was torn down between the check and here */ }
            }
        }

        private static void StartWorkers()
        {
            _cancel?.Dispose();
            _cancel = new CancellationTokenSource();
            var token = _cancel.Token;

            _toSpeak = new BlockingCollection<string>();

            // Two deep: enough that the next clip is always ready when the current one ends,
            // small enough that cancelling does not leave half an answer already paid for.
            _toPlay = new BlockingCollection<Clip>(2);

            var toSpeak = _toSpeak;
            var toPlay = _toPlay;

            _useProviderVoice = null;

            Task.Run(() => Fetch(toSpeak, toPlay, token), CancellationToken.None);
            Task.Run(() => PlayLoop(toPlay, token), CancellationToken.None);
        }

        /// <summary>Cuts the voice off — a new question, or the panel being dismissed.</summary>
        public static void Stop()
        {
            lock (Gate)
            {
                try { _cancel?.Cancel(); } catch { }

                // Drain both stages. A queue left full would be read out by the next question.
                Drain(_toSpeak);
                Drain(_toPlay);

                try { _toSpeak?.CompleteAdding(); } catch { }
                try { _toPlay?.CompleteAdding(); } catch { }

                _toSpeak = null;
                _toPlay = null;
                _useProviderVoice = null;

                try { _synth?.SpeakAsyncCancelAll(); } catch { }
            }
        }

        private static void Drain<T>(BlockingCollection<T>? queue)
        {
            if (queue == null) return;
            while (queue.TryTake(out _)) { }
        }

        /// <summary>
        /// Turns each piece of text into a clip, one ahead of the one being played.
        /// </summary>
        private static void Fetch(
            BlockingCollection<string> toSpeak, BlockingCollection<Clip> toPlay, CancellationToken ct)
        {
            try
            {
                foreach (var text in toSpeak.GetConsumingEnumerable(ct))
                {
                    if (ct.IsCancellationRequested) return;

                    byte[]? audio = null;

                    // Only asked for while this answer is being read in the provider's voice.
                    // Once Windows has taken over, asking again would switch back mid-answer.
                    if (_useProviderVoice != false)
                    {
                        audio = Synthesize(text, ct);

                        // One retry before giving the answer away to Windows: the usual reason
                        // for a refusal is a per-minute limit, and the next clip is a second or
                        // two later anyway.
                        if (audio == null && _useProviderVoice == true && !ct.IsCancellationRequested)
                        {
                            Thread.Sleep(400);
                            audio = Synthesize(text, ct);
                        }

                        _useProviderVoice = audio != null;
                    }

                    if (ct.IsCancellationRequested) return;

                    try { toPlay.Add(new Clip(text, audio), ct); }
                    catch { return; }
                }

                toPlay.CompleteAdding();
            }
            catch (OperationCanceledException) { }
            catch
            {
                // A voice that has gone away mid-session is not worth taking the answer with it;
                // the panel still has the text.
            }
        }

        private static byte[]? Synthesize(string text, CancellationToken ct)
        {
            try { return AiSpeech.SynthesizeAsync(text, ct).GetAwaiter().GetResult(); }
            catch { return null; }
        }

        /// <summary>
        /// Plays the clips in order, one at a time.
        ///
        /// Serial on purpose. The whole reason these queues exist is that two voices at once are
        /// neither of them audible.
        /// </summary>
        private static void PlayLoop(BlockingCollection<Clip> toPlay, CancellationToken ct)
        {
            try
            {
                foreach (var clip in toPlay.GetConsumingEnumerable(ct))
                {
                    if (ct.IsCancellationRequested) return;

                    if (clip.Audio != null && Play(clip.Audio, ct)) continue;

                    SpeakWithWindows(clip.Text, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
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
            catch (Exception ex)
            {
                // Reported rather than swallowed: this ends in the Windows voice just like a
                // refused request, and used to be the one failure with nothing to show for it.
                AiSpeech.ReportPlaybackFailure("The audio came back but could not be played: " + ex.Message);
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

                // Blocking, because this runs on the playing worker and the next clip must not
                // start until this one has finished.
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

            try { _synth?.Dispose(); }
            catch { }

            _synth = null;
        }
    }
}
