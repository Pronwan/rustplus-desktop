using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesktop.Tests;

/// <summary>
/// A refresh reads a switch's on/off state only on the generic path. A snapshot that got
/// attached to a switch must not send it down the storage path, or a wrong state is never
/// corrected by a refresh or a restart.
/// </summary>
[TestClass]
public class StorageDeviceTests
{
    [DataTestMethod]
    [DataRow("SmartSwitch")]
    [DataRow("SmartAlarm")]
    public void SwitchesAndAlarmsWithASnapshotAreNotStorage(string kind)
    {
        var device = new SmartDevice { Kind = kind, Storage = new StorageSnapshot() };
        Assert.IsFalse(RustPlusClientReal.IsStorageDevice(device));
    }

    [TestMethod]
    public void StorageMonitorsAndUnknownDevicesWithASnapshotAreStorage()
    {
        Assert.IsTrue(RustPlusClientReal.IsStorageDevice(new SmartDevice { Kind = "StorageMonitor" }));
        Assert.IsTrue(RustPlusClientReal.IsStorageDevice(new SmartDevice { Storage = new StorageSnapshot() }));
    }
}
