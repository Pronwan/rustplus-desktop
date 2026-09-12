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
                404 => "The provider does not know that model. Check the model name in the settings.",
                413 => "The recording was too large to send.",

                // Two very different problems arrive as 429 and need opposite responses:
                // waiting fixes one and never fixes the other.
                429 when IsOutOfCredit(body) =>
                    "There is no credit on this API account. A free rate-limit tier is not free " +
                    "usage — the API has to be topped up before it will answer.",
                429 => "Too many requests in a short time. Wait a moment and ask again.",

                >= 500 => "The provider had a problem at its end.",
                _ => $"The provider refused the request ({code}).",
            };

            return new AiRequestException(string.IsNullOrEmpty(detail) ? head : head + " " + detail);
        }

        /// <summary>
        /// Whether a 429 means "no money" rather than "too fast".
        ///
        /// OpenAI marks the first insufficient_quota, Google RESOURCE_EXHAUSTED with billing in
        /// the text, Anthropic credit_balance_too_low — and all three send it as 429, the same
        /// status as an ordinary rate limit. Told to wait, someone with an empty account waits
        /// forever; told to top up, someone who merely asked twice in a second pays for nothing.
        /// </summary>
        private static bool IsOutOfCredit(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return false;

            return body.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase)
                || body.Contains("credit_balance", StringComparison.OrdinalIgnoreCase)
                || body.Contains("billing", StringComparison.OrdinalIgnoreCase)
                || body.Contains("exceeded your current quota", StringComparison.OrdinalIgnoreCase);
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
