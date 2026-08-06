using NAudio.Wave;

namespace EventSoundProbe;

/// <summary>
/// Landmark ("Shazam"-style) audio fingerprinting.
///
/// Plain spectral correlation is the wrong tool here: it compares whole frames, so it falls
/// apart as soon as the target sound is mixed with gunfire and music at varying levels —
/// which is exactly the situation we need to survive. Landmark hashing instead keys only on
/// local spectral peaks and the time offsets between them. Loud unrelated sounds add extra
/// peaks but do not remove the ones we are looking for, so the match degrades gracefully
/// instead of collapsing.
/// </summary>
public static class Fingerprint
{
    public const int SampleRate = 16000;   // event cues are low/mid frequency — 8 kHz Nyquist is plenty
    public const int FftSize = 1024;       // 64 ms window
    public const int HopSize = 256;        // 16 ms hop

    // Peak picking: split each frame into logarithmic bands and keep the strongest bin per
    // band, provided it stands out from the frame's own average (level independence).
    // Six bands, measured. Widening to fourteen was tried on the theory that more distinct
    // peak frequencies would reduce random hash collisions; it does the opposite. More bands
    // means more peaks, more hashes, and the collision floor grows faster than the signal —
    // separation dropped from ~23x to ~11x across the board. Do not "improve" this without
    // re-running `selftest` at several noise levels.
    private static readonly int[] BandEdges = { 4, 12, 28, 60, 124, 256, 512 };
    private const double PeakThresholdFactor = 1.6;

    // Absolute energy gate, and it is not optional. Peak picking is purely relative — a bin
    // only has to stand out from its own frame — so near-silence still yields a full
    // constellation of peaks drawn from dither, and enough of those align by chance to clear
    // the match threshold. The first unattended run produced six false spawns in under two
    // minutes at peak levels around 0.002, across three different events. Real cues are loud
    // server-wide announcements; a frame this quiet cannot contain one.
    //
    // Value measured, not guessed. A 12-minute session of actual Rust gameplay reported
    // RMS between 0.002 and 0.02, while true idle sat at 0.0004. The first attempt at 0.005
    // therefore fell inside the gameplay range and threw away quieter stretches of real play.
    // 0.002 is five times above idle and below everything the game produced.
    public const double MinFrameRms = 0.002;

    // Hash pairing: an anchor peak is paired with later peaks inside a target zone.
    private const int MinDeltaFrames = 1;
    private const int MaxDeltaFrames = 48;   // ~0.77 s
    private const int MaxPairsPerAnchor = 6;

    public sealed record Reference(
        string Name,
        string Group,
        string Cue,
        string SourcePath,
        Dictionary<uint, List<int>> Hashes,
        int FrameCount,
        double Seconds)
    {
        /// <summary>Score needed to count as a hit. Filled in by calibration, not guessed.</summary>
        public double Threshold { get; set; }

        /// <summary>
        /// Only the "monument-event-*" clips announce that something spawned. The horns and
        /// the Deep Sea wipe alarm are ambience of an already-running event and must never
        /// raise one.
        /// </summary>
        public bool IsTrigger => Name.StartsWith("monument", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Ambience clips earn their place by acting as a veto: hearing the cargo horn must
        /// not raise a cargo spawn. That only works if the ambience actually outscores the
        /// trigger on its own sound *and* loses on the trigger's sound. The Deep Sea wipe
        /// alarm fails the second half — it outscores the open cue even when the open cue is
        /// what is playing — so it would suppress every real Deep Sea open. Calibration
        /// detects that and switches the clip off instead of letting it do damage.
        /// </summary>
        public bool Disabled { get; set; }
    }

    /// <summary>
    /// Acoustic grouping only — clips that sound alike, NOT clips that mean the same thing.
    /// The two Deep Sea clips score 7752 against each other with no noise at all, and the
    /// cargo clips do the same, but they carry opposite meanings (open vs. despawn, spawn
    /// vs. horn). Grouping is purely so one physical sound is not reported several times;
    /// what it *means* is decided by <see cref="CueOf"/> plus event state, never by the
    /// fingerprint alone.
    /// </summary>
    public static string GroupOf(string fileName)
    {
        string n = fileName.ToLowerInvariant();
        if (n.Contains("deep-sea") || n.Contains("deepsea")) return "deep-sea";
        if (n.Contains("oil-rig") || n.Contains("oilrig")) return "oil-rig";
        if (n.Contains("excavator")) return "excavator";
        if (n.Contains("cargo") || n.Contains("horn")) return "cargo";
        return n;
    }

    /// <summary>
    /// What the clip actually signals. Clips inside one group differ here — that is the
    /// whole problem, and it is resolved by state, not by audio: a Deep Sea cue can only be
    /// "open" while Deep Sea is closed, and the despawn alarm is only audible to someone
    /// inside the zone anyway. Likewise a cargo cue is the spawn while no cargo is active
    /// and the horn while one is.
    /// </summary>
    public static string CueOf(string fileName)
    {
        string n = fileName.ToLowerInvariant();
        if (n.Contains("deep-sea-open") || n.Contains("deepsea-open")) return "deep-sea-open";
        if (n.Contains("wipe-alarm")) return "deep-sea-despawn";
        if (n.Contains("cargo-ship-spawn")) return "cargo-spawn";
        if (n.Contains("horn")) return "cargo-horn";
        if (n.Contains("excavator")) return "excavator-start";
        if (n.Contains("oil-rig-reset")) return "oil-rig-reset";
        return n;
    }

    // ---------------------------------------------------------------- loading

    /// <summary>Reads any WAV the tool ships with and returns mono 16 kHz float samples.</summary>
    public static float[] LoadMono16k(string path)
    {
        using var reader = new AudioFileReader(path);
        var sp = reader.ToSampleProvider();
        int channels = sp.WaveFormat.Channels;
        int rate = sp.WaveFormat.SampleRate;

        var raw = new List<float>(1 << 20);
        var buf = new float[16384];
        int read;
        while ((read = sp.Read(buf, 0, buf.Length)) > 0)
            for (int i = 0; i < read; i++) raw.Add(buf[i]);

        // interleaved -> mono
        int frames = raw.Count / channels;
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
    /// Linear interpolation alone is a far too weak low-pass: capture devices report rates up
    /// to 96 kHz, so decimating to 16 kHz folds everything between 8 and 48 kHz straight back
    /// into the band the peak picker works on. That injects spurious peaks, and worse, it does
    /// so differently for the live stream (96 kHz) than for the reference clips (44.1/48 kHz),
    /// which is exactly the asymmetry that breaks matching.
    ///
    /// A box filter over the decimation window is crude but real attenuation, and it costs one
    /// pass. Applied before the interpolation so both sides land in a comparable band.
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

    /// <summary>STFT + per-band peak picking. Returns (frameIndex, freqBin) pairs.</summary>
    public static List<(int T, int F)> Peaks(float[] samples)
    {
        var peaks = new List<(int, int)>();
        if (samples.Length < FftSize) return peaks;

        var window = new double[FftSize];
        for (int i = 0; i < FftSize; i++)                       // Hann
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
            if (Math.Sqrt(energy / FftSize) < MinFrameRms) continue;   // too quiet to carry a cue

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
            if (mean <= 1e-9) continue;                          // silence

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

    /// <summary>Pairs peaks into (f1, f2, dt) hashes keyed by the anchor's frame index.</summary>
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
                if (dt > MaxDeltaFrames) break;                  // peaks are time-ordered

                uint hash = (uint)((f1 & 0x3FF) << 20 | (f2 & 0x3FF) << 10 | (dt & 0x3FF));
                if (!map.TryGetValue(hash, out var list)) map[hash] = list = new List<int>();
                list.Add(t1);
                paired++;
            }
        }
        return map;
    }

    public static Reference BuildReference(string path)
    {
        var samples = LoadMono16k(path);
        var peaks = Peaks(samples);
        int frames = samples.Length < FftSize ? 0 : 1 + (samples.Length - FftSize) / HopSize;
        string name = Path.GetFileNameWithoutExtension(path);
        return new Reference(
            name,
            GroupOf(name),
            CueOf(name),
            path,
            Hashes(peaks),
            frames,
            samples.Length / (double)SampleRate);
    }

    // ---------------------------------------------------------------- matching

    /// <summary>
    /// Scores a live window against a reference. A real match shows up as many hashes
    /// agreeing on the *same* time offset, while random collisions scatter evenly across
    /// offsets.
    ///
    /// The score is the raw height of that peak. Normalising it against the histogram's own
    /// background was tried and is measurably worse — it collapsed the separation between a
    /// clip and itself from ~23x down to ~1.3x, because a genuine match also fills many
    /// neighbouring offsets, so the "background" rises along with the peak. The raw peak,
    /// combined with a per-reference threshold from calibration, discriminates far better.
    /// </summary>
    public static (double Score, int Peak, int TotalHits) Match(Reference reference, List<(int T, int F)> livePeaks)
    {
        var live = Hashes(livePeaks);
        var histogram = new Dictionary<int, int>();
        int totalHits = 0;

        foreach (var (hash, liveTimes) in live)
        {
            if (!reference.Hashes.TryGetValue(hash, out var refTimes)) continue;
            foreach (int rt in refTimes)
                foreach (int lt in liveTimes)
                {
                    int offset = lt - rt;
                    histogram[offset] = histogram.GetValueOrDefault(offset) + 1;
                    totalHits++;
                }
        }

        if (histogram.Count == 0) return (0, 0, 0);

        int peak = 0;
        foreach (int count in histogram.Values)
            if (count > peak) peak = count;

        return (peak, peak, totalHits);
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
