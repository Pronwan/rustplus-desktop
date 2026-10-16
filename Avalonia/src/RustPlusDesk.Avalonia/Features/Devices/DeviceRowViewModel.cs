using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RustPlusDesk.Core.Devices;

namespace RustPlusDesk.Avalonia.Features.Devices;

public sealed partial class DeviceRowViewModel : ObservableObject
{
    private readonly Func<DeviceRowViewModel, bool, CancellationToken, Task> control;
    public PairedDevice Definition { get; }
    public IReadOnlyList<DeviceRowViewModel> Children { get; }
    public string Name => Definition.Name;
    public string KindLabel => Definition.Kind switch
    {
        DeviceKind.Switch => "Smart switch", DeviceKind.Alarm => "Smart alarm",
        DeviceKind.StorageMonitor => "Storage monitor", DeviceKind.Group => "Group", _ => "Unknown device"
    };
    public bool IsStorage => Definition.Kind == DeviceKind.StorageMonitor;
    public bool SupportsControl => Definition.Kind == DeviceKind.Switch || Definition.Kind == DeviceKind.Group
        && Definition.DescendantsAndSelf().Any(device => device.Kind == DeviceKind.Switch);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    private bool? isOn;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    private bool isMissing;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    private bool isConnected;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TurnOnCommand))]
    [NotifyCanExecuteChangedFor(nameof(TurnOffCommand))]
    private bool controlsEnabled;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TurnOnCommand))]
    [NotifyCanExecuteChangedFor(nameof(TurnOffCommand))]
    private bool isBusy;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string errorMessage = "";
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    [NotifyPropertyChangedFor(nameof(StorageCaption))]
    private IReadOnlyList<StorageItemViewModel> items = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StorageCaption))]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    private DeviceStorage? storage;

    public string StateLabel => !IsConnected ? "Disconnected" : IsMissing ? "Missing"
        : IsStorage ? Storage is null ? "Unknown" : $"{Items.Count} item slots"
        : IsOn is not bool on ? Definition.Kind == DeviceKind.Group ? "Mixed / unknown" : "Unknown"
        : Definition.Kind == DeviceKind.Alarm ? on ? "ACTIVE" : "Inactive" : on ? "ON" : "OFF";

    public string StorageCaption
    {
        get
        {
            if (Storage is null) return "No storage snapshot";
            string caption = $"Capacity: {Storage.Capacity?.ToString() ?? "—"}";
            if (Storage.HasProtection == true && Storage.ProtectionExpiry is { } expiry)
            {
                var remaining = expiry - DateTimeOffset.UtcNow;
                caption += remaining > TimeSpan.Zero ? $" · Upkeep: {(int)remaining.TotalDays}d {remaining.Hours}h {remaining.Minutes}m" : " · Upkeep expired";
            }
            return caption;
        }
    }

    public DeviceRowViewModel(PairedDevice definition, Func<DeviceRowViewModel, bool, CancellationToken, Task> control)
    {
        Definition = definition;
        this.control = control;
        Children = definition.Children.Select(child => new DeviceRowViewModel(child, control)).ToArray();
    }

    private bool CanControl => ControlsEnabled && !IsBusy && SupportsControl;
    [RelayCommand(CanExecute = nameof(CanControl))]
    private Task TurnOnAsync(CancellationToken cancellationToken) => control(this, true, cancellationToken);
    [RelayCommand(CanExecute = nameof(CanControl))]
    private Task TurnOffAsync(CancellationToken cancellationToken) => control(this, false, cancellationToken);

    public IEnumerable<DeviceRowViewModel> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var row in child.DescendantsAndSelf()) yield return row;
    }

    internal void Apply(DeviceSnapshot state)
    {
        IsMissing = state.IsMissing;
        IsOn = state.IsOn;
        Storage = state.Storage;
        Items = state.Storage?.Items.Select(item => new StorageItemViewModel(item)).ToArray() ?? [];
        ErrorMessage = state.IsMissing ? "This device was not found. The server connection is still valid." : "";
    }

    internal void Reset()
    {
        IsConnected = ControlsEnabled = IsBusy = IsMissing = false;
        IsOn = null;
        Storage = null;
        Items = [];
        ErrorMessage = "";
    }

    internal void UpdateTime() => OnPropertyChanged(nameof(StorageCaption));
}

public sealed record StorageItemViewModel(DeviceItem Item)
{
    public string Name => string.IsNullOrWhiteSpace(Item.Name) ? $"Item #{Item.ItemId}" : Item.Name;
    public string Quantity => Item.Quantity?.ToString("N0") ?? "—";
    public bool IsBlueprint => Item.IsBlueprint == true;
}
