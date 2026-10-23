using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Avalonia.Features.Devices;
using RustPlusDesk.Core.Devices;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class DevicesViewModelTests
{
    private static readonly PairedDevice Switch = new(1, "Lights", DeviceKind.Switch);

    [TestMethod]
    public async Task Switch_WaitsForConfirmation_DisablesOtherCommands_ThenResetsOnDisconnect()
    {
        var confirmation = new TaskCompletionSource<DeviceSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new TestDeviceClient
        {
            Read = (device, _) => Task.FromResult(new DeviceSnapshot(device.EntityId, false)),
            Set = (_, _, _) => confirmation.Task
        };
        var model = new DevicesViewModel();
        model.Select([Switch]);
        using var lifetime = new CancellationTokenSource();
        var running = model.RunAsync(client, lifetime.Token);
        var row = model.Devices.Single();
        try
        {
            Assert.AreEqual(false, row.IsOn);
            var command = row.TurnOnCommand.ExecuteAsync(null);
            Assert.IsTrue(row.IsBusy);
            Assert.IsFalse(row.TurnOffCommand.CanExecute(null));
            Assert.AreEqual(false, row.IsOn);
            confirmation.SetResult(new(1, true));
            await command.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(true, row.IsOn);
            Assert.IsTrue(row.TurnOffCommand.CanExecute(null));
        }
        finally { lifetime.Cancel(); await running; }
        Assert.IsFalse(row.IsConnected);
        Assert.IsNull(row.IsOn);
        Assert.IsFalse(row.TurnOnCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task NestedGroup_ControlsUniqueSwitches_ReportsPartialFailureAcrossAliases()
    {
        var nested = new PairedDevice(0, "Nested", DeviceKind.Group)
        { Children = [Switch, new(2, "Pump", DeviceKind.Switch), new(3, "Alarm", DeviceKind.Alarm)] };
        var calls = new List<uint>();
        var client = new TestDeviceClient
        {
            Read = (device, _) => Task.FromResult(new DeviceSnapshot(device.EntityId, false)),
            Set = (device, value, _) =>
            {
                calls.Add(device.EntityId);
                return device.EntityId == 2 ? Task.FromException<DeviceSnapshot>(new DeviceOperationException(DeviceFailure.Rejected))
                    : Task.FromResult(new DeviceSnapshot(device.EntityId, value));
            }
        };
        var model = new DevicesViewModel();
        model.Select([new(0, "All", DeviceKind.Group) { Children = [nested, Switch, new(2, "Pump alias", DeviceKind.Switch)] }]);
        using var lifetime = new CancellationTokenSource();
        var running = model.RunAsync(client, lifetime.Token);
        try
        {
            var group = model.Devices.Single();
            await group.TurnOnCommand.ExecuteAsync(null);
            CollectionAssert.AreEqual(new uint[] { 1, 2 }, calls);
            Assert.IsNull(group.IsOn);
            StringAssert.Contains(group.ErrorMessage, "1 switch(es) confirmed; 1 failed");
            var rows = group.DescendantsAndSelf().Where(row => row.Definition.EntityId == 2).ToArray();
            Assert.AreEqual(2, rows.Length);
            Assert.IsTrue(rows.All(row => row.IsOn is null && row.ErrorMessage.Contains("rejected", StringComparison.Ordinal)));
            Assert.AreEqual(false, group.DescendantsAndSelf().Single(row => row.Definition.Kind == DeviceKind.Alarm).IsOn);
        }
        finally { lifetime.Cancel(); await running; }
    }

    [TestMethod]
    public async Task WrongConfirmation_DoesNotShowSuccessOrExposeRawErrors()
    {
        var client = new TestDeviceClient { Set = (_, _, _) => Task.FromResult(new DeviceSnapshot(1, false)) };
        var model = new DevicesViewModel();
        model.Select([Switch]);
        using var lifetime = new CancellationTokenSource();
        var running = model.RunAsync(client, lifetime.Token);
        try
        {
            var row = model.Devices.Single();
            await row.TurnOnCommand.ExecuteAsync(null);
            Assert.IsNull(row.IsOn);
            StringAssert.Contains(row.ErrorMessage, "not confirmed");
            client.Set = (_, _, _) => Task.FromException<DeviceSnapshot>(new IOException("private token and request"));
            await row.TurnOnCommand.ExecuteAsync(null);
            Assert.AreEqual("Device unavailable. Refresh to try again.", row.ErrorMessage);
        }
        finally { lifetime.Cancel(); await running; }
    }

    [TestMethod]
    public async Task MissingDevice_IsLocalFailure_StorageRefreshCanClearAllItems()
    {
        bool emptied = false;
        var client = new TestDeviceClient { Read = (device, _) => Task.FromResult(device.EntityId == 1
            ? new DeviceSnapshot(1, IsMissing: true)
            : new DeviceSnapshot(2, Storage: new(24, true, DateTimeOffset.UtcNow.AddDays(2),
                emptied ? [] : [new(-151838493, 1200, true) { Name = "Wood" }]))) };
        var model = new DevicesViewModel();
        model.Select([Switch, new(2, "Tool cupboard", DeviceKind.StorageMonitor)]);
        using var lifetime = new CancellationTokenSource();
        var running = model.RunAsync(client, lifetime.Token);
        try
        {
            Assert.IsTrue(model.IsConnected);
            Assert.AreEqual("Missing", model.Devices[0].StateLabel);
            var storage = model.Devices[1];
            Assert.AreEqual("Wood", storage.Items.Single().Name);
            Assert.IsTrue(storage.Items.Single().IsBlueprint);
            StringAssert.Contains(storage.StorageCaption, "Upkeep:");
            emptied = true;
            await model.RefreshCommand.ExecuteAsync(null);
            Assert.AreEqual(0, storage.Items.Count);
            Assert.AreEqual("0 item slots", storage.StateLabel);
            Assert.IsTrue(model.IsConnected);
        }
        finally { lifetime.Cancel(); await running; }
    }

    [TestMethod]
    public async Task Broadcast_InvalidatesStateAndReadsAuthoritativeSnapshot()
    {
        bool on = false;
        var client = new TestDeviceClient { Read = (device, _) => Task.FromResult(new DeviceSnapshot(device.EntityId, on)) };
        var model = new DevicesViewModel();
        model.Select([Switch]);
        using var lifetime = new CancellationTokenSource();
        var running = model.RunAsync(client, lifetime.Token);
        try
        {
            var row = model.Devices.Single();
            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            row.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(row.IsOn) && row.IsOn == true) changed.TrySetResult(); };
            on = true;
            await client.Changes.Writer.WriteAsync(1);
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual("ON", row.StateLabel);
        }
        finally { lifetime.Cancel(); await running; }
    }

    [TestMethod]
    public async Task ServerChange_CancelsCommand_IgnoresLateCompletionAndOldControls()
    {
        var completion = new TaskCompletionSource<DeviceSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new DevicesViewModel();
        model.Select([Switch]);
        using var firstLifetime = new CancellationTokenSource();
        var firstRun = model.RunAsync(new TestDeviceClient { Set = (_, _, _) => completion.Task }, firstLifetime.Token);
        var oldRow = model.Devices.Single();
        var command = oldRow.TurnOnCommand.ExecuteAsync(null);
        model.Select([new(2, "Other server switch", DeviceKind.Switch)]);
        using var secondLifetime = new CancellationTokenSource();
        int calls = 0;
        var secondRun = model.RunAsync(new TestDeviceClient { Set = (device, value, _) =>
        { calls++; return Task.FromResult(new DeviceSnapshot(device.EntityId, value)); } }, secondLifetime.Token);
        try
        {
            firstLifetime.Cancel();
            await firstRun.WaitAsync(TimeSpan.FromSeconds(5));
            await command;
            completion.SetResult(new(1, true));
            await oldRow.TurnOnCommand.ExecuteAsync(null);
            Assert.AreEqual(0, calls);
            Assert.IsTrue(model.IsConnected);
            Assert.IsNull(model.Devices.Single().IsOn);
            Assert.AreEqual("Disconnected", oldRow.StateLabel);
        }
        finally { firstLifetime.Cancel(); secondLifetime.Cancel(); await Task.WhenAll(firstRun, secondRun); }
    }
}
