using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services.Deaths;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class DeathLocationClassifierTests
{
    private const double WorldSize = 4000;
    private const double Pad = 2000;

    private static readonly (byte B, byte G, byte R) Sea = (0x4D, 0x40, 0x12);
    private static readonly (byte B, byte G, byte R) Land = (0x5A, 0x8C, 0xA0);

    [TestMethod]
    public void WaterMask_ReadsSeaRiverAndLand_FromTheMapImage()
    {
        var mask = BuildIsland();

        Assert.IsTrue(mask.IsWater(200, 200), "the coast band around the island is sea");
        Assert.IsFalse(mask.IsWater(1500, 1500), "the island is land");
        Assert.IsTrue(mask.IsWater(2000, 1500), "the river through the island is water");
        Assert.IsTrue(mask.IsWater(-50, 2000), "beyond the world square is ocean");
        Assert.IsTrue(mask.IsWater(2000, WorldSize + 50), "beyond the world square is ocean");
    }

    [TestMethod]
    public void Classify_DeepSea_BeatsEverything()
    {
        var classifier = new DeathLocationClassifier(
            [new DeathZone(-3000, 2000, 500, "Deep Sea Platform")],
            (_, _) => "Main base",
            (_, _) => "A1",
            (_, _) => true);

        Assert.AreEqual((DeathLocationClassifier.DeepSeaType, (string?)null), classifier.Classify(-3000, 2000));
    }

    [TestMethod]
    public void Classify_MonumentOnWater_KeepsTheMonument()
    {
        var mask = BuildIsland();
        var classifier = new DeathLocationClassifier(
            [new DeathZone(200, 200, 150, "Small Oil Rig")],
            (_, _) => null,
            (_, _) => null,
            mask.IsWater);

        Assert.AreEqual(("monument", (string?)"Small Oil Rig"), classifier.Classify(200, 200));
        Assert.AreEqual((DeathLocationClassifier.WaterType, (string?)null), classifier.Classify(600, 200));
        Assert.AreEqual(("open", (string?)null), classifier.Classify(1500, 1500));
    }

    [TestMethod]
    public void Classify_WithoutAMap_StaysOpen()
    {
        var classifier = new DeathLocationClassifier([], (_, _) => null, (_, _) => null);

        Assert.AreEqual(("open", (string?)null), classifier.Classify(600, 200));
    }

    /// <summary>
    /// A map image like Rust+ draws it: ocean padding, a square island from 1000 to 3000 in
    /// world units, and a north-south river at x = 2000.
    /// </summary>
    private static MapWaterMask BuildIsland()
    {
        const int size = 600;
        int stride = size * 4;
        var pixels = new byte[stride * size];
        double total = WorldSize + Pad;

        for (int v = 0; v < size; v++)
        {
            for (int u = 0; u < size; u++)
            {
                double x = (u / (double)(size - 1) * total) - (Pad / 2);
                double y = WorldSize - ((v / (double)(size - 1) * total) - (Pad / 2));
                bool island = x is >= 1000 and <= 3000 && y is >= 1000 and <= 3000;
                bool river = Math.Abs(x - 2000) < 60;
                var colour = island && !river ? Land : Sea;

                int i = (v * stride) + (u * 4);
                pixels[i] = colour.B;
                pixels[i + 1] = colour.G;
                pixels[i + 2] = colour.R;
                pixels[i + 3] = 255;
            }
        }

        return MapWaterMask.Build(pixels, size, size, stride, WorldSize, Pad);
    }
}
