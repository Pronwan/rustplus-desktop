using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using System.Reflection;
using System.Text.Json;

namespace RustPlusDesktop.Tests;

[TestClass]
public class ConnectionRecoveryTests
{
    [TestMethod]
    public async Task TemporaryFailure_RetriesThreeTimes_WithBoundedDelays()
    {
        int attempts = 0;
        var delays = new List<int>();
        bool success = await RustPlusConnectionException.RetryAsync(() => { attempts++; return Task.FromResult(false); },
            () => true, () => ConnectionFailureKind.Unavailable, delay => { delays.Add(delay); return Task.CompletedTask; });
        Assert.IsFalse(success);
        Assert.AreEqual(3, attempts);
        CollectionAssert.AreEqual(new[] { 1000, 2000 }, delays);
    }

    [TestMethod]
    public async Task PermanentFailures_StopWithoutRetry_AndSelectionChangesCancelWaitingRetry()
    {
        foreach (var kind in new[] { ConnectionFailureKind.RePairRequired, ConnectionFailureKind.MissingCompanionId, ConnectionFailureKind.SignInRequired, ConnectionFailureKind.Unsupported })
        {
            int attempts = 0;
            await RustPlusConnectionException.RetryAsync(() => { attempts++; return Task.FromResult(false); }, () => true,
                () => kind, _ => throw new AssertFailedException("Permanent failures must not retry."));
            Assert.AreEqual(1, attempts);
        }
        bool current = true;
        int count = 0;
        await RustPlusConnectionException.RetryAsync(() => { count++; return Task.FromResult(false); }, () => current,
            () => ConnectionFailureKind.Unavailable, _ => { current = false; return Task.CompletedTask; });
        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public async Task SuccessfulRetry_StopsImmediately()
    {
        int attempts = 0;
        Assert.IsTrue(await RustPlusConnectionException.RetryAsync(() => Task.FromResult(++attempts == 2), () => true,
            () => ConnectionFailureKind.ServerError, _ => Task.CompletedTask));
        Assert.AreEqual(2, attempts);
    }

    [TestMethod]
    public void FailureClassification_PreservesTypedLoginAndProtocolErrors()
    {
        var signIn = new RustPlusConnectionException(ConnectionFailureKind.SignInRequired, "Unauthorized");
        Assert.AreEqual(ConnectionFailureKind.SignInRequired, RustPlusConnectionException.Classify(new TargetInvocationException(signIn)));
        Assert.AreEqual(ConnectionFailureKind.SignInRequired, RustPlusConnectionException.Classify(new AggregateException(signIn, new Exception("network"))));
        Assert.AreEqual(ConnectionFailureKind.RePairRequired, RustPlusConnectionException.FromServerError("not_found"));
        Assert.AreEqual(ConnectionFailureKind.Unsupported, RustPlusConnectionException.FromServerError("unhandled"));
        Assert.AreEqual(ConnectionFailureKind.ServerError, RustPlusConnectionException.FromServerError("rate_limit"));
        Assert.IsTrue(RustPlusConnectionException.IsLoginRejection(new Exception("status code '403'")));
        Assert.IsFalse(RustPlusConnectionException.IsLoginRejection(new Exception("entity #1403123")));
    }

    [TestMethod]
    public void CompanionConnection_RequiresServerIdAndLoginBeforeAnySocketAttempt()
    {
        string id = Guid.NewGuid().ToString();
        Assert.AreEqual(Guid.Parse(id), RustPlusConnectionException.RequireCompanionCredentials(id, "login"));
        Assert.AreEqual(ConnectionFailureKind.SignInRequired, Assert.ThrowsException<RustPlusConnectionException>(
            () => RustPlusConnectionException.RequireCompanionCredentials(id, null)).Kind);
        foreach (var missing in new[] { null, "", "legacy-server", Guid.Empty.ToString() })
            Assert.AreEqual(ConnectionFailureKind.MissingCompanionId, Assert.ThrowsException<RustPlusConnectionException>(
                () => RustPlusConnectionException.RequireCompanionCredentials(missing, "login")).Kind);
    }

    [TestMethod]
    public void Health_IsSeparateFromConnectionMode_AndNeverPersisted()
    {
        var profile = new ServerProfile { IsConnected = true, IsFullConnected = true, WantsFullConnection = true };
        profile.ConnectionFailure = ConnectionFailureKind.Unsupported;
        Assert.IsTrue(profile.IsConnected);
        Assert.IsTrue(profile.IsFullConnected);
        Assert.IsTrue(profile.HasConnectionIssue);
        Assert.IsFalse(profile.IsAccessDenied);
        foreach (var kind in Enum.GetValues<ConnectionFailureKind>())
        {
            profile.ConnectionFailure = kind;
            Assert.IsFalse(string.IsNullOrWhiteSpace(profile.ConnectionIssueTitle));
            Assert.IsFalse(string.IsNullOrWhiteSpace(profile.ConnectionIssueDescription));
            Assert.IsFalse(string.IsNullOrWhiteSpace(profile.ConnectionRecoveryAction));
        }
        profile.ConnectionFailure = ConnectionFailureKind.RePairRequired;
        Assert.IsTrue(profile.IsAccessDenied);
        profile.PlayerToken = "new pairing";
        Assert.IsFalse(profile.HasConnectionIssue);
        profile.ConnectionFailure = ConnectionFailureKind.MissingCompanionId;
        profile.ServerId = Guid.Empty.ToString();
        Assert.IsTrue(profile.HasConnectionIssue);
        profile.ServerId = Guid.NewGuid().ToString();
        Assert.IsFalse(profile.HasConnectionIssue);
        profile.ConnectionFailure = ConnectionFailureKind.SignInRequired;
        var restored = JsonSerializer.Deserialize<ServerProfile>(JsonSerializer.Serialize(profile))!;
        Assert.IsFalse(restored.HasConnectionIssue);
        Assert.IsFalse(restored.WantsFullConnection);
    }
}
