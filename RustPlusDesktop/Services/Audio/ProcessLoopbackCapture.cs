using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace RustPlusDesk.Services.Audio;

/// <summary>
/// Captures the audio of one process instead of the whole system mix.
///
/// This is what makes a detection trustworthy: a client listening to RustClient.exe directly
/// cannot pick a cue up from a Rust video or a stream playing in another window, so the
/// backend accepts its report without corroboration. The system mix cannot make that promise.
///
/// Windows only exposes this through ActivateAudioInterfaceAsync against the pseudo-device
/// VAD\Process_Loopback, available from build 20348. .NET does not wrap it and neither does
/// NAudio 2.x, hence the hand-written COM declarations below. OBS uses the same API for its
/// "Application Audio Capture" source.
///
/// Two things differ from ordinary WASAPI loopback and are easy to get wrong:
///   * GetMixFormat is not supported here, so the format has to be stated up front;
///   * the client must be initialised in shared mode with LOOPBACK and EVENTCALLBACK, and the
///     activation itself has to happen on an MTA thread.
/// </summary>
internal sealed class ProcessLoopbackCapture : IDisposable
{
    /// <summary>Raised on the capture thread with interleaved 32-bit float samples.</summary>
    public event Action<float[], int>? DataAvailable;

    public int SampleRate { get; }
    public int Channels { get; }

    private const string VirtualAudioDeviceProcessLoopback = "VAD\\Process_Loopback";

    private const int AudclntShareModeShared = 0;
    private const uint AudclntStreamflagsLoopback = 0x00020000;
    private const uint AudclntStreamflagsEventcallback = 0x00040000;
    private const uint AudclntStreamflagsAutoconvertpcm = 0x80000000;

    private const int WaveFormatExtensible = unchecked((short)0xFFFE);
    private const int SizeOfWaveFormatEx = 18;

    private static readonly Guid IidAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    private static readonly Guid IidAudioCaptureClient = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
    private static readonly Guid KsDataFormatSubtypeIeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");

    private IAudioClient? _audioClient;
    private IAudioCaptureClient? _captureClient;
    private EventWaitHandle? _bufferReady;
    private Thread? _captureThread;
    private volatile bool _running;

    public ProcessLoopbackCapture(int sampleRate = 48000, int channels = 2)
    {
        SampleRate = sampleRate;
        Channels = channels;
    }

    /// <summary>
    /// True when this Windows build exposes process loopback at all. Checked before use so
    /// older systems fall back to the system mix rather than failing at activation.
    /// </summary>
    public static bool IsSupported => Environment.OSVersion.Version.Build >= 20348;

    public void Start(int processId)
    {
        if (!IsSupported) throw new PlatformNotSupportedException("Process loopback needs Windows build 20348 or newer.");
        if (_running) return;

        // ActivateAudioInterfaceAsync completes on a COM callback, so it must not run on an
        // STA thread — a WPF UI thread would deadlock waiting for its own message pump.
        Exception? failure = null;
        var done = new ManualResetEventSlim(false);

        var activationThread = new Thread(() =>
        {
            try { _audioClient = ActivateForProcess(processId); }
            catch (Exception ex) { failure = ex; }
            finally { done.Set(); }
        }) { IsBackground = true, Name = "Rust process loopback activation" };

        activationThread.SetApartmentState(ApartmentState.MTA);
        activationThread.Start();

        if (!done.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Audio interface activation timed out.");
        if (failure != null) throw failure;
        if (_audioClient == null) throw new InvalidOperationException("Audio interface activation returned nothing.");

        InitialiseClient();

        _running = true;
        _captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = "Rust process loopback capture" };
        _captureThread.Start();
    }

    public void Stop()
    {
        _running = false;
        _bufferReady?.Set();

        try { _captureThread?.Join(TimeSpan.FromSeconds(2)); } catch { }
        _captureThread = null;

        try { _audioClient?.Stop(); } catch { }

        if (_captureClient != null) { Marshal.ReleaseComObject(_captureClient); _captureClient = null; }
        if (_audioClient != null) { Marshal.ReleaseComObject(_audioClient); _audioClient = null; }

        _bufferReady?.Dispose();
        _bufferReady = null;
    }

    public void Dispose() => Stop();

    // ---------------------------------------------------------------- activation

    private static IAudioClient ActivateForProcess(int processId)
    {
        var loopbackParams = new AudioclientProcessLoopbackParams
        {
            TargetProcessId = (uint)processId,
            // Include the tree: Rust spawns helper processes, and audio does not always come
            // from the PID we matched by name.
            ProcessLoopbackMode = 0,
        };

        var activationParams = new AudioclientActivationParams
        {
            ActivationType = 1,   // AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
            ProcessLoopbackParams = loopbackParams,
        };

        int size = Marshal.SizeOf<AudioclientActivationParams>();
        IntPtr blob = Marshal.AllocHGlobal(size);
        IntPtr propVariant = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariant>());

        try
        {
            Marshal.StructureToPtr(activationParams, blob, false);

            var variant = new PropVariant
            {
                Vt = 65,          // VT_BLOB
                BlobSize = (uint)size,
                BlobData = blob,
            };
            Marshal.StructureToPtr(variant, propVariant, false);

            var handler = new ActivationHandler();
            Guid iid = IidAudioClient;

            int hr = ActivateAudioInterfaceAsync(
                VirtualAudioDeviceProcessLoopback, ref iid, propVariant, handler, out var operation);
            Marshal.ThrowExceptionForHR(hr);

            if (!handler.Completed.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Activation callback never fired.");

            operation.GetActivateResult(out int activateHr, out object? client);
            Marshal.ThrowExceptionForHR(activateHr);

            return (IAudioClient)client!;
        }
        finally
        {
            Marshal.FreeHGlobal(propVariant);
            Marshal.FreeHGlobal(blob);
        }
    }

    private void InitialiseClient()
    {
        // GetMixFormat is unsupported on this pseudo-device, so the format is stated rather
        // than queried. 32-bit float matches what the fingerprinter wants anyway.
        var format = new WaveFormatExtensibleStruct
        {
            FormatTag = (short)WaveFormatExtensible,
            Channels = (short)Channels,
            SamplesPerSec = SampleRate,
            BitsPerSample = 32,
            BlockAlign = (short)(Channels * 4),
            AvgBytesPerSec = SampleRate * Channels * 4,
            Size = 22,
            ValidBitsPerSample = 32,
            ChannelMask = Channels == 1 ? 0x4u : 0x3u,
            SubFormat = KsDataFormatSubtypeIeeeFloat,
        };

        IntPtr formatPtr = Marshal.AllocHGlobal(SizeOfWaveFormatEx + 22);
        try
        {
            Marshal.StructureToPtr(format, formatPtr, false);

            const long hundredNanosecondsPerSecond = 10_000_000;
            int hr = _audioClient!.Initialize(
                AudclntShareModeShared,
                AudclntStreamflagsLoopback | AudclntStreamflagsEventcallback | AudclntStreamflagsAutoconvertpcm,
                hundredNanosecondsPerSecond / 5,   // 200 ms buffer
                0,
                formatPtr,
                IntPtr.Zero);
            Marshal.ThrowExceptionForHR(hr);
        }
        finally
        {
            Marshal.FreeHGlobal(formatPtr);
        }

        _bufferReady = new EventWaitHandle(false, EventResetMode.AutoReset);
        Marshal.ThrowExceptionForHR(_audioClient.SetEventHandle(_bufferReady.SafeWaitHandle.DangerousGetHandle()));

        Guid captureIid = IidAudioCaptureClient;
        Marshal.ThrowExceptionForHR(_audioClient.GetService(ref captureIid, out object captureService));
        _captureClient = (IAudioCaptureClient)captureService;

        Marshal.ThrowExceptionForHR(_audioClient.Start());
    }

    // ---------------------------------------------------------------- capture

    private void CaptureLoop()
    {
        var buffer = new float[SampleRate * Channels];   // one second, grown if ever needed

        while (_running)
        {
            if (!_bufferReady!.WaitOne(500)) continue;
            if (!_running) break;

            try
            {
                while (_captureClient!.GetNextPacketSize(out uint packetFrames) == 0 && packetFrames > 0)
                {
                    int hr = _captureClient.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _);
                    if (hr != 0 || frames == 0) break;

                    int sampleCount = (int)frames * Channels;
                    if (sampleCount > buffer.Length) buffer = new float[sampleCount];

                    // AUDCLNT_BUFFERFLAGS_SILENT: the buffer contents are undefined and must be
                    // treated as silence rather than read.
                    if ((flags & 0x2) != 0) Array.Clear(buffer, 0, sampleCount);
                    else Marshal.Copy(data, buffer, 0, sampleCount);

                    _captureClient.ReleaseBuffer(frames);
                    DataAvailable?.Invoke(buffer, sampleCount);
                }
            }
            catch
            {
                // A single bad packet must not end the capture.
            }
        }
    }

    // ---------------------------------------------------------------- interop

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        ref Guid riid,
        IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation operation);

    [ComVisible(true)]
    private sealed class ActivationHandler : IActivateAudioInterfaceCompletionHandler
    {
        public readonly ManualResetEventSlim Completed = new(false);
        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation) => Completed.Set();
    }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceCompletionHandler
    {
        void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceAsyncOperation
    {
        void GetActivateResult([MarshalAs(UnmanagedType.Error)] out int activateResult,
                               [MarshalAs(UnmanagedType.IUnknown)] out object? activatedInterface);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration,
                                     long periodicity, IntPtr format, IntPtr audioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint bufferFrames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, IntPtr closestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags,
                                    out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioclientProcessLoopbackParams
    {
        public uint TargetProcessId;
        public int ProcessLoopbackMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioclientActivationParams
    {
        public int ActivationType;
        public AudioclientProcessLoopbackParams ProcessLoopbackParams;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Vt;
        public ushort Reserved1, Reserved2, Reserved3;
        public uint BlobSize;
        public IntPtr BlobData;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WaveFormatExtensibleStruct
    {
        public short FormatTag;
        public short Channels;
        public int SamplesPerSec;
        public int AvgBytesPerSec;
        public short BlockAlign;
        public short BitsPerSample;
        public short Size;
        public short ValidBitsPerSample;
        public uint ChannelMask;
        public Guid SubFormat;
    }
}
