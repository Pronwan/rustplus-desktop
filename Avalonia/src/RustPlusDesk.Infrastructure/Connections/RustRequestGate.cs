using System.Diagnostics;

namespace RustPlusDesk.Infrastructure.Connections;

/// <summary>Paces all requests on a session to the reference client's 25 requests per second.</summary>
internal sealed class RustRequestGate
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private long lastRequest;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var remaining = TimeSpan.FromMilliseconds(40) - Stopwatch.GetElapsedTime(lastRequest);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
            lastRequest = Stopwatch.GetTimestamp();
        }
        finally { gate.Release(); }
    }
}
