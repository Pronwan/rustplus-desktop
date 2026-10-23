using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RustPlusDesk.Core.Accounts;

namespace RustPlusDesk.Avalonia.Features.Accounts;

/// <summary>Owns the push enumeration and awaits cleanup before changing account or profile source.</summary>
public sealed partial class CompanionListenerViewModel : ObservableObject
{
    private readonly ICompanionListener listener;
    private readonly Func<CompanionUpdate, Task> paired;
    private readonly ICompanionPushHealth? health;
    private readonly Func<string, CancellationToken, Task<CompanionAccount>>? renew;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private string source;
    private bool healthChecked;
    private TaskCompletionSource<bool> listening = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CompanionListenerViewModel(ICompanionListener listener, string profilesPath, Func<CompanionUpdate, Task> paired,
        ICompanionPushHealth? health = null, Func<string, CancellationToken, Task<CompanionAccount>>? renew = null)
        : this(listener, profilesPath, paired, health, renew, Task.Delay) { }

    internal CompanionListenerViewModel(ICompanionListener listener, string profilesPath, Func<CompanionUpdate, Task> paired,
        ICompanionPushHealth? health, Func<string, CancellationToken, Task<CompanionAccount>>? renew, Func<TimeSpan, CancellationToken, Task> delay)
    { this.listener = listener; source = profilesPath; this.paired = paired; this.health = health; this.renew = renew; this.delay = delay; }

    public bool HasHealth => health is not null;
    public bool HasRenewal => renew is not null;
    public ObservableCollection<CompanionUpdate> Notifications { get; } = [];
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool isRunning;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckDeliveryCommand))]
    private bool isListening;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckDeliveryCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenewCommand))]
    private bool isChecking;
    [ObservableProperty] private string healthMessage = "Push delivery has not been checked.";
    [ObservableProperty] private bool requiresSignIn;
    [ObservableProperty] private string status = "Start listening, then pair a server or device from Rust.";
    private bool CanStart => !IsRunning && !IsChecking;
    private bool CanStop => IsRunning || IsChecking;
    private bool CanCheckDelivery => health is not null && IsListening && !IsChecking;
    private bool CanRenew => renew is not null && !IsChecking;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync(CancellationToken cancellationToken)
    {
        if (IsRunning) return;
        listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IsRunning = true;
        try
        {
            await foreach (var update in listener.ListenAsync(source, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Status = update.Message;
                if (update.Kind is CompanionUpdateKind.Connecting or CompanionUpdateKind.Listening or CompanionUpdateKind.Reconnecting)
                {
                    IsListening = update.Kind == CompanionUpdateKind.Listening;
                    if (IsListening)
                    {
                        listening.TrySetResult(true);
                        if (!healthChecked && health is not null && !IsChecking)
                        { healthChecked = true; CheckDeliveryCommand.Execute(null); }
                    }
                    continue;
                }
                Notifications.Insert(0, update);
                if (Notifications.Count > 100) Notifications.RemoveAt(Notifications.Count - 1);
                if (update.Kind == CompanionUpdateKind.Pairing || update.ProfilesChanged) await paired(update);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { Status = "Rust+ listening stopped."; }
        catch (CompanionListenerException ex)
        {
            RequiresSignIn = ex.Failure is CompanionListenerFailure.MissingCredentials or CompanionListenerFailure.InvalidCredentials;
            Status = ex.Failure switch
            {
                CompanionListenerFailure.MissingCredentials => "Sign in with Steam before listening for pairings.",
                CompanionListenerFailure.InvalidCredentials => "The saved push credentials are invalid. Sign in again with Steam.",
                _ => "Listening stopped because local data could not be read or saved. Check file permissions and refresh profiles."
            };
        }
        catch (Exception) { Status = "Rust+ listening stopped. Check your connection and local files, then try again."; }
        finally { IsListening = false; IsRunning = false; listening.TrySetResult(false); }
    }

    [RelayCommand(CanExecute = nameof(CanCheckDelivery))]
    private Task CheckDeliveryAsync(CancellationToken cancellationToken) => RunHealthAsync(false, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanRenew))]
    private Task RenewAsync(CancellationToken cancellationToken) => RunHealthAsync(true, cancellationToken);

    private async Task RunHealthAsync(bool repair, CancellationToken cancellationToken)
    {
        if (IsChecking || (repair ? !CanRenew : !CanCheckDelivery)) return;
        IsChecking = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            if (!repair)
            {
                HealthMessage = "Checking push delivery…";
                await delay(TimeSpan.FromSeconds(8), timeout.Token);
                var report = await health!.CheckAsync(source, timeout.Token);
                HealthMessage = report.Message;
                RequiresSignIn = report.Outcome == PushHealthOutcome.SignInRequired;
                if (!report.NeedsRepair || renew is null) return;
            }
            HealthMessage = "Renewing Rust+ push registration…";
            await StopTransportAsync();
            timeout.Token.ThrowIfCancellationRequested();
            await renew!(source, timeout.Token);
            RequiresSignIn = false;
            timeout.Token.ThrowIfCancellationRequested();
            healthChecked = true;
            // The health operation owns this restart while user start/renew commands remain disabled.
            StartCommand.Execute(null);
            if (!await listening.Task.WaitAsync(TimeSpan.FromSeconds(30), timeout.Token))
            { HealthMessage = "Registration renewed, but the listener could not restart. Check your connection and try listening again."; return; }
            if (health is null) { HealthMessage = "Push registration renewed and listener restarted."; return; }
            await delay(TimeSpan.FromSeconds(8), timeout.Token);
            var checkedAgain = await health.CheckAsync(source, timeout.Token);
            RequiresSignIn = checkedAgain.Outcome == PushHealthOutcome.SignInRequired;
            HealthMessage = checkedAgain.Outcome == PushHealthOutcome.Healthy
                ? "Push registration renewed. This device received the test push."
                : "Registration renewed, but delivery is still unverified. " + checkedAgain.Message;
        }
        catch (OperationCanceledException) { HealthMessage = "Push check or renewal canceled."; }
        catch (CompanionAccountException ex)
        {
            RequiresSignIn = ex.Failure == AccountFailure.Rejected;
            HealthMessage = ex.Failure == AccountFailure.Rejected
                ? "The saved Rust+ sign-in was rejected. Sign in again with Steam."
                : "Push registration could not be renewed. Check your connection and saved sign-in file.";
        }
        catch (Exception) { HealthMessage = "Push delivery could not be checked or renewed. Try again when the connection is available."; }
        finally { IsChecking = false; }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task StopAsync() => StopListeningAsync();

    public async Task StopListeningAsync()
    {
        CheckDeliveryCommand.Cancel();
        RenewCommand.Cancel();
        await StopTransportAsync();
        if (CheckDeliveryCommand.ExecutionTask is { } check) await check;
        if (RenewCommand.ExecutionTask is { } repair) await repair;
    }

    private async Task StopTransportAsync()
    {
        StartCommand.Cancel();
        if (StartCommand.ExecutionTask is { } running) await running;
    }

    public async Task SetSourceAsync(string path)
    {
        await StopListeningAsync();
        source = path;
        healthChecked = false;
        RequiresSignIn = false;
        HealthMessage = "Push delivery has not been checked.";
        Notifications.Clear();
        Status = "Start listening, then pair a server or device from Rust.";
    }
}
