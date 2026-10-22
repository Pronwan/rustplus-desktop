using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services;

namespace RustPlusDesktop.Tests;

[TestClass]
public class ResourceHeatmapRasterTests
{
    private const int N = ResourceHeatmapRaster.Size;

    private static byte Alpha(int pixel) => (byte)((uint)pixel >> 24);

    [TestMethod]
    public void WrongSize_ReturnsNull()
    {
        Assert.IsNull(ResourceHeatmapRaster.Colorize(new byte[100]));
    }

    [TestMethod]
    public void EmptyHeatmap_IsFullyTransparent()
    {
        var pixels = ResourceHeatmapRaster.Colorize(new byte[N * N])!;
        Assert.IsTrue(pixels.All(p => p == 0));
    }

    [TestMethod]
    public void LowRangeHeatmap_IsScaledToItsOwnRange()
    {
        // Like wood: every filled cell is around 20 of 255.
        var raw = new byte[N * N];
        for (int y = 100; y < 200; y++)
            for (int x = 100; x < 200; x++)
                raw[y * N + x] = 20;

        var pixels = ResourceHeatmapRaster.Colorize(raw)!;
        Assert.AreEqual(210, Alpha(pixels[150 * N + 150]), "The heatmap's own peak shows at full strength.");
    }

    [TestMethod]
    public void BlurredEdges_FadeOutInsteadOfTintingTheMap()
    {
        var raw = new byte[N * N];
        raw[256 * N + 256] = 255;
        for (int i = 0; i < 40; i++) raw[(10 + i) * N + 10] = 255;

        var pixels = ResourceHeatmapRaster.Colorize(raw)!;
        // Three cells from a lone hot cell the blur still carries a little value; it stays clear.
        Assert.AreEqual(0, Alpha(pixels[256 * N + 259]));
        Assert.IsTrue(Alpha(pixels[256 * N + 256]) > 150);
        Assert.AreEqual(0, Alpha(pixels[400 * N + 400]));
    }

    [TestMethod]
    public void Ramp_RunsYellowToRed_WithRedLeading()
    {
        Assert.AreEqual((255, 215, 0), ResourceHeatmapRaster.Ramp(0f));
        Assert.AreEqual((215, 25, 25), ResourceHeatmapRaster.Ramp(1f));
        for (float t = 0; t <= 1f; t += 0.1f)
        {
            var (r, g, _) = ResourceHeatmapRaster.Ramp(t);
            Assert.IsTrue(r >= g, $"Red should lead at t={t}.");
        }
    }

    [TestMethod]
    public void WeakAreas_StayClear_SoTheMapIsNotWashedInColour()
    {
        // A dense half at the heatmap's top value beside a wide half at a third of it.
        var raw = new byte[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N / 2; x++)
                raw[y * N + x] = 40;
        for (int y = 0; y < N; y++)
            for (int x = N / 2; x < N; x++)
                raw[y * N + x] = 120;

        var pixels = ResourceHeatmapRaster.Colorize(raw)!;
        Assert.AreEqual(0, Alpha(pixels[256 * N + 100]), "A third of the reference should not be coloured.");
        Assert.IsTrue(Alpha(pixels[256 * N + 400]) > 150);
    }

    [TestMethod]
    public void FullResolutionHeatmap_IsColouredAtItsOwnSize()
    {
        const int size = 2048;
        var raw = new byte[size * size];
        for (int y = 1000; y < 1010; y++)
            for (int x = 1000; x < 1010; x++)
                raw[y * size + x] = 200;

        var pixels = ResourceHeatmapRaster.Colorize(raw, size)!;
        Assert.AreEqual(size * size, pixels.Length);
        Assert.IsTrue(Alpha(pixels[1005 * size + 1005]) > 150);
        Assert.AreEqual(0, Alpha(pixels[10 * size + 10]));
    }

    [TestMethod]
    public void ReferenceLevel_IgnoresAFewExtremeCells()
    {
        var raw = new byte[N * N];
        for (int i = 0; i < 1000; i++) raw[i] = 40;
        for (int i = 1000; i < 1010; i++) raw[i] = 255;
        Assert.AreEqual(40, ResourceHeatmapRaster.ReferenceLevel(raw));
    }
}
