using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services.Cloud;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class CloudFeatureFlagsWindowTests
{
    private static string Feed(string flagJson) => $"{{\"data\":{{\"halloween_event\":{flagJson}}}}}";

    private static string Iso(DateTimeOffset t) => t.ToString("O");

    [TestMethod]
    public void MissingKeyIsClosedEvenThoughIsEnabledFailsOpen()
    {
        CloudFeatureFlags.Load("{\"data\":{\"alexa\":{\"enabled\":true}}}");

        Assert.IsTrue(CloudFeatureFlags.IsEnabled("halloween_event"));
        Assert.IsFalse(CloudFeatureFlags.IsWithinWindow("halloween_event"));
    }

    [TestMethod]
    public void DisabledFlagIsClosedInsideItsWindow()
    {
        var now = DateTimeOffset.UtcNow;
        CloudFeatureFlags.Load(Feed($"{{\"enabled\":false,\"starts_at\":\"{Iso(now.AddDays(-1))}\",\"ends_at\":\"{Iso(now.AddDays(1))}\"}}"));

        Assert.IsFalse(CloudFeatureFlags.IsWithinWindow("halloween_event"));
    }

    [TestMethod]
    public void EnabledFlagFollowsItsWindow()
    {
        var now = DateTimeOffset.UtcNow;

        CloudFeatureFlags.Load(Feed($"{{\"enabled\":true,\"starts_at\":\"{Iso(now.AddDays(-1))}\",\"ends_at\":\"{Iso(now.AddDays(1))}\"}}"));
        Assert.IsTrue(CloudFeatureFlags.IsWithinWindow("halloween_event"));

        CloudFeatureFlags.Load(Feed($"{{\"enabled\":true,\"starts_at\":\"{Iso(now.AddHours(1))}\",\"ends_at\":\"{Iso(now.AddDays(1))}\"}}"));
        Assert.IsFalse(CloudFeatureFlags.IsWithinWindow("halloween_event"), "not started yet");

        CloudFeatureFlags.Load(Feed($"{{\"enabled\":true,\"starts_at\":\"{Iso(now.AddDays(-2))}\",\"ends_at\":\"{Iso(now.AddMinutes(-1))}\"}}"));
        Assert.IsFalse(CloudFeatureFlags.IsWithinWindow("halloween_event"), "already ended");
    }

    [TestMethod]
    public void EnabledFlagWithoutBoundsIsOpen()
    {
        CloudFeatureFlags.Load(Feed("{\"enabled\":true,\"starts_at\":null,\"ends_at\":null}"));

        Assert.IsTrue(CloudFeatureFlags.IsWithinWindow("halloween_event"));
    }

    [TestMethod]
    public void OffsetTimestampsAreCompared()
    {
        // Laravel serialises timestamptz as e.g. 2026-10-24T00:00:00.000000Z, but an
        // admin-entered offset must work too.
        var now = DateTimeOffset.UtcNow;
        string start = now.AddHours(-1).ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-ddTHH:mm:sszzz");
        CloudFeatureFlags.Load(Feed($"{{\"enabled\":true,\"starts_at\":\"{start}\",\"ends_at\":\"{now.AddHours(1):yyyy-MM-ddTHH:mm:ss.ffffff}Z\"}}"));

        Assert.IsTrue(CloudFeatureFlags.IsWithinWindow("halloween_event"));
    }
}
