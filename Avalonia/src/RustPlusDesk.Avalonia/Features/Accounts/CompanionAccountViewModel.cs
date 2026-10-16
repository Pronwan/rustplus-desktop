using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RustPlusDesk.Core.Accounts;

namespace RustPlusDesk.Avalonia.Features.Accounts;

public sealed partial class CompanionAccountViewModel(ICompanionAccountService service, string profilesPath, Func<Task> beforeSignIn) : ObservableObject
{
    private string configPath = ConfigPathFor(profilesPath);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Identity))]
    private CompanionAccount account = new(AccountState.Missing);
    [ObservableProperty] private string message = "Check your Rust+ sign-in status or sign in with Steam.";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private bool isBusy;
    public string Identity => string.IsNullOrEmpty(Account.SteamId) ? "Rust+ Companion" : $"Steam ID: {Account.SteamId}";
    private bool CanRun => !IsBusy;

    public async Task SetSourceAsync(string source)
    {
        await StopAsync();
        configPath = ConfigPathFor(source);
        Account = new(AccountState.Missing);
        Message = "Check your Rust+ sign-in status or sign in with Steam.";
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task CheckAsync(CancellationToken cancellationToken) => RunAsync(false, cancellationToken);
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task SignInAsync(CancellationToken cancellationToken) => RunAsync(true, cancellationToken);

    private async Task RunAsync(bool signIn, CancellationToken cancellationToken)
    {
        if (IsBusy) return;
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
        if (CheckCommand.ExecutionTask is { } check) await check;
        if (SignInCommand.ExecutionTask is { } signIn) await signIn;
    }

    private static string ConfigPathFor(string source) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source))!, "rustplusjs-config.json");
}
