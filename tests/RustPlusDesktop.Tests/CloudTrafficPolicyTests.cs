using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services;

namespace RustPlusDesktop.Tests;

[TestClass]
public class CloudTrafficPolicyTests
{
    [TestMethod]
    public void Intervals_AdjustWhenMinimized()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(60), CloudTrafficPolicy.TeamHeartbeatInterval(false));
        Assert.AreEqual(TimeSpan.FromSeconds(120), CloudTrafficPolicy.TeamHeartbeatInterval(true));
        Assert.AreEqual(TimeSpan.FromMinutes(15), CloudTrafficPolicy.ProfileTouchInterval(false));
        Assert.AreEqual(TimeSpan.FromMinutes(30), CloudTrafficPolicy.ProfileTouchInterval(true));
        Assert.AreEqual(TimeSpan.FromMinutes(5), CloudTrafficPolicy.PresenceInterval(false));
        Assert.AreEqual(TimeSpan.FromMinutes(10), CloudTrafficPolicy.PresenceInterval(true));
    }

    [TestMethod]
    public async Task MinimizedState_CanBePublishedAcrossThreads()
    {
        CloudTrafficPolicy.IsMinimized = true;
        Assert.IsTrue(await Task.Run(() => CloudTrafficPolicy.IsMinimized));
        CloudTrafficPolicy.IsMinimized = false;
    }

    [TestMethod]
    public void UpgradeBlock_AppliesOnlyToCachedClientVersion()
    {
        Assert.IsTrue(CloudTrafficPolicy.IsUpgradeBlockedVersion(null, "7.1.0", "7.1.0"));
        Assert.IsFalse(CloudTrafficPolicy.IsUpgradeBlockedVersion(null, "7.1.0", "7.1.1"));
        Assert.IsTrue(CloudTrafficPolicy.IsUpgradeBlockedVersion("7.2.0", "7.1.0", "7.1.5"));
        Assert.IsFalse(CloudTrafficPolicy.IsUpgradeBlockedVersion("7.2.0", "7.1.0", "7.2.0"));
    }

    [TestMethod]
    public void UpgradeBlockByMinimum_OnlyHonoursConcreteMinimum()
    {
        // Genuine "below the minimum" still blocks until the build is updated.
        Assert.IsTrue(CloudTrafficPolicy.IsUpgradeBlockedByMinimumVersion("7.2.0", "7.1.5"));
        Assert.IsFalse(CloudTrafficPolicy.IsUpgradeBlockedByMinimumVersion("7.2.0", "7.2.0"));
        Assert.IsFalse(CloudTrafficPolicy.IsUpgradeBlockedByMinimumVersion("7.2.0", "7.3.0"));

        // A stale cache with no comparable minimum must NOT latch the current build —
        // this is the exact-version fallback that permanently blocked test/rebuilt
        // builds, and it is deliberately absent here.
        Assert.IsFalse(CloudTrafficPolicy.IsUpgradeBlockedByMinimumVersion(null, "7.1.0"));
        Assert.IsFalse(CloudTrafficPolicy.IsUpgradeBlockedByMinimumVersion("", "7.1.0"));
        Assert.IsFalse(CloudTrafficPolicy.IsUpgradeBlockedByMinimumVersion("not-a-version", "7.1.0"));

        // Version prefixes/suffixes are tolerated the same way the caching path emits them.
        Assert.IsTrue(CloudTrafficPolicy.IsUpgradeBlockedByMinimumVersion("v7.2.0", "7.1.0-beta"));
    }
}
