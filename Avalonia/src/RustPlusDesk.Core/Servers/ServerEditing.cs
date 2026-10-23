namespace RustPlusDesk.Core.Servers;

/// <summary>Editable connection details, without account or pairing credentials.</summary>
public sealed record ServerAddress(string Host, int Port)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host) || Host.Any(char.IsWhiteSpace) || Port is < 1 or > 65535)
            throw new ArgumentException("Enter a host without spaces and a port between 1 and 65535.");
    }
}

/// <summary>Changes one saved profile while preserving its unrecognized data.</summary>
public interface IServerProfileEditor
{
    Task<ServerSummary> SaveAsync(string path, ServerSummary original, string name, ServerAddress address, CancellationToken cancellationToken);
}

/// <summary>Probes an edited endpoint using the selected profile's existing credentials without saving.</summary>
public interface IServerConnectionTester
{
    Task<ConnectionTestResult> TestAsync(string path, ServerSummary original, ServerAddress address, CancellationToken cancellationToken);
}

public sealed record ConnectionTestResult(int DevicesChecked, int DevicesResponding);

/// <summary>A stale edit must be refreshed rather than overwriting newer data.</summary>
public sealed class ProfileChangedException() : IOException("The profiles file changed. Refresh before editing again.");
