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
        private CoreWebView2? _core;
        // The Genetics Lab build the WebView is mapped to; see GeneticsLabContentService.
        private GeneticsLabContent? _content;
        // Version of the build the current page replaced, handed to the page for "What's new".
        private string? _previousVersionForPage;
        // Builds that send "host-hello" show update notices themselves; for older ones the
        // header button is the only sign an update is waiting.
        private bool _pageShowsUpdates;
        private bool _isInitialized;
        private bool _isInitializing;
        // A failure stays on screen until Retry, so reopening the workspace cannot hide it.
        private bool _loadFailed;
        // Bumped whenever the browser is torn down, so a slow initialisation from before cannot
        // attach itself to (or report a failure over) a newer attempt.
        private int _browserAttempt;
        // The page navigation being waited on; about:blank on teardown is not one of them.
        private ulong? _navigationId;
        private CoreWebView2Environment? _environment;
        private readonly Func<GeneticsLabContent?> _resolveContent;
        private readonly Func<WebView2, Task> _ensureCoreAsync;
        private readonly Func<Task>? _tryDownloadMissingContentAsync;
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

        public GeneticsLabTabContent() : this(GeneticsLabContentService.Instance.ResolveContent)
        {
        }

        internal GeneticsLabTabContent(Func<GeneticsLabContent?> resolveContent, Func<WebView2, Task>? ensureCoreAsync = null)
        {
            _resolveContent = resolveContent;
            _ensureCoreAsync = ensureCoreAsync ?? EnsureCoreAsync;
            if (ensureCoreAsync == null)
                _tryDownloadMissingContentAsync = async () => await GeneticsLabContentService.Instance.CheckForUpdatesAsync(force: true, default);

            InitializeComponent();
            Loaded += GeneticsLabTabContent_Loaded;
            IsVisibleChanged += GeneticsLabTabContent_IsVisibleChanged;

            if (ensureCoreAsync == null && !System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
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
                // A queued notification can arrive after a reload already selected that build.
                RefreshUpdateStatus(GeneticsLabContentService.Instance.Status);
            }));
        }

        private void RefreshUpdateStatus(GeneticsLabUpdateStatus? status)
        {
            PostUpdateStatusToPage(status);
            UpdateReadyButton.Visibility = status?.State == "ready" && !_pageShowsUpdates &&
                GeneticsLabContentService.Instance.FindNewerThan(_content) != null
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void PostUpdateStatusToPage(GeneticsLabUpdateStatus? status)
        {
            if (status == null || _core == null) return;
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
                _core?.PostWebMessageAsJson(JsonSerializer.Serialize(message));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GeneticsLab] Posting to the page failed: {ex.Message}");
            }
        }

        private async void GeneticsLabTabContent_Loaded(object sender, RoutedEventArgs e)
        {
            if (IsVisible) await EnsureWebViewAsync();
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

        internal async Task EnsureWebViewAsync(bool recoverMissingContent = false)
        {
            // Loaded and IsVisibleChanged can both arrive before initialization finishes. Also,
            // minimizing/reopening the workspace must not silently dismiss an actionable failure.
            if (_webView != null || _isInitializing || _loadFailed) return;

            _isInitializing = true;
            int attempt = ++_browserAttempt;
            try
            {
                await InitializeWebViewAsync(attempt, recoverMissingContent);
            }
            finally
            {
                if (attempt == _browserAttempt) _isInitializing = false;
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
            if (!ReferenceEquals(sender, _core) || _core == null) return;
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

        private enum LoadFailure { MissingContent, Runtime, Navigation }

        private static bool HasEntryPoint(GeneticsLabContent? content) =>
            content != null && File.Exists(Path.Combine(content.Folder, "index.html"));

        // Hidden, not Collapsed: the browser's window still gets built and sized while it loads,
        // but it cannot cover the WPF overlay (an HWND always draws above WPF content).
        private void ShowLoading()
        {
            WebViewHost.Visibility = Visibility.Hidden;
            FailurePanel.Visibility = Visibility.Collapsed;
            LoadingOverlay.Visibility = Visibility.Visible;
        }

        private void ShowPage()
        {
            FailurePanel.Visibility = Visibility.Collapsed;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            WebViewHost.Visibility = Visibility.Visible;
        }

        private void ShowFailure(LoadFailure failure, string? diagnostic = null)
        {
            System.Diagnostics.Debug.WriteLine($"[GeneticsLab] {failure}: {diagnostic}");
            ShutdownWebView();
            _loadFailed = true;
            (FailureTitle.Text, FailureMessage.Text) = failure switch
            {
                LoadFailure.MissingContent => ("Genetics Lab files are missing",
                    "Connect to the internet and retry to download the files. If that doesn't work, reinstall RustPlus Desktop."),
                LoadFailure.Runtime => ("Genetics Lab couldn't start its browser",
                    "Install or repair the Microsoft Edge WebView2 Runtime, then retry. If it's already installed, restarting RustPlus Desktop may help."),
                _ => ("Genetics Lab couldn't load its page",
                    "Retry to reopen Genetics Lab. If it still won't load, repair or reinstall RustPlus Desktop.")
            };
            LoadingOverlay.Visibility = Visibility.Collapsed;
            WebViewHost.Visibility = Visibility.Collapsed;
            FailurePanel.Visibility = Visibility.Visible;
        }

        private async void Retry_Click(object sender, RoutedEventArgs e) => await RetryAsync();

        private async Task RetryAsync()
        {
            ShutdownWebView();
            await EnsureWebViewAsync(recoverMissingContent: true);
            UpdatePerformanceMode();
        }

        private async Task EnsureCoreAsync(WebView2 browser)
        {
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

            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: webViewDataFolder,
                options: envOptions);
            if (!ReferenceEquals(browser, _webView)) return;

            _environment = environment;
            await browser.EnsureCoreWebView2Async(environment);
            if (!ReferenceEquals(browser, _webView))
                throw new OperationCanceledException("Genetics Lab was closed while its browser started.");
        }

        private async Task InitializeWebViewAsync(int attempt, bool recoverMissingContent)
        {
            ShowLoading();
            LoadFailure failure = LoadFailure.MissingContent;
            try
            {
                // Resolve again on retry; the updater or a repair may have restored the files.
                _content = _resolveContent();
                if (!HasEntryPoint(_content) && recoverMissingContent && _tryDownloadMissingContentAsync != null)
                {
                    if (_content != null) GeneticsLabContentService.Instance.ReleaseContent(_content);
                    _content = null;
                    await _tryDownloadMissingContentAsync();
                    if (attempt != _browserAttempt) return;
                    _content = _resolveContent();
                }

                if (_content is not { } content || !HasEntryPoint(content))
                {
                    ShowFailure(failure);
                    return;
                }

                failure = LoadFailure.Runtime;
                var browser = new WebView2
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                _webView = browser;
                WebViewHost.Children.Add(browser);

                await _ensureCoreAsync(browser);
                if (attempt != _browserAttempt) return;

                CoreWebView2 core = _core = browser.CoreWebView2;
                core.WebMessageReceived += OnWebMessageReceived;
                core.NavigationStarting += GeneticsWebView_NavigationStarting;
                core.NavigationCompleted += GeneticsWebView_NavigationCompleted;
                core.ProcessFailed += GeneticsWebView_ProcessFailed;
                if (_environment != null)
                    _environment.ProcessInfosChanged += SharedEnvironment_ProcessInfosChanged;
                RefreshWebViewProcessIds();

                failure = LoadFailure.Navigation;
                await NavigateToContentAsync(browser, content);
                if (attempt != _browserAttempt) return;

                // The page itself is still loading; NavigationCompleted reveals it or reports a failure.
                _isInitialized = true;
                UpdatePerformanceMode();
                GeneticsLabContentService.Instance.CheckForUpdatesInBackground();
            }
            catch (Exception ex)
            {
                if (attempt == _browserAttempt) ShowFailure(failure, ex.Message);
            }
        }

        private async Task NavigateToContentAsync(WebView2 browser, GeneticsLabContent content)
        {
            CoreWebView2 core = browser.CoreWebView2;

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

        private void GeneticsWebView_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (!ReferenceEquals(sender, _core)) return;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) &&
                string.Equals(uri.Host, VirtualHost, StringComparison.OrdinalIgnoreCase))
            {
                _navigationId = e.NavigationId;
            }
        }

        private async void GeneticsWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!ReferenceEquals(sender, _core) || e.NavigationId != _navigationId) return;

            if (!e.IsSuccess)
            {
                ShowFailure(LoadFailure.Navigation, e.WebErrorStatus.ToString());
                return;
            }

            ShowPage();
            if (_performanceModeEnabled)
            {
                await KeepWebViewActiveAsync();
            }
        }

        // The browser or the page's renderer is gone: the WebView is blank and will not recover on
        // its own. GPU and utility processes restart by themselves and are left alone.
        private void GeneticsWebView_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
        {
            if (!ReferenceEquals(sender, _core)) return;
            if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited
                or CoreWebView2ProcessFailedKind.RenderProcessExited)
            {
                ShowFailure(e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited
                    ? LoadFailure.Runtime
                    : LoadFailure.Navigation, e.ProcessFailedKind.ToString());
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
            _webViewProcessIds = _environment?.GetProcessInfos()
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
            // No browser to reload: a failed or half-built start. Rebuild it instead of doing nothing.
            if (_webView is not { CoreWebView2: not null } browser)
            {
                await RetryAsync();
                return;
            }

            try
            {
                GeneticsLabContent? newer = GeneticsLabContentService.Instance.FindNewerThan(_content);
                if (newer != null)
                {
                    GeneticsLabContent? previous = _content;
                    GeneticsLabContent? next = GeneticsLabContentService.Instance.ResolveContent();
                    if (HasEntryPoint(next))
                    {
                        _content = next;
                        ShowLoading();
                        await NavigateToContentAsync(browser, next!);
                        if (previous != null) GeneticsLabContentService.Instance.ReleaseContent(previous);
                        UpdateReadyButton.Visibility = Visibility.Collapsed;
                        return;
                    }
                    if (next != null) GeneticsLabContentService.Instance.ReleaseContent(next);
                }

                ShowLoading();
                browser.CoreWebView2.Reload();
            }
            catch (Exception ex)
            {
                ShowFailure(LoadFailure.Navigation, ex.Message);
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

            // Any initialisation still in flight now belongs to a browser that is gone.
            _browserAttempt++;
            _isInitializing = false;
            _loadFailed = false;
            _navigationId = null;

            if (_environment != null)
            {
                try { _environment.ProcessInfosChanged -= SharedEnvironment_ProcessInfosChanged; } catch { }
            }

            if (_core != null)
            {
                try
                {
                    _core.WebMessageReceived -= OnWebMessageReceived;
                    _core.NavigationStarting -= GeneticsWebView_NavigationStarting;
                    _core.NavigationCompleted -= GeneticsWebView_NavigationCompleted;
                    _core.ProcessFailed -= GeneticsWebView_ProcessFailed;
                    // Unloads the page before the control goes, so the scanner's screen capture
                    // and the audio context are released by the page's own teardown rather than
                    // being cut off with the process.
                    _core.Navigate("about:blank");
                }
                catch { }
                _core = null;
            }

            if (_webView != null)
            {

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
            _environment = null;
            _isInitialized = false;
            FailurePanel.Visibility = Visibility.Collapsed;
            WebViewHost.Visibility = Visibility.Hidden;
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
