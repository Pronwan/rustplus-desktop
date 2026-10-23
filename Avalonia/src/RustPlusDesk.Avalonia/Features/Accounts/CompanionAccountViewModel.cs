using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RustPlusDesk.Core.Accounts;

namespace RustPlusDesk.Avalonia.Features.Accounts;

public sealed partial class CompanionAccountViewModel(ICompanionAccountService service, string profilesPath, Func<Task> beforeSignIn,
    Func<Task>? afterSignIn = null) : ObservableObject
{
    private string configPath = ConfigPathFor(profilesPath);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Identity))]
    [NotifyPropertyChangedFor(nameof(RequiresSignIn))]
    private CompanionAccount account = new(AccountState.Missing);
    [ObservableProperty] private string message = "Check your Rust+ sign-in status or sign in with Steam.";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(RequestSignOutCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmSignOutCommand))]
    private bool isBusy;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingSignOut))]
    [NotifyPropertyChangedFor(nameof(SignOutExplanation))]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(RequestSignOutCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmSignOutCommand))]
    private CompanionSignOutScope? pendingSignOut;
    public bool IsConfirmingSignOut => PendingSignOut.HasValue;
    public string SignOutExplanation => PendingSignOut == CompanionSignOutScope.AllDevices
        ? "Sign out across devices using this Rust+ sign-in? You will need to sign in again. Your paired servers stay saved."
        : "Sign out this device and stop its Rust+ pairing notifications? Your paired servers stay saved.";
    public string Identity => string.IsNullOrEmpty(Account.SteamId) ? "Rust+ Companion" : $"Steam ID: {Account.SteamId}";
    public bool RequiresSignIn => Account.State is AccountState.Missing or AccountState.Expired or AccountState.Rejected;
    private bool CanRun => !IsBusy && !IsConfirmingSignOut;
    private bool CanConfirmSignOut => !IsBusy && IsConfirmingSignOut;

    public async Task SetSourceAsync(string source)
    {
        await StopAsync();
        PendingSignOut = null;
        configPath = ConfigPathFor(source);
        Account = new(AccountState.Missing);
        Message = "Check your Rust+ sign-in status or sign in with Steam.";
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task CheckAsync(CancellationToken cancellationToken) => RunAsync(false, cancellationToken);
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task SignInAsync(CancellationToken cancellationToken) => RunAsync(true, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void RequestSignOut(CompanionSignOutScope scope)
    {
        if (CanRun && Enum.IsDefined(scope)) PendingSignOut = scope;
    }

    [RelayCommand]
    private void KeepSignedIn() { if (!IsBusy) PendingSignOut = null; }

    [RelayCommand(CanExecute = nameof(CanConfirmSignOut))]
    private async Task ConfirmSignOutAsync(CancellationToken cancellationToken)
    {
        if (!CanConfirmSignOut || PendingSignOut is not { } scope) return;
        IsBusy = true;
        Message = "Signing out of Rust+…";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await beforeSignIn();
            timeout.Token.ThrowIfCancellationRequested();
            var result = await service.SignOutAsync(configPath, scope, timeout.Token);
            Account = new(result == CompanionSignOutResult.RemoteCompletedLocalCleanupFailed ? AccountState.Unavailable : AccountState.Missing);
            Message = result switch
            {
                CompanionSignOutResult.Completed => scope == CompanionSignOutScope.AllDevices
                    ? "Signed out across devices. Local sign-in removed." : "This device is signed out. Local sign-in removed.",
                CompanionSignOutResult.LocalOnly => "Local sign-in removed. Facepunch did not confirm a remote sign-out; the saved token was missing or rejected.",
                _ => "Facepunch sign-out succeeded, but the local file could not be updated. Check status before signing in again."
            };
        }
        catch (OperationCanceledException) { Message = "Sign-out was canceled or timed out. Check status before trying again."; }
        catch (CompanionAccountException ex)
        {
            Message = ex.Failure switch
            {
                AccountFailure.MissingDeviceRegistration => "The saved device ID is missing. Sign out across devices or check your sign-in file.",
                AccountFailure.InvalidConfiguration => "The saved sign-in file is unreadable. Keep a copy before repairing it.",
                AccountFailure.ChangedConfiguration => "The local sign-in file changed. Refresh status before signing out again.",
                _ => "Unable to confirm sign-out. Check your connection and try again; saved sign-in was kept."
            };
        }
        catch (Exception) { Message = "Unable to confirm sign-out. Check status before trying again."; }
        finally { PendingSignOut = null; IsBusy = false; }
    }

    private async Task RunAsync(bool signIn, CancellationToken cancellationToken)
    {
        if (!CanRun) return;
        IsBusy = true;
        Message = signIn ? "Complete Steam sign-in in the authentication window…" : "Checking Rust+ sign-in…";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(signIn ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(30));
        try
        {
            if (signIn) await beforeSignIn();
            timeout.Token.ThrowIfCancellationRequested();
            var result = signIn ? await service.SignInAsync(configPath, timeout.Token) : await service.CheckAsync(configPath, timeout.Token);
            if (!signIn) timeout.Token.ThrowIfCancellationRequested();
            Account = result;
            Message = result.State switch
            {
                AccountState.Active => "Signed in to Rust+.",
                AccountState.Missing => "No Rust+ sign-in is saved for this profiles file.",
                AccountState.Expired => "Your Rust+ sign-in expired. Sign in again with Steam.",
                AccountState.Rejected => "Your Rust+ sign-in was rejected. Sign in again with Steam.",
                AccountState.Offline => "Unable to reach Facepunch. Sign-in could not be verified.",
                AccountState.Invalid => "The saved sign-in file is unreadable. Keep a copy before repairing it.",
                _ => "Sign-in status is unavailable. Check your connection and file permissions."
            };
            if (signIn && result.State == AccountState.Active && afterSignIn is not null && !cancellationToken.IsCancellationRequested)
                await afterSignIn();
        }
        catch (OperationCanceledException) { Message = timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested ? "Sign-in operation timed out." : "Sign-in operation canceled."; }
        catch (CompanionAccountException ex)
        {
            Message = ex.Failure switch
            {
                AccountFailure.InvalidConfiguration => "The saved sign-in file is unreadable. Keep a copy before repairing it.",
                AccountFailure.ChangedConfiguration => "The saved sign-in changed in another application. Check status before trying again.",
                AccountFailure.PushRegistration => "Unable to register this device for Rust+ pairing. Try signing in again.",
                AccountFailure.Rejected => "Steam sign-in was not accepted. Try signing in again.",
                _ => "Unable to sign in. Check your connection, browser runtime and file permissions."
            };
        }
        catch (Exception) { Message = "Unable to complete sign-in. Check your connection, browser runtime and file permissions."; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private Task CancelAsync() => StopAsync();
    public async Task StopAsync()
    {
        CheckCommand.Cancel();
        SignInCommand.Cancel();
        ConfirmSignOutCommand.Cancel();
        if (CheckCommand.ExecutionTask is { } check) await check;
        if (SignInCommand.ExecutionTask is { } signIn) await signIn;
        if (ConfirmSignOutCommand.ExecutionTask is { } signOut) await signOut;
        PendingSignOut = null;
    }

    internal static string ConfigPathFor(string source) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source))!, "rustplusjs-config.json");
}
