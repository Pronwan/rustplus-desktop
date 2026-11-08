using System;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.Map;

/// <summary>
/// Hands what a map leaves behind back to Windows, once a load or a disconnect has settled.
///
/// A load allocates 150 MB or more of short-lived large arrays: PNG encoding for the wipe map,
/// the water mask's pixel copy, JSON parsing. Leaving a map drops its bitmaps, whose pixels are
/// native memory freed only by their finalizers. All of it is garbage within seconds, but a
/// mostly idle app rarely reaches the full collection that would free it, so each reconnect
/// stacked a new map on top of the old one.
/// </summary>
internal static class MapMemoryTrim
{
    /// <summary>Long enough for the overlays that load after the map is shown to finish.</summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(15);

    private static CancellationTokenSource? _pending;

    /// <summary>Set while something is animating: a blocking collection is a visible stall.</summary>
    internal static volatile bool Hold;

    /// <summary>Collects once, <see cref="SettleDelay"/> after the last call. Call on the UI thread.</summary>
    public static void Schedule()
    {
        _pending?.Cancel();
        _pending?.Dispose();
        var pending = _pending = new CancellationTokenSource();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SettleDelay, pending.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            while (Hold) await Task.Delay(250).ConfigureAwait(false);

            // The first pass queues the bitmaps' finalizers; the second takes what they released.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        });
    }
}
