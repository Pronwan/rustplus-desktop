using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.AiCompanion
{
    /// <summary>
    /// Turning a recording into words with the configured model instead of with Windows.
    ///
    /// Windows' own recogniser only knows the languages it has been given a recogniser for —
    /// usually just the display language — and handed anything else it returns the nearest words
    /// it does know rather than nothing, which reads like a result and is not one. A model that
    /// takes audio works out the language itself, which is the whole point when the question is
    /// "what did that player just say".
    ///
    /// It is not the default anywhere, because it spends the user's own API credit. Everything
    /// here is reached from a switch somebody turned on.
    /// </summary>
    public static class AiTranscription
    {
        /// <summary>
        /// Whether this can be used at all right now: a provider that takes audio, with a key.
        ///
        /// Both halves matter. Claude takes no audio however good the key is, and a provider
        /// that does is no use without one — so the switch that offers this is disabled unless
        /// this is true, rather than failing at the moment somebody presses record.
        /// </summary>
        public static bool Available =>
            AiProviders.AcceptsAudio(AiCompanionStore.Current.Provider) && AiCompanionStore.HasKey;

        /// <summary>
        /// Writes down what was said on either track, as it was said.
        ///
        /// Deliberately not a translation: the caller decides what to do with the words, and a
        /// model asked to do both at once tends to return one tidied sentence instead of what it
        /// actually heard.
        /// </summary>
        /// <exception cref="AiRequestException">The provider refused, or takes no audio.</exception>
        public static async Task<string> TranscribeAsync(string? micPath, string? gamePath, CancellationToken ct)
        {
            var provider = AiCompanionStore.Current.Provider;
            var key = AiCompanionStore.ReadKey();

            if (string.IsNullOrEmpty(key))
            {
                throw new AiRequestException(
                    "No key is stored for the chosen provider, so the recording cannot be sent anywhere.");
            }

            return provider switch
            {
                AiProviders.OpenAi => await ViaWhisper(micPath, gamePath, key!, ct),
                AiProviders.Gemini => await ViaGemini(micPath, gamePath, key!, ct),
                _ => throw new AiRequestException(
                    $"{AiProviders.DisplayName(provider)} does not take audio, so it cannot write down a recording."),
            };
        }

        /// <summary>Both tracks through OpenAI's transcription endpoint, which detects the language.</summary>
        private static async Task<string> ViaWhisper(string? micPath, string? gamePath, string key, CancellationToken ct)
        {
            var parts = new List<string>();

            foreach (var path in new[] { gamePath, micPath })
            {
                var text = await OpenAiProvider.Transcribe(path, key, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(text)) parts.Add(text.Trim());
            }

            return string.Join(" ", parts).Trim();
        }

        /// <summary>
        /// Both tracks in one Gemini request, asked for the words and nothing else.
        ///
        /// One request rather than two: Gemini charges by what it is sent either way, and the
        /// two tracks are the same moment heard from two places — sending them together lets it
        /// use one to make sense of the other.
        /// </summary>
        private static async Task<string> ViaGemini(string? micPath, string? gamePath, string key, CancellationToken ct)
        {
            var parts = new List<object>
            {
                new
                {
                    text = "Write down exactly what is said in these recordings, in the language it " +
                           "is spoken in. Do not translate it, do not explain it, do not add anything " +
                           "of your own. If nothing intelligible is said, answer with nothing at all.",
                },
            };

            var temporary = new List<string>();

            try
            {
                AddAudio(parts, temporary, gamePath);
                AddAudio(parts, temporary, micPath);

                // Only the instruction survived, so there was nothing audible to send.
                if (parts.Count == 1) return "";

                var payload = new
                {
                    contents = new[] { new { role = "user", parts } },
                    generationConfig = new { maxOutputTokens = 400 },
                };

                var url = string.Format(
                    "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent",
                    AiProviders.Model(AiProviders.Gemini));

                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
                };

                // In a header rather than the query string: a URL ends up in logs and in crash
                // reports, and this one would carry the key with it.
                request.Headers.Add("x-goog-api-key", key);

                using var response = await AiHttp.Client.SendAsync(request, ct).ConfigureAwait(false);
                var body = await AiHttp.ReadBody(response, ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode) throw AiHttp.Failure(response, body);

                return TextOf(body).Trim();
            }
            finally
            {
                foreach (var path in temporary)
                {
                    try { File.Delete(path); } catch { }
                }
            }
        }

        private static void AddAudio(List<object> parts, List<string> temporary, string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            var send = WavTools.ToSpeechWav(path);
            if (send != path && WavTools.IsTemporaryCopy(send)) temporary.Add(send);

            if (WavTools.IsSilent(send)) return;

            parts.Add(new { inline_data = new { mime_type = "audio/wav", data = AiHttp.Base64(send) } });
        }

        private static string TextOf(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);

                if (!document.RootElement.TryGetProperty("candidates", out var candidates) ||
                    candidates.GetArrayLength() == 0)
                {
                    return "";
                }

                var text = new StringBuilder();

                foreach (var part in candidates[0].GetProperty("content").GetProperty("parts").EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var piece)) text.Append(piece.GetString());
                }

                return text.ToString();
            }
            catch
            {
                return "";
            }
        }
    }
}
