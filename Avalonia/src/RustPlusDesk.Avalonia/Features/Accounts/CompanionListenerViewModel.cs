using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RustPlusDesk.Core.Accounts;

namespace RustPlusDesk.Avalonia.Features.Accounts;

/// <summary>Owns the push enumeration and awaits cleanup before changing account or profile source.</summary>
public sealed partial class CompanionListenerViewModel(ICompanionListener listener, string profilesPath,
    Func<CompanionUpdate, Task> paired) : ObservableObject
{
    private string source = profilesPath;
    public ObservableCollection<CompanionUpdate> Notifications { get; } = [];
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool isRunning;
    [ObservableProperty] private string status = "Start listening, then pair a server or device from Rust.";
    private bool CanStart => !IsRunning;
    private bool CanStop => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync(CancellationToken cancellationToken)
    {
        if (IsRunning) return;
        IsRunning = true;
        try
        {
            await foreach (var update in listener.ListenAsync(source, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Status = update.Message;
                if (update.Kind is CompanionUpdateKind.Connecting or CompanionUpdateKind.Listening or CompanionUpdateKind.Reconnecting) continue;
                Notifications.Insert(0, update);
                if (Notifications.Count > 100) Notifications.RemoveAt(Notifications.Count - 1);
                if (update.Kind == CompanionUpdateKind.Pairing || update.ProfilesChanged) await paired(update);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { Status = "Rust+ listening stopped."; }
        catch (CompanionListenerException ex)
        {
            Status = ex.Failure switch
            {
                CompanionListenerFailure.MissingCredentials => "Sign in with Steam before listening for pairings.",
                CompanionListenerFailure.InvalidCredentials => "The saved push credentials are invalid. Sign in again with Steam.",
                _ => "Listening stopped because local data could not be read or saved. Check file permissions and refresh profiles."
            };
        }
        catch (Exception) { Status = "Rust+ listening stopped. Check your connection and local files, then try again."; }
        finally { IsRunning = false; }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task StopAsync() => StopListeningAsync();

    public async Task StopListeningAsync()
    {
        StartCommand.Cancel();
        if (StartCommand.ExecutionTask is { } running) await running;
    }

    public async Task SetSourceAsync(string path)
    {
        await StopListeningAsync();
        source = path;
        Notifications.Clear();
        Status = "Start listening, then pair a server or device from Rust.";
    }
}
