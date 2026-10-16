using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Avalonia.Features.Connections;
using RustPlusDesk.Core.Connections;
using RustPlusDesk.Core.Servers;
using RustPlusDesk.Avalonia.Features.Servers;
using RustPlusDesk.Avalonia.Services;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class ConnectionViewModelTests
{
    [TestMethod]
    public async Task ConnectAsync_VerifiedSession_ShowsStatusAndClock_DisconnectDisposesSession()
    {
        var session = new TestSession();
        var model = CreateModel(new TestFactory((_, _, _) => Task.FromResult<IServerSession>(session)));
        var running = model.ConnectCommand.ExecuteAsync(null);
        try
        {
            Assert.AreEqual(ConnectionPhase.Connected, model.Phase);
            Assert.AreEqual("Live server", model.ServerName);
            Assert.AreEqual("42 / 200", model.Population);
            Assert.AreEqual("3", model.Queue);
            Assert.AreEqual("14:30", model.ServerTime);
            Assert.IsFalse(model.ConnectCommand.CanExecute(null));
            Assert.IsTrue(model.DisconnectCommand.CanExecute(null));
        }
        finally { await model.StopAsync(); }
        await running;
        Assert.IsTrue(session.Disposed);
        Assert.AreEqual(ConnectionPhase.Disconnected, model.Phase);
        Assert.AreEqual("—", model.ServerTime);
        Assert.IsTrue(model.ConnectCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task ConnectAsync_CanceledWhileOpening_DoesNotShowFalseFailure()
    {
        var pending = new TaskCompletionSource<IServerSession>();
        var model = CreateModel(new TestFactory((_, _, ct) => pending.Task.WaitAsync(ct)));
        var running = model.ConnectCommand.ExecuteAsync(null);
        Assert.AreEqual(ConnectionPhase.Connecting, model.Phase);
        await model.DisconnectCommand.ExecuteAsync(null);
        await running;
        Assert.AreEqual(ConnectionPhase.Disconnected, model.Phase);
        Assert.AreEqual("Disconnected", model.StatusMessage);
    }

    [TestMethod]
    [DataRow(ConnectionFailure.RePairRequired, ConnectionPhase.RePairRequired)]
    [DataRow(ConnectionFailure.CompanionLoginRequired, ConnectionPhase.SignInRequired)]
    [DataRow(ConnectionFailure.InvalidCredentials, ConnectionPhase.Failed)]
    [DataRow(ConnectionFailure.ProfileUnavailable, ConnectionPhase.Failed)]
    [DataRow(ConnectionFailure.Unreachable, ConnectionPhase.Failed)]
    public async Task ConnectAsync_ClassifiedFailure_ShowsRecoveryStateWithoutAutomaticRetry(ConnectionFailure failure, ConnectionPhase expected)
    {
        int attempts = 0;
        var model = CreateModel(new TestFactory((_, _, _) =>
        {
            attempts++;
            return Task.FromException<IServerSession>(new ServerConnectionException(failure));
        }));
        await model.ConnectCommand.ExecuteAsync(null);
        Assert.AreEqual(expected, model.Phase);
        Assert.AreEqual(1, attempts);
        Assert.IsFalse(model.IsActive);
        Assert.IsTrue(model.ConnectCommand.CanExecute(null));
        Assert.AreEqual("—", model.Population);
    }

    [TestMethod]
    public async Task ConnectAsync_UnexpectedFailure_DoesNotExposeExceptionPayload()
    {
        var model = CreateModel(new TestFactory((_, _, _) => Task.FromException<IServerSession>(new IOException("private-token"))));
        await model.ConnectCommand.ExecuteAsync(null);
        Assert.AreEqual(ConnectionPhase.Failed, model.Phase);
        Assert.IsFalse(model.StatusMessage.Contains("private-token", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Select_WhileConnected_DisposesPreviousSessionAndClearsOldStatus()
    {
        var session = new TestSession();
        var model = CreateModel(new TestFactory((_, _, _) => Task.FromResult<IServerSession>(session)));
        var running = model.ConnectCommand.ExecuteAsync(null);
        model.Select("another.json", new ServerSummary("New server", "other.test", 28083) { ProfileKey = "other" });
        await running;
        Assert.IsTrue(session.Disposed);
        Assert.AreEqual("New server", model.ServerName);
        Assert.AreEqual("—", model.Population);
        Assert.AreEqual(ConnectionPhase.Disconnected, model.Phase);
    }

    [TestMethod]
    public async Task Select_WhileOpening_IgnoresLateSessionAndDisposesIt()
    {
        var opened = new TaskCompletionSource<IServerSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new TestSession();
        var model = CreateModel(new TestFactory((_, _, _) => opened.Task));
        var running = model.ConnectCommand.ExecuteAsync(null);
        model.Select("another.json", new ServerSummary("New server", "other.test", 28083) { ProfileKey = "other" });
        opened.SetResult(session);
        await running;
        Assert.IsTrue(session.Disposed);
        Assert.AreEqual("New server", model.ServerName);
        Assert.AreEqual("—", model.Population);
        Assert.AreEqual(ConnectionPhase.Disconnected, model.Phase);
    }

    [TestMethod]
    public async Task ShutdownAsync_ActiveConnection_DisposesSessionAndStopsPolling()
    {
        var session = new TestSession();
        var model = new ServersViewModel(new TestCatalog(), new TestPicker(), "profiles.json",
            new TestFactory((_, _, _) => Task.FromResult<IServerSession>(session)));
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual("Saved server", model.SelectedServer?.Name);
        var running = model.Connection.ConnectCommand.ExecuteAsync(null);
        await model.ShutdownAsync();
        await running;
        Assert.IsTrue(session.Disposed);
        Assert.IsFalse(model.Connection.IsActive);
    }

    [TestMethod]
    public async Task ConnectAsync_LostConnection_ReleasesOldSessionAndReconnects()
    {
        var first = new TestSession { ReadError = new ServerConnectionException(ConnectionFailure.Unreachable) };
        var recovered = new TestSession { InitialStatus = new ServerStatus("Recovered server", 99, 200, 0, 9) };
        int attempts = 0;
        var model = CreateModel(new TestFactory((_, _, _) => Task.FromResult<IServerSession>(++attempts == 1 ? first : recovered)));
        var running = model.ConnectCommand.ExecuteAsync(null);
        try
        {
            await WaitForPhaseAsync(model, ConnectionPhase.Reconnecting);
            Assert.IsTrue(first.Disposed);
            Assert.AreEqual("—", model.Population);
            await WaitForPhaseAsync(model, ConnectionPhase.Connected);
            Assert.AreEqual(2, attempts);
            Assert.AreEqual("Recovered server", model.ServerName);
            Assert.AreEqual("99 / 200", model.Population);
        }
        finally { await model.StopAsync(); }
        await running;
        Assert.IsTrue(recovered.Disposed);
    }

    [TestMethod]
    public async Task ConnectAsync_DeniedAfterLoss_StopsRecoveryAndRequestsRePair()
    {
        var first = new TestSession { ReadError = new ServerConnectionException(ConnectionFailure.Unreachable) };
        int attempts = 0;
        var model = CreateModel(new TestFactory((_, _, _) => ++attempts == 1
            ? Task.FromResult<IServerSession>(first)
            : Task.FromException<IServerSession>(new ServerConnectionException(ConnectionFailure.RePairRequired))));
        await model.ConnectCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(ConnectionPhase.RePairRequired, model.Phase);
        Assert.AreEqual(2, attempts);
        Assert.IsTrue(first.Disposed);
        Assert.IsTrue(model.ConnectCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task ConnectAsync_MissingTime_IsConnectedWithoutInventingClock()
    {
        var model = CreateModel(new TestFactory((_, _, _) => Task.FromResult<IServerSession>(
            new TestSession { InitialStatus = new ServerStatus("No clock", null, null, null) })));
        var running = model.ConnectCommand.ExecuteAsync(null);
        try
        {
            Assert.IsTrue(model.IsConnected);
            Assert.AreEqual("— / —", model.Population);
            Assert.AreEqual("—", model.ServerTime);
        }
        finally { await model.StopAsync(); }
        await running;
    }

    [TestMethod]
    public async Task ConnectAsync_ServerPaused_KeepsReportedTimeAndShowsPause()
    {
        var session = new TestSession { InitialStatus = new ServerStatus("Paused", 42, 200, 0, 14.5, 8, 20, 80, 0) };
        var model = CreateModel(new TestFactory((_, _, _) => Task.FromResult<IServerSession>(session)));
        var running = model.ConnectCommand.ExecuteAsync(null);
        try
        {
            Assert.AreEqual("14:30", model.ServerTime);
            Assert.AreEqual("Time paused", model.NextPhase);
        }
        finally { await model.StopAsync(); }
        await running;
    }

    [TestMethod]
    public async Task ConnectAsync_StatusRequestPending_ClockContinuesUpdating()
    {
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new TestSession
        {
            InitialStatus = new ServerStatus("Fast clock", 42, 200, 0, 14.5, 8, 20, 1, 1),
            Read = async ct =>
            {
                reading.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                throw new AssertFailedException("Canceled reads must not return status.");
            }
        };
        var model = CreateModel(new TestFactory((_, _, _) => Task.FromResult<IServerSession>(session)));
        var running = model.ConnectCommand.ExecuteAsync(null);
        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string previous = "";
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(model.ServerTime) && model.ServerTime != previous) updated.TrySetResult();
        }
        try
        {
            await reading.Task.WaitAsync(TimeSpan.FromSeconds(5));
            previous = model.ServerTime;
            model.PropertyChanged += Changed;
            await updated.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.IsTrue(model.IsConnected);
            Assert.AreNotEqual(previous, model.ServerTime);
        }
        finally
        {
            model.PropertyChanged -= Changed;
            await model.StopAsync();
        }
        await running;
        Assert.IsTrue(session.Disposed);
    }

    private static ConnectionViewModel CreateModel(IServerSessionFactory factory)
    {
        var model = new ConnectionViewModel(factory);
        model.Select("profiles.json", new ServerSummary("Saved server", "localhost", 28082) { ProfileKey = "fixture-key" });
        return model;
    }

    private static async Task WaitForPhaseAsync(ConnectionViewModel model, ConnectionPhase phase)
    {
        if (model.Phase == phase) return;
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(model.Phase) && model.Phase == phase) reached.TrySetResult();
        }
        model.PropertyChanged += Changed;
        try
        {
            if (model.Phase == phase) return;
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { model.PropertyChanged -= Changed; }
    }

    private sealed class TestFactory(Func<string, string, CancellationToken, Task<IServerSession>> connect) : IServerSessionFactory
    {
        public Task<IServerSession> ConnectAsync(string profilesPath, string profileKey, CancellationToken cancellationToken)
        {
            Assert.AreEqual("profiles.json", profilesPath);
            Assert.AreEqual("fixture-key", profileKey);
            return connect(profilesPath, profileKey, cancellationToken);
        }
    }

    private sealed class TestSession : IServerSession
    {
        public RustPlusDesk.Core.Devices.IDeviceClient Devices { get; init; } = new TestDeviceClient();
        public ServerStatus InitialStatus { get; init; } = new("Live server", 42, 200, 3, 14.5);
        public Exception? ReadError { get; init; }
        public Func<CancellationToken, Task<ServerStatus>>? Read { get; init; }
        public bool Disposed { get; private set; }
        public Task<ServerStatus> ReadStatusAsync(CancellationToken cancellationToken)
            => Read?.Invoke(cancellationToken) ?? (ReadError is null ? Task.FromResult(InitialStatus) : Task.FromException<ServerStatus>(ReadError));
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class TestCatalog : IServerCatalog
    {
        public Task<IReadOnlyList<ServerSummary>> LoadAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ServerSummary>>([new("Saved server", "localhost", 28082) { ProfileKey = "fixture-key" }]);
    }

    private sealed class TestPicker : IProfileFilePicker
    {
        public Task<string?> PickAsync() => Task.FromResult<string?>(null);
    }
}
