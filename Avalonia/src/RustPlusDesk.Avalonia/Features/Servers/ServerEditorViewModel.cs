using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RustPlusDesk.Core.Connections;
using RustPlusDesk.Core.Servers;

namespace RustPlusDesk.Avalonia.Features.Servers;

/// <summary>Owns an unsaved edit, endpoint probe and cancellation until the dialog closes.</summary>
public sealed partial class ServerEditorViewModel : ObservableObject
{
    private readonly string path;
    private readonly ServerSummary original;
    private readonly IServerProfileEditor editor;
    private readonly IServerConnectionTester tester;
    private readonly TaskCompletionSource<ServerSummary?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<ServerSummary?> Completion => completion.Task;

    [ObservableProperty] private string name;
    [ObservableProperty] private string host;
    [ObservableProperty] private string port;
    [ObservableProperty] private string message = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    private bool isBusy;
    public bool CanRun => !IsBusy && !completion.Task.IsCompleted;

    public ServerEditorViewModel(string path, ServerSummary original, IServerProfileEditor editor, IServerConnectionTester tester)
    {
        this.path = path;
        this.original = original;
        this.editor = editor;
        this.tester = tester;
        name = original.Name;
        host = original.Host;
        port = original.Port.ToString(CultureInfo.InvariantCulture);
    }

    private ServerAddress? ReadAddress()
    {
        var address = new ServerAddress(Host.Trim(), int.TryParse(Port, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0);
        try { address.Validate(); return address; }
        catch (ArgumentException) { Message = "Enter a host without spaces and a port between 1 and 65535."; return null; }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (!CanRun || ReadAddress() is not { } address) return;
        IsBusy = true;
        Message = "Saving…";
        try
        {
            var saved = await editor.SaveAsync(path, original, Name, address, cancellationToken);
            completion.TrySetResult(saved);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { Message = "Save canceled."; }
        catch (ProfileChangedException) { Message = "The profiles file changed. Close this editor and refresh before editing again."; }
        catch (ArgumentException) { Message = "Check the address and port. Another profile for this server and account may already exist."; }
        catch (Exception) { Message = "Unable to save. Check file permissions and available disk space. Refresh before retrying."; }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task TestAsync(CancellationToken cancellationToken)
    {
        if (!CanRun || ReadAddress() is not { } address) return;
        IsBusy = true;
        Message = "Testing address and paired devices…";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var result = await tester.TestAsync(path, original, address, timeout.Token);
            Message = result.DevicesChecked == 0 ? "Connection successful. The server responded."
                : $"Connection successful. {result.DevicesResponding} of {result.DevicesChecked} paired devices responded."
                    + (result.DevicesResponding < result.DevicesChecked ? " Check missing devices; the map may have wiped." : "");
        }
        catch (OperationCanceledException) { Message = cancellationToken.IsCancellationRequested ? "Connection test canceled." : "Connection test timed out."; }
        catch (ServerConnectionException ex)
        {
            Message = ex.Failure == ConnectionFailure.RePairRequired ? "Pairing rejected. Re-pair this server from Rust."
                : "Unable to reach this direct address with the saved pairing. Check its host and companion port.";
        }
        catch (Exception) { Message = "Unable to test this connection. Check the address and saved pairing."; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await StopAsync();
        completion.TrySetResult(null);
    }

    public async Task StopAsync()
    {
        SaveCommand.Cancel();
        TestCommand.Cancel();
        if (SaveCommand.ExecutionTask is { } save) await save;
        if (TestCommand.ExecutionTask is { } test) await test;
    }
}
