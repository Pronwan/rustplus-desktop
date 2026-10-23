using RustPlusApi;
using RustPlusDesk.Core.Connections;
using RustPlusDesk.Core.Devices;

namespace RustPlusDesk.Infrastructure.Connections;

internal sealed class RustPlusTransport : IRustTransport
{
    private readonly RustPlus api;
    private readonly bool companionSocket;
    private bool disposed;
    private readonly RustRequestGate requests = new();
    private readonly RustPlusDeviceClient devices;
    private readonly TaskCompletionSource disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Disconnected => disconnected.Task;
    public IDeviceClient Devices => devices;

    public RustPlusTransport(RustPlusConnection connection)
    {
        api = new RustPlus(connection, new RustPlusSocketOptions { RequestTimeout = TimeSpan.FromSeconds(6) });
        devices = new RustPlusDeviceClient(api, requests);
        api.Disconnected += OnDisconnected;
        companionSocket = connection.ServerId is not null;
    }

    public ServerStatus InitialStatus { get; private set; } = null!;
    private void OnDisconnected(object? sender, EventArgs e) => disconnected.TrySetResult();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await api.ConnectAsync(cancellationToken).ConfigureAwait(false);
        InitialStatus = await ReadStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ServerStatus> ReadStatusAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            await requests.WaitAsync(timeout.Token).ConfigureAwait(false);
            var info = await api.GetInfoAsync(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            if (!info.IsSuccess || info.Data is null)
                throw new ServerConnectionException(ConnectionFailureClassifier.Response(info.Error?.Message));

            var data = info.Data;
            var status = new ServerStatus(data.Name ?? "", data.PlayerCount, data.MaxPlayerCount, data.QueuedPlayerCount);
            // Time is optional on some modded servers; authenticated server info still proves connectivity.
            try
            {
                await requests.WaitAsync(timeout.Token).ConfigureAwait(false);
                var time = await api.GetTimeAsync(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
                if (time.IsSuccess && time.Data is not null)
                    status = status with
                    {
                        GameHours = time.Data.Time,
                        Sunrise = time.Data.Sunrise,
                        Sunset = time.Data.Sunset,
                        DayLengthMinutes = time.Data.DayLengthMinutes,
                        TimeScale = time.Data.TimeScale
                    };
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
            cancellationToken.ThrowIfCancellationRequested();
            return status;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            throw new ServerConnectionException(ConnectionFailureClassifier.Exception(ex, companionSocket));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        api.Disconnected -= OnDisconnected;
        disconnected.TrySetResult();
        devices.Dispose();
        try { await api.DisconnectAsync(true).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (Exception) { /* Teardown must still release the socket after a failed disconnect. */ }
        finally { api.Dispose(); }
    }
}
