using System;
using System.Windows;
using System.Windows.Media;

namespace RustPlusDesk.Models;

/// <summary>
/// Somebody on your friends list, or somebody asking to be.
///
/// One shape for both, because the row is the same person either way — what differs is the state,
/// and which buttons that state puts next to them.
/// </summary>
public sealed class Friend
{
    /// <summary>The friendship, not the person. Accepting and removing address this.</summary>
    public string Id { get; init; } = "";

    public string State { get; init; } = "pending";

    /// <summary>True when we asked them, false when they asked us.</summary>
    public bool RequestedByMe { get; init; }

    public string UserId { get; init; } = "";

    public string DisplayName { get; init; } = "—";

    public string? AvatarUrl { get; init; }

    public string? SteamId { get; init; }

    public bool IsOnline { get; init; }

    /// <summary>The server they are playing on, or null when they are not in one.</summary>
    public string? Server { get; init; }

    public int TeamSize { get; init; }

    public bool IsAccepted => State == "accepted";

    /// <summary>Green when online, muted red otherwise — the same light the listings use.</summary>
    public Brush StatusBrush => IsOnline
        ? new SolidColorBrush(Color.FromRgb(0x4C, 0xC3, 0x8A))
        : new SolidColorBrush(Color.FromRgb(0x9A, 0x54, 0x54));

    /// <summary>
    /// Where they are, in one line.
    ///
    /// Not in a server is not the same as offline: somebody can have the app open between wipes,
    /// and saying nothing at all would make the two look identical.
    /// </summary>
    public string WhereLabel => Server is { Length: > 0 } server
        ? (TeamSize > 1 ? $"{server}  ·  {TeamSize}" : server)
        : "";

    public Visibility WhereVisibility => Server is { Length: > 0 }
        ? Visibility.Visible
        : Visibility.Collapsed;
}

/// <summary>What one read of the friends list returns.</summary>
public sealed record FriendList(
    System.Collections.Generic.List<Friend> Friends,
    System.Collections.Generic.List<Friend> Incoming,
    System.Collections.Generic.List<Friend> Outgoing,
    bool Ok = true);
