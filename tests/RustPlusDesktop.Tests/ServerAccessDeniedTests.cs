using System;
using System.Net.WebSockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class ServerAccessDeniedTests
{
    [TestMethod]
    public void IsAccessDeniedError_Detects418InWebSocketException()
    {
        var ex = new WebSocketException("The server returned status code '418' when status code '101' was expected.");
        Assert.IsTrue(RustPlusClientReal.IsAccessDeniedError(ex));
    }

    [TestMethod]
    public void IsAccessDeniedError_DetectsRustPlusAccessDeniedException()
    {
        var ex = new RustPlusAccessDeniedException("Rust+ Access Denied (418): Player token rejected.");
        Assert.IsTrue(RustPlusClientReal.IsAccessDeniedError(ex));
    }

    [TestMethod]
    public void IsAccessDeniedError_ReturnsFalseForGenericNetworkError()
    {
        var ex = new InvalidOperationException("Unable to connect to the remote server");
        Assert.IsFalse(RustPlusClientReal.IsAccessDeniedError(ex));
    }

    [TestMethod]
    public void ServerProfile_IsAccessDenied_ResetsOnNewToken()
    {
        var profile = new ServerProfile
        {
            Name = "Test Server",
            Host = "127.0.0.1",
            Port = 28082,
            PlayerToken = "1234",
            IsAccessDenied = true
        };

        Assert.IsTrue(profile.IsAccessDenied);

        // Update token (e.g. from in-game pairing)
        profile.PlayerToken = "5678";
        Assert.IsFalse(profile.IsAccessDenied);
    }

    [TestMethod]
    public void ServerProfile_IsAccessDenied_ResetsOnSuccessfulConnect()
    {
        var profile = new ServerProfile
        {
            Name = "Test Server",
            Host = "127.0.0.1",
            Port = 28082,
            PlayerToken = "1234",
            IsAccessDenied = true
        };

        Assert.IsTrue(profile.IsAccessDenied);

        profile.IsConnected = true;
        Assert.IsFalse(profile.IsAccessDenied);
    }
}
