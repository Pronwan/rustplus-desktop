using System.Security.AccessControl;
using System.Security.Principal;
using RustPlusDesk.Core.Servers;

namespace RustPlusDesk.Infrastructure.Storage;

/// <summary>Publishes private data atomically after checking the exact source revision.</summary>
internal static class AtomicPrivateFile
{
    public static async Task<byte[]?> ReadAsync(string path, CancellationToken ct)
    {
        try { return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return null; }
    }

    public static async Task WriteAsync(string path, byte[] data, byte[]? original, bool backup, CancellationToken ct)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using var source = original is null ? null : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            await VerifyAsync(path, original, ct).ConfigureAwait(false);
            await using (var pending = CreatePrivateFile(temporary))
            {
                await pending.WriteAsync(data, ct).ConfigureAwait(false);
                pending.Flush(flushToDisk: true);
            }
            await VerifyAsync(path, original, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (original is null) File.Move(temporary, path);
            else
            {
                // Replace retains the destination ACL, including the copy used for a backup.
                if (OperatingSystem.IsWindows()) new FileInfo(path).SetAccessControl(CreateWindowsPermissions());
                else File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.Replace(temporary, path, backup ? path + "." + Guid.NewGuid().ToString("N") + ".bak" : null);
            }
        }
        catch (FileNotFoundException) { throw new ProfileChangedException(); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static FileStream CreatePrivateFile(string path)
    {
        if (OperatingSystem.IsWindows())
            return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough, CreateWindowsPermissions());
        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        });
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static FileSecurity CreateWindowsPermissions()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var permissions = new FileSecurity();
        permissions.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        permissions.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl, AccessControlType.Allow));
        return permissions;
    }

    private static async Task VerifyAsync(string path, byte[]? expected, CancellationToken ct)
    {
        byte[]? current = await ReadAsync(path, ct).ConfigureAwait(false);
        if (expected is null ? current is not null : current is null || !expected.AsSpan().SequenceEqual(current))
            throw new ProfileChangedException();
    }
}
