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

    private static byte Alpha(System.Windows.Media.Imaging.BitmapSource bitmap, double worldX, double worldY)
    {
        var size = bitmap.PixelWidth;
        var column = (int)(worldX / WorldSize * size);
        var row = (int)((WorldSize - worldY) / WorldSize * size);
        var pixel = new byte[4];
        bitmap.CopyPixels(new System.Windows.Int32Rect(column, row, 1, 1), pixel, 4, 0);
        return pixel[3];
    }
}
