using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Views;

namespace RustPlusDesktop.Tests;

[TestClass]
public class LoadingScreenTests
{
    [STATestMethod]
    public void LoadingScreen_IsOpaque_UpdatesStatus_AndFitsNarrowLayouts()
    {
        var view = new BusyOverlay();
        var status = (TextBlock)view.FindName("StatusText");
        var version = (TextBlock)view.FindName("VersionText");
        var progress = (ProgressBar)view.FindName("LoadingProgress");
        Assert.IsTrue(progress.IsIndeterminate);
        Assert.IsTrue(version.Text.StartsWith("v"));

        foreach (var size in new[] { new Size(640, 360), new Size(320, 480), new Size(160, 500) })
        {
            view.Status = "Synchronizing server dataâ€¦";
            view.Measure(size);
            view.Arrange(new Rect(size));
            view.UpdateLayout();
            Assert.AreEqual(view.Status, status.Text);
            var statusBounds = new Rect(status.TranslatePoint(new Point(), view), status.RenderSize);
            var versionBounds = new Rect(version.TranslatePoint(new Point(), view), version.RenderSize);
            Assert.IsTrue(statusBounds.Right <= size.Width && statusBounds.Left >= 0);
            Assert.IsTrue(statusBounds.Bottom < versionBounds.Top, "Loading status must not overlap the version badge.");
            Assert.IsTrue(progress.TranslatePoint(new Point(), view).Y + progress.ActualHeight < versionBounds.Top);

            var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var pixel = new byte[4];
            bitmap.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
            Assert.AreEqual((byte)255, pixel[3], "The loader must fully obscure the Connect card beneath it.");
        }
    }

}
