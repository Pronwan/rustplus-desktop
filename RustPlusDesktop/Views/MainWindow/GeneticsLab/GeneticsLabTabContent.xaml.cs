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
using RustPlusDesk.Services.GeneticsLab;

namespace RustPlusDesk.Views
{
    public partial class GeneticsLabTabContent : UserControl
    {
        public event RoutedEventHandler? CloseRequested;

        private const string VirtualHost = "geneticslab.rustplus";

        private WebView2? _webView;
        // The Genetics Lab build the WebView is mapped to; see GeneticsLabContentService.
        private GeneticsLabContent? _content;
        // Version of the build the current page replaced, handed to the page for "What's new".
        private string? _previousVersionForPage;
        // Builds that send "host-hello" show update notices themselves; for older ones the
        // header button is the only sign an update is waiting.
        private bool _pageShowsUpdates;
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

            if (!System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
                GeneticsLabContentService.Instance.StatusChanged += ContentService_StatusChanged;
                // Genetics Lab updates on its own schedule, independent of desktop releases. Check
                // shortly after startup so most users have the new build before they open the tab.
                GeneticsLabContentService.Instance.CheckForUpdatesInBackground(TimeSpan.FromSeconds(60));
                // ...and keep checking while the app runs, so a release that lands after start-up
                // (or after the last update) is found without a restart.
                GeneticsLabContentService.Instance.StartPeriodicChecks();
            }
        }

        // Nothing changes under an open page -- it may be mid-scan -- so the page is told and offers
        // a reload; a closed tab simply picks the new build up when reopened.
        private void ContentService_StatusChanged(object? sender, GeneticsLabUpdateStatus? status)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_isInitialized) return;
                PostUpdateStatusToPage(status);
                UpdateReadyButton.Visibility = status?.State == "ready" && !_pageShowsUpdates
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }));
        }

        private void PostUpdateStatusToPage(GeneticsLabUpdateStatus? status)
        {
            if (status == null || _webView?.CoreWebView2 == null) return;
            // A "ready" build the page is already running is not news.
            if (status.State == "ready" && GeneticsLabContentService.Instance.FindNewerThan(_content) == null) return;

            PostToPage(new
            {
                type = "genetics-lab-update",
                state = status.State,
                version = status.Version,
                changelog = status.Changelog
            });
        }

        private void PostToPage(object message)
        {
            try
            {
                _webView?.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GeneticsLab] Posting to the page failed: {ex.Message}");
            }
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
                // Every time Genetics Lab is opened, look for a newer build. It costs one small
                // manifest request; files download only when there is a new release.
                GeneticsLabContentService.Instance.CheckForUpdatesInBackground(force: true);
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
                string? type = root.TryGetProperty("type", out JsonElement typeElement) ? typeElement.GetString() : null;
                switch (type)
                {
                    case "scanner-state":
                        _isScannerActive =
                            root.TryGetProperty("active", out JsonElement active) &&
                            active.ValueKind == JsonValueKind.True;
                        UpdatePerformanceMode();
                        break;

                    // Sent by the page on start-up: it shows update notices itself, so it gets the
                    // pending status and, right after an update, the version it replaced.
                    case "host-hello":
                        _pageShowsUpdates = true;
                        UpdateReadyButton.Visibility = Visibility.Collapsed;
                        // Only builds that can open their patch notes on request get the button.
                        PatchNotesButton.Visibility = HasCapability(root, "patch-notes") ? Visibility.Visible : Visibility.Collapsed;
                        if (_previousVersionForPage != null)
                        {
                            PostToPage(new { type = "genetics-lab-host", previousVersion = _previousVersionForPage });
                            _previousVersionForPage = null;
                        }
                        PostUpdateStatusToPage(GeneticsLabContentService.Instance.Status);
                        break;

                    case "apply-update":
                        Reload();
                        break;
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
                // The newest of the bundled build and the last update downloaded (GitHub or the hosted site).
                _content = GeneticsLabContentService.Instance.ResolveContent();

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

                if (_content != null)
                {
                    _webView.NavigationCompleted += GeneticsWebView_NavigationCompleted;
                    await NavigateToContentAsync(_content);
                }
                else
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                }

                _isInitialized = true;
                GeneticsLabContentService.Instance.CheckForUpdatesInBackground();
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

        private async Task NavigateToContentAsync(GeneticsLabContent content)
        {
            CoreWebView2 core = _webView!.CoreWebView2;

            // Hashed JS/CSS names change with their content, but images and OCR files keep their
            // names across builds, so a cached response could outlive the build it came from. The
            // profile belongs to Genetics Lab alone and only its HTTP cache is dropped, never the
            // page's saved projects in localStorage/IndexedDB.
            (bool changed, string? previousVersion) = GeneticsLabContentService.Instance.MarkLoaded(content);
            if (changed)
            {
                _previousVersionForPage = previousVersion;
                try { await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[GeneticsLab] Cache clear failed: {ex.Message}"); }
            }

            // Set again by the new page's "host-hello" if it shows update notices itself.
            _pageShowsUpdates = false;
            PatchNotesButton.Visibility = Visibility.Collapsed;

            core.ClearVirtualHostNameToFolderMapping(VirtualHost);
            core.SetVirtualHostNameToFolderMapping(VirtualHost, content.Folder, CoreWebView2HostResourceAccessKind.Allow);

            // The query makes index.html a fresh URL per build; the page itself ignores it.
            string build = content.BuildId is { Length: >= 12 } id ? $"?build={id[..12]}" : string.Empty;
            core.Navigate($"https://{VirtualHost}/index.html{build}");
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

        /// <summary>Reloads the page, switching to a newer Genetics Lab build if one was installed.</summary>
        public async void Reload()
        {
            try
            {
                if (_webView?.CoreWebView2 == null) return;

                GeneticsLabContent? newer = GeneticsLabContentService.Instance.FindNewerThan(_content);
                if (newer != null)
                {
                    GeneticsLabContent? previous = _content;
                    _content = GeneticsLabContentService.Instance.ResolveContent();
                    if (_content != null)
                    {
                        LoadingOverlay.Visibility = Visibility.Visible;
                        await NavigateToContentAsync(_content);
                        if (previous != null) GeneticsLabContentService.Instance.ReleaseContent(previous);
                        UpdateReadyButton.Visibility = Visibility.Collapsed;
                        return;
                    }
                }

                _webView.CoreWebView2.Reload();
            }
            catch
            {
                // ignore
            }
        }

        private void Reload_Click(object sender, RoutedEventArgs e) => Reload();

        // The patch notes live in the page (they ship with each build); the button asks it to open them.
        private void PatchNotes_Click(object sender, RoutedEventArgs e) => PostToPage(new { type = "open-patch-notes" });

        private static bool HasCapability(JsonElement hello, string capability) =>
            hello.TryGetProperty("capabilities", out JsonElement list) &&
            list.ValueKind == JsonValueKind.Array &&
            list.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == capability);

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

            if (_content != null)
            {
                GeneticsLabContentService.Instance.ReleaseContent(_content);
                _content = null;
            }
            UpdateReadyButton.Visibility = Visibility.Collapsed;
            PatchNotesButton.Visibility = Visibility.Collapsed;

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
