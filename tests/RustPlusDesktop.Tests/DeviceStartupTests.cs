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
