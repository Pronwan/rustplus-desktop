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
    /// Gemini, in one request: it takes the recordings and the screenshot as they are.
    ///
    /// The one provider here that hears the question rather than reading a transcript of it,
    /// which is worth something — tone, hesitation and background noise all survive, and a name
    /// it half caught is not turned into the wrong word before it ever sees it.
    /// </summary>
    public sealed class GeminiProvider : IAiProvider
    {
        private static string Model => AiProviders.Model(AiProviders.Gemini);

        private const string Endpoint =
            "https://generativelanguage.googleapis.com/v1beta/models/{0}:{1}";

        public async Task<AiAnswerResult> AskAsync(
            AiQuestion question, string apiKey, Action<string>? onDelta, CancellationToken ct)
        {
            var parts = new List<object>();
            var temporary = new List<string>();

            try
            {
                // Said in words, because the request carries two audio tracks and nothing else
                // in it distinguishes them.
                var intro = new StringBuilder();
                intro.Append(question.MicPath != null
                    ? "The first audio track is the player asking you something. "
                    : "");
                if (question.GamePath != null)
                    intro.Append("The other audio track is the game's own sound at that moment. ");
                if (intro.Length == 0) intro.Append(AiPrompt.NoSpeechFallback);

                parts.Add(new { text = intro.ToString() });

                AddAudio(parts, temporary, question.MicPath);
                AddAudio(parts, temporary, question.GamePath);

                if (question.ScreenshotPath != null && File.Exists(question.ScreenshotPath))
                {
                    parts.Add(new
                    {
                        inline_data = new
                        {
                            mime_type = "image/jpeg",
                            data = AiHttp.Base64(question.ScreenshotPath),
                        },
                    });
                }

                var payload = new
                {
                    system_instruction = new { parts = new[] { new { text = AiPrompt.SystemMessage(question) } } },
                    contents = new[] { new { role = "user", parts } },
                    generationConfig = new { maxOutputTokens = 700 },
                };

                // alt=sse only on the streaming call: generateContent does not speak it, and
                // asking anyway gets a body this code cannot read.
                var url = onDelta != null
                    ? string.Format(Endpoint, Model, "streamGenerateContent") + "?alt=sse"
                    : string.Format(Endpoint, Model, "generateContent");

                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
                };

                // In a header rather than the query string: a URL ends up in logs and in crash
                // reports, and this one would carry the key with it.
                request.Headers.Add("x-goog-api-key", apiKey);

                using var response = await AiHttp.Client.SendAsync(
                    request,
                    onDelta != null ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                    ct);

                if (!response.IsSuccessStatusCode)
                    throw AiHttp.Failure(response, await AiHttp.ReadBody(response, ct));

                if (onDelta == null)
                    return new AiAnswerResult(TextOf(await AiHttp.ReadBody(response, ct)), null);

                var answer = new StringBuilder();

                await foreach (var chunk in AiHttp.ReadEvents(response, ct))
                {
                    var piece = TextOf(chunk);
                    if (string.IsNullOrEmpty(piece)) continue;

                    answer.Append(piece);
                    onDelta(piece);
                }

                return new AiAnswerResult(answer.ToString(), null);
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

        /// <summary>Pulls the text out of one response or one streamed frame — the shape is the same.</summary>
        private static string TextOf(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("candidates", out var candidates) ||
                    candidates.GetArrayLength() == 0)
                    return "";

                if (!candidates[0].TryGetProperty("content", out var content) ||
                    !content.TryGetProperty("parts", out var parts))
                    return "";

                var text = new StringBuilder();
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var value)) text.Append(value.GetString());
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
