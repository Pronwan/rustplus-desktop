using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.PlayerWipeTracker;

/// <summary>Bounded, coalescing uploader; failures never reach team polling.</summary>
public sealed class PlayerWipeTrackerCloudSyncQueue : IAsyncDisposable
{
    private readonly Channel<string> _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(64)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
    });
    private readonly ConcurrentDictionary<string, CloudDayUploadRequest> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _scheduled = new(StringComparer.Ordinal);
    private readonly Func<CloudDayUploadRequest, CancellationToken, Task<int>> _upload;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;

    public PlayerWipeTrackerCloudSyncQueue(Func<CloudDayUploadRequest, CancellationToken, Task<int>> upload)
    {
        _upload = upload ?? throw new ArgumentNullException(nameof(upload));
        _worker = Task.Run(WorkAsync);
    }

    public bool Enqueue(CloudDayUploadRequest request)
    {
        var key = $"{request.ServerKey}|{request.WipeKey}|{request.PlayerSteamId}|{request.Day}";
        _pending[key] = request;
        if (!_scheduled.TryAdd(key, 0))
            return true;
        if (_queue.Writer.TryWrite(key))
            return true;
        _scheduled.TryRemove(key, out _);
        _pending.TryRemove(key, out _);
        return false;
    }

    private async Task WorkAsync()
    {
        try
        {
            await foreach (var key in _queue.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                if (!_pending.TryRemove(key, out var request))
                {
                    _scheduled.TryRemove(key, out _);
                    continue;
                }

                for (var attempt = 0; attempt < 3; attempt++)
                {
                    int status;
                    try { status = await _upload(request, _shutdown.Token).ConfigureAwait(false); }
                    catch { status = 599; }
                    if (status is >= 200 and < 300 || status == 409 || status == 403 || status == 422)
                        break;
                    if (attempt < 2)
                        await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << attempt) + Random.Shared.Next(0, 250)), _shutdown.Token).ConfigureAwait(false);
                }

                if (_pending.ContainsKey(key))
                    _queue.Writer.TryWrite(key);
                else
                    _scheduled.TryRemove(key, out _);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        _shutdown.Cancel();
        try { await _worker.ConfigureAwait(false); } catch { }
        _shutdown.Dispose();
    }
}
