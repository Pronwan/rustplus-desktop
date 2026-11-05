using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services;

namespace RustPlusDesktop.Tests;

/// <summary>
/// The clan tab tells "you are not in a clan" apart from "the clan request failed", and both hang on
/// the error payload of the failed <c>Response&lt;T&gt;</c>. The server sends the identifier
/// <c>no_clan</c>, which the API also parses into <c>RustPlusErrorCode.NoClan</c>; either spelling
/// has to be recognised, and nothing else may be mistaken for it.
/// </summary>
[TestClass]
public sealed class ClanNoClanStateTests
{
    [TestMethod]
    public void IsNoClanError_DetectsParsedErrorCode()
        => Assert.IsTrue(RustPlusClientReal.IsNoClanError("NoClan", "no_clan"));

    [TestMethod]
    public void IsNoClanError_DetectsRawIdentifierWithoutCode()
        => Assert.IsTrue(RustPlusClientReal.IsNoClanError(null, "no_clan"));

    [TestMethod]
    public void IsNoClanError_DetectsCodeWithoutMessage()
        => Assert.IsTrue(RustPlusClientReal.IsNoClanError("NoClan", null));

    [TestMethod]
    public void IsNoClanError_AcceptsSurroundingWording()
        => Assert.IsTrue(RustPlusClientReal.IsNoClanError(null, "Clan error: No Clan"));

    [TestMethod]
    public void IsNoClanError_RejectsOtherFailures()
    {
        Assert.IsFalse(RustPlusClientReal.IsNoClanError("NoTeam", "no_team"));
        Assert.IsFalse(RustPlusClientReal.IsNoClanError("ServerError", "server_error"));
        Assert.IsFalse(RustPlusClientReal.IsNoClanError("RateLimit", "rate_limit"));
        Assert.IsFalse(RustPlusClientReal.IsNoClanError("AccessDenied", "access_denied"));
        Assert.IsFalse(RustPlusClientReal.IsNoClanError(null, null));
        Assert.IsFalse(RustPlusClientReal.IsNoClanError("", "   "));
    }

    [TestMethod]
    public void ReadResponseError_ReadsCodeAndMessageFromResponseLikeObject()
    {
        var response = new FakeResponse
        {
            IsSuccess = false,
            Error = new FakeError { Code = "NoClan", Message = "no_clan" }
        };

        var (code, message) = RustPlusClientReal.ReadResponseError(response);

        Assert.AreEqual("NoClan", code);
        Assert.AreEqual("no_clan", message);
    }

    [TestMethod]
    public void ReadResponseError_ToleratesNullAndSuccessWithoutError()
    {
        var (missingCode, missingMessage) = RustPlusClientReal.ReadResponseError(null);
        Assert.IsNull(missingCode);
        Assert.IsNull(missingMessage);

        var (okCode, okMessage) = RustPlusClientReal.ReadResponseError(new FakeResponse { IsSuccess = true });
        Assert.IsNull(okCode);
        Assert.IsNull(okMessage);
    }

    private sealed class FakeResponse
    {
        public bool IsSuccess { get; set; }
        public FakeError? Error { get; set; }
    }

    private sealed class FakeError
    {
        public string? Code { get; set; }
        public string? Message { get; set; }
    }
}
