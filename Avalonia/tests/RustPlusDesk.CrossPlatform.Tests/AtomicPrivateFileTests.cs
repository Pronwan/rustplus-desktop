using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Core.Servers;
using RustPlusDesk.Infrastructure.Storage;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class AtomicPrivateFileTests
{
    [TestMethod]
    public async Task Replace_ProtectsNewDataAndExactBackup_AndRejectsStaleRevision()
    {
        using var files = new PairingFiles();
        byte[] before = "private-fixture-before"u8.ToArray();
        byte[] after = "private-fixture-after"u8.ToArray();
        await File.WriteAllBytesAsync(files.Profiles, before);
        await AtomicPrivateFile.WriteAsync(files.Profiles, after, before, true, CancellationToken.None);
        string backup = Directory.GetFiles(files.Directory, "*.bak").Single();
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(backup));
        CollectionAssert.AreEqual(after, await File.ReadAllBytesAsync(files.Profiles));
        foreach (string path in new[] { backup, files.Profiles })
        {
            if (OperatingSystem.IsWindows())
            {
                using var user = WindowsIdentity.GetCurrent();
                var permissions = new FileInfo(path).GetAccessControl();
                Assert.IsTrue(permissions.AreAccessRulesProtected);
                foreach (FileSystemAccessRule rule in permissions.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                    Assert.AreEqual(user.User, rule.IdentityReference);
            }
            else Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
        await Assert.ThrowsExceptionAsync<ProfileChangedException>(() => AtomicPrivateFile.WriteAsync(files.Profiles, before, before, true, CancellationToken.None));
        CollectionAssert.AreEqual(after, await File.ReadAllBytesAsync(files.Profiles));
        Assert.AreEqual(2, Directory.GetFiles(files.Directory).Length);
    }
}
