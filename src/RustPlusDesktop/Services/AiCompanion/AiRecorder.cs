using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NAudio.Wave;
using RustPlusDesk.Services.Audio;

namespace RustPlusDesk.Services.AiCompanion
{
    public enum AiRecorderState
    {
        Idle,
        Recording,

        /// <summary>Stopped with something to send, waiting for the user to send or discard it.</summary>
        Ready,
    }

    /// <summary>
    /// Records the question: the microphone always, and the game's own sound alongside it when
    /// asked for.
    ///
    /// The two go to separate files and are only put together at the point of sending. Keeping
    /// them apart is what lets the companion be told "the microphone is me asking, the other
    /// track is what was said in game" — mixed down first, that distinction is gone for good.
    /// </summary>
    public sealed class AiRecorder : IDisposable
    {
        public static AiRecorder Instance { get; } = new();

        /// <summary>
        /// A recording nobody stopped is a microphone left open. It ends and is thrown away
        /// rather than kept, because five minutes of a forgotten hot mic is not a question
        /// anyone meant to ask.
        /// </summary>
        public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(5);

        private const string GameProcessName = "RustClient";

        public AiRecorderState State { get; private set; } = AiRecorderState.Idle;

        public TimeSpan Elapsed => State == AiRecorderState.Recording
            ? DateTime.UtcNow - _startedUtc
            : _finalLength;

        /// <summary>The finished microphone track, or null while idle.</summary>
        public string? MicPath { get; private set; }

        /// <summary>The finished game-audio track, null when it was not asked for or not available.</summary>
        public string? GamePath { get; private set; }

        /// <summary>Why the last attempt to start failed, for the tile to show.</summary>
        public string? LastError { get; private set; }

        public event Action? Changed;

        private WaveInEvent? _mic;
        private WaveFileWriter? _micWriter;

        private ProcessLoopbackCapture? _game;
        private WasapiLoopbackCapture? _systemMix;
        private WaveFileWriter? _gameWriter;

        private DateTime _startedUtc;
        private TimeSpan _finalLength;
        private System.Threading.Timer? _cutoff;

        private AiRecorder() { }

        private static string Folder
        {
            get
            {
                var path = Path.Combine(Path.GetTempPath(), "RustPlusDesk", "ai-companion");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        /// <summary>Begins recording. False when there is no microphone to record from.</summary>
        public bool Start(bool captureGameAudio)
        {
            if (State == AiRecorderState.Recording) return true;

            Discard();
            LastError = null;

            try
            {
                if (WaveInEvent.DeviceCount == 0)
                {
                    LastError = "no-microphone";
                    Raise();
                    return false;
                }

                MicPath = Path.Combine(Folder, $"mic-{DateTime.UtcNow:yyyyMMdd-HHmmss}.wav");

                // 16 kHz mono: speech carries fine at that rate and every provider takes it,
                // while a minute of it is under two megabytes to upload.
                _mic = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1) };
                _micWriter = new WaveFileWriter(MicPath, _mic.WaveFormat);
                _mic.DataAvailable += (_, e) =>
                {
                    try { _micWriter?.Write(e.Buffer, 0, e.BytesRecorded); }
                    catch { /* a full disk must not take the app with it */ }
                };
                _mic.StartRecording();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Cleanup();
                Raise();
                return false;
            }

            if (captureGameAudio) TryStartGameAudio();

            _startedUtc = DateTime.UtcNow;
            State = AiRecorderState.Recording;

            _cutoff = new System.Threading.Timer(_ => AbandonAtCutoff(), null, MaxDuration, Timeout.InfiniteTimeSpan);

            Raise();
            return true;
        }

        /// <summary>
        /// Records what the game itself is playing, on its own track.
        ///
        /// Per process where Windows allows it, so a video in another window cannot end up in
        /// the recording. Falls back to the system mix, which hears everything — and with the
        /// game closed there is nothing to record either way, so it is simply skipped.
        /// </summary>
        private void TryStartGameAudio()
        {
            try
            {
                int pid = 0;
                var processes = Process.GetProcessesByName(GameProcessName);
                if (processes.Length > 0) pid = processes[0].Id;
                if (pid == 0) return;

                GamePath = Path.Combine(Folder, $"game-{DateTime.UtcNow:yyyyMMdd-HHmmss}.wav");

                if (ProcessLoopbackCapture.IsSupported)
                {
                    var capture = new ProcessLoopbackCapture();
                    _gameWriter = new WaveFileWriter(GamePath,
                        WaveFormat.CreateIeeeFloatWaveFormat(capture.SampleRate, 1));

                    capture.DataAvailable += (samples, count) =>
                    {
                        try
                        {
                            // Down to mono on the way in: the companion is being asked what was
                            // said, and stereo doubles the upload for nothing.
                            for (int i = 0; i + capture.Channels <= count; i += capture.Channels)
                            {
                                float sum = 0;
                                for (int c = 0; c < capture.Channels; c++) sum += samples[i + c];
                                _gameWriter?.WriteSample(sum / capture.Channels);
                            }
                        }
                        catch { }
                    };

                    capture.Start(pid);
                    _game = capture;
                    return;
                }

                var mix = new WasapiLoopbackCapture();
                _gameWriter = new WaveFileWriter(GamePath, mix.WaveFormat);
                mix.DataAvailable += (_, e) =>
                {
                    try { _gameWriter?.Write(e.Buffer, 0, e.BytesRecorded); }
                    catch { }
                };
                mix.StartRecording();
                _systemMix = mix;
            }
            catch
            {
                // The question still works without the game's sound, so a failure here costs the
                // second track and nothing else.
                StopGameAudio();
                GamePath = null;
            }
        }

        /// <summary>Ends the recording and keeps it, ready to be sent or thrown away.</summary>
        public void Stop()
        {
            if (State != AiRecorderState.Recording) return;

            _finalLength = DateTime.UtcNow - _startedUtc;
            StopCapture();

            State = AiRecorderState.Ready;
            Raise();
        }

        /// <summary>Ends the recording and deletes it.</summary>
        public void Discard()
        {
            StopCapture();
            Cleanup();

            State = AiRecorderState.Idle;
            _finalLength = TimeSpan.Zero;
            Raise();
        }

        private void AbandonAtCutoff()
        {
            if (State != AiRecorderState.Recording) return;

            LastError = "cutoff";
            Discard();
        }

        private void StopCapture()
        {
            _cutoff?.Dispose();
            _cutoff = null;

            try { _mic?.StopRecording(); } catch { }
            try { _mic?.Dispose(); } catch { }
            _mic = null;

            try { _micWriter?.Dispose(); } catch { }
            _micWriter = null;

            StopGameAudio();
        }

        private void StopGameAudio()
        {
            try { _game?.Stop(); } catch { }
            try { _game?.Dispose(); } catch { }
            _game = null;

            try { _systemMix?.StopRecording(); } catch { }
            try { _systemMix?.Dispose(); } catch { }
            _systemMix = null;

            try { _gameWriter?.Dispose(); } catch { }
            _gameWriter = null;
        }

        private void Cleanup()
        {
            TryDelete(MicPath);
            TryDelete(GamePath);
            MicPath = null;
            GamePath = null;
        }

        private static void TryDelete(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private void Raise()
        {
            try { Changed?.Invoke(); } catch { }
        }

        public void Dispose() => Discard();
    }
}
