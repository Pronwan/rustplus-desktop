using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows.Threading;
using RustPlusDesk.Views;
using RustPlusDesk.Services;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RustPlusDesk.Services.Auth;
using Application = System.Windows.Application;
using Velopack;

namespace RustPlusDesk;

public partial class App : Application
{
    private static Mutex? _single;
    private const string SingleMutexName = "RustPlusDesk_SingleInstance";
    private const string PipeName = "RustPlusDeskLinkPipe";

    private MainWindow? _main;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _resourceCache = new();
    private int _languageApplyVersion;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        AssemblyLoadContext.Default.Resolving += ResolveSatelliteAssemblyFromLangFolder;
        SetLanguage(applySynchronously: true);
        base.OnStartup(e);

        // Run legacy Inno Setup cleanup in the background
        Task.Run(CleanupLegacyInnoSetupInstallation);

        EnsureUrlProtocolRegistered();

        bool isBackgroundArg = e.Args.Contains("--background");
        bool createdNew;
        _single = new Mutex(initiallyOwned: true, name: SingleMutexName, createdNew: out createdNew);

        if (!createdNew)
        {
            // Already running
            if (e.Args.Length > 0 && e.Args[0].StartsWith("rustplus://", StringComparison.OrdinalIgnoreCase))
                _ = SendLinkToRunningInstanceAsync(e.Args[0]);
            else if (!isBackgroundArg)
                _ = SendCommandToRunningInstanceAsync("SHOWUI");

            Shutdown();
            return;
        }

        // Initialize Supabase Client
        _ = SupabaseAuthManager.InitializeAsync();

        SetupTrayIcon();

        // Start polling if enabled
        if (TrackingService.IsBackgroundTrackingEnabled)
        {
            var (host, port, name) = TrackingService.LastServer;
            TrackingService.StartPolling(host ?? "", port, name ?? "", TrackingService.LastBMId);
        }

        if (isBackgroundArg && TrackingService.StartMinimizedEnabled)
        {
            // Started by Windows (auto-start) and minimized is enabled
            if (e.Args.Length > 0 && e.Args[0].StartsWith("rustplus://", StringComparison.OrdinalIgnoreCase))
                ShowMainWindow();
        }
        else
        {
            // Manual start by user, or auto-start with minimized disabled
            ShowMainWindow();
        }

        _ = StartPipeServerAsync();

        if (e.Args.Length > 0 && e.Args[0].StartsWith("rustplus://", StringComparison.OrdinalIgnoreCase))
            _main?.HandleRustPlusLink(e.Args[0]);
    }

    private static Assembly? ResolveSatelliteAssemblyFromLangFolder(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName.Name) ||
            !assemblyName.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(assemblyName.CultureName))
        {
            return null;
        }

        string satellitePath = Path.Combine(
            AppContext.BaseDirectory,
            "lang",
            assemblyName.CultureName,
            $"{assemblyName.Name}.dll");

        return File.Exists(satellitePath)
            ? context.LoadFromAssemblyPath(satellitePath)
            : null;
    }

    private void ShowMainWindow()
    {
        if (_main == null)
        {
            _main = new MainWindow();
            _main.Closed += (s, ev) => _main = null;
        }
        _main.Show();
        _main.WindowState = WindowState.Normal;
        _main.Activate();
        _main.Topmost = true; _main.Topmost = false;
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new System.Windows.Forms.NotifyIcon();
        _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!);
        _trayIcon.Text = RustPlusDesk.Properties.Resources.TrayIconDefault;
        _trayIcon.Visible = true;

        var menu = new System.Windows.Forms.ContextMenuStrip();
        
        // Dynamic update on open
        menu.Opening += (s, e) =>
        {
            menu.Items.Clear();
            var status = TrackingService.IsTracking ? "Active" : "Idle";
            var last = TrackingService.LastPullTime?.ToString("HH:mm:ss") ?? "--:--:--";
            
            var statusItem = new System.Windows.Forms.ToolStripMenuItem(string.Format(RustPlusDesk.Properties.Resources.TrayTrackingStatus, status));
            statusItem.Enabled = false;
            menu.Items.Add(statusItem);
            
            var lastItem = new System.Windows.Forms.ToolStripMenuItem(string.Format(RustPlusDesk.Properties.Resources.TrayLastUpdate, last));
            lastItem.Enabled = false;
            menu.Items.Add(lastItem);
            
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(RustPlusDesk.Properties.Resources.OpenRustPlusDesk, null, (s, ex) => ShowMainWindow());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(RustPlusDesk.Properties.Resources.Exit, null, (s, ex) => {
                if (_trayIcon != null) _trayIcon.Visible = false;
                Current.Shutdown();
            });
        };

        _trayIcon.MouseUp += (s, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                // Ensure the window exists to provide a handle for focus management
                if (_main == null)
                {
                    _main = new MainWindow();
                    _main.Closed += (s, ev) => _main = null;
                }

                // This is a known fix for NotifyIcon context menus in WPF.
                // It ensures the menu opens on the first click and closes when clicking away.
                var handle = new System.Windows.Interop.WindowInteropHelper(_main).Handle;
                SetForegroundWindow(handle);

                menu.Show(System.Windows.Forms.Control.MousePosition);
            }
        };

        _trayIcon.DoubleClick += (s, e) => ShowMainWindow();

        CultureChanged += () =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_trayIcon != null)
                {
                    var last = TrackingService.LastPullTime?.ToString("HH:mm:ss") ?? "--:--";
                    _trayIcon.Text = TrackingService.IsTracking 
                        ? string.Format(RustPlusDesk.Properties.Resources.TrayIconTracking, last)
                        : RustPlusDesk.Properties.Resources.TrayIconDefault;
                }
            });
        };
        
        // Also update tray tooltip periodically or on event
        TrackingService.OnOnlinePlayersUpdated += () => {
            var last = TrackingService.LastPullTime?.ToString("HH:mm:ss") ?? "--:--";
            Dispatcher.Invoke(() => {
                try {
                    if (_trayIcon != null)
                        _trayIcon.Text = string.Format(RustPlusDesk.Properties.Resources.TrayIconTracking, last);
                } catch { }
            });
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon != null) _trayIcon.Visible = false;
        base.OnExit(e);
    }

    private static void EnsureUrlProtocolRegistered()
    {
        try
        {
            const string scheme = "rustplus";
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{scheme}");
            key.SetValue("", "URL: rustplus Protocol");
            key.SetValue("URL Protocol", "");
            using var shell = key.CreateSubKey(@"shell\open\command");
            var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;
            shell.SetValue("", $"\"{exe}\" \"%1\"");
        }
        catch { /* unkritisch */ }
    }

    private static async Task SendCommandToRunningInstanceAsync(string cmd)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            await client.ConnectAsync(1500);
            var data = Encoding.UTF8.GetBytes(cmd + "\n");
            await client.WriteAsync(data, 0, data.Length);
            await client.FlushAsync();
        }
        catch { }
    }

    private static async Task SendLinkToRunningInstanceAsync(string link) => await SendCommandToRunningInstanceAsync(link);

    public void SetLanguage(bool applySynchronously = false)
    {
        try
        {
            string lang = TrackingService.SelectedLanguage;
            CultureInfo culture;

            if (string.IsNullOrEmpty(lang))
            {
                culture = CultureInfo.InstalledUICulture;
            }
            else
            {
                culture = new CultureInfo(lang);
            }

            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = culture;

            // Also set it for the generated Resources class
            RustPlusDesk.Properties.Resources.Culture = culture;

            int version = Interlocked.Increment(ref _languageApplyVersion);

            if (applySynchronously)
            {
                ApplyDynamicResources(GetDynamicResourceMap(culture));
                CultureChanged?.Invoke();
            }
            else
            {
                _ = ApplyLanguageResourcesAsync(culture, version);
            }
        }
        catch { }
    }

    public static event Action? CultureChanged;

    private async Task ApplyLanguageResourcesAsync(CultureInfo culture, int version)
    {
        try
        {
            var resourceMap = await Task.Run(() => GetDynamicResourceMap(culture));
            if (version != Volatile.Read(ref _languageApplyVersion))
                return;

            await Dispatcher.InvokeAsync(() =>
            {
                if (version != Volatile.Read(ref _languageApplyVersion))
                    return;

                ApplyDynamicResources(resourceMap);
                CultureChanged?.Invoke();
            }, DispatcherPriority.Background);
        }
        catch { }
    }

    private static IReadOnlyDictionary<string, string> GetDynamicResourceMap(CultureInfo culture)
    {
        string cacheKey = string.IsNullOrEmpty(culture.Name) ? "invariant" : culture.Name;
        return _resourceCache.GetOrAdd(cacheKey, _ => BuildDynamicResourceMap(culture));
    }

    private static IReadOnlyDictionary<string, string> BuildDynamicResourceMap(CultureInfo culture)
    {
        var rm = RustPlusDesk.Properties.Resources.ResourceManager;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        var neutralSet = rm.GetResourceSet(CultureInfo.InvariantCulture, true, false);
        if (neutralSet != null)
        {
            foreach (System.Collections.DictionaryEntry entry in neutralSet)
            {
                if (entry.Key is string key && entry.Value is string value && !string.IsNullOrWhiteSpace(value))
                    values[key] = value;
            }
        }

        var resourceSet = rm.GetResourceSet(culture, true, true);
        if (resourceSet != null)
        {
            foreach (System.Collections.DictionaryEntry entry in resourceSet)
            {
                if (entry.Key is string key && entry.Value is string value && !string.IsNullOrWhiteSpace(value))
                    values[key] = value;
            }
        }

        return values;
    }

    private void ApplyDynamicResources(IReadOnlyDictionary<string, string> resourceMap)
    {
        foreach (var entry in resourceMap)
        {
            if (!Resources.Contains(entry.Key) || !Equals(Resources[entry.Key], entry.Value))
                Resources[entry.Key] = entry.Value;
        }
    }

    private async Task StartPipeServerAsync()
    {
        while (true)
        {
            using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                                                         PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try
            {
                await server.WaitForConnectionAsync();
                using var reader = new StreamReader(server, Encoding.UTF8);
                var link = await reader.ReadLineAsync();
                if (!string.IsNullOrWhiteSpace(link) && _main != null)
                {
                    _main.Dispatcher.Invoke(() =>
                    {
                        if (link == "SHOWUI")
                        {
                            ShowMainWindow();
                        }
                        else if (link.StartsWith("rustplus://", StringComparison.OrdinalIgnoreCase))
                        {
                            ShowMainWindow();
                            _main.HandleRustPlusLink(link);
                        }
                    });
                }
                else if (link == "SHOWUI")
                {
                    Dispatcher.Invoke(ShowMainWindow);
                }
            }
            catch
            {
                // Pipe neu starten, wenn irgendwas schief ging
            }
        }
    }

    private static void CleanupLegacyInnoSetupInstallation()
    {
        try
        {
            string baseDir = AppContext.BaseDirectory;
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!baseDir.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase))
            {
                // Not running from AppData (e.g. running from C:\Program Files or development folder)
                return;
            }

            // Find the Inno Setup uninstaller path from the registry.
            // AppID could have one or two closing braces due to Inno Setup escaping: {E8E0C4C1-2E2F-4D2D-9BE7-3B19F0C1ABCD}_is1 or {E8E0C4C1-2E2F-4D2D-9BE7-3B19F0C1ABCD}}_is1
            string[] possibleKeys = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{E8E0C4C1-2E2F-4D2D-9BE7-3B19F0C1ABCD}}_is1",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{E8E0C4C1-2E2F-4D2D-9BE7-3B19F0C1ABCD}_is1"
            };

            string? uninstallString = null;
            foreach (var keyPath in possibleKeys)
            {
                // Try 64-bit Registry View
                using (var baseKey64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var subKey64 = baseKey64.OpenSubKey(keyPath))
                {
                    uninstallString = subKey64?.GetValue("UninstallString")?.ToString();
                }

                // Try 32-bit Registry View if not found
                if (string.IsNullOrEmpty(uninstallString))
                {
                    using (var baseKey32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                    using (var subKey32 = baseKey32.OpenSubKey(keyPath))
                    {
                        uninstallString = subKey32?.GetValue("UninstallString")?.ToString();
                    }
                }

                if (!string.IsNullOrEmpty(uninstallString))
                {
                    break;
                }
            }

            if (string.IsNullOrEmpty(uninstallString))
            {
                return; // Inno Setup version is not installed.
            }

            string uninstallerExe = uninstallString.Replace("\"", "").Trim();
            if (!File.Exists(uninstallerExe))
            {
                return;
            }

            // Run the uninstaller silently (will prompt for UAC)
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = uninstallerExe,
                Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART",
                UseShellExecute = true,
                Verb = "runas"
            };

            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to trigger legacy cleanup: {ex.Message}");
        }
    }
}
