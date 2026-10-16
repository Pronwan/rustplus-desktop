using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Avalonia.Features.Servers;
using RustPlusDesk.Avalonia.Services;
using RustPlusDesk.Core.Servers;
using RustPlusDesk.Core.Connections;
using RustPlusDesk.Infrastructure.Profiles;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class ServersViewModelTests
{
    [TestMethod]
    public async Task Edit_SavesAndReloadsNewIdentity_ThenReconnectsSelectedServer()
    {
        string directory = Path.Combine(Path.GetTempPath(), "rustplus-edit-flow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "profiles.json");
        await File.WriteAllTextAsync(path, "[{\"Name\":\"Saved\",\"Host\":\"old.test\",\"Port\":28082}]");
        var services = new ServerEditorViewModelTests.EditorServices();
        var factory = new EditSessionFactory();
        var model = new ServersViewModel(new LegacyServerCatalog(), new TestPicker(), path, factory, new LegacyServerProfileEditor(), services);
        try
        {
            await model.RefreshCommand.ExecuteAsync(null);
            string oldKey = model.SelectedServer!.ProfileKey;
            var editing = model.EditCommand.ExecuteAsync(null);
            Assert.IsNotNull(model.Editor);
            Assert.IsFalse(model.RefreshCommand.CanExecute(null));
            Assert.IsFalse(model.OpenFileCommand.CanExecute(null));
            model.Editor.Host = "new.test";
            model.Editor.Name = "Renamed";
            await model.Editor.SaveCommand.ExecuteAsync(null);
            await editing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsNull(model.Editor);
            Assert.AreEqual("new.test", model.SelectedServer!.Host);
            Assert.AreEqual("Renamed", model.SelectedServer.Name);
            Assert.AreNotEqual(oldKey, model.SelectedServer.ProfileKey);
            Assert.AreEqual(model.SelectedServer.ProfileKey, factory.Key);
            Assert.IsTrue(model.Connection.IsConnected);
            Assert.AreEqual(1, Directory.GetFiles(directory, "*.bak").Length);
        }
        finally { await model.ShutdownAsync(); Directory.Delete(directory, recursive: true); }
        Assert.IsTrue(factory.Disposed);
    }

    [TestMethod]
    public async Task Shutdown_ClosesEditorAndWaitsForPendingConnectionTest()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool stopped = false;
        var services = new ServerEditorViewModelTests.EditorServices { Probe = async ct =>
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { stopped = true; }
            return new(0, 0);
        }};
        var model = new ServersViewModel(new TestCatalog(), new TestPicker(), "original.json", new UnusedSessionFactory(), services, services);
        await model.RefreshCommand.ExecuteAsync(null);
        var editing = model.EditCommand.ExecuteAsync(null);
        var testing = model.Editor!.TestCommand.ExecuteAsync(null);
        await started.Task;
        await model.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(editing, testing);
        Assert.IsTrue(stopped);
        Assert.IsNull(model.Editor);
        Assert.IsFalse(model.IsBusy);
        Assert.AreEqual(0, services.Saves);
    }

    [TestMethod]
    public async Task RefreshAsync_LoadsServersAndRaisesDerivedProperties()
    {
        var catalog = new TestCatalog();
        var model = new ServersViewModel(catalog, new TestPicker(), "original.json", new UnusedSessionFactory());
        var changed = new List<string?>();
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await model.RefreshCommand.ExecuteAsync(null);

        Assert.IsTrue(model.HasServers);
        Assert.IsFalse(model.ShowStatus);
        Assert.IsFalse(model.IsBusy);
        Assert.AreEqual("original.json", catalog.LastPath);
        CollectionAssert.Contains(changed, nameof(model.HasServers));
        CollectionAssert.Contains(changed, nameof(model.ShowStatus));
    }

    [TestMethod]
    public async Task OpenFileAsync_CanceledPicker_KeepsCurrentServersAndSource()
    {
        var model = new ServersViewModel(new TestCatalog(), new TestPicker(), "original.json", new UnusedSessionFactory());
        await model.RefreshCommand.ExecuteAsync(null);
        var previous = model.Servers;

        await model.OpenFileCommand.ExecuteAsync(null);

        Assert.AreSame(previous, model.Servers);
        Assert.AreEqual("original.json", model.SourcePath);
        Assert.IsFalse(model.IsBusy);
    }

    [TestMethod]
    public async Task OpenFileAsync_SelectedFile_RefreshUsesSelectedSource()
    {
        var catalog = new TestCatalog();
        var model = new ServersViewModel(catalog, new TestPicker("chosen.json"), "original.json", new UnusedSessionFactory());
        await model.OpenFileCommand.ExecuteAsync(null);
        Assert.AreEqual("chosen.json", model.SourcePath);
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual("chosen.json", catalog.LastPath);
    }

    [TestMethod]
    public async Task RefreshAsync_BadFile_ClearsStaleRowsAndHidesExceptionContents()
    {
        var catalog = new TestCatalog();
        var model = new ServersViewModel(catalog, new TestPicker(), "original.json", new UnusedSessionFactory());
        await model.RefreshCommand.ExecuteAsync(null);
        catalog.Error = new JsonException("private-token-from-input");

        await model.RefreshCommand.ExecuteAsync(null);

        Assert.IsFalse(model.HasServers);
        Assert.IsTrue(model.ShowStatus);
        Assert.AreEqual("Unable to read profiles", model.StatusTitle);
        Assert.IsFalse(model.StatusMessage.Contains("private-token", StringComparison.Ordinal));
        Assert.IsFalse(model.IsBusy);
    }

    [TestMethod]
    public async Task RefreshAsync_WhileLoading_DisablesBothCommandsAndRejectsDuplicateLoad()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<ServerSummary>>();
        var catalog = new TestCatalog { Pending = completion.Task };
        var model = new ServersViewModel(catalog, new TestPicker(), "original.json", new UnusedSessionFactory());
        var loading = model.RefreshCommand.ExecuteAsync(null);

        Assert.IsTrue(model.IsBusy);
        Assert.IsFalse(model.RefreshCommand.CanExecute(null));
        Assert.IsFalse(model.OpenFileCommand.CanExecute(null));
        await model.OpenFileCommand.ExecuteAsync(null);
        Assert.AreEqual(1, catalog.Loads);

        completion.SetResult([]);
        await loading;
        Assert.IsTrue(model.RefreshCommand.CanExecute(null));
        Assert.IsTrue(model.OpenFileCommand.CanExecute(null));
        Assert.IsTrue(model.ShowStatus);
    }

    [TestMethod]
    public async Task RefreshAsync_Canceled_ReleasesBusyState()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<ServerSummary>>();
        var catalog = new TestCatalog { Pending = completion.Task };
        var model = new ServersViewModel(catalog, new TestPicker(), "original.json", new UnusedSessionFactory());
        var loading = model.RefreshCommand.ExecuteAsync(null);
        model.RefreshCommand.Cancel();
        completion.SetCanceled(catalog.LastCancellationToken);
        await loading;
        Assert.IsFalse(model.IsBusy);
        Assert.IsTrue(model.RefreshCommand.CanExecute(null));
    }

    private sealed class TestPicker(string? path = null) : IProfileFilePicker
    {
        public Task<string?> PickAsync() => Task.FromResult(path);
    }

    private sealed class EditSessionFactory : IServerSessionFactory, IServerSession
    {
        public string? Key { get; private set; }
        public bool Disposed { get; private set; }
        public RustPlusDesk.Core.Devices.IDeviceClient Devices { get; } = new TestDeviceClient();
        public ServerStatus InitialStatus { get; } = new("Renamed", 1, 100, 0);
        public Task<IServerSession> ConnectAsync(string profilesPath, string profileKey, CancellationToken cancellationToken)
        { Key = profileKey; return Task.FromResult<IServerSession>(this); }
        public Task<ServerStatus> ReadStatusAsync(CancellationToken cancellationToken) => Task.FromResult(InitialStatus);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class TestCatalog : IServerCatalog
    {
        public string? LastPath { get; private set; }
        public int Loads { get; private set; }
        public Exception? Error { get; set; }
        public Task<IReadOnlyList<ServerSummary>>? Pending { get; set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<IReadOnlyList<ServerSummary>> LoadAsync(string path, CancellationToken cancellationToken = default)
        {
            LastPath = path;
            LastCancellationToken = cancellationToken;
            Loads++;
            if (Error is not null) return Task.FromException<IReadOnlyList<ServerSummary>>(Error);
            return Pending ?? Task.FromResult<IReadOnlyList<ServerSummary>>([new("Test server", "localhost", 28082)]);
        }
    }
}
