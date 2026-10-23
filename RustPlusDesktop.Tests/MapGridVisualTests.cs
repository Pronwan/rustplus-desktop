using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Views;

namespace RustPlusDesktop.Tests;

[TestClass]
public class MapGridVisualTests
{
    [STATestMethod]
    public void Grid_PreservesCellBoundaries_Labels_AndOceanPadding()
    {
        foreach (double worldSize in new[] { 1000d, 3000, 4250, 4500, 6000 })
        {
            int cells = MainWindow.GridCellCount(worldSize);
            double cell = MainWindow.GridCellSize(worldSize);
            Assert.AreEqual(worldSize, cell * cells, 1e-8);
            var bounds = new Rect(100, 125, worldSize * 0.8, worldSize * 0.8);
            var drawing = new MapGridDrawing(bounds, new Size(bounds.Right + 100, bounds.Bottom + 125), cells, false);
            Assert.IsTrue(drawing.Lines.IsFrozen);
            Assert.AreEqual(cells * cells, drawing.Labels.Length);
            for (int col = 0; col < cells; col++)
            for (int row = 0; row < cells; row++)
            {
                var (origin, glyph) = drawing.Labels[col * cells + row];
                Assert.AreEqual(bounds.Left + col * cell * 0.8, origin.X, 1e-8);
                Assert.AreEqual(bounds.Top + row * cell * 0.8, origin.Y, 1e-8);
                Assert.IsTrue(glyph.IsFrozen);
                Assert.IsInstanceOfType<ImageDrawing>(glyph.Children.Single());
                Assert.IsTrue(((BitmapSource)((ImageDrawing)glyph.Children.Single()).ImageSource).IsFrozen,
                    "Zoom must reuse pre-rendered label pixels instead of rasterizing glyphs again.");
            }
            var lines = PathGeometry.CreateFromGeometry(drawing.Lines).Figures;
            Assert.IsTrue(lines.Any(line => Math.Abs(line.StartPoint.X - bounds.Left) < 1e-8));
            Assert.IsTrue(lines.Any(line => Math.Abs(line.StartPoint.X - bounds.Right) < 1e-8));
        }
        Assert.AreEqual(29, MainWindow.GridCellCount(4250));
        Assert.AreEqual("A", MainWindow.ColumnLabel(0));
        Assert.AreEqual("Z", MainWindow.ColumnLabel(25));
        Assert.AreEqual("AA", MainWindow.ColumnLabel(26));
        Assert.AreEqual("AC", MainWindow.ColumnLabel(28));
    }

    [STATestMethod]
    public void Grid_ReusesVectorsDuringFollow_AndKeepsEachViewIndependent()
    {
        var data = new MapGridDrawing(new Rect(40, 40, 580, 580), new Size(660, 660), 29, false);
        var main = new MapGridVisual();
        var mini = new MapGridVisual();
        main.SetDrawing(data);
        mini.SetDrawing(data);
        Assert.AreEqual(2, VisualTreeHelper.GetChildrenCount(main));
        Assert.IsTrue(main.SetViewScale(3, false));
        var mainLines = ((DrawingVisual)VisualTreeHelper.GetChild(main, 0)).Drawing;
        for (int frame = 0; frame < 120; frame++)
            Assert.IsFalse(main.SetViewScale(3, false), "Unchanged zoom must not redraw during follow/polling.");
        Assert.IsTrue(mini.SetViewScale(0.5, false));
        Assert.AreSame(data, mini.Drawing);
        Assert.AreEqual(mainLines.Bounds, ((DrawingVisual)VisualTreeHelper.GetChild(main, 0)).Drawing.Bounds);
        var labels = (DrawingVisual)VisualTreeHelper.GetChild(main, 1);
        Assert.IsFalse(labels.Drawing.Bounds.IsEmpty);
        int fullLabelCount = labels.Drawing.Children.Count;
        main.SetViewport(new Rect(200, 200, 60, 60));
        Assert.IsTrue(labels.Drawing.Children.Count < fullLabelCount / 4,
            "Zoomed views should draw only nearby labels, not every cell on the server.");
        Assert.IsTrue(((DrawingVisual)VisualTreeHelper.GetChild(mini, 1)).Drawing == null,
            "Overview labels should hide when cells are too small to read.");
        Assert.IsTrue(main.SetViewScale(3, true));
        Assert.IsFalse(main.SetViewScale(double.NaN, false));
        main.SetDrawing(null);
        Assert.IsNull(main.Drawing);
        Assert.IsNull(((DrawingVisual)VisualTreeHelper.GetChild(main, 0)).Drawing);
    }

    [STATestMethod]
    public void DeepSea_AndFullMapBrush_RenderWithoutLayoutControls()
    {
        double side = MainWindow.DeepSeaCells * MainWindow.DeepSeaCellSize;
        var bounds = new Rect(20, 30, 540, 540);
        var data = new MapGridDrawing(bounds, new Size(600, 600), MainWindow.DeepSeaCells, true);
        Assert.AreEqual(27 * 27, data.Labels.Length);
        Assert.AreEqual(4001.4, side, 1e-8);
        Assert.AreEqual(56, PathGeometry.CreateFromGeometry(data.Lines).Figures.Count);
        var view = new MapGridVisual();
        view.SetDrawing(data);
        view.SetViewScale(0.5, false);
        view.Measure(new Size(600, 600));
        view.Arrange(new Rect(0, 0, 600, 600));
        var export = new DrawingVisual();
        using (var context = export.RenderOpen())
            context.DrawRectangle(new VisualBrush(view)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, 600, 600), Stretch = Stretch.Fill
            }, null, new Rect(0, 0, 300, 300));
        var bitmap = new RenderTargetBitmap(300, 300, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(export);
        var pixels = new byte[300 * 300 * 4];
        bitmap.CopyPixels(pixels, 300 * 4, 0);
        Assert.IsTrue(pixels.Where((_, index) => index % 4 == 3).Any(alpha => alpha != 0));
    }

    [STATestMethod]
    public void OceanGrid_CoversTheViewport_AndStaysAlignedDuringPan()
    {
        var data = new MapGridDrawing(new Rect(40, 40, 580, 580), new Size(660, 660), 29, false);
        var brush = MapGridVisual.CreateOceanBrush(data, 0.5, false, 0.7, Color.FromRgb(11, 58, 74));
        var transform = (MatrixTransform)brush.Transform;
        transform.Matrix = new Matrix(0.5, 0, 0, 0.5, 140, 180);
        var content = brush.Drawing;
        foreach (double pan in new[] { 0d, 3d })
        {
            transform.Matrix = new Matrix(0.5, 0, 0, 0.5, 140 + pan, 180);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
                context.DrawRectangle(brush, null, new Rect(0, 0, 600, 900));
            var bitmap = new RenderTargetBitmap(600, 900, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[600 * 900 * 4];
            bitmap.CopyPixels(pixels, 600 * 4, 0);
            Assert.AreEqual((byte)255, pixels[3]);
            Assert.AreEqual((byte)255, pixels[^1]);
            int line = (55 * 600 + 50 + (int)pan) * 4;
            int ocean = (55 * 600 + 55 + (int)pan) * 4;
            Assert.IsTrue(pixels[line] < pixels[ocean], "Grid must continue beyond the image bounds into the outer ocean.");
            Assert.AreSame(content, brush.Drawing, "Panning must reuse the tile drawing.");
        }
    }

    [STATestMethod]
    public void StaticCaching_DoesNotRasterizeTheGridOrMovingMarkers()
    {
        var scene = new Grid { CacheMode = new BitmapCache() };
        var texture = new Image();
        var grid = new MapGridVisual();
        var players = new Canvas();
        scene.Children.Add(texture);
        scene.Children.Add(grid);
        scene.Children.Add(players);
        foreach (bool enabled in new[] { true, false, true })
        {
            MainWindow.ConfigureStaticMapCaching(scene, new UIElement[] { texture }, enabled, 1.5);
            Assert.IsNull(scene.CacheMode);
            Assert.IsNull(grid.CacheMode);
            Assert.IsNull(players.CacheMode);
            if (enabled) Assert.AreEqual(1.5, ((BitmapCache)texture.CacheMode).RenderAtScale);
            else Assert.IsNull(texture.CacheMode);
        }
    }
}
