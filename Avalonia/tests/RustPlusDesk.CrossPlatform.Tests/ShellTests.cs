using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using Avalonia.Media;
using FluentAvalonia.Styling;
using RustPlusDesk.Avalonia.Features.Accounts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Avalonia;
using RustPlusDesk.Avalonia.Features.Servers;
using RustPlusDesk.Avalonia.Services;
using RustPlusDesk.Avalonia.Shared.Controls;
using RustPlusDesk.Avalonia.Shell;
using RustPlusDesk.Core.Servers;
using RustPlusDesk.Core.Connections;
using RustPlusDesk.Core.Devices;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class ShellTests
{
    [DataTestMethod]
    [DataRow(1000, 700)]
    [DataRow(1380, 1000)]
    [DataRow(2560, 1440)]
    public async Task ReferenceLayout_ResizesCollapsesAndKeepsNavigationAndDevicesAvailable(int width, int height)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(async () =>
        {
            var model = new ServersViewModel(new PreviewCatalog(), new UnusedPicker(), "profiles.json", new PreviewSessionFactory(),
                recyclerCatalog: new RustPlusDesk.Infrastructure.Items.BundledRecyclerCatalog());
            var window = new MainWindow { DataContext = model, Width = width, Height = height, RequestedThemeVariant = ThemeVariant.Dark };
            try
            {
                window.Show();
                await model.RefreshCommand.ExecuteAsync(null);
                using (var frame = window.CaptureRenderedFrame())
                {
                    Assert.IsNotNull(frame);
                    frame.Save(Path.Combine(AppContext.BaseDirectory, $"ui-layout-{width}.png"), PngBitmapEncoderOptions.Default);
                }
                var pane = window.GetVisualDescendants().OfType<ServerDevicePane>().Single();
                var map = window.GetVisualDescendants().OfType<MapWorkspace>().Single();
                var rail = window.GetVisualDescendants().OfType<NavigationRail>().Single();
                var split = window.FindControl<SplitView>("WorkspaceSplit")!;
                Assert.IsTrue(pane.Bounds.Width >= 296 && pane.Bounds.Width <= 480);
                Assert.IsTrue(map.Bounds.Width >= 600);
                Assert.AreEqual(60, rail.Bounds.Width);
                var paneOrigin = pane.TranslatePoint(default, window)!.Value;
                var mapOrigin = map.TranslatePoint(default, window)!.Value;
                Assert.IsTrue(mapOrigin.X >= paneOrigin.X + pane.Bounds.Width);
                var activity = map.FindControl<Border>("ActivityPanel")!;
                Assert.IsTrue(activity.Bounds.Height >= 90);
                Assert.IsTrue(activity.TranslatePoint(default, window)!.Value.Y > mapOrigin.Y + 180);

                var handle = window.GetVisualDescendants().OfType<global::Avalonia.Controls.Primitives.Thumb>()
                    .Single(control => global::Avalonia.Automation.AutomationProperties.GetName(control) == "Resize server and device panel");
                double before = split.OpenPaneLength;
                handle.Focus();
                window.KeyPress(global::Avalonia.Input.Key.Right, global::Avalonia.Input.RawInputModifiers.None,
                    global::Avalonia.Input.PhysicalKey.ArrowRight, null);
                window.KeyRelease(global::Avalonia.Input.Key.Right, global::Avalonia.Input.RawInputModifiers.None,
                    global::Avalonia.Input.PhysicalKey.ArrowRight, null);
                Assert.AreEqual(Math.Min(480, before + 16), split.OpenPaneLength, .01);

                double originalMapWidth = map.Bounds.Width;
                model.ToggleSidebarCommand.Execute(null);
                using (var collapsed = window.CaptureRenderedFrame()) Assert.IsNotNull(collapsed);
                Assert.IsFalse(split.IsPaneOpen);
                Assert.IsTrue(map.Bounds.Width > originalMapWidth);
                model.ShowDevicesCommand.Execute(null);
                using (var reopened = window.CaptureRenderedFrame()) Assert.IsNotNull(reopened);
                Assert.IsTrue(split.IsPaneOpen);
                Assert.AreSame(pane, window.GetVisualDescendants().OfType<ServerDevicePane>().Single());
                Assert.AreEqual(1, model.Connection.Devices.Devices.Count);

                model.OpenRecyclerCommand.Execute(null);
                using (var recycler = window.CaptureRenderedFrame()) Assert.IsNotNull(recycler);
                Assert.IsTrue(model.IsRecyclerSelected);
                Assert.IsTrue(pane.IsEffectivelyVisible);
                model.ShowDevicesCommand.Execute(null);
                Assert.IsFalse(model.IsRecyclerSelected);
                Assert.IsTrue(model.IsSidebarOpen);
                window.Width = 1000;
                window.Height = 700;
                using (var resized = window.CaptureRenderedFrame()) Assert.IsNotNull(resized);
                Assert.IsTrue(split.OpenPaneLength >= 300 && split.OpenPaneLength <= 480);
                Assert.IsTrue(map.Bounds.Width >= 460);
                model.SelectedServer = new(new string('W', 160), "long.example.test", 28082)
                { Devices = [new(1, new string('W', 160), DeviceKind.Switch)] };
                window.RequestedThemeVariant = ThemeVariant.Light;
                using (var longNames = window.CaptureRenderedFrame()) Assert.IsNotNull(longNames);
                var deviceView = pane.GetVisualDescendants().OfType<RustPlusDesk.Avalonia.Features.Devices.DevicesView>().Single();
                foreach (var button in deviceView.GetVisualDescendants().OfType<Button>().Where(control => control.Command is not null && control.IsEffectivelyVisible))
                {
                    var point = button.TranslatePoint(default, deviceView)!.Value;
                    Assert.IsTrue(point.X >= 0 && point.X + button.Bounds.Width <= deviceView.Bounds.Width);
                }
            }
            finally { await model.ShutdownAsync(); window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task Account_UsesWorkingCommandsAndModalFocusScope_WithoutNativeLoginDuringTest()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(async () =>
        {
            var service = new CompanionAccountViewModelTests.Service();
            var source = new CompanionListenerViewModelTests.Source();
            var model = new ServersViewModel(new PreviewCatalog(), new UnusedPicker(), "profiles.json", new PreviewSessionFactory(), accountService: service, companionListener: source, pushHealth: new PreviewHealth());
            var window = new MainWindow { DataContext = model, Width = 1000, Height = 700 };
            try
            {
                window.Show();
                model.OpenAccountCommand.Execute(null);
                Assert.IsTrue(model.HasOverlay);
                Assert.IsFalse(model.RefreshCommand.CanExecute(null));
                using var opened = window.CaptureRenderedFrame();
                Assert.IsNotNull(opened);
                var view = window.GetVisualDescendants().OfType<CompanionAccountView>().Single();
                Assert.IsTrue(view.IsEffectivelyVisible);
                Assert.IsNotNull(model.Account);
                await model.Account.CheckCommand.ExecuteAsync(null);
                Task listening = model.Listener!.StartCommand.ExecuteAsync(null);
                await source.Started.Task;
                source.Send(new(RustPlusDesk.Core.Accounts.CompanionUpdateKind.Listening, "Rust+", "Listening"));
                await CompanionListenerViewModelTests.Eventually(() => model.Listener.IsChecking);
                await model.Listener.CheckDeliveryCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(12));
                source.Send(new(RustPlusDesk.Core.Accounts.CompanionUpdateKind.Alarm, "Fixture base alarm", "Motion detected") { ServerName = "EU Vanilla" });
                await CompanionListenerViewModelTests.Eventually(() => model.Listener.Notifications.Count == 1);
                using var frame = window.CaptureRenderedFrame();
                Assert.IsNotNull(frame);
                frame.Save(Path.Combine(AppContext.BaseDirectory, "ui-account.png"), PngBitmapEncoderOptions.Default);
                Assert.IsTrue(window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Signed in to Rust+."));
                foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible))
                {
                    var origin = button.TranslatePoint(default, window)!.Value;
                    Assert.IsTrue(origin.X >= 0 && origin.X + button.Bounds.Width <= window.ClientSize.Width);
                    Assert.IsTrue(origin.Y >= 0 && origin.Y + button.Bounds.Height <= window.ClientSize.Height);
                }
                model.Account.RequestSignOutCommand.Execute(RustPlusDesk.Core.Accounts.CompanionSignOutScope.AllDevices);
                using (var confirmation = window.CaptureRenderedFrame())
                {
                    Assert.IsNotNull(confirmation);
                    confirmation.Save(Path.Combine(AppContext.BaseDirectory, "ui-account-signout.png"), PngBitmapEncoderOptions.Default);
                }
                Assert.IsTrue(model.Account.IsConfirmingSignOut);
                Assert.AreEqual(0, service.SignOuts);
                model.Account.KeepSignedInCommand.Execute(null);
                await model.CloseAccountCommand.ExecuteAsync(null);
                Assert.IsFalse(model.HasOverlay);
                Assert.IsFalse(view.IsEffectivelyVisible);
                Assert.IsTrue(model.RefreshCommand.CanExecute(null));
                Assert.IsTrue(model.Listener.IsRunning);
                await model.Listener.StopListeningAsync();
                await listening;
            }
            finally { await model.ShutdownAsync(); window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task Shell_RendersBoundServerListAndSharedStatusAtMinimumSize()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(async () =>
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.IsNotNull(Application.Current!.Styles.OfType<FluentAvaloniaTheme>().Single());
                Assert.IsFalse(Application.Current.Styles.OfType<FluentTheme>().Any());
            }
            else Assert.AreEqual(DensityStyle.Compact, Application.Current!.Styles.OfType<FluentTheme>().Single().DensityStyle);
            Assert.AreEqual(ThemeVariant.Default, Application.Current.RequestedThemeVariant);
            var connectionFactory = new PreviewSessionFactory();
            var model = new ServersViewModel(new PreviewCatalog(), new UnusedPicker(), "profiles.json", connectionFactory);
            var window = new MainWindow { DataContext = model, RequestedThemeVariant = ThemeVariant.Dark };
            try
            {
                window.Show();
                Assert.AreEqual(WindowDecorations.Full, window.WindowDecorations);
                Assert.IsFalse(window.IsExtendedIntoWindowDecorations);
                using (var frame = window.CaptureRenderedFrame())
                {
                    Assert.IsNotNull(frame);
                    frame.Save(Path.Combine(AppContext.BaseDirectory, "ui-layout-empty.png"), PngBitmapEncoderOptions.Default);
                }

                var status = window.GetVisualDescendants().OfType<StatusPanel>().Single();
                Assert.AreEqual("No saved servers yet", status.Title);
                Assert.IsTrue(status.IsEffectivelyVisible);

                await model.RefreshCommand.ExecuteAsync(null);
                using (var frame = window.CaptureRenderedFrame())
                {
                    Assert.IsNotNull(frame);
                    frame.Save(Path.Combine(AppContext.BaseDirectory, "ui-preview.png"), PngBitmapEncoderOptions.Default);
                }
                Assert.IsFalse(status.IsEffectivelyVisible);
                var list = window.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "SavedServerSelector");
                Assert.AreEqual(2, list.ItemCount);
                Assert.IsTrue(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "EU Vanilla"));

                var connected = model.Connection.ConnectCommand.ExecuteAsync(null);
                using (var frame = window.CaptureRenderedFrame())
                {
                    Assert.IsNotNull(frame);
                    frame.Save(Path.Combine(AppContext.BaseDirectory, "ui-connected.png"), PngBitmapEncoderOptions.Default);
                }
                Assert.IsTrue(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "42 / 200"));
                Assert.IsTrue(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "14:30"));
                Assert.IsTrue(model.Connection.IsConnected);
                Assert.AreEqual("OFF", model.Connection.Devices.Devices[0].Children[0].StateLabel);
                Assert.IsTrue(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Wood"));
                await model.Connection.Devices.Devices[0].TurnOnCommand.ExecuteAsync(null);
                Assert.AreEqual(true, model.Connection.Devices.Devices[0].IsOn);

                window.Width = window.MinWidth;
                window.Height = window.MinHeight;
                using var smallFrame = window.CaptureRenderedFrame();
                Assert.IsNotNull(smallFrame);
                smallFrame.Save(Path.Combine(AppContext.BaseDirectory, "ui-preview-small.png"), PngBitmapEncoderOptions.Default);
                foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(b => b.Command is not null && b.IsEffectivelyVisible))
                {
                    var origin = button.TranslatePoint(default, window);
                    Assert.IsNotNull(origin);
                    Assert.IsTrue(origin.Value.X >= 0 && origin.Value.X + button.Bounds.Width <= window.ClientSize.Width);
                }
                model.SelectedServer = model.Servers[1];
                await connected;
                Assert.IsTrue(connectionFactory.Disposed);
                Assert.AreEqual("Team practice", model.Connection.ServerName);
                Assert.IsFalse(model.Connection.IsConnected);
                var dark = ((ISolidColorBrush)window.Background!).Color;
                window.RequestedThemeVariant = ThemeVariant.Light;
                using var lightFrame = window.CaptureRenderedFrame();
                Assert.IsNotNull(lightFrame);
                lightFrame.Save(Path.Combine(AppContext.BaseDirectory, "ui-light.png"), PngBitmapEncoderOptions.Default);
                Assert.AreNotEqual(dark, ((ISolidColorBrush)window.Background!).Color);
            }
            finally { await model.ShutdownAsync(); window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task PlatformStyles_RenderBothVariantsAndEditorAtMinimumWindowSize()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(async () =>
        {
            var services = new ServerEditorViewModelTests.EditorServices();
            var model = new ServersViewModel(new PreviewCatalog(), new UnusedPicker(), "profiles.json", new PreviewSessionFactory(), services, services);
            var window = new MainWindow { DataContext = model, Width = 1000, Height = 700 };
            try
            {
                window.Show();
                await model.RefreshCommand.ExecuteAsync(null);
                var editing = model.EditCommand.ExecuteAsync(null);
                Assert.IsNotNull(model.Editor);
                var editor = window.GetVisualDescendants().OfType<ServerEditorView>().Single();
                Assert.IsTrue(editor.IsEffectivelyVisible);
                Assert.IsFalse(window.GetVisualDescendants().OfType<ServersView>().Single().IsEffectivelyEnabled);
                foreach (string platform in new[] { "Windows", "Linux", "MacOS" })
                {
                    var style = (Styles)global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(new Uri($"avares://RustPlusDesk.Avalonia/Styles/Platforms/{platform}.axaml"));
                    var resources = style.Resources;
                    style.Resources = new ResourceDictionary();
                    Application.Current!.Resources.MergedDictionaries.Add(resources);
                    Application.Current.Styles.Add(style);
                    try
                    {
                        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                        {
                            window.RequestedThemeVariant = variant;
                            using var frame = window.CaptureRenderedFrame();
                            Assert.IsNotNull(frame);
                            Assert.AreEqual(Color.Parse(variant == ThemeVariant.Dark ? "#F0F3F7" : "#1A1A1A"),
                                ((ISolidColorBrush)window.Foreground!).Color, $"Window text must follow {platform}/{variant}.");
                            Assert.AreEqual(((ISolidColorBrush)window.Foreground!).Color,
                                ((ISolidColorBrush)window.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "RUST+ DESKTOP").Foreground!).Color);
                            frame.Save(Path.Combine(AppContext.BaseDirectory, $"ui-editor-{platform}-{variant}.png"), PngBitmapEncoderOptions.Default);
                            foreach (var button in editor.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible))
                            {
                                var origin = button.TranslatePoint(default, window)!.Value;
                                Assert.IsTrue(origin.X >= 0 && origin.X + button.Bounds.Width <= window.ClientSize.Width);
                                Assert.IsTrue(origin.Y >= 0 && origin.Y + button.Bounds.Height <= window.ClientSize.Height);
                            }
                        }
                    }
                    finally
                    {
                        Application.Current.Styles.Remove(style);
                        Application.Current.Resources.MergedDictionaries.Remove(resources);
                    }
                }
                await model.Editor.CancelCommand.ExecuteAsync(null);
                await editing;
                Assert.IsNull(model.Editor);
                Assert.IsTrue(window.GetVisualDescendants().OfType<ServersView>().Single().IsEffectivelyEnabled);
            }
            finally { await model.ShutdownAsync(); window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    private sealed class PreviewCatalog : IServerCatalog
    {
        public Task<IReadOnlyList<ServerSummary>> LoadAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ServerSummary>>(
                [new("EU Vanilla", "eu.example.test", 28082) { Devices =
                    [new(0, "Main base", DeviceKind.Group) { Children =
                        [new(1, "Compound lights", DeviceKind.Switch), new(2, "Base alarm", DeviceKind.Alarm),
                         new(3, "Tool cupboard", DeviceKind.StorageMonitor)] }] },
                 new("Team practice", "team.example.test", 28083)]);
    }

    private sealed class UnusedPicker : IProfileFilePicker
    {
        public Task<string?> PickAsync() => throw new AssertFailedException("The file picker should not run in this test.");
    }

    private sealed class PreviewHealth : RustPlusDesk.Core.Accounts.ICompanionPushHealth
    {
        public Task<RustPlusDesk.Core.Accounts.PushHealthReport> CheckAsync(string path, CancellationToken ct)
            => Task.FromResult(new RustPlusDesk.Core.Accounts.PushHealthReport(RustPlusDesk.Core.Accounts.PushHealthOutcome.Healthy, "Push delivery verified by fixture."));
    }

    private sealed class PreviewSessionFactory : IServerSessionFactory, IServerSession
    {
        public IDeviceClient Devices { get; } = new TestDeviceClient
        {
            Read = (device, _) => Task.FromResult(
            device.Kind == DeviceKind.StorageMonitor
                ? new DeviceSnapshot(device.EntityId, Storage: new(24, true, DateTimeOffset.UtcNow.AddDays(3),
                    [new(-151838493, 1200, false) { Name = "Wood" }, new(69511070, 500, false) { Name = "Metal Fragments" }]))
                : new DeviceSnapshot(device.EntityId, false))
        };
        public bool Disposed { get; private set; }
        public ServerStatus InitialStatus { get; } = new("EU Vanilla", 42, 200, 3, 14.5);
        public Task Disconnected { get; } = new TaskCompletionSource().Task;
        public Task<IServerSession> ConnectAsync(string profilesPath, string profileKey, CancellationToken cancellationToken)
            => Task.FromResult<IServerSession>(this);
        public Task<ServerStatus> ReadStatusAsync(CancellationToken cancellationToken) => Task.FromResult(InitialStatus);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
