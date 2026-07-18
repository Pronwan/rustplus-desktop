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
}
