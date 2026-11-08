using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services.Map;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class MapIconCacheTests
{
    [TestMethod]
    public void DecodeSize_ShrinksByTheLongerSide_AndNeverEnlarges()
    {
        Assert.AreEqual((256, 0), MapIconCache.DecodeSize(700, 700));
        Assert.AreEqual((256, 0), MapIconCache.DecodeSize(664, 287));
        Assert.AreEqual((0, 256), MapIconCache.DecodeSize(287, 664));
        Assert.AreEqual((0, 0), MapIconCache.DecodeSize(128, 128));
        Assert.AreEqual((0, 0), MapIconCache.DecodeSize(256, 256));
    }

    [TestMethod]
    public void Get_SharesOneCappedBitmapPerFile()
    {
        string path = WriteIcon(700, 350);
        try
        {
            string uri = new Uri(path).AbsoluteUri;
            var first = MapIconCache.Get(uri);

            Assert.AreSame(first, MapIconCache.Get(uri), "Every element drawing an icon must share one decode.");
            Assert.IsTrue(first.IsFrozen);
            Assert.AreEqual(MapIconCache.MaxDecodePx, first.PixelWidth);
            Assert.AreEqual(128, first.PixelHeight);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Get_LeavesSmallIconsAtTheirOwnSize()
    {
        string path = WriteIcon(40, 40);
        try
        {
            Assert.AreEqual(40, MapIconCache.Get(new Uri(path).AbsoluteUri).PixelWidth);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteIcon(int width, int height)
    {
        int stride = width * 4;
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, new byte[stride * height], stride);
        string path = Path.Combine(Path.GetTempPath(), $"icon-{Guid.NewGuid():N}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }
}
