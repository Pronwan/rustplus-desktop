using System;
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
        /// The key, protected with the Windows account's own data protection.
        ///
        /// Never the plain key: this file sits in a folder any process running as this user can
        /// read, and a leaked key is somebody else's bill. Protected, it is useless on another
        /// account or another machine — which also means it does not survive a reinstall, and
        /// the settings say so rather than letting it fail mysteriously.
        /// </summary>
        public string? ProtectedKey { get; set; }

        /// <summary>The voice language for spoken answers, as a culture name such as "de-DE".</summary>
        public string TtsLanguage { get; set; } = "";

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

        /// <summary>When the user accepted what happens to their recordings. Null until they have.</summary>
        public DateTime? PolicyAcceptedUtc { get; set; }
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
                return JsonSerializer.Deserialize<AiCompanionSettings>(json) ?? new AiCompanionSettings();
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

        /// <summary>True once a key is stored, without going near the key itself.</summary>
        public static bool HasKey => !string.IsNullOrEmpty(Current.ProtectedKey);

        /// <summary>
        /// The key in the clear, for the moment of sending a request. Deliberately a method and
        /// not a property: reading it does real work and should look like it at the call site.
        /// </summary>
        public static string? ReadKey()
        {
            var stored = Current.ProtectedKey;
            if (string.IsNullOrEmpty(stored)) return null;

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

        public static void WriteKey(string? key)
        {
            var settings = Current;

            if (string.IsNullOrWhiteSpace(key))
            {
                settings.ProtectedKey = null;
            }
            else
            {
                var cipher = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(key.Trim()), Entropy, DataProtectionScope.CurrentUser);
                settings.ProtectedKey = Convert.ToBase64String(cipher);
            }

            Save(settings);
        }
    }
}
