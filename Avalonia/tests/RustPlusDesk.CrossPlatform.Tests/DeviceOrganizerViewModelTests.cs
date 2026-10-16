using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Avalonia.Features.Devices;
using RustPlusDesk.Avalonia.Features.Servers;
using RustPlusDesk.Avalonia.Services;
using RustPlusDesk.Core.Devices;
using RustPlusDesk.Core.Connections;
using RustPlusDesk.Core.Servers;
using RustPlusDesk.Infrastructure.Profiles;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class DeviceOrganizerViewModelTests
{
    [TestMethod]
    public async Task Rename_Group_MoveAndReorder_KeepSelectionAndUseFreshRevisions()
    {
        using var file = new DeviceProfileEditorTests.Fixture();
        var model = new DeviceOrganizerViewModel(file.Path, await file.ServerAsync(), new LegacyDeviceProfileEditor());
        model.Selected = model.Devices[0].Children[0];
        Assert.AreEqual("Alias", model.Alias);
        model.Alias = "Renamed";
        await model.RenameCommand.ExecuteAsync(null);
        Assert.AreEqual("Renamed", model.Selected!.Name);
        model.GroupName = "Inner";
        await model.GroupCommand.ExecuteAsync(null);
        Assert.AreEqual("Inner", model.Selected!.Name);
        Assert.IsFalse(model.Destinations.Any(item => item.DeviceKey == model.Selected.DeviceKey));
        Assert.IsFalse(model.Destinations.Any(item => item.DeviceKey?.StartsWith(model.Selected.DeviceKey + "/", StringComparison.Ordinal) == true));
        model.Destination = model.Destinations[0];
        await model.MoveCommand.ExecuteAsync(null);
        Assert.AreEqual("2", model.Selected!.DeviceKey);
        Assert.IsFalse(model.MoveDownCommand.CanExecute(null));
        await model.MoveUpCommand.ExecuteAsync(null);
        Assert.AreEqual("1", model.Selected!.DeviceKey);
        await model.MoveDownCommand.ExecuteAsync(null);
        Assert.AreEqual("2", model.Selected!.DeviceKey);
        Assert.IsFalse(model.Completion.IsCompleted);
        await model.CloseCommand.ExecuteAsync(null);
        var saved = await model.Completion;
        Assert.IsNotNull(saved);
        Assert.AreEqual("Renamed", saved.Devices[2].Children[0].Name);
    }

    [TestMethod]
    public async Task CloseWithoutChange_DoesNotRequestReloadOrCreateBackup()
    {
        using var file = new DeviceProfileEditorTests.Fixture();
        var model = new DeviceOrganizerViewModel(file.Path, await file.ServerAsync(), new LegacyDeviceProfileEditor());
        await model.RenameCommand.ExecuteAsync(null);
        await model.CloseCommand.ExecuteAsync(null);
        Assert.IsNull(await model.Completion);
        Assert.AreEqual(1, Directory.GetFiles(file.Directory).Length);
    }

    [TestMethod]
    public async Task ChangedRevision_DisablesFurtherWrites_AndDoesNotExposeExceptionData()
    {
        using var file = new DeviceProfileEditorTests.Fixture();
        var original = await file.ServerAsync();
        var model = new DeviceOrganizerViewModel(file.Path, original, new LegacyDeviceProfileEditor());
        string changed = await File.ReadAllTextAsync(file.Path) + "\n";
        await File.WriteAllTextAsync(file.Path, changed);
        model.Alias = "Changed";
        await model.RenameCommand.ExecuteAsync(null);
        StringAssert.Contains(model.Message, "file changed");
        Assert.IsFalse(model.RenameCommand.CanExecute(null));
        Assert.IsFalse(model.GroupCommand.CanExecute(null));
        Assert.IsFalse(model.Message.Contains(file.Path, StringComparison.Ordinal));
        Assert.AreEqual(changed, await File.ReadAllTextAsync(file.Path));
        await model.CloseCommand.ExecuteAsync(null);
        Assert.IsNull(await model.Completion);
    }

    [TestMethod]
    public async Task CloseDuringSave_CancelsAndAwaitsWriter_BeforeCompleting()
    {
        using var file = new DeviceProfileEditorTests.Fixture();
        var editor = new WaitingEditor();
        var model = new DeviceOrganizerViewModel(file.Path, await file.ServerAsync(), editor);
        Task saving = model.RenameCommand.ExecuteAsync(null);
        await editor.Started.Task;
        Assert.IsFalse(model.GroupCommand.CanExecute(null));
        Task closing = model.CloseCommand.ExecuteAsync(null);
        Assert.IsFalse(closing.IsCompleted);
        Assert.IsFalse(model.Completion.IsCompleted);
        editor.Release.TrySetResult();
        await Task.WhenAll(saving, closing).WaitAsync(TimeSpan.FromSeconds(4));
        Assert.IsTrue(editor.Canceled);
        Assert.IsNull(await model.Completion);
        Assert.AreEqual(1, Directory.GetFiles(file.Directory).Length);
    }

    [TestMethod]
    public async Task RootModal_BlocksOtherLoadsAndShutdown_AwaitsOwnedWrite()
    {
        using var file = new DeviceProfileEditorTests.Fixture();
        var editor = new WaitingEditor();
        var model = new ServersViewModel(new LegacyServerCatalog(), new Picker(), file.Path, new UnusedSessionFactory(), deviceEditor: editor);
        await model.RefreshCommand.ExecuteAsync(null);
        Task organizing = model.OrganizeDevicesCommand.ExecuteAsync(null);
        Assert.IsTrue(model.HasOverlay);
        Assert.IsFalse(model.RefreshCommand.CanExecute(null));
        Assert.IsFalse(model.OpenFileCommand.CanExecute(null));
        Task saving = model.Organizer!.RenameCommand.ExecuteAsync(null);
        await editor.Started.Task;
        Task shutdown = model.ShutdownAsync();
        Assert.IsFalse(shutdown.IsCompleted);
        editor.Release.TrySetResult();
        await Task.WhenAll(saving, organizing, shutdown).WaitAsync(TimeSpan.FromSeconds(4));
        Assert.IsTrue(editor.Canceled);
        Assert.IsFalse(model.HasOrganizer);
    }

    [TestMethod]
    public async Task SavedOrganization_ReloadsSelectedTree_AndRestartsOnlyThePreviouslyConnectedSession()
    {
        using var file = new DeviceProfileEditorTests.Fixture();
        var sessions = new Sessions();
        var model = new ServersViewModel(new LegacyServerCatalog(), new Picker(), file.Path, sessions, deviceEditor: new LegacyDeviceProfileEditor());
        try
        {
            await model.RefreshCommand.ExecuteAsync(null);
            model.Connection.ConnectCommand.Execute(null);
            await CompanionListenerViewModelTests.Eventually(() => model.Connection.IsConnected);
            string key = model.SelectedServer!.ProfileKey;
            Task organizing = model.OrganizeDevicesCommand.ExecuteAsync(null);
            model.Organizer!.Selected = model.Organizer.Devices[0].Children[0];
            model.Organizer.Alias = "New label";
            await model.Organizer.RenameCommand.ExecuteAsync(null);
            await model.Organizer.CloseCommand.ExecuteAsync(null);
            await organizing.WaitAsync(TimeSpan.FromSeconds(4));
            await CompanionListenerViewModelTests.Eventually(() => sessions.Opened.Count == 2 && model.Connection.IsConnected);
            Assert.IsTrue(sessions.Opened[0].Disposed);
            Assert.AreEqual(key, model.SelectedServer!.ProfileKey);
            Assert.AreEqual("New label", model.SelectedServer.Devices[0].Children[0].Name);
        }
        finally { await model.ShutdownAsync(); }
    }

    private sealed class Sessions : IServerSessionFactory
    {
        public List<Session> Opened { get; } = [];
        public Task<IServerSession> ConnectAsync(string profilesPath, string profileKey, CancellationToken cancellationToken)
        { var session = new Session(); Opened.Add(session); return Task.FromResult<IServerSession>(session); }
    }
    private sealed class Session : IServerSession
    {
        public IDeviceClient Devices { get; } = new TestDeviceClient();
        public ServerStatus InitialStatus { get; } = new("Fixture", 1, 100, 0);
        public bool Disposed { get; private set; }
        public Task<ServerStatus> ReadStatusAsync(CancellationToken cancellationToken) => Task.FromResult(InitialStatus);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class Picker : IProfileFilePicker
    {
        public Task<string?> PickAsync() => throw new AssertFailedException("Picker must remain blocked.");
    }

    private sealed class WaitingEditor : IDeviceProfileEditor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Canceled { get; private set; }
        public async Task<DeviceEditResult> RenameAsync(string path, ServerSummary original, string deviceKey, string alias, CancellationToken ct)
        {
            Started.TrySetResult();
            await Release.Task;
            Canceled = ct.IsCancellationRequested;
            ct.ThrowIfCancellationRequested();
            throw new AssertFailedException("Test must cancel the pending writer.");
        }
        public Task<DeviceEditResult> GroupAsync(string path, ServerSummary original, string deviceKey, string name, CancellationToken ct) => throw new AssertFailedException();
        public Task<DeviceEditResult> MoveAsync(string path, ServerSummary original, string deviceKey, string? parentKey, int? index, CancellationToken ct) => throw new AssertFailedException();
    }
}
