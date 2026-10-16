using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Avalonia.Features.Recycling;
using RustPlusDesk.Avalonia.Features.Servers;
using RustPlusDesk.Avalonia.Services;
using RustPlusDesk.Avalonia.Shell;
using RustPlusDesk.Infrastructure.Items;
using RustPlusDesk.Infrastructure.Profiles;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class RecyclerViewTests
{
    [TestMethod]
    public async Task Recycler_RendersAtMinimumSize_AndNumericPointerAndKeyboardInputsWorkWithoutNetwork()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(async () =>
        {
            var model = new ServersViewModel(new LegacyServerCatalog(), new Picker(), "unused.json",
                new UnusedSessionFactory(), recyclerCatalog: new BundledRecyclerCatalog());
            var window = new MainWindow { DataContext = model, Width = 1000, Height = 700 };
            try
            {
                window.Show();
                model.WorkspaceIndex = 1;
                using (var opened = window.CaptureRenderedFrame()) Assert.IsNotNull(opened);
                Assert.IsTrue(model.IsRecyclerSelected);
                Assert.IsFalse(model.HasOverlay);
                Assert.IsTrue(model.RefreshCommand.CanExecute(null));
                var view = window.GetVisualDescendants().OfType<RecyclerView>().Single();
                var search = view.GetVisualDescendants().OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "Search recyclable items");
                search.Text = "hat.wolf";
                using (var filtered = window.CaptureRenderedFrame()) Assert.IsNotNull(filtered);
                var recycler = model.Recycler!;
                Assert.AreEqual(1, recycler.Items.Count);
                var numeric = view.GetVisualDescendants().OfType<NumericUpDown>().Single();
                numeric.Value = 2;
                Assert.AreEqual(2, recycler.Items[0].Quantity);
                Assert.AreEqual("Guar: 12", recycler.Outputs.Single(output => output.Output.Resource.ShortName == "cloth").WildGuaranteed);
                var tile = numeric.GetVisualAncestors().OfType<Border>().First(border => border.DataContext is RecyclerItemViewModel);
                var point = tile.TranslatePoint(new Point(tile.Bounds.Width / 2, 10), window)!.Value;
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift);
                window.MouseUp(point, MouseButton.Left, RawInputModifiers.Shift);
                Assert.AreEqual(12, recycler.Items[0].Quantity);
                window.MouseWheel(point, new Vector(0, -1), RawInputModifiers.Shift);
                Assert.AreEqual(2, recycler.Items[0].Quantity);
                window.MouseDown(point, MouseButton.Right);
                window.MouseUp(point, MouseButton.Right);
                Assert.AreEqual(1, recycler.Items[0].Quantity);
                using (var frame = window.CaptureRenderedFrame())
                {
                    Assert.IsNotNull(frame);
                    frame.Save(Path.Combine(AppContext.BaseDirectory, "ui-recycler.png"), PngBitmapEncoderOptions.Default);
                }
                foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible))
                {
                    var origin = button.TranslatePoint(default, window)!.Value;
                    Assert.IsTrue(origin.X >= 0 && origin.X + button.Bounds.Width <= window.ClientSize.Width);
                    Assert.IsTrue(origin.Y >= 0 && origin.Y + button.Bounds.Height <= window.ClientSize.Height);
                }
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Assert.IsFalse(model.IsRecyclerSelected);
                Assert.IsFalse(model.HasOverlay);
                Assert.IsTrue(model.RefreshCommand.CanExecute(null));
                model.WorkspaceIndex = 1;
                Assert.AreSame(recycler, model.Recycler);
                Assert.IsNotNull(model.Recycler);
                Assert.AreEqual(1, model.Recycler.Items[0].Quantity);
                Assert.AreEqual("hat.wolf", model.Recycler.Search);
            }
            finally { await model.ShutdownAsync(); window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    private sealed class Picker : IProfileFilePicker { public Task<string?> PickAsync() => Task.FromResult<string?>(null); }
}
