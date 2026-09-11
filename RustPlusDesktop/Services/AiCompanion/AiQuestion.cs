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
        /// <summary>The microphone track: the player asking.</summary>
        public string? MicPath { get; init; }

        /// <summary>The game's own sound during the recording, when it was asked for.</summary>
        public string? GamePath { get; init; }

        /// <summary>A JPEG of the game's screen, when one was attached.</summary>
        public string? ScreenshotPath { get; init; }

        /// <summary>The language the answer should be in, as an English name such as "German".</summary>
        public string Language { get; init; } = "English";

        /// <summary>What the app knows about the situation — the server, the time, the team.</summary>
        public string? Context { get; init; }
    }

    /// <summary>What went wrong, in the terms the answer panel shows it in.</summary>
    public sealed class AiRequestException : Exception
    {
        public AiRequestException(string message) : base(message) { }
    }
}
