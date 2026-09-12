using System;
using System.Collections.Generic;
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
    /// GPT, in two steps: the recordings are transcribed first, then the text and the screenshot
    /// are asked about together.
    ///
    /// Two steps rather than one because the audio models and the vision models are not the same
    /// models. Transcribing first also produces something worth keeping on its own — the player
    /// can see what it heard, which is the only way to tell a bad answer from a misheard question.
    /// </summary>
    public sealed class OpenAiProvider : IAiProvider
    {
        // Overridable in the settings, because a retired model name is otherwise a dead end
        // until the next release.
        private static string ChatModel => AiProviders.Model(AiProviders.OpenAi);
        private const string TranscribeModel = "whisper-1";

        private const string ChatUrl = "https://api.openai.com/v1/chat/completions";
        private const string TranscribeUrl = "https://api.openai.com/v1/audio/transcriptions";

        public async Task<AiAnswerResult> AskAsync(
            AiQuestion question, string apiKey, Action<string>? onDelta, CancellationToken ct)
        {
            var spoken = await Transcribe(question.MicPath, apiKey, ct);
            var overheard = await Transcribe(question.GamePath, apiKey, ct);

            var prompt = new StringBuilder();
            prompt.Append(string.IsNullOrWhiteSpace(spoken) ? AiPrompt.NoSpeechFallback : spoken);

            if (!string.IsNullOrWhiteSpace(overheard))
                prompt.Append("\n\nHeard in the game at the same time: \"").Append(overheard).Append('"');

            var answer = await Chat(question, prompt.ToString(), apiKey, onDelta, ct);
            return new AiAnswerResult(answer, prompt.ToString());
        }

        /// <summary>Turns one track into words, or into nothing when there were none.</summary>
        private static async Task<string> Transcribe(string? path, string apiKey, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";

            var send = WavTools.ToSpeechWav(path);

            try
            {
                if (WavTools.IsSilent(send)) return "";

                using var form = new MultipartFormDataContent();
                using var audio = new ByteArrayContent(await File.ReadAllBytesAsync(send, ct));
                audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

                form.Add(audio, "file", Path.GetFileName(send));
                form.Add(new StringContent(TranscribeModel), "model");

                using var request = new HttpRequestMessage(HttpMethod.Post, TranscribeUrl) { Content = form };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                using var response = await AiHttp.Client.SendAsync(request, ct);
                var body = await AiHttp.ReadBody(response, ct);

                if (!response.IsSuccessStatusCode) throw AiHttp.Failure(response, body);

                using var doc = JsonDocument.Parse(body);
                return doc.RootElement.TryGetProperty("text", out var text) ? text.GetString() ?? "" : "";
            }
            finally
            {
                if (send != path && WavTools.IsTemporaryCopy(send))
                {
                    try { File.Delete(send); } catch { }
                }
            }
        }

        private static async Task<string> Chat(
            AiQuestion question, string prompt, string apiKey, Action<string>? onDelta, CancellationToken ct)
        {
            var content = new List<object> { new { type = "text", text = prompt } };

            if (question.ScreenshotPath != null && File.Exists(question.ScreenshotPath))
            {
                content.Add(new
                {
                    type = "image_url",
                    image_url = new
                    {
                        url = "data:image/jpeg;base64," + AiHttp.Base64(question.ScreenshotPath),

                        // High detail costs more tokens and reads the frame in 512-pixel tiles
                        // instead of one shrunken thumbnail. On a 4K screenshot that is the
                        // difference between seeing a deployable and seeing a smudge.
                        detail = "high",
                    },
                });
            }

            var payload = new
            {
                model = ChatModel,
                stream = onDelta != null,
                max_tokens = 700,
                messages = new object[]
                {
                    new { role = "system", content = AiPrompt.SystemMessage(question) },
                    new { role = "user", content },
                },
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, ChatUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await AiHttp.Client.SendAsync(
                request,
                onDelta != null ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                ct);

            if (!response.IsSuccessStatusCode)
                throw AiHttp.Failure(response, await AiHttp.ReadBody(response, ct));

            if (onDelta == null)
            {
                using var doc = JsonDocument.Parse(await AiHttp.ReadBody(response, ct));
                return doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content").GetString() ?? "";
            }

            var answer = new StringBuilder();

            await foreach (var chunk in AiHttp.ReadEvents(response, ct))
            {
                string? piece = null;

                try
                {
                    using var doc = JsonDocument.Parse(chunk);
                    var choices = doc.RootElement.GetProperty("choices");
                    if (choices.GetArrayLength() == 0) continue;

                    if (choices[0].TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("content", out var text))
                        piece = text.GetString();
                }
                catch
                {
                    // A half-written frame; the next one carries the rest.
                    continue;
                }

                if (string.IsNullOrEmpty(piece)) continue;

                answer.Append(piece);
                onDelta(piece);
            }

            return answer.ToString();
        }
    }
}
