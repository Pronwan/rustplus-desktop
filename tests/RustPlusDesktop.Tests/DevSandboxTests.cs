using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Cloud;
using RustPlusDesk.Services.Data;
using RustPlusDesk.Services.Social;
using RustPlusDesk.Views;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class DevSandboxTests
{
    [TestMethod]
    public void CreateFakeServerProfile_BuildsMarkedSandboxServer()
    {
        var server = MainWindow.CreateFakeServerProfile(null, 2, "76561198000000099");

        Assert.IsTrue(server.IsFakeSandbox);
        Assert.AreEqual("[DEV] Sandbox Server #3", server.Name);
        Assert.AreEqual(ServerProfile.FakeSandboxHost, server.Host);
        Assert.AreEqual(28084, server.Port);
        Assert.AreEqual("76561198000000099", server.SteamId64);
        Assert.IsTrue(Guid.TryParse(server.ServerId, out _));
        Assert.IsTrue(server.ChatCommandsEnabled);
        Assert.IsTrue(ServerProfile.IsFakeSandboxServerKey(server.ServerKey));
        Assert.IsTrue(ServerProfile.IsFakeSandboxServerKey(server.PlatformServerKey));
    }

    [TestMethod]
    public async Task FakeSandboxServerAndDevices_AreBlockedFromCloudUploads()
    {
        var fakeServer = MainWindow.CreateFakeServerProfile("Fake Cloud Test", 0, "76561198000000099");
        var fakeSwitch = MainWindow.CreateFakeSmartDevice("SmartSwitch", 900_000_001, null, 1);

        Assert.AreEqual(0, await DeviceDataModule.UploadDevicesSnapshotAsync(fakeServer.ServerKey, 76561198000000099UL, new[] { fakeSwitch }, null, explicitWipe: true));
        Assert.AreEqual(0, await DeviceDataModule.UploadDevicesSnapshotAsync("real.server.com-28082", 76561198000000099UL, new[] { fakeSwitch }, null, explicitWipe: false));
        Assert.IsFalse(await OverlayDataModule.UploadOverlayAsync(fakeServer.ServerKey, 76561198000000099UL, new OverlaySaveData(), explicitWipe: true));
        Assert.IsFalse(await CloudServerModifiersSync.PushAsync(fakeServer));
        Assert.IsNull(await CloudAlexaAdapter.PairServerAsync("76561198000000099", fakeServer.Host, fakeServer.Port, fakeServer.Name, fakeServer.PlayerToken, fakeServer.PlatformServerKey));

        var realGroup = new SmartDevice { EntityId = 1001, Name = "Group", IsGroup = true };
        realGroup.Children.Add(new SmartDevice { EntityId = 1002, Name = "Real", IsFakeSandbox = false });
        realGroup.Children.Add(fakeSwitch);
        var mapped = DeviceDataModule.MapDeviceToDto(realGroup, excludeFakeSandbox: true);
        Assert.IsNotNull(mapped.Children);
        Assert.AreEqual(1, mapped.Children!.Count);
        Assert.AreEqual(1002u, mapped.Children[0].EntityId);
    }

    [TestMethod]
    public void CreateFakeSmartDevice_SupportsSwitchAlarmAndStorageMonitor()
    {
        var sw = MainWindow.CreateFakeSmartDevice("SmartSwitch", 900_000_001, null, 1);
        Assert.IsTrue(sw.IsFakeSandbox);
        Assert.AreEqual("SmartSwitch", sw.Kind);
        Assert.AreEqual(false, sw.IsOn);
        Assert.IsFalse(sw.IsMissing);

        var alarm = MainWindow.CreateFakeSmartDevice("SmartAlarm", 900_000_002, "Raid Siren", 1);
        Assert.IsTrue(alarm.IsFakeSandbox);
        Assert.AreEqual("SmartAlarm", alarm.Kind);
        Assert.AreEqual("Raid Siren", alarm.Name);
        Assert.AreEqual("Raid Siren", alarm.InGameAlarmTitle);

        var storage = MainWindow.CreateFakeSmartDevice("StorageMonitor", 900_000_003, null, 1);
        Assert.IsTrue(storage.IsFakeSandbox);
        Assert.AreEqual("StorageMonitor", storage.Kind);
        Assert.IsNull(storage.IsOn);
        Assert.IsNotNull(storage.Storage);
        Assert.IsTrue(storage.Storage!.IsToolCupboard);
        Assert.AreEqual(4, storage.Storage.Items.Count);
    }

    [TestMethod]
    public void GlobalChatFeed_InjectDevMessage_And_ClearDevMessages_WorkWithoutAuth()
    {
        GlobalChatFeed.ClearDevMessages();
        try
        {
            var line = new ChatLine
            {
                Id = "dev-test-" + Guid.NewGuid().ToString("N"),
                Body = "Test global message",
                SenderName = "DevTester",
                Room = GlobalChatFeed.DefaultRoom,
                SentAt = DateTime.UtcNow,
            };

            GlobalChatFeed.InjectDevMessage(line);

            var lines = GlobalChatFeed.Lines(GlobalChatFeed.DefaultRoom);
            Assert.IsTrue(lines.Any(l => l.Id == line.Id && l.Body == "Test global message"));

            GlobalChatFeed.ClearDevMessages();
            Assert.IsFalse(GlobalChatFeed.Lines(GlobalChatFeed.DefaultRoom).Any(l => l.Id == line.Id));
        }
        finally
        {
            GlobalChatFeed.ClearDevMessages();
        }
    }
}
