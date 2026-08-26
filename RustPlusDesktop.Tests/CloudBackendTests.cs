using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services.Cloud;

namespace RustPlusDesktop.Tests;

[TestClass]
public class CloudBackendTests
{
    [TestMethod]
    public void Mode_Targetscloud_AfterTheCutover()
    {
        Assert.AreEqual(CloudBackendMode.Platform, CloudBackend.Mode);
        Assert.IsTrue(CloudBackend.UsePlatform);
    }

    [DataTestMethod]
    [DataRow("user-profile/limits", "me/limits")]
    [DataRow("user-profile/presence", "profile/presence")]
    [DataRow("user-profile/consent", "profile/consent")]
    [DataRow("user-profile", "profile")]
    [DataRow("discord-roles", "me/discord/sync-roles")]
    [DataRow("team-member-overlay", "sync/team-member-overlay")]
    [DataRow("/user-profile/limits/", "me/limits")]
    public void MapEdgeFunctionToRoute_MapsKnownFunctions(string edge, string expected)
    {
        Assert.AreEqual(expected, CloudBackend.MapEdgeFunctionToRoute(edge));
    }

    [DataTestMethod]
    [DataRow("overlay/purge-orphaned", "overlay/purge-orphaned")]
    [DataRow("user-profile/touch", "profile/touch")]
    [DataRow("team-feature/heartbeat", "team-feature/heartbeat")]
    [DataRow("team-feature/master", "team-feature/master")]
    [DataRow("team-feature/has-master", "team-feature/has-master")]
    public void MapEdgeFunctionToRoute_MapsSyncAndTeamFunctions(string edge, string expected)
    {
        Assert.AreEqual(expected, CloudBackend.MapEdgeFunctionToRoute(edge));
    }

    [TestMethod]
    public void MapEdgeFunctionToRoute_RoutesOverlayByMethod()
    {
        // Legacy contract for reads/writes; the versioned sync route for deletes.
        Assert.AreEqual("overlay", CloudBackend.MapEdgeFunctionToRoute("overlay", "GET"));
        Assert.AreEqual("overlay", CloudBackend.MapEdgeFunctionToRoute("overlay", "POST"));
        Assert.AreEqual("sync/overlay", CloudBackend.MapEdgeFunctionToRoute("overlay", "DELETE"));
    }

    [DataTestMethod]
    [DataRow("user-profile/claim")]
    [DataRow("discord-bot/settings")]
    [DataRow("admin/check")]
    [DataRow("unknown-function")]
    [DataRow("")]
    [DataRow("   ")]
    public void MapEdgeFunctionToRoute_ReturnsNull_ForUnmapped(string edge)
    {
        Assert.IsNull(CloudBackend.MapEdgeFunctionToRoute(edge));
    }

    [TestMethod]
    public void ApiUrl_NormalisesSlashes()
    {
        Assert.AreEqual(
            "https://api.example.com/api/v1/profile/presence",
            CloudBackend.ApiUrl("https://api.example.com", "/profile/presence"));
    }
}
