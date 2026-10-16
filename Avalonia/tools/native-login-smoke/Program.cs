using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using RustPlusDesk.Avalonia.Services;
using System.Net;
using System.Net.Sockets;

internal class Program
{
    [STAThread]
    public static int Main(string[] args) => AppBuilder.Configure<SmokeApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}

internal class SmokeApp : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new Window { Title = "Local sign-in browser smoke check", Width = 600, Height = 400 };
        lifetime.MainWindow = window;
        window.Opened += async (_, _) =>
        {
            try
            {
                using var port = new TcpListener(IPAddress.Loopback, 0);
                port.Start();
                int number = ((IPEndPoint)port.LocalEndpoint).Port;
                port.Stop();
                using var listener = new HttpListener();
                string root = $"http://localhost:{number}/";
                listener.Prefixes.Add(root);
                listener.Start();
                var callback = new Uri(root + "callback/fixture");
                var serve = Task.Run(async () =>
                {
                    var request = await listener.GetContextAsync();
                    request.Response.Redirect(callback.AbsoluteUri + "?code=fixture-only");
                    request.Response.Close();
                });
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                var browser = new CompanionLoginBrowser(window);
                var result = await browser.AuthenticateAsync(new Uri(root + "login"), callback, timeout.Token);
                await serve;
                if (result?.Query != "?code=fixture-only") throw new Exception("Callback mismatch");
                using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    await browser.AuthenticateAsync(new Uri("about:blank"), callback, cancel.Token);
                    throw new Exception("Cancellation did not end authentication");
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
                Console.WriteLine("PASS: Native sign-in window, local redirect and cancellation");
                lifetime.Shutdown(0);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                lifetime.Shutdown(1);
            }
        };
        base.OnFrameworkInitializationCompleted();
    }
}
