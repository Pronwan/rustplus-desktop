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
    Task<ServerStatus> ReadStatusAsync(CancellationToken cancellationToken);
}
