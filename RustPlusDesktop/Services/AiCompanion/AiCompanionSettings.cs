using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RustPlusDesk.Services.AiCompanion
{
    /// <summary>
    /// Everything the AI companion needs, stored on this machine and nowhere else.
    ///
    /// The key never reaches our cloud. That is a promise made to the user's face in the policy
    /// they accept before the field will save, so it is worth stating where the code lives too:
    /// this file writes to %AppData% and nothing here has a network call in it.
    /// </summary>
    public sealed class AiCompanionSettings
    {
        public string Provider { get; set; } = AiProviders.OpenAi;

        /// <summary>
        /// One key per provider, each protected with the Windows account's own data protection.
        ///
        /// Per provider because switching between GPT and Gemini is something people do — to
        /// compare them, or because one is rate-limited — and a single slot meant the other
        /// account's key was silently overwritten and the next question failed on a wrong key.
        ///
        /// Never the plain key: this file sits in a folder any process running as this user can
        /// read, and a leaked key is somebody else's bill. Protected, it is useless on another
        /// account or another machine — which also means it does not survive a reinstall, and
        /// the settings say so rather than letting it fail mysteriously.
        /// </summary>
        public Dictionary<string, string> ProtectedKeys { get; set; } = new();

        /// <summary>
        /// The single key slot this used to have, read once and migrated into
        /// <see cref="ProtectedKeys"/> under whichever provider was selected at the time.
        /// Written back as null so it is migrated only once.
        /// </summary>
        public string? ProtectedKey { get; set; }

        /// <summary>The voice language for spoken answers, as a culture name such as "de-DE".</summary>
        public string TtsLanguage { get; set; } = "";

        /// <summary>
        /// What language answers come back in.
        ///
        /// <c>match</c> follows the question, which is the only one of the three that is right
        /// for someone who asks in two languages — and asking a Rust question in English while
        /// running the app in German is entirely normal, because the game's own terms are
        /// English. <c>app</c> pins it to the app's language, and anything else is a culture
        /// name to pin it to.
        /// </summary>
        public string AnswerLanguage { get; set; } = AnswerLanguages.MatchQuestion;

        /// <summary>Record the game's own sound alongside the microphone.</summary>
        public bool CaptureGameAudio { get; set; }

        /// <summary>Read the answer out loud.</summary>
        public bool AudioAnswers { get; set; } = true;

        /// <summary>Show the answer as text under the tile.</summary>
        public bool TextAnswers { get; set; } = true;

        /// <summary>Supporter only: start speaking before the answer is finished.</summary>
        public bool StreamingVoice { get; set; } = true;

        /// <summary>Push to talk, as a gesture string the global hotkey manager understands.</summary>
        public string Hotkey { get; set; } = "";

        /// <summary>Attach a screenshot by default, without having to tick it each time.</summary>
        public bool AttachScreenshotByDefault { get; set; }

        /// <summary>
        /// How much of the screen a screenshot covers: 1 for all of it, 0.5 for the middle
        /// half, 0.25 for the middle quarter.
        ///
        /// This is the one lever that really changes what a model can make out. Providers
        /// scale an image down before they read it, so on a 4K screen a switch on a wall
        /// arrives a couple of dozen pixels across whatever the file was saved at. Cropping
        /// to the middle — which in a first-person game is whatever is being looked at —
        /// hands over the same object several times larger, at the cost of the surroundings.
        /// </summary>
        public double ScreenshotZoom { get; set; } = 1.0;

        /// <summary>
        /// Send the shipped raid and crafting tables with every question.
        ///
        /// On by default, because without them a model answers recipes and raid costs from
        /// training data that has every retired version of them in it and no way to tell which
        /// is current. It costs roughly ten thousand tokens a question — free on Gemini, and
        /// mostly cached after the first question everywhere, but it is the user's bill.
        /// </summary>
        public bool IncludeGameData { get; set; } = true;

        /// <summary>Send as soon as the recording stops, without a second press.</summary>
        public bool AutoSendAfterRecording { get; set; }

        /// <summary>
        /// A model name per provider, where the user has chosen one.
        ///
        /// Providers retire models faster than this app ships, and an answer that fails
        /// because a name went stale is not something a user can work around from the outside.
        /// Empty means whatever <see cref="AiProviders.DefaultModel"/> currently says.
        /// </summary>
        public Dictionary<string, string> Models { get; set; } = new();

        /// <summary>How see-through the answer panel is. Zero leaves only the text.</summary>
        public double AnswerOpacity { get; set; } = 1.0;

        /// <summary>How wide the answer panel is, in pixels.</summary>
        public double AnswerWidth { get; set; } = 340;

        /// <summary>How tall it may grow before it scrolls, in pixels.</summary>
        public double AnswerHeight { get; set; } = 260;

        /// <summary>Its text colour, by the same keys the dock's tiles use.</summary>
        public string AnswerTextColorKey { get; set; } = RustPlusDesk.Models.CommandDockTextColors.Auto;

        /// <summary>When the user accepted what happens to their recordings. Null until they have.</summary>
        public DateTime? PolicyAcceptedUtc { get; set; }
    }

    public static class AnswerLanguages
    {
        /// <summary>Answer in whatever language the question was asked in.</summary>
        public const string MatchQuestion = "match";

        /// <summary>Answer in the language the app itself is set to.</summary>
        public const string AppLanguage = "app";

        /// <summary>Answer in English, whatever the question was.</summary>
        public const string English = "en";
    }

    /// <summary>Loads and saves <see cref="AiCompanionSettings"/>, and guards the key.</summary>
    public static class AiCompanionStore
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RustPlusDesk", "ai-companion.json");

        // Tied to this entry so a file lifted onto another machine decrypts to nothing rather
        // than to somebody's key.
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RustPlusDesk.AiCompanion.v1");

        private static AiCompanionSettings? _cached;

        public static AiCompanionSettings Current => _cached ??= Load();

        private static AiCompanionSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new AiCompanionSettings();

                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AiCompanionSettings>(json) ?? new AiCompanionSettings();

                // The key that was stored before there was one slot per provider belongs to
                // whichever provider was selected when it was saved — that is the only thing
                // it could have been for. Cleared afterwards so this runs once.
                if (!string.IsNullOrEmpty(settings.ProtectedKey))
                {
                    settings.ProtectedKeys[settings.Provider] = settings.ProtectedKey;
                    settings.ProtectedKey = null;
                    _cached = settings;
                    Save(settings);
                }

                return settings;
            }
            catch
            {
                // A corrupt settings file is not worth refusing to start over.
                return new AiCompanionSettings();
            }
        }

        public static void Save(AiCompanionSettings settings)
        {
            _cached = settings;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(settings,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // Nothing here is worth an exception on the UI thread; the settings simply do
                // not survive the session.
            }
        }

        /// <summary>True once a key is stored for the chosen provider, without reading it.</summary>
        public static bool HasKey => HasKeyFor(Current.Provider);

        public static bool HasKeyFor(string provider) =>
            Current.ProtectedKeys.TryGetValue(provider, out var stored) && !string.IsNullOrEmpty(stored);

        /// <summary>
        /// The key in the clear, for the moment of sending a request. Deliberately a method and
        /// not a property: reading it does real work and should look like it at the call site.
        /// </summary>
        public static string? ReadKey() => ReadKeyFor(Current.Provider);

        public static string? ReadKeyFor(string provider)
        {
            if (!Current.ProtectedKeys.TryGetValue(provider, out var stored) || string.IsNullOrEmpty(stored))
                return null;

            try
            {
                var plain = ProtectedData.Unprotect(
                    Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                // Written by a different Windows account, or restored from a backup of another
                // machine. Either way it can never be decrypted here.
                return null;
            }
        }

        public static void WriteKey(string? key) => WriteKeyFor(Current.Provider, key);

        public static void WriteKeyFor(string provider, string? key)
        {
            var settings = Current;

            if (string.IsNullOrWhiteSpace(key))
            {
                settings.ProtectedKeys.Remove(provider);
            }
            else
            {
                var cipher = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(key.Trim()), Entropy, DataProtectionScope.CurrentUser);
                settings.ProtectedKeys[provider] = Convert.ToBase64String(cipher);
            }

            Save(settings);
        }
    }
}
