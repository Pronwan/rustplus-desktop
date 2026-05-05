using Microsoft.Win32;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using RustPlusDesk.Views;
using RustPlusDesk.Services;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace RustPlusDesk;

public partial class App : Application
{
    private static Mutex? _single;
    private const string SingleMutexName = "RustPlusDesk_SingleInstance";
    private const string PipeName = "RustPlusDeskLinkPipe";

    private MainWindow? _main;
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        SetupTrayIcon();

        // Start polling if enabled and we have a server
        if (TrackingService.IsBackgroundTrackingEnabled)
        {
            var (host, port, name) = TrackingService.LastServer;
            if (!string.IsNullOrEmpty(host))
                TrackingService.StartPolling(host, port, name);
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
        _trayIcon.Text = "Rust+ Desk Tracker";
        _trayIcon.Visible = true;

        var menu = new System.Windows.Forms.ContextMenuStrip();
        
        // Dynamic update on open
        menu.Opening += (s, e) =>
        {
            menu.Items.Clear();
            var status = TrackingService.IsTracking ? "Active" : "Idle";
            var last = TrackingService.LastPullTime?.ToString("HH:mm:ss") ?? "--:--:--";
            
            var statusItem = new System.Windows.Forms.ToolStripMenuItem($"Tracking: {status}");
            statusItem.Enabled = false;
            menu.Items.Add(statusItem);
            
            var lastItem = new System.Windows.Forms.ToolStripMenuItem($"Last update: {last}");
            lastItem.Enabled = false;
            menu.Items.Add(lastItem);
            
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Open Rust+ Desk", null, (s, ex) => ShowMainWindow());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, ex) => {
                _trayIcon.Visible = false;
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
        
        // Also update tray tooltip periodically or on event
        TrackingService.OnOnlinePlayersUpdated += () => {
            var last = TrackingService.LastPullTime?.ToString("HH:mm:ss") ?? "--:--";
            if (_trayIcon != null)
                _trayIcon.Text = $"Rust+ Desk (Tracking {last})";
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon != null) _trayIcon.Visible = false;
        base.OnExit(e);
    }

    /// <summary>Show a balloon-tip notification via the tray icon. Safe to call from any thread.</summary>
    public static void ShowTrayToast(string title, string body, int timeoutMs = 5000)
    {
        try
        {
            if (Current is App app && app._trayIcon != null && app._trayIcon.Visible)
            {
                app._trayIcon.ShowBalloonTip(timeoutMs, title, body, System.Windows.Forms.ToolTipIcon.Info);
            }
        }
        catch { /* tray may not be initialised yet */ }
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
}
