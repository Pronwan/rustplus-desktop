using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;

namespace RustPlusDesktop.Tests;

/// <summary>
/// Devices restored from Facepunch's pairing history arrive with a numeric entity type.
/// They must end up with the same Kind as a live-paired device, or the device list shows
/// them without a toggle or a switch command.
/// </summary>
[TestClass]
public class SmartDeviceKindTests
{
    [DataTestMethod]
    [DataRow("1", "SmartSwitch")]
    [DataRow("2", "SmartAlarm")]
    [DataRow("3", "StorageMonitor")]
    [DataRow(" 1 ", "SmartSwitch")]
    public void NumericFacepunchTypesMapToKindNames(string raw, string expected)
    {
        Assert.AreEqual(expected, SmartDevice.NormalizeKind(raw));
        Assert.AreEqual(expected, new SmartDevice { Kind = raw }.Kind);
    }

    [DataTestMethod]
    [DataRow("SmartSwitch")]
    [DataRow("Smart Switch")]
    [DataRow("StorageMonitor")]
    [DataRow("4")]
    [DataRow(null)]
    public void OtherKindsAreLeftAlone(string? raw)
    {
        Assert.AreEqual(raw, SmartDevice.NormalizeKind(raw));
    }

    [TestMethod]
    public void SavedDeviceWithNumericKindIsRepairedOnLoad()
    {
        var device = JsonSerializer.Deserialize<SmartDevice>("{\"EntityId\":2162864,\"Kind\":\"1\"}");

        Assert.IsNotNull(device);
        Assert.AreEqual("SmartSwitch", device.Kind);
    }
}
