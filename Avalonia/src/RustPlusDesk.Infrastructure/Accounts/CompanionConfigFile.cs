using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.AccessControl;
using System.Security.Principal;
using RustPlusDesk.Core.Accounts;

namespace RustPlusDesk.Infrastructure.Accounts;

internal sealed record CompanionConfigFile(JsonObject Values, byte[]? Original)
{
    public string? Text(string key) => Values[key]?.GetValue<string>();

    public static async Task<CompanionConfigFile> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            byte[] original = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return new(JsonNode.Parse(original)?.AsObject() ?? throw new JsonException(), original);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return new(new(), null); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        { throw new CompanionAccountException(AccountFailure.InvalidConfiguration); }
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using var source = Original is null ? null : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            await VerifyAsync(path, cancellationToken).ConfigureAwait(false);
            await using (var pending = CreatePrivateFile(temporary))
            {
                await JsonSerializer.SerializeAsync(pending, Values, cancellationToken: cancellationToken).ConfigureAwait(false);
                pending.Flush(flushToDisk: true);
            }
            await VerifyAsync(path, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (Original is null) File.Move(temporary, path);
            else
            {
                // Windows replacement retains the destination ACL, so restrict it before publishing new credentials.
                if (OperatingSystem.IsWindows()) new FileInfo(path).SetAccessControl(CreateWindowsPermissions());
                File.Replace(temporary, path, destinationBackupFileName: null);
            }
        }
        catch (FileNotFoundException) { throw new CompanionAccountException(AccountFailure.ChangedConfiguration); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static FileStream CreatePrivateFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough, CreateWindowsPermissions());
        }
        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
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

    private async Task VerifyAsync(string path, CancellationToken cancellationToken)
    {
        byte[]? current;
        try { current = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false); }
        catch (FileNotFoundException) { current = null; }
        if (Original is null ? current is not null : current is null || !Original.AsSpan().SequenceEqual(current))
            throw new CompanionAccountException(AccountFailure.ChangedConfiguration);
    }
}
