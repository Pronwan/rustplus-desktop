using System;

namespace RustPlusDesk.Services.AiCompanion
{
    /// <summary>
    /// The three models the companion can talk to, and what each of them can actually do.
    ///
    /// They differ in ways the UI has to be honest about rather than paper over: only two take
    /// audio at all, and only two have a voice of their own. Pretending otherwise would mean a
    /// setting that silently does nothing on one of the three.
    /// </summary>
    public static class AiProviders
    {
        public const string OpenAi = "openai";
        public const string Gemini = "gemini";
        public const string Anthropic = "anthropic";

        public static readonly string[] All = { OpenAi, Gemini, Anthropic };

        public static string DisplayName(string provider) => provider switch
        {
            OpenAi => "OpenAI · GPT",
            Gemini => "Google · Gemini",
            Anthropic => "Anthropic · Claude",
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
        /// Whether the provider can speak the answer. Where it cannot, Windows' own voice reads
        /// it instead — understandable, but plainly a synthesiser.
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
            _ => "",
        };

        /// <summary>The model actually used: the user's own where they set one.</summary>
        public static string Model(string provider)
        {
            var chosen = AiCompanionStore.Current.Models.TryGetValue(provider, out var name) ? name : null;
            return string.IsNullOrWhiteSpace(chosen) ? DefaultModel(provider) : chosen.Trim();
        }

        /// <summary>Where to get a key, for the link next to the field.</summary>
        public static string KeyUrl(string provider) => provider switch
        {
            OpenAi => "https://platform.openai.com/api-keys",
            Gemini => "https://aistudio.google.com/app/apikey",
            Anthropic => "https://console.anthropic.com/settings/keys",
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
                _ => key.Length > 20,
            };
        }
    }
}
