using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services;

namespace RustPlusDesktop.Tests;

[TestClass]
public class MonumentFormatterTests
{
    [TestMethod]
    public void Beautify_HandlesNullOrWhitespace()
    {
        Assert.AreEqual(string.Empty, MonumentFormatter.Beautify(null));
        Assert.AreEqual(string.Empty, MonumentFormatter.Beautify(""));
        Assert.AreEqual(string.Empty, MonumentFormatter.Beautify("   "));
    }

    [TestMethod]
    public void Beautify_ScreenshotMonuments_AreBeautifiedCorrectly()
    {
        Assert.AreEqual("Train Tunnel", MonumentFormatter.Beautify("train_tunnel_display_name"));
        Assert.AreEqual("Mining Outpost", MonumentFormatter.Beautify("mining_outpost_display_name"));
        Assert.AreEqual("Lighthouse", MonumentFormatter.Beautify("lighthouse_display_name"));
        Assert.AreEqual("Harbor", MonumentFormatter.Beautify("harbor_display_name"));
        Assert.AreEqual("Harbor", MonumentFormatter.Beautify("harbor_2"));
        Assert.AreEqual("Large Fishing Village", MonumentFormatter.Beautify("large_fishing_village_display_name"));
        Assert.AreEqual("Fishing Village", MonumentFormatter.Beautify("fishing_village_display_name"));
        Assert.AreEqual("Sulfur Quarry", MonumentFormatter.Beautify("mining_quarry_sulfur_display_name"));
        Assert.AreEqual("Stone Quarry", MonumentFormatter.Beautify("mining_quarry_stone_display_name"));
        Assert.AreEqual("HQM Quarry", MonumentFormatter.Beautify("mining_quarry_hqm_display_name"));
        Assert.AreEqual("Jungle Ziggurat", MonumentFormatter.Beautify("jungle_ziggurat"));
        Assert.AreEqual("Ranch", MonumentFormatter.Beautify("stables_a"));
        Assert.AreEqual("Large Barn", MonumentFormatter.Beautify("stables_b"));
        Assert.AreEqual("Large Oil Rig", MonumentFormatter.Beautify("large_oil_rig"));
        Assert.AreEqual("Underwater Labs", MonumentFormatter.Beautify("underwater_lab"));
        Assert.AreEqual("Swamp", MonumentFormatter.Beautify("swamp"));
        Assert.AreEqual("Outpost", MonumentFormatter.Beautify("outpost"));
    }

    [TestMethod]
    public void Beautify_StandardMonuments_AreBeautifiedCorrectly()
    {
        Assert.AreEqual("Launch Site", MonumentFormatter.Beautify("launchsite"));
        Assert.AreEqual("Launch Site", MonumentFormatter.Beautify("launch_facility"));
        Assert.AreEqual("Missile Silo", MonumentFormatter.Beautify("missile_silo_monument"));
        Assert.AreEqual("Dome", MonumentFormatter.Beautify("sphere_tank"));
        Assert.AreEqual("Dome", MonumentFormatter.Beautify("dome_monument"));
        Assert.AreEqual("Airfield", MonumentFormatter.Beautify("airfield_display_name"));
        Assert.AreEqual("Power Plant", MonumentFormatter.Beautify("powerplant_display_name"));
        Assert.AreEqual("Water Treatment Plant", MonumentFormatter.Beautify("water_treatment_plant_display_name"));
        Assert.AreEqual("Train Yard", MonumentFormatter.Beautify("trainyard_display_name"));
        Assert.AreEqual("Military Tunnels", MonumentFormatter.Beautify("military_tunnel_display_name"));
        Assert.AreEqual("Abandoned Military Base", MonumentFormatter.Beautify("abandonedmilitarybase"));
        Assert.AreEqual("Arctic Research Base", MonumentFormatter.Beautify("arctic_base"));
        Assert.AreEqual("Small Oil Rig", MonumentFormatter.Beautify("small_oil_rig"));
        Assert.AreEqual("Abandoned Supermarket", MonumentFormatter.Beautify("supermarket_1"));
        Assert.AreEqual("Oxum's Gas Station", MonumentFormatter.Beautify("gas_station"));
        Assert.AreEqual("Sewer Branch", MonumentFormatter.Beautify("sewer_display_name"));
        Assert.AreEqual("Large Excavator Pit", MonumentFormatter.Beautify("giant_excavator"));
        Assert.AreEqual("Apartments Complex", MonumentFormatter.Beautify("apartment_complex"));
        Assert.AreEqual("Bandit Camp", MonumentFormatter.Beautify("bandit_town"));
        Assert.AreEqual("Ferry Terminal", MonumentFormatter.Beautify("ferryterminal"));
        Assert.AreEqual("Satellite Dish", MonumentFormatter.Beautify("satellite_dish_display_name"));
        Assert.AreEqual("Junkyard", MonumentFormatter.Beautify("junkyard_display_name"));
        Assert.AreEqual("Radtown", MonumentFormatter.Beautify("radtown_small"));
    }

    [TestMethod]
    public void Beautify_PrefabPathsAndCustomTokens_AreCleanedAndTitleCased()
    {
        Assert.AreEqual("Water Well", MonumentFormatter.Beautify("assets/bundled/prefabs/autospawn/monument/water_well.prefab"));
        Assert.AreEqual("Custom Event Area", MonumentFormatter.Beautify("custom_event_area_display_name"));
        Assert.AreEqual("Canyon", MonumentFormatter.Beautify("canyon"));
    }
}
