using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private WebView2? _baseBuilderWebView;
    private bool _baseBuilderOpening;

    private async Task OpenBaseBuilderWorkspaceAsync()
    {
        if (_baseBuilderWebView != null) { _baseBuilderWebView.CoreWebView2?.Resume(); return; }
        if (_baseBuilderOpening) return;
        _baseBuilderOpening = true;
        BaseBuilderLoading.Visibility = Visibility.Visible;
        BaseBuilderRetry.Visibility = Visibility.Collapsed;
        BaseBuilderLoadingText.Text = "Opening your building workshop…";
        try
        {
            string root = ResolveMap3DViewerSourceRoot()
                ?? throw new DirectoryNotFoundException("Base Builder files are missing. Reinstall or rebuild RustPlus Desktop to restore them.");
            const string host = "rustplus3d.local";
            string dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RustPlusDesk", "WebView2");
            _map3DWebViewEnvironment ??= await CoreWebView2Environment.CreateAsync(userDataFolder: dataFolder);
            var view = new WebView2();
            _baseBuilderWebView = view;
            BaseBuilderHost.Children.Add(view);
            await view.EnsureCoreWebView2Async(_map3DWebViewEnvironment);
            view.CoreWebView2.Settings.IsZoomControlEnabled = false;
            // Same origin and profile as the map, so its base library is available in both tools.
            view.CoreWebView2.SetVirtualHostNameToFolderMapping(host, root, CoreWebView2HostResourceAccessKind.DenyCors);
            view.CoreWebView2.AddWebResourceRequestedFilter("https://rustplus-api.invalid/archipel/*", CoreWebView2WebResourceContext.All);
            view.CoreWebView2.WebResourceRequested += (_, args) => TryRelayArchipelRequest(args, new Uri(args.Request.Uri), _map3DWebViewEnvironment, () => ReferenceEquals(view, _baseBuilderWebView));
            view.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != host) args.Cancel = true;
            };
            view.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
            view.CoreWebView2.DownloadStarting += (_, args) =>
            {
                var deferral = args.GetDeferral();
                // Show modal UI after the WebView callback returns; nested message loops can hang it.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    using (deferral)
                    {
                        args.Handled = true;
                        try
                        {
                            var dialog = new SaveFileDialog
                            {
                                FileName = Path.GetFileName(args.ResultFilePath),
                                DefaultExt = ".json", Filter = "CopyPaste base (*.json)|*.json", AddExtension = true
                            };
                            if (dialog.ShowDialog(this) == true) args.ResultFilePath = dialog.FileName;
                            else args.Cancel = true;
                        }
                        catch (Exception ex) { args.Cancel = true; AppendLog($"[Base Builder] Export canceled: {ex.Message}"); }
                    }
                }));
            };
            view.CoreWebView2.ProcessFailed += (_, _) => BaseBuilderFailed("The workshop renderer stopped. Your last recovery draft will be available when you reopen it.");
            view.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess) BaseBuilderLoading.Visibility = Visibility.Collapsed;
                else BaseBuilderFailed("Could not open Base Builder. Please try again.");
                if (!BaseBuilderPanel.IsVisible) _ = SuspendBaseBuilderAsync();
            };
            view.CoreWebView2.Navigate($"https://{host}/index.html?embedded=1&workspace=base-builder&v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
        }
        catch (Exception ex)
        {
            AppendLog($"[Base Builder] {ex.Message}");
            BaseBuilderFailed(Services.WebView2Diagnostics.Explain(ex));
        }
        finally { _baseBuilderOpening = false; }
    }

    private void BaseBuilderFailed(string message)
    {
        BaseBuilderLoadingText.Text = message;
        BaseBuilderLoading.Visibility = Visibility.Visible;
        BaseBuilderRetry.Visibility = Visibility.Visible;
    }

    private async Task SuspendBaseBuilderAsync()
    {
        try
        {
            if (_baseBuilderWebView?.CoreWebView2 is not { } core) return;
            await core.TrySuspendAsync();
            if (BaseBuilderPanel.IsVisible) core.Resume();
        }
        catch (Exception ex) { AppendLog($"[Base Builder] Could not pause workshop: {ex.Message}"); }
    }

    private void BaseBuilderHost_Unloaded(object sender, RoutedEventArgs e)
    {
        _baseBuilderWebView?.Dispose();
        _baseBuilderWebView = null;
        BaseBuilderHost.Children.Clear();
    }

    private void BaseBuilderRetry_Click(object sender, RoutedEventArgs e)
    {
        BaseBuilderHost_Unloaded(sender, e);
        _ = OpenBaseBuilderWorkspaceAsync();
    }

    private void BaseBuilder_CloseRequested(object sender, RoutedEventArgs e) => ReturnToLastWorkspace();
}
