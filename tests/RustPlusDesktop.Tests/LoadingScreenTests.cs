using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Xml.Linq;
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
            view.Status = "Synchronizing server data…";
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

    [STATestMethod]
    public void ServerConnection_ReplacesConnectCard_WithoutStartupArtwork()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var document = XDocument.Load(Path.Combine(root, "src/RustPlusDesktop/MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var loader = new XElement(document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "MapConnectionLoader"));
        Assert.IsFalse(loader.Descendants(wpf + "Image").Any(), "Server connection must not show startup artwork.");
        var placeholder = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "MapPlaceholder");
        var card = new XElement(wpf + "Border", new XElement(placeholder.Element(wpf + "Border")!.Element(wpf + "Border.Style")!));
        card.SetAttributeValue(x + "Name", "ConnectCard");
        var markup = new XElement(wpf + "Grid",
            new XAttribute(XNamespace.Xmlns + "x", x.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "conv", "clr-namespace:RustPlusDesk.Converters;assembly=RustPlusDesk"),
            new XElement(wpf + "Grid.Resources", new XElement(XName.Get("BoolToVisibilityConverter", "clr-namespace:RustPlusDesk.Converters;assembly=RustPlusDesk"), new XAttribute(x + "Key", "BoolToVisibility"))),
            card, loader);
        var host = (Grid)XamlReader.Parse(markup.ToString());
        foreach (bool loading in new[] { false, true, false })
        {
            host.DataContext = new { IsConnectionLoading = loading, BusyText = "Connecting…" };
            host.Measure(new Size(640, 480));
            host.Arrange(new Rect(0, 0, 640, 480));
            host.UpdateLayout();
            Assert.AreEqual(loading ? Visibility.Collapsed : Visibility.Visible, host.Children[0].Visibility);
            Assert.AreEqual(loading ? Visibility.Visible : Visibility.Collapsed, host.Children[1].Visibility);
        }
    }
}
