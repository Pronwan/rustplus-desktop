using RustPlusDesk.Core.Servers;

namespace RustPlusDesk.Core.Devices;

/// <summary>A saved tree and the edited item's new position after a revision-protected mutation.</summary>
public sealed record DeviceEditResult(ServerSummary Server, string DeviceKey);

/// <summary>Local device organization; device settings and unknown persisted fields remain intact.</summary>
public interface IDeviceProfileEditor
{
    Task<DeviceEditResult> RenameAsync(string path, ServerSummary original, string deviceKey, string alias, CancellationToken cancellationToken);
    Task<DeviceEditResult> SaveAlarmTitleAsync(string path, ServerSummary original, string deviceKey, string title, CancellationToken cancellationToken);
    Task<DeviceEditResult> GroupAsync(string path, ServerSummary original, string deviceKey, string name, CancellationToken cancellationToken);
    Task<DeviceEditResult> MoveAsync(string path, ServerSummary original, string deviceKey, string? parentKey, int? index, CancellationToken cancellationToken);
}
