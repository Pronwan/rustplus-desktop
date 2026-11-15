using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Cloud;

namespace RustPlusDesktop.Tests;

/// <summary>
/// A modded server's upkeep cost and oil rig crate time: Rust+ reports protection at vanilla
/// costs, so the app scales it, and the values come back from the website with the pairings.
/// </summary>
[TestClass]
public class ServerModifiersTests
{
    private const string ServerId = "97bec487-47ad-4077-b3db-f28694ba19d2";

    private static string Body(string modifiers) =>
        "{\"data\":[{\"server_key\":\"" + ServerId + "\",\"name\":\"Limitless\",\"host\":null,\"port\":null," +
        "\"facepunch_id\":\"" + ServerId + "\",\"steam_id\":\"76561190000000123\",\"player_token\":\"555\"," +
        "\"updated_at\":\"2026-11-15T09:59:00+00:00\",\"devices\":[]," +
        "\"modifiers\":" + modifiers + "}],\"meta\":{\"as_of\":\"2026-11-15T10:00:00+00:00\"}}";

    private static ServerProfile Profile() => new()
    {
        Name = "Limitless", Host = "companion-socket", Port = 28082, ServerId = ServerId, PlayerToken = "555",
    };

    [TestMethod]
    public void Double_upkeep_halves_the_time_Rust_reports_and_half_upkeep_doubles_it()
    {
        var profile = Profile();
        int reported = 33 * 3600; // what Rust+ says at vanilla costs

        profile.UpkeepPercent = 200;
        Assert.AreEqual(16 * 3600 + 1800, profile.ScaleUpkeep(reported));

        profile.UpkeepPercent = 50;
        Assert.AreEqual(66 * 3600, profile.ScaleUpkeep(reported));

        profile.UpkeepPercent = 100;
        Assert.AreEqual(reported, profile.ScaleUpkeep(reported));
        Assert.IsNull(profile.ScaleUpkeep(null));
    }

    [TestMethod]
    public void Values_outside_what_a_server_could_have_are_clamped()
    {
        var profile = Profile();
        profile.UpkeepPercent = 5000;
        profile.OilRigCrateMinutes = 0;

        Assert.AreEqual(ServerProfile.MaxUpkeepPercent, profile.UpkeepPercent);
        Assert.AreEqual(ServerProfile.VanillaOilRigCrateMinutes, profile.OilRigCrateMinutes);
    }

    [TestMethod]
    public void Modifiers_set_on_the_website_come_back_with_the_pairing()
    {
        var profile = Profile();
        var profiles = new List<ServerProfile> { profile };

        CloudPairingImporter.Merge(Body("{\"upkeep_percent\":200,\"oil_rig_crate_minutes\":10,\"modified\":true}"), profiles, () => { });

        Assert.AreEqual(200, profile.UpkeepPercent);
        Assert.AreEqual(10, profile.OilRigCrateMinutes);
        Assert.IsTrue(profile.HasCustomModifiers);
    }

    [TestMethod]
    public void A_change_made_here_and_not_sent_yet_is_not_overwritten_by_the_import()
    {
        var profile = Profile();
        profile.UpkeepPercent = 50;
        profile.ModifiersPushPending = true;
        var profiles = new List<ServerProfile> { profile };

        CloudPairingImporter.Merge(Body("{\"upkeep_percent\":100,\"oil_rig_crate_minutes\":15,\"modified\":false}"), profiles, () => { });

        Assert.AreEqual(50, profile.UpkeepPercent);
    }
}
