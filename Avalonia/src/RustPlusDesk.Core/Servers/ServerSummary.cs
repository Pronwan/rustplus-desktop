namespace RustPlusDesk.Core.Servers;

/// <summary>A credential-free description of a saved server.</summary>
public sealed record ServerSummary(string Name, string Host, int Port)
{
    public string ProfileKey { get; init; } = "";
    public string SourceRevision { get; init; } = "";
    public IReadOnlyList<Devices.PairedDevice> Devices { get; init; } = [];
}
