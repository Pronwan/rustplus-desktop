using System;

namespace RustPlusDesk.Models;

/// <summary>
/// One line in the public room.
///
/// Carries a name, an avatar and a time — and deliberately no server. That is the single
/// difference between this and an LFG listing, where where-you-play is disclosed on purpose and
/// only after somebody agreed to it.
/// </summary>
public sealed class ChatLine
{
    public string Id { get; init; } = "";

    public string Body { get; init; } = "";

    public string? SenderId { get; init; }

    public string SenderName { get; init; } = "—";

    public string? AvatarUrl { get; init; }

    public DateTime? SentAt { get; init; }

    /// <summary>
    /// The timestamp exactly as the server wrote it, kept for the catch-up read.
    /// Handing back what was received avoids a round trip through DateTime deciding
    /// how many fractional digits or which offset spelling to use.
    /// </summary>
    public string? SentAtIso { get; init; }

    public string TimeLabel => SentAt?.ToLocalTime().ToString("HH:mm") ?? "";
}

/// <summary>Why the text box is closed, and until when.</summary>
public sealed record ChatSanction(string Kind, string Reason, DateTime? ExpiresAt);

/// <summary>
/// What one read of the room returns.
///
/// <paramref name="Ok"/> separates "the room is empty" from "the room could not be read". The
/// two look the same in the list and deliberately say the same thing there, but they must not
/// look the same to the silenced bar: a failed read carries no sanction, and treating that as
/// "no sanction" would hand a silenced account its text box back.
/// </summary>
public sealed record ChatSnapshot(
    System.Collections.Generic.List<ChatLine> Lines,
    ChatSanction? Sanction,
    bool Ok = true);
