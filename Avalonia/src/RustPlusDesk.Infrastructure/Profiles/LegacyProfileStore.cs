using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using RustPlusDesk.Core.Servers;
using RustPlusDesk.Infrastructure.Storage;

namespace RustPlusDesk.Infrastructure.Profiles;

/// <summary>Serializes local profile writers and preserves unknown JSON with private, backed-up replacement.</summary>
internal static class LegacyProfileStore
{
    private static readonly SemaphoreSlim Writes = new(1, 1);

    public static async Task<IReadOnlyList<ServerSummary>> UpdateAsync(string path, Action<JsonArray> update, string? expectedRevision, CancellationToken ct)
    {
        await Writes.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            byte[]? before = await AtomicPrivateFile.ReadAsync(path, ct).ConfigureAwait(false);
            if (expectedRevision is not null && (before is null || Convert.ToHexString(SHA256.HashData(before)) != expectedRevision))
                throw new ProfileChangedException();
            if (before is not null) _ = LegacyServerCatalog.Parse(before);
            var document = before is null ? new JsonArray() : JsonNode.Parse(before)!.AsArray();
            var original = document.DeepClone();
            update(document);
            byte[] after = JsonSerializer.SerializeToUtf8Bytes(document, new JsonSerializerOptions { WriteIndented = true });
            var servers = LegacyServerCatalog.Parse(after);
            if (servers.Select(server => server.ProfileKey).Distinct().Count() != servers.Count)
                throw new ArgumentException("A profile for that server and account already exists.");
            if (!JsonNode.DeepEquals(document, original))
                await AtomicPrivateFile.WriteAsync(path, after, before, true, ct).ConfigureAwait(false);
            else if (before is not null) return LegacyServerCatalog.Parse(before);
            return servers;
        }
        finally { Writes.Release(); }
    }
}
