using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RustPlusDesktop.Tests;

[TestClass]
public class ServerMapTransitionTests
{
    [STATestMethod]
    public void Snapshot_PreservesViewportAspectAndContent_AtBoundedMemoryCost()
    {
        var viewport = new Border { Background = Brushes.DarkSlateGray };
        viewport.Measure(new Size(4000, 2000));
        viewport.Arrange(new Rect(0, 0, 4000, 2000));
        viewport.UpdateLayout();
        var snapshot = MainWindow.CaptureServerMapTransition(viewport)!;
        Assert.AreEqual(1280, snapshot.PixelWidth);
        Assert.AreEqual(640, snapshot.PixelHeight);
        Assert.IsTrue(snapshot.IsFrozen);
        var pixel = new byte[4];
        snapshot.CopyPixels(new Int32Rect(100, 100, 1, 1), pixel, 4, 0);
        CollectionAssert.AreEqual(new byte[] { 79, 79, 47, 255 }, pixel);
    }

    [STATestMethod]
    public void Snapshot_SkipsUnlaidOutViewport_AndNeverUpscalesSmallWindows()
    {
        var viewport = new Border { Background = Brushes.Black };
        Assert.IsNull(MainWindow.CaptureServerMapTransition(viewport));
        viewport.Measure(new Size(320, 180));
        viewport.Arrange(new Rect(0, 0, 320, 180));
        Assert.AreEqual(320, MainWindow.CaptureServerMapTransition(viewport)!.PixelWidth);
    }
}
