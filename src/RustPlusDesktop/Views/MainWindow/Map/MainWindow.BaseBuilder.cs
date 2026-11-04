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

    // The workshop page's workspace parameter. Both builders share the panel and its WebView; only one page runs at a time.
    private const string BaseBuilderWorkspace = "base-builder";
    private const string ShipBuilderWorkspace = "ship-builder";
    private string _builderWorkspace = BaseBuilderWorkspace;
    private string BuilderName => _builderWorkspace == ShipBuilderWorkspace ? "Ship Builder" : "Base Builder";

    private async Task OpenBaseBuilderWorkspaceAsync(string workspace)
    {
        if (_baseBuilderWebView != null && workspace != _builderWorkspace)
        {
            if (_baseBuilderOpening || !await CloseBuilderForOtherWorkspaceAsync(workspace)) return;
        }
        if (_baseBuilderWebView != null) { _baseBuilderWebView.CoreWebView2?.Resume(); return; }
        if (_baseBuilderOpening) return;
        _baseBuilderOpening = true;
        _builderWorkspace = workspace;
        BaseBuilderTitle.Text = $"Tools / {BuilderName}";
        UpdateBuilderModeUi();
        System.Windows.Automation.AutomationProperties.SetName(BaseBuilderClose, $"Close {BuilderName}");
        BaseBuilderLoading.Visibility = Visibility.Visible;
        BaseBuilderRetry.Visibility = Visibility.Collapsed;
        BaseBuilderLoadingText.Text = workspace == ShipBuilderWorkspace ? "Opening your shipyard…" : "Opening your building workshop…";
        try
        {
            string root = ResolveMap3DViewerSourceRoot()
                ?? throw new DirectoryNotFoundException($"{BuilderName} files are missing. Reinstall or rebuild RustPlus Desktop to restore them.");
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
            MapBuilderItemIcons(view.CoreWebView2);
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
                else if (type == "builder_reply" && json!["id"]?.ToString() is { } replyId
                         && _builderReplies.TryGetValue(replyId, out var reply))
                    reply.TrySetResult(json["result"]?.ToString() ?? "failed");
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
                        catch (Exception ex) { args.Cancel = true; AppendLog($"[{BuilderName}] Export canceled: {ex.Message}"); }
                    }
                }));
            };
            view.CoreWebView2.ProcessFailed += (_, _) => BaseBuilderFailed("The workshop renderer stopped. Your last recovery draft will be available when you reopen it.");
            view.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess) BaseBuilderLoading.Visibility = Visibility.Collapsed;
                else BaseBuilderFailed($"Could not open {BuilderName}. Please try again.");
                if (!BaseBuilderPanel.IsVisible) _ = CloseBaseBuilderWorkspaceAsync();
            };
            view.CoreWebView2.Navigate($"https://{host}/index.html?embedded=1&workspace={_builderWorkspace}&v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
        }
        catch (Exception ex)
        {
            AppendLog($"[{BuilderName}] {ex.Message}");
            BaseBuilderFailed(Services.WebView2Diagnostics.Explain(ex));
        }
        finally { _baseBuilderOpening = false; }
    }

    /// <summary>
    /// RustHelp item icons bundled with the app (tools/Download-BuilderItemIcons.ps1), for the builder's belt, wheel and
    /// item browser. Both the workshop and the 3D map build with it, so both need the host. Without it the builder falls
    /// back to its own SVGs, which only works where the lookup of the missing host fails quickly (the MapParser dev server).
    /// </summary>
    internal static void MapBuilderItemIcons(CoreWebView2 core)
    {
        string itemIcons = Path.Combine(AppContext.BaseDirectory, "Assets", "icons", "builder-items");
        if (Directory.Exists(itemIcons))
            core.SetVirtualHostNameToFolderMapping("rustplus-items.local", itemIcons, CoreWebView2HostResourceAccessKind.DenyCors);
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

    /// <summary>Work the builder was asked to do, by id, until it answers (builder_reply).</summary>
    private readonly System.Collections.Generic.Dictionary<string, TaskCompletionSource<string>> _builderReplies = new();

    /// <summary>
    /// Runs one of the builder's window functions that answer by message, and waits for the answer: "saved", "nothing",
    /// "failed", or here "unsupported" (a viewer build without the function) and "unanswered" (no answer in time).
    /// </summary>
    private async Task<string> AskBaseBuilderAsync(string function, TimeSpan? timeout = null)
    {
        var wait = timeout ?? BaseBuilderDraftTimeout;
        var core = _baseBuilderWebView?.CoreWebView2;
        if (core == null) return "nothing";
        var id = Guid.NewGuid().ToString("N");
        var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _builderReplies[id] = answer;
        try
        {
            var asked = await core.ExecuteScriptAsync($"window.{function} ? window.{function}('{id}') : false")
                .WaitAsync(wait);
            if (asked != "true") return "unsupported";
            return await answer.Task.WaitAsync(wait);
        }
        catch (Exception ex)
        {
            AppendLog($"[Base Builder] {function} did not answer: {ex.Message}");
            return "unanswered";
        }
        finally { _builderReplies.Remove(id); }
    }

    /// <summary>Writes the builder's recovery draft now and waits until IndexedDB has it.</summary>
    private Task<string> FlushBaseBuilderDraftAsync(TimeSpan? timeout = null) => AskBaseBuilderAsync("flushWorkshopDraft", timeout);

    // Leaving a tab has time to wait for a big base's IndexedDB write; a reply that comes after the deadline is dropped,
    // which would report saved work as lost. Quitting keeps the short deadline, the process is going.
    private static readonly TimeSpan BaseBuilderTabDraftTimeout = TimeSpan.FromSeconds(12);

    // Leaving the workshop frees it: the recovery draft is written first (the workshop reopens with "Continue"), then the
    // WebView and its renderer, 3D scene and base are disposed instead of lingering suspended in memory. A draft that
    // could not be written keeps the builder alive instead, so the edits are still there to go back to.
    private async Task CloseBaseBuilderWorkspaceAsync()
    {
        ExitBaseBuilderFullscreen();
        var view = _baseBuilderWebView;
        if (view == null) return;
        var draft = await FlushBaseBuilderDraftAsync(BaseBuilderTabDraftTimeout);
        // The player may have come back (or reopened it) while the draft was saving.
        if (!ReferenceEquals(view, _baseBuilderWebView) || BaseBuilderPanel.IsVisible) return;
        if (draft is "failed" or "unanswered")
        {
            ShowInfoSnackbar(BuilderName,
                "Your latest changes could not be saved as a recovery draft, so the workshop stays open in the background. Go back to it and export the base to keep them.",
                Wpf.Ui.Controls.ControlAppearance.Caution);
            return;
        }
        // A live Build Together session needs the page: it carries the host's batches and the guests' proposals. Looking at
        // another tab must not end it for everyone, so the workshop stays in the background until the session is over.
        if (ActiveBuildSession != null && BuilderCarriesSession)
        {
            _baseBuilderParked = true;
            return;
        }
        DisposeBaseBuilderView();
        BaseBuilderLoading.Visibility = Visibility.Collapsed;
    }

    /// <summary>The builder page a co-op session runs in.</summary>
    private static string BuilderWorkspaceOf(Services.Builder.BuildSessionInfo session) => session.IsShip ? ShipBuilderWorkspace : BaseBuilderWorkspace;

    private System.Windows.Controls.TabItem BuilderTabOf(string workspace) => workspace == ShipBuilderWorkspace ? ShipBuilderTab : BaseBuilderTab;

    /// <summary>
    /// The running page is the one the co-op session belongs to (or there is no session). Only then does it get the session's
    /// state and batches, and only then does closing it end or leave the session: a guest who accepts a ship invite while the
    /// Base Builder is open keeps the session while that page goes and the Ship Builder opens.
    /// </summary>
    private bool BuilderCarriesSession => ActiveBuildSession is not { } session || BuilderWorkspaceOf(session) == _builderWorkspace;

    /// <summary>
    /// The player switched between the Base and the Ship Builder. The running page is the other builder's, so its draft is
    /// written and it is freed before the new one opens. A Build Together session running in that page or a draft that could
    /// not be written keeps the running builder instead: the player is sent back to it.
    /// </summary>
    private async Task<bool> CloseBuilderForOtherWorkspaceAsync(string workspace)
    {
        var view = _baseBuilderWebView;
        string running = BuilderName;
        string? refusal = ActiveBuildSession != null && BuilderCarriesSession
            ? $"A Build Together session is running in the {running}. Leave it before you open the {(workspace == ShipBuilderWorkspace ? "Ship Builder" : "Base Builder")}."
            : null;
        _baseBuilderOpening = true;
        try
        {
            if (refusal == null)
            {
                var draft = await FlushBaseBuilderDraftAsync(BaseBuilderTabDraftTimeout);
                if (draft is "failed" or "unanswered")
                    refusal = $"Your latest changes in the {running} could not be saved as a recovery draft, so it stays open. Export your work there first.";
            }
        }
        finally { _baseBuilderOpening = false; }
        if (!ReferenceEquals(view, _baseBuilderWebView)) return false;
        if (refusal != null)
        {
            ShowInfoSnackbar(running, refusal, Wpf.Ui.Controls.ControlAppearance.Caution);
            var back = BuilderTabOf(_builderWorkspace);
            // Not from inside the tab change that brought us here.
            _ = Dispatcher.BeginInvoke(new Action(() => MainTabs.SelectedItem = back));
            return false;
        }
        // The player may have moved on while the draft was saving.
        if (MainTabs.SelectedItem != BuilderTabOf(workspace)) return false;
        ExitBaseBuilderFullscreen();
        DisposeBaseBuilderView();
        return true;
    }

    /// <summary>True while the workshop is kept alive off screen only because a Build Together session is running.</summary>
    private bool _baseBuilderParked;

    /// <summary>The session that kept the workshop alive is over: free it now, as leaving the tab would have.</summary>
    private void ReleaseParkedBaseBuilder()
    {
        if (!_baseBuilderParked || ActiveBuildSession != null) return;
        _baseBuilderParked = false;
        if (!BaseBuilderPanel.IsVisible) _ = CloseBaseBuilderWorkspaceAsync();
    }

    /// <summary>The app is quitting: the draft is written and any co-op session left while the page still runs.</summary>
    private async Task CloseBaseBuilderOnExitAsync()
    {
        if (_baseBuilderWebView == null) return;
        var draft = await FlushBaseBuilderDraftAsync();
        if (draft is "failed" or "unanswered") AppendLog($"[Base Builder] Quitting without a fresh recovery draft ({draft}).");
        await LeaveBuildTogetherOnCloseAsync().WaitAsync(BaseBuilderDraftTimeout);
    }

    /// <summary>
    /// Logoff, restart or shutdown: the window's close path races the process ending, so the draft is written here, on the
    /// UI thread, with the dispatcher kept running so the page's answer can arrive.
    /// </summary>
    private void Application_SessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        if (_baseBuilderWebView == null) return;
        try
        {
            var exit = CloseBaseBuilderOnExitAsync();
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = BaseBuilderDraftTimeout + TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) => frame.Continue = false;
            timer.Start();
            _ = exit.ContinueWith(_ => Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            timer.Stop();
        }
        catch (Exception ex) { AppendLog($"[Base Builder] Session ending without a fresh recovery draft: {ex.Message}"); }
    }

    private void BaseBuilderHost_Unloaded(object sender, RoutedEventArgs e) => DisposeBaseBuilderView();

    // A page that carries the co-op session takes it with it; one of the other builder leaves it to the page that opens next.
    private void DisposeBaseBuilderView()
    {
        _baseBuilderParked = false;
        if (_baseBuilderWebView != null && BuilderCarriesSession) _ = LeaveBuildTogetherOnCloseAsync();
        _baseBuilderWebView?.Dispose();
        _baseBuilderWebView = null;
        BaseBuilderHost.Children.Clear();
    }

    // The Bases / Ships switch in the panel header shows which builder is open and opens the other one. It selects that builder's
    // tab, so switching takes the same way as from Tools: the draft is written first and a running Build Together session is kept.
    private void UpdateBuilderModeUi()
    {
        bool ship = _builderWorkspace == ShipBuilderWorkspace;
        BuilderModeBase.Appearance = ship ? Wpf.Ui.Controls.ControlAppearance.Secondary : Wpf.Ui.Controls.ControlAppearance.Primary;
        BuilderModeShip.Appearance = ship ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;
    }

    private void BuilderMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string workspace } && workspace != _builderWorkspace) MainTabs.SelectedItem = BuilderTabOf(workspace);
    }

    private void BaseBuilderRetry_Click(object sender, RoutedEventArgs e)
    {
        BaseBuilderHost_Unloaded(sender, e);
        _ = OpenBaseBuilderWorkspaceAsync(_builderWorkspace);
    }

    private void BaseBuilder_CloseRequested(object sender, RoutedEventArgs e)
    {
        ExitBaseBuilderFullscreen();
        ReturnToLastWorkspace();
    }
}
