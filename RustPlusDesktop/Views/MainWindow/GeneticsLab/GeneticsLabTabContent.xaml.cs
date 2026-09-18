using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace RustPlusDesk.Views
{
    public partial class GeneticsLabTabContent : UserControl
    {
        public event RoutedEventHandler? CloseRequested;

        private WebView2? _webView;
        private bool _isInitialized;
        private bool _isInitializing;
        private static CoreWebView2Environment? _sharedEnvironment;
        private volatile bool _performanceModeEnabled;
        // The high-priority/anti-throttling performance boost is only justified while the
        // scanner is actually running. Calculations (tab visible but not scanning) must run
        // at normal priority so they don't starve the rest of the PC. JS reports this state
        // over a web message; see OnWebMessageReceived.
        private bool _isScannerActive;
        private volatile int[] _webViewProcessIds = Array.Empty<int>();
        private DispatcherTimer? _performanceTimer;
        private bool _isReassertingPerformanceMode;

        private const uint ProcessSetInformation = 0x0200;
        private const int ProcessPowerThrottling = 4;
        private const uint PowerThrottlingCurrentVersion = 1;
        private const uint PowerThrottlingExecutionSpeed = 0x1;
        private const uint NormalPriorityClass = 0x00000020;
        private const uint AboveNormalPriorityClass = 0x00008000;

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessPowerThrottlingState
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessInformation(
            IntPtr process,
            int processInformationClass,
            ref ProcessPowerThrottlingState processInformation,
            uint processInformationSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetPriorityClass(IntPtr process, uint priorityClass);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        public GeneticsLabTabContent()
        {
            InitializeComponent();
            Loaded += GeneticsLabTabContent_Loaded;
            IsVisibleChanged += GeneticsLabTabContent_IsVisibleChanged;
        }

        private async void GeneticsLabTabContent_Loaded(object sender, RoutedEventArgs e)
        {
            await EnsureWebViewAsync();
            UpdatePerformanceMode();
        }

        // Closing disposes the WebView2 while this control stays in the visual tree, so Loaded
        // does not fire again on the way back in. Becoming visible is the signal that the tab was
        // reopened and the browser has to be rebuilt.
        private async void GeneticsLabTabContent_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                await EnsureWebViewAsync();
            }

            UpdatePerformanceMode();
        }

        private async Task EnsureWebViewAsync()
        {
            // Loaded and IsVisibleChanged can both land before the first initialisation finishes,
            // and two of these in flight would build two browsers and leak the first.
            if (_isInitialized || _isInitializing) return;

            _isInitializing = true;
            try
            {
                await InitializeWebViewAsync();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        // Boost whenever the scanner is actively running (even in background behind Rust)
        // or when visible. This keeps background capture and audio playback 100% unthrottled.
        // Gated on the browser existing: after a close there is nothing left to boost, and the
        // 250ms reassert timer must not be revived to raise the priority of processes that are
        // already gone.
        private void UpdatePerformanceMode() => SetPerformanceMode(_isInitialized && (_isScannerActive || IsVisible));

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(e.WebMessageAsJson);
                JsonElement root = doc.RootElement;
                if (root.TryGetProperty("type", out JsonElement type) &&
                    type.GetString() == "scanner-state")
                {
                    _isScannerActive =
                        root.TryGetProperty("active", out JsonElement active) &&
                        active.ValueKind == JsonValueKind.True;
                    UpdatePerformanceMode();
                }
            }
            catch
            {
                // Ignore malformed messages.
            }
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string distFolder = Path.Combine(baseDir, "Features", "GeneticsLab", "dist");

                // In debug / development mode, look in project source tree if not in bin
                if (!Directory.Exists(distFolder))
                {
                    string candidate = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Features", "GeneticsLab", "dist"));
                    if (Directory.Exists(candidate))
                    {
                        distFolder = candidate;
                    }
                }

                if (!Directory.Exists(distFolder))
                {
                    // Fallback to non-dist folder if dist is not yet built
                    string nonDist = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Features", "GeneticsLab"));
                    if (Directory.Exists(nonDist))
                    {
                        distFolder = nonDist;
                    }
                }

                string webViewDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RustPlusDesk",
                    "WebView2_GeneticsLab");

                Directory.CreateDirectory(webViewDataFolder);

                var envOptions = new CoreWebView2EnvironmentOptions(
                    additionalBrowserArguments: "--autoplay-policy=no-user-gesture-required " +
                                                "--disable-background-timer-throttling " +
                                                "--disable-backgrounding-occluded-windows " +
                                                "--disable-renderer-backgrounding " +
                                                "--disable-background-media-suspend " +
                                                "--disable-features=CalculateNativeWinOcclusion " +
                                                "--force-high-performance-gpu " +
                                                "--disable-gpu-vsync");

                _sharedEnvironment ??= await CoreWebView2Environment.CreateAsync(
                    userDataFolder: webViewDataFolder,
                    options: envOptions);

                LoadingOverlay.Visibility = Visibility.Visible;

                _webView = new WebView2
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                WebViewHost.Children.Add(_webView);

                await _webView.EnsureCoreWebView2Async(_sharedEnvironment);
                _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                _sharedEnvironment.ProcessInfosChanged += SharedEnvironment_ProcessInfosChanged;
                RefreshWebViewProcessIds();

                if (Directory.Exists(distFolder))
                {
                    _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        "geneticslab.rustplus",
                        distFolder,
                        CoreWebView2HostResourceAccessKind.Allow);

                    _webView.NavigationCompleted += GeneticsWebView_NavigationCompleted;

                    _webView.CoreWebView2.Navigate("https://geneticslab.rustplus/index.html");
                }
                else
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                }

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GeneticsLab] Failed to initialize WebView2: {ex.Message}");
                LoadingOverlay.Visibility = Visibility.Collapsed;
                // A half-built browser must not be left parented and unreachable; the next open
                // would add a second one beside it.
                ShutdownWebView();
            }
        }

        private async void GeneticsWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            if (_performanceModeEnabled)
            {
                await KeepWebViewActiveAsync();
            }
        }

        private void SharedEnvironment_ProcessInfosChanged(object? sender, object e)
        {
            RefreshWebViewProcessIds();
            if (_performanceModeEnabled)
            {
                ApplyPowerMode(useSystemManagedThrottling: false);
            }
        }

        private void RefreshWebViewProcessIds()
        {
            _webViewProcessIds = _sharedEnvironment?.GetProcessInfos()
                .Select(process => process.ProcessId)
                .ToArray() ?? Array.Empty<int>();
        }

        private void SetPerformanceMode(bool enabled)
        {
            if (_performanceModeEnabled == enabled) return;

            _performanceModeEnabled = enabled;
            _performanceTimer?.Stop();
            _performanceTimer = null;

            ApplyPowerMode(useSystemManagedThrottling: !enabled);

            if (enabled)
            {
                // ponytail: WebView2 147 can restore EcoQoS/Idle priority after focus changes; remove this timer when the runtime regression is fixed.
                _performanceTimer = new DispatcherTimer(DispatcherPriority.Send)
                {
                    Interval = TimeSpan.FromMilliseconds(250)
                };
                _performanceTimer.Tick += PerformanceTimer_Tick;
                _performanceTimer.Start();
            }
        }

        private async void PerformanceTimer_Tick(object? sender, EventArgs e)
        {
            if (!_performanceModeEnabled || _isReassertingPerformanceMode) return;

            _isReassertingPerformanceMode = true;
            try
            {
                ApplyPowerMode(useSystemManagedThrottling: false);
                await KeepWebViewActiveAsync();
            }
            finally
            {
                _isReassertingPerformanceMode = false;
            }
        }

        private async Task KeepWebViewActiveAsync()
        {
            try
            {
                CoreWebView2? webView = _webView?.CoreWebView2;
                if (webView == null) return;

                webView.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                await webView.CallDevToolsProtocolMethodAsync(
                    "Page.setWebLifecycleState",
                    "{\"state\":\"active\"}");
                await webView.CallDevToolsProtocolMethodAsync(
                    "Emulation.setFocusEmulationEnabled",
                    "{\"enabled\":true}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GeneticsLab] Failed to keep WebView active: {ex.Message}");
            }
        }

        private void ApplyPowerMode(bool useSystemManagedThrottling)
        {
            SetProcessPowerThrottling(Environment.ProcessId, useSystemManagedThrottling);
            foreach (int processId in _webViewProcessIds)
            {
                SetProcessPowerThrottling(processId, useSystemManagedThrottling);
            }
        }

        private static void SetProcessPowerThrottling(int processId, bool useSystemManagedThrottling)
        {
            IntPtr process = OpenProcess(ProcessSetInformation, inheritHandle: false, processId);
            if (process == IntPtr.Zero) return;

            try
            {
                var state = new ProcessPowerThrottlingState
                {
                    Version = PowerThrottlingCurrentVersion,
                    ControlMask = useSystemManagedThrottling ? 0 : PowerThrottlingExecutionSpeed,
                    StateMask = 0
                };

                SetProcessInformation(
                    process,
                    ProcessPowerThrottling,
                    ref state,
                    (uint)Marshal.SizeOf<ProcessPowerThrottlingState>());

                SetPriorityClass(
                    process,
                    useSystemManagedThrottling ? NormalPriorityClass : AboveNormalPriorityClass);
            }
            finally
            {
                CloseHandle(process);
            }
        }

        public void Reload()
        {
            try
            {
                _webView?.CoreWebView2?.Reload();
            }
            catch
            {
                // ignore
            }
        }

        private void Reload_Click(object sender, RoutedEventArgs e) => Reload();

        /// <summary>
        /// Leaves the workspace with Genetics Lab still loaded.
        /// </summary>
        /// <remarks>
        /// This is what the dismiss button always did. It is worth keeping and worth naming
        /// honestly: a scan in progress survives, unsaved work in the page survives, and coming
        /// back is instant because nothing was torn down. The cost is that the browser and its
        /// loaded OCR data stay resident, which is precisely the trade <see cref="Close_Click"/>
        /// makes the other way.
        /// </remarks>
        private void Minimize_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, e);

        /// <summary>
        /// Closes Genetics Lab and gives its memory back.
        /// </summary>
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            ShutdownWebView();
            CloseRequested?.Invoke(this, e);
        }

        /// <summary>
        /// Disposes the browser and everything hanging off it.
        /// </summary>
        /// <remarks>
        /// Closing used to only collapse the panel. The WebView2 stayed alive for the rest of the
        /// session with the whole app resident behind it -- the React bundle, the solver workers,
        /// and the OCR engine's language data, which alone is tens of megabytes once a scan has
        /// warmed it -- across a browser process, a renderer and a GPU process. Nothing ever
        /// released any of it, and the 250ms performance timer went on reasserting raised process
        /// priority and an EcoQoS opt-out on processes the user believed they had closed.
        ///
        /// Order matters here. Power throttling is restored first, while the process ids are still
        /// valid; handlers come off next so a late callback cannot touch a disposed control; the
        /// static environment reference goes last, because the browser process outlives the
        /// WebView and only exits once nothing holds the environment.
        /// </remarks>
        private void ShutdownWebView()
        {
            // Drops the boost and the reassert timer, and hands the processes back to the system
            // scheduler while their ids are still valid.
            _isScannerActive = false;
            SetPerformanceMode(false);

            if (_sharedEnvironment != null)
            {
                try { _sharedEnvironment.ProcessInfosChanged -= SharedEnvironment_ProcessInfosChanged; } catch { }
            }

            if (_webView != null)
            {
                try
                {
                    _webView.NavigationCompleted -= GeneticsWebView_NavigationCompleted;
                    if (_webView.CoreWebView2 != null)
                    {
                        _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                        // Unloads the page before the control goes, so the scanner's screen capture
                        // and the audio context are released by the page's own teardown rather than
                        // being cut off with the process.
                        _webView.CoreWebView2.Navigate("about:blank");
                    }
                }
                catch { }

                try { WebViewHost.Children.Remove(_webView); } catch { }
                try { _webView.Dispose(); } catch { }
                _webView = null;
            }

            _webViewProcessIds = Array.Empty<int>();
            _sharedEnvironment = null;
            _isInitialized = false;
            LoadingOverlay.Visibility = Visibility.Visible;

            ReclaimProcessMemory();
        }

        // Hosting the browser leaves large buffers on this process's managed heap too. The Large
        // Object Heap is not handed back to the OS by an ordinary collection, so a compacting one
        // is forced after teardown. Deferred to Background priority so it never stalls the close.
        private void ReclaimProcessMemory()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            }), DispatcherPriority.Background);
        }
    }
}
