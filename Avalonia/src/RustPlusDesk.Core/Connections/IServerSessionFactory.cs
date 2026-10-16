namespace RustPlusDesk.Core.Connections;

/// <summary>Opens an authenticated session using a credential-free profile reference.</summary>
public interface IServerSessionFactory
{
    Task<IServerSession> ConnectAsync(string profilesPath, string profileKey, CancellationToken cancellationToken);
}

/// <summary>An owned connection whose credentials never leave its implementation.</summary>
public interface IServerSession : IAsyncDisposable
{
    Devices.IDeviceClient Devices { get; }
    ServerStatus InitialStatus { get; }
    /// <summary>Completes when this socket reports disconnection; scoped to this session only.</summary>
    Task Disconnected { get; }
    Task<ServerStatus> ReadStatusAsync(CancellationToken cancellationToken);
}
