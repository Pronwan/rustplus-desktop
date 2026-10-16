namespace RustPlusDesk.Core.Devices;

/// <summary>Device operations scoped to an owned authenticated session.</summary>
public interface IDeviceClient
{
    Task<DeviceSnapshot> ReadAsync(PairedDevice device, CancellationToken cancellationToken);
    Task<DeviceSnapshot> SetSwitchAsync(PairedDevice device, bool value, CancellationToken cancellationToken);
    IAsyncEnumerable<uint> WatchChangesAsync(CancellationToken cancellationToken);
}
