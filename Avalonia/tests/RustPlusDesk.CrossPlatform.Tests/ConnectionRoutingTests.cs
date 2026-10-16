using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusApi;
using RustPlusDesk.Core.Connections;
using RustPlusDesk.Infrastructure.Connections;
using RustPlusDesk.Infrastructure.Profiles;
using RustPlusDesk.Core.Servers;
using RustPlusDesk.Core.Devices;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class ConnectionRoutingTests
{
    [TestMethod]
    public async Task EditedAddressProbe_BypassesSocketChecksNestedDevicesAndDisposesWithoutSaving()
    {
        using var fixture = new ProfileFixture();
        string before = await File.ReadAllTextAsync(fixture.Path);
        var profile = (await new LegacyServerCatalog().LoadAsync(fixture.Path))[0] with
        { Devices = [new(0, "Group", DeviceKind.Group) { Children =
            [new(1, "Switch", DeviceKind.Switch), new(2, "Storage", DeviceKind.StorageMonitor), new(1, "Alias", DeviceKind.Switch)] }] };
        var routes = new List<TestTransport>();
        var factory = new RustPlusSessionFactory(route =>
        {
            var transport = new TestTransport(route) { Devices = new TestDeviceClient
                { Read = (device, _) => Task.FromResult(new DeviceSnapshot(device.EntityId, IsMissing: device.EntityId == 2)) } };
            routes.Add(transport);
            return transport;
        });
        var result = await factory.TestAsync(fixture.Path, profile, new("edited.test", 29082), CancellationToken.None);
        Assert.AreEqual(2, result.DevicesChecked);
        Assert.AreEqual(1, result.DevicesResponding);
        var used = routes.Single();
        Assert.IsNull(used.Route.ServerId, "The companion socket ignores host/port and cannot verify an edited address.");
        Assert.AreEqual("edited.test", used.Route.Server);
        Assert.AreEqual(29082, used.Route.Port);
        Assert.IsTrue(used.Disposed);
        Assert.AreEqual(before, await File.ReadAllTextAsync(fixture.Path));
    }

    [TestMethod]
    public async Task ConnectAsync_CompanionSuccess_UsesLoginAndDoesNotTryDirect()
    {
        using var fixture = new ProfileFixture();
        var transports = new List<TestTransport>();
        var factory = new RustPlusSessionFactory(route =>
        {
            var transport = new TestTransport(route);
            transports.Add(transport);
            return transport;
        });
        await using var session = await factory.ConnectAsync(fixture.Path, await fixture.KeyAsync(), CancellationToken.None);

        Assert.AreEqual(1, transports.Count);
        Assert.AreEqual(ProfileFixture.ServerId, transports[0].Route.ServerId);
        Assert.AreEqual("fixture-companion-token", transports[0].Route.AuthToken);
        Assert.IsFalse(transports[0].Disposed);
        Assert.AreEqual("Verified server", session.InitialStatus.Name);
        Assert.IsFalse(JsonSerializer.Serialize((await new LegacyServerCatalog().LoadAsync(fixture.Path))[0])
            .Contains("fixture-companion-token", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ConnectAsync_CompanionFailure_DisposesBeforeDirectFallbackSucceeds()
    {
        using var fixture = new ProfileFixture();
        TestTransport? first = null, second = null;
        var factory = new RustPlusSessionFactory(route =>
        {
            if (route.ServerId is not null)
                return first = new TestTransport(route) { Error = new InvalidOperationException("status '403'") };
            Assert.IsTrue(first!.Disposed);
            return second = new TestTransport(route);
        });
        var session = await factory.ConnectAsync(fixture.Path, await fixture.KeyAsync(), CancellationToken.None);
        Assert.IsNotNull(second);
        Assert.IsNull(second.Route.ServerId);
        Assert.IsFalse(second.Route.UseFacepunchProxy);
        await session.DisposeAsync();
        Assert.IsTrue(second.Disposed);
    }

    [TestMethod]
    public async Task ConnectAsync_RejectedCompanionLoginAndOfflineDirect_ReportsSignInNotRePair()
    {
        using var fixture = new ProfileFixture();
        var factory = new RustPlusSessionFactory(route => new TestTransport(route)
        {
            Error = new InvalidOperationException(route.ServerId is not null ? "status code '403'" : "offline port 41842")
        });
        string key = await fixture.KeyAsync();
        var error = await Assert.ThrowsExceptionAsync<ServerConnectionException>(
            () => factory.ConnectAsync(fixture.Path, key, CancellationToken.None));
        Assert.AreEqual(ConnectionFailure.CompanionLoginRequired, error.Failure);
    }

    [TestMethod]
    public async Task ConnectAsync_MissingLogin_PreservesDirectRouteAndReportsMissingLoginWhenOffline()
    {
        using var fixture = new ProfileFixture();
        File.Delete(fixture.AuthPath);
        int attempts = 0;
        var factory = new RustPlusSessionFactory(route =>
        {
            attempts++;
            Assert.IsNull(route.ServerId);
            return new TestTransport(route) { Error = new IOException("offline") };
        });
        string key = await fixture.KeyAsync();
        var error = await Assert.ThrowsExceptionAsync<ServerConnectionException>(
            () => factory.ConnectAsync(fixture.Path, key, CancellationToken.None));
        Assert.AreEqual(ConnectionFailure.CompanionLoginRequired, error.Failure);
        Assert.AreEqual(1, attempts);
    }

    [TestMethod]
    public async Task ConnectAsync_CancelWhileOpening_DisposesTransportAndSkipsFallback()
    {
        using var fixture = new ProfileFixture();
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TestTransport? transport = null;
        int attempts = 0;
        var factory = new RustPlusSessionFactory(route =>
        {
            attempts++;
            return transport = new TestTransport(route) { Initialize = ct =>
            {
                started.SetResult();
                return Task.Delay(Timeout.Infinite, ct);
            }};
        });
        var connecting = factory.ConnectAsync(fixture.Path, await fixture.KeyAsync(), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => connecting);
        Assert.IsTrue(transport!.Disposed);
        Assert.AreEqual(1, attempts);
    }

    [TestMethod]
    public async Task ConnectAsync_ProfileChangedOrCredentialsInvalid_DoesNotOpenTransport()
    {
        using var fixture = new ProfileFixture();
        string key = await fixture.KeyAsync();
        var factory = new RustPlusSessionFactory(_ => throw new AssertFailedException("Invalid credentials must not open a connection."));
        fixture.WriteProfiles("invalid");
        var invalid = await Assert.ThrowsExceptionAsync<ServerConnectionException>(
            () => factory.ConnectAsync(fixture.Path, key, CancellationToken.None));
        Assert.AreEqual(ConnectionFailure.InvalidCredentials, invalid.Failure);
        var changed = await Assert.ThrowsExceptionAsync<ServerConnectionException>(
            () => factory.ConnectAsync(fixture.Path, "stale-reference", CancellationToken.None));
        Assert.AreEqual(ConnectionFailure.ProfileUnavailable, changed.Failure);
        Assert.IsFalse(invalid.ToString().Contains("fixture-companion-token", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ProfileIdentity_TokenRotationKeepsIdentity_AccountsRemainDistinct()
    {
        using var fixture = new ProfileFixture();
        string original = await fixture.KeyAsync();
        fixture.WriteProfiles("-2345");
        Assert.AreEqual(original, await fixture.KeyAsync());
        string json = await File.ReadAllTextAsync(fixture.Path);
        await File.WriteAllTextAsync(fixture.Path, json.Replace("76561198000000001", "76561198000000002", StringComparison.Ordinal));
        Assert.AreNotEqual(original, await fixture.KeyAsync());
    }

    [TestMethod]
    [DataRow("HTTP 418", ConnectionFailure.RePairRequired)]
    [DataRow("port 41842 is offline", ConnectionFailure.Unreachable)]
    [DataRow("not_found", ConnectionFailure.RePairRequired)]
    [DataRow("Invalid Player Token", ConnectionFailure.RePairRequired)]
    [DataRow("timeout", ConnectionFailure.Unreachable)]
    public void ResponseClassification_PreservesAuthenticationEvidence(string response, ConnectionFailure expected)
        => Assert.AreEqual(expected, ConnectionFailureClassifier.Response(response));

    private sealed class TestTransport(RustPlusConnection route) : IRustTransport
    {
        public IDeviceClient Devices { get; init; } = new TestDeviceClient();
        public RustPlusConnection Route { get; } = route;
        public bool Disposed { get; private set; }
        public Exception? Error { get; init; }
        public Func<CancellationToken, Task>? Initialize { get; init; }
        public ServerStatus InitialStatus { get; } = new("Verified server", 50, 200, 3, 14);
        public Task InitializeAsync(CancellationToken cancellationToken) => Initialize?.Invoke(cancellationToken)
            ?? (Error is null ? Task.CompletedTask : Task.FromException(Error));
        public Task<ServerStatus> ReadStatusAsync(CancellationToken cancellationToken) => Task.FromResult(InitialStatus);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class ProfileFixture : IDisposable
    {
        public static Guid ServerId { get; } = Guid.Parse("ba135994-22aa-4a34-bc2e-bf3c6d045678");
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rustplus-routing-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(directory, "profiles.json");
        public string AuthPath => System.IO.Path.Combine(directory, "rustplusjs-config.json");
        public ProfileFixture()
        {
            Directory.CreateDirectory(directory);
            WriteProfiles("1234");
            File.WriteAllText(AuthPath, "{\"rustplus_auth_token\":\"fixture-companion-token\"}");
        }
        public void WriteProfiles(string token) => File.WriteAllText(Path, JsonSerializer.Serialize(new[]
        {
            new { Name = "Fixture server", Host = "localhost", Port = 28082, SteamId64 = "76561198000000001", PlayerToken = token, ServerId }
        }));
        public async Task<string> KeyAsync() => (await new LegacyServerCatalog().LoadAsync(Path))[0].ProfileKey;
        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
