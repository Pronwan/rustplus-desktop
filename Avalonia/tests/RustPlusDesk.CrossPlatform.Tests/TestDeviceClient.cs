using System.Runtime.CompilerServices;
using System.Threading.Channels;
using RustPlusDesk.Core.Devices;

namespace RustPlusDesk.CrossPlatform.Tests;

internal sealed class TestDeviceClient : IDeviceClient
{
    public Func<PairedDevice, CancellationToken, Task<DeviceSnapshot>> Read { get; set; }
        = (device, _) => Task.FromResult(new DeviceSnapshot(device.EntityId));
    public Func<PairedDevice, bool, CancellationToken, Task<DeviceSnapshot>> Set { get; set; }
        = (device, value, _) => Task.FromResult(new DeviceSnapshot(device.EntityId, value));
    public Channel<uint> Changes { get; } = Channel.CreateUnbounded<uint>();
    public Task<DeviceSnapshot> ReadAsync(PairedDevice device, CancellationToken cancellationToken) => Read(device, cancellationToken);
    public Task<DeviceSnapshot> SetSwitchAsync(PairedDevice device, bool value, CancellationToken cancellationToken) => Set(device, value, cancellationToken);
    public async IAsyncEnumerable<uint> WatchChangesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (uint id in Changes.Reader.ReadAllAsync(cancellationToken)) yield return id;
    }
}
