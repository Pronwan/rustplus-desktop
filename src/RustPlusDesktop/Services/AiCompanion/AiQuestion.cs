using System;

namespace RustPlusDesk.Services.AiCompanion
{
    /// <summary>
    /// One question and everything the player attached to it.
    ///
    /// The two audio tracks stay separate all the way to the provider. Which one is the player
    /// and which one is the game is the difference between "answer me" and "translate what they
    /// said", and mixing them down would throw that away for good.
    /// </summary>
    public sealed class AiQuestion
    {
        /// <summary>
        /// A question in words, where it was typed rather than spoken.
        ///
        /// Set by the chat command. When it is present there is nothing to transcribe and no
        /// recording to send, so the providers skip straight to asking.
        /// </summary>
        public string? Text { get; init; }

        /// <summary>
        /// Instructions to send instead of the companion's own, for a job that is not one.
        ///
        /// The standing prompt describes a Rust companion answering a player mid-session,
        /// and carries the raid table and every recipe with it. Asked to translate a line of
        /// chat, a model told all that answers as the companion — it comments on the line
        /// instead of translating it, and pays to re-read ten thousand tokens of recipes to
        /// do it. Set this and none of that is sent.
        /// </summary>
        public string? Instructions { get; init; }

        /// <summary>
        /// A hard ceiling on the answer, in words, or zero for the usual limit.
        ///
        /// Rust's chat truncates a long line and shows it to everyone in the team, so an
        /// answer that goes there has to be shorter than one nobody else reads.
        /// </summary>
        public int MaxWords { get; init; }

        /// <summary>The microphone track: the player asking.</summary>
        public string? MicPath { get; init; }

        /// <summary>The game's own sound during the recording, when it was asked for.</summary>
        public string? GamePath { get; init; }

        /// <summary>A JPEG of the game's screen, when one was attached.</summary>
        public string? ScreenshotPath { get; init; }

        /// <summary>
        /// How much of the screen that JPEG covers: 1 for all of it, 0.5 for the middle half.
        ///
        /// The model has to be told, or it reads a missing HUD and missing surroundings as
        /// facts about the situation rather than as the edges of a crop.
        /// </summary>
        public double Zoom { get; init; } = 1.0;

        /// <summary>
        /// The language to answer in, as an English name such as "German".
        ///
        /// When <see cref="MatchQuestionLanguage"/> is set this is only the fallback, for a
        /// recording with nothing recognisable in it.
        /// </summary>
        public string Language { get; init; } = "English";

        /// <summary>Answer in whatever language the question was asked in.</summary>
        public bool MatchQuestionLanguage { get; init; } = true;

        /// <summary>
        /// Whether the answer is going to be read aloud.
        ///
        /// A provider that can answer in speech directly only helps if speech is wanted; with
        /// text-only answers the ordinary path is both cheaper and streams as it writes.
        /// </summary>
        public bool WantsSpokenAnswer { get; init; }

        /// <summary>What the app knows about the situation — the server, the time, the team.</summary>
        public string? Context { get; init; }
    }

    /// <summary>
    /// An answer, and what the provider was actually asked.
    ///
    /// The transcript matters where the recording was turned into words before it was sent:
    /// a wrong answer to a mis-heard question looks exactly like a wrong answer until you can
    /// see what it heard. Null where the provider listened to the recording itself.
    /// </summary>
    /// <param name="Audio">
    /// The answer already spoken, where the provider could do it in the same call. Null
    /// everywhere else, and the text is then read by whatever voice is configured.
    /// </param>
    public sealed record AiAnswerResult(string Answer, string? Transcript, byte[]? Audio = null);

    /// <summary>What went wrong, in the terms the answer panel shows it in.</summary>
    public sealed class AiRequestException : Exception
    {
        public AiRequestException(string message) : base(message) { }
    }
}
