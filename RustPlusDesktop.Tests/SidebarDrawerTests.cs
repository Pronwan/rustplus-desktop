using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Views;

namespace RustPlusDesktop.Tests;

[TestClass]
public class SidebarDrawerTests
{
    [STATestMethod]
    public void Drawer_OpeningDoesNotResizeMap_AndWideLayoutRestoresDocking()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        root.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 160 });
        var map = new Border { MinWidth = 160 };
        var sidebar = new Border();
        var splitter = new GridSplitter();
        Grid.SetColumn(map, 2);
        root.Children.Add(map);
        root.Children.Add(sidebar);
        root.Children.Add(splitter);

        foreach (double rail in new[] { 64d, 200d })
        {
            double expanded = 420 + rail - 64;
            Assert.IsTrue(MainWindow.ShouldUseSidebarDrawer(800, expanded, false));
            Assert.IsFalse(MainWindow.ShouldUseSidebarDrawer(1600, expanded, false));
            Assert.IsFalse(MainWindow.ShouldUseSidebarDrawer(800, expanded, true));
            foreach (bool pinned in new[] { false, true, false })
            foreach (double viewport in new[] { 800d, 1000d, 1600d, 1000d })
            {
                bool drawer = MainWindow.ShouldUseSidebarDrawer(viewport, expanded, pinned);
                var bounds = new List<Rect>();
                foreach (double width in new[] { rail, (rail + expanded) / 2, expanded, rail })
                {
                    MainWindow.ApplySidebarLayout(root, sidebar, splitter, rail, width, drawer);
                    root.Measure(new Size(viewport, 700));
                    root.Arrange(new Rect(0, 0, viewport, 700));
                    bounds.Add(new Rect(map.TranslatePoint(new Point(), root), map.RenderSize));
                    Assert.IsTrue(bounds[^1].Right <= viewport, "The resized map must fit inside the window.");
                    Assert.AreEqual(width, sidebar.ActualWidth);
                    Assert.AreEqual(drawer ? Visibility.Collapsed : Visibility.Visible, splitter.Visibility);
                    Assert.AreEqual(drawer ? 3 : 1, Grid.GetColumnSpan(sidebar));
                    Assert.AreEqual(drawer ? 10 : 0, Panel.GetZIndex(sidebar));
                }
                if (drawer)
                    Assert.IsTrue(bounds.All(bound => bound == bounds[0]), "Drawer animation must keep map bounds unchanged.");
                else
                    Assert.IsTrue(bounds[2].Width < bounds[0].Width, "Wide layouts should dock the expanded sidebar.");
            }
        }
    }
}
