using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Avalonia.Features.Accounts;
using RustPlusDesk.Core.Accounts;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class CompanionAccountViewModelTests
{
    [TestMethod]
    public async Task SignIn_StopsPreviousConnection_AndUsesConfigBesideSelectedProfiles()
    {
        bool stopped = false;
        var service = new Service { BeforeSignIn = () => Assert.IsTrue(stopped) };
        string profile = Path.Combine(Path.GetTempPath(), "copied-rustplus", "profiles.json");
        var model = new CompanionAccountViewModel(service, profile, () => { stopped = true; return Task.CompletedTask; });
        await model.SignInCommand.ExecuteAsync(null);
        Assert.AreEqual(Path.Combine(Path.GetDirectoryName(profile)!, "rustplusjs-config.json"), service.Path);
        Assert.AreEqual(AccountState.Active, model.Account.State);
        Assert.AreEqual("Signed in to Rust+.", model.Message);
        StringAssert.Contains(model.Identity, "76561198000000001");
    }

    [TestMethod]
    public async Task SourceChange_CancelsAndAwaitsOldCheck_ThenClearsOldIdentity()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool stopped = false;
        var service = new Service
        {
            Check = async ct =>
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { stopped = true; }
            return new(AccountState.Active, "76561198000000001");
        }
        };
        var model = new CompanionAccountViewModel(service, "old/profiles.json", () => Task.CompletedTask);
        var check = model.CheckCommand.ExecuteAsync(null);
        await started.Task;
        Assert.IsFalse(model.SignInCommand.CanExecute(null));
        await model.SignInCommand.ExecuteAsync(null);
        Assert.AreEqual(0, service.SignIns);
        await model.SetSourceAsync("other/profiles.json").WaitAsync(TimeSpan.FromSeconds(5));
        await check;
        Assert.IsTrue(stopped);
        Assert.IsFalse(model.IsBusy);
        Assert.AreEqual("Rust+ Companion", model.Identity);
        Assert.AreEqual(AccountState.Missing, model.Account.State);
    }

    [TestMethod]
    public async Task FailedSignIn_DoesNotExposeCredentials_AndCanRetry()
    {
        var service = new Service { Error = new IOException("private auth token and request URL") };
        var model = new CompanionAccountViewModel(service, "profiles.json", () => Task.CompletedTask);
        await model.SignInCommand.ExecuteAsync(null);
        Assert.IsFalse(model.Message.Contains("private", StringComparison.Ordinal));
        Assert.IsFalse(model.IsBusy);
        service.Error = new CompanionAccountException(AccountFailure.ChangedConfiguration);
        await model.SignInCommand.ExecuteAsync(null);
        StringAssert.Contains(model.Message, "changed in another application");
        service.Error = null;
        await model.SignInCommand.ExecuteAsync(null);
        Assert.AreEqual(AccountState.Active, model.Account.State);
    }

    [TestMethod]
    public async Task BrowserClosedByUser_ReportsCancellationWithoutClaimingSignInSuccess()
    {
        var service = new Service { Error = new OperationCanceledException() };
        var model = new CompanionAccountViewModel(service, "profiles.json", () => Task.CompletedTask);
        await model.SignInCommand.ExecuteAsync(null);
        Assert.AreEqual(AccountState.Missing, model.Account.State);
        Assert.AreEqual("Sign-in operation canceled.", model.Message);
    }

    internal sealed class Service : ICompanionAccountService
    {
        public string? Path { get; private set; }
        public int SignIns { get; private set; }
        public int SignOuts { get; private set; }
        public CompanionSignOutScope? Scope { get; private set; }
        public CompanionSignOutResult SignOutResult { get; set; }
        public Exception? Error { get; set; }
        public Action? BeforeSignIn { get; init; }
        public Func<CancellationToken, Task<CompanionAccount>>? Check { get; init; }
        public Task<CompanionAccount> CheckAsync(string configPath, CancellationToken cancellationToken)
        { Path = configPath; return Check?.Invoke(cancellationToken) ?? Task.FromResult(new CompanionAccount(AccountState.Active, "76561198000000001")); }
        public Task<CompanionAccount> SignInAsync(string configPath, CancellationToken cancellationToken)
        {
            Path = configPath;
            SignIns++;
            BeforeSignIn?.Invoke();
            return Error is { } error ? Task.FromException<CompanionAccount>(error)
                : Task.FromResult(new CompanionAccount(AccountState.Active, "76561198000000001"));
        }
        public Task<CompanionSignOutResult> SignOutAsync(string configPath, CompanionSignOutScope scope, CancellationToken cancellationToken)
        {
            Path = configPath; Scope = scope; SignOuts++; BeforeSignIn?.Invoke();
            return Error is { } error ? Task.FromException<CompanionSignOutResult>(error) : Task.FromResult(SignOutResult);
        }
        public Task<CompanionAccount> RenewPushAsync(string configPath, CancellationToken cancellationToken)
            => Task.FromResult(new CompanionAccount(AccountState.Active, "76561198000000001"));
    }

    [TestMethod]
    public async Task SignOut_RequiresExplicitConfirmation_AndStopsOwnedSessionsBeforeRemoteCall()
    {
        bool stopped = false;
        var service = new Service { BeforeSignIn = () => Assert.IsTrue(stopped) };
        var model = new CompanionAccountViewModel(service, "profiles.json", () => { stopped = true; return Task.CompletedTask; });
        await model.ConfirmSignOutCommand.ExecuteAsync(null);
        Assert.AreEqual(0, service.SignOuts);
        model.RequestSignOutCommand.Execute(CompanionSignOutScope.AllDevices);
        Assert.IsTrue(model.IsConfirmingSignOut);
        Assert.IsFalse(model.CheckCommand.CanExecute(null));
        model.KeepSignedInCommand.Execute(null);
        Assert.IsFalse(model.IsConfirmingSignOut);
        Assert.AreEqual(0, service.SignOuts);
        model.RequestSignOutCommand.Execute(CompanionSignOutScope.CurrentDevice);
        await model.ConfirmSignOutCommand.ExecuteAsync(null);
        Assert.AreEqual(CompanionSignOutScope.CurrentDevice, service.Scope);
        Assert.AreEqual(AccountState.Missing, model.Account.State);
        Assert.IsFalse(model.IsConfirmingSignOut);
        Assert.IsTrue(model.CheckCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task SignOut_PartialCleanupAndNetworkFailure_HaveTruthfulSafeMessages()
    {
        var service = new Service { SignOutResult = CompanionSignOutResult.RemoteCompletedLocalCleanupFailed };
        var model = new CompanionAccountViewModel(service, "profiles.json", () => Task.CompletedTask);
        model.RequestSignOutCommand.Execute(CompanionSignOutScope.AllDevices);
        await model.ConfirmSignOutCommand.ExecuteAsync(null);
        Assert.AreEqual(AccountState.Unavailable, model.Account.State);
        StringAssert.Contains(model.Message, "local file could not be updated");
        service.Error = new IOException("private token and endpoint URL");
        model.RequestSignOutCommand.Execute(CompanionSignOutScope.CurrentDevice);
        await model.ConfirmSignOutCommand.ExecuteAsync(null);
        Assert.IsFalse(model.Message.Contains("private", StringComparison.Ordinal));
        Assert.IsFalse(model.IsBusy);
        Assert.IsFalse(model.IsConfirmingSignOut);
    }
}
