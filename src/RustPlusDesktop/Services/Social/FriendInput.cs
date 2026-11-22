using System;
using System.Text.RegularExpressions;

namespace RustPlusDesk.Services.Social;

public enum FriendInputKind
{
    /// <summary>Nothing typed yet.</summary>
    Empty,

    /// <summary>A usable 17 digit Steam id, typed or lifted out of a profile link.</summary>
    SteamId,

    /// <summary>Digits so far, but not enough of them. Said gently: the user is still typing.</summary>
    IncompleteId,

    /// <summary>A steamcommunity.com/id/name link. Resolving it would need Steam, so it is refused with advice.</summary>
    VanityLink,

    /// <summary>Anything else, a plain name included.</summary>
    Invalid,
}

/// <summary>The outcome of reading the add-friend box.</summary>
public readonly record struct FriendInputResult(FriendInputKind Kind, string? SteamId = null, int Digits = 0)
{
    public bool IsValid => Kind == FriendInputKind.SteamId;
}

/// <summary>
/// Reads what somebody pasted into the add-friend box.
///
/// The platform addresses people by Steam id (names are not unique, and a search that answered
/// with near matches would be a directory of everybody who plays), so the box accepts the id or a
/// profile link that contains it and explains everything else.
/// </summary>
public static class FriendInput
{
    private static readonly Regex Id = new(@"^7656\d{13}$", RegexOptions.Compiled);
    private static readonly Regex Digits = new(@"^\d+$", RegexOptions.Compiled);

    private static readonly Regex ProfileLink = new(
        @"^(?:https?://)?(?:www\.)?steamcommunity\.com/profiles/(\d+)(?:[/?#].*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex VanityLinkPattern = new(
        @"^(?:https?://)?(?:www\.)?steamcommunity\.com/id/[^/?#\s]+(?:[/?#].*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static FriendInputResult Parse(string? input)
    {
        var text = input?.Trim() ?? "";
        if (text.Length == 0) return new FriendInputResult(FriendInputKind.Empty);

        var link = ProfileLink.Match(text);
        if (link.Success) text = link.Groups[1].Value;

        if (Id.IsMatch(text)) return new FriendInputResult(FriendInputKind.SteamId, text, text.Length);

        if (Digits.IsMatch(text))
        {
            // Too short is still being typed; too long or the wrong prefix will never be an id.
            return text.Length < 17 && "7656".StartsWith(text[..Math.Min(4, text.Length)], StringComparison.Ordinal)
                ? new FriendInputResult(FriendInputKind.IncompleteId, null, text.Length)
                : new FriendInputResult(FriendInputKind.Invalid, null, text.Length);
        }

        if (VanityLinkPattern.IsMatch(text)) return new FriendInputResult(FriendInputKind.VanityLink);

        return new FriendInputResult(FriendInputKind.Invalid);
    }
}
