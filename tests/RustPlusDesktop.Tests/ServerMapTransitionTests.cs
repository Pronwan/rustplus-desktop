using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RustPlusDesktop.Tests;

[TestClass]
public class ServerMapTransitionTests
{
    [STATestMethod]
    public void ImageBounds_PreserveViewportPanAndZoom_WithoutRenderingAScreenshot()
    {
        var image = new Image { Width = 1000, Height = 500, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        image.Source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 255 }, 4);
        image.RenderTransform = new TransformGroup { Children = { new ScaleTransform(2, 2), new TranslateTransform(-300, 40) } };
        var viewport = new Grid { Children = { image } };
        viewport.Measure(new Size(800, 600));
        viewport.Arrange(new Rect(0, 0, 800, 600));
        viewport.UpdateLayout();
        Assert.AreEqual(new Rect(-300, 40, 2000, 1000), MainWindow.ServerMapTransitionBounds(image, viewport));
    }

    [STATestMethod]
    public void ImageBounds_SkipUnlaidOutMaps_AndPreserveSmallImageSize()
    {
        var viewport = new Border { Background = Brushes.Black };
        Assert.IsTrue(MainWindow.ServerMapTransitionBounds(viewport, viewport).IsEmpty);
        viewport.Measure(new Size(320, 180));
        viewport.Arrange(new Rect(0, 0, 320, 180));
        Assert.AreEqual(new Rect(0, 0, 320, 180), MainWindow.ServerMapTransitionBounds(viewport, viewport));
    }
}
