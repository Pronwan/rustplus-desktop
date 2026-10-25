using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services.Deaths;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class DeathHeatmapRasterTests
{
    private const double WorldSize = 4000;

    [TestMethod]
    public void Hotspot_StaysHotterThanLoneDeath_WithHundredsOfDeaths()
    {
        // A long wipe: 400 deaths around one hotspot, plus one death somewhere quiet.
        var random = new Random(3);
        var deaths = Enumerable.Range(0, 400)
            .Select(_ => (1000 + random.NextDouble() * 60, 3000 + random.NextDouble() * 60))
            .Append((3000d, 1000d))
            .ToList();

        var bitmap = DeathHeatmapRaster.Render(deaths, WorldSize, 90);

        Assert.IsNotNull(bitmap);
        var hot = Alpha(bitmap, 1030, 3030);
        var lone = Alpha(bitmap, 3000, 1000);
        var empty = Alpha(bitmap, 2000, 2000);
        Assert.IsTrue(hot > lone, $"hotspot {hot} vs lone {lone}");
        Assert.IsTrue(lone > 0, "a single death must stay visible");
        Assert.AreEqual(0, empty);
    }

    [TestMethod]
    public void DeathsOutsideTheWorld_AreIgnored()
    {
        // Deep Sea deaths are reported far west of the playable square.
        Assert.IsNull(DeathHeatmapRaster.Render(new List<(double, double)> { (-4000, 1500) }, WorldSize, 90));
    }

    [TestMethod]
    public void Scale_NeverDropsBelowHotDeaths()
    {
        Assert.AreEqual(DeathHeatmapRaster.HotDeaths, DeathHeatmapRaster.Scale(1));
        Assert.AreEqual(DeathHeatmapRaster.HotDeaths, DeathHeatmapRaster.Scale(0));
        Assert.AreEqual(400, DeathHeatmapRaster.Scale(400));
    }

    [TestMethod]
    public void Intensity_BuildsUpWithStackedDeaths()
    {
        var scale = DeathHeatmapRaster.Scale(1);

        var one = DeathHeatmapRaster.Intensity(1, scale);
        var three = DeathHeatmapRaster.Intensity(3, scale);
        var hot = DeathHeatmapRaster.Intensity(DeathHeatmapRaster.HotDeaths, scale);

        // One death stays at the cold end of the ramp, three reach the middle, a full stack is red.
        Assert.IsTrue(one < 0.35, $"one death: {one}");
        Assert.IsTrue(three is > 0.5 and < 0.75, $"three deaths: {three}");
        Assert.AreEqual(1, hot, 1e-9);
        Assert.AreEqual(0, DeathHeatmapRaster.Intensity(0, scale));
        Assert.AreEqual(1, DeathHeatmapRaster.Intensity(50, scale));
    }

    [TestMethod]
    public void Intensity_OnALongWipe_KeepsMidSizedHotspotsVisible()
    {
        // A 400-death hotspot elsewhere must not wash a 20-death spot down to nothing.
        var twenty = DeathHeatmapRaster.Intensity(20, DeathHeatmapRaster.Scale(400));
        Assert.IsTrue(twenty > 0.45, $"twenty deaths next to a 400-death hotspot: {twenty}");
    }

    [TestMethod]
    public void Opacity_IsFaintForOneDeath_AndRisesWithIntensity()
    {
        var scale = DeathHeatmapRaster.Scale(1);
        var one = DeathHeatmapRaster.Opacity(DeathHeatmapRaster.Intensity(1, scale));
        var three = DeathHeatmapRaster.Opacity(DeathHeatmapRaster.Intensity(3, scale));
        var hot = DeathHeatmapRaster.Opacity(1);

        Assert.AreEqual(0, DeathHeatmapRaster.Opacity(0));
        Assert.IsTrue(one < 0.2, $"one death: {one}");
        Assert.IsTrue(one < three && three < hot, $"{one} < {three} < {hot}");
        Assert.AreEqual(0.85, hot, 1e-9);
    }

    [TestMethod]
    public void LoneDeaths_AreFaintAndCold_WhenNothingIsStacked()
    {
        // Early wipe: two deaths far apart, as in the bug report where each drew a full red blob.
        var bitmap = DeathHeatmapRaster.Render(new List<(double, double)> { (1000, 3000), (3000, 1000) }, WorldSize, 90);

        Assert.IsNotNull(bitmap);
        foreach (var (x, y) in new[] { (1000d, 3000d), (3000d, 1000d) })
        {
            var (r, g, b, a) = Pixel(bitmap, x, y);
            Assert.IsTrue(a > 0, "a single death must stay visible");
            Assert.IsTrue(a < 0.2 * 255, $"lone death alpha {a}");
            Assert.IsTrue(b > r, $"lone death should sit on the cold end, got r{r} g{g} b{b}");
        }
    }

    [TestMethod]
    public void StackedDeaths_BuildUpToHotColours_AndAWiderMark()
    {
        var stack = Enumerable.Repeat((2000d, 2000d), 20).ToList();
        var single = new List<(double, double)> { (2000, 2000) };

        var hotBitmap = DeathHeatmapRaster.Render(stack, WorldSize, 90)!;
        var loneBitmap = DeathHeatmapRaster.Render(single, WorldSize, 90)!;

        var (r, g, b, a) = Pixel(hotBitmap, 2000, 2000);
        Assert.IsTrue(a > 0.8 * 255, $"20 stacked deaths alpha {a}");
        Assert.IsTrue(r > 200 && g < 80 && b < 40, $"20 stacked deaths should be red, got r{r} g{g} b{b}");

        // The visible footprint grows with the count, without changing the kernel radius.
        Assert.IsTrue(VisiblePixels(hotBitmap) > VisiblePixels(loneBitmap) * 1.5,
            $"stack {VisiblePixels(hotBitmap)} vs lone {VisiblePixels(loneBitmap)}");
    }

    private static byte Alpha(System.Windows.Media.Imaging.BitmapSource bitmap, double worldX, double worldY)
        => Pixel(bitmap, worldX, worldY).A;

    // Un-premultiplied colour of the pixel under a world position.
    private static (int R, int G, int B, byte A) Pixel(System.Windows.Media.Imaging.BitmapSource bitmap, double worldX, double worldY)
    {
        var size = bitmap.PixelWidth;
        var column = (int)(worldX / WorldSize * size);
        var row = (int)((WorldSize - worldY) / WorldSize * size);
        var pixel = new byte[4];
        bitmap.CopyPixels(new System.Windows.Int32Rect(column, row, 1, 1), pixel, 4, 0);
        var a = pixel[3];
        if (a == 0)
            return (0, 0, 0, 0);
        return (pixel[2] * 255 / a, pixel[1] * 255 / a, pixel[0] * 255 / a, a);
    }

    // Pixels opaque enough to actually read on the map (alpha above ~5%).
    private static int VisiblePixels(System.Windows.Media.Imaging.BitmapSource bitmap)
    {
        var size = bitmap.PixelWidth;
        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(pixels, size * 4, 0);
        var count = 0;
        for (var i = 3; i < pixels.Length; i += 4)
            if (pixels[i] > 12)
                count++;
        return count;
    }
}
