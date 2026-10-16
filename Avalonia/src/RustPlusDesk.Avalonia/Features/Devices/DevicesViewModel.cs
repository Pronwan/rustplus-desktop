using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RustPlusDesk.Core.Devices;

namespace RustPlusDesk.Avalonia.Features.Devices;

/// <summary>Coordinates refresh, subscriptions and confirmed commands within one session lifetime.</summary>
public sealed partial class DevicesViewModel : ObservableObject
{
    private sealed record ActiveClient(IDeviceClient Client, CancellationToken Token, int Generation);
    private readonly SemaphoreSlim operations = new(1, 1);
    private ActiveClient? active;
    private int generation;
    private Dictionary<uint, DeviceRowViewModel[]> byId = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDevices))]
    private IReadOnlyList<DeviceRowViewModel> devices = [];
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool isConnected;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool isRefreshing;
    [ObservableProperty] private string message = "Connect to load live device state.";
    private bool controlling;

    public bool HasDevices => Devices.Count > 0;
    private IEnumerable<DeviceRowViewModel> AllRows => Devices.SelectMany(row => row.DescendantsAndSelf());
    private bool CanRefresh => IsConnected && !IsRefreshing;

    public void Select(IReadOnlyList<PairedDevice> definitions)
    {
        generation++;
        active = null;
        controlling = false;
        IsConnected = IsRefreshing = false;
        Devices = definitions.Select(device => new DeviceRowViewModel(device, ControlAsync)).ToArray();
        byId = AllRows.Where(row => row.Definition.Kind != DeviceKind.Group && row.Definition.EntityId != 0)
            .GroupBy(row => row.Definition.EntityId).ToDictionary(group => group.Key, group => group.ToArray());
        Message = HasDevices ? "Connect to load live device state." : "No devices paired for this server.";
    }

    public async Task RunAsync(IDeviceClient client, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var context = new ActiveClient(client, lifetime.Token, generation);
        var rows = AllRows.ToArray();
        active = context;
        IsConnected = true;
        foreach (var row in rows) { row.IsConnected = true; row.ControlsEnabled = true; }
        var changes = WatchAsync(context);
        try
        {
            await RefreshAsyncCore(context);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(lifetime.Token)) await RefreshAsyncCore(context);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            lifetime.Cancel();
            await changes;
            var commands = rows.SelectMany(row => new[] { row.TurnOnCommand.ExecutionTask, row.TurnOffCommand.ExecutionTask })
                .Append(RefreshCommand.ExecutionTask)
                .Where(task => task is not null).Cast<Task>();
            await Task.WhenAll(commands);
            foreach (var row in rows) row.Reset();
            if (active == context)
            {
                active = null;
                IsConnected = IsRefreshing = controlling = false;
                Message = "Connect to load live device state.";
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (active is not { } context) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.Token, cancellationToken);
        try { await RefreshAsyncCore(context, linked.Token); }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
    }

    private async Task RefreshAsyncCore(ActiveClient context, CancellationToken cancellationToken = default)
    {
        if (IsRefreshing || active != context) return;
        var token = cancellationToken.CanBeCanceled ? cancellationToken : context.Token;
        IsRefreshing = true;
        try
        {
            foreach (var row in byId.Values.Select(rows => rows[0]).ToArray())
            {
                token.ThrowIfCancellationRequested();
                await ReadAsync(context, row.Definition, token);
            }
            if (active == context) Message = "Live device updates active.";
        }
        finally { if (active == context) IsRefreshing = false; }
    }

    private async Task WatchAsync(ActiveClient context)
    {
        try
        {
            await foreach (uint id in context.Client.WatchChangesAsync(context.Token))
                if (active == context && byId.TryGetValue(id, out var rows))
                    await ReadAsync(context, rows[0].Definition, context.Token);
        }
        catch (OperationCanceledException) when (context.Token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (active == context) Message = "Live updates unavailable. Periodic refresh will continue.";
        }
    }

    private async Task ReadAsync(ActiveClient context, PairedDevice definition, CancellationToken cancellationToken)
    {
        await operations.WaitAsync(cancellationToken);
        try
        {
            context.Token.ThrowIfCancellationRequested();
            var state = await context.Client.ReadAsync(definition, cancellationToken).WaitAsync(cancellationToken);
            if (active == context) Apply(state);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            if (active == context && byId.TryGetValue(definition.EntityId, out var rows))
            {
                foreach (var row in rows)
                {
                    row.Apply(new DeviceSnapshot(definition.EntityId));
                    row.ErrorMessage = FailureMessage(ex);
                }
                UpdateGroups();
            }
        }
        finally { operations.Release(); }
    }

    private async Task ControlAsync(DeviceRowViewModel row, bool value, CancellationToken cancellationToken)
    {
        if (active is not { } context || controlling || !row.SupportsControl || !AllRows.Contains(row)) return;
        controlling = true;
        row.IsBusy = true;
        foreach (var item in AllRows) item.ControlsEnabled = false;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, context.Token);
        int confirmed = 0, failed = 0;
        try
        {
            var targets = row.Definition.Kind == DeviceKind.Group ? row.DescendantsAndSelf() : [row];
            foreach (var target in targets.Where(item => item.Definition.Kind == DeviceKind.Switch)
                .DistinctBy(item => item.Definition.EntityId))
            {
                await operations.WaitAsync(linked.Token);
                try
                {
                    linked.Token.ThrowIfCancellationRequested();
                    var state = await context.Client.SetSwitchAsync(target.Definition, value, linked.Token).WaitAsync(linked.Token);
                    linked.Token.ThrowIfCancellationRequested();
                    if (state.EntityId != target.Definition.EntityId || state.IsMissing || state.IsOn != value)
                        throw new DeviceOperationException(DeviceFailure.ConfirmationTimedOut);
                    if (active == context) Apply(state);
                    confirmed++;
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    failed++;
                    if (active == context)
                    {
                        if (byId.TryGetValue(target.Definition.EntityId, out var aliases))
                            foreach (var alias in aliases)
                            {
                                alias.IsOn = null;
                                alias.ErrorMessage = FailureMessage(ex);
                            }
                    }
                }
                finally { operations.Release(); }
            }
            if (active == context)
            {
                if (row.Definition.Kind == DeviceKind.Group)
                    row.ErrorMessage = failed > 0 ? $"{confirmed} switch(es) confirmed; {failed} failed. Review individual states." : "";
                UpdateGroups();
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        finally
        {
            row.IsBusy = false;
            if (active == context)
            {
                controlling = false;
                foreach (var item in AllRows) item.ControlsEnabled = IsConnected;
            }
        }
    }

    private void Apply(DeviceSnapshot state)
    {
        if (byId.TryGetValue(state.EntityId, out var rows)) foreach (var row in rows) row.Apply(state);
        UpdateGroups();
    }

    internal void UpdateTime()
    {
        foreach (var row in AllRows.Where(row => row.IsStorage && row.Storage is not null)) row.UpdateTime();
    }

    private void UpdateGroups()
    {
        foreach (var group in AllRows.Where(row => row.Definition.Kind == DeviceKind.Group))
        {
            var values = group.DescendantsAndSelf().Where(row => row.Definition.Kind == DeviceKind.Switch).Select(row => row.IsOn).ToArray();
            group.IsOn = values.Length > 0 && values.All(value => value == true) ? true
                : values.Length > 0 && values.All(value => value == false) ? false : null;
        }
    }

    private static string FailureMessage(Exception exception) => exception is DeviceOperationException device ? device.Failure switch
    {
        DeviceFailure.Unsupported => "This device kind is not supported by this operation.",
        DeviceFailure.Rejected => "The server rejected this device command. Refresh and check its pairing.",
        DeviceFailure.ConfirmationTimedOut => "Command sent, but the requested state was not confirmed. Refresh before retrying.",
        _ => "Device unavailable. Refresh to try again."
    } : "Device unavailable. Refresh to try again.";
}
