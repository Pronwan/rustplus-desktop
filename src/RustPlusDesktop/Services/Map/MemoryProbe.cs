using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using RustPlusDesk.Services.Data;

namespace RustPlusDesk.Services.Map;

/// <summary>
/// Writes where the app's memory sits at a few moments around loading a map, to
/// %APPDATA%\RustPlusDesk\memory-log.txt.
///
/// The split is the point: the managed heap (and its large-object part, where every
/// full-size bitmap buffer lands) against what the process holds in total, most of the
/// rest being decoded images and WPF's own copies of them.
///
/// Debug builds only: the calls compile away in Release, so nobody else's disk collects a log.
/// </summary>
internal static class MemoryProbe
{
    private static readonly object Gate = new();
    private static string LogPath => Path.Combine(DataManager.AppDir, "memory-log.txt");

    [Conditional("DEBUG")]
    public static void Record(string stage)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var gc = GC.GetGCMemoryInfo();
            long loh = gc.GenerationInfo.Length > 3 ? gc.GenerationInfo[3].SizeAfterBytes : 0;

            string line = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss} | {1,-40} | working set {2,5} MB | private {3,5} MB | managed heap {4,5} MB (LOH {5,4} MB, committed {6,5} MB, fragmented {7,4} MB)",
                DateTime.Now, stage,
                Mb(process.WorkingSet64), Mb(process.PrivateMemorySize64),
                Mb(gc.HeapSizeBytes), Mb(loh), Mb(gc.TotalCommittedBytes), Mb(gc.FragmentedBytes));

            lock (Gate) File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch
        {
            // A diagnostic that fails is not worth an error of its own.
        }
    }

    /// <summary>Records now, and again once the load has settled.</summary>
    [Conditional("DEBUG")]
    public static void RecordAndFollowUp(string stage)
    {
        Record(stage);
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            Record(stage + " +20s");
            await Task.Delay(TimeSpan.FromSeconds(40)).ConfigureAwait(false);
            Record(stage + " +60s");
        });
    }

    private static long Mb(long bytes) => bytes / (1024 * 1024);
}
