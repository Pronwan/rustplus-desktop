using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusApi.Data;
using RustPlusApi.Data.Entities;
using RustPlusApi.Data.Events;
using RustPlusApi.Interfaces;
using RustPlusDesk.Core.Devices;
using RustPlusDesk.Infrastructure.Connections;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class RustPlusDeviceClientTests
{
    [TestMethod]
    public async Task Switch_CommandAcknowledgementWaitsForObservedRequestedState()
    {
        var (api, proxy) = ApiProxy.Create();
        int reads = 0;
        proxy.ReadSwitch = () => Success(new SmartDeviceInfo { IsActive = ++reads > 1 });
        using var client = new RustPlusDeviceClient(api, new RustRequestGate());
        var result = await client.SetSwitchAsync(new(7, "Lights", DeviceKind.Switch), true, CancellationToken.None);
        Assert.AreEqual(2, reads);
        Assert.AreEqual(true, result.IsOn);
        Assert.AreEqual(7u, result.EntityId);
        Assert.AreEqual(1, proxy.Sets);
    }

    [TestMethod]
    public async Task MissingEntity_DoesNotBecomeServerAuthenticationFailure()
    {
        var (api, proxy) = ApiProxy.Create();
        proxy.ReadSwitch = () => new() { Error = new() { Message = "not_found" } };
        using var client = new RustPlusDeviceClient(api, new RustRequestGate());
        var result = await client.ReadAsync(new(7, "Alarm", DeviceKind.Alarm), CancellationToken.None);
        Assert.IsTrue(result.IsMissing);
        Assert.IsNull(result.IsOn);
        proxy.ReadSwitch = () => new() { Error = new() { Message = "private server error" } };
        var error = await Assert.ThrowsExceptionAsync<DeviceOperationException>(
            () => client.ReadAsync(new(7, "Alarm", DeviceKind.Alarm), CancellationToken.None));
        Assert.AreEqual(DeviceFailure.Unavailable, error.Failure);
        Assert.IsFalse(error.ToString().Contains("private", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Storage_ResolvesBundledNames_PreservesUnknownsAndEmptySnapshots()
    {
        var (api, proxy) = ApiProxy.Create();
        proxy.Storage = new() { Capacity = 24, HasProtection = true, ProtectionExpiry = DateTime.UtcNow.AddDays(1),
            Items = [new() { Id = -151838493, Quantity = 123, IsItemBlueprint = true }, new() { Id = int.MinValue }] };
        using var client = new RustPlusDeviceClient(api, new RustRequestGate());
        var device = new PairedDevice(8, "TC", DeviceKind.StorageMonitor);
        var result = await client.ReadAsync(device, CancellationToken.None);
        Assert.IsNotNull(result.Storage);
        Assert.AreEqual("Wood", result.Storage.Items[0].Name);
        Assert.AreEqual(123, result.Storage.Items[0].Quantity);
        Assert.AreEqual(true, result.Storage.Items[0].IsBlueprint);
        Assert.IsNull(result.Storage.Items[1].Quantity);
        Assert.AreEqual($"Item #{int.MinValue}", result.Storage.Items[1].Name);
        proxy.Storage = new() { Items = null };
        var empty = (await client.ReadAsync(device, CancellationToken.None)).Storage!;
        Assert.AreEqual(0, empty.Items.Count);
        Assert.IsNull(empty.ProtectionExpiry);
    }

    [TestMethod]
    public async Task Broadcasts_CoalesceKnownEntitiesAndUnsubscribeOnDispose()
    {
        var (api, proxy) = ApiProxy.Create();
        var client = new RustPlusDeviceClient(api, new RustRequestGate());
        await client.ReadAsync(new(7, "Switch", DeviceKind.Switch), CancellationToken.None);
        proxy.Emit(99);
        proxy.Emit(7);
        proxy.Emit(7);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var updates = client.WatchChangesAsync(lifetime.Token).GetAsyncEnumerator();
        Assert.IsTrue(await updates.MoveNextAsync());
        Assert.AreEqual(7u, updates.Current);
        client.Dispose();
        Assert.IsFalse(await updates.MoveNextAsync());
        Assert.IsNull(proxy.EntityChanged);
    }

    [TestMethod]
    public async Task UnsupportedAndCancelledOperations_DoNotSendCommands()
    {
        var (api, proxy) = ApiProxy.Create();
        using var client = new RustPlusDeviceClient(api, new RustRequestGate());
        var unsupported = await Assert.ThrowsExceptionAsync<DeviceOperationException>(
            () => client.SetSwitchAsync(new(7, "Alarm", DeviceKind.Alarm), true, CancellationToken.None));
        Assert.AreEqual(DeviceFailure.Unsupported, unsupported.Failure);
        using var lifetime = new CancellationTokenSource();
        lifetime.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(
            () => client.SetSwitchAsync(new(7, "Switch", DeviceKind.Switch), true, lifetime.Token));
        Assert.AreEqual(0, proxy.Sets);
    }

    private static Response<SmartDeviceInfo?> Success(SmartDeviceInfo data) => new() { IsSuccess = true, Data = data };

    public class ApiProxy : DispatchProxy
    {
        public Func<Response<SmartDeviceInfo?>> ReadSwitch { get; set; } = () => Success(new());
        public StorageMonitorInfo Storage { get; set; } = new();
        public int Sets { get; private set; }
        public EventHandler<EntityChangedEventArg>? EntityChanged { get; private set; }
        public void Emit(uint id) => EntityChanged?.Invoke(this, new() { Id = id });
        public static (IRustPlus Api, ApiProxy Proxy) Create()
        {
            var api = DispatchProxy.Create<IRustPlus, ApiProxy>();
            return (api, (ApiProxy)api);
        }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "add_OnEntityChanged": EntityChanged += (EventHandler<EntityChangedEventArg>)args![0]!; return null;
                case "remove_OnEntityChanged": EntityChanged -= (EventHandler<EntityChangedEventArg>)args![0]!; return null;
                case "GetSmartDeviceInfoAsync": return Task.FromResult(ReadSwitch());
                case "GetStorageMonitorInfoAsync": return Task.FromResult(new Response<StorageMonitorInfo?> { IsSuccess = true, Data = Storage });
                case "SetSmartSwitchValueAsync": Sets++; return Task.FromResult(Success(new()));
                default: throw new AssertFailedException($"Unexpected SDK call: {targetMethod?.Name}");
            }
        }
    }
}
