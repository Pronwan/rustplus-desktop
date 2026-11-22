using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Social;

namespace RustPlusDesktop.Tests;

[TestClass]
public class FriendsListLayoutTests
{
    private static Friend F(string id, string name, bool online = false, string? server = null, string? steam = null)
        => new() { Id = id, DisplayName = name, IsOnline = online, Server = server, SteamId = steam ?? "7656119800000000" + id };

    [TestMethod]
    public void SplitsOnlineFromOffline_AndSortsEachAlphabetically()
    {
        var layout = FriendsListLayout.Build(
            new[] { F("1", "zed"), F("2", "Bob", true), F("3", "alice", true), F("4", "Carl") },
            null, null, null);

        CollectionAssert.AreEqual(new[] { "alice", "Bob" }, layout.Online.Select(f => f.DisplayName).ToArray());
        CollectionAssert.AreEqual(new[] { "Carl", "zed" }, layout.Offline.Select(f => f.DisplayName).ToArray());
    }

    [TestMethod]
    public void OnlineFriendsInAServerComeBeforeOnlineFriendsWithoutOne()
    {
        var layout = FriendsListLayout.Build(
            new[] { F("1", "Amy", true), F("2", "Zoe", true, "Rustafied EU"), F("3", "Ben", true, "Moose US") },
            null, null, null);

        CollectionAssert.AreEqual(new[] { "Ben", "Zoe", "Amy" }, layout.Online.Select(f => f.DisplayName).ToArray());
    }

    [TestMethod]
    public void NamelessAccountsSortLast()
    {
        var layout = FriendsListLayout.Build(new[] { F("1", "—"), F("2", "Zed"), F("3", "") }, null, null, null);

        Assert.AreEqual("Zed", layout.Offline[0].DisplayName);
        Assert.AreEqual(3, layout.Offline.Count);
    }

    [TestMethod]
    public void RequestsKeepTheServersOrder_AndAreNotResorted()
    {
        var layout = FriendsListLayout.Build(
            null,
            new[] { F("1", "Zed"), F("2", "Amy") },
            new[] { F("3", "Mia") },
            null);

        CollectionAssert.AreEqual(new[] { "Zed", "Amy" }, layout.Incoming.Select(f => f.DisplayName).ToArray());
        Assert.AreEqual("Mia", layout.Outgoing[0].DisplayName);
        Assert.AreEqual(3, layout.RequestCount);
        Assert.AreEqual(3, layout.TotalRequests);
    }

    [TestMethod]
    public void SearchMatchesNameSteamIdAndServer_IgnoringCase()
    {
        var friends = new[]
        {
            F("1", "Kestrel", true, "Rusticated EU Long", "76561198000000001"),
            F("2", "Marlow", false, null, "76561198000000002"),
        };

        Assert.AreEqual(1, FriendsListLayout.Build(friends, null, null, "KEST").ShownCount);
        Assert.AreEqual(1, FriendsListLayout.Build(friends, null, null, "rusticated").ShownCount);
        Assert.AreEqual(1, FriendsListLayout.Build(friends, null, null, "198000000002").ShownCount);
        Assert.AreEqual(2, FriendsListLayout.Build(friends, null, null, "  ").ShownCount);
    }

    [TestMethod]
    public void SearchFiltersRequestsToo_AndReportsNoMatchesDistinctFromEmpty()
    {
        var layout = FriendsListLayout.Build(
            new[] { F("1", "Amy") }, new[] { F("2", "Ben") }, new[] { F("3", "Cat") }, "ben");

        Assert.AreEqual(1, layout.Incoming.Count);
        Assert.AreEqual(0, layout.Outgoing.Count);
        Assert.AreEqual(0, layout.Offline.Count);

        var none = FriendsListLayout.Build(new[] { F("1", "Amy") }, null, null, "zzz");
        Assert.IsTrue(none.HasNoMatches);
        Assert.IsFalse(none.IsEmpty);

        var empty = FriendsListLayout.Build(null, null, null, "zzz");
        Assert.IsTrue(empty.IsEmpty);
        Assert.IsFalse(empty.HasNoMatches);
    }

    [TestMethod]
    public void EmptyAccountIsEmptyOnlyWhenThereAreNoFriendsAndNoRequests()
    {
        Assert.IsTrue(FriendsListLayout.Build(null, null, null, null).IsEmpty);
        Assert.IsFalse(FriendsListLayout.Build(null, new[] { F("1", "Amy") }, null, null).IsEmpty);
        Assert.IsFalse(FriendsListLayout.Build(new[] { F("1", "Amy") }, null, null, null).IsEmpty);
    }

    [TestMethod]
    public void BlocksAreNewestFirst_ThenByName_AndSearchable()
    {
        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var blocks = new[]
        {
            new BlockedPlayer { UserId = "a", DisplayName = "Old", BlockedAt = now.AddDays(-30) },
            new BlockedPlayer { UserId = "b", DisplayName = "Zed", BlockedAt = null },
            new BlockedPlayer { UserId = "c", DisplayName = "New", BlockedAt = now },
            new BlockedPlayer { UserId = "d", DisplayName = "Amy", BlockedAt = null, SteamId = "76561198000000009" },
        };

        CollectionAssert.AreEqual(new[] { "New", "Old", "Amy", "Zed" },
            FriendsListLayout.OrderBlocks(blocks, null).Select(b => b.DisplayName).ToArray());

        Assert.AreEqual("Old", FriendsListLayout.OrderBlocks(blocks, "ol").Single().DisplayName);
        Assert.AreEqual("Amy", FriendsListLayout.OrderBlocks(blocks, "98000000009").Single().DisplayName);
        Assert.AreEqual(0, FriendsListLayout.OrderBlocks(null, "x").Count);
    }

    [DataTestMethod]
    [DataRow("76561198012345678", FriendInputKind.SteamId, "76561198012345678")]
    [DataRow("  76561198012345678 ", FriendInputKind.SteamId, "76561198012345678")]
    [DataRow("https://steamcommunity.com/profiles/76561198012345678", FriendInputKind.SteamId, "76561198012345678")]
    [DataRow("steamcommunity.com/profiles/76561198012345678/", FriendInputKind.SteamId, "76561198012345678")]
    [DataRow("HTTP://www.SteamCommunity.com/profiles/76561198012345678?l=english", FriendInputKind.SteamId, "76561198012345678")]
    public void ParsesSteamIdsAndProfileLinks(string input, FriendInputKind kind, string id)
    {
        var result = FriendInput.Parse(input);

        Assert.AreEqual(kind, result.Kind);
        Assert.AreEqual(id, result.SteamId);
        Assert.IsTrue(result.IsValid);
    }

    [DataTestMethod]
    [DataRow(null, FriendInputKind.Empty)]
    [DataRow("   ", FriendInputKind.Empty)]
    [DataRow("7656119", FriendInputKind.IncompleteId)]
    [DataRow("76", FriendInputKind.IncompleteId)]
    [DataRow("12345", FriendInputKind.Invalid)]
    [DataRow("123456789012345678", FriendInputKind.Invalid)]
    [DataRow("12345678901234567", FriendInputKind.Invalid)]
    [DataRow("https://steamcommunity.com/id/some_vanity_name", FriendInputKind.VanityLink)]
    [DataRow("steamcommunity.com/id/name/", FriendInputKind.VanityLink)]
    [DataRow("Kestrel", FriendInputKind.Invalid)]
    [DataRow("https://example.com/profiles/76561198012345678", FriendInputKind.Invalid)]
    public void ExplainsEverythingElse(string? input, FriendInputKind kind)
    {
        var result = FriendInput.Parse(input);

        Assert.AreEqual(kind, result.Kind);
        Assert.IsFalse(result.IsValid);
        Assert.IsNull(result.SteamId);
    }

    [TestMethod]
    public void IncompleteIdReportsHowManyDigitsWereTyped()
        => Assert.AreEqual(7, FriendInput.Parse("7656119").Digits);
}
