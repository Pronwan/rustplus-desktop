using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Avalonia.Features.Accounts;
using RustPlusDesk.Avalonia.Features.Servers;
using RustPlusDesk.Avalonia.Services;
using RustPlusDesk.Core.Accounts;
using RustPlusDesk.Core.Servers;
using RustPlusDesk.Core.Connections;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class CompanionListenerViewModelTests
{
    [TestMethod]
    public async Task SourceChange_AndStop_AwaitCleanupAndClearPreviousAccountNotifications()
    {
        var source = new Source();
        int pairings = 0;
        var model = new CompanionListenerViewModel(source, "old/profiles.json", _ => { pairings++; return Task.CompletedTask; });
        Task running = model.StartCommand.ExecuteAsync(null);
        await source.Started.Task;
        Assert.IsTrue(model.IsRunning);
        Assert.IsFalse(model.StartCommand.CanExecute(null));
        Assert.AreEqual(1, source.Starts);
        source.Send(new(CompanionUpdateKind.Pairing, "Fixture", "Paired"));
        await Eventually(() => pairings == 1);
        await model.SetSourceAsync("new/profiles.json").WaitAsync(TimeSpan.FromSeconds(4));
        await running;
        Assert.IsTrue(source.Disposed);
        Assert.IsFalse(model.IsRunning);
        Assert.AreEqual(0, model.Notifications.Count);
        running = model.StartCommand.ExecuteAsync(null);
        await Eventually(() => source.Starts == 2);
        Assert.AreEqual("new/profiles.json", source.Path);
        await model.StopListeningAsync().WaitAsync(TimeSpan.FromSeconds(4));
        await running;
    }

    [TestMethod]
    public async Task Notifications_AreBounded_AndErrorsDoNotExposePrivateInputs()
    {
        var source = new Source();
        var model = new CompanionListenerViewModel(source, "profiles.json", _ => Task.CompletedTask);
        Task running = model.StartCommand.ExecuteAsync(null);
        await source.Started.Task;
        for (int i = 0; i < 105; i++) source.Send(new(CompanionUpdateKind.Alarm, "Alarm " + i, "Fixture"));
        await Eventually(() => model.Notifications.Count == 100 && model.Notifications[0].Title == "Alarm 104");
        Assert.AreEqual("Alarm 5", model.Notifications[^1].Title);
        source.Fail(new IOException("private token and request URL"));
        await running.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.IsFalse(model.IsRunning);
        Assert.IsFalse(model.Status.Contains("private", StringComparison.Ordinal));
        Assert.IsTrue(model.StartCommand.CanExecute(null));
    }

    [TestMethod]
    [DataRow(CompanionListenerFailure.MissingCredentials)]
    [DataRow(CompanionListenerFailure.InvalidCredentials)]
    public async Task InvalidPushCredentials_RequestSignIn_WithoutOpeningBrowser(CompanionListenerFailure failure)
    {
        var source = new Source();
        var model = new CompanionListenerViewModel(source, "profiles.json", _ => Task.CompletedTask);
        Task running = model.StartCommand.ExecuteAsync(null);
        await source.Started.Task;
        source.Fail(new CompanionListenerException(failure));
        await running.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.IsTrue(model.RequiresSignIn);
        Assert.IsFalse(model.IsRunning);
        await model.SetSourceAsync("other/profiles.json");
        Assert.IsFalse(model.RequiresSignIn);
    }

    [TestMethod]
    public async Task AccountClose_KeepsListening_AndRefreshesSavedPairings_ThenSignInStopsListener()
    {
        var source = new Source();
        var catalog = new Catalog();
        var account = new CompanionAccountViewModelTests.Service { BeforeSignIn = () => Assert.IsTrue(source.Disposed) };
        var model = new ServersViewModel(catalog, new Picker(), "profiles.json", new UnusedSessionFactory(), accountService: account, companionListener: source);
        await model.RefreshCommand.ExecuteAsync(null);
        model.OpenAccountCommand.Execute(null);
        Assert.IsNotNull(model.Listener);
        Task running = model.Listener.StartCommand.ExecuteAsync(null);
        await source.Started.Task;
        source.Send(new(CompanionUpdateKind.Pairing, "Fixture", "Server paired.") { ProfileKey = "paired" });
        await Eventually(() => model.Listener.Notifications.Count == 1);
        Assert.AreEqual(1, catalog.Loads, "A modal must defer background refresh until it closes.");
        await model.CloseAccountCommand.ExecuteAsync(null);
        await Eventually(() => catalog.Loads == 2);
        Assert.IsTrue(model.Listener.IsRunning);
        model.OpenAccountCommand.Execute(null);
        await model.Account!.SignInCommand.ExecuteAsync(null);
        await running.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.IsTrue(model.Listener.IsRunning, "Successful sign-in starts a fresh listener using its new credentials.");
        Assert.AreEqual(2, source.Starts);
        Assert.AreEqual(AccountState.Active, model.Account.Account.State);
        await model.ShutdownAsync();
    }

    [TestMethod]
    public async Task OpeningAnotherProfilesFile_AndShutdown_AwaitListenerCleanup()
    {
        var source = new Source();
        var picker = new Picker { Next = "other/profiles.json" };
        var model = new ServersViewModel(new Catalog(), picker, "profiles.json", new UnusedSessionFactory(),
            accountService: new CompanionAccountViewModelTests.Service(), companionListener: source);
        model.OpenAccountCommand.Execute(null);
        Task running = model.Listener!.StartCommand.ExecuteAsync(null);
        await source.Started.Task;
        await model.CloseAccountCommand.ExecuteAsync(null);
        await model.OpenFileCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(4));
        await running;
        Assert.IsTrue(source.Disposed);
        Assert.AreEqual("other/profiles.json", model.SourcePath);
        running = model.Listener.StartCommand.ExecuteAsync(null);
        await Eventually(() => source.Starts == 2);
        await model.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(4));
        await running;
        Assert.IsTrue(source.Disposed);
    }

    internal static async Task Eventually(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    [TestMethod]
    public async Task ServerIdRePair_ReloadsChangedAddress_ReselectsAndReconnectsWithoutBlockingPushes()
    {
        var source = new Source();
        var catalog = new Catalog { Servers = [new("Other", "other.test", 28082) { ProfileKey = "other" }, new("Paired", "old.test", 28082) { ProfileKey = "old" }] };
        var connections = new Sessions();
        var model = new ServersViewModel(catalog, new Picker(), "profiles.json", connections,
            accountService: new CompanionAccountViewModelTests.Service(), companionListener: source);
        try
        {
            await model.RefreshCommand.ExecuteAsync(null);
            model.SelectedServer = model.Servers[1];
            _ = model.Connection.ConnectCommand.ExecuteAsync(null);
            await Eventually(() => model.Connection.IsConnected);
            model.OpenAccountCommand.Execute(null);
            _ = model.Listener!.StartCommand.ExecuteAsync(null);
            await source.Started.Task;
            catalog.Servers = [catalog.Servers[0], new("Paired", "new.test", 29082) { ProfileKey = "new" }];
            source.Send(new(CompanionUpdateKind.Pairing, "Paired", "Server paired.") { ReplacesProfileKey = "old", ProfileKey = "new" });
            await Eventually(() => model.Listener.Notifications.Count == 1);
            await model.CloseAccountCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(4));
            await Eventually(() => connections.Starts == 2 && model.Connection.IsConnected);
            Assert.AreEqual("new", model.SelectedServer!.ProfileKey);
            Assert.IsTrue(connections.Opened[0].Disposed);
            source.Send(new(CompanionUpdateKind.Chat, "Teammate", "Still receiving"));
            await Eventually(() => model.Listener.Notifications.Count == 2);
        }
        finally { await model.ShutdownAsync(); }
    }

    internal sealed class Source : ICompanionListener
    {
        private Channel<CompanionUpdate> updates = Channel.CreateUnbounded<CompanionUpdate>();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Starts { get; private set; }
        public bool Disposed { get; private set; }
        public string? Path { get; private set; }
        public void Send(CompanionUpdate update) => updates.Writer.TryWrite(update);
        public void Fail(Exception error) => updates.Writer.TryComplete(error);
        public async IAsyncEnumerable<CompanionUpdate> ListenAsync(string profilesPath, [EnumeratorCancellation] CancellationToken ct)
        {
            updates = Channel.CreateUnbounded<CompanionUpdate>();
            Starts++;
            Disposed = false;
            Path = profilesPath;
            Started.TrySetResult();
            try { await foreach (var update in updates.Reader.ReadAllAsync(ct)) yield return update; }
            finally { Disposed = true; }
        }
    }

    private sealed class Catalog : IServerCatalog
    {
        public int Loads { get; private set; }
        public IReadOnlyList<ServerSummary> Servers { get; set; } = [new("Fixture", "fixture.test", 28082) { ProfileKey = "paired" }];
        public Task<IReadOnlyList<ServerSummary>> LoadAsync(string path, CancellationToken cancellationToken = default)
        { Loads++; return Task.FromResult(Servers); }
    }
    private sealed class Picker : IProfileFilePicker
    {
        public string? Next { get; init; }
        public Task<string?> PickAsync() => Task.FromResult(Next);
    }

    private sealed class Sessions : IServerSessionFactory
    {
        public int Starts => Opened.Count;
        public List<Session> Opened { get; } = [];
        public Task<IServerSession> ConnectAsync(string profilesPath, string profileKey, CancellationToken cancellationToken)
        { var session = new Session(); Opened.Add(session); return Task.FromResult<IServerSession>(session); }
    }
    private sealed class Session : IServerSession
    {
        public RustPlusDesk.Core.Devices.IDeviceClient Devices { get; } = new TestDeviceClient();
        public ServerStatus InitialStatus { get; } = new("Paired", 1, 100, 0);
        public Task Disconnected { get; } = new TaskCompletionSource().Task;
        public bool Disposed { get; private set; }
        public Task<ServerStatus> ReadStatusAsync(CancellationToken cancellationToken) => Task.FromResult(InitialStatus);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
