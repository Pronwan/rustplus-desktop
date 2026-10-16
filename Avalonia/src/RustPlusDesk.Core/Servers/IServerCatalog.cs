namespace RustPlusDesk.Core.Servers;

/// <summary>Reads server descriptions without changing pairing data.</summary>
public interface IServerCatalog
{
    Task<IReadOnlyList<ServerSummary>> LoadAsync(string path, CancellationToken cancellationToken = default);
}
