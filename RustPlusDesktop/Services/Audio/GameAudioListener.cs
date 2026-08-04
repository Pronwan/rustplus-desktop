using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NAudio.Wave;

namespace RustPlusDesk.Services.Audio;

/// <param name="CueStartedAtUtc">
/// When the sound itself began, derived from where the match sits in the analysis window —
/// not when this client happened to notice it.
///
/// This is what lets the backend tell corroboration from a second occurrence. Two clients
/// hearing the same cue compute nearly the same start time however far apart they cross their
/// detection thresholds; two different cues eight seconds apart compute start times eight
/// seconds apart. Arrival time cannot separate those cases, because the spread between clients
/// and the gap between cues are the same order of magnitude.
/// </param>
public sealed record GameAudioDetection(
    string EventType, double Score, DateTime DetectedAtUtc, string CaptureMode, DateTime CueStartedAtUtc);

/// <summary>
/// Listens for Rust's server-wide monument cues and raises <see cref="Detected"/> when one is
/// heard. Facepunch stopped sending vending and dynamic event markers over Rust+, so for those
/// servers the audio is the only remaining signal that Cargo, Deep Sea or an Oil Rig crate is
/// up.
///
/// Prefers capturing RustClient.exe directly (see <see cref="ProcessLoopbackCapture"/>) and
/// falls back to the system mix where Windows is too old for it. The difference is not
/// detection quality — Rust's own mix is just as full of gunfire — but provenance: a
/// per-process client cannot pick a cue up from a Rust video playing in another window. That is
/// why the report carries <see cref="CaptureMode"/> and the backend accepts a process client's
/// word alone while a system-mix client needs corroboration.
/// </summary>
public sealed class GameAudioListener : IDisposable
{
    public static GameAudioListener Instance { get; } = new();

    /// <summary>Raised on the capture thread. Marshal to the UI yourself.</summary>
    public event Action<GameAudioDetection>? Detected;

    public bool IsRunning { get; private set; }

    /// <summary>
    /// Reported to the backend so it can weigh the source. A "process" client cannot pick a
    /// cue up from a video in another window, so the backend accepts its report on its own;
    /// a "system" client needs corroboration.
    /// </summary>
    public string CaptureMode { get; private set; } = "system";

    private const string GameProcessName = "RustClient";
    private const double MatchIntervalSeconds = 0.5;
    private const double WindowSlackSeconds = 1.5;

    // Telling one occurrence from the next cannot be done with a timer.
    //
    // A cue stays matchable for as long as it sits in the ring buffer — up to ~25 seconds after
    // it began. But on a server where both Oil Rigs respawned together, the second announcement
    // starts as soon as the first has finished, roughly 8-10 seconds in. The two intervals
    // overlap, so any hold long enough to swallow the echo also swallows a real second cue.
    //
    // The match offset separates them. It says where inside the analysis window the cue sits,
    // and as the buffer advances the same sound's offset shrinks by exactly the elapsed time.
    // A new sound instead shows up at a fresh offset near the end of the window. Predicting
    // where the previous occurrence *would* now be and comparing is therefore exact, where a
    // timer can only guess.
    private const double SameOccurrenceMaxAgeSeconds = 30;   // beyond this the cue has left the buffer
    private const int SameOccurrenceToleranceFrames = 30;    // ~0.5 s; two cues are ~500 frames apart

    /// <summary>Guards against re-firing inside a single match interval. Far below the ~8 s
    /// that separates two back-to-back announcements, so it cannot hide a real one.</summary>
    private const double MinReportIntervalSeconds = 3;

    // Floor under every calibrated threshold, measured rather than derived. Calibration scores
    // clean clips against white noise, but real game audio produces *structured* peaks, so the
    // live collision floor sits higher than the synthetic one. Over one night every false
    // positive landed between 208 and 331 — traced to mechanical game sounds, garage doors
    // above all — while confirmed in-game cues scored 423, 515, 622, 962, 1017, 1514 and 1698.
    private const double MinScore = 400;

    // Measured exclusions:
    //   excavator   — only clip in its event, weakest margin, and it produced five of the
    //                 eight false positives observed before thresholds were raised
    //   horn_disant — the distant cargo horn is never played in-game, and carrying it only
    //                 added a collision source (dropping it lifted Deep Sea from 11.7x to
    //                 23.9x and Oil Rig from 7.2x to 10.9x)
    private static readonly HashSet<string> ExcludedEvents = new(StringComparer.OrdinalIgnoreCase) { "excavator" };
    private static readonly HashSet<string> ExcludedClips = new(StringComparer.OrdinalIgnoreCase) { "horn_disant" };

    private readonly object _gate = new();
    /// <summary>Last reported occurrence per event: when it fired and where in the analysis
    /// window it sat, which is what distinguishes an echo from a genuine second cue.</summary>
    private readonly Dictionary<string, (DateTime At, int Offset)> _lastHit = new(StringComparer.OrdinalIgnoreCase);

    private List<EventSoundFingerprint.Reference>? _references;
    private WasapiLoopbackCapture? _capture;
    private ProcessLoopbackCapture? _processCapture;
    private Timer? _processWatchdog;

    private float[] _ring = Array.Empty<float>();
    private int _ringPos;
    private bool _ringFilled;
    private double _windowSeconds;
    private int _samplesSinceMatch;
    private bool _disposed;

    private GameAudioListener() { }

    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning || _disposed) return;
            IsRunning = true;
        }

        // Calibration reads and fingerprints every reference — a few seconds of work that
        // must not sit on the UI thread at startup.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                EnsureCalibrated();
                _processWatchdog = new Timer(_ => SyncCaptureWithGame(), null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                Log($"[audio] Listener could not start: {ex.Message}");
                lock (_gate) IsRunning = false;
            }
        });
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!IsRunning) return;
            IsRunning = false;
        }

        _processWatchdog?.Dispose();
        _processWatchdog = null;
        StopCapture();
        Log("[audio] Listener stopped.");
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }

    // ---------------------------------------------------------------- calibration

    /// <summary>
    /// Derives a threshold per reference instead of guessing one. Raw scores scale with clip
    /// length and hash count, so a single global number is meaningless — a 16 s clip scores an
    /// order of magnitude above a 5 s one for the same match quality.
    /// </summary>
    private void EnsureCalibrated()
    {
        if (_references != null) return;

        var loaded = new List<(string Name, float[] Samples, EventSoundFingerprint.Reference Ref)>();

        foreach (var (name, samples) in EventSoundFingerprint.LoadEmbeddedReferences())
        {
            string eventType = EventSoundFingerprint.EventTypeOf(name);
            if (ExcludedEvents.Contains(eventType) || ExcludedClips.Contains(name)) continue;

            var reference = new EventSoundFingerprint.Reference
            {
                Name = name,
                EventType = eventType,
                Hashes = EventSoundFingerprint.Hashes(EventSoundFingerprint.Peaks(samples)),
                Seconds = samples.Length / (double)EventSoundFingerprint.SampleRate,
            };
            loaded.Add((name, samples, reference));
        }

        if (loaded.Count == 0)
        {
            Log("[audio] No reference cues embedded — listener disabled.");
            _references = new List<EventSoundFingerprint.Reference>();
            return;
        }

        var rng = new Random(1);
        foreach (var (_, samples, reference) in loaded)
        {
            var noisy = new float[samples.Length];
            for (int i = 0; i < samples.Length; i++)
                noisy[i] = (float)Math.Clamp(samples[i] + (rng.NextDouble() * 2 - 1) * 0.15, -1, 1);

            var peaks = EventSoundFingerprint.Peaks(noisy);
            double self = EventSoundFingerprint.Match(reference, peaks).Score;

            double worstCross = 0;
            foreach (var (_, _, other) in loaded)
            {
                if (other.EventType == reference.EventType) continue;
                worstCross = Math.Max(worstCross, EventSoundFingerprint.Match(other, peaks).Score);
            }

            double floor = Math.Max(worstCross * 1.5, MinScore);
            double ceiling = Math.Max(self * 0.6, floor);
            reference.Threshold = Math.Clamp(Math.Sqrt(self * Math.Max(worstCross, 1)), floor, ceiling);
        }

        DropHarmfulAmbience(loaded);

        // A cue that is still entering the ring buffer scores below threshold roughly 19 s
        // ahead of its peak, so suppression must cover the whole buffer — a shorter hold lets
        // the same occurrence fire twice.
        _references = loaded.Select(l => l.Ref).ToList();
        _windowSeconds = _references.Max(r => r.Seconds) + WindowSlackSeconds;

        Log($"[audio] Listening for {string.Join(", ", _references.Select(r => r.EventType).Distinct())} " +
            $"({_references.Count} reference cues).");
    }

    /// <summary>
    /// An ambience clip earns its place by acting as a veto — hearing the cargo horn must not
    /// raise a cargo spawn. That only works if it wins on its own sound AND loses on the
    /// trigger's. The Deep Sea wipe alarm fails the second half: measured against a real open
    /// cue it scores 1067 to the open clip's 1058, so keeping it armed would silently swallow
    /// every genuine Deep Sea detection. A missed spawn is invisible; a spurious one gets
    /// filtered by corroboration. So when an ambience clip cannot lose, it is dropped.
    /// </summary>
    private static void DropHarmfulAmbience(List<(string Name, float[] Samples, EventSoundFingerprint.Reference Ref)> loaded)
    {
        var rng = new Random(2);

        foreach (var group in loaded.GroupBy(l => l.Ref.EventType).ToList())
        {
            var trigger = group.FirstOrDefault(l => l.Ref.IsTrigger);
            if (trigger.Ref == null) continue;

            var noisy = new float[trigger.Samples.Length];
            for (int i = 0; i < noisy.Length; i++)
                noisy[i] = (float)Math.Clamp(trigger.Samples[i] + (rng.NextDouble() * 2 - 1) * 0.15, -1, 1);

            var peaks = EventSoundFingerprint.Peaks(noisy);
            double triggerScore = EventSoundFingerprint.Match(trigger.Ref, peaks).Score;

            foreach (var ambience in group.Where(l => !l.Ref.IsTrigger).ToList())
            {
                if (EventSoundFingerprint.Match(ambience.Ref, peaks).Score < triggerScore) continue;
                loaded.Remove(ambience);
                Log($"[audio] Dropped '{ambience.Name}' — it outscores the {trigger.Ref.EventType} " +
                    "trigger on the trigger's own sound and would suppress real detections.");
            }
        }
    }

    // ---------------------------------------------------------------- capture

    /// <summary>
    /// Capture only while the game is running. Analysing the system mix with Rust closed costs
    /// CPU for nothing and can only produce false positives.
    /// </summary>
    private void SyncCaptureWithGame()
    {
        if (!IsRunning) return;

        int pid = 0;
        try
        {
            var processes = Process.GetProcessesByName(GameProcessName);
            if (processes.Length > 0) pid = processes[0].Id;
        }
        catch { pid = 0; }

        bool running = pid != 0;
        bool capturing = _capture != null || _processCapture != null;

        if (running && !capturing) StartCapture(pid);
        else if (!running && capturing) StopCapture();
    }

    private void StartCapture(int processId)
    {
        // Per-process capture first: it hears only Rust, so a stream or video playing in
        // another window cannot trigger a detection. Falls back to the system mix when the
        // Windows build is too old or activation fails for any reason — a working detector on
        // the system mix is worth far more than none at all.
        if (ProcessLoopbackCapture.IsSupported && TryStartProcessCapture(processId)) return;

        StartSystemMixCapture();
    }

    private bool TryStartProcessCapture(int processId)
    {
        try
        {
            var capture = new ProcessLoopbackCapture();

            _ring = new float[(int)(_windowSeconds * EventSoundFingerprint.SampleRate)];
            _ringPos = 0;
            _ringFilled = false;
            _samplesSinceMatch = 0;

            capture.DataAvailable += (samples, count) =>
                OnSamples(samples, count, capture.SampleRate, capture.Channels);

            capture.Start(processId);

            _processCapture = capture;
            CaptureMode = "process";
            Log($"[audio] Capturing {GameProcessName} directly ({capture.SampleRate} Hz, {capture.Channels} ch) — " +
                "detections from this client stand on their own.");
            return true;
        }
        catch (Exception ex)
        {
            _processCapture = null;
            Log($"[audio] Per-process capture unavailable ({ex.Message}) — falling back to the system mix.");
            return false;
        }
    }

    private void StartSystemMixCapture()
    {
        try
        {
            var capture = new WasapiLoopbackCapture();
            int rate = capture.WaveFormat.SampleRate;
            int channels = capture.WaveFormat.Channels;

            _ring = new float[(int)(_windowSeconds * EventSoundFingerprint.SampleRate)];
            _ringPos = 0;
            _ringFilled = false;
            _samplesSinceMatch = 0;

            capture.DataAvailable += (_, e) => OnAudio(e, rate, channels);
            capture.RecordingStopped += (_, __) => { };
            capture.StartRecording();

            _capture = capture;
            CaptureMode = "system";
            Log($"[audio] Capturing system mix ({rate} Hz, {channels} ch) while {GameProcessName} runs — " +
                "detections need a second client to confirm them.");
        }
        catch (Exception ex)
        {
            Log($"[audio] Capture could not start: {ex.Message}");
            _capture = null;
        }
    }

    private void StopCapture()
    {
        var systemCapture = _capture;
        _capture = null;
        if (systemCapture != null)
            try { systemCapture.StopRecording(); systemCapture.Dispose(); } catch { }

        var processCapture = _processCapture;
        _processCapture = null;
        if (processCapture != null)
            try { processCapture.Dispose(); } catch { }

        CaptureMode = "system";
        lock (_gate) _lastHit.Clear();
    }

    /// <summary>Byte buffer from WASAPI loopback — 32-bit float, interleaved.</summary>
    private void OnAudio(WaveInEventArgs e, int captureRate, int channels)
    {
        if (channels <= 0) return;

        int frames = e.BytesRecorded / 4 / channels;
        var interleaved = new float[frames * channels];
        Buffer.BlockCopy(e.Buffer, 0, interleaved, 0, frames * channels * 4);
        OnSamples(interleaved, frames * channels, captureRate, channels);
    }

    /// <summary>
    /// Shared analysis path. Both capture sources hand over interleaved 32-bit float samples,
    /// so everything downstream — mixdown, resampling, matching — stays identical and the two
    /// paths cannot drift apart in behaviour.
    /// </summary>
    private void OnSamples(float[] interleaved, int sampleCount, int captureRate, int channels)
    {
        var references = _references;
        if (references == null || references.Count == 0 || channels <= 0) return;

        try
        {
            int frames = sampleCount / channels;
            var mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++) sum += interleaved[f * channels + c];
                mono[f] = sum / channels;
            }

            var resampled = EventSoundFingerprint.Resample(mono, captureRate, EventSoundFingerprint.SampleRate);
            foreach (float s in resampled)
            {
                _ring[_ringPos] = s;
                _ringPos = (_ringPos + 1) % _ring.Length;
                if (_ringPos == 0) _ringFilled = true;
            }

            _samplesSinceMatch += resampled.Length;
            if (!_ringFilled || _samplesSinceMatch < MatchIntervalSeconds * EventSoundFingerprint.SampleRate) return;
            _samplesSinceMatch = 0;

            var window = new float[_ring.Length];
            for (int i = 0; i < _ring.Length; i++) window[i] = _ring[(_ringPos + i) % _ring.Length];
            var peaks = EventSoundFingerprint.Peaks(window);

            foreach (var group in references.GroupBy(r => r.EventType))
            {
                var scored = group
                    .Select(r =>
                    {
                        var (score, offset) = EventSoundFingerprint.Match(r, peaks);
                        return (Ref: r, Score: score, Offset: offset);
                    })
                    .OrderByDescending(x => x.Score)
                    .ToList();

                var top = scored[0];
                if (top.Score < top.Ref.Threshold) continue;

                // Ambience beat the trigger: the event is already running, not starting.
                if (!top.Ref.IsTrigger) continue;

                var now = DateTime.UtcNow;
                lock (_gate)
                {
                    if (_lastHit.TryGetValue(top.Ref.EventType, out var previous))
                    {
                        double elapsed = (now - previous.At).TotalSeconds;

                        if (elapsed < MinReportIntervalSeconds) continue;

                        // Where the earlier occurrence would sit now, had the buffer simply
                        // carried it along. A match at that position is the same sound again.
                        if (elapsed < SameOccurrenceMaxAgeSeconds)
                        {
                            int framesElapsed = (int)Math.Round(
                                elapsed * EventSoundFingerprint.SampleRate / EventSoundFingerprint.HopSize);
                            int expected = previous.Offset - framesElapsed;

                            if (Math.Abs(top.Offset - expected) <= SameOccurrenceToleranceFrames) continue;
                        }
                    }

                    _lastHit[top.Ref.EventType] = (now, top.Offset);
                }

                // Window frame 0 is the oldest sample in the ring, so the cue begins
                // `offset` hops after that.
                double hopSeconds = (double)EventSoundFingerprint.HopSize / EventSoundFingerprint.SampleRate;
                DateTime cueStart = now.AddSeconds(-_windowSeconds + top.Offset * hopSeconds);

                Log($"[audio] {top.Ref.EventType} detected (score {top.Score:F0}, threshold {top.Ref.Threshold:F0}, " +
                    $"cue began {(now - cueStart).TotalSeconds:F1}s ago).");
                Detected?.Invoke(new GameAudioDetection(
                    top.Ref.EventType, top.Score, now, CaptureMode, cueStart));
            }
        }
        catch
        {
            // A single bad buffer must never kill the capture callback.
        }
    }

    private static void Log(string message)
    {
        try
        {
            var app = System.Windows.Application.Current;
            app?.Dispatcher.Invoke(() =>
            {
                if (app.MainWindow is Views.MainWindow window) window.AppendLog(message);
            });
        }
        catch { }
    }
}
