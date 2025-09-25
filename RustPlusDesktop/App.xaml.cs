using Microsoft.Win32;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using RustPlusDesk.Views;

namespace RustPlusDesk;

public partial class App : Application
{
    private static Mutex? _single;
    private const string SingleMutexName = "RustPlusDesk_SingleInstance";
    private const string PipeName = "RustPlusDeskLinkPipe";

    private MainWindow? _main;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        EnsureUrlProtocolRegistered();

        bool createdNew;
        _single = new Mutex(initiallyOwned: true, name: SingleMutexName, createdNew: out createdNew);

        if (!createdNew)
        {
            // schon laufend → Link (falls vorhanden) an laufende Instanz schicken und beenden
            if (e.Args.Length > 0 && e.Args[0].StartsWith("rustplus://", StringComparison.OrdinalIgnoreCase))
                _ = SendLinkToRunningInstanceAsync(e.Args[0]);
            Shutdown();
            return;
        }

        // erste/laufende Instanz
        _main = new MainWindow();
        _main.Show();

        // Pipe-Server für zukünftige Links starten
        _ = StartPipeServerAsync();

        // Falls diese Instanz selbst mit Link gestartet wurde: direkt verarbeiten
        if (e.Args.Length > 0 && e.Args[0].StartsWith("rustplus://", StringComparison.OrdinalIgnoreCase))
            _main.HandleRustPlusLink(e.Args[0]);
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

    private static async Task SendLinkToRunningInstanceAsync(string link)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            await client.ConnectAsync(1500);
            var data = Encoding.UTF8.GetBytes(link + "\n");
            await client.WriteAsync(data, 0, data.Length);
            await client.FlushAsync();
        }
        catch { /* wenn keiner lauscht: ignore */ }
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
                if (!string.IsNullOrWhiteSpace(link) &&
                    link.StartsWith("rustplus://", StringComparison.OrdinalIgnoreCase) &&
                    _main != null)
                {
                    _main.Dispatcher.Invoke(() =>
                    {
                        // Fenster in den Vordergrund holen
                        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
                        _main.Activate();
                        _main.Topmost = true; _main.Topmost = false;

                        _main.HandleRustPlusLink(link);
                    });
                }
            }
            catch
            {
                // Pipe neu starten, wenn irgendwas schief ging
            }
        }
    }
}
