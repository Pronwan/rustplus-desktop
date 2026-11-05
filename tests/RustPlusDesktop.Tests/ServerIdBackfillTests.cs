using System;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class ServerIdBackfillTests
{
    private const string Steam = "76561199163758692";
    private static readonly Guid Rustoria = new("eafb7986-4abe-4725-93ef-33ebe08e6254");
    private static readonly Guid Renegade = new("089941d9-064e-4628-9c60-eaeb01a943e8");

    private static ServerProfile Profile(string name, string host, int port, string steam = Steam) =>
        new() { Name = name, Host = host, Port = port, SteamId64 = steam, PlayerToken = "1" };

    // history/read nests the push payload as a JSON string, ports included as strings.
    private static string History(params (Guid id, string ip, string port, string playerId)[] pushes) =>
        JsonSerializer.Serialize(pushes.Select(p => new
        {
            serverId = p.id,
            channel = 1001,
            data = JsonSerializer.Serialize(new { id = p.id, ip = p.ip, port = p.port, playerId = p.playerId, playerToken = "123", type = "server" }),
        }));

    // syncData nests the web app's state as a JSON string, player ids as JS Long halves.
    private static string SyncData(params (Guid id, string name, long low, long high)[] servers) =>
        JsonSerializer.Serialize(new
        {
            version = 33,
            payload = JsonSerializer.Serialize(new
            {
                servers = servers.Select(s => new { id = s.id, name = s.name, playerId = new { low = s.low, high = s.high }, playerToken = 1 }),
            }),
        });

    [TestMethod]
    public void ParseHistory_ReadsIdAddressAndPlayerFromNestedPayload()
    {
        var c = ServerIdBackfill.ParseHistory(History((Rustoria, "192.248.164.111", "28085", Steam))).Single();

        Assert.AreEqual(Rustoria, c.ServerId);
        Assert.AreEqual("192.248.164.111", c.Ip);
        Assert.AreEqual(28085, c.Port);
        Assert.AreEqual(Steam, c.SteamId);
    }

    [TestMethod]
    public void ParseHistory_SkipsMalformedEntries_AndToleratesGarbage()
    {
        var json = "[{\"data\":\"not json\"},{\"data\":\"{\\\"id\\\":\\\"x\\\",\\\"port\\\":\\\"1\\\"}\"},{\"nodata\":1}," +
                   History((Rustoria, "1.2.3.4", "28085", Steam)).TrimStart('[');

        Assert.AreEqual(1, ServerIdBackfill.ParseHistory(json).Count);
        Assert.AreEqual(0, ServerIdBackfill.ParseHistory("{oops").Count);
        Assert.AreEqual(0, ServerIdBackfill.ParseHistory(null).Count);
    }

    [TestMethod]
    public void ParseSyncData_JoinsJsLongPlayerId()
    {
        // 76561199163758692 = (17825793 << 32) + 1203492964, as app.playrust.com stores it.
        var c = ServerIdBackfill.ParseSyncData(SyncData((Renegade, "EU RENEGADE", 1203492964, 17825793))).Single();

        Assert.AreEqual(Renegade, c.ServerId);
        Assert.AreEqual("EU RENEGADE", c.Name);
        Assert.AreEqual(Steam, c.SteamId);
    }

    [TestMethod]
    public void ParseSyncData_NegativeLowHalf_IsUnsigned()
    {
        // A low half above 2^31 arrives negative from JavaScript.
        var c = ServerIdBackfill.ParseSyncData(SyncData((Renegade, "x", -1, 0))).Single();

        Assert.AreEqual(uint.MaxValue.ToString(), c.SteamId);
    }

    [TestMethod]
    public void MatchByAddress_ExactHostPortAndPlayer()
    {
        var candidates = ServerIdBackfill.ParseHistory(History(
            (Rustoria, "192.248.164.111", "28085", Steam),
            (Renegade, "168.100.161.22", "28082", Steam)));

        Assert.AreEqual(Rustoria, ServerIdBackfill.MatchByAddress(Profile("a", "192.248.164.111", 28085), candidates));
        Assert.IsNull(ServerIdBackfill.MatchByAddress(Profile("a", "192.248.164.111", 28086), candidates));
        Assert.IsNull(ServerIdBackfill.MatchByAddress(Profile("a", "192.248.164.111", 28085, "1"), candidates));
    }

    [TestMethod]
    public void MatchByAddress_TwoIdsForOneAddress_IsAmbiguous()
    {
        var candidates = ServerIdBackfill.ParseHistory(History(
            (Rustoria, "1.2.3.4", "28085", Steam),
            (Renegade, "1.2.3.4", "28085", Steam)));

        Assert.IsNull(ServerIdBackfill.MatchByAddress(Profile("a", "1.2.3.4", 28085), candidates));
    }

    [TestMethod]
    public void MatchByAddress_RepeatedPushesForOneId_StillMatch()
    {
        var candidates = ServerIdBackfill.ParseHistory(History(
            (Rustoria, "1.2.3.4", "28085", Steam),
            (Rustoria, "1.2.3.4", "28085", Steam)));

        Assert.AreEqual(Rustoria, ServerIdBackfill.MatchByAddress(Profile("a", "1.2.3.4", 28085), candidates));
    }

    [TestMethod]
    public void MatchByName_IgnoresCaseAndOuterSpace_ButNotOtherWording()
    {
        var candidates = ServerIdBackfill.ParseSyncData(SyncData((Renegade, " EU Renegade 2x ", 1203492964, 17825793)));

        Assert.AreEqual(Renegade, ServerIdBackfill.MatchByName(Profile("eu renegade 2x", "h", 1), candidates));
        Assert.IsNull(ServerIdBackfill.MatchByName(Profile("EU Renegade 2x | JUST WIPED", "h", 1), candidates));
        Assert.IsNull(ServerIdBackfill.MatchByName(Profile("eu renegade 2x", "h", 1, "1"), candidates));
        Assert.IsNull(ServerIdBackfill.MatchByName(Profile("", "h", 1), candidates));
    }
}
