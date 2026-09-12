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
    /// OpenRouter: one key for many models behind an OpenAI-compatible chat endpoint.
    ///
    /// Recordings are transcribed on this machine first (see <see cref="LocalTranscriber"/>),
    /// like the Anthropic path — OpenRouter is a chat gateway, not an audio endpoint, so there
    /// is no transcription step to call with the same key. Typed questions skip that entirely.
    /// Spoken answers fall back to Windows (see <see cref="AiProviders.HasVoice"/>).
    /// </summary>
    public sealed class OpenRouterProvider : IAiProvider
    {
        private static string ChatModel => AiProviders.Model(AiProviders.OpenRouter);

        private const string ChatUrl = "https://openrouter.ai/api/v1/chat/completions";

        /// <summary>Shown to OpenRouter as the requesting app. Identifies usage, not the user.</summary>
        private const string AppTitle = "RustPlusDesk AI Companion";

        public async Task<AiAnswerResult> AskAsync(
            AiQuestion question, string apiKey, Action<string>? onDelta, CancellationToken ct)
        {
            bool typed = !string.IsNullOrWhiteSpace(question.Text);

            // On a worker: the recogniser chews through the file synchronously and would hold
            // the calling thread for as long as the recording runs. Skipped entirely for a
            // typed question, which has nothing to transcribe.
            var spoken = typed
                ? question.Text!
                : await Task.Run(() => LocalTranscriber.Transcribe(question.MicPath), ct);

            var overheard = typed || question.GamePath == null
                ? ""
                : await Task.Run(() => LocalTranscriber.Transcribe(question.GamePath), ct);

            var prompt = new StringBuilder();
            prompt.Append(string.IsNullOrWhiteSpace(spoken) ? AiPrompt.NoSpeechFallback : spoken);

            if (!string.IsNullOrWhiteSpace(overheard))
                prompt.Append("\n\nHeard in the game at the same time: \"").Append(overheard).Append('"');

            if (!typed && !string.IsNullOrWhiteSpace(spoken))
            {
                prompt.Append(
                    "\n\n(That question was transcribed by Windows' own speech recognition and may " +
                    "contain mis-hearings. If a word makes no sense in Rust, say what you think it " +
                    "was meant to be and answer that.)");
            }

            var answer = await Chat(question, prompt.ToString(), apiKey, onDelta, ct);
            return new AiAnswerResult(answer, prompt.ToString());
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

            // Recommended by OpenRouter for routing/analytics; harmless if ignored.
            // Referer must be a valid ASCII value — a fixed title avoids locale issues.
            try
            {
                request.Headers.Add("HTTP-Referer", "https://github.com/rustplus-desktop");
                request.Headers.Add("X-Title", AppTitle);
            }
            catch { /* optional headers must never fail a question */ }

            AiLog.Info($"[OpenRouter] Sending POST to {ChatUrl} (model={ChatModel}, stream={onDelta != null})");

            using var response = await AiHttp.Client.SendAsync(
                request,
                onDelta != null ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                ct);

            if (!response.IsSuccessStatusCode)
                throw AiHttp.Failure(response, await AiHttp.ReadBody(response, ct));

            if (onDelta == null)
            {
                using var doc = JsonDocument.Parse(await AiHttp.ReadBody(response, ct));

                // OpenAI-compatible shape: choices[0].message.content (string or parts array).
                var message = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message");

                if (message.TryGetProperty("content", out var contentProp))
                    return ContentToString(contentProp);

                return "";
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
                        piece = ContentToString(text);
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

        /// <summary>
        /// Some OpenRouter-backed models return content as a string, others as a parts array.
        /// </summary>
        private static string ContentToString(JsonElement content)
        {
            return content.ValueKind switch
            {
                JsonValueKind.String => content.GetString() ?? "",
                JsonValueKind.Array => ReadParts(content),
                _ => "",
            };
        }

        private static string ReadParts(JsonElement parts)
        {
            var text = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.String)
                {
                    text.Append(part.GetString());
                }
                else if (part.ValueKind == JsonValueKind.Object &&
                         part.TryGetProperty("text", out var inner))
                {
                    text.Append(inner.GetString());
                }
            }
            return text.ToString();
        }
    }
}
