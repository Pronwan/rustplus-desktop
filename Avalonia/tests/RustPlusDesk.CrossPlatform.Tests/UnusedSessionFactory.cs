using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Core.Connections;

namespace RustPlusDesk.CrossPlatform.Tests;

internal sealed class UnusedSessionFactory : IServerSessionFactory
{
    public Task<IServerSession> ConnectAsync(string profilesPath, string profileKey, CancellationToken cancellationToken)
        => throw new AssertFailedException("Loading profiles must not connect to a server.");
}
