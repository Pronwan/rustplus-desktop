using System;
using System.Collections.Generic;
using System.Linq;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services.Social;

/// <summary>
/// What the friends pane shows, already grouped, sorted and filtered.
///
/// A plain value with no UI in it, so the rules below (who comes first, what a search matches)
/// can be tested without a window.
/// </summary>
public sealed record FriendsLayout(
    IReadOnlyList<Friend> Incoming,
    IReadOnlyList<Friend> Outgoing,
    IReadOnlyList<Friend> Online,
    IReadOnlyList<Friend> Offline,
    int TotalFriends,
    int TotalRequests,
    bool HasQuery)
{
    /// <summary>Requests that survived the search, both directions.</summary>
    public int RequestCount => Incoming.Count + Outgoing.Count;

    /// <summary>Everything the pane will draw.</summary>
    public int ShownCount => RequestCount + Online.Count + Offline.Count;

    /// <summary>No friends and no requests at all: the account has nothing to list.</summary>
    public bool IsEmpty => TotalFriends == 0 && TotalRequests == 0;

    /// <summary>There is something to list, but the search matched none of it.</summary>
    public bool HasNoMatches => HasQuery && !IsEmpty && ShownCount == 0;
}

/// <summary>Grouping, ordering and searching for the friends and block lists.</summary>
public static class FriendsListLayout
{
    /// <summary>
    /// Requests first (asked of you, then asked by you), then friends who are online (the ones in a
    /// server before the ones who just have the app open), then everybody else. Each group is
    /// alphabetical, case and accent blind, with nameless accounts last.
    ///
    /// Requests keep the order the server gave them: the list carries no timestamp to sort on, and
    /// re-sorting by name would move a request somebody is about to answer.
    /// </summary>
    public static FriendsLayout Build(
        IEnumerable<Friend>? friends,
        IEnumerable<Friend>? incoming,
        IEnumerable<Friend>? outgoing,
        string? query)
    {
        var allFriends = (friends ?? Enumerable.Empty<Friend>()).ToList();
        var allIncoming = (incoming ?? Enumerable.Empty<Friend>()).ToList();
        var allOutgoing = (outgoing ?? Enumerable.Empty<Friend>()).ToList();

        var text = Normalize(query);
        var hasQuery = text.Length > 0;

        var matching = hasQuery ? allFriends.Where(f => Matches(f, text)).ToList() : allFriends;

        var online = matching
            .Where(f => f.IsOnline)
            .OrderBy(f => HasServer(f) ? 0 : 1)
            .ThenBy(f => f, NameComparer.Instance)
            .ToList();

        var offline = matching
            .Where(f => !f.IsOnline)
            .OrderBy(f => f, NameComparer.Instance)
            .ToList();

        return new FriendsLayout(
            hasQuery ? allIncoming.Where(f => Matches(f, text)).ToList() : allIncoming,
            hasQuery ? allOutgoing.Where(f => Matches(f, text)).ToList() : allOutgoing,
            online,
            offline,
            allFriends.Count,
            allIncoming.Count + allOutgoing.Count,
            hasQuery);
    }

    /// <summary>Name, Steam id or server, anywhere in the text, ignoring case.</summary>
    public static bool Matches(Friend friend, string? query)
    {
        var text = Normalize(query);
        if (text.Length == 0) return true;

        return Contains(friend.DisplayName, text)
            || Contains(friend.SteamId, text)
            || Contains(friend.Server, text);
    }

    /// <summary>
    /// The block list for display: the search applied, most recent block first (that is the one
    /// somebody is most likely to be looking for), then by name.
    /// </summary>
    public static IReadOnlyList<BlockedPlayer> OrderBlocks(IEnumerable<BlockedPlayer>? blocks, string? query)
    {
        var text = Normalize(query);

        return (blocks ?? Enumerable.Empty<BlockedPlayer>())
            .Where(b => text.Length == 0 || Contains(b.DisplayName, text) || Contains(b.SteamId, text))
            .OrderByDescending(b => b.BlockedAt.HasValue)
            .ThenByDescending(b => b.BlockedAt)
            .ThenBy(b => b.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string Normalize(string? query) => query?.Trim() ?? "";

    private static bool Contains(string? haystack, string needle)
        => !string.IsNullOrEmpty(haystack) && haystack.Contains(needle, StringComparison.CurrentCultureIgnoreCase);

    private static bool HasServer(Friend friend) => !string.IsNullOrWhiteSpace(friend.Server);

    private sealed class NameComparer : IComparer<Friend>
    {
        public static readonly NameComparer Instance = new();

        public int Compare(Friend? x, Friend? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return 1;
            if (y is null) return -1;

            var xNameless = IsNameless(x.DisplayName);
            var yNameless = IsNameless(y.DisplayName);
            if (xNameless != yNameless) return xNameless ? 1 : -1;

            var byName = StringComparer.CurrentCultureIgnoreCase.Compare(x.DisplayName, y.DisplayName);
            return byName != 0 ? byName : string.CompareOrdinal(x.Id, y.Id);
        }

        /// <summary>The model's placeholder for "the server sent no name" sorts with the empty ones.</summary>
        private static bool IsNameless(string? name) => string.IsNullOrWhiteSpace(name) || name == "—";
    }
}
