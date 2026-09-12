using System;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.AiCompanion
{
    public enum AiAnswerState
    {
        /// <summary>Nothing asked, or the last answer dismissed.</summary>
        None,

        /// <summary>Sent, nothing back yet. On the slow path this includes transcribing.</summary>
        Sending,

        /// <summary>Words are arriving.</summary>
        Streaming,

        Answered,
        Failed,
    }

    /// <summary>
    /// One question at a time, from the recording to the answer.
    ///
    /// It holds the answer rather than handing it to a caller because more than one thing wants
    /// it — the panel under the tile shows it, the voice reads it, and the tile itself only
    /// needs to know whether anything is happening. All three can then ask, and none of them has
    /// to own it.
    /// </summary>
    public sealed class AiCompanionService
    {
        public static AiCompanionService Instance { get; } = new();

        private AiCompanionService() { }

        public AiAnswerState State { get; private set; } = AiAnswerState.None;

        /// <summary>The answer so far. Grows while streaming, complete once answered.</summary>
        public string Answer { get; private set; } = "";

        /// <summary>Why it failed, in a sentence meant to be read on the overlay.</summary>
        public string? Error { get; private set; }

        /// <summary>What the provider was asked, where the recording was transcribed first.</summary>
        public string? Transcript { get; private set; }

        /// <summary>
        /// The screenshot waiting to go with the next question.
        ///
        /// Owned here rather than by the tile so that it survives a rebuild of the dock, and so
        /// that sending, discarding and replacing it are all one decision in one place.
        /// </summary>
        public string? Screenshot { get; private set; }

        public string StatusTitle { get; private set; } = "";
        public string StatusText { get; private set; } = "";

        public void SetStatus(string title, string text)
        {
            StatusTitle = title;
            StatusText = text;
            Raise();
        }

        public bool IsBusy => State is AiAnswerState.Sending or AiAnswerState.Streaming;

        /// <summary>Fires on whatever thread the change happened on. Marshal before touching UI.</summary>
        public event Action? Changed;

        private CancellationTokenSource? _cancel;

        public void AttachScreenshot(string? path)
        {
            if (!string.IsNullOrEmpty(Screenshot) && Screenshot != path) DeleteScreenshot();

            Screenshot = path;
            Raise();
        }

        private void DeleteScreenshot()
        {
            GameScreenshot.Delete(Screenshot);
            Screenshot = null;
        }

        /// <summary>
        /// Sends the recording and whatever is attached to it.
        ///
        /// The recording is thrown away as soon as it is on its way. Keeping it would mean a
        /// folder of everything the player has ever said building up in the temp directory, and
        /// nothing in the app would ever ask for it again.
        /// </summary>
        public async Task AskAsync(string? context)
        {
            if (IsBusy) return;

            var recorder = AiRecorder.Instance;
            var mic = recorder.MicPath;
            var game = recorder.GamePath;
            var shot = Screenshot;

            // Nothing to ask about. Not an error — it is what an accidental press looks like.
            if (mic == null && game == null && shot == null) return;

            var key = AiCompanionStore.ReadKey();
            if (string.IsNullOrEmpty(key))
            {
                Fail("No key is stored for this provider. Add one under Connected Services.");
                return;
            }

            var settings = AiCompanionStore.Current;

            var question = new AiQuestion
            {
                MicPath = mic,
                GamePath = game,
                ScreenshotPath = shot,
                Zoom = shot == null ? 1.0 : settings.ScreenshotZoom,
                WantsSpokenAnswer = settings.AudioAnswers && AiVoice.IsAvailable,
                Language = AnswerLanguageName(settings.AnswerLanguage),
                MatchQuestionLanguage = settings.AnswerLanguage == AnswerLanguages.MatchQuestion,
                Context = context,
            };

            var record = new AiExchange
            {
                Provider = settings.Provider,
                Model = AiProviders.Model(settings.Provider),
                HadScreenshot = shot != null,
                HadGameAudio = game != null,
            };

            bool speak = settings.AudioAnswers && AiVoice.IsAvailable;

            // Speaking as the answer is written is the supporter half of the setting. The
            // rest of it — speaking at all — is not gated.
            bool speakLive = speak && settings.StreamingVoice && Auth.SupabaseAuthManager.IsPremium;

            // Streamed only when something is watching the words land, whether that is the
            // panel reading them or the voice speaking them. Asking for a stream nobody
            // follows costs a longer connection for the same answer.
            //
            // Never for a question the provider can answer in speech in one call: that answer
            // arrives whole, and asking for it in pieces would give up the round trip the
            // shortcut exists to save. The conditions are OpenAiProvider's, mirrored — getting
            // this wrong costs a stream, not an answer, and the alternative is asking the
            // provider what it is about to do.
            bool oneCall = settings.Provider == AiProviders.OpenAi
                && speak
                && shot == null
                && game == null
                && mic != null;

            bool stream = (settings.TextAnswers || speakLive) && !oneCall;

            // What has been handed to the voice already, so the rest can be flushed at the end.
            int spokenUpTo = 0;

            Answer = "";
            Error = null;
            Transcript = null;
            State = AiAnswerState.Sending;
            SetStatus("Thinking…", $"Connecting to {AiProviders.DisplayName(settings.Provider)}…");
            _cancel = new CancellationTokenSource();

            AiLog.Info($"Sending question to {settings.Provider} ({AiProviders.Model(settings.Provider)}) | Mic: {(mic != null ? "Yes" : "No")}, Screen: {(shot != null ? "Yes" : "No")}, GameAudio: {(game != null ? "Yes" : "No")}, Language: {question.Language}");

            // A question asked while the last answer is still being read out loud replaces
            // it; two answers over each other are neither of them audible.
            AiVoice.Stop();

            Raise();

            try
            {
                var provider = AiProviderFactory.For(settings.Provider);

                Action<string>? onDelta = stream
                    ? piece =>
                      {
                          Answer += piece;
                          State = AiAnswerState.Streaming;
                          SetStatus("Answering…", "");

                          // A sentence at a time, never a fragment: the synthesiser reads
                          // half a clause as a statement and the intonation comes out wrong.
                          //
                          // The first one goes as soon as it is complete, however short,
                          // because nothing is being heard until it does. After that they are
                          // gathered into longer pieces — every piece is a round trip to the
                          // provider and a request against its per-minute limit, and by then
                          // there is already something playing to cover the wait.
                          if (speakLive)
                          {
                              spokenUpTo += SpeakSentences(
                                  Answer.Substring(spokenUpTo),
                                  spokenUpTo == 0 ? 0 : LaterChunk);
                          }

                          Raise();
                      }
                    : null;

                // On a worker, all of it.
                //
                // A provider does real work before its first await — building the prompt,
                // base64-ing a four-megapixel screenshot — and all of that ran on the UI
                // thread, where it is at best a stutter and at worst a freeze. Off-thread it
                // cannot be either. The deltas then arrive off-thread too, which is why every
                // listener marshals before touching a control.
                var token = _cancel.Token;
                var result = await Task.Run(
                    () => provider.AskAsync(question, key, onDelta, token), token);

                Answer = string.IsNullOrWhiteSpace(result.Answer)
                    ? "The provider returned an empty answer."
                    : result.Answer.Trim();

                Transcript = result.Transcript;

                record.Answer = Answer;
                record.Transcript = Transcript;
                AiHistory.Add(record);

                State = AiAnswerState.Answered;
                SetStatus("Answer", "");
                AiLog.Info($"Response received from {settings.Provider}: {Answer}");

                if (result.Audio != null)
                {
                    // Answered in the provider's own voice in the same call. Nothing to
                    // synthesise, and nothing was streamed, so the whole clip goes at once.
                    if (speak) AiVoice.SpeakClip(result.Audio, Answer);
                }

                // Whatever the sentence flush did not reach — the last sentence usually has
                // no terminator until the very end, and often no terminator at all.
                else if (speakLive && spokenUpTo < Answer.Length) AiVoice.Speak(Answer.Substring(spokenUpTo));
                else if (speak && !speakLive) AiVoice.Speak(Answer);

                Raise();
            }
            catch (OperationCanceledException)
            {
                State = AiAnswerState.None;
                Answer = "";
                SetStatus("", "");
                AiLog.Info($"Request to {settings.Provider} cancelled by user.");
                Raise();
            }
            catch (AiRequestException ex)
            {
                record.Error = ex.Message;
                AiHistory.Add(record);
                SetStatus("Failed", ex.Message);
                AiLog.Info($"Request to {settings.Provider} failed: {ex.Message}");
                Fail(ex.Message);
            }
            catch (Exception ex)
            {
                record.Error = Readable(ex);
                AiHistory.Add(record);
                SetStatus("Failed", record.Error);
                AiLog.Info($"Request to {settings.Provider} error: {record.Error}");
                Fail(record.Error);
            }
            finally
            {
                _cancel?.Dispose();
                _cancel = null;

                // Whether it worked or not: the recording has been sent or has failed to send,
                // and either way asking again means recording again.
                recorder.Discard();
                DeleteScreenshot();
                Raise();
            }
        }

        /// <summary>
        /// The language to ask for, as an English name.
        ///
        /// For "match the question" this is the fallback rather than the instruction — the
        /// app's own language, which is the best guess for someone whose recording had nothing
        /// recognisable in it.
        /// </summary>
        private static string AnswerLanguageName(string setting) => setting switch
        {
            AnswerLanguages.English => "English",
            AnswerLanguages.AppLanguage or AnswerLanguages.MatchQuestion => AiPrompt.CurrentLanguage(),
            _ => AiPrompt.LanguageNamed(setting),
        };

        /// <summary>
        /// Asks a typed question and hands back the answer, touching none of the state above.
        ///
        /// The chat command uses this. It must not blank the panel under the tile, must not be
        /// refused because a spoken question is in flight, and must not leave its answer behind
        /// as the thing the panel is showing — a teammate asking about sulfur should be
        /// invisible to whoever is wearing the overlay.
        ///
        /// It goes through the same settings as everything else: same provider, same model,
        /// same game data, same key. That is the whole point of the permission that gates it.
        /// </summary>
        /// <returns>The answer, or null with <paramref name="error"/> set.</returns>
        public static async Task<string?> AskTextAsync(
            string question, string? context, int maxWords, CancellationToken ct = default)
        {
            var key = AiCompanionStore.ReadKey();
            if (string.IsNullOrEmpty(key)) return null;

            var settings = AiCompanionStore.Current;

            AiLog.Info($"Sending text question to {settings.Provider} ({AiProviders.Model(settings.Provider)}): \"{question}\"");

            var record = new AiExchange
            {
                Provider = settings.Provider,
                Model = AiProviders.Model(settings.Provider),
                Transcript = question,
            };

            try
            {
                var provider = AiProviderFactory.For(settings.Provider);

                var asked = new AiQuestion
                {
                    Text = question,
                    MaxWords = maxWords,
                    Language = AnswerLanguageName(settings.AnswerLanguage),
                    MatchQuestionLanguage = settings.AnswerLanguage == AnswerLanguages.MatchQuestion,
                    Context = context,
                };

                // Never streamed: nothing is watching it arrive, and the answer cannot be sent
                // to chat until it is whole anyway.
                var result = await Task.Run(() => provider.AskAsync(asked, key, null, ct), ct);

                record.Answer = result.Answer.Trim();
                AiHistory.Add(record);
                AiLog.Info($"Response received from {settings.Provider}: {record.Answer}");

                return string.IsNullOrWhiteSpace(result.Answer) ? null : result.Answer.Trim();
            }
            catch (OperationCanceledException)
            {
                AiLog.Info($"Text request to {settings.Provider} cancelled.");
                return null;
            }
            catch (AiRequestException ex)
            {
                record.Error = ex.Message;
                AiHistory.Add(record);
                AiLog.Info($"Text request to {settings.Provider} failed: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                record.Error = Readable(ex);
                AiHistory.Add(record);
                AiLog.Info($"Text request to {settings.Provider} error: {record.Error}");
                return null;
            }
        }

        /// <summary>A network failure in the terms the player can do something about.</summary>
        private static string Readable(Exception ex) => ex switch
        {
            TaskCanceledException => "The provider took too long to answer.",
            System.Net.Http.HttpRequestException =>
                "Could not reach the provider. Check the connection and try again.",
            _ => ex.Message,
        };

        public void Cancel()
        {
            try { _cancel?.Cancel(); } catch { }
            AiVoice.Stop();
        }

        /// <summary>
        /// Roughly two sentences. Long enough that a long answer is not fifty separate
        /// requests, short enough that the voice never runs dry waiting for the next one.
        /// </summary>
        private const int LaterChunk = 120;

        /// <summary>
        /// Hands every complete sentence in <paramref name="pending"/> to the voice, and
        /// returns how many characters of it were taken. Nothing is taken until there are at
        /// least <paramref name="minimum"/> characters of finished sentences to take.
        /// </summary>
        private static int SpeakSentences(string pending, int minimum)
        {
            int cut = -1;

            for (int i = 0; i < pending.Length; i++)
            {
                if (pending[i] is not ('.' or '!' or '?' or '\n')) continue;

                // A terminator with nothing after it yet may still be the middle of a number
                // or an abbreviation, so it waits for the next chunk to settle it.
                if (i + 1 < pending.Length && !char.IsWhiteSpace(pending[i + 1])) continue;

                cut = i + 1;
            }

            if (cut <= 0 || cut < minimum) return 0;

            AiVoice.Speak(pending.Substring(0, cut));
            return cut;
        }

        /// <summary>Dismisses the answer and closes the panel under the tile.</summary>
        public void ClearAnswer()
        {
            if (IsBusy) Cancel();
            AiVoice.Stop();

            Answer = "";
            Error = null;
            State = AiAnswerState.None;
            Raise();
        }

        private void Fail(string message)
        {
            Error = message;
            State = AiAnswerState.Failed;
            Raise();
        }

        private void Raise()
        {
            try { Changed?.Invoke(); } catch { }
        }

    }
}
