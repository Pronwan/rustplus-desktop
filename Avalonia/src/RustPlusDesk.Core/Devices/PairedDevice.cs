namespace RustPlusDesk.Core.Devices;

public enum DeviceKind { Unknown, Switch, Alarm, StorageMonitor, Group }

/// <summary>Paired device metadata, separate from transient server state and presentation.</summary>
public sealed record PairedDevice(uint EntityId, string Name, DeviceKind Kind)
{
    /// <summary>Position in the saved tree; valid only with the containing server's source revision.</summary>
    public string DeviceKey { get; init; } = "";
    public string? Alias { get; init; }
    public string? InGameAlarmTitle { get; init; }
    public IReadOnlyList<PairedDevice> Children { get; init; } = [];

    public IEnumerable<PairedDevice> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var device in child.DescendantsAndSelf()) yield return device;
    }
}

public sealed record DeviceItem(int ItemId, int? Quantity, bool? IsBlueprint)
{
    public string Name { get; init; } = "";
}

public sealed record DeviceStorage(int? Capacity, bool? HasProtection, DateTimeOffset? ProtectionExpiry,
    IReadOnlyList<DeviceItem> Items);

public sealed record DeviceSnapshot(uint EntityId, bool? IsOn = null, DeviceStorage? Storage = null, bool IsMissing = false);

public enum DeviceFailure { Unavailable, Rejected, ConfirmationTimedOut, Unsupported }

public sealed class DeviceOperationException(DeviceFailure failure) : Exception(failure.ToString())
{
    public DeviceFailure Failure { get; } = failure;
}
