using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class DeviceGroupToggleTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RunAsync_MixedSwitches_TurnsOffMemberOnRegardlessOfOrder(bool onFirst)
    {
        var on = Switch(1, true);
        var off = Switch(2, false);
        var group = onFirst ? Group(on, off) : Group(off, on);
        var sent = new List<(uint, bool)>();

        await new DeviceGroupToggle().RunAsync(group, _ => false, Record(sent), () => true);

        CollectionAssert.AreEqual(new[] { (2u, true) }, sent);
    }

    [TestMethod]
    public async Task RunAsync_AllEligibleSwitchesOn_TurnsThemOffAndSkipsMissingOrBusy()
    {
        var missing = Switch(1, false);
        missing.IsMissing = true;
        var busy = Switch(2, false);
        busy.IsToggleBusy = true;
        var locked = Switch(3, false);
        var on = Switch(4, true);
        var nested = Group(on, new SmartDevice { EntityId = 5, Kind = "SmartAlarm" });
        var sent = new List<(uint, bool)>();

        var result = await new DeviceGroupToggle().RunAsync(
            Group(missing, busy, locked, nested), d => d == locked, Record(sent), () => true);

        CollectionAssert.AreEqual(new[] { (4u, false) }, sent);
        Assert.AreEqual(1, result!.Changed);
        Assert.AreEqual(3, result.Skipped);
    }

    [TestMethod]
    public async Task RunAsync_UnknownState_TurnsOnAndDoesNotSendToAlreadyOnSwitch()
    {
        var sent = new List<(uint, bool)>();

        var result = await new DeviceGroupToggle().RunAsync(
            Group(Switch(1, true), Switch(2, null)), _ => false, Record(sent), () => true);

        CollectionAssert.AreEqual(new[] { (2u, true) }, sent);
        Assert.AreEqual(1, result!.Unchanged);
    }

    [TestMethod]
    public async Task RunAsync_TimeoutAndLateBusy_ReportsPartialResultsAndContinues()
    {
        var lateBusy = Switch(2, false);
        var group = Group(Switch(1, false), lateBusy, Switch(3, false), Switch(4, false));
        var sent = new List<uint>();
        var errors = new List<Exception>();

        Task<bool> Toggle(SmartDevice device, bool target)
        {
            sent.Add(device.EntityId);
            if (device.EntityId == 1)
            {
                lateBusy.IsToggleBusy = true;
                return Task.FromException<bool>(new OperationCanceledException("Per-device timeout"));
            }
            return Task.FromResult(device.EntityId != 3); // Acquisition lost to another toggle.
        }

        var result = await new DeviceGroupToggle().RunAsync(group, _ => false, Toggle, () => true,
            onFailure: (_, error) => errors.Add(error));

        CollectionAssert.AreEqual(new[] { 1u, 3u, 4u }, sent);
        Assert.AreEqual(1, errors.Count);
        Assert.AreEqual(1, result!.Changed);
        Assert.AreEqual(2, result.Skipped);
        Assert.AreEqual(1, result.Failed);
    }

    [TestMethod]
    public async Task RunAsync_ContextChanges_StopsBeforeSendingToAnotherSwitch()
    {
        var current = true;
        var sent = new List<uint>();
        Task<bool> Toggle(SmartDevice device, bool target)
        {
            sent.Add(device.EntityId);
            if (device.EntityId == 2)
            {
                current = false;
                return Task.FromException<bool>(new OperationCanceledException("Connection changed"));
            }
            return Task.FromResult(true);
        }

        var result = await new DeviceGroupToggle().RunAsync(
            Group(Switch(1, false), Switch(2, false), Switch(3, false)), _ => false, Toggle, () => current);

        CollectionAssert.AreEqual(new[] { 1u, 2u }, sent);
        Assert.AreEqual(1, result!.Changed);
        Assert.AreEqual(0, result.Failed);
        Assert.AreEqual(2, result.Interrupted);
    }

    [TestMethod]
    public async Task RunAsync_SameGroupAlreadyRunning_IgnoresDuplicateAndAllowsNextClick()
    {
        var group = Group(Switch(1, false));
        var sent = new List<(uint, bool)>();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var toggle = new DeviceGroupToggle();
        var first = toggle.RunAsync(group, _ => false, (_, _) => release.Task, () => true);
        try
        {
            var duplicate = await toggle.RunAsync(group, _ => false, Record(sent), () => true);
            Assert.IsNull(duplicate);
            Assert.AreEqual(0, sent.Count);
        }
        finally
        {
            release.SetResult(true);
            await first;
        }

        await toggle.RunAsync(group, _ => false, Record(sent), () => true);
        CollectionAssert.AreEqual(new[] { (1u, true) }, sent);
    }

    [TestMethod]
    public async Task RunAsync_NoEligibleSwitches_DoesNotConnectOrSend()
    {
        var missing = Switch(1, true);
        missing.IsMissing = true;
        var sent = new List<(uint, bool)>();
        var connectCalls = 0;

        var result = await new DeviceGroupToggle().RunAsync(Group(missing), _ => false, Record(sent), () => true,
            ensureConnectedAsync: () => { connectCalls++; return Task.FromResult(true); });

        Assert.AreEqual(0, connectCalls);
        Assert.AreEqual(0, sent.Count);
        Assert.AreEqual(1, result!.Skipped);
    }

    private static SmartDevice Switch(uint id, bool? on) => new() { EntityId = id, Kind = "SmartSwitch", IsOn = on };

    private static SmartDevice Group(params SmartDevice[] children) => new()
    {
        Kind = "Group",
        IsGroup = true,
        Children = new(children)
    };

    private static Func<SmartDevice, bool, Task<bool>> Record(List<(uint, bool)> sent) => (device, target) =>
    {
        sent.Add((device.EntityId, target));
        return Task.FromResult(true);
    };
}
