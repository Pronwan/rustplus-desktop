using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Avalonia.Features.Accounts;
using RustPlusDesk.Core.Accounts;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class CompanionListenerHealthViewModelTests
{
    [TestMethod]
    public async Task FirstListening_ChecksOnce_AndInconclusiveDoesNotRenew()
    {
        var source = new CompanionListenerViewModelTests.Source();
        var health = new Health([new(PushHealthOutcome.Inconclusive, "Connection unavailable")]);
        int renewals = 0;
        var model = Model(source, health, _ => { renewals++; return Task.CompletedTask; });
        Task running = model.StartCommand.ExecuteAsync(null);
        await source.Started.Task;
        source.Send(Listening);
        await CompanionListenerViewModelTests.Eventually(() => health.Checks == 1 && !model.IsChecking);
        Assert.AreEqual(0, renewals);
        Assert.AreEqual("Connection unavailable", model.HealthMessage);
        source.Send(new(CompanionUpdateKind.Reconnecting, "Rust+", "Reconnecting"));
        source.Send(Listening);
        await CompanionListenerViewModelTests.Eventually(() => model.IsListening);
        Assert.AreEqual(1, health.Checks);
        await model.StopListeningAsync();
        await running;
    }

    [TestMethod]
    public async Task FailedDelivery_RenewsOnce_RestartsListener_AndChecksAgainWithoutLooping()
    {
        var source = new CompanionListenerViewModelTests.Source();
        var health = new Health([new(PushHealthOutcome.TokenRejected, "Rejected"), new(PushHealthOutcome.NotDelivered, "Not delivered")]);
        int renewals = 0;
        var model = Model(source, health, _ => { Assert.IsTrue(source.Disposed); renewals++; return Task.CompletedTask; });
        _ = model.StartCommand.ExecuteAsync(null);
        await source.Started.Task;
        source.Send(Listening);
        await CompanionListenerViewModelTests.Eventually(() => source.Starts == 2);
        source.Send(Listening);
        await CompanionListenerViewModelTests.Eventually(() => health.Checks == 2 && !model.IsChecking);
        Assert.AreEqual(1, renewals);
        Assert.IsTrue(model.IsRunning);
        StringAssert.Contains(model.HealthMessage, "still unverified");
        await model.StopListeningAsync();
    }

    [TestMethod]
    public async Task SourceChange_CancelsAndAwaitsHealthCheck_AndCannotRestartOldListener()
    {
        var source = new CompanionListenerViewModelTests.Source();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool ended = false;
        var health = new Health([])
        {
            Run = async ct =>
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { ended = true; }
            return new(PushHealthOutcome.NotDelivered, "Not delivered");
        }
        };
        int renewals = 0;
        var model = Model(source, health, _ => { renewals++; return Task.CompletedTask; });
        _ = model.StartCommand.ExecuteAsync(null);
        await source.Started.Task;
        source.Send(Listening);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(4));
        await model.SetSourceAsync("new/profiles.json").WaitAsync(TimeSpan.FromSeconds(4));
        Assert.IsTrue(ended);
        Assert.AreEqual(0, renewals);
        Assert.AreEqual(1, source.Starts);
        Assert.IsFalse(model.IsRunning);
        Assert.IsFalse(model.IsChecking);
        Assert.AreEqual("Push delivery has not been checked.", model.HealthMessage);
    }

    [TestMethod]
    public async Task StopDuringRenewal_AwaitsItAndDoesNotRestartTransport()
    {
        var source = new CompanionListenerViewModelTests.Source();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool ended = false;
        var model = Model(source, new Health([]), async ct =>
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { ended = true; }
        });
        Task repair = model.RenewCommand.ExecuteAsync(null);
        await started.Task;
        Assert.IsFalse(model.StartCommand.CanExecute(null));
        Assert.IsTrue(model.StopCommand.CanExecute(null));
        await model.StopListeningAsync().WaitAsync(TimeSpan.FromSeconds(4));
        await repair;
        Assert.IsTrue(ended);
        Assert.AreEqual(0, source.Starts);
        Assert.IsFalse(model.IsChecking);
    }

    [TestMethod]
    public async Task RejectedRenewal_RequestsUserSignInWithoutOpeningBrowserOrExposingErrors()
    {
        var source = new CompanionListenerViewModelTests.Source();
        var model = Model(source, new Health([]), _ => throw new CompanionAccountException(AccountFailure.Rejected));
        await model.RenewCommand.ExecuteAsync(null);
        StringAssert.Contains(model.HealthMessage, "Sign in again with Steam");
        Assert.IsFalse(model.IsRunning);
        Assert.IsFalse(model.IsChecking);
    }

    private static CompanionListenerViewModel Model(CompanionListenerViewModelTests.Source source, Health health, Func<CancellationToken, Task> renew)
        => new(source, "profiles.json", _ => Task.CompletedTask, health, async (_, ct) => { await renew(ct); return new(AccountState.Active); },
            async (_, ct) => { ct.ThrowIfCancellationRequested(); await Task.Yield(); });

    private static CompanionUpdate Listening => new(CompanionUpdateKind.Listening, "Rust+", "Listening");

    private sealed class Health(IEnumerable<PushHealthReport> reports) : ICompanionPushHealth
    {
        private readonly ConcurrentQueue<PushHealthReport> reports = new(reports);
        public int Checks { get; private set; }
        public Func<CancellationToken, Task<PushHealthReport>>? Run { get; init; }
        public Task<PushHealthReport> CheckAsync(string path, CancellationToken ct)
        { Checks++; return Run?.Invoke(ct) ?? Task.FromResult(reports.TryDequeue(out var result) ? result : new(PushHealthOutcome.Inconclusive, "No result")); }
    }
}
