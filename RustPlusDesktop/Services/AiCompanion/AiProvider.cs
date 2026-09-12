using System;

namespace RustPlusDesk.Services.AiCompanion
{
    /// <summary>
    /// The models the companion can talk to, and what each of them can actually do.
    ///
    /// They differ in ways the UI has to be honest about rather than paper over: only two take
    /// audio at all, and only two have a voice of their own. Pretending otherwise would mean a
    /// setting that silently does nothing on one of them.
    /// </summary>
    public static class AiProviders
    {
        public const string OpenAi = "openai";
        public const string Gemini = "gemini";
        public const string Anthropic = "anthropic";
        public const string OpenRouter = "openrouter";

        public static readonly string[] All = { OpenAi, Gemini, Anthropic, OpenRouter };

        public static string DisplayName(string provider) => provider switch
        {
            OpenAi => "OpenAI · GPT",
            Gemini => "Google · Gemini",
            Anthropic => "Anthropic · Claude",
            OpenRouter => "OpenRouter · Many models",
            _ => provider,
        };

        /// <summary>
        /// Whether the model takes the recording itself.
        ///
        /// Claude accepts text, images and documents but not audio, so its recordings have to be
        /// transcribed on this machine first — which is slower and less accurate, and the reason
        /// the settings say so next to the choice rather than in a help page.
        /// </summary>
        public static bool AcceptsAudio(string provider) => provider is OpenAi or Gemini;

        /// <summary>
        /// Whether the provider will read an answer back in a voice of its own.
        ///
        /// Where it will not, Windows reads it instead — understandable, and plainly a
        /// synthesiser. Claude has no speech endpoint, so it is always Windows there.
        /// </summary>
        public static bool HasVoice(string provider) => provider is OpenAi or Gemini;

        /// <summary>
        /// The model each provider is asked by default.
        ///
        /// These go stale. Providers retire a name and every request starts coming back as
        /// "no such model", which is why the settings let one be typed over the top rather
        /// than leaving the user waiting for an update.
        /// </summary>
        public static string DefaultModel(string provider) => provider switch
        {
            OpenAi => "gpt-4o",
            Gemini => "gemini-3.6-flash",
            Anthropic => "claude-sonnet-5",
            OpenRouter => "openai/gpt-4o-mini",
            _ => "",
        };

        /// <summary>
        /// A short list of well-known model ids offered as one-click choices.
        ///
        /// The text field stays the source of truth — anything typed there wins, including a
        /// model id released after this app shipped. These are just the common starting
        /// points so nobody has to guess the exact spelling of a model id.
        /// </summary>
        public static string[] SuggestedModels(string provider) => provider switch
        {
            OpenAi => new[] { "gpt-4o", "gpt-4o-mini", "gpt-4.1", "gpt-4.1-mini" },
            Gemini => new[] { "gemini-3.6-flash", "gemini-2.5-flash", "gemini-2.5-pro" },
            Anthropic => new[] { "claude-sonnet-5", "claude-sonnet-4-5", "claude-haiku-4-5" },
            OpenRouter => new[]
            {
                "openai/gpt-4o-mini",
                "openai/gpt-4o",
                "anthropic/claude-sonnet-4",
                "anthropic/claude-3.5-sonnet",
                "google/gemini-2.5-flash",
                "google/gemini-2.5-pro",
                "meta-llama/llama-3.3-70b-instruct",
                "deepseek/deepseek-chat",
            },
            _ => Array.Empty<string>(),
        };

        /// <summary>The model actually used: the user's own where they set one.</summary>
        public static string Model(string provider)
        {
            var chosen = AiCompanionStore.Current.Models.TryGetValue(provider, out var name) ? name : null;
            return string.IsNullOrWhiteSpace(chosen) ? DefaultModel(provider) : chosen.Trim();
        }

        /// <summary>
        /// The model that reads answers aloud.
        ///
        /// Separate from the chat model and separately stale-able — the two are retired on
        /// their own schedules, and a speech model that has gone away is worse than a chat
        /// one that has: the request fails quietly and Windows takes over, so it sounds like
        /// the setting simply did nothing.
        /// </summary>
        public static string DefaultVoiceModel(string provider) => provider switch
        {
            OpenAi => "tts-1",
            Gemini => "gemini-2.5-flash-preview-tts",
            _ => "",
        };

        /// <summary>The key the voice model is stored under, beside the chat model.</summary>
        public static string VoiceModelKey(string provider) => provider + ":tts";

        public static string VoiceModel(string provider)
        {
            var chosen = AiCompanionStore.Current.Models.TryGetValue(VoiceModelKey(provider), out var name)
                ? name
                : null;

            return string.IsNullOrWhiteSpace(chosen) ? DefaultVoiceModel(provider) : chosen.Trim();
        }

        /// <summary>Where to get a key, for the link next to the field.</summary>
        public static string KeyUrl(string provider) => provider switch
        {
            OpenAi => "https://platform.openai.com/api-keys",
            Gemini => "https://aistudio.google.com/app/apikey",
            Anthropic => "https://console.anthropic.com/settings/keys",
            OpenRouter => "https://openrouter.ai/keys",
            _ => "",
        };

        /// <summary>
        /// A quick shape check before a key is stored, so an obvious paste error is caught here
        /// rather than as an authentication failure in the middle of a raid.
        /// </summary>
        public static bool LooksLikeKey(string provider, string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            key = key.Trim();

            return provider switch
            {
                OpenAi => key.StartsWith("sk-", StringComparison.Ordinal) && key.Length > 20,
                Anthropic => key.StartsWith("sk-ant-", StringComparison.Ordinal) && key.Length > 20,
                // Google's keys carry no prefix worth checking; length is all there is to go on.
                Gemini => key.Length > 20,
                // OpenRouter keys look like sk-or-v1-…, but older ones are plain sk-or-….
                OpenRouter => (key.StartsWith("sk-or-", StringComparison.Ordinal) || key.Length > 20) && key.Length > 20,
                _ => key.Length > 20,
            };
        }
    }
}
