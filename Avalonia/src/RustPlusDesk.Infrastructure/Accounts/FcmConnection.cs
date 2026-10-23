using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;

namespace RustPlusDesk.Infrastructure.Accounts;

internal interface IFcmConnection : IAsyncDisposable
{
    event EventHandler? Disconnected;
    event EventHandler? SocketClosed;
    event EventHandler<Exception>? ErrorOccurred;
    Task ConnectAsync(CancellationToken ct);
}

internal sealed class FcmConnection(Credentials credentials, List<string> ids, Action<FcmMessage> receive)
    : RustPlusFcm(credentials, ids, new RustPlusFcmSocketOptions
    { HeartbeatInterval = TimeSpan.FromSeconds(60), InactivityTimeout = TimeSpan.FromMinutes(3) }), IFcmConnection
{
    protected override void ParseNotification(FcmMessage message)
    {
        // This runs on the same SDK receive loop that adds IDs, so pruning cannot race its collection access.
        if (ids.Count > 1000) ids.RemoveRange(0, ids.Count - 1000);
        receive(message);
    }
}
