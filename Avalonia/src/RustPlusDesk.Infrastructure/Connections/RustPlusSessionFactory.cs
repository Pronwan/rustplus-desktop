using RustPlusApi;
using RustPlusDesk.Core.Connections;
using RustPlusDesk.Core.Servers;
using RustPlusDesk.Core.Devices;

namespace RustPlusDesk.Infrastructure.Connections;

/// <summary>Preserves the companion-socket-first route and direct fallback of the reference client.</summary>
public sealed class RustPlusSessionFactory : IServerSessionFactory, IServerConnectionTester
{
    private readonly Func<RustPlusConnection, IRustTransport> createTransport;

    public RustPlusSessionFactory() : this(connection => new RustPlusTransport(connection)) { }

    internal RustPlusSessionFactory(Func<RustPlusConnection, IRustTransport> createTransport)
        => this.createTransport = createTransport;

    public Task<IServerSession> ConnectAsync(string profilesPath, string profileKey, CancellationToken cancellationToken)
        => ConnectAsyncCore(profilesPath, profileKey, null, cancellationToken);

    public async Task<ConnectionTestResult> TestAsync(string path, ServerSummary original, ServerAddress address, CancellationToken cancellationToken)
    {
        address.Validate();
        await using var session = await ConnectAsyncCore(path, original.ProfileKey, address, cancellationToken).ConfigureAwait(false);
        var devices = original.Devices.SelectMany(device => device.DescendantsAndSelf())
            .Where(device => device.EntityId != 0 && device.Kind is DeviceKind.Switch or DeviceKind.Alarm or DeviceKind.StorageMonitor)
            .DistinctBy(device => device.EntityId).ToArray();
        int responding = 0;
        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { if (!(await session.Devices.ReadAsync(device, cancellationToken).ConfigureAwait(false)).IsMissing) responding++; }
            catch (DeviceOperationException) { }
        }
        return new(devices.Length, responding);
    }

    private async Task<IServerSession> ConnectAsyncCore(string profilesPath, string profileKey, ServerAddress? address, CancellationToken cancellationToken)
    {
        var routes = await LegacyConnectionRoutes.ReadAsync(profilesPath, profileKey, cancellationToken).ConfigureAwait(false);
        var failure = routes.CompanionLoginMissing ? ConnectionFailure.CompanionLoginRequired : ConnectionFailure.Unreachable;
        foreach (var route in routes.Routes)
        {
            // A companion socket ignores host/port, so only a direct probe can verify an edited address.
            if (address is not null && route.ServerId is not null) continue;
            cancellationToken.ThrowIfCancellationRequested();
            var transport = createTransport(address is null ? route : route with { Server = address.Host, Port = address.Port });
            bool connected = false;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(6));
                await transport.InitializeAsync(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
                connected = true;
                return transport;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                var routeFailure = ConnectionFailureClassifier.Exception(ex, route.ServerId is not null);
                if (routeFailure == ConnectionFailure.RePairRequired || failure == ConnectionFailure.Unreachable)
                    failure = routeFailure;
            }
            finally
            {
                if (!connected) await transport.DisposeAsync().ConfigureAwait(false);
            }
        }
        throw new ServerConnectionException(failure);
    }
}

internal interface IRustTransport : IServerSession
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
