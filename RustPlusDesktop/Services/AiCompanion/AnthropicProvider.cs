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
    /// Claude. It reads and it looks, but it does not listen, so the recording is turned into
    /// text on this machine before anything is sent.
    ///
    /// That transcription is the weak link here, not the model — see <see cref="LocalTranscriber"/>.
    /// The request says so too, so a mis-heard word is treated as a mis-hearing rather than
    /// answered literally.
    /// </summary>
    public sealed class AnthropicProvider : IAiProvider
    {
        private static string Model => AiProviders.Model(AiProviders.Anthropic);
        private const string Url = "https://api.anthropic.com/v1/messages";
        private const string Version = "2023-06-01";

        public async Task<AiAnswerResult> AskAsync(
            AiQuestion question, string apiKey, Action<string>? onDelta, CancellationToken ct)
        {
            // On a worker: the recogniser chews through the file synchronously and would hold
            // the UI thread for as long as the recording runs.
            var spoken = await Task.Run(() => LocalTranscriber.Transcribe(question.MicPath), ct);
            var overheard = question.GamePath == null
                ? ""
                : await Task.Run(() => LocalTranscriber.Transcribe(question.GamePath), ct);

            var prompt = new StringBuilder();
            prompt.Append(string.IsNullOrWhiteSpace(spoken) ? AiPrompt.NoSpeechFallback : spoken);

            if (!string.IsNullOrWhiteSpace(overheard))
                prompt.Append("\n\nHeard in the game at the same time: \"").Append(overheard).Append("\"");

            if (!string.IsNullOrWhiteSpace(spoken))
            {
                prompt.Append(
                    "\n\n(That question was transcribed by Windows' own speech recognition and may " +
                    "contain mis-hearings. If a word makes no sense in Rust, say what you think it " +
                    "was meant to be and answer that.)");
            }

            var content = new List<object>();

            // The image first: a question that refers to "this" wants the picture already in
            // view by the time it is read.
            if (question.ScreenshotPath != null && File.Exists(question.ScreenshotPath))
            {
                content.Add(new
                {
                    type = "image",
                    source = new
                    {
                        type = "base64",
                        media_type = "image/jpeg",
                        data = AiHttp.Base64(question.ScreenshotPath),
                    },
                });
            }

            content.Add(new { type = "text", text = prompt.ToString() });

            var payload = new
            {
                model = Model,
                max_tokens = 700,
                stream = onDelta != null,
                system = AiPrompt.SystemMessage(question),
                messages = new object[] { new { role = "user", content } },
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, Url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", Version);

            using var response = await AiHttp.Client.SendAsync(
                request,
                onDelta != null ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                ct);

            if (!response.IsSuccessStatusCode)
                throw AiHttp.Failure(response, await AiHttp.ReadBody(response, ct));

            if (onDelta == null)
            {
                using var doc = JsonDocument.Parse(await AiHttp.ReadBody(response, ct));

                var whole = new StringBuilder();
                foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
                {
                    if (block.TryGetProperty("text", out var text)) whole.Append(text.GetString());
                }

                return new AiAnswerResult(whole.ToString(), prompt.ToString());
            }

            var answer = new StringBuilder();

            await foreach (var chunk in AiHttp.ReadEvents(response, ct))
            {
                string? piece = null;

                try
                {
                    using var doc = JsonDocument.Parse(chunk);
                    var root = doc.RootElement;

                    // Only content_block_delta carries text; the other event types are the
                    // envelope around it.
                    if (!root.TryGetProperty("type", out var type) ||
                        type.GetString() != "content_block_delta") continue;

                    if (root.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("text", out var text))
                        piece = text.GetString();
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrEmpty(piece)) continue;

                answer.Append(piece);
                onDelta(piece);
            }

            return new AiAnswerResult(answer.ToString(), prompt.ToString());
        }
    }
}
