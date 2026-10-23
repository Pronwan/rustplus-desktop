using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Avalonia.Features.Devices;
using RustPlusDesk.Avalonia.Features.Servers;
using RustPlusDesk.Avalonia.Services;
using RustPlusDesk.Avalonia.Shell;
using RustPlusDesk.Infrastructure.Profiles;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class DeviceOrganizerViewTests
{
    [TestMethod]
    public async Task Organizer_UsesTypedBindingsAndWorkingButtons_AtMinimumWindowSize()
    {
        using var file = new DeviceProfileEditorTests.Fixture();
        var source = await file.JsonAsync();
        source[0]!["Devices"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject
        { ["EntityId"] = 99, ["Kind"] = "SmartAlarm", ["Name"] = "Alarm" });
        await File.WriteAllTextAsync(file.Path, source.ToJsonString());
        await using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(async () =>
        {
            var model = new ServersViewModel(new LegacyServerCatalog(), new Picker(), file.Path,
                new UnusedSessionFactory(), deviceEditor: new LegacyDeviceProfileEditor());
            var window = new MainWindow { DataContext = model, Width = 1000, Height = 700 };
            try
            {
                await model.RefreshCommand.ExecuteAsync(null);
                window.Show();
                using (var before = window.CaptureRenderedFrame()) Assert.IsNotNull(before);
                var open = window.GetVisualDescendants().OfType<Button>().Single(button => button.Command == model.OrganizeDevicesCommand);
                Assert.IsTrue(open.IsEffectivelyVisible);
                Assert.IsTrue(open.IsEffectivelyEnabled);
                open.Command!.Execute(null);
                Task organizing = model.OrganizeDevicesCommand.ExecutionTask!;
                using (var opened = window.CaptureRenderedFrame()) Assert.IsNotNull(opened);
                var view = window.GetVisualDescendants().OfType<DeviceOrganizerView>().Single();
                Assert.IsTrue(view.IsEffectivelyVisible);
                var editor = model.Organizer!;
                var tree = view.GetVisualDescendants().OfType<TreeView>().Single();
                tree.SelectedItem = editor.Devices[0].Children[0];
                Assert.AreEqual("0/0", editor.Selected!.DeviceKey);
                var alias = view.GetVisualDescendants().OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "Device display name");
                Assert.AreEqual("Alias", alias.Text);
                alias.Text = "Workshop";
                var rename = view.GetVisualDescendants().OfType<Button>().Single(button => button.Command == editor.RenameCommand);
                rename.Command!.Execute(null);
                await ((IAsyncRelayCommand)rename.Command).ExecutionTask!;
                Assert.AreEqual("Workshop", editor.Selected!.Name);
                using (var frame = window.CaptureRenderedFrame())
                {
                    Assert.IsNotNull(frame);
                    frame.Save(Path.Combine(AppContext.BaseDirectory, "ui-device-organizer.png"), PngBitmapEncoderOptions.Default);
                }
                foreach (var button in view.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible))
                {
                    var point = button.TranslatePoint(default, window)!.Value;
                    Assert.IsTrue(point.X >= 0 && point.X + button.Bounds.Width <= window.ClientSize.Width);
                    Assert.IsTrue(point.Y >= 0 && point.Y + button.Bounds.Height <= window.ClientSize.Height);
                }
                var moveDown = view.GetVisualDescendants().OfType<Button>().Single(button => button.Command == editor.MoveDownCommand);
                var scroll = moveDown.GetVisualAncestors().OfType<ScrollViewer>().First();
                Assert.IsTrue(moveDown.TranslatePoint(default, scroll)!.Value.Y + moveDown.Bounds.Height <= scroll.Bounds.Height);
                tree.SelectedItem = editor.Devices[2];
                using (var alarm = window.CaptureRenderedFrame()) Assert.IsNotNull(alarm);
                var title = view.GetVisualDescendants().OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "In-game alarm title");
                Assert.IsTrue(title.IsEffectivelyVisible);
                title.Text = "Fixture raid";
                await editor.SaveAlarmTitleCommand.ExecuteAsync(null);
                var close = view.GetVisualDescendants().OfType<Button>().Single(button => button.Command == editor.CloseCommand);
                close.Command!.Execute(null);
                await organizing.WaitAsync(TimeSpan.FromSeconds(4));
                Assert.IsFalse(model.HasOverlay);
                Assert.AreEqual("Workshop", model.SelectedServer!.Devices[0].Children[0].Name);
                Assert.AreEqual("Fixture raid", model.SelectedServer.Devices[2].InGameAlarmTitle);
            }
            finally { await model.ShutdownAsync(); window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    private sealed class Picker : IProfileFilePicker
    {
        public Task<string?> PickAsync() => Task.FromResult<string?>(null);
    }
}
