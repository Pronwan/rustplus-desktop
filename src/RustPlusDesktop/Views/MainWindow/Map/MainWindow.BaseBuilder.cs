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
            // RustHelp item icons bundled with the app (tools/Download-BuilderItemIcons.ps1). The builder
            // falls back to its own SVGs when this host is absent, e.g. in the MapParser dev server.
            string itemIcons = Path.Combine(AppContext.BaseDirectory, "Assets", "icons", "builder-items");
            if (Directory.Exists(itemIcons))
                view.CoreWebView2.SetVirtualHostNameToFolderMapping("rustplus-items.local", itemIcons, CoreWebView2HostResourceAccessKind.DenyCors);
            view.CoreWebView2.AddWebResourceRequestedFilter("https://rustplus-api.invalid/archipel/*", CoreWebView2WebResourceContext.All);
            view.CoreWebView2.WebResourceRequested += (_, args) => TryRelayArchipelRequest(args, new Uri(args.Request.Uri), _map3DWebViewEnvironment, () => ReferenceEquals(view, _baseBuilderWebView));
            view.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != host) args.Cancel = true;
            };
            view.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
            // The workshop's full screen button and F11 post this; the host owns the window, so it does the switching.
            // The Together panel posts objects ({ type: "coop_*" }); the app does the talking to the platform.
            view.CoreWebView2.WebMessageReceived += (_, args) =>
            {
                string? message = null;
                try { message = args.TryGetWebMessageAsString(); } catch { }
                if (string.Equals(message?.Trim('"'), "toggle_fullscreen", StringComparison.OrdinalIgnoreCase))
                {
                    Dispatcher.Invoke(ToggleBaseBuilderFullscreen);
                    return;
                }

                Newtonsoft.Json.Linq.JObject? json = null;
                try { json = Newtonsoft.Json.Linq.JObject.Parse(args.WebMessageAsJson); } catch { }
                var type = json?["type"]?.ToString() ?? "";
                if (type.StartsWith("coop_", StringComparison.Ordinal))
                    Dispatcher.InvokeAsync(() => HandleBuildTogetherMessageAsync(json!));
                else if (type.StartsWith("app_toast_", StringComparison.Ordinal))
                    Dispatcher.InvokeAsync(() => HandleBuilderToastMessage(json!));
                else if (type == "workshop_draft_flushed" && json!["id"]?.ToString() is { } flushId
                         && _builderDraftFlushes.TryGetValue(flushId, out var flushed))
                    flushed.TrySetResult(json["result"]?.ToString() ?? "failed");
            };
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
                            bool video = string.Equals(Path.GetExtension(args.ResultFilePath), ".webm", StringComparison.OrdinalIgnoreCase);
                            var dialog = new SaveFileDialog
                            {
                                FileName = Path.GetFileName(args.ResultFilePath),
                                DefaultExt = video ? ".webm" : ".json",
                                Filter = video ? "Showcase video (*.webm)|*.webm" : "CopyPaste base (*.json)|*.json",
                                AddExtension = true
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
                if (!BaseBuilderPanel.IsVisible) _ = CloseBaseBuilderWorkspaceAsync();
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

    private Window? _baseBuilderFullscreenWindow;

    // Moves the workshop's WebView into a borderless maximised window and back (the same approach as the 3D map).
    private void ToggleBaseBuilderFullscreen()
    {
        if (_baseBuilderWebView == null) return;
        bool on;
        if (_baseBuilderFullscreenWindow == null)
        {
            BaseBuilderHost.Children.Remove(_baseBuilderWebView);
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                WindowState = WindowState.Maximized,
                ResizeMode = ResizeMode.NoResize,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(14, 17, 23)),
                Content = _baseBuilderWebView
            };
            window.PreviewKeyDown += (_, ev) =>
            {
                if (ev.Key == System.Windows.Input.Key.F11) { ev.Handled = true; ToggleBaseBuilderFullscreen(); }
            };
            window.Closed += (_, _) => ExitBaseBuilderFullscreen();
            _baseBuilderFullscreenWindow = window;
            window.Show();
            on = true;
        }
        else { ExitBaseBuilderFullscreen(); on = false; }
        try { _baseBuilderWebView?.CoreWebView2?.ExecuteScriptAsync($"if (window.setFullscreenState) window.setFullscreenState({on.ToString().ToLowerInvariant()});"); } catch { }
    }

    private void ExitBaseBuilderFullscreen()
    {
        var window = _baseBuilderFullscreenWindow;
        if (window == null) return;
        _baseBuilderFullscreenWindow = null;
        window.Content = null;
        if (_baseBuilderWebView != null && _baseBuilderWebView.Parent == null) BaseBuilderHost.Children.Add(_baseBuilderWebView);
        if (window.IsLoaded) window.Close();
    }

    private void BaseBuilderFailed(string message)
    {
        BaseBuilderLoadingText.Text = message;
        BaseBuilderLoading.Visibility = Visibility.Visible;
        BaseBuilderRetry.Visibility = Visibility.Visible;
    }

    private static readonly TimeSpan BaseBuilderDraftTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Draft writes the builder was asked for, by id, until it answers (workshop_draft_flushed).</summary>
    private readonly System.Collections.Generic.Dictionary<string, TaskCompletionSource<string>> _builderDraftFlushes = new();

    /// <summary>
    /// Writes the builder's recovery draft now and waits until IndexedDB has it. "saved", "nothing" (no draft to keep),
    /// "failed", "unsupported" (a viewer build without the call) or "unanswered" (no answer in time).
    /// </summary>
    private async Task<string> FlushBaseBuilderDraftAsync()
    {
        var core = _baseBuilderWebView?.CoreWebView2;
        if (core == null) return "nothing";
        var id = Guid.NewGuid().ToString("N");
        var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _builderDraftFlushes[id] = answer;
        try
        {
            var asked = await core.ExecuteScriptAsync($"window.flushWorkshopDraft ? window.flushWorkshopDraft('{id}') : false")
                .WaitAsync(BaseBuilderDraftTimeout);
            if (asked != "true") return "unsupported";
            return await answer.Task.WaitAsync(BaseBuilderDraftTimeout);
        }
        catch (Exception ex)
        {
            AppendLog($"[Base Builder] The recovery draft did not answer: {ex.Message}");
            return "unanswered";
        }
        finally { _builderDraftFlushes.Remove(id); }
    }

    // Leaving the workshop frees it: the recovery draft is written first (the workshop reopens with "Continue"), then the
    // WebView and its renderer, 3D scene and base are disposed instead of lingering suspended in memory. A draft that
    // could not be written keeps the builder alive instead, so the edits are still there to go back to.
    private async Task CloseBaseBuilderWorkspaceAsync()
    {
        ExitBaseBuilderFullscreen();
        var view = _baseBuilderWebView;
        if (view == null) return;
        var draft = await FlushBaseBuilderDraftAsync();
        // The player may have come back (or reopened it) while the draft was saving.
        if (!ReferenceEquals(view, _baseBuilderWebView) || BaseBuilderPanel.IsVisible) return;
        if (draft is "failed" or "unanswered")
        {
            ShowInfoSnackbar("Base Builder",
                "Your latest changes could not be saved as a recovery draft, so the workshop stays open in the background. Go back to it and export the base to keep them.",
                Wpf.Ui.Controls.ControlAppearance.Caution);
            return;
        }
        BaseBuilderHost_Unloaded(this, new RoutedEventArgs());
        BaseBuilderLoading.Visibility = Visibility.Collapsed;
    }

    /// <summary>The app is quitting: the draft is written and any co-op session left while the page still runs.</summary>
    private async Task CloseBaseBuilderOnExitAsync()
    {
        if (_baseBuilderWebView == null) return;
        var draft = await FlushBaseBuilderDraftAsync();
        if (draft is "failed" or "unanswered") AppendLog($"[Base Builder] Quitting without a fresh recovery draft ({draft}).");
        await LeaveBuildTogetherOnCloseAsync().WaitAsync(BaseBuilderDraftTimeout);
    }

    private void BaseBuilderHost_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_baseBuilderWebView != null) _ = LeaveBuildTogetherOnCloseAsync();
        _baseBuilderWebView?.Dispose();
        _baseBuilderWebView = null;
        BaseBuilderHost.Children.Clear();
    }

    private void BaseBuilderRetry_Click(object sender, RoutedEventArgs e)
    {
        BaseBuilderHost_Unloaded(sender, e);
        _ = OpenBaseBuilderWorkspaceAsync();
    }

    private void BaseBuilder_CloseRequested(object sender, RoutedEventArgs e)
    {
        ExitBaseBuilderFullscreen();
        ReturnToLastWorkspace();
    }
}
