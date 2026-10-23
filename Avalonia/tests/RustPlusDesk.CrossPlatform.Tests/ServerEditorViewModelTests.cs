using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Avalonia.Features.Servers;
using RustPlusDesk.Core.Servers;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class ServerEditorViewModelTests
{
    private static readonly ServerSummary Original = new("Saved", "old.test", 28082) { ProfileKey = "key", SourceRevision = "revision" };

    [TestMethod]
    public async Task InvalidAddress_DoesNotCallWriterOrTester()
    {
        var services = new EditorServices();
        var model = new ServerEditorViewModel("profiles.json", Original, services, services) { Host = "", Port = "invalid" };
        await model.TestCommand.ExecuteAsync(null);
        await model.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(0, services.Tests);
        Assert.AreEqual(0, services.Saves);
        Assert.IsFalse(model.Completion.IsCompleted);
        StringAssert.Contains(model.Message, "port between");
    }

    [TestMethod]
    public async Task Test_UsesUnsavedAddressWithoutWriting_ReportsMissingDevices()
    {
        var services = new EditorServices();
        var model = new ServerEditorViewModel("profiles.json", Original, services, services) { Host = " new.test ", Port = "29082" };
        await model.TestCommand.ExecuteAsync(null);
        Assert.AreEqual(new ServerAddress("new.test", 29082), services.Address);
        Assert.AreEqual(0, services.Saves);
        StringAssert.Contains(model.Message, "1 of 2");
        Assert.IsFalse(model.Completion.IsCompleted);
        await model.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual("new.test", (await model.Completion)!.Host);
        Assert.AreEqual(1, services.Saves);
    }

    [TestMethod]
    public async Task Cancel_TestPending_StopsWorkAndDoesNotSave()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new EditorServices { Probe = async ct =>
        { started.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return new(0, 0); } };
        var model = new ServerEditorViewModel("profiles.json", Original, services, services);
        var testing = model.TestCommand.ExecuteAsync(null);
        await started.Task;
        Assert.IsFalse(model.SaveCommand.CanExecute(null));
        await model.CancelCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
        await testing;
        Assert.IsNull(await model.Completion);
        Assert.IsFalse(model.IsBusy);
        Assert.AreEqual(0, services.Saves);
    }

    [TestMethod]
    public async Task ChangedFile_DoesNotCloseEditor_AndRawErrorsStayPrivate()
    {
        var services = new EditorServices { SaveError = new ProfileChangedException() };
        var model = new ServerEditorViewModel("profiles.json", Original, services, services);
        await model.SaveCommand.ExecuteAsync(null);
        Assert.IsFalse(model.Completion.IsCompleted);
        StringAssert.Contains(model.Message, "file changed");
        services.SaveError = new IOException("private credential path");
        await model.SaveCommand.ExecuteAsync(null);
        Assert.IsFalse(model.Message.Contains("private", StringComparison.Ordinal));
    }

    internal sealed class EditorServices : IServerProfileEditor, IServerConnectionTester
    {
        public int Tests { get; private set; }
        public int Saves { get; private set; }
        public ServerAddress? Address { get; private set; }
        public Exception? SaveError { get; set; }
        public Func<CancellationToken, Task<ConnectionTestResult>>? Probe { get; init; }
        public Task<ServerSummary> SaveAsync(string path, ServerSummary original, string name, ServerAddress address, CancellationToken cancellationToken)
        {
            Saves++;
            return SaveError is { } error ? Task.FromException<ServerSummary>(error)
                : Task.FromResult(original with { Name = name, Host = address.Host, Port = address.Port });
        }
        public Task<ConnectionTestResult> TestAsync(string path, ServerSummary original, ServerAddress address, CancellationToken cancellationToken)
        {
            Tests++;
            Address = address;
            return Probe?.Invoke(cancellationToken) ?? Task.FromResult(new ConnectionTestResult(2, 1));
        }
    }
}
