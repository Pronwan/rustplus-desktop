using RustPlusDesk.Features.Tutorials;
using RustPlusDesk.Services;
using RustPlusDesk.Services.Auth;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace RustPlusDesk.Views;

public partial class MainWindow : ITutorialContext, ITutorialNavigationCoordinator
{
    private TutorialRegistry? _tutorialRegistry;
    private TutorialProgressStore? _tutorialProgressStore;
    private TutorialService? _tutorialService;
    private string? _preparedTutorialStepId;

    bool ITutorialContext.IsLoggedIn => SupabaseAuthManager.IsAuthenticated;
    bool ITutorialContext.IsPremium => _vm.IsPremium;
    bool ITutorialContext.HasPairedServer => _vm.Servers.Count > 0;
    bool ITutorialContext.HasSelectedServer => _vm.Selected is not null;
    bool ITutorialContext.IsSoftConnected => _vm.Selected?.IsConnected == true;
    bool ITutorialContext.IsFullConnected => _vm.Selected?.IsFullConnected == true;
    bool ITutorialContext.HasMap => ImgMap.Source is not null || _isMap3DActive;
    bool ITutorialContext.HasDevices => _vm.CurrentDevices?.Count > 0;
    bool ITutorialContext.HasTeam => TeamMembers.Count > 0;
    bool ITutorialContext.IsDiscordConfigured => false;
    bool ITutorialContext.HasAutomationRules => _vm.Selected?.LogicRules.Count > 0;

    private void InitializeTutorials()
    {
        _tutorialRegistry = new TutorialRegistry();
        _tutorialProgressStore = new TutorialProgressStore();
        var webBridge = new WebViewTutorialBridge(() => _map3DWebView);
        var resolver = new TutorialTargetResolver(webBridge);
        _tutorialService = new TutorialService(_tutorialRegistry, _tutorialProgressStore, resolver, this, TutorialOverlay, this, Root);
#if DEBUG
        _ = new TutorialInspector(this, Root, _tutorialRegistry, _tutorialService, AppendLog);
#endif

        TutorialOverlay.NextRequested += (_, _) => _ = _tutorialService.NextAsync();
        TutorialOverlay.BackRequested += (_, _) => _ = _tutorialService.BackAsync();
        TutorialOverlay.SkipRequested += (_, _) => _ = _tutorialService.SkipAsync();
        TutorialOverlay.CancelRequested += (_, _) => _ = _tutorialService.CancelAsync();
        TutorialOverlay.QuickTourRequested += async (_, _) =>
        {
            await DismissWelcomeAsync();
            await _tutorialService.StartAsync("application-basics");
        };
        TutorialOverlay.ChooseTutorialsRequested += async (_, _) =>
        {
            await DismissWelcomeAsync();
            await ShowTutorialCenterAsync();
        };
        TutorialOverlay.WelcomeDismissed += async (_, _) => await DismissWelcomeAsync();
        TutorialsPagePanel.CloseRequested += (_, _) => TutorialsPagePanel.Visibility = Visibility.Collapsed;
        _tutorialService.TutorialCompleted += TutorialStateChanged;
        _tutorialService.TutorialSkipped += TutorialStateChanged;
        _tutorialService.TutorialCancelled += TutorialStateChanged;
        _tutorialService.TutorialStarted += (_, _) => _preparedTutorialStepId = null;
        _tutorialService.TargetResolutionFailed += (_, e) => AppendLog($"[tutorial] target unavailable tutorial={e.TutorialId} step={e.StepId} reason={e.Reason}");

        SizeChanged += (_, _) => { if (_tutorialService.IsRunning) _ = _tutorialService.RefreshAsync(); };
        AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) =>
        {
            if (_tutorialService.IsRunning) _ = _tutorialService.RefreshAsync();
        }));
        StateChanged += (_, _) =>
        {
            if (!_tutorialService.IsRunning) return;
            if (WindowState == WindowState.Minimized) TutorialOverlay.Visibility = Visibility.Collapsed;
            else _ = _tutorialService.RefreshAsync();
        };

        Loaded += async (_, _) =>
        {
            await TutorialsPagePanel.InitializeAsync(_tutorialRegistry, _tutorialProgressStore, _tutorialService);
            await Task.Delay(700);
            await StartTutorialOnboardingIfReadyAsync();
        };
    }

    private async Task StartTutorialOnboardingIfReadyAsync()
    {
        TrackingService.ReadFcmConfig();
        bool hasValidFcm = TrackingService.IsFcmConfigured() &&
            (!TrackingService.FcmExpiresAt.HasValue || TrackingService.FcmExpiresAt.Value >= DateTime.Now);
        if (!hasValidFcm || _tutorialProgressStore is null || _tutorialService?.IsRunning == true ||
            TutorialOverlay.Visibility == Visibility.Visible) return;

        var preferences = await _tutorialProgressStore.GetPreferencesAsync();
        if (preferences.AutoStartBasicTutorial && !preferences.FirstRunPromptDismissed)
        {
            TutorialOverlay.ShowWelcome();
        }
    }

    private async Task OfferNewFeatureTutorialOnceAsync(string tutorialId)
    {
        if (_tutorialRegistry?.Find(tutorialId) is not { IsNewFeature: true } definition ||
            _tutorialProgressStore is null || _tutorialService?.IsRunning != false ||
            TutorialOverlay.Visibility == Visibility.Visible) return;

        var progress = await _tutorialProgressStore.GetAsync(definition);
        var preferences = await _tutorialProgressStore.GetPreferencesAsync();
        if (!preferences.AutoStartNewFeatureTutorials || progress.Status != TutorialStatus.NotStarted ||
            !preferences.OfferedTutorialIds.Add(tutorialId)) return;
        await _tutorialProgressStore.SavePreferencesAsync(preferences);

        var prompt = new Wpf.Ui.Controls.MessageBox
        {
            Title = Properties.Resources.GetString(definition.TitleKey),
            Content = Properties.Resources.GetString(definition.DescriptionKey),
            PrimaryButtonText = Properties.Resources.GetString("Tutorials.Common.Start"),
            CloseButtonText = Properties.Resources.GetString("Tutorials.Welcome.NotNow"),
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        if (await prompt.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary)
            await _tutorialService.StartAsync(tutorialId);
    }

    private async void BtnTutorials_Click(object sender, RoutedEventArgs e) => await ShowTutorialCenterAsync();

    private async Task ShowTutorialCenterAsync()
    {
        if (_tutorialRegistry is null || _tutorialProgressStore is null || _tutorialService is null) return;
        TutorialOverlay.Hide();
        await TutorialsPagePanel.InitializeAsync(_tutorialRegistry, _tutorialProgressStore, _tutorialService);
        TutorialsPagePanel.Visibility = Visibility.Visible;
        TutorialsPagePanel.Focus();
    }

    private async Task DismissWelcomeAsync()
    {
        TutorialOverlay.Hide();
        if (_tutorialProgressStore is null) return;
        var preferences = await _tutorialProgressStore.GetPreferencesAsync();
        preferences.FirstRunPromptDismissed = true;
        await _tutorialProgressStore.SavePreferencesAsync(preferences);
    }

    private async void TutorialStateChanged(object? sender, TutorialEventArgs e)
    {
        AppendLog($"[tutorial] state changed tutorial={e.TutorialId} step={e.StepId}");
        await TutorialsPagePanel.RefreshAsync();
    }

    public async Task PrepareAsync(TutorialStep step, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(_preparedTutorialStepId, step.Id, StringComparison.Ordinal))
        {
            _preparedTutorialStepId = step.Id;
            TutorialsPagePanel.Visibility = step.PageKey == "tutorials" ? Visibility.Visible : Visibility.Collapsed;

            if (step.PageKey != "settings") AppSettingsPanel.Visibility = Visibility.Collapsed;
            if (step.PageKey != "logic") LogicEnginePanel.Visibility = Visibility.Collapsed;
            if (step.PageKey != "device-automation") DeviceAutomationPanel.Visibility = Visibility.Collapsed;

            switch (step.PageKey)
            {
                case "devices": MainTabs.SelectedItem = DevicesTabItem; SetSidebarExpanded(true); break;
                case "team": MainTabs.SelectedItem = TabTeam; SetSidebarExpanded(true); break;
                case "cameras": MainTabs.SelectedItem = CamerasTabItem; SetSidebarExpanded(true); break;
                case "notifications": MainTabs.SelectedItem = NotificationsTab; SetSidebarExpanded(true); break;
                case "raid": MainTabs.SelectedItem = RaidCalculatorTab; SetSidebarExpanded(true); break;
                case "recycler": MainTabs.SelectedItem = RecyclerCalculatorTab; SetSidebarExpanded(true); break;
                case "logic":
                    MainTabs.SelectedItem = DevicesTabItem;
                    SetSidebarExpanded(true);
                    LogicEnginePanel.Visibility = Visibility.Visible;
                    break;
                case "device-automation":
                    MainTabs.SelectedItem = DevicesTabItem;
                    SetSidebarExpanded(true);
                    DeviceAutomationPanel.Visibility = Visibility.Visible;
                    break;
                case "map" when step.TargetId?.StartsWith("Chat.", StringComparison.Ordinal) == true:
                    if (ChatContentBorder.Visibility != Visibility.Visible) await OpenChatOverlayAsync();
                    break;
                case "shops":
                    if (step.Id != "shops.open" && ShopSearchContent.Visibility != Visibility.Visible) ToggleShopSearch();
                    break;
                case "settings":
                    SetSidebarExpanded(true);
                    AppSettingsPanel.LoadSettings();
                    AppSettingsPanel.Visibility = Visibility.Visible;
                    AppSettingsPanel.OpenCategory(step.TargetId switch
                    {
                        "Settings.Cloud" => "connected",
                        "Settings.ChatCommands" => "chat-commands",
                        "Settings.Map" => "map",
                        "Settings.Maintenance" => "system",
                        _ => "general"
                    });
                    break;
            }

            if (step.TargetId is "Servers.ConnectionStatus" or "Servers.List" or "Servers.ConnectionActions")
                PanelServerArea.Visibility = Visibility.Visible;

            if (step.Id.StartsWith("heatmaps.", StringComparison.Ordinal))
                ServerHudToggle.IsChecked = true;

            if (step.Id == "heatmaps.selector" && BtnToggleHeatmap.IsVisible) HeatmapPopup.IsOpen = true;

            if (step.WebViewTargetId is not null && !_isMap3DActive)
            {
                TutorialOverlay.Visibility = Visibility.Collapsed;
                await OpenMap3DAsync();
            }
        }
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded, cancellationToken);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
    }

    ITutorialStateSnapshot ITutorialNavigationCoordinator.CaptureState() => new WindowTutorialSnapshot(
        this, MainTabs.SelectedItem, AppSettingsPanel.Visibility, LogicEnginePanel.Visibility,
        DeviceAutomationPanel.Visibility, TutorialsPagePanel.Visibility, ShopSearchContent.Visibility,
        ChatContentBorder.Visibility, PanelServerArea.Visibility, HeatmapPopup.IsOpen, _isSidebarExpanded,
        ServerHudToggle.IsChecked, _isMap3DActive);

    private sealed class WindowTutorialSnapshot(
        MainWindow window, object? selectedTab, Visibility settings, Visibility logic,
        Visibility automation, Visibility tutorials, Visibility shops, Visibility chat,
        Visibility serverArea, bool heatmapOpen, bool sidebarExpanded, bool? serverHudExpanded,
        bool map3DWasActive) : ITutorialStateSnapshot
    {
        public Task RestoreAsync(CancellationToken cancellationToken = default)
        {
            if (!window.IsLoaded) return Task.CompletedTask;
            window.MainTabs.SelectedItem = selectedTab;
            window.AppSettingsPanel.Visibility = settings;
            window.LogicEnginePanel.Visibility = logic;
            window.DeviceAutomationPanel.Visibility = automation;
            window.TutorialsPagePanel.Visibility = tutorials;
            window.ShopSearchContent.Visibility = shops;
            window.ChatContentBorder.Visibility = chat;
            window.PanelServerArea.Visibility = serverArea;
            window.HeatmapPopup.IsOpen = heatmapOpen;
            window.ServerHudToggle.IsChecked = serverHudExpanded;
            window.SetSidebarExpanded(sidebarExpanded);
            if (!map3DWasActive && window._isMap3DActive) window.CloseMap3DView();
            return Task.CompletedTask;
        }
    }
}
