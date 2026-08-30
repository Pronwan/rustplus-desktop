using System;

namespace RustPlusDesk.Models;

/// <summary>
/// Somebody on your block list.
///
/// Kept as its own shape rather than reusing an LFG row: a block outlives the listing that
/// prompted it, and the only things still true about the person afterwards are who they are and
/// when you blocked them.
/// </summary>
public sealed class BlockedPlayer
{
    public string UserId { get; init; } = "";

    public string DisplayName { get; init; } = "—";

    public string? AvatarUrl { get; init; }

    public string? SteamId { get; init; }

    public DateTime? BlockedAt { get; init; }

    /// <summary>Date only. To the hour would suggest a precision nobody needs here.</summary>
    public string BlockedAtLabel => BlockedAt?.ToLocalTime().ToString("d") ?? "";
}
