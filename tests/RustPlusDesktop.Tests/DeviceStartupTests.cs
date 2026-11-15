using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;
using RustPlusDesk.Views;
using RustPlusDesk.Services;
using RustPlusDesk.ViewModels;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RustPlusDesktop.Tests;

[TestClass]
public class DeviceStartupTests
{
    [STATestMethod]
    public async Task EmptyDeviceStartup_SkipsSubscriptionsAndStatusChecks_IncludingEmptyGroups()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        var profile = new ServerProfile { Port = 28082 };
        typeof(MainViewModel).GetField("_selected", flags)!.SetValue(vm, profile);
        typeof(MainWindow).GetField("_vm", flags)!.SetValue(window, vm);
        using var client = new RustPlusClientReal(message => Assert.Fail("Empty device startup accessed the client: " + message));
        typeof(MainWindow).GetField("_rust", flags)!.SetValue(window, client);
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        var initialize = typeof(MainWindow).GetMethod("InitializeDevicesAsync", flags)!;

        await (Task)initialize.Invoke(window, new object[] { profile, false })!;
        profile.Devices.Add(new SmartDevice { IsGroup = true });
        await (Task)initialize.Invoke(window, new object[] { profile, false })!;

        Assert.IsFalse(vm.IsDeviceSubscribePriming);
        Assert.IsFalse(vm.IsDeviceStatusChecking);
        Assert.IsFalse(changes.Contains(nameof(MainViewModel.IsDeviceSubscribePriming)));
        Assert.IsFalse(changes.Contains(nameof(MainViewModel.IsDeviceStatusChecking)));
    }

    [STATestMethod]
    public async Task FullConnect_RevealsCachedMapBeforeWaitingForPreviousConnection_AndAbortsIfSelectionChanges()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        var profile = new ServerProfile { Port = 28082, IsConnected = true };
        typeof(MainViewModel).GetField("_selected", flags)!.SetValue(vm, profile);
        typeof(MainWindow).GetField("_vm", flags)!.SetValue(window, vm);
        var pendingReset = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        typeof(MainWindow).GetField("_connectionRecoveryHooksAdded", flags)!.SetValue(window, true);
        typeof(MainWindow).GetField("_serverSelectionResetTask", flags)!.SetValue(window, pendingReset.Task);
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 255 }, 4);
        typeof(MainWindow).GetField("_mapBaseBmp", flags)!.SetValue(window, bitmap);
        vm.HasMapImage = true;
        vm.IsConnectionLoading = true;

        Task<bool> connect = window.PerformConnectAsync(silent: false);

        Assert.IsFalse(connect.IsCompleted, "Connection work must still be waiting for the previous socket.");
        Assert.IsTrue(vm.IsFullConnectRequested);
        Assert.IsFalse(vm.IsMapPreviewVisible, "Full Connect must reveal the map before any network work completes.");
        Assert.IsFalse(vm.IsConnectionLoading);
        Assert.IsFalse(profile.IsFullConnected, "Revealing a cache must not claim a live connection.");

        typeof(MainViewModel).GetField("_selected", flags)!.SetValue(vm, new ServerProfile { Port = 28083 });
        typeof(MainWindow).GetField("_mapSelectionVersion", flags)!.SetValue(window, 1);
        pendingReset.SetResult();
        Assert.IsFalse(await connect, "A queued upgrade must not connect a newly selected server.");
    }

    [STATestMethod]
    public async Task CachePreview_DoesNotFetchMissingOrKnownStaleMaps()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        var profile = new ServerProfile { Host = "cache-preview-" + Guid.NewGuid().ToString("N"), Port = 28082 };
        typeof(MainViewModel).GetField("_selected", flags)!.SetValue(vm, profile);
        typeof(MainWindow).GetField("_vm", flags)!.SetValue(window, vm);
        using var client = new RustPlusClientReal(message => Assert.Fail("Cache preview accessed the disconnected client: " + message));
        typeof(MainWindow).GetField("_rust", flags)!.SetValue(window, client);

        await (Task)typeof(MainWindow).GetMethod("LoadMapAsync", flags)!.Invoke(window, new object[] { true })!;

        Assert.IsFalse(profile.IsConnected);
        Assert.IsNull(typeof(MainWindow).GetField("_dynTimer", flags)!.GetValue(window));

        var cacheDirectory = (string)typeof(MainWindow).GetField("s_mapCacheDir", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Directory.CreateDirectory(cacheDirectory);
        string image = Path.Combine(cacheDirectory, profile.CacheKey + ".png");
        string metadata = Path.Combine(cacheDirectory, profile.CacheKey + ".json");
        try
        {
            var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 255 }, 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(image)) encoder.Save(file);
            var oldWipe = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
            File.WriteAllText(metadata, JsonSerializer.Serialize(new
            {
                WorldSize = 4000, PixelWidth = 1, PixelHeight = 1, WipeTime = oldWipe, Monuments = Array.Empty<object>()
            }));
            var cache = typeof(MainWindow).GetMethod("TryLoadMapCache", BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.IsNotNull(cache.Invoke(null, new object?[] { profile.CacheKey, null }), "The fixture must be a readable cached map.");
            profile.WipeTime = oldWipe.AddDays(7);

            await (Task)typeof(MainWindow).GetMethod("LoadMapAsync", flags)!.Invoke(window, new object[] { true })!;

            Assert.IsTrue(File.Exists(image), "Preview must leave cache invalidation to full connect.");
            Assert.IsTrue(File.Exists(metadata));
            Assert.IsNull(typeof(MainWindow).GetField("_mapBaseBmp", flags)!.GetValue(window), "A map known to predate the current wipe must never be displayed.");
        }
        finally
        {
            File.Delete(image);
            File.Delete(metadata);
        }
    }

    [TestMethod]
    public void SoftConnectionUpgrade_RechecksStaleMissingAndUnknownDevices()
    {
        var now = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
        var device = new SmartDevice { Kind = "SmartSwitch", LastPolledAt = now.AddMinutes(-9) };
        Assert.IsFalse(MainWindow.NeedsDeviceStatusRefresh(device, now));
        device.LastPolledAt = now.AddMinutes(-10);
        Assert.IsTrue(MainWindow.NeedsDeviceStatusRefresh(device, now), "Ten minutes is stale.");
        device.LastPolledAt = now;
        device.IsMissing = true;
        Assert.IsTrue(MainWindow.NeedsDeviceStatusRefresh(device, now));
        device.IsMissing = false;
        device.Kind = "";
        Assert.IsTrue(MainWindow.NeedsDeviceStatusRefresh(device, now));
        device.Kind = "SmartSwitch";
        device.LastPolledAt = now.AddSeconds(1);
        Assert.IsTrue(MainWindow.NeedsDeviceStatusRefresh(device, now), "A clock change cannot make cached state fresh indefinitely.");
        device.LastPolledAt = DateTime.MinValue;
        Assert.IsTrue(MainWindow.NeedsDeviceStatusRefresh(device, now));
        device.IsGroup = true;
        Assert.IsFalse(MainWindow.NeedsDeviceStatusRefresh(device, now), "Groups are local and cannot be probed.");
    }
}
