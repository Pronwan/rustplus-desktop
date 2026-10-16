using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Avalonia.Features.Servers;
using RustPlusDesk.Avalonia.Services;
using RustPlusDesk.Core.Accounts;
using RustPlusDesk.Core.Servers;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class CompanionStartupTests
{
    [TestMethod]
    [DataRow(AccountState.Active, true)]
    [DataRow(AccountState.Offline, true)]
    [DataRow(AccountState.Missing, false)]
    [DataRow(AccountState.Expired, false)]
    [DataRow(AccountState.Rejected, false)]
    public async Task Startup_ChecksSavedAccount_AndOnlyStartsExistingUsableCredentials(AccountState state, bool starts)
    {
        var source = new CompanionListenerViewModelTests.Source();
        var accounts = new CompanionAccountViewModelTests.Service { Check = _ => Task.FromResult(new CompanionAccount(state)) };
        var model = new ServersViewModel(new Catalog(), new Picker(), "profiles.json", new UnusedSessionFactory(), accountService: accounts, companionListener: source);
        try
        {
            await model.InitializeCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(4));
            Assert.AreEqual(starts ? 1 : 0, source.Starts);
            Assert.AreEqual(0, accounts.SignIns, "Startup must never open Steam login.");
            Assert.AreEqual(!starts, model.RequiresSignIn);
            Assert.IsFalse(model.IsAccountOpen);
            await model.InitializeCommand.ExecuteAsync(null);
            Assert.AreEqual(starts ? 1 : 0, source.Starts);
        }
        finally { await model.ShutdownAsync(); }
    }

    [TestMethod]
    public async Task Shutdown_CancelsAndAwaitsStartupCheck_WithoutLateListenerStart()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool ended = false;
        var accounts = new CompanionAccountViewModelTests.Service
        {
            Check = async ct =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { ended = true; }
            return new(AccountState.Active);
        }
        };
        var source = new CompanionListenerViewModelTests.Source();
        var model = new ServersViewModel(new Catalog(), new Picker(), "profiles.json", new UnusedSessionFactory(), accountService: accounts, companionListener: source);
        Task startup = model.InitializeCommand.ExecuteAsync(null);
        await entered.Task;
        await model.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(4));
        await startup;
        Assert.IsTrue(ended);
        Assert.AreEqual(0, source.Starts);
    }

    [TestMethod]
    public async Task SourceChange_DuringStartupCheck_CannotStartListenerForOldProfiles()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accounts = new CompanionAccountViewModelTests.Service
        {
            Check = async ct =>
        { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return new(AccountState.Active); }
        };
        var source = new CompanionListenerViewModelTests.Source();
        var model = new ServersViewModel(new Catalog(), new Picker { Path = "other/profiles.json" }, "profiles.json", new UnusedSessionFactory(), accountService: accounts, companionListener: source);
        Task startup = model.InitializeCommand.ExecuteAsync(null);
        await entered.Task;
        await model.OpenFileCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(4));
        await startup;
        Assert.AreEqual("other/profiles.json", model.SourcePath);
        Assert.AreEqual(0, source.Starts);
        await model.ShutdownAsync();
    }

    private sealed class Catalog : IServerCatalog
    { public Task<IReadOnlyList<ServerSummary>> LoadAsync(string path, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ServerSummary>>([]); }
    private sealed class Picker : IProfileFilePicker
    { public string? Path { get; init; } public Task<string?> PickAsync() => Task.FromResult(Path); }
}
