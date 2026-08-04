using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NAudio.Wave;

namespace RustPlusDesk.Services.Audio;

/// <summary>
/// Landmark ("Shazam"-style) fingerprinting for Rust's server-wide monument cues.
///
/// Plain spectral correlation is the wrong tool: it compares whole frames and falls apart
/// once the cue is mixed with gunfire and music, which is exactly the situation that has to
/// work. Landmark hashing keys only on local spectral peaks and the time offsets between
/// them, so unrelated loud sounds add peaks without removing ours.
///
/// Every constant below was measured against real gameplay over one night, not guessed.
/// Changing any of them without re-running that measurement is very likely to make detection
/// worse — the tuning is not intuitive, and two plausible "improvements" were tried and
/// reverted (see the notes on BandEdges and on scoring in Match).
/// </summary>
internal static class EventSoundFingerprint
{
    public const int SampleRate = 16000;
    public const int FftSize = 1024;      // 64 ms window
    public const int HopSize = 256;       // 16 ms hop

    // Six bands, measured. Widening to fourteen was tried on the theory that more distinct
    // peak frequencies would reduce random hash collisions; it does the opposite. More bands
    // means more peaks and more hashes, and the collision floor grows faster than the signal
    // — separation dropped from ~23x to ~11x across the board.
    private static readonly int[] BandEdges = { 4, 12, 28, 60, 124, 256, 512 };
    private const double PeakThresholdFactor = 1.6;

    // Absolute energy gate, and it is not optional. Peak picking is purely relative — a bin
    // only has to stand out from its own frame — so near-silence still yields a full
    // constellation of peaks drawn from dither, and enough of those align by chance to clear
    // the match threshold. Without this, an idle machine produced six false detections in
    // under two minutes across three different events.
    //
    // 0.002 is measured: real gameplay reported RMS between 0.002 and 0.02, true idle sat at
    // 0.0004. An earlier guess of 0.005 fell inside the gameplay range and threw away quieter
    // stretches of real play, including one confirmed Oil Rig cue that arrived at RMS 0.0033.
    public const double MinFrameRms = 0.002;

    private const int MinDeltaFrames = 1;
    private const int MaxDeltaFrames = 48;   // ~0.77 s
    private const int MaxPairsPerAnchor = 6;

    public sealed class Reference
    {
        public required string Name { get; init; }
        public required string EventType { get; init; }
        public required Dictionary<uint, List<int>> Hashes { get; init; }
        public required double Seconds { get; init; }
        public double Threshold { get; set; }

        /// <summary>
        /// Only the "monument-event-*" cues announce that something happened. The horns and
        /// the Deep Sea wipe alarm are ambience of an already-running event and must never
        /// raise one — hearing the cargo horn at a harbour is not a cargo spawn.
        /// </summary>
        public bool IsTrigger => Name.StartsWith("monument", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Maps a clip to the event it belongs to. Clips of one event may carry different
    /// meanings — Deep Sea open versus its wipe alarm, cargo spawn versus cargo horn — and
    /// those are deliberately NOT separated here: measurement showed they cannot be told
    /// apart acoustically (Deep Sea open scored 1061 against its own reference and 1092
    /// against the alarm). Which meaning applies is decided by event state, never by audio.
    /// </summary>
    public static string EventTypeOf(string fileName)
    {
        string n = fileName.ToLowerInvariant();
        if (n.Contains("deep-sea") || n.Contains("deepsea")) return "deep-sea";
        if (n.Contains("oil-rig") || n.Contains("oilrig")) return "oil-rig";
        if (n.Contains("excavator")) return "excavator";
        if (n.Contains("cargo") || n.Contains("horn")) return "cargo";
        return n;
    }

    // ---------------------------------------------------------------- loading

    /// <summary>Loads every embedded reference cue as mono 16 kHz float samples.</summary>
    public static IEnumerable<(string Name, float[] Samples)> LoadEmbeddedReferences()
    {
        Assembly assembly = typeof(EventSoundFingerprint).Assembly;

        // MSBuild rewrites the folder name into the resource id and replaces the dash:
        // "Assets\audio-alerts\x.wav" becomes "RustPlusDesk.Assets.audio_alerts.x.wav".
        // Matching only the on-disk spelling finds nothing, and it fails silently — the
        // listener would simply report no reference cues. Accept both spellings.
        foreach (string resource in assembly.GetManifestResourceNames()
                     .Where(n => n.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                              && (n.Contains("audio_alerts", StringComparison.OrdinalIgnoreCase)
                               || n.Contains("audio-alerts", StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(n => n))
        {
            float[] samples;
            try
            {
                using Stream? stream = assembly.GetManifestResourceStream(resource);
                if (stream == null) continue;
                samples = ReadMono16k(stream);
            }
            catch
            {
                continue;   // a broken reference must not take the whole listener down
            }

            // "RustPlusDesk.Assets.audio_alerts.monument-event-cargo-ship-spawn-01.wav"
            // -> "monument-event-cargo-ship-spawn-01". Cue file names carry no dots of their
            // own, so the last separator before the extension is the folder boundary.
            string name = resource[..^4];
            int lastDot = name.LastIndexOf('.');
            if (lastDot >= 0) name = name[(lastDot + 1)..];

            yield return (name, samples);
        }
    }

    public static float[] ReadMono16k(Stream stream)
    {
        using var reader = new WaveFileReader(stream);
        var provider = reader.ToSampleProvider();
        int channels = provider.WaveFormat.Channels;
        int rate = provider.WaveFormat.SampleRate;

        var raw = new List<float>(1 << 18);
        var buffer = new float[16384];
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
            for (int i = 0; i < read; i++) raw.Add(buffer[i]);

        int frames = channels > 0 ? raw.Count / channels : 0;
        var mono = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++) sum += raw[f * channels + c];
            mono[f] = sum / channels;
        }

        return rate == SampleRate ? mono : Resample(mono, rate, SampleRate);
    }

    /// <summary>
    /// Downsampler with anti-aliasing.
    ///
    /// Linear interpolation alone is far too weak a low-pass: capture devices report rates up
    /// to 96 kHz, so decimating to 16 kHz folds everything between 8 and 48 kHz straight back
    /// into the band the peak picker works on. Worse, it does so differently for the live
    /// stream than for the reference clips, which is exactly the asymmetry that breaks
    /// matching. The box filter is crude but real attenuation and costs one pass.
    /// </summary>
    public static float[] Resample(float[] input, int fromRate, int toRate)
    {
        if (input.Length == 0 || fromRate == toRate) return input;

        double ratio = (double)fromRate / toRate;

        if (ratio > 1.0)
        {
            int window = (int)Math.Round(ratio);
            if (window > 1)
            {
                var smoothed = new float[input.Length];
                double sum = 0;
                for (int i = 0; i < input.Length; i++)
                {
                    sum += input[i];
                    if (i >= window) sum -= input[i - window];
                    smoothed[i] = (float)(sum / Math.Min(i + 1, window));
                }
                input = smoothed;
            }
        }

        int outLength = (int)(input.Length / ratio);
        var output = new float[Math.Max(outLength, 0)];
        for (int i = 0; i < output.Length; i++)
        {
            double pos = i * ratio;
            int i0 = (int)pos;
            int i1 = Math.Min(i0 + 1, input.Length - 1);
            double frac = pos - i0;
            output[i] = (float)(input[i0] * (1 - frac) + input[i1] * frac);
        }
        return output;
    }

    // ---------------------------------------------------------------- analysis

    public static List<(int T, int F)> Peaks(float[] samples)
    {
        var peaks = new List<(int, int)>();
        if (samples.Length < FftSize) return peaks;

        var window = new double[FftSize];
        for (int i = 0; i < FftSize; i++)
            window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1));

        var re = new double[FftSize];
        var im = new double[FftSize];
        var mag = new double[FftSize / 2];

        int frameCount = 1 + (samples.Length - FftSize) / HopSize;
        for (int frame = 0; frame < frameCount; frame++)
        {
            int start = frame * HopSize;

            double energy = 0;
            for (int i = 0; i < FftSize; i++)
            {
                double s = samples[start + i];
                energy += s * s;
            }
            if (Math.Sqrt(energy / FftSize) < MinFrameRms) continue;

            for (int i = 0; i < FftSize; i++)
            {
                re[i] = samples[start + i] * window[i];
                im[i] = 0;
            }
            Fft(re, im);

            double sum = 0;
            for (int i = 0; i < mag.Length; i++)
            {
                mag[i] = Math.Sqrt(re[i] * re[i] + im[i] * im[i]);
                sum += mag[i];
            }

            double mean = sum / mag.Length;
            if (mean <= 1e-9) continue;

            for (int b = 0; b + 1 < BandEdges.Length; b++)
            {
                int lo = BandEdges[b], hi = Math.Min(BandEdges[b + 1], mag.Length);
                int best = -1;
                double bestMag = 0;
                for (int i = lo; i < hi; i++)
                    if (mag[i] > bestMag) { bestMag = mag[i]; best = i; }

                if (best >= 0 && bestMag > mean * PeakThresholdFactor)
                    peaks.Add((frame, best));
            }
        }
        return peaks;
    }

    public static Dictionary<uint, List<int>> Hashes(List<(int T, int F)> peaks)
    {
        var map = new Dictionary<uint, List<int>>();
        for (int i = 0; i < peaks.Count; i++)
        {
            var (t1, f1) = peaks[i];
            int paired = 0;
            for (int j = i + 1; j < peaks.Count && paired < MaxPairsPerAnchor; j++)
            {
                var (t2, f2) = peaks[j];
                int dt = t2 - t1;
                if (dt < MinDeltaFrames) continue;
                if (dt > MaxDeltaFrames) break;

                uint hash = (uint)((f1 & 0x3FF) << 20 | (f2 & 0x3FF) << 10 | (dt & 0x3FF));
                if (!map.TryGetValue(hash, out var list)) map[hash] = list = new List<int>();
                list.Add(t1);
                paired++;
            }
        }
        return map;
    }

    // ---------------------------------------------------------------- matching

    /// <summary>
    /// Scores a live window against a reference. A real match shows up as many hashes
    /// agreeing on the same time offset, while random collisions scatter across offsets.
    ///
    /// The score is the raw height of that peak. Normalising it against the histogram's own
    /// background was tried and is measurably worse — it collapsed the separation between a
    /// clip and itself from ~23x to ~1.3x, because a genuine match also fills neighbouring
    /// offsets, so the "background" rises along with the peak.
    /// </summary>
    public static double Match(Reference reference, List<(int T, int F)> livePeaks)
    {
        var live = Hashes(livePeaks);
        var histogram = new Dictionary<int, int>();

        foreach (var (hash, liveTimes) in live)
        {
            if (!reference.Hashes.TryGetValue(hash, out var refTimes)) continue;
            foreach (int rt in refTimes)
                foreach (int lt in liveTimes)
                {
                    int offset = lt - rt;
                    histogram[offset] = histogram.GetValueOrDefault(offset) + 1;
                }
        }

        int peak = 0;
        foreach (int count in histogram.Values)
            if (count > peak) peak = count;
        return peak;
    }

    // ---------------------------------------------------------------- fft

    /// <summary>In-place iterative radix-2 FFT. Length must be a power of two.</summary>
    private static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            double wRe = Math.Cos(ang), wIm = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double curRe = 1, curIm = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    double tRe = re[b] * curRe - im[b] * curIm;
                    double tIm = re[b] * curIm + im[b] * curRe;
                    re[b] = re[a] - tRe; im[b] = im[a] - tIm;
                    re[a] += tRe;        im[a] += tIm;
                    double nextRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                }
            }
        }
    }
}
