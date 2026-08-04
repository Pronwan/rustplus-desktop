using NAudio.Wave;

namespace EventSoundProbe;

internal static class Program
{
    private const double WindowSlackSeconds = 1.5;
    private const double MatchIntervalSeconds = 0.5;

    // Noise amplitude used to calibrate thresholds. White noise is a harsher test than
    // gunfire (it covers the whole spectrum instead of being transient and band-limited),
    // so calibrating here leaves headroom for the real thing.
    private const double DefaultCalibrationNoise = 0.15;

    // Floor under every calibrated threshold, measured from real sessions rather than
    // derived. Calibration scores clean clips against white noise, but real game audio
    // produces *structured* peaks, so the live floor sits higher than the synthetic one.
    //
    // Observed so far, all on Oil Rig (the only event with enough samples yet):
    //   in-game confounders   121, 169, 169, 184, 186, 204, 208 ... 331
    //     — identified as mechanical game sounds, garage doors above all: a door motor and
    //       a rig reset share real spectral structure, so this is a systematic confusion
    //       rather than random hash collision, and its upper tail is not yet known
    //   confirmed real resets 622, 1514, and very probably 2156
    //
    // 400 sits in the gap. Note how much narrower that is than the ~11x the white-noise
    // calibration suggested: against the real confounder the margin is closer to 3x.
    //
    // Override with --min-score while gathering more, and revisit once Cargo and Deep Sea
    // have produced in-game numbers of their own.
    private const double DefaultMinScore = 400;

    // Near-miss logging. A threshold that is set safely high turns a real cue that falls
    // short into complete silence — indistinguishable from having heard nothing, and useless
    // for deciding where the threshold actually belongs. Anything above this bar is logged
    // with its score even when it does not qualify as a detection, so a session spent waiting
    // for one event still yields the number.
    private const double DefaultWatchScore = 120;

    private static readonly object ConsoleLock = new();
    private static StreamWriter? _log;

    /// <summary>Console plus, if --log was given, an immediately flushed file.</summary>
    private static void Say(string line, ConsoleColor? colour = null)
    {
        lock (ConsoleLock)
        {
            var prev = Console.ForegroundColor;
            if (colour.HasValue) Console.ForegroundColor = colour.Value;
            Console.WriteLine(line);
            Console.ForegroundColor = prev;
            _log?.WriteLine(line);
        }
    }

    private static int Main(string[] args)
    {
        string mode = args.Length > 0 && !args[0].StartsWith("--") ? args[0].ToLowerInvariant() : "listen";
        double noise = ArgDouble(args, "--noise") ?? DefaultCalibrationNoise;
        string assets = ArgString(args, "--assets") ?? DefaultAssetsPath();

        if (!Directory.Exists(assets))
        {
            Console.Error.WriteLine($"Reference folder not found: {assets}");
            Console.Error.WriteLine("Pass it explicitly with --assets <path>.");
            return 1;
        }

        if (mode == "devices") return ListDevices();
        if (mode is not ("listen" or "selftest" or "matrix")) return Usage();

        string? logPath = ArgString(args, "--log");
        if (logPath != null)
        {
            // AutoFlush matters: a multi-hour run must survive being killed with Ctrl+C.
            _log = new StreamWriter(logPath, append: true) { AutoFlush = true };
            _log.WriteLine($"\n===== session started {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
            Console.WriteLine($"Logging to {logPath}\n");
        }

        Console.WriteLine($"References: {assets}\n");
        var references = LoadReferences(assets);
        if (references.Count == 0) { Console.Error.WriteLine("No .wav references found."); return 1; }

        var excluded = (ArgString(args, "--exclude") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (excluded.Count > 0)
        {
            int before = references.Count;
            references = references.Where(r => !excluded.Contains(r.Group) && !excluded.Contains(r.Name)).ToList();
            Console.WriteLine($"Excluded: {string.Join(", ", excluded)} — {before - references.Count} clips dropped\n");
            if (references.Count == 0) { Console.Error.WriteLine("Everything was excluded."); return 1; }
        }

        if (mode == "matrix") return Matrix(references, noise);

        Calibrate(references, noise, ArgDouble(args, "--min-score") ?? DefaultMinScore);
        DisableHarmfulVetoes(references, noise);

        return mode == "listen"
            ? Listen(references.Where(r => !r.Disabled).ToList(), ArgDouble(args, "--watch-score") ?? DefaultWatchScore)
            : 0;
    }

    // ---------------------------------------------------------------- calibration

    /// <summary>
    /// Derives a per-reference threshold instead of guessing one. Absolute scores scale with
    /// clip length and hash count, so a single global number is meaningless — a 16 s clip
    /// scores an order of magnitude higher than a 5 s one for the same quality of match.
    ///
    /// For each reference we measure how it scores against itself under noise, and how high
    /// clips of *other* event classes score against it. The threshold lands between the two.
    /// </summary>
    private static void Calibrate(List<Fingerprint.Reference> references, double noise, double minScore)
    {
        Console.WriteLine($"Calibrating at noise {noise:F2}, threshold floor {minScore:F0}");
        Console.WriteLine("  cross = other group  -> decides whether we detect the right event at all");
        Console.WriteLine("  twin  = same group   -> decides whether we can tell the CUES apart (open vs despawn, spawn vs horn)\n");
        Console.WriteLine($"{"reference",-40} {"cue",-18} {"len",6} {"self",8} {"cross",8} {"twin",8} {"thresh",7}  verdict");

        var rng = new Random(1);

        foreach (var reference in references)
        {
            var samples = Fingerprint.LoadMono16k(reference.SourcePath);
            if (noise > 0)
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = (float)Math.Clamp(samples[i] + (rng.NextDouble() * 2 - 1) * noise, -1, 1);

            var peaks = Fingerprint.Peaks(samples);
            double self = Fingerprint.Match(reference, peaks).Score;

            double worstCross = 0, worstTwin = 0;
            foreach (var other in references)
            {
                if (other.Name == reference.Name) continue;
                double score = Fingerprint.Match(other, peaks).Score;
                if (other.Group == reference.Group)
                {
                    if (score > worstTwin) worstTwin = score;
                }
                else if (score > worstCross) worstCross = score;
            }

            double floor = Math.Max(worstCross * 1.5, minScore);
            double ceiling = Math.Max(self * 0.6, floor);
            reference.Threshold = Math.Clamp(Math.Sqrt(self * Math.Max(worstCross, 1)), floor, ceiling);

            double detect = worstCross > 0 ? self / worstCross : double.PositiveInfinity;
            double cue = worstTwin > 0 ? self / worstTwin : double.PositiveInfinity;
            string verdict = detect < 1.5 ? "NO DETECT"
                           : !reference.IsTrigger ? "(not a trigger)"
                           : cue < 1.5 ? "detect ok / cue ambiguous"
                           : cue < 3 ? "detect ok / cue weak"
                           : "ok";

            Console.WriteLine($"{reference.Name,-40} {reference.Cue,-18} {reference.Seconds,5:F1}s " +
                              $"{self,8:F1} {worstCross,8:F1} {worstTwin,8:F1} {reference.Threshold,7:F1}  " +
                              $"{detect,5:F1}x/{cue,5:F1}x {verdict}");
        }

        Console.WriteLine();
    }

    /// <summary>
    /// An ambience clip only earns its keep if it wins on its own sound and loses on the
    /// trigger's. One that wins on both would silently swallow every real spawn, which is a
    /// worse failure than the false positive it was meant to prevent — a missed Deep Sea
    /// open is invisible, a spurious one gets filtered by corroboration.
    /// </summary>
    private static void DisableHarmfulVetoes(List<Fingerprint.Reference> references, double noise)
    {
        var rng = new Random(2);

        foreach (var group in references.GroupBy(r => r.Group))
        {
            var trigger = group.FirstOrDefault(r => r.IsTrigger);
            if (trigger == null) continue;

            var samples = Fingerprint.LoadMono16k(trigger.SourcePath);
            if (noise > 0)
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = (float)Math.Clamp(samples[i] + (rng.NextDouble() * 2 - 1) * noise, -1, 1);

            var peaks = Fingerprint.Peaks(samples);
            double triggerScore = Fingerprint.Match(trigger, peaks).Score;

            foreach (var ambience in group.Where(r => !r.IsTrigger))
            {
                double score = Fingerprint.Match(ambience, peaks).Score;
                if (score < triggerScore) continue;

                ambience.Disabled = true;
                Console.WriteLine($"  disabled  {ambience.Name}: scores {score:F0} on {trigger.Cue} " +
                                  $"(vs {triggerScore:F0}) — would suppress the real spawn");
            }
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Full pairwise score matrix. Rows = the audio being played, columns = the reference it
    /// is scored against. Makes it obvious at a glance whether a low margin comes from two
    /// clips genuinely resembling each other, or simply from a weak diagonal.
    /// </summary>
    private static int Matrix(List<Fingerprint.Reference> references, double noise)
    {
        Console.WriteLine($"Pairwise scores at noise {noise:F2}  (row = audio played, column = reference)\n");
        Console.Write($"{"played \\ matched",-36}");
        foreach (var r in references) Console.Write($"{Short(r.Name),9}");
        Console.WriteLine();

        var rng = new Random(1);
        foreach (var row in references)
        {
            var samples = Fingerprint.LoadMono16k(row.SourcePath);
            if (noise > 0)
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = (float)Math.Clamp(samples[i] + (rng.NextDouble() * 2 - 1) * noise, -1, 1);

            var peaks = Fingerprint.Peaks(samples);
            Console.Write($"{Short(row.Name),-36}");
            foreach (var col in references)
                Console.Write($"{Fingerprint.Match(col, peaks).Score,9:F0}");
            Console.WriteLine();
        }
        return 0;
    }

    private static string Short(string name) => name
        .Replace("monument-event-", "")
        .Replace("-01", "")
        .Replace("deepsea-", "ds-");

    // ---------------------------------------------------------------- listen

    /// <summary>
    /// Captures the system mix and matches continuously. Run this while actually playing —
    /// that is the measurement. Every line is a timestamp to compare against what really
    /// happened on the server.
    /// </summary>
    private static int Listen(List<Fingerprint.Reference> references, double watchScore)
    {
        double windowSeconds = references.Max(r => r.Seconds) + WindowSlackSeconds;
        int windowSamples = (int)(windowSeconds * Fingerprint.SampleRate);

        var ring = new float[windowSamples];
        int ringPos = 0;
        bool ringFilled = false;
        var pending = new List<float>(1 << 16);

        using var capture = new WasapiLoopbackCapture();
        int captureRate = capture.WaveFormat.SampleRate;
        int captureChannels = capture.WaveFormat.Channels;

        Console.WriteLine($"Device: {captureRate} Hz, {captureChannels} ch, {capture.WaveFormat.BitsPerSample} bit");
        Console.WriteLine($"Listening (system mix), window {windowSeconds:F1}s. Ctrl+C to stop.\n");

        var lastHit = new Dictionary<string, DateTime>();
        var lastHeartbeat = DateTime.UtcNow;
        double peakLevel = 0;
        double rmsSum = 0;
        long rmsCount = 0;
        int samplesSinceMatch = 0;
        int matchEvery = (int)(MatchIntervalSeconds * Fingerprint.SampleRate);

        capture.DataAvailable += (_, e) =>
        {
            int frames = e.BytesRecorded / 4 / captureChannels;
            pending.Clear();
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < captureChannels; c++)
                    sum += BitConverter.ToSingle(e.Buffer, (f * captureChannels + c) * 4);
                float v = sum / captureChannels;
                pending.Add(v);
                double a = Math.Abs(v);
                if (a > peakLevel) peakLevel = a;
                rmsSum += (double)v * v;
                rmsCount++;
            }

            var resampled = Fingerprint.Resample(pending.ToArray(), captureRate, Fingerprint.SampleRate);
            foreach (float s in resampled)
            {
                ring[ringPos] = s;
                ringPos = (ringPos + 1) % ring.Length;
                if (ringPos == 0) ringFilled = true;
            }

            samplesSinceMatch += resampled.Length;
            if (!ringFilled || samplesSinceMatch < matchEvery) return;
            samplesSinceMatch = 0;

            var window = new float[ring.Length];
            for (int i = 0; i < ring.Length; i++) window[i] = ring[(ringPos + i) % ring.Length];
            var peaks = Fingerprint.Peaks(window);

            // One physical sound must not be reported once per clip, so we dedupe by acoustic
            // group. But we print every member's score: whether the cues inside a group can
            // be told apart in a real mix is exactly what this session is measuring, and a
            // single "winner" would hide it.
            foreach (var group in references.GroupBy(r => r.Group))
            {
                var scored = group
                    .Select(r => (Ref: r, Score: Fingerprint.Match(r, peaks).Score))
                    .OrderByDescending(x => x.Score)
                    .ToList();

                var top = scored[0];
                bool isHit = top.Score >= top.Ref.Threshold;
                if (!isHit && top.Score < watchScore) continue;

                // Suppress for at least the whole ring buffer, not just the clip length. A
                // sound stays inside the analysis window for `windowSeconds`, so a shorter
                // hold lets the *same* occurrence fire again the moment it expires — the
                // first session showed Excavator re-reporting every 10.5s, exactly its clip
                // length, while one playback was still in the buffer.
                if (lastHit.TryGetValue(top.Ref.Group, out var when) &&
                    (DateTime.UtcNow - when).TotalSeconds < windowSeconds + WindowSlackSeconds)
                    continue;

                // Only a real detection takes the suppression slot. A near miss must not:
                // a cue enters the ring buffer weakly before it is fully inside, so its
                // leading edge scores below threshold roughly 19s ahead of the peak. With
                // the hold at 18.3s both confirmed detections of the first night — Deep Sea
                // 03:11:47/03:12:06 and Cargo 03:56:58/03:57:17 — cleared it by under a
                // second. A slightly longer buffer would have swallowed the real hit.
                if (isHit) lastHit[top.Ref.Group] = DateTime.UtcNow;

                string detail = string.Join("  ", scored.Select(x =>
                    $"{x.Ref.Cue}={x.Score}{(x.Score >= x.Ref.Threshold ? "*" : "")}"));

                // Ambience beat the spawn cue — the event is already running, not starting,
                // so no spawn is reported. Logged anyway: while measuring, "heard the horn
                // and correctly stayed quiet" is a result, and staying silent would be
                // indistinguishable from having heard nothing at all.
                if (!top.Ref.IsTrigger)
                {
                    Say($"[{DateTime.Now:HH:mm:ss}] {top.Ref.Group,-10} .. {top.Ref.Cue,-18} {detail}" +
                        "   (ambience — no spawn reported)", ConsoleColor.DarkGray);
                    continue;
                }

                // Below threshold but worth recording: this is the number that decides where
                // the threshold should sit, and it only exists if we log the miss.
                if (!isHit)
                {
                    Say($"[{DateTime.Now:HH:mm:ss}] {top.Ref.Group,-10} ?? {top.Ref.Cue,-18} {detail}" +
                        $"   (near miss — needed {top.Ref.Threshold:F0})", ConsoleColor.DarkCyan);
                    continue;
                }

                bool tooClose = scored.Count > 1 && scored[0].Score < scored[1].Score * 2;
                Say($"[{DateTime.Now:HH:mm:ss}] {top.Ref.Group,-10} -> {top.Ref.Cue,-18} {detail}" +
                    (tooClose ? "   <- cues close, compare against ground truth" : ""),
                    tooClose ? ConsoleColor.Yellow : ConsoleColor.Green);
            }
        };

        capture.StartRecording();

        while (true)
        {
            Thread.Sleep(1000);
            if ((DateTime.UtcNow - lastHeartbeat).TotalSeconds < 15) continue;
            lastHeartbeat = DateTime.UtcNow;
            double rms = rmsCount > 0 ? Math.Sqrt(rmsSum / rmsCount) : 0;
            Say($"[{DateTime.Now:HH:mm:ss}] listening… peak {peakLevel:F3}  rms {rms:F4}" +
                (rms < Fingerprint.MinFrameRms ? "  (below gate — nothing is being analysed)" : ""));
            peakLevel = 0;
            rmsSum = 0;
            rmsCount = 0;
        }
    }

    // ---------------------------------------------------------------- helpers

    private static int ListDevices()
    {
        using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(
                     NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active))
            Console.WriteLine($"  {device.FriendlyName}");
        return 0;
    }

    private static int Usage()
    {
        Console.WriteLine("""
            Usage: EventSoundProbe <mode> [options]

              listen                 calibrate, then capture the system mix and log events (default)
              selftest               calibrate and print the table only
              devices                list active render devices

            Options:
              --assets <path>        folder holding the reference .wav files
              --noise <0..1>         noise amplitude used for calibration (default 0.15)
              --min-score <n>        floor under every threshold (default 400, measured)
              --exclude <names>      comma-separated groups or clip names to drop
              --watch-score <n>      log sub-threshold hits above this, with their score (default 120)
              --log <file>           append everything to a file as well (flushed immediately)
            """);
        return 1;
    }

    private static List<Fingerprint.Reference> LoadReferences(string folder)
    {
        var list = new List<Fingerprint.Reference>();
        foreach (string path in Directory.EnumerateFiles(folder, "*.wav").OrderBy(p => p))
        {
            try { list.Add(Fingerprint.BuildReference(path)); }
            catch (Exception ex) { Console.Error.WriteLine($"  skipped {Path.GetFileName(path)}: {ex.Message}"); }
        }
        return list;
    }

    private static string DefaultAssetsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "RustPlusDesktop", "Assets", "audio-alerts");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "audio-alerts");
    }

    private static string? ArgString(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static double? ArgDouble(string[] args, string name) =>
        double.TryParse(ArgString(args, name), System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : null;
}
