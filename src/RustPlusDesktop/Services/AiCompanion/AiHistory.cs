using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RustPlusDesk.Services.AiCompanion
{
    /// <summary>One exchange, as it is worth reading back afterwards.</summary>
    public sealed class AiExchange
    {
        public DateTime WhenUtc { get; set; } = DateTime.UtcNow;
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";

        /// <summary>What the provider was asked, where the recording was turned into words here.</summary>
        public string? Transcript { get; set; }

        public bool HadScreenshot { get; set; }
        public bool HadGameAudio { get; set; }

        /// <summary>The answer, or empty when it failed.</summary>
        public string Answer { get; set; } = "";

        /// <summary>Why it failed, or null when it did not.</summary>
        public string? Error { get; set; }

        public bool Failed => !string.IsNullOrEmpty(Error);
    }

    /// <summary>
    /// The last few exchanges, kept on this machine.
    ///
    /// It exists because of the failures, not the answers. A question asked with the answer
    /// panel switched off leaves nothing behind but the word "failed" on a tile the size of a
    /// stamp — and the reason it failed, which is the one thing worth having, was on screen for
    /// no one. This is where it went.
    ///
    /// Local only, like the key and the recordings. Nothing here is ever sent anywhere.
    /// </summary>
    public static class AiHistory
    {
        /// <summary>
        /// Twenty. Enough to find the request that went wrong an hour ago, few enough that a
        /// transcript of everything ever said into the microphone is not sitting in a file.
        /// </summary>
        public const int MaxEntries = 20;

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RustPlusDesk", "ai-history.json");

        private static List<AiExchange>? _cached;

        /// <summary>Newest first.</summary>
        public static IReadOnlyList<AiExchange> Entries => _cached ??= Load();

        /// <summary>Fires when an exchange is added or the history is cleared.</summary>
        public static event Action? Changed;

        private static List<AiExchange> Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new List<AiExchange>();

                return JsonSerializer.Deserialize<List<AiExchange>>(File.ReadAllText(FilePath))
                       ?? new List<AiExchange>();
            }
            catch
            {
                return new List<AiExchange>();
            }
        }

        public static void Add(AiExchange exchange)
        {
            var list = (List<AiExchange>)Entries;

            list.Insert(0, exchange);
            while (list.Count > MaxEntries) list.RemoveAt(list.Count - 1);

            Save();
            Raise();
        }

        public static void Clear()
        {
            ((List<AiExchange>)Entries).Clear();
            Save();
            Raise();
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(
                    Entries.ToList(), new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // A history that does not survive the session is a smaller problem than an
                // exception thrown out of the middle of an answer.
            }
        }

        private static void Raise()
        {
            try { Changed?.Invoke(); } catch { }
        }
    }
}
