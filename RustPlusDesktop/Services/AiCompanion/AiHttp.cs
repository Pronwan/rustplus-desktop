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
    /// The plumbing all three providers share: one client, one way of reading a server-sent
    /// event stream, and one way of turning a failure into something a player can act on.
    /// </summary>
    internal static class AiHttp
    {
        /// <summary>
        /// Two minutes. A long answer with a screenshot attached is slow, and the alternative to
        /// waiting is a timeout in the middle of a raid with the recording already gone.
        /// </summary>
        public static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(2) };

        public static async Task<string> ReadBody(HttpResponseMessage response, CancellationToken ct)
        {
            try { return await response.Content.ReadAsStringAsync(ct); }
            catch { return ""; }
        }

        /// <summary>
        /// Turns a refusal into a sentence worth reading.
        ///
        /// Providers put the real reason in a JSON body that nobody should have to open a log to
        /// see — a wrong key, a spent quota and a model that does not exist all arrive as a
        /// number otherwise, and they need three different things done about them.
        /// </summary>
        public static AiRequestException Failure(HttpResponseMessage response, string body)
        {
            var detail = ExtractMessage(body);
            int code = (int)response.StatusCode;

            var head = code switch
            {
                401 or 403 => "The provider rejected the key.",
                402 => "The provider says this account cannot be billed.",
                404 => "The provider does not know that model.",
                413 => "The recording was too large to send.",
                429 => "Rate limit or quota reached at the provider.",
                >= 500 => "The provider had a problem at its end.",
                _ => $"The provider refused the request ({code}).",
            };

            return new AiRequestException(string.IsNullOrEmpty(detail) ? head : head + " " + detail);
        }

        private static string ExtractMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                // { "error": { "message": … } } for OpenAI, Anthropic and Gemini alike.
                if (root.TryGetProperty("error", out var error))
                {
                    if (error.ValueKind == JsonValueKind.String) return error.GetString() ?? "";
                    if (error.TryGetProperty("message", out var message)) return message.GetString() ?? "";
                }

                if (root.TryGetProperty("message", out var top)) return top.GetString() ?? "";
            }
            catch
            {
                // Not JSON. A page of HTML from a proxy is not worth showing.
            }

            return "";
        }

        /// <summary>Reads an SSE body line by line, yielding the payload of each data: line.</summary>
        public static async IAsyncEnumerable<string> ReadEvents(
            HttpResponseMessage response,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(ct);
                if (line == null) break;
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

                var payload = line.Substring(5).Trim();
                if (payload.Length == 0 || payload == "[DONE]") continue;

                yield return payload;
            }
        }

        public static string Base64(string path) => Convert.ToBase64String(File.ReadAllBytes(path));
    }
}
