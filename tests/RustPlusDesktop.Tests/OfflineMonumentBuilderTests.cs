using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services;

namespace RustPlusDesktop.Tests;

[TestClass]
public class OfflineMonumentBuilderTests
{
    private const string Metadata = """
    {
      "prefabs": {
        "assets/bundled/prefabs/autospawn/monument/harbor/harbor_1.prefab": {
          "landmarks": [
            { "component_type": "DungeonGridInfo", "display_token": "train_tunnel_display_name", "should_display_on_map": true,
              "local_matrix": [[1,0,0,87],[0,1,0,0],[0,0,1,-48],[0,0,0,1]] },
            { "component_type": "MonumentInfo", "display_token": "harbor_display_name", "should_display_on_map": true,
              "local_matrix": [[1,0,0,0],[0,1,0,0],[0,0,1,0],[0,0,0,1]] }
          ]
        },
        "assets/bundled/prefabs/autospawn/monument/tiny/water_well_a.prefab": {
          "landmarks": [
            { "component_type": "MonumentInfo", "display_token": "water_well_a_display_name", "should_display_on_map": false,
              "local_matrix": [[1,0,0,0],[0,1,0,0],[0,0,1,0],[0,0,0,1]] }
          ]
        },
        "assets/bundled/prefabs/autospawn/monument/underwater_lab/underwater_lab_b.prefab": {
          "landmarks": [
            { "component_type": "DungeonBaseLandmarkInfo", "display_token": null, "should_display_on_map": true,
              "local_matrix": [[1,0,0,0],[0,1,0,0],[0,0,1,0],[0,0,0,1]] }
          ]
        }
      }
    }
    """;

    private static IReadOnlyList<(double X, double Y, string Name)> Build(string mapData)
    {
        using var meta = JsonDocument.Parse(Metadata);
        using var map = JsonDocument.Parse(mapData);
        return OfflineMonumentBuilder.Build(map.RootElement, OfflineMonumentBuilder.ParseMetadata(meta.RootElement));
    }

    [TestMethod]
    public void Monument_IsPlacedInApiCoordinates_WithItsToken()
    {
        var mons = Build("""
        { "size": 4000, "prefabs": [
          { "c": "monument", "i": "harbor_1", "x": 100, "y": -200, "ry": 0,
            "p": "Assets/bundled/Prefabs/autospawn/monument/harbor/harbor_1.prefab" } ] }
        """);

        var harbor = mons.Single(m => m.Name == "harbor_display_name");
        Assert.AreEqual(2100, harbor.X, 1e-6);
        Assert.AreEqual(1800, harbor.Y, 1e-6);

        var tunnel = mons.Single(m => m.Name == "train_tunnel_display_name");
        Assert.AreEqual(2187, tunnel.X, 1e-6);
        Assert.AreEqual(1752, tunnel.Y, 1e-6);
    }

    [TestMethod]
    public void LandmarkOffset_FollowsThePrefabRotation()
    {
        var mons = Build("""
        { "size": 4000, "prefabs": [
          { "c": "monument", "i": "harbor_1", "x": 0, "y": 0, "ry": 90,
            "p": "assets/bundled/prefabs/autospawn/monument/harbor/harbor_1.prefab" } ] }
        """);

        var tunnel = mons.Single(m => m.Name == "train_tunnel_display_name");
        Assert.AreEqual(2000 - 48, tunnel.X, 1e-6);
        Assert.AreEqual(2000 - 87, tunnel.Y, 1e-6);
    }

    [TestMethod]
    public void HiddenLandmarks_AndUnknownPrefabs_AreSkipped()
    {
        var mons = Build("""
        { "size": 4000, "prefabs": [
          { "c": "monument", "i": "water_well_a", "x": 0, "y": 0,
            "p": "Assets/bundled/Prefabs/autospawn/monument/tiny/water_well_a.prefab" },
          { "c": "monument", "i": "underwater_lab_b", "x": 10, "y": 10,
            "p": "Assets/bundled/Prefabs/autospawn/monument/underwater_lab/underwater_lab_b.prefab" },
          { "c": "rock", "i": "rock_1", "x": 0, "y": 0, "p": "assets/content/rock_1.prefab" } ] }
        """);

        Assert.AreEqual(0, mons.Count);
    }

    [TestMethod]
    public void UnderwaterLabMoonpools_BecomeDungeonBases()
    {
        var mons = Build("""
        { "size": 4000, "prefabs": [
          { "c": "underwater-lab-base", "i": "module_900x900_2way_moonpool", "x": 1974, "y": -1107 },
          { "c": "underwater-lab-base", "i": "module_900x900_2way_moonpool", "x": 1972, "y": -1041 } ] }
        """);

        Assert.AreEqual(2, mons.Count(m => m.Name == "DungeonBase"));
        Assert.IsTrue(mons.Any(m => Math.Abs(m.X - 3974) < 1e-6 && Math.Abs(m.Y - 893) < 1e-6));
    }

    [TestMethod]
    public void MissingSizeOrPrefabs_ReturnsEmpty()
    {
        Assert.AreEqual(0, Build("""{ "prefabs": [] }""").Count);
        Assert.AreEqual(0, Build("""{ "size": 4000 }""").Count);
    }
}
