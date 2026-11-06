using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Data;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class ServerProfileCacheKeyTests
{
    [TestMethod]
    public void CacheKey_WhenHostAndPortAreReal_ReturnsHostPortWithUnderscore()
    {
        var profile = new ServerProfile
        {
            Host = "192.168.1.100",
            Port = 28015,
            ServerId = "guid-1234"
        };

        Assert.AreEqual("192.168.1.100_28015", profile.CacheKey);
    }

    [TestMethod]
    public void CacheKey_WhenHostIsCompanionSocket_ReturnsServerId()
    {
        var profile = new ServerProfile
        {
            Host = "companion-socket",
            Port = 28082,
            ServerId = "eafb7986-4abe-4725-93ef-33ebe08e6254"
        };

        Assert.AreEqual("eafb7986-4abe-4725-93ef-33ebe08e6254", profile.CacheKey);
    }

    [TestMethod]
    public void CacheKey_WhenHostIsCompanionSocketAndNoServerId_ReturnsFallback()
    {
        var profile = new ServerProfile
        {
            Host = "companion-socket",
            Port = 28082,
            ServerId = null
        };

        Assert.AreEqual("companion-socket_28082", profile.CacheKey);
    }

    [TestMethod]
    public void ServerKey_WhenHostAndPortAreReal_ReturnsHostPortWithHyphen()
    {
        var profile = new ServerProfile
        {
            Host = "192.168.1.100",
            Port = 28015,
            ServerId = "guid-1234"
        };

        Assert.AreEqual("192.168.1.100-28015", profile.ServerKey);
    }

    [TestMethod]
    public void ServerKey_WhenHostIsCompanionSocket_ReturnsServerId()
    {
        var profile = new ServerProfile
        {
            Host = "companion-socket",
            Port = 28082,
            ServerId = "eafb7986-4abe-4725-93ef-33ebe08e6254"
        };

        Assert.AreEqual("eafb7986-4abe-4725-93ef-33ebe08e6254", profile.ServerKey);
    }

    [TestMethod]
    public void ServerKey_WhenHostIsCompanionSocketAndNoServerId_ReturnsFallback()
    {
        var profile = new ServerProfile
        {
            Host = "companion-socket",
            Port = 28082,
            ServerId = ""
        };

        Assert.AreEqual("companion-socket-28082", profile.ServerKey);
    }

    [TestMethod]
    public void ServerLocalDataCleanup_CompanionSocket_DistinguishesProfilesByServerId()
    {
        var server1 = new ServerProfile
        {
            Host = "companion-socket",
            Port = 28082,
            ServerId = "server-1-guid"
        };

        var server2 = new ServerProfile
        {
            Host = "companion-socket",
            Port = 28082,
            ServerId = "server-2-guid"
        };

        string tempAppDir = Path.Combine(Path.GetTempPath(), "RPD_Test_" + Guid.NewGuid().ToString("N"));
        string tempLocalDir = Path.Combine(Path.GetTempPath(), "RPD_Test_Local_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempLocalDir, "chat"));

        string chat1 = Path.Combine(tempLocalDir, "chat", "server-1-guid.json");
        string chat2 = Path.Combine(tempLocalDir, "chat", "server-2-guid.json");
        File.WriteAllText(chat1, "[]");
        File.WriteAllText(chat2, "[]");

        try
        {
            // Deleting server1 with server2 remaining should NOT think server1 is still used
            int removed = ServerLocalDataCleanup.DeleteFor(
                server1.Host,
                server1.Port,
                keepServerData: false,
                ownMapFolder: null,
                tempAppDir,
                tempLocalDir,
                server1.ServerKey,
                server1.CacheKey);

            Assert.IsFalse(File.Exists(chat1), "server1 chat should be deleted");
            Assert.IsTrue(File.Exists(chat2), "server2 chat should remain intact");
        }
        finally
        {
            if (Directory.Exists(tempAppDir)) Directory.Delete(tempAppDir, true);
            if (Directory.Exists(tempLocalDir)) Directory.Delete(tempLocalDir, true);
        }
    }
}

