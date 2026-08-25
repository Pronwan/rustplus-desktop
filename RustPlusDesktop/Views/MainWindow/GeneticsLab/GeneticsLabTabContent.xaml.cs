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

namespace RustPlusDesk.Views
{
    public partial class GeneticsLabTabContent : UserControl
    {
        public event RoutedEventHandler? CloseRequested;

        private bool _isInitialized;
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
            if (!_isInitialized)
            {
                await InitializeWebViewAsync();
            }

            UpdatePerformanceMode();
        }

        private void GeneticsLabTabContent_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) =>
            UpdatePerformanceMode();

        // Boost only while the tab is visible AND the scanner is actively running. This keeps
        // the scanner's realtime performance intact while ensuring heavy gene calculations
        // (visible but not scanning) run at normal priority and don't stutter the whole PC.
        private void UpdatePerformanceMode() => SetPerformanceMode(IsVisible && _isScannerActive);

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
                    additionalBrowserArguments: "--disable-background-timer-throttling " +
                                                "--disable-backgrounding-occluded-windows " +
                                                "--disable-renderer-backgrounding " +
                                                "--disable-background-media-suspend " +
                                                "--disable-features=CalculateNativeWinOcclusion " +
                                                "--force-high-performance-gpu " +
                                                "--disable-gpu-vsync");

                _sharedEnvironment ??= await CoreWebView2Environment.CreateAsync(
                    userDataFolder: webViewDataFolder,
                    options: envOptions);

                await GeneticsWebView.EnsureCoreWebView2Async(_sharedEnvironment);
                GeneticsWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                _sharedEnvironment.ProcessInfosChanged += SharedEnvironment_ProcessInfosChanged;
                RefreshWebViewProcessIds();

                if (Directory.Exists(distFolder))
                {
                    GeneticsWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        "geneticslab.rustplus",
                        distFolder,
                        CoreWebView2HostResourceAccessKind.Allow);

                    GeneticsWebView.NavigationCompleted += GeneticsWebView_NavigationCompleted;

                    GeneticsWebView.CoreWebView2.Navigate("https://geneticslab.rustplus/index.html");
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
                CoreWebView2? webView = GeneticsWebView.CoreWebView2;
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
                GeneticsWebView?.CoreWebView2?.Reload();
            }
            catch
            {
                // ignore
            }
        }

        private void Reload_Click(object sender, RoutedEventArgs e) => Reload();

        private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, e);
    }
}
