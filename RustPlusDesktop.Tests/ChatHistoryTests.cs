using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;

namespace RustPlusDesktop.Tests;

[TestClass]
public class ChatHistoryTests
{
    [TestMethod]
    public void TeamChatMessage_Deduplication_IdentifiesSameTimestampAndMessage()
    {
        var now = DateTime.UtcNow;
        var msg1 = new TeamChatMessage(now, "AboYzbk", 76561198000000000UL, "Hiiiiiiiiii");
        var msg2 = new TeamChatMessage(now.AddMilliseconds(500), "AboYzbk", 76561198000000000UL, "Hiiiiiiiiii");
        var msg3 = new TeamChatMessage(now.AddSeconds(5), "AboYzbk", 76561198000000000UL, "Hiiiiiiiiii");

        var log = new List<TeamChatMessage> { msg1 };

        // Within 3 seconds threshold -> isDuplicate = true
        bool isDuplicateMsg2 = false;
        foreach (var ext in log.AsEnumerable().Reverse().Take(100))
        {
            var extUtc = ext.Timestamp.Kind == DateTimeKind.Utc ? ext.Timestamp : ext.Timestamp.ToUniversalTime();
            var msg2Utc = msg2.Timestamp.Kind == DateTimeKind.Utc ? msg2.Timestamp : msg2.Timestamp.ToUniversalTime();
            if (ext.SteamId == msg2.SteamId && ext.Text == msg2.Text && Math.Abs((extUtc - msg2Utc).TotalSeconds) < 3)
            {
                isDuplicateMsg2 = true;
                break;
            }
        }
        Assert.IsTrue(isDuplicateMsg2);

        // Outside 3 seconds threshold -> isDuplicate = false
        bool isDuplicateMsg3 = false;
        foreach (var ext in log.AsEnumerable().Reverse().Take(100))
        {
            var extUtc = ext.Timestamp.Kind == DateTimeKind.Utc ? ext.Timestamp : ext.Timestamp.ToUniversalTime();
            var msg3Utc = msg3.Timestamp.Kind == DateTimeKind.Utc ? msg3.Timestamp : msg3.Timestamp.ToUniversalTime();
            if (ext.SteamId == msg3.SteamId && ext.Text == msg3.Text && Math.Abs((extUtc - msg3Utc).TotalSeconds) < 3)
            {
                isDuplicateMsg3 = true;
                break;
            }
        }
        Assert.IsFalse(isDuplicateMsg3);
    }

    [TestMethod]
    public void ClanInfoModel_MapsAllPropertiesCorrectly()
    {
        var model = new ClanInfoModel
        {
            ClanId = 123456789,
            Name = "Alpha Clan",
            Motd = "Raiding tonight at 20:00",
            Created = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Creator = 76561198000000001UL,
            MotdTimestamp = new DateTime(2026, 8, 20, 10, 30, 0, DateTimeKind.Utc),
            MotdAuthor = 76561198000000002UL,
            Logo = new byte[] { 0x89, 0x50, 0x4E, 0x47 },
            Color = unchecked((int)0xFF1E5AB4),
            MaxMemberCount = 50,
            Score = 15420
        };

        model.Roles.Add(new ClanRoleModel
        {
            RoleId = 1,
            Rank = 1,
            Name = "Leader",
            CanSetMotd = true,
            CanSetLogo = true,
            CanInvite = true,
            CanKick = true,
            CanPromote = true,
            CanDemote = true,
            CanSetPlayerNotes = true,
            CanAccessLogs = true,
            CanAccessScoreEvents = true
        });

        model.Members.Add(new ClanMemberModel
        {
            SteamId = 76561198000000001UL,
            RoleId = 1,
            RoleName = "Leader",
            Rank = 1,
            Joined = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            LastSeen = new DateTime(2026, 8, 26, 9, 0, 0, DateTimeKind.Utc),
            Notes = "Clan Founder",
            IsOnline = true
        });

        model.Invites.Add(new ClanInviteModel
        {
            SteamId = 76561198000000003UL,
            Recruiter = 76561198000000001UL,
            Timestamp = new DateTime(2026, 8, 25, 18, 0, 0, DateTimeKind.Utc)
        });

        Assert.AreEqual(123456789, model.ClanId);
        Assert.AreEqual("Alpha Clan", model.Name);
        Assert.AreEqual("Raiding tonight at 20:00", model.Motd);
        Assert.AreEqual(1, model.Roles.Count);
        Assert.IsTrue(model.Roles[0].CanSetMotd);
        Assert.IsTrue(model.Roles[0].CanAccessLogs);
        Assert.AreEqual(1, model.Members.Count);
        Assert.IsTrue(model.Members[0].IsOnline);
        Assert.AreEqual(1, model.Invites.Count);
        Assert.AreEqual(15420, model.Score);
    }

    [TestMethod]
    public void ChatMessages_SortConsistentlyInUtc()
    {
        var nowUtc = DateTime.UtcNow;
        var list = new List<TeamChatMessage>
        {
            new TeamChatMessage(nowUtc.AddMinutes(-5), "Alice", 1UL, "Old message"),
            new TeamChatMessage(nowUtc, "Bob", 2UL, "Latest message"),
            new TeamChatMessage(nowUtc.AddMinutes(-2), "Charlie", 3UL, "Middle message")
        };

        var sorted = list.OrderBy(x => x.Timestamp.Kind == DateTimeKind.Utc ? x.Timestamp : x.Timestamp.ToUniversalTime()).ToList();
        Assert.AreEqual("Old message", sorted[0].Text);
        Assert.AreEqual("Middle message", sorted[1].Text);
        Assert.AreEqual("Latest message", sorted[2].Text);
    }

    [TestMethod]
    public void ClanColor_UnpacksRgbaHexCorrectly()
    {
        // Facepunch RGBA hex uint32: byte3=R, byte2=G, byte1=B, byte0=A
        
        // Red: R=255, G=0, B=0, A=255 -> uint 0xFF0000FF
        uint redPacked = 0xFF0000FF;
        byte redR = (byte)((redPacked >> 24) & 0xFF);
        byte redG = (byte)((redPacked >> 16) & 0xFF);
        byte redB = (byte)((redPacked >> 8) & 0xFF);
        byte redA = (byte)(redPacked & 0xFF);
        Assert.AreEqual(255, redR);
        Assert.AreEqual(0, redG);
        Assert.AreEqual(0, redB);
        Assert.AreEqual(255, redA);

        // Yellow: R=255, G=255, B=0, A=255 -> uint 0xFFFF00FF
        uint yellowPacked = 0xFFFF00FF;
        byte yelR = (byte)((yellowPacked >> 24) & 0xFF);
        byte yelG = (byte)((yellowPacked >> 16) & 0xFF);
        byte yelB = (byte)((yellowPacked >> 8) & 0xFF);
        byte yelA = (byte)(yellowPacked & 0xFF);
        Assert.AreEqual(255, yelR);
        Assert.AreEqual(255, yelG);
        Assert.AreEqual(0, yelB);
        Assert.AreEqual(255, yelA);

        // Orange: R=230, G=100, B=0, A=255 -> uint 0xE66400FF
        uint orangePacked = 0xE66400FF;
        byte orgR = (byte)((orangePacked >> 24) & 0xFF);
        byte orgG = (byte)((orangePacked >> 16) & 0xFF);
        byte orgB = (byte)((orangePacked >> 8) & 0xFF);
        byte orgA = (byte)(orangePacked & 0xFF);
        Assert.AreEqual(230, orgR);
        Assert.AreEqual(100, orgG);
        Assert.AreEqual(0, orgB);
        Assert.AreEqual(255, orgA);
    }

    private class MockClanMessage
    {
        public ulong SteamId { get; set; }
        public string Name { get; set; } = "";
        public string Message { get; set; } = "";
        public long Time { get; set; }
    }

    [TestMethod]
    public void ClanChatMessage_DeduplicationWindowWorks()
    {
        var fixedTime = new DateTime(2026, 8, 26, 10, 49, 0, DateTimeKind.Utc);
        var msg1 = new TeamChatMessage(fixedTime, "AboYzbk", 76561198000000001UL, "FGGG");
        var msg2 = new TeamChatMessage(fixedTime.AddSeconds(2), "AboYzbk", 76561198000000001UL, "FGGG");
        var msgDifferentText = new TeamChatMessage(fixedTime, "AboYzbk", 76561198000000001UL, "asdatweagasdg");

        var log = new List<TeamChatMessage> { msg1 };

        // Test duplicate detection
        bool isDuplicate = false;
        foreach (var ext in log)
        {
            if (ext.SteamId == msg2.SteamId &&
                string.Equals(ext.Text.Trim(), msg2.Text.Trim(), StringComparison.Ordinal) &&
                Math.Abs((ext.Timestamp - msg2.Timestamp).TotalSeconds) <= 5)
            {
                isDuplicate = true;
                break;
            }
        }
        Assert.IsTrue(isDuplicate, "msg2 should be detected as duplicate within 5s");

        // Test non-duplicate with different text
        bool isDifferentDuplicate = false;
        foreach (var ext in log)
        {
            if (ext.SteamId == msgDifferentText.SteamId &&
                string.Equals(ext.Text.Trim(), msgDifferentText.Text.Trim(), StringComparison.Ordinal) &&
                Math.Abs((ext.Timestamp - msgDifferentText.Timestamp).TotalSeconds) <= 5)
            {
                isDifferentDuplicate = true;
                break;
            }
        }
        Assert.IsFalse(isDifferentDuplicate, "msgDifferentText should not be marked as duplicate");
    }
}
