using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RustPlusDesk.Core.Devices;
using RustPlusDesk.Core.Servers;

namespace RustPlusDesk.Avalonia.Features.Devices;

public sealed record DeviceGroupChoice(string Name, string? DeviceKey);

/// <summary>Owns revision-protected local organization and awaits writes before closing.</summary>
public sealed partial class DeviceOrganizerViewModel : ObservableObject
{
    private readonly string path;
    private readonly IDeviceProfileEditor editor;
    private ServerSummary current;
    private bool changed;
    private bool closing;
    private readonly TaskCompletionSource<ServerSummary?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<ServerSummary?> Completion => completion.Task;
    public IReadOnlyList<PairedDevice> Devices => current.Devices;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAlarmTitleCommand))]
    [NotifyPropertyChangedFor(nameof(IsAlarm))]
    [NotifyCanExecuteChangedFor(nameof(GroupCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    private PairedDevice? selected;
    [ObservableProperty] private string alias = "";
    [ObservableProperty] private string alarmTitle = "";
    public bool IsAlarm => Selected?.Kind == DeviceKind.Alarm;
    private bool CanEditAlarm => CanEdit && IsAlarm;
    [ObservableProperty] private string groupName = "Group";
    [ObservableProperty] private IReadOnlyList<DeviceGroupChoice> destinations = [];
    [ObservableProperty] private DeviceGroupChoice? destination;
    [ObservableProperty] private string message = "Each action saves locally. Cloud synchronization is not available in this preview.";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAlarmTitleCommand))]
    [NotifyCanExecuteChangedFor(nameof(GroupCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    private bool isBusy;
    private bool needsRefresh;
    private bool CanEdit => !closing && !IsBusy && !needsRefresh && Selected is not null;
    private bool CanMoveUp => CanEdit && Position > 0;
    private bool CanMoveDown => CanEdit && Position < Siblings.Count - 1;
    private int Position => Selected is null ? -1 : int.Parse(Selected.DeviceKey.Split('/')[^1], CultureInfo.InvariantCulture);
    private string? ParentKey => Selected?.DeviceKey.LastIndexOf('/') is int slash && slash >= 0 ? Selected.DeviceKey[..slash] : null;
    private IReadOnlyList<PairedDevice> Siblings => ParentKey is { } key
        ? Devices.SelectMany(item => item.DescendantsAndSelf()).First(item => item.DeviceKey == key).Children : Devices;

    public DeviceOrganizerViewModel(string path, ServerSummary original, IDeviceProfileEditor editor)
    {
        this.path = path; current = original; this.editor = editor;
        Selected = Devices.FirstOrDefault();
    }

    partial void OnSelectedChanged(PairedDevice? value)
    {
        Alias = value?.Alias ?? "";
        AlarmTitle = value?.InGameAlarmTitle ?? "";
        var choices = new List<DeviceGroupChoice> { new("All devices", null) };
        void Add(IEnumerable<PairedDevice> items, string prefix)
        {
            foreach (var item in items)
            {
                if (value is not null && (item.DeviceKey == value.DeviceKey || item.DeviceKey.StartsWith(value.DeviceKey + "/", StringComparison.Ordinal))) continue;
                string name = prefix + item.Name;
                if (item.Kind == DeviceKind.Group) choices.Add(new(name, item.DeviceKey));
                Add(item.Children, name + " / ");
            }
        }
        Add(Devices, "");
        Destinations = choices;
        Destination = choices.FirstOrDefault(item => item.DeviceKey == ParentKey) ?? choices[0];
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task RenameAsync(CancellationToken ct) => SaveAsync((server, key) => editor.RenameAsync(path, server, key, Alias, ct));
    [RelayCommand(CanExecute = nameof(CanEditAlarm))]
    private Task SaveAlarmTitleAsync(CancellationToken ct) => SaveAsync((server, key) => editor.SaveAlarmTitleAsync(path, server, key, AlarmTitle, ct));
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task GroupAsync(CancellationToken ct) => SaveAsync((server, key) => editor.GroupAsync(path, server, key, GroupName, ct));
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task MoveAsync(CancellationToken ct) => SaveAsync((server, key) => editor.MoveAsync(path, server, key, Destination?.DeviceKey, null, ct));
    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private Task MoveUpAsync(CancellationToken ct) => ReorderAsync(-1, ct);
    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private Task MoveDownAsync(CancellationToken ct) => ReorderAsync(1, ct);

    private Task ReorderAsync(int delta, CancellationToken ct)
        => Position + delta < 0 || Position + delta >= Siblings.Count ? Task.CompletedTask
            : SaveAsync((server, key) => editor.MoveAsync(path, server, key, ParentKey, Position + delta, ct));

    private async Task SaveAsync(Func<ServerSummary, string, Task<DeviceEditResult>> save)
    {
        if (!CanEdit) return;
        IsBusy = true;
        try
        {
            var result = await save(current, Selected!.DeviceKey);
            changed |= result.Server.SourceRevision != current.SourceRevision;
            current = result.Server;
            OnPropertyChanged(nameof(Devices));
            Selected = Devices.SelectMany(item => item.DescendantsAndSelf()).First(item => item.DeviceKey == result.DeviceKey);
            Message = "Saved locally. Cloud synchronization is not available in this preview.";
        }
        catch (OperationCanceledException) { Message = "Save canceled."; }
        catch (ProfileChangedException) { needsRefresh = true; Message = "The profiles file changed. Close and refresh before organizing again."; }
        catch (ArgumentException) { Message = "Enter a group name and choose a valid destination."; }
        catch (Exception) { Message = "Unable to save. Check file permissions and available disk space, then close and refresh."; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        closing = true;
        await StopAsync();
        completion.TrySetResult(changed ? current : null);
    }

    public async Task StopAsync()
    {
        foreach (var command in Commands) command.Cancel();
        foreach (var command in Commands) if (command.ExecutionTask is { } task) await task;
    }

    private IAsyncRelayCommand[] Commands => [RenameCommand, SaveAlarmTitleCommand, GroupCommand, MoveCommand, MoveUpCommand, MoveDownCommand];
}
