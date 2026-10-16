using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using RustPlusDesk.Core.Servers;

namespace RustPlusDesk.Infrastructure.Profiles;

/// <summary>Updates only editable fields; backs up exact source bytes before replacing the file.</summary>
public sealed class LegacyServerProfileEditor : IServerProfileEditor
{
    public async Task<ServerSummary> SaveAsync(string path, ServerSummary original, string name, ServerAddress address, CancellationToken cancellationToken)
    {
        address.Validate();
        path = Path.GetFullPath(path);
        // Deny in-place writers while reading and replacing. Atomic renames by other applications still require the revision recheck.
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            4096, FileOptions.Asynchronous);
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        byte[] before = buffer.ToArray();
        VerifyRevision(before, original.SourceRevision);
        var profiles = LegacyServerCatalog.Parse(before);
        int[] matches = profiles.Select((profile, index) => (profile, index))
            .Where(pair => pair.profile.ProfileKey == original.ProfileKey).Select(pair => pair.index).ToArray();
        if (matches.Length != 1) throw new ProfileChangedException();
        int selected = matches[0];
        var document = JsonNode.Parse(before)!.AsArray();
        var node = document[selected]!.AsObject();
        if (!string.IsNullOrWhiteSpace(name)) node["Name"] = name.Trim();
        node["Host"] = address.Host.Trim();
        node["Port"] = address.Port;
        byte[] after = JsonSerializer.SerializeToUtf8Bytes(document, new JsonSerializerOptions { WriteIndented = true });
        var updated = LegacyServerCatalog.Parse(after)[selected];
        if (profiles.Where((_, index) => index != selected).Any(profile => profile.ProfileKey == updated.ProfileKey))
            throw new ArgumentException("A profile for that server and account already exists.");

        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string backup = path + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "." + Guid.NewGuid().ToString("N") + ".bak";
        try
        {
            await using (var pending = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, File.GetUnixFileMode(path));
                await pending.WriteAsync(after, cancellationToken).ConfigureAwait(false);
                pending.Flush(flushToDisk: true);
            }
            VerifyRevision(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false), original.SourceRevision);
            cancellationToken.ThrowIfCancellationRequested();
            File.Replace(temporary, path, backup);
            return updated;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void VerifyRevision(byte[] bytes, string expected)
    {
        if (string.IsNullOrEmpty(expected) || Convert.ToHexString(SHA256.HashData(bytes)) != expected)
            throw new ProfileChangedException();
    }
}
