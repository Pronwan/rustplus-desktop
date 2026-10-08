using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk;
using System.Windows;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class MiniMapPlacementTests
{
    [TestMethod]
    [DataRow(1920, 0, 1920, 1080, 3560, 20)]
    [DataRow(-2560, 0, 2560, 1440, -280, 20)]
    [DataRow(0, -1440, 2560, 1440, 2280, -1420)]
    public void NewMap_StartsOnSecondaryMonitor(int x, int y, int width, int height, int expectedX, int expectedY)
    {
        var areas = new[] { new Rect(0, 0, 1920, 1080), new Rect(x, y, width, height) };
        Assert.AreEqual(new Point(expectedX, expectedY),
            MiniMapWindow.GetMapPosition(null, new Size(260, 260), areas, 1));
    }

    [TestMethod]
    public void SavedPosition_IsIndependentAndRestoredOnItsMonitor()
    {
        var areas = new[] { new Rect(0, 0, 1920, 1080), new Rect(1920, 0, 2560, 1440) };
        Assert.AreEqual(new Point(100, 400),
            MiniMapWindow.GetMapPosition(new Point(100, 400), new Size(260, 260), areas, 1));
        Assert.AreEqual(new Point(4100, 900),
            MiniMapWindow.GetMapPosition(new Point(4100, 900), new Size(260, 260), areas, 1));
    }

    [TestMethod]
    public void RemovedMonitor_FallsBackToAvailableDisplay()
    {
        var areas = new[] { new Rect(0, 0, 1920, 1040) };
        Assert.AreEqual(new Point(1640, 20),
            MiniMapWindow.GetMapPosition(new Point(-2400, 300), new Size(260, 260), areas, 0));
        Assert.AreEqual(new Point(1640, 20),
            MiniMapWindow.GetMapPosition(null, new Size(260, 260), areas, 0));
    }

    [TestMethod]
    public void PartlyOffscreenMap_IsRestoredWithinWorkingArea()
    {
        var areas = new[] { new Rect(0, 0, 1920, 1040) };
        Assert.AreEqual(new Point(1660, 780),
            MiniMapWindow.GetMapPosition(new Point(1900, 1000), new Size(260, 260), areas, 0));
    }

}
