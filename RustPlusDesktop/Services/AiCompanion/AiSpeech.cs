using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.AiCompanion
{
    /// <summary>
    /// The provider's own voice, where it has one.
    ///
    /// Windows' synthesiser is the fallback and sounds like one — which is fine for "cargo in
    /// four minutes" and poor for a paragraph. Both GPT and Gemini will read text back in a
    /// voice that is worth listening to, so where a key for one of them is already stored,
    /// that is what reads the answer.
    ///
    /// Everything here returns a playable WAV or MP3 as bytes, or null. Null always means "use
    /// Windows instead" — a voice that failed must never cost the user the answer.
    /// </summary>
    public static class AiSpeech
    {
        /// <summary>Whether this provider can read an answer aloud with a key already stored.</summary>
        public static bool IsAvailable(string provider) =>
            AiProviders.HasVoice(provider) && AiCompanionStore.HasKeyFor(provider);

        /// <summary>
        /// Why the provider's voice last refused, or null if it has not.
        ///
        /// Falling back to Windows is the right behaviour and the wrong silence: the answer
        /// still gets read, so a wrong model name or a spent quota sounds exactly like the
        /// setting having no effect. The settings panel shows this, which is the difference
        /// between a mystery and a one-line fix.
        /// </summary>
        public static string? LastError { get; private set; }

        /// <summary>
        /// Reads one piece of text. Null when the provider refused, or has no voice at all.
        /// </summary>
        public static async Task<byte[]?> SynthesizeAsync(string text, CancellationToken ct)
        {
            var provider = AiCompanionStore.Current.Provider;
            if (!IsAvailable(provider)) return null;

            var key = AiCompanionStore.ReadKeyFor(provider);
            if (string.IsNullOrEmpty(key)) return null;

            try
            {
                var audio = provider switch
                {
                    AiProviders.OpenAi => await OpenAi(text, key, ct),
                    AiProviders.Gemini => await Gemini(text, key, ct),
                    _ => null,
                };

                if (audio != null) LastError = null;
                return audio;
            }
            catch (OperationCanceledException)
            {
                return null;   // a new question, not a failure worth reporting
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return null;
            }
        }

        // ── OpenAI ──────────────────────────────────────────────────────────────

        private const string OpenAiVoice = "alloy";
        private const string OpenAiUrl = "https://api.openai.com/v1/audio/speech";

        private static async Task<byte[]?> OpenAi(string text, string key, CancellationToken ct)
        {
            var payload = new
            {
                model = AiProviders.VoiceModel(AiProviders.OpenAi),
                voice = OpenAiVoice,

                // WAV rather than MP3: it plays from a stream with no decoder involved, and the
                // file never leaves this machine, so its size does not matter.
                response_format = "wav",
                input = text,
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, OpenAiUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

            using var response = await AiHttp.Client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                LastError = Describe(response, await AiHttp.ReadBody(response, ct));
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync(ct);
        }

        // ── Gemini ──────────────────────────────────────────────────────────────

        private const string GeminiVoice = "Kore";

        private const string GeminiUrl =
            "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";

        /// <summary>
        /// Gemini answers with raw signed 16-bit PCM at 24 kHz, mono, and no header on it.
        /// </summary>
        private const int GeminiRate = 24000;

        private static async Task<byte[]?> Gemini(string text, string key, CancellationToken ct)
        {
            var payload = new
            {
                contents = new[] { new { parts = new[] { new { text } } } },
                generationConfig = new
                {
                    responseModalities = new[] { "AUDIO" },
                    speechConfig = new
                    {
                        voiceConfig = new
                        {
                            prebuiltVoiceConfig = new { voiceName = GeminiVoice },
                        },
                    },
                },
            };

            using var request = new HttpRequestMessage(
                HttpMethod.Post, string.Format(GeminiUrl, AiProviders.VoiceModel(AiProviders.Gemini)))
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("x-goog-api-key", key);

            using var response = await AiHttp.Client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                LastError = Describe(response, await AiHttp.ReadBody(response, ct));
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) ||
                candidates.GetArrayLength() == 0)
                return null;

            if (!candidates[0].TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts))
                return null;

            foreach (var part in parts.EnumerateArray())
            {
                if (!part.TryGetProperty("inlineData", out var inline) ||
                    !inline.TryGetProperty("data", out var data)) continue;

                var pcm = Convert.FromBase64String(data.GetString() ?? "");
                return pcm.Length == 0 ? null : WrapAsWav(pcm, GeminiRate);
            }

            return null;
        }

        /// <summary>The provider's own words where it gave any, with the status as a fallback.</summary>
        private static string Describe(HttpResponseMessage response, string body)
        {
            var failure = AiHttp.Failure(response, body);
            return failure.Message;
        }

        /// <summary>
        /// Puts a WAV header on raw PCM so the same player can handle both providers.
        ///
        /// Forty-four bytes of header, which is a great deal less machinery than teaching the
        /// playback side about a second audio format.
        /// </summary>
        private static byte[] WrapAsWav(byte[] pcm, int sampleRate, short channels = 1, short bits = 16)
        {
            using var buffer = new MemoryStream();
            using var writer = new BinaryWriter(buffer);

            int byteRate = sampleRate * channels * bits / 8;
            short blockAlign = (short)(channels * bits / 8);

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + pcm.Length);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);                 // chunk size for PCM
            writer.Write((short)1);           // format: PCM
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write(bits);

            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(pcm.Length);
            writer.Write(pcm);

            writer.Flush();
            return buffer.ToArray();
        }
    }
}
