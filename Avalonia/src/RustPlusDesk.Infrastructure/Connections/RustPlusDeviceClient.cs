using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using RustPlusApi.Interfaces;
using RustPlusApi.Data.Events;
using RustPlusDesk.Core.Devices;
using RustPlusDesk.Infrastructure.Items;

namespace RustPlusDesk.Infrastructure.Connections;

internal sealed class RustPlusDeviceClient : IDeviceClient, IDisposable
{
    private readonly IRustPlus api;
    private readonly RustRequestGate requests;
    private readonly ConcurrentDictionary<uint, byte> watched = new();
    private readonly ConcurrentDictionary<uint, byte> pending = new();
    // One queued invalidation per subscribed entity bounds bursts without discarding an entity's latest state.
    private readonly Channel<uint> changes = Channel.CreateUnbounded<uint>(new UnboundedChannelOptions { SingleReader = true });

    public RustPlusDeviceClient(IRustPlus api, RustRequestGate requests)
    {
        this.api = api;
        this.requests = requests;
        api.OnEntityChanged += EntityChanged;
    }

    public async Task<DeviceSnapshot> ReadAsync(PairedDevice device, CancellationToken cancellationToken)
    {
        if (device.EntityId == 0 || device.Kind is DeviceKind.Group or DeviceKind.Unknown)
            throw new DeviceOperationException(DeviceFailure.Unsupported);
        watched.TryAdd(device.EntityId, 0);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            await requests.WaitAsync(timeout.Token).ConfigureAwait(false);
            if (device.Kind == DeviceKind.StorageMonitor)
            {
                var response = await api.GetStorageMonitorInfoAsync(device.EntityId, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccess || response.Data is null) return MissingOrThrow(device.EntityId, response.Error?.Message);
                var data = response.Data;
                return new DeviceSnapshot(device.EntityId, Storage: new DeviceStorage(data.Capacity, data.HasProtection,
                    data.ProtectionExpiry == default ? null : new DateTimeOffset(DateTime.SpecifyKind(data.ProtectionExpiry, DateTimeKind.Utc)),
                    (data.Items ?? []).Select(item => new DeviceItem(item.Id, item.Quantity, item.IsItemBlueprint)
                    { Name = BundledItemCatalog.GetName(item.Id) }).ToArray()));
            }
            var state = await api.GetSmartDeviceInfoAsync(device.EntityId, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            return state.IsSuccess && state.Data is not null
                ? new DeviceSnapshot(device.EntityId, state.Data.IsActive)
                : MissingOrThrow(device.EntityId, state.Error?.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (DeviceOperationException) { throw; }
        catch (Exception) { throw new DeviceOperationException(DeviceFailure.Unavailable); }
    }

    public async Task<DeviceSnapshot> SetSwitchAsync(PairedDevice device, bool value, CancellationToken cancellationToken)
    {
        if (device.Kind != DeviceKind.Switch || device.EntityId == 0)
            throw new DeviceOperationException(DeviceFailure.Unsupported);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        bool sent = false;
        try
        {
            await requests.WaitAsync(timeout.Token).ConfigureAwait(false);
            var result = await api.SetSmartSwitchValueAsync(device.EntityId, value, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            if (!result.IsSuccess) throw new DeviceOperationException(DeviceFailure.Rejected);
            sent = true;
            while (true)
            {
                var state = await ReadAsync(device, timeout.Token).ConfigureAwait(false);
                if (state.IsMissing) throw new DeviceOperationException(DeviceFailure.Rejected);
                if (state.IsOn == value) return state;
                await Task.Delay(200, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (DeviceOperationException) { throw; }
        catch (Exception)
        {
            throw new DeviceOperationException(sent ? DeviceFailure.ConfirmationTimedOut : DeviceFailure.Unavailable);
        }
    }

    public async IAsyncEnumerable<uint> WatchChangesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (uint id in changes.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            pending.TryRemove(id, out _);
            yield return id;
        }
    }

    private void EntityChanged(object? sender, EntityChangedEventArg args)
    {
        if (args.Id > uint.MaxValue) return;
        uint id = (uint)args.Id;
        if (watched.ContainsKey(id) && pending.TryAdd(id, 0) && !changes.Writer.TryWrite(id)) pending.TryRemove(id, out _);
    }

    private static DeviceSnapshot MissingOrThrow(uint id, string? error)
        => string.Equals(error?.Trim(), "not_found", StringComparison.OrdinalIgnoreCase)
            ? new DeviceSnapshot(id, IsMissing: true) : throw new DeviceOperationException(DeviceFailure.Unavailable);

    public void Dispose()
    {
        api.OnEntityChanged -= EntityChanged;
        changes.Writer.TryComplete();
    }
}
