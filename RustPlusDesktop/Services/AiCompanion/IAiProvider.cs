using System;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.AiCompanion
{
    /// <summary>
    /// One model provider. These exist and they differ in what they will accept, not in
    /// what they are asked — the differences are absorbed here so the rest of the app only ever
    /// has a question and an answer.
    /// </summary>
    public interface IAiProvider
    {
        /// <summary>
        /// Asks, and returns the whole answer.
        ///
        /// <paramref name="onDelta"/> is called with each fragment as it arrives, off the UI
        /// thread. Passing null asks for the answer in one piece, which is the cheaper path
        /// where nothing is waiting to read it as it lands.
        /// </summary>
        Task<AiAnswerResult> AskAsync(AiQuestion question, string apiKey, Action<string>? onDelta, CancellationToken ct);
    }

    public static class AiProviderFactory
    {
        public static IAiProvider For(string provider) => provider switch
        {
            AiProviders.Gemini => new GeminiProvider(),
            AiProviders.Anthropic => new AnthropicProvider(),
            AiProviders.OpenRouter => new OpenRouterProvider(),
            _ => new OpenAiProvider(),
        };
    }
}
