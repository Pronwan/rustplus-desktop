using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using RustPlusDesk.ViewModels;
using RustPlusDesk.Views;
using System.Windows.Markup;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using RustPlusDesk.Converters;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using StorageSnap = RustPlusDesk.Models.StorageSnapshot;
using StorageItemVM = RustPlusDesk.Models.StorageItemVM;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Resources; // für Application.GetResourceStream
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Linq;
using static RustPlusDesk.Services.RustPlusClientReal;
using IOPath = System.IO.Path;
using RustPlusDesk.Helpers;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using WpfUi = Wpf.Ui.Controls;


namespace RustPlusDesk.Views;


public partial class MainWindow : WpfUi.FluentWindow
{

    private readonly MainViewModel _vm = new();
    private bool _chatOpenedForCommandsOnly = false;
    private readonly UpdateService _updateService = new();

    private DateTime _lastPairingPingAt = DateTime.MinValue;
    private readonly IRustPlusClient _rust;  // Interface statt fester Klasse
    private WebView2? _webView;
    private IPairingListener _pairing;
    private readonly Dictionary<uint, DateTime> _entityPairSeen = new();
    private string? _lastPairSig;
    private bool _listenerStarting; // Schutz gegen Doppelklicks
    private readonly System.Windows.Threading.DispatcherTimer _statusTimer =
    new() { Interval = TimeSpan.FromSeconds(30) };

    private readonly System.Windows.Threading.DispatcherTimer _upkeepTimer =
    new() { Interval = TimeSpan.FromSeconds(60) };

    private Viewbox? _mapView;
    private Grid? _scene;
    private bool _isPanning;
    private const double ZoomStep = 1.1;   // ~10% pro Wheel-Klick
    private readonly MatrixTransform MapTransform = new MatrixTransform();
    // --- Dark theme brushes for search window ---
    private static readonly Brush SearchWinBg = new SolidColorBrush(Color.FromRgb(24, 26, 30));
    private static readonly Brush SearchCardBg = new SolidColorBrush(Color.FromRgb(36, 40, 46));
    private static readonly Brush SearchCardBrd = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
    private static readonly Brush SearchText = Brushes.White;
    private static readonly Brush SearchSubtle = new SolidColorBrush(Color.FromArgb(180, 220, 220, 220));
    // CROSSHAIR
    private CrosshairWindow? _overlay;
    private CrosshairStyle _currentStyle = CrosshairStyle.GreenDot;
    private bool _alertsNeedRebaseline = false;
    private bool _visible;
    // CAMERA TAB

    // Chinook Chekcer
    private readonly MonumentWatcher _monumentWatcher = new MonumentWatcher();

    private uint? _trackingEntityId; // NEU: ID des Objekts, dem die Kamera folgt
    private IReadOnlyList<RustPlusClientReal.DynMarker>? _lastMarkers; // Cache für Interaktionen

    public void StopTracking()
    {
        _trackingEntityId = null;
        _vm.FollowingSteamId = null;
        _vm.FollowingPlayerName = "";
        _vm.FollowingPlayerAvatar = null;
    }

    private void BtnFollowPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsFollowing)
        {
            StopTracking();
            return;
        }

        // Build dynamic menu
        MenuFollowPlayer.Items.Clear();

        var miMe = new MenuItem { Header = Properties.Resources.FollowMe, Icon = new TextBlock { FontFamily = new FontFamily("Segoe MDL2 Assets"), Text = "\uE77B" } };
        miMe.Click += (s, ev) => StartFollowing(_mySteamId, "Me");
        MenuFollowPlayer.Items.Add(miMe);

        if (TeamMembers.Count > 0)
        {
            MenuFollowPlayer.Items.Add(new Separator());
            foreach (var member in TeamMembers)
            {
                if (member.SteamId == _mySteamId) continue;
                var mi = new MenuItem { Header = string.Format(Properties.Resources.FollowPlayer, member.DisplayName), Tag = member.SteamId };
                mi.Click += (s, ev) => StartFollowing(member.SteamId, member.DisplayName);
                MenuFollowPlayer.Items.Add(mi);
            }
        }

        MenuFollowPlayer.PlacementTarget = BtnFollowPlayer;
        MenuFollowPlayer.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        MenuFollowPlayer.IsOpen = true;
    }

    // Camera thumbs: Throttling & "in-flight"-Wächter
    // Dumper Button
    private async void BtnDynCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_rust is not RustPlusClientReal real)
        {
            AppendLog("dyn2: kein Client.");
            return;
        }

        try
        {
            var list = await real.GetDynamicMapMarkersAsync2();
            AppendLog($"dyn2: total={list.Count}");

            // kleine Verteilung nach RawType
            var groups = list.GroupBy(m => m.RawType).OrderBy(g => g.Key)
                             .Select(g => $"{g.Key}×{g.Count()}");
            AppendLog("dyn2 types: " + string.Join(", ", groups));

            // zeig die ersten 6 Marker „roh“
            foreach (var m in list.Take(6))
                AppendLog("dyn2 sample: " + m.DebugLine);
            // (optional) schnelle Heuristik für crate-verdächtige
            var suspects = list.Where(m =>
                (m.RawType == 7 || m.RawType == 0) &&
                ((m.Label ?? "").IndexOf("crate", StringComparison.OrdinalIgnoreCase) >= 0
               || (m.Label ?? "").IndexOf("hack", StringComparison.OrdinalIgnoreCase) >= 0
               || (m.Label ?? "").IndexOf("lock", StringComparison.OrdinalIgnoreCase) >= 0))
               .ToList();

            if (suspects.Count > 0)
            {
                AppendLog($"dyn2 crate-like: {suspects.Count}");
                foreach (var s in suspects.Take(3))
                    AppendLog($"dyn2 crate-like: {s.DebugLine}");
            }
        }
        catch (Exception ex)
        {
            AppendLog("dyn2 error: " + ex.Message);
        }
    }

    private void RefreshUpkeepUI()
    {
        if (_vm.Servers == null) return;
        foreach (var server in _vm.Servers)
        {
            if (server.Devices == null) continue;
            foreach (var dev in server.Devices)
            {
                if (dev.HasStorage)
                {
                    dev.NotifyUpkeepChanged();
                }
            }
        }
    }
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool bb && bb;
            bool invert = (parameter as string)?.Equals("invert", StringComparison.OrdinalIgnoreCase) == true;
            if (invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    private BitmapSource? _mapBaseBmp; // Original-Map ohne Marker
    private readonly List<(double uPx, double vPx, string? label)> _staticMarkers = new();
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (TrackingService.CloseToTrayEnabled)
        {
            e.Cancel = true;
            this.Hide();
            return;
        }

        if (ColSidebar != null)
        {
            TrackingService.SidebarWidth = ColSidebar.ActualWidth;
        }

        base.OnClosing(e);
    }

    // --- Overlay State ---
    private readonly List<(SmartDevice? Device, AlarmNotification Notification)> _overlayAlarms = new();
    private int _overlayAlarmIndex = -1;
    private DispatcherTimer? _overlayHideTimer;

    private Action? _pendingUploadAction;

    public void ShowUploadConsent(Action onAccept)
    {
        if (TrackingService.UploadConsentGiven)
        {
            onAccept?.Invoke();
            return;
        }

        _pendingUploadAction = onAccept;
        UploadConsentOverlay.Visibility = Visibility.Visible;
    }

    private void BtnAcceptUploadConsent_Click(object sender, RoutedEventArgs e)
    {
        TrackingService.UploadConsentGiven = true;
        UploadConsentOverlay.Visibility = Visibility.Collapsed;
        _pendingUploadAction?.Invoke();
        _pendingUploadAction = null;
    }

    private void BtnDeclineUploadConsent_Click(object sender, RoutedEventArgs e)
    {
        UploadConsentOverlay.Visibility = Visibility.Collapsed;
        _pendingUploadAction = null;
    }

    public MainWindow()
    {
        // Nur freiwillig zum Diagnostizieren:
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        _vm.IsInitializing = true;
        InitializeComponent();
        UpdateLanguageFlag();
        InitializeAppSettings();
        
        // ── WinUI 3: Apply OS-level Mica backdrop via DWM ────────────────────
        WindowBackdropHelper.Apply(this, WindowBackdropHelper.BackdropType.Mica);
        // ── Wpf.Ui: Apply Fluent dark theme to all controls ───────────────────
        ApplicationThemeManager.Apply(ApplicationTheme.Dark, updateAccent: true);
        
        if (AppTitleBar != null) AppTitleBar.Title = $"RUST+ DESKTOP v{_updateService.VersionRaw}";
        this.Title = $"RUST+ DESKTOP v{_updateService.VersionRaw}";
        if (FindName("TxtVersion") is TextBlock txt)
            txt.Text = $"v{_updateService.VersionRaw}";
        InitCameraUi();
        InitSmoothFollowLoop();
        ApplySettings();
        _selectedMonitor = WinMonitors.All().Count > 0 ? WinMonitors.All()[0] : null;
        AppendLog($"[items-new] baseDir={baseDir}");
        EnsureNewItemDbLoaded();
        AppendLog($"[items-new] source={sNewDbSource} items={sItemsById.Count} byShort={sItemsByShort.Count}");

        // NEU: Hintergrund-Update der Item-Liste von rusthelp.com
        _ = Task.Run(async () =>
        {
            if (await TryUpdateItemDbAsync())
            {
                Dispatcher.Invoke(() => {
                    EnsureNewItemDbLoaded(force: true);
                    AppendLog($"[items-update] Updated from web! New count: {sItemsById.Count}");
                });
            }
        });
        // GridLayer.RenderTransform = MapTransform;
        // Overlay.RenderTransform   = MapTransform;
        // bei Host-Resize: nur Markerpositionen neu berechnen


        WebViewHost.SizeChanged += (_, __) =>
        {
            // FitMapToHost();
            // <<< NEU: Basis an neue Hostgröße anpassen

            // UpdateMarkerPositions();
        };
        WebViewHost.MouseWheel += WebViewHost_MouseWheel;
        WebViewHost.MouseDown += WebViewHost_MouseDown;
        WebViewHost.MouseMove += WebViewHost_MouseMove;
        WebViewHost.MouseUp += WebViewHost_MouseUp;

        WebViewHost.KeyDown += WebViewHost_KeyDown;
        WebViewHost.Focusable = true;
        DataContext = _vm;
        _vm.Load();
        // NEU: einmalig auf die aktuell ausgewählte Server-Instanz „umstecken“
        SwitchCameraSourceTo(_vm.Selected);

        // NEU: bei jedem späteren Serverwechsel Kameraliste umhängen
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Selected))
                SwitchCameraSourceTo(_vm.Selected);
            if (e.PropertyName == nameof(MainViewModel.IsDownloadingUpdate) && !_vm.IsDownloadingUpdate)
                UpdateDownloadPopup.IsOpen = false;
        };

        // MapTransform.Changed += (_, __) => UpdateMarkerPositions();
        HydrateSteamUiFromStorage();   // <= HIER

        // Set version badge dynamically from assembly
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (ver != null)
            TxtAppVersion.Text = $"v{ver.Major}.{ver.Minor}.{ver.Build}";
        // _statusTimer.Tick += async (_, __) => await UpdateServerStatusAsync();
        _upkeepTimer.Tick += (_, __) => RefreshUpkeepUI();
        _upkeepTimer.Start();

        ListServers.ItemsSource = _vm.Servers;
        _vm.Servers.CollectionChanged += (_, __) => Dispatcher.Invoke(() => UpdatePairingGuideSnackbar());
        
        UpdateMasterToggleState();
        SyncAlertMenuItems();

        // Auto-start FCM listener silently in background on app open
        Loaded += (_, __) => _ = Task.Delay(1500).ContinueWith(_ => Dispatcher.Invoke(() => 
        {
            // Seed in-memory state from the config file (issue_date, expiry_date, steam_id)
            TrackingService.ReadFcmConfig();
            _vm.NotifyFcmChanged();

            StartPairingSilent(true);
            
            // Auto-connect if enabled and not already connected
            if (TrackingService.AutoConnectEnabled && _vm.Selected != null && !_vm.Selected.IsConnected)
            {
                _ = Task.Run(async () => {
                    await Task.Delay(1000); // Give Pairing Listener a head start
                    await Dispatcher.InvokeAsync(async () => await PerformConnectAsync(true));
                });
            }

            // Auto-check for updates
            _ = Task.Run(async () => await AutoCheckUpdatesAsync());

            UpdatePairingGuideSnackbar();
        }));

        // One-time migration notice for v5.2.0
        const string AppVersion = "5.3.0-beta";

        bool IsVersionLessThanOrEqual(string versionStr, string targetStr)
        {
            string cleanVer = versionStr.Split('-')[0];
            string cleanTarget = targetStr.Split('-')[0];
            if (System.Version.TryParse(cleanVer, out var v1) && System.Version.TryParse(cleanTarget, out var v2))
            {
                return v1 <= v2;
            }
            return false;
        }
        
        if (string.IsNullOrEmpty(TrackingService.LastSeenVersion))
        {
            // Erst-Installation: Version setzen, aber kein Popup zeigen
            TrackingService.LastSeenVersion = AppVersion;

            if (TrackingService.SelectedLanguage == "en")
            {
                TrackingService.SelectedLanguage = "";
                if (Application.Current is App app)
                {
                    app.SetLanguage();
                }
            }
        }
        else if (TrackingService.LastSeenVersion != AppVersion)
        {
            if (TrackingService.SelectedLanguage == "en")
            {
                TrackingService.SelectedLanguage = "";
                if (Application.Current is App app)
                {
                    app.SetLanguage();
                }
            }

            if (IsVersionLessThanOrEqual(TrackingService.LastSeenVersion, "5.2.0"))
            {
                // Upgrade von 5.2.0 oder geringer: Popup zeigen
                Dispatcher.InvokeAsync(() =>
                {
                    var dlg = new Views.MigrationNoticeWindow { Owner = this };
                    dlg.ShowDialog();
                    
                    // Nur speichern, wenn der User "Don't show again" angehakt hat (Standard ist true)
                    if (dlg.DontShowAgain)
                    {
                        TrackingService.LastSeenVersion = AppVersion;
                    }
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                // Kommt von einer neueren/gleichen Version: Einfach Version updaten ohne Popup
                TrackingService.LastSeenVersion = AppVersion;
            }
        }

        // Initial tracking status update and hook global events
        TrackingService.OnOnlinePlayersUpdated -= OnOnlinePlayersUpdated;
        TrackingService.OnOnlinePlayersUpdated += OnOnlinePlayersUpdated;
        TrackingService.OnServerInfoUpdated -= OnServerInfoUpdated;
        TrackingService.OnServerInfoUpdated += OnServerInfoUpdated;
        TrackingService.OnTrackingNotification -= OnTrackingNotification;
        TrackingService.OnTrackingNotification += OnTrackingNotification;
        OnOnlinePlayersUpdated();
        _vm.IsInitializing = false;
        
        // Einmal erzeugen (falls du den Stub behalten willst: try/fallback – aber nur EINMAL zuweisen)

        _pairing = new PairingListenerRealProcess(AppendLog);

        _pairing.Paired += Pairing_Paired;

        // EINMALIG auf AlarmReceived hören:
        if (_pairing is PairingListenerRealProcess pr)
        {
            pr.AlarmReceived += (_, a) => Dispatcher.Invoke(() => ShowAlarmPopup(a));
        }

        // Status → UI
        _pairing.Listening += (_, __) => Dispatcher.BeginInvoke(new Action(() =>
        {
            _vm.IsPairingRunning = true;
            _vm.IsPairingBusy = true; // Update UI button state
            TxtPairingState.Text = Properties.Resources.PairingListening;
            UpdatePairingGuideSnackbar();
        }));
        _pairing.Stopped += (_, __) => Dispatcher.BeginInvoke(new Action(() =>
        {
            _vm.IsPairingRunning = false;
            _vm.IsPairingBusy = false; // Update UI button state
            TxtPairingState.Text = Properties.Resources.PairingStopped;
        }));
        _pairing.RegistrationCompleted += (_, __) => Dispatcher.BeginInvoke(new Action(() => _vm.NotifyFcmChanged()));
        _pairing.Failed += (_, msg) => Dispatcher.BeginInvoke(new Action(() =>
        {
            _vm.IsPairingRunning = false;
            _vm.IsPairingBusy = false; // Error occurred, not busy anymore
            TxtPairingState.Text = Properties.Resources.PairingFailed; // show failure text
            AppendLog("[listener] " + msg);
            // Auto-retry after a short delay
            _ = Task.Delay(5000).ContinueWith(_ => Dispatcher.BeginInvoke(new Action(() => StartPairingSilent(true))));
        }));


        _rust = new RustPlusClientReal(AppendLog);

        if (_rust is RustPlusClientReal real)
        {
            real.EnsureEventsHooked();
            real.DeviceStateEvent += async (id, isOn, kindFromApi) =>
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    var dev = FindDeviceById(_vm.Selected?.Devices, id);
                    if (dev == null) return;

                    // Kind nur setzen, wenn wir es NOCH NICHT kennen – nie ein SmartAlarm "wegschreiben"
                    if (string.IsNullOrWhiteSpace(dev.Kind) && !string.IsNullOrWhiteSpace(kindFromApi))
                        dev.Kind = kindFromApi;

                    // ⬇️ SmartAlarm: NICHT proben, sondern den Eventwert verwenden (true = gerade ausgelöst)
                    if ((dev.Kind ?? kindFromApi)?.Equals("SmartAlarm", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        _suppressToggleHandler = true;
                        dev.IsOn = isOn;                  // zeigt in der Liste AKTIV nur während der Auslösung
                        _suppressToggleHandler = false;

                        // optional: nach kurzer Zeit automatisch auf INAKTIV zurücknehmen,
                        // falls kein weiterer Alarm-Event kommt
                        // Trigger Alarm UI/Sound auch via WebSocket
                        if (isOn)
                        {
                            string srv = _vm.Selected?.Name ?? "Server";
                            // Bereinigen für UI und Cache-Matching
                            srv = Regex.Replace(srv, @"\x1B\[[0-9;]*[A-Za-z]", "");
                            srv = Regex.Replace(srv, @"\[/?[a-zA-Z]+\]", "").Trim();
                            if (string.IsNullOrEmpty(srv)) srv = "Server";

                            string title = dev.Name ?? "Smart Alarm";
                            string msg = dev.LastAlarmMessage ?? "Alarm activated!";
                            if (_alarmMetadataCache.TryGetValue(dev.EntityId, out var cached))
                            {
                                if (title == "Smart Alarm" && !string.IsNullOrEmpty(cached.Title)) title = cached.Title;
                                if (msg == "Alarm activated!" && !string.IsNullOrEmpty(cached.Message)) msg = cached.Message;
                            }

                            var alarm = new AlarmNotification(DateTime.Now, srv, title, dev.EntityId, msg);
                            ShowAlarmPopup(alarm, "WS");
                        }

                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(7000);   // 7s Puls-Fenster
                            await Dispatcher.InvokeAsync(() =>
                            {
                                // nur zurücksetzen, wenn seither kein neuer Alarm kam
                                if (dev.IsOn == true)
                                {
                                    _suppressToggleHandler = true;
                                    dev.IsOn = false;  // INAKTIV
                                    _suppressToggleHandler = false;
                                }
                            });
                        });
                        return;
                    }

                    // Standard-Geräte (Switch etc.): Eventwert reicht aus
                    _suppressToggleHandler = true;
                    dev.IsOn = isOn;
                    _suppressToggleHandler = false;
                });
            };
        }
        // AppendLog($"DEBUG: Selected={_vm.Selected?.Name ?? "(null)"}  Devices={_vm.Selected?.Devices?.Count.ToString() ?? "(null)"}");




        TxtSteamId.Text = string.IsNullOrEmpty(_vm.SteamId64) ? Properties.Resources.NotLoggedIn : _vm.SteamId64;

        this.Closing += MainWindow_Closing;
        _ = EnsureWebView2Async();
        try { ClearAllToggleBusy(); } catch { }
        try { ResetAllBusyStates(); } catch { }
        this.Closed += MainWindow_Closed;

        _toolButtons = new Dictionary<OverlayToolMode, Button>
    {
        { OverlayToolMode.Draw,  ToolDrawButton },
        { OverlayToolMode.Text,  ToolTextButton },
        { OverlayToolMode.Icon,  ToolIconButton },
        { OverlayToolMode.Erase, ToolEraseButton }
    };

        _monumentWatcher.OnOilRigTriggered += (s, data) =>
        {
            if (!TrackingService.AnnounceSpawnsMaster || !TrackingService.AnnounceOilRig) return;
            string timeStr = data.Duration >= 800 ? "~15m" : "~12:30m";
            string rigName = data.Name == "Small Oil Rig" ? Properties.Resources.SmallOilRig :
                             data.Name == "Large Oil Rig" ? Properties.Resources.LargeOilRig :
                             data.Name;
            Dispatcher.InvokeAsync(async () =>
                await SendTeamChatSafeAsync(string.Format(Properties.Resources.AlertOilRigTriggered, rigName, timeStr)));
        };

        // NEU: Update Events (10m / 5m Warnungen)
        _monumentWatcher.OnOilRigChatUpdate += (s, message) =>
        {
            if (!TrackingService.AnnounceSpawnsMaster || !TrackingService.AnnounceOilRig) return;
            Dispatcher.InvokeAsync(async () => await SendTeamChatSafeAsync(message));
        };
        
        _monumentWatcher.OnDebug += (s, msg) => Dispatcher.BeginInvoke(new Action(() => AppendLog(msg)));

        App.CultureChanged += () =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RebuildChatMessages();
                RefreshEventDock();
                SyncAlertMenuItems();
                UpdateLanguageFlag();
            }));
        };
    }

    private void OnTrackingNotification(string msg, string serverName)
    {
        if (_vm.Selected != null && _vm.Selected.Name == serverName)
        {
            Dispatcher.InvokeAsync(async () => await SendTeamChatSafeAsync(msg));
        }
    }

    // CROSSHAIR \\
    private MonitorInfo? _selectedMonitor;

    private void BtnCrosshair_Click(object sender, RoutedEventArgs e)
    {
        if (_visible)
            HideOverlay();
        else
            ShowOverlay();
    }

    private void ShowOverlay()
    {
        if (_overlay == null)
            _overlay = new CrosshairWindow
            {
                Owner = this,             // <<< wichtig
                ShowInTaskbar = false
            };

        _overlay.SetStyle(_currentStyle);
        _overlay.Topmost = true;
        if (_selectedMonitor != null)
            PositionOverlayCentered(_overlay, _selectedMonitor);

        _overlay.Show();
        _visible = true;

        BtnCrosshair.Background = new SolidColorBrush(Color.FromArgb(50, 0, 150, 255));
        BtnCrosshair.BorderBrush = new SolidColorBrush(Colors.DodgerBlue);
        BtnCrosshair.BorderThickness = new Thickness(1);
    }

    private void HideOverlay()
    {
        if (_overlay != null)
        {
            _overlay.Close();    // statt Hide()
            _overlay = null;
        }
        _visible = false;
        
        BtnCrosshair.ClearValue(Control.BackgroundProperty);
        BtnCrosshair.ClearValue(Control.BorderBrushProperty);
        BtnCrosshair.ClearValue(Control.BorderThicknessProperty);
    }

    private void PositionOverlayCentered(Window w, MonitorInfo mon)
    {
        var ps = PresentationSource.FromVisual(this);
        double dpiX = 1.0, dpiY = 1.0;
        if (ps?.CompositionTarget != null)
        {
            var m = ps.CompositionTarget.TransformFromDevice;
            dpiX = m.M11; dpiY = m.M22;
        }

        double screenWidthDip = mon.Width * dpiX;
        double screenHeightDip = mon.Height * dpiY;
        double screenLeftDip = mon.Left * dpiX;
        double screenTopDip = mon.Top * dpiY;

        // w.Width / w.Height kommen jetzt aus dem CrosshairWindow je nach Stil
        w.Left = screenLeftDip + (screenWidthDip - w.Width) / 2.0;
        w.Top = screenTopDip + (screenHeightDip - w.Height) / 2.0;
    }

    // Kontextmenü: Rechtsklick abfangen, damit das Menü sicher aufgeht
    private void BtnCrosshair_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var btn = (FrameworkElement)sender;
        if (btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    // Menü beim Öffnen mit Monitoren füllen und Häkchen setzen
    private void CrosshairContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        BuildMonitorMenu();
        LoadCustomCrosshairs();
        UpdateStyleChecks();
    }

    private void LoadCustomCrosshairs()
    {
        if (FindName("MenuCrosshairStyle") is MenuItem menuCrosshairStyle && FindName("CrosshairStyleSeparator") is Separator sep)
        {
            int sepIndex = menuCrosshairStyle.Items.IndexOf(sep);
            if (sepIndex == -1) return;

            // Remove all items after "Draw Crosshair..."
            int drawItemIndex = sepIndex + 1;
            while (menuCrosshairStyle.Items.Count > drawItemIndex + 1)
            {
                menuCrosshairStyle.Items.RemoveAt(drawItemIndex + 1);
            }

            var customCrosshairs = CustomCrosshairManager.LoadCrosshairs();
            foreach (var cc in customCrosshairs)
            {
                var mi = new MenuItem();
                mi.Tag = "Custom_" + cc.Id;

                var sp = new StackPanel { Orientation = Orientation.Horizontal };

                var tb = new TextBlock { Text = cc.Name, Width = 100, VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(tb);

                var btnRename = new Button { Content = "Abc", ToolTip = "Rename", Width = 28, Height = 24, Margin = new Thickness(0, 0, 5, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.LightGray, Tag = cc };
                btnRename.Click += CustomCrosshairRename_Click;

                var btnEdit = new Button { Content = "✏️", ToolTip = "Edit", Width = 24, Height = 24, Margin = new Thickness(0, 0, 5, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Tag = cc };
                btnEdit.Click += CustomCrosshairEdit_Click;
                
                var btnDelete = new Button { Content = "🗑️", ToolTip = "Delete", Width = 24, Height = 24, Margin = new Thickness(0, 0, 5, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Tag = cc };
                btnDelete.Click += CustomCrosshairDelete_Click;

                sp.Children.Add(btnRename);
                sp.Children.Add(btnEdit);
                sp.Children.Add(btnDelete);

                if (!string.IsNullOrEmpty(cc.Base64Image))
                {
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(cc.Base64Image);
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = new System.IO.MemoryStream(bytes);
                        bitmap.EndInit();
                        bitmap.Freeze();
                        
                        mi.Icon = new Image
                        {
                            Source = bitmap,
                            Width = 16,
                            Height = 16,
                            Stretch = Stretch.Uniform
                        };
                    }
                    catch { }
                }

                mi.Header = sp;
                mi.Click += CustomStyle_Click;

                menuCrosshairStyle.Items.Add(mi);
            }
        }
    }

    private void DrawCustomCrosshair_Click(object sender, RoutedEventArgs e)
    {
        var editor = new CrosshairEditorWindow { Owner = this };
        if (editor.ShowDialog() == true && editor.SavedCrosshair != null)
        {
            _currentStyle = CrosshairStyle.Custom;
            if (_visible && _overlay != null)
            {
                _overlay.CustomBase64 = editor.SavedCrosshair.Base64Image;
                _overlay.SetStyle(_currentStyle);
                if (_selectedMonitor != null)
                    PositionOverlayCentered(_overlay, _selectedMonitor);
            }
        }
    }

    private void CustomStyle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Header is StackPanel sp)
        {
            var btn = sp.Children.OfType<Button>().FirstOrDefault();
            if (btn != null && btn.Tag is CustomCrosshair cc)
            {
                _currentStyle = CrosshairStyle.Custom;
                UpdateStyleChecks();

                if (!_visible)
                {
                    ShowOverlay();
                }
                if (_visible && _overlay != null)
                {
                    _overlay.CustomBase64 = cc.Base64Image;
                    _overlay.SetStyle(_currentStyle);
                    if (_selectedMonitor != null)
                        PositionOverlayCentered(_overlay, _selectedMonitor);
                }
            }
        }
    }

    private void CustomCrosshairRename_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button btn && btn.Tag is CustomCrosshair cc)
        {
            var dlg = new RenameDialog(cc.Name) { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.InputText))
            {
                var list = CustomCrosshairManager.LoadCrosshairs();
                var existing = list.FirstOrDefault(c => c.Id == cc.Id);
                if (existing != null)
                {
                    existing.Name = dlg.InputText.Trim();
                    CustomCrosshairManager.SaveCrosshairs(list);
                }
            }
            BtnCrosshair.ContextMenu.IsOpen = false;
        }
    }

    private void CustomCrosshairEdit_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button btn && btn.Tag is CustomCrosshair cc)
        {
            var editor = new CrosshairEditorWindow(cc) { Owner = this };
            if (editor.ShowDialog() == true && editor.SavedCrosshair != null)
            {
                // The editor saves it correctly now, replacing the old entry.
                // We just update the currently selected crosshair if we were editing the active one
                if (_currentStyle == CrosshairStyle.Custom && _overlay?.CustomBase64 == cc.Base64Image)
                {
                    _overlay.CustomBase64 = editor.SavedCrosshair.Base64Image;
                    if (_visible) _overlay.SetStyle(_currentStyle);
                }
            }
            BtnCrosshair.ContextMenu.IsOpen = false;
        }
    }

    private void CustomCrosshairDelete_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button btn && btn.Tag is CustomCrosshair cc)
        {
            var list = CustomCrosshairManager.LoadCrosshairs();
            list.RemoveAll(c => c.Id == cc.Id);
            CustomCrosshairManager.SaveCrosshairs(list);

            if (_currentStyle == CrosshairStyle.Custom && _overlay?.CustomBase64 == cc.Base64Image)
            {
                _currentStyle = CrosshairStyle.GreenDot;
                if (_visible && _overlay != null)
                {
                    _overlay.CustomBase64 = null;
                    _overlay.SetStyle(_currentStyle);
                }
            }

            BtnCrosshair.ContextMenu.IsOpen = false;
        }
    }

    // Menüaufbau
    private void BuildMonitorMenu()
    {
        MonitorRoot.Items.Clear();
        var screens = WinMonitors.All();

        for (int i = 0; i < screens.Count; i++)
        {
            var s = screens[i];
            var item = new MenuItem
            {
                Header = $"{i + 1}: {(s.Primary ? "Hauptmonitor" : "Monitor")} {s.Width}×{s.Height} @ {s.Left},{s.Top}",
                IsCheckable = true,
                IsChecked = _selectedMonitor != null &&
                            s.Left == _selectedMonitor.Left &&
                            s.Top == _selectedMonitor.Top &&
                            s.Width == _selectedMonitor.Width &&
                            s.Height == _selectedMonitor.Height,
                Tag = s
            };
            item.Click += Monitor_Click;
            MonitorRoot.Items.Add(item);
        }
    }


    private void UpdateStyleChecks()
    {
        if (FindName("MenuCrosshairStyle") is MenuItem menuCrosshairStyle)
        {
            foreach (var item in menuCrosshairStyle.Items.OfType<MenuItem>())
            {
                if (item.Header is StackPanel)
                {
                    bool isSelected = false;
                    var btn = ((StackPanel)item.Header).Children.OfType<Button>().FirstOrDefault();
                    if (btn != null && btn.Tag is CustomCrosshair cc)
                    {
                        isSelected = (_currentStyle == CrosshairStyle.Custom && _overlay?.CustomBase64 == cc.Base64Image);
                    }
                    item.Background = isSelected ? new SolidColorBrush(Color.FromRgb(45, 90, 136)) : Brushes.Transparent;
                }
                else
                {
                    bool isSelected = (item.Tag is string tag && _currentStyle.ToString() == tag);
                    item.Background = isSelected ? new SolidColorBrush(Color.FromRgb(45, 90, 136)) : Brushes.Transparent;
                }
            }
        }
    }

    private MenuItem? FindStyleItem(string tag) =>
        (BtnCrosshair.ContextMenu.Items[0] as MenuItem)?
            .Items
            .OfType<MenuItem>()
            .FirstOrDefault(mi => (string)mi.Tag == tag);

    private void Style_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string tag)
        {
            _currentStyle = tag switch
            {
                "GreenDot" => CrosshairStyle.GreenDot,
                "MiniGreen" => CrosshairStyle.MiniGreen,
                "OpenCrossRG" => CrosshairStyle.OpenCrossRG,
                "ThinRedCircle" => CrosshairStyle.ThinRedCircle,
                "SquareDot" => CrosshairStyle.SquareDot,
                "MagentaDot" => CrosshairStyle.MagentaDot,
                "MagentaOpenCross" => CrosshairStyle.MagentaOpenCross,
                "RangeLine" => CrosshairStyle.RangeLine,
                _ => _currentStyle
            };

            UpdateStyleChecks();

            if (!_visible)
            {
                ShowOverlay();
            }
            else if (_overlay != null)
            {
                _overlay.SetStyle(_currentStyle);
                // nach Größenänderung neu zentrieren
                if (_selectedMonitor != null)
                    PositionOverlayCentered(_overlay, _selectedMonitor);
            }
        }
    }

    private void Monitor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is MonitorInfo s)
        {
            _selectedMonitor = s;

            foreach (MenuItem it in MonitorRoot.Items)
                it.IsChecked = ReferenceEquals(it.Tag, _selectedMonitor);

            if (_visible && _overlay != null && _selectedMonitor != null)
                PositionOverlayCentered(_overlay, _selectedMonitor);
        }
    }


    // === Map-Mapping-State ===
    private Rect _worldRectPx;      // zentriertes Welt-Quadrat in Bild-Pixeln
    private int _worldSizeS;       // WorldSize (S) der aktuellen Map
                                   // === Dynamische Marker (z.B. Shops) ===
    private readonly Dictionary<uint, FrameworkElement> _shopEls = new();
    private DispatcherTimer? _shopTimer;
    // eingebaute Minimal-Liste: ID -> Shortname
    private static readonly Dictionary<int, string> sIdToShort = new();
    private static readonly Dictionary<string, string> sShortToNice = new(StringComparer.OrdinalIgnoreCase);
    private static bool sItemMapLoaded;
    private static string sItemMapSource = "(unbekannt)";
    private readonly Dictionary<uint, FrameworkElement> _dynEls = new();   // UI per marker

    private sealed class DynMarkerState
    {
        public List<(double X, double Y)> History = new();
        public int MissingCount;
        public int Type;
        public double LastVX, LastVY;
        public double LastCalculatedAngle;
        public bool SeenAtEdge;
        public double LastRealX, LastRealY; // last confirmed non-ghost position (for crash detection)
    }
    private readonly Dictionary<uint, DynMarkerState> _dynStates = new();
    private readonly HashSet<uint> _dynKnown = new();                      // “already spawned” for chat announcements
    private DispatcherTimer? _dynTimer;
    private bool _showPlayers = true;                                      // controlled by ChkPlayers
                                                                           // Wie stark Icons die Zoom-Stufe kompensieren (je kleiner der Exponent, desto GRÖSSER beim Rauszoomen)
    private const double MON_SIZE_EXP = 0.5;  // Monumente: sehr präsent beim Rauszoomen


    // Globale Grenzen, damit es nicht ausufert
    private const double ICON_SCALE_MIN = 0.6;  // kleiner als 60% nie
    private const double ICON_SCALE_MAX = 4.5;  // größer als 350% nie

    // Optional: Baseline-Verstärker, um generell alles größer zu machen
    private const double MON_BASE_MULT = 2.2;  // 20% größer als Basis
    private const double SHOP_BASE_MULT = 1.3;  // 30% größer als Basis

    // tiny map from type → icon (pack URIs). Put your icons in /icons as Resource.
    private static readonly Dictionary<int, string> sDynIconByType = new()
{
    { 5, "pack://application:,,,/Assets/icons/cargo.png"  },
    { 6, "pack://application:,,,/Assets/icons/vendor.png"  },
    { 7, "pack://application:,,,/Assets/icons/blocked.png"   }, // Building areas
    { 8, "pack://application:,,,/Assets/icons/patrol.png" },
    { 9, "pack://application:,,,/Assets/icons/crate.png"  }, // alt crate id seen on some builds
    { 4, "pack://application:,,,/Assets/icons/ch47.png"   }, // optional safety
    { 2, "pack://application:,,,/Assets/icons/explosion.png"   }, // optional safety
};
    private static readonly Brush PopupBg = new SolidColorBrush(Color.FromRgb(32, 36, 40));   // dunkel
    private static readonly Brush PopupBrd = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
    private const int SHOPS_WRAP_COLUMNS = 3;   // 3 oder 4 – so viele Karten pro Zeile
    private const double SHOP_CARD_WIDTH = 320; // feste Breite deiner Shop-Karte
    private const double SHOP_GAP = 8;   // Abstand zwischen Karten

    // Lokaler Icon-Cache (z.B. %LOCALAPPDATA%\RustPlusDesk\icons)
    private static readonly string sIconCacheDir =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                               "RustPlusDesk", "icons");

    // === Layers ===
    // Optional: externe Ergänzungen laden (Datei neben der EXE)
    private static bool _itemMapLoaded;
    /// <summary>lädt rust_items.json aus dem Programmordner oder eingebettet als WPF-Resource.</summary>
    /// 

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        // Holen Sie alle laufenden "node"-Prozesse
        var nodes = System.Diagnostics.Process.GetProcessesByName("node");

        foreach (var p in nodes)
        {
            try
            {
                // Überprüfe, ob der Prozess ein Hauptfenster hat.
                // Hintergrundprozesse (wie der Listener) haben in der Regel keins.
                // Der von der "fcm-register"-Methode gestartete Prozess, der den Browser öffnet,
                // sollte eine Ausnahme sein und hat ein Fenster, daher wird er hier ignoriert.
                if (p.MainWindowHandle == IntPtr.Zero)
                {
                    p.Kill(true); // Kill den Prozess und seine Unterprozesse
                }
            }
            catch (Exception ex)
            {
                // Dies fängt Berechtigungsfehler oder Prozesse ab, die bereits beendet sind.
                // Ignoriere die Ausnahme, da das erwartete Verhalten ist.
                // Du kannst hier auch loggen, wenn du möchtest: Debug.WriteLine($"Konnte Prozess {p.Id} nicht beenden: {ex.Message}");
            }
        }
        try
        {
            // falls noch offen/hidden → hart schließen
            if (_overlay != null)
            {
                _overlay.Close();
                _overlay = null;
            }

            // Kontextmenü sauber schließen (optional)
            BtnCrosshair.ContextMenu?.IsOpen.Equals(false);

            // Launch pending update installer if available
            if (!string.IsNullOrEmpty(_updateService.PendingInstallerPath))
            {
                _updateService.StartInstaller(_updateService.PendingInstallerPath);
            }
        }
        catch (Exception ex)
        { }
    }

    // --- Chat Persistence & Switching ---

    private ServerProfile? _lastChatProfile;

    private string GetChatCachePath(string serverId)
    {
        var dir = System.IO.Path.Combine(sIconCacheDir, "..", "chat"); // ../chat/
        Directory.CreateDirectory(dir);
        // Sanitize Filename
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) serverId = serverId.Replace(c, '_');
        return System.IO.Path.Combine(dir, $"{serverId}.json");
    }

    private void SaveChatHistory(ServerProfile? p)
    {
        if (p == null) return;
        try
        {
            var serverKey = $"{p.Host}_{p.Port}";
            var path = GetChatCachePath(serverKey); // Use Host_Port as filename
            lock (_chatHistoryLog)
            {
                // Begrenzen auf z.B. 500
                while (_chatHistoryLog.Count > 500) _chatHistoryLog.RemoveAt(0);

                var json = JsonSerializer.Serialize(_chatHistoryLog);
                System.IO.File.WriteAllText(path, json);
            }
        }
        catch (Exception ex) { AppendLog($"[CHAT-SAVE] {ex.Message}"); }
    }

    private void LoadChatHistory(ServerProfile? p)
    {
        // 1. Clear old
        lock (_chatHistoryLog) { _chatHistoryLog.Clear(); }

        _lastChatTsForCurrentServer = null;

        // UI leeren - Overlay handled by ChatMessages clearing
        ChatMessages.Clear();

        if (p == null) return;

        // 2. Load new server specific history
        try
        {
            var serverKey = $"{p.Host}_{p.Port}";
            var path = GetChatCachePath(serverKey);
            if (System.IO.File.Exists(path))
            {
                var json = System.IO.File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<List<TeamChatMessage>>(json);
                if (loaded != null)
                {
                    lock (_chatHistoryLog)
                    {
                        foreach (var m in loaded)
                        {
                            // In LoadChatHistory we don't deduplicate yet, just fill
                            _chatHistoryLog.Add(m);
                            
                            // Max TS tracken
                            if (!_lastChatTsForCurrentServer.HasValue || m.Timestamp > _lastChatTsForCurrentServer.Value)
                                _lastChatTsForCurrentServer = m.Timestamp;
                        }
                    }
                }
            }
            AppendLog($"[CHAT-LOAD] Loaded {_chatHistoryLog.Count} entries for {serverKey}");
        }
        catch (Exception ex) { AppendLog($"[CHAT-LOAD] {ex.Message}"); }
    }

    // Ersetzt deine bestehende SwitchCameraSourceTo Logic z.T.
    private void SwitchCameraSourceTo(ServerProfile? srv)
    {
        if (srv == null)
        {
            // If srv is null, we are effectively disconnecting from a server.
            // Save chat for the last profile, then clear camera IDs.
            SaveChatHistory(_lastChatProfile);
            _lastChatProfile = null; // No current server
            _cameraIds = new ObservableCollection<string>();
            RebuildCameraTiles();
            return;
        }

        // 1. Chat speichern (alter Server)
        SaveChatHistory(_lastChatProfile);
        
        // 2. Chat laden (neuer Server)
        LoadChatHistory(srv);
        
        _lastChatProfile = srv;

        // Reset state for specific server logic
        _monumentWatcher.Reset();
        _deepSeaActive = false;
        _firstShopPollDone = false;
        _deepSeaSpawnTime = null;
        _deepSeaDespawnTime = null;
        _deepSeaMidEvent = false;
        foreach (var cs in _heliCrashSites) { if (cs.MapElement != null) Overlay?.Children.Remove(cs.MapElement); }
        _heliCrashSites.Clear();

        if (_rust is RustPlusClientReal real)
        {
             // real.Disconnect(); // Nein, wir sharen die Instanz, wir reconnecten erst bei "Connect"
        }
        
        srv.CameraIds ??= new ObservableCollection<string>(); // Ensure CameraIds is initialized
        _cameraIds = srv.CameraIds;          
        RebuildCameraTiles();
        EnsureCamThumbPolling();
    }

    // Verbesserter Key ohne Zeitstempel (nur Inhalt + Autor)
    private static string ChatKey(TeamChatMessage m)
    {
        var author = (m.Author ?? "").Trim().ToLowerInvariant();
        var text = (m.Text ?? "").Trim().ToLowerInvariant();
        return $"{author}|{text}";
    }

    private sealed class ItemInfo
    {
        public int Id { get; init; }
        public string ShortName { get; init; } = "";
        public string Display { get; init; } = "";   // „pretty“ name
        public string? IconUrl { get; init; }
    }

    private static readonly Dictionary<int, ItemInfo> sItemsById = new();
    private static readonly Dictionary<string, ItemInfo> sItemsByShort = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ImageSource> sIconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> sPendingDownloads = new();
    private static bool sNewDbLoaded = false;
    private static string sNewDbSource = "(unbekannt)";

    private static void EnsureNewItemDbLoaded(bool force = false)
    {
        if (sNewDbLoaded && !force) return;

        sItemsById.Clear();
        sItemsByShort.Clear();
        sNewDbSource = "(unbekannt)";

        bool loaded = false;

        // 1) Disk-Kandidaten
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string currDir = Environment.CurrentDirectory;
        string? entryDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath);

        var diskCandidates = new[]
        {
        System.IO.Path.Combine(baseDir, "rust-item-list.json"),
        System.IO.Path.Combine(currDir, "rust-item-list.json"),
        entryDir is null ? null : System.IO.Path.Combine(entryDir, "rust-item-list.json"),
        // häufige Ordner:
        System.IO.Path.Combine(baseDir, "assets", "rust-item-list.json"),
        System.IO.Path.Combine(baseDir, "data",   "rust-item-list.json"),
        System.IO.Path.Combine(baseDir, "Assets", "Data", "rust-item-list.json"),
    }.Where(p => !string.IsNullOrWhiteSpace(p)).Cast<string>();

        foreach (var path in diskCandidates)
        {
            try
            {
                if (System.IO.File.Exists(path))
                {
                    var json = System.IO.File.ReadAllText(path);
                    if (TryParseNewItemList(json))
                    {
                        sNewDbSource = System.IO.Path.GetFileName(path) + " (Disk: " + System.IO.Path.GetDirectoryName(path) + ")";
                        loaded = true;
                        break;
                    }
                }
            }
            catch { /* tolerant */ }
        }

        // 2) WPF-Resource (Build Action: Resource)
        if (!loaded)
        {
            string asmName = System.Reflection.Assembly.GetEntryAssembly()!.GetName().Name!;
            var packUris = new[]
            {
            "pack://application:,,,/rust-item-list.json",
            "pack://application:,,,/assets/rust-item-list.json",
            "pack://application:,,,/data/rust-item-list.json",
            "pack://application:,,,/Assets/Data/rust-item-list.json",
            $"pack://application:,,,/{asmName};component/rust-item-list.json",
            $"pack://application:,,,/{asmName};component/assets/rust-item-list.json",
            $"pack://application:,,,/{asmName};component/data/rust-item-list.json",
            $"pack://application:,,,/{asmName};component/Assets/Data/rust-item-list.json",
        };

            foreach (var uri in packUris)
            {
                try
                {
                    var sri = System.Windows.Application.GetResourceStream(new Uri(uri));
                    if (sri?.Stream != null)
                    {
                        using var r = new StreamReader(sri.Stream);
                        if (TryParseNewItemList(r.ReadToEnd()))
                        {
                            sNewDbSource = uri + " (Resource)";
                            loaded = true;
                            break;
                        }
                    }
                }
                catch { /* tolerant */ }
            }
        }

        sNewDbLoaded = loaded;
#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[items-new] loaded={loaded} source={sNewDbSource} count={sItemsById.Count}");
#endif
    }
    private void BindIcon(Image img, string? shortName, int itemId)
    {
        BindIcon(img, itemId, shortName);
    }
    private static void BindIcon(Image img, int itemId, string? shortName, int decodePx = 32)
    {
        // 1) Sofort versuchen
        var src = ResolveItemIcon(itemId, shortName, decodePx);
        if (src != null) { img.Source = src; return; }

        // 2) Download wurde von ResolveItemIcon bereits angestoßen → in Intervallen nochmal versuchen
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < 10; i++)   // ~2.75s max (250+300+…)
            {
                await Task.Delay(250 + i * 250);
                var ready = ResolveItemIcon(itemId, shortName, decodePx);
                if (ready != null)
                {
                    // auf UI-Thread setzen
                    Application.Current.Dispatcher.Invoke(() => img.Source = ready);
                    break;
                }
            }
        });
    }
    private Border BuildOfferRowUI(RustPlusClientReal.ShopOrder o)
    {
        bool outOfStock = o.Stock <= 0;

        var row = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(outOfStock ? (byte)28 : (byte)42, 255, 255, 255)),
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(8, 6, 8, 6),
            Opacity = outOfStock ? 0.70 : 1.0
        };

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // Icon L
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Name+Stock
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // "Price"
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // Icon R+Amount
        row.Child = g;

        // Linkes Icon mit Mengen-Badge (xN nur wenn >1)
        var leftIcon = CreateShopIconwithBadge(o.ItemShortName, o.ItemId, o.Quantity);
        Grid.SetColumn(leftIcon, 0);
        g.Children.Add(leftIcon);

        // Name + Stock
        var nameStack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        nameStack.Children.Add(new TextBlock
        {
            Text = ResolveItemName(o.ItemId, o.ItemShortName),
            Foreground = Brushes.White,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 200
        });
        var stockPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        stockPanel.Children.Add(new TextBlock
        {
            Text = "Stock",
            Foreground = new SolidColorBrush(Color.FromArgb(200, 220, 220, 220)),
            FontSize = 11,
            Margin = new Thickness(0, 2, 6, 0)
        });
        stockPanel.Children.Add(new TextBlock
        {
            Text = o.Stock.ToString(),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13
        });
        nameStack.Children.Add(stockPanel);
        Grid.SetColumn(nameStack, 1);
        g.Children.Add(nameStack);

        // "Price" Label
        var priceLbl = new TextBlock
        {
            Text = "Price",
            Foreground = new SolidColorBrush(Color.FromArgb(200, 220, 220, 220)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(priceLbl, 2);
        g.Children.Add(priceLbl);

        // Rechtes Icon + Amount
        var pricePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var curIcon = new Image { Width = 32, Height = 32, Margin = new Thickness(0, 0, 6, 0), Opacity = outOfStock ? 0.65 : 1.0 };
        // <- dank Overload ist die Reihenfolge egal
        BindIcon(curIcon, o.CurrencyShortName, o.CurrencyItemId);
        pricePanel.Children.Add(curIcon);
        pricePanel.Children.Add(new TextBlock
        {
            Text = o.CurrencyAmount.ToString(),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(pricePanel, 3);
        g.Children.Add(pricePanel);

        return row;
    }

    // Sichtbarkeit per Checkbox/Toggle
    private bool _showMonuments = true;

    // Overlay-Elemente für Monumente
    private readonly Dictionary<string, FrameworkElement> _monEls = new();

    // Rohdaten (aus GetMapWithMonumentsAsync)
    private List<(double X, double Y, string Name)> _monData = new();

    // Icon-Zuordnung (key = normalisierte Kennung)

    private static readonly Dictionary<string, string> sMonIconByKeyRaw = new(StringComparer.OrdinalIgnoreCase)
{
    // nur Beispiele – ergänze frei:
    { "stone quarry",            "pack://application:,,,/Assets/icons/stonequarry.png" },
    { "hqm quarry",              "pack://application:,,,/Assets/icons/hqmquarry.png" },
    { "sulfur quarry",           "pack://application:,,,/Assets/icons/sulfurquarry.png" },
    { "excavator",               "pack://application:,,,/Assets/icons/excavator.png" },
    { "train tunnel",            "pack://application:,,,/Assets/icons/traintunnel2.png" },
    { "train tunnel link",       "pack://application:,,,/Assets/icons/traintunnel.png" },
    { "supermarket",             "pack://application:,,,/Assets/icons/supermarket.png" },
    { "abandoned military base", "pack://application:,,,/Assets/icons/militarybase.png" },
    { "large fishing village",   "pack://application:,,,/Assets/icons/fishingvillagelarge.png" },
    { "power plant",             "pack://application:,,,/Assets/icons/powerplant.png" },
    { "mining outpost",          "pack://application:,,,/Assets/icons/miningoutpost.png" },
    { "military tunnel",         "pack://application:,,,/Assets/icons/militarytunnel.png" },
    { "gas station",             "pack://application:,,,/Assets/icons/gasstation.png" },
    { "arctic base",             "pack://application:,,,/Assets/icons/arcticresearch.png" },
    { "sewer branch",            "pack://application:,,,/Assets/icons/sewerbranch.png" },
    { "airfield",                "pack://application:,,,/Assets/icons/airfield.png" },
    { "radtown",                 "pack://application:,,,/Assets/icons/radtown.png" },
    { "stables a",               "pack://application:,,,/Assets/icons/stable.png" },
    { "stables b",               "pack://application:,,,/Assets/icons/barn.png" },
    { "dome",                    "pack://application:,,,/Assets/icons/dome.png" },
    { "harbor",                  "pack://application:,,,/Assets/icons/harbour.png" },
    { "harbor 2",                "pack://application:,,,/Assets/icons/harbour2.png" },
    { "lighthouse",              "pack://application:,,,/Assets/icons/lighthouse.png" },
    { "fishing village",         "pack://application:,,,/Assets/icons/fishingvillage.png" },
    { "missile silo",            "pack://application:,,,/Assets/icons/missilesilo.png" },
    { "ferry terminal",          "pack://application:,,,/Assets/icons/ferryterminal.png" },
    { "train yard",              "pack://application:,,,/Assets/icons/trainyard.png" },
    { "satellite dish",          "pack://application:,,,/Assets/icons/satellitedish.png" },
    { "outpost",                 "pack://application:,,,/Assets/icons/outpost.png" },
    { "launch site",             "pack://application:,,,/Assets/icons/launchsite.png" },
    { "water treatment plant",   "pack://application:,,,/Assets/icons/watertreatment.png" },
    { "large oil rig",           "pack://application:,,,/Assets/icons/largeoilrig.png" },
    { "small oil rig",           "pack://application:,,,/Assets/icons/oilrig.png" },
    { "underwater lab",          "pack://application:,,,/Assets/icons/underwater.png" },
    { "underwater lab b",          "pack://application:,,,/Assets/icons/underwater.png" },
    { "underwater labs",          "pack://application:,,,/Assets/icons/underwater.png" },
    { "junkyard",                "pack://application:,,,/Assets/icons/junkyard.png" },
    { "bandit camp",             "pack://application:,,,/Assets/icons/banditcamp.png" },
    { "swamp",                   "pack://application:,,,/Assets/icons/swamp.png" },
    { "jungle ziggurat",         "pack://application:,,,/Assets/icons/jungle.png" },
};

    private static double CalcOverlayScale(double effZoom, double exp, double baseMult = 1.0)
    {
        // Gegen-Skalierung (1 / effZoom^exp) + Baseline + Clamp
        var s = Math.Pow(effZoom, -exp) * baseMult;
        return Math.Clamp(s, ICON_SCALE_MIN, ICON_SCALE_MAX);
    }

    private static readonly Dictionary<string, string> sMonIconByKey =
    BuildCanonIconMap(sMonIconByKeyRaw);

    private static Dictionary<string, string> BuildCanonIconMap(
        Dictionary<string, string> raw)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in raw)
        {
            var key = Canon(kv.Key);              // <- deine Canon(...) von oben
            if (string.IsNullOrEmpty(key)) continue;

            // Bei Kollision gewinnt der „präzisere“ Eintrag: Priorisiere längere Keys
            if (!map.TryGetValue(key, out var existing) || kv.Key.Length > existing.Length)
                map[key] = kv.Value;
        }
        return map;
    }
    private static string NormalizeMonName(string raw, out string variant)
    {
        variant = "";
        var low = raw?.ToLowerInvariant() ?? "";
        if (low.Contains("underwater") || low.Contains("under water") || low.Contains("underwaterlab") || low.Contains("moonpool"))
        {
            // Do not extract variant for underwater labs to merge them under one name "Underwater Labs"
        }
        else
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(low, @"\s+a\s*$")) variant = "A";
            else if (System.Text.RegularExpressions.Regex.IsMatch(low, @"\s+b\s*$")) variant = "B";
            else if (System.Text.RegularExpressions.Regex.IsMatch(low, @"\s+c\s*$")) variant = "C";
        }

        return Canon(raw); // <- macht die eigentliche harte Arbeit
    }

    private static string Canon(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.ToLowerInvariant();

        if (s.Contains("underwater") || s.Contains("under water") || s.Contains("underwaterlab") || s.Contains("moonpool"))
        {
            return "underwater labs";
        }

        // unerwünschte Suffixe/Teile robust entfernen (auch mehrfach, egal wo)
        s = System.Text.RegularExpressions.Regex.Replace(
                s,
                @"\b(display\s*name|monument\s*name)\b",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Klammer-Inhalte mit genau diesen Phrasen entfernen, z. B. "(display name)"
        s = System.Text.RegularExpressions.Regex.Replace(
                s,
                @"\((?:\s*(?:display\s*name|monument\s*name)\s*)\)",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Trennzeichen vereinheitlichen
        s = s.Replace('_', ' ').Replace('-', ' ');

        // Varianten A/B/C am Ende abtrennen
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+([abc])\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Mehrfach-Whitespace reduzieren + trimmen
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();

        // Aliase vereinheitlichen
        s = s.Replace("mining quarry stone", "stone quarry")
             .Replace("mining quarry hqm", "hqm quarry")
             .Replace("mining quarry sulfur", "sulfur quarry")
             .Replace("underwaterlab", "underwater lab")
             .Replace("underwater lab c", "underwater lab")
               .Replace("underwater lab b", "underwater lab")
                 .Replace("underwater lab a", "underwater lab")
              .Replace("sewer display name", "sewer branch")
             .Replace("abandonedmilitarybase", "abandoned military base")
             .Replace("ferryterminal", "ferry terminal")
             .Replace("launch site", "launchsite")
             .Replace("missile silo monument", "missile silo")
             .Replace("military tunnels display name", "military tunnel")
             .Replace("oil rig small", "small oil rig")
            .Replace("module 900x900 2way moonpool", "Moon Pool");

        return s;
    }

    private FrameworkElement MakeMonIcon(string key, string tooltip, int size = 64)
    {
        key = Canon(key);

        if (TrackingService.MapMonumentDisplayMode == 1) // Original text monument names
        {
            if (key.Contains("train tunnel"))
            {
                try
                {
                    var img = MakeIcon("pack://application:,,,/Assets/icons/assets_markers_train.png", size);
                    ToolTipService.SetToolTip(img, tooltip);
                    return img;
                }
                catch { /* falls back to text flow */ }
            }

            var textBlock = new TextBlock
            {
                Text = tooltip,
                FontFamily = new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Permanent Marker"),
                Foreground = Brushes.Black,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            var textBorder = new Border
            {
                Child = textBlock,
                Padding = new Thickness(2, 1, 2, 1),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };

            ToolTipService.SetToolTip(textBorder, tooltip);
            return textBorder;
        }

        if (TrackingService.MapMonumentDisplayMode == 0) // Icons by rustmaps.com
        {
            if (sMonIconByKey.TryGetValue(key, out var uri))
            {
                try
                {
                    var img = MakeIcon(uri, size);
                    ToolTipService.SetToolTip(img, tooltip);
                    return img;
                }
                catch { /* fällt auf Dot zurück */ }
            }
        }

        // Mode 2: Default icons (or fallback dot for Mode 0)
        var dot = new Ellipse
        {
            Width = Math.Max(1, size / 5),
            Height = Math.Max(1, size / 5),
            Fill = Brushes.OrangeRed,
            Stroke = Brushes.Black,
            StrokeThickness = 1.5
        };
        ToolTipService.SetToolTip(dot, tooltip);
        return dot;
    }


    private Grid CreateShopIconwithBadge(string? shortName, int itemId, int qty)
    {
        var g = new Grid { Width = 32, Height = 32 };

        var img = new Image { Width = 32, Height = 32, Stretch = Stretch.Uniform };
        // Reihenfolge beliebig dank Overload
        BindIcon(img, shortName, itemId);
        g.Children.Add(img);

        if (qty > 1)
        {
            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 20, 20, 20)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4, 0, 4, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -6, -6, 0)
            };
            badge.Child = new TextBlock
            {
                Text = $"x{qty}",
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            g.Children.Add(badge);
        }

        return g;
    }

    private static async Task<bool> TryUpdateItemDbAsync()
    {
        const string url = "https://rusthelp.com/downloads/admin-item-list-public.json";
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RustPlusDesktop/1.0");
            client.Timeout = TimeSpan.FromSeconds(15);
            
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(json) || !json.Trim().StartsWith("[")) return false;

            // Grobe Validierung: ist es ein JSON Array mit Items?
            if (!json.Contains("shortName") || !json.Contains("displayName")) return false;

            // Pfad bestimmen: Wir speichern direkt ins Root-Verzeichnis, da dies die höchste Priorität beim Laden hat
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string targetPath = System.IO.Path.Combine(baseDir, "rust-item-list.json");

            // Sicherstellen, dass Ordner existiert
            string? dir = System.IO.Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            await System.IO.File.WriteAllTextAsync(targetPath, json);
            return true;
        }
        catch { return false; }
    }

    private static bool TryParseNewItemList(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return false;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                int id = el.TryGetProperty("id", out var pid) ? pid.GetInt32() : 0;
                string shortName = el.TryGetProperty("shortName", out var ps) ? (ps.GetString() ?? "") : "";
                string display = el.TryGetProperty("displayName", out var pd) ? (pd.GetString() ?? "") : "";
                string? icon = el.TryGetProperty("iconUrl", out var pi) ? pi.GetString() : null;

                if (id == 0 && string.IsNullOrWhiteSpace(shortName)) continue;

                var ii = new ItemInfo
                {
                    Id = id,
                    ShortName = shortName,
                    Display = string.IsNullOrWhiteSpace(display) ? (shortName ?? $"Item #{id}") : display,
                    IconUrl = string.IsNullOrWhiteSpace(icon) ? null : icon
                };

                if (id != 0) sItemsById[id] = ii;
                if (!string.IsNullOrWhiteSpace(shortName)) sItemsByShort[shortName] = ii;
            }

            return sItemsById.Count + sItemsByShort.Count > 0;
        }
        catch { return false; }
    }

    private static void EnsureItemMapLoaded()
    {
        if (sItemMapLoaded) return;            // nur wenn noch nicht geladen

        sIdToShort.Clear();
        sShortToNice.Clear();

        bool loaded = false;

        // 1) Disk – bevorzugt (Content + Copy if newer)
        foreach (var path in new[] {
        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rust_items.json"),
        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "items-map.json"),
        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Data", "rust_items.json"),
        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Data", "items-map.json"),
    })
        {
            if (System.IO.File.Exists(path))
            {
                if (TryLoadFromJson(System.IO.File.ReadAllText(path)))
                {
                    sItemMapSource = System.IO.Path.GetFileName(path) + " (Disk)";
                    loaded = true;
                    break;
                }
            }
        }

        // 2) WPF Resource – fallback (REBUILD nötig, wenn du die Datei änderst)
        if (!loaded)
        {
            foreach (var uri in new[] {
            "pack://application:,,,/rust_items.json",
            "pack://application:,,,/items-map.json",
            "pack://application:,,,/Assets/Data/rust_items.json",
            "pack://application:,,,/Assets/Data/items-map.json",
        })
            {
                try
                {
                    var sri = Application.GetResourceStream(new Uri(uri));
                    if (sri?.Stream != null)
                    {
                        using var r = new StreamReader(sri.Stream);
                        if (TryLoadFromJson(r.ReadToEnd()))
                        {
                            sItemMapSource = uri + " (Resource)";
                            loaded = true;
                            break;
                        }
                    }
                }
                catch { /* tolerant */ }
            }
        }

        sItemMapLoaded = loaded;
#if DEBUG
        System.Diagnostics.Debug.WriteLine(
            $"[items] loaded={loaded} source={sItemMapSource} id->short={sIdToShort.Count} short->nice={sShortToNice.Count}");
#endif
    }


    // gibt true zurück, wenn mind. ein Mapping ankam (beide Dictionaries werden ergänzt)
    private static bool TryLoadFromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("id_to_short", out var ids) && ids.ValueKind == JsonValueKind.Object)
            {
                foreach (var kv in ids.EnumerateObject())
                    if (int.TryParse(kv.Name, out var id))
                    {
                        var sn = kv.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(sn))
                            sIdToShort[id] = sn!;
                    }
            }

            if (root.TryGetProperty("short_to_nice", out var nice) && nice.ValueKind == JsonValueKind.Object)
            {
                foreach (var kv in nice.EnumerateObject())
                {
                    var pretty = kv.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(pretty))
                        sShortToNice[kv.Name] = pretty!;
                }
            }

            return sIdToShort.Count > 0 || sShortToNice.Count > 0;
        }
        catch { return false; }
    }


    /// <summary>gibt einen schönen Anzeigenamen zurück (Shortname bevorzugt, sonst ID-Fallback)</summary>
    public static string ResolveItemName(int itemId, string? shortName)
    {
        // 1) neue DB bevorzugt
        EnsureNewItemDbLoaded();
        if (itemId != 0 && sItemsById.TryGetValue(itemId, out var ii1) && !string.IsNullOrWhiteSpace(ii1.Display))
            return ii1.Display;
        if (!string.IsNullOrWhiteSpace(shortName) && sItemsByShort.TryGetValue(shortName!, out var ii2) && !string.IsNullOrWhiteSpace(ii2.Display))
            return ii2.Display;

        // 2) Fallback: alte Map
        EnsureItemMapLoaded();
        if (!string.IsNullOrWhiteSpace(shortName) && sShortToNice.TryGetValue(shortName!, out var nice))
            return nice;
        if (sIdToShort.TryGetValue(itemId, out var sn))
            return sShortToNice.TryGetValue(sn, out var nice2) ? nice2 : sn;

        // 3) letzter Fallback
        return !string.IsNullOrWhiteSpace(shortName) ? shortName! : $"Item #{itemId}";
    }


    /// <summary>Formatiert eine Shop-Zeile angenehm lesbar.</summary>
    private static string FormatShopLine(RustPlusClientReal.ShopOrder o)
    {
        var left = $"{ResolveItemName(o.ItemId, o.ItemShortName)} x{o.Quantity}";
        var right = $"{o.CurrencyAmount} {ResolveItemName(o.CurrencyItemId, o.CurrencyShortName)}";
        var stock = o.Stock > 0 ? $" (stock {o.Stock})" : "";
        var bp = o.IsBlueprint ? " [BP]" : "";

        return $"{left} → {right}{stock}{bp}";
    }
private sealed record MarkerRef(System.Windows.Shapes.Ellipse Dot, double U_DIP, double V_DIP, double Radius);
    private readonly List<MarkerRef> _markers = new();

    private AlarmWindow? _alarmWin; // nicht AlarmPopupWindow
    private readonly ObservableCollection<AlarmNotification> _alarmFeed = new();
    private readonly Dictionary<string, DateTime> _lastAlarmProcessed = new();
    private DateTime _lastAnyAlarmTime = DateTime.MinValue; // Globaler Marker für Fuzzy-Dedup
    private readonly Dictionary<uint, (string Title, string Message)> _alarmMetadataCache = new();
    private readonly Dictionary<string, (uint Id, DateTime Time)> _lastSeenIdPerServer = new();
    private readonly List<string> _alarmHistoryDedup = new();

    private void ShowAlarmPopup(AlarmNotification n, string source = "FCM")
    {
        // 0) Backlog-Filter: Ignoriere Alarme, die älter als 5 Minuten sind
        if ((DateTime.Now - n.Timestamp).TotalMinutes > 5) return;

        // 0.1) Exakter Duplikat-Check (Server + Msg + Zeitstempel)
        string dedupKey = $"{n.Server}|{n.Message}|{n.Timestamp:yyyyMMddHHmmss}";
        if (_alarmHistoryDedup.Contains(dedupKey)) return;
        _alarmHistoryDedup.Add(dedupKey);
        if (_alarmHistoryDedup.Count > 100) _alarmHistoryDedup.RemoveAt(0);

        if (n.Message == "Your base is under attack!")
        {
            n = n with { Message = Properties.Resources.YourBaseIsUnderAttack };
        }

        var now = DateTime.UtcNow;

        // Servernamen bereinigen für stabiles Mapping
        string cleanSrv = Regex.Replace(n.Server ?? "", @"\x1B\[[0-9;]*[A-Za-z]", "");
        cleanSrv = Regex.Replace(cleanSrv, @"\[/?[a-zA-Z]+\]", "").Trim();
        if (string.IsNullOrEmpty(cleanSrv)) cleanSrv = "-";

        // Wenn die Meldung eine ID hat (WS), merken wir sie uns für diesen Server
        if (n.EntityId.HasValue)
        {
            _lastSeenIdPerServer[cleanSrv] = (n.EntityId.Value, now);
        }

        SmartDevice? dev = null;
        if (n.EntityId.HasValue)
        {
            uint eid = n.EntityId.Value;
            foreach (var profile in _vm.Servers)
            {
                dev = FindDeviceById(profile.Devices, eid);
                if (dev != null) break;
            }
        }

        // FUZZY MATCH: If we couldn't map the device via ID (e.g. generic FCM push without ID),
        // we try to find a matching server and check if it has exactly one Smart Alarm.
        if (dev == null)
        {
            // 1) Try to find the specific server profile first
            var profile = _vm.Servers.FirstOrDefault(s => 
                string.Equals(Regex.Replace(s.Name ?? "", @"\x1B\[[0-9;]*[A-Za-z]", "").Trim(), cleanSrv, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Host, cleanSrv, StringComparison.OrdinalIgnoreCase));

            if (profile != null)
            {
                var serverAlarms = profile.Devices.Where(d => d.Kind == "SmartAlarm").ToList();
                if (serverAlarms.Count == 1)
                {
                    dev = serverAlarms[0];
                    n = n with { EntityId = dev.EntityId };
                    AppendLog($"[alarm/debug] ({source}) Fuzzy matched single alarm on server '{cleanSrv}': {dev.Name} (ID: {dev.EntityId})");
                }
            }

            // 2) Global Fallback: check if there is EXACTLY ONE Smart Alarm registered across all servers.
            if (dev == null)
            {
                var allAlarms = _vm.Servers.SelectMany(s => s.Devices).Where(d => d.Kind == "SmartAlarm").ToList();
                if (allAlarms.Count == 1)
                {
                    dev = allAlarms[0];
                    n = n with { EntityId = dev.EntityId };
                    AppendLog($"[alarm/debug] ({source}) Fuzzy matched single global alarm device: {dev.Name} (ID: {dev.EntityId})");
                }
            }
        }

        // Metadaten cachen (FCM liefert Text, WS liefert ID)
        if (source == "FCM" && n.Message != "Alarm activated!")
        {
            uint? tid = n.EntityId;
            // Falls FCM keine ID hat, versuchen wir sie über den letzten WS-Event dieses Servers zu finden
            if (!tid.HasValue && _lastSeenIdPerServer.TryGetValue(cleanSrv, out var last) && (now - last.Time).TotalSeconds < 10)
            {
                tid = last.Id;
                // WICHTIG: Die Benachrichtigung selbst mit der ID aktualisieren, damit UpdateOrAdd korrekt funktioniert!
                n = n with { EntityId = tid };
            }

            if (tid.HasValue)
            {
                _alarmMetadataCache[tid.Value] = (n.DeviceName, n.Message);
                if (dev == null)
                {
                    foreach (var profile in _vm.Servers) { dev = FindDeviceById(profile.Devices, tid.Value); if (dev != null) break; }
                }
                if (dev != null) dev.LastAlarmMessage = n.Message;
            }
        }
        
        // n.Server ebenfalls bereinigen für konsistentes UI/Matching
        n = n with { Server = cleanSrv };

        // Override DeviceName with Custom Name / PureName if device is identified
        if (dev != null)
        {
            n = n with { DeviceName = dev.PureName };
        }

        if (n.EntityId.HasValue)
        {
            // Dedup primär über ID (ignoriere Server-Namensunterschiede wie ANSI-Farben)
            string key = $"ID:{n.EntityId.Value}";
            if (_lastAlarmProcessed.TryGetValue(key, out var last) && (now - last).TotalSeconds < 5)
            {
                // Wenn dies eine detaillierte FCM-Meldung ist, die auf eine generische WS-Meldung folgt: Update!
                if (source == "FCM" && n.Message != "Alarm activated!")
                {
                    if (_alarmWin != null && _alarmWin.IsLoaded)
                    {
                        _alarmWin.UpdateOrAdd(n);
                    }
                }
                return;
            }
            _lastAlarmProcessed[key] = now;
            _lastAnyAlarmTime = now; // Merken, dass IRGENDEIN Alarm kam
        }
        else
        {
            // FUZZY DEDUP: Wenn gerade erst (vor < 5s) ein gezielter Alarm kam, 
            // ignoriere diesen generischen (ID-losen) FCM-Alarm.
            if ((now - _lastAnyAlarmTime).TotalSeconds < 5)
            {
                // Auch hier: Wenn es ein detaillierter FCM-Alarm ist -> Updaten statt droppen!
                if (source == "FCM" && n.Message != "Alarm activated!")
                {
                    if (_alarmWin != null && _alarmWin.IsLoaded)
                    {
                        _alarmWin.UpdateOrAdd(n);
                    }
                }
                AppendLog($"[alarm/debug] ({source}) Dropping generic alarm because a specific alarm was just handled (or updated).");
                return;
            }
            _lastAnyAlarmTime = now;
        }

        // 4) Play Audio (Respects settings if device is identified, otherwise plays default)
        PlayAlarmAudio(dev);

        // Send smart alert to team chat if setting and master switch are enabled
        if (TrackingService.AnnounceSmartAlerts && _announceSpawns)
        {
            string alarmName = dev?.PureName ?? (!string.IsNullOrEmpty(n.DeviceName) ? n.DeviceName : "Smart Alarm");
            _ = SendTeamChatSafeAsync(string.Format(Properties.Resources.AlertAlarmTriggered, alarmName));
        }

        if (dev != null)
        {
            AppendLog($"[alarm/debug] ({source}) Device identified: {dev.Name} (Kind: {dev.Kind}, ID: {dev.EntityId})");
            AppendLog($"[alarm/debug] ({source}) Settings: AudioEnabled={dev.AudioEnabled}, PopupEnabled={dev.PopupEnabled}");
            
            // Wenn der Alarm via FCM kommt, setzen wir den UI-Zustand manuell auf "ACTIVE" (10s Puls)
            if (source != "WS")
            {
                dev.IsOn = true;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(10000);
                    await Dispatcher.InvokeAsync(() => dev.IsOn = false);
                });
            }

            if (dev.OverlayEnabled)
            {
                AddAlarmToOverlay(dev, n);
            }

            if (!dev.PopupEnabled) 
            {
                AppendLog($"[alarm/debug] ({source}) Skipping popup window because PopupEnabled is false for this device.");
                return; 
            }
        }
        else
        {
            if (n.EntityId.HasValue)
                AppendLog($"[alarm/debug] ({source}) No device found for ID {n.EntityId.Value}. Showing generic popup.");
            else
                AppendLog($"[alarm/debug] ({source}) Generic alarm (no ID). Showing generic popup.");

            // Generic overlay fallback
            AddAlarmToOverlay(null, n);
        }

        AppendLog($"[alarm/debug] ({source}) Executing: Show Alarm Window");

        if (_alarmWin is null || !_alarmWin.IsLoaded)
        {
            _alarmWin = new AlarmWindow { Owner = this };
            _alarmWin.Closed += (_, __) => _alarmWin = null;
            _alarmWin.Show();
        }
        _alarmWin.Add(n);
    }

    private void AddAlarmToOverlay(SmartDevice? dev, AlarmNotification n)
    {
        Dispatcher.Invoke(() =>
        {
            _overlayAlarms.Add((dev, n));
            _overlayAlarmIndex = _overlayAlarms.Count - 1;
            UpdateAlarmOverlayUi();

            AlarmOverlayBorder.Visibility = Visibility.Visible;

            if (AlarmOverlayAutoHideChk.IsChecked == true)
            {
                RestartAlarmOverlayTimer();
            }
            else
            {
                _overlayHideTimer?.Stop();
            }
        });
    }

    private void UpdateAlarmOverlayUi()
    {
        if (_overlayAlarms.Count == 0 || _overlayAlarmIndex < 0 || _overlayAlarmIndex >= _overlayAlarms.Count)
        {
            AlarmOverlayBorder.Visibility = Visibility.Collapsed;
            return;
        }

        var current = _overlayAlarms[_overlayAlarmIndex];
        
        string srvName = string.IsNullOrWhiteSpace(current.Notification.Server) ? "Unknown Server" : current.Notification.Server;
        AlarmOverlayServerTxt.Text = $"{srvName} - {current.Notification.Timestamp:HH:mm}";
        AlarmOverlayNameTxt.Text = current.Device?.PureName ?? Properties.Resources.SmartAlarm;
        AlarmOverlayMsgTxt.Text = current.Notification.Message ?? Properties.Resources.AlarmActivated;
        
        AlarmOverlayPagingTxt.Text = $"{_overlayAlarmIndex + 1}/{_overlayAlarms.Count}";

        AlarmOverlayPrevBtn.IsEnabled = _overlayAlarmIndex > 0;
        AlarmOverlayNextBtn.IsEnabled = _overlayAlarmIndex < _overlayAlarms.Count - 1;
        
        bool multi = _overlayAlarms.Count > 1;
        AlarmOverlayPrevBtn.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
        AlarmOverlayPagingTxt.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
        AlarmOverlayNextBtn.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AlarmOverlayPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_overlayAlarmIndex > 0)
        {
            _overlayAlarmIndex--;
            UpdateAlarmOverlayUi();
            if (AlarmOverlayAutoHideChk.IsChecked == true) RestartAlarmOverlayTimer();
        }
    }

    private void AlarmOverlayNext_Click(object sender, RoutedEventArgs e)
    {
        if (_overlayAlarmIndex < _overlayAlarms.Count - 1)
        {
            _overlayAlarmIndex++;
            UpdateAlarmOverlayUi();
            if (AlarmOverlayAutoHideChk.IsChecked == true) RestartAlarmOverlayTimer();
        }
    }

    private void AlarmOverlayClose_Click(object sender, RoutedEventArgs e)
    {
        AlarmOverlayBorder.Visibility = Visibility.Collapsed;
        _overlayAlarms.Clear();
        _overlayAlarmIndex = -1;
        _overlayHideTimer?.Stop();
        StopLoopPlayer();
        StopAlarmPlayer();
    }

    private void AlarmOverlayAutoHideChk_Changed(object sender, RoutedEventArgs e)
    {
        if (AlarmOverlayAutoHideChk.IsChecked == true)
        {
            RestartAlarmOverlayTimer();
        }
        else
        {
            _overlayHideTimer?.Stop();
        }
    }

    private void OnAudioLoopClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem item && item.Tag is SmartDevice dev)
        {
            if (dev.AudioLoopEnabled)
            {
                if (!dev.OverlayEnabled) dev.OverlayEnabled = true;
                if (AlarmOverlayAutoHideChk.IsChecked == true)
                {
                    AlarmOverlayAutoHideChk.IsChecked = false;
                }
            }
            UpdateGlobalAutoHideUI();
        }
    }

    private async void OnInAppPopupCheckBoxClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox chk && chk.DataContext is SmartDevice dev)
        {
            if (dev.AudioLoopEnabled)
            {
                e.Handled = true;
                var msgBox = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "Smart Alarm",
                    Content = Properties.Resources.LoopAudioPrompt,
                    PrimaryButtonText = Properties.Resources.TurnOffNow,
                    CloseButtonText = Properties.Resources.KeepActive
                };
                msgBox.Owner = this;
                msgBox.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
                var result = await msgBox.ShowDialogAsync();
                if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                    dev.AudioLoopEnabled = false;
                    dev.OverlayEnabled = false;
                    UpdateGlobalAutoHideUI();
                }
            }
        }
    }

    private async void OnAutoHideCheckBoxClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        bool anyLooping = _vm.CurrentDevices != null && System.Linq.Enumerable.Any(_vm.CurrentDevices, d => d.AudioLoopEnabled);
        if (anyLooping && AlarmOverlayAutoHideChk.IsChecked == false)
        {
            e.Handled = true;
            var msgBox = new Wpf.Ui.Controls.MessageBox
            {
                Title = "Smart Alarm",
                Content = Properties.Resources.LoopAudioGlobalPrompt,
                PrimaryButtonText = Properties.Resources.TurnOffNow,
                CloseButtonText = Properties.Resources.KeepActive
            };
            msgBox.Owner = this;
            msgBox.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
            var result = await msgBox.ShowDialogAsync();
            if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
            {
                if (_vm.CurrentDevices != null)
                {
                    foreach (var d in System.Linq.Enumerable.Where(_vm.CurrentDevices, d => d.AudioLoopEnabled))
                    {
                        d.AudioLoopEnabled = false;
                    }
                }
                AlarmOverlayAutoHideChk.IsChecked = true;
                UpdateGlobalAutoHideUI();
            }
        }
    }

    private void UpdateGlobalAutoHideUI()
    {
        bool anyLooping = _vm.CurrentDevices != null && System.Linq.Enumerable.Any(_vm.CurrentDevices, d => d.AudioLoopEnabled);
        AlarmOverlayAutoHideChk.Opacity = anyLooping ? 0.4 : 1.0;
        if (anyLooping && AlarmOverlayAutoHideChk.IsChecked == true)
        {
            AlarmOverlayAutoHideChk.IsChecked = false;
        }
    }

    private void RestartAlarmOverlayTimer()
    {
        if (_overlayHideTimer == null)
        {
            _overlayHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _overlayHideTimer.Tick += (s, ev) =>
            {
                _overlayHideTimer.Stop();
                AlarmOverlayClose_Click(null!, null!);
            };
        }
        _overlayHideTimer.Stop();
        _overlayHideTimer.Start();
    }

    // Hilfsfunktion: stabiler Schlüssel für eine Chat-Nachricht


    // Liefert Viewbox-Skalierung s und Offsets (Letterboxing) relativ zum WebViewHost
    private (double s, double offX, double offY) GetViewboxScaleAndOffset()
    {
        if (_scene == null || WebViewHost == null) return (1.0, 0.0, 0.0);

        double hostW = Math.Max(1, WebViewHost.ActualWidth);
        double hostH = Math.Max(1, WebViewHost.ActualHeight);

        // Inhalt: wir nehmen die "natürliche" Breite/Höhe der Szene
        double contentW = _scene.ActualWidth > 0 ? _scene.ActualWidth : _scene.Width;
        double contentH = _scene.ActualHeight > 0 ? _scene.ActualHeight : _scene.Height;
        if (contentW <= 0 || contentH <= 0) return (1.0, 0.0, 0.0);

        double s = Math.Min(hostW / contentW, hostH / contentH);
        double offX = (hostW - contentW * s) * 0.5;
        double offY = (hostH - contentH * s) * 0.5;
        return (s, offX, offY);
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (TrackingService.CloseToTrayEnabled)
        {
            e.Cancel = true;
            this.Hide();
            // We still save profiles just in case
            try { _vm.Save(); } catch { }
            return;
        }

        try
        {
            AppendLog($"Speichere Profile → {StorageService.GetProfilesPath()}");
            _vm.Save();
        }
        catch (Exception ex) { AppendLog("Saving failed: " + ex.Message); }
    }

    public void HandleRustPlusLink(string link)
    {
        try
        {
            string host = "";
            int port = 28082; // Standard Rust+ Port
            string playerId = _vm.SteamId64 ?? "0";
            string playerToken = "0";
            string serverName = "Manual Server";

            if (link.Contains("?") && (link.Contains("address=") || link.Contains("ip=")))
            {
                // --- FALL A: Offizieller Link (mit Parametern) ---
                var p = ParseRustPlusLink(link);
                host = p.host;
                port = p.port;
                playerId = p.playerId != 0 ? p.playerId.ToString() : playerId;
                playerToken = p.playerToken.ToString();
                serverName = !string.IsNullOrEmpty(p.name) ? p.name : "Paired Server";
            }
            else
            {
                // --- FALL B: Manueller Link (z.B. rustplus://1.2.3.4:28082) ---
                // Wir entfernen das Protokoll "rustplus://"
                var raw = link.Replace("rustplus://", "").TrimEnd('/');

                if (raw.Contains(":"))
                {
                    var parts = raw.Split(':');
                    host = parts[0];
                    int.TryParse(parts[1], out port);
                }
                else
                {
                    host = raw;
                }

                serverName = "Custom: " + host;
                AppendLog($"Manual IP detected: {host}:{port}");
            }

            if (string.IsNullOrEmpty(host)) throw new Exception("IP/Address missing");

            // Wir rufen die Pairing-Funktion auf
            Pairing_Paired(this, new PairingPayload
            {
                Host = host,
                Port = port,
                SteamId64 = playerId,
                PlayerToken = playerToken,
                ServerName = serverName
            });

            AppendLog($"Server {host} added to list.");
            this.Activate(); // Bringt das Fenster nach vorne
        }
        catch (Exception ex)
        {
            AppendLog("RustPlus-Link-Error: " + ex.Message);
            MessageBox.Show(string.Format(Properties.Resources.UnableToReadLink, ex.Message));
        }
    }


    private (string host, int port, ulong playerId, int playerToken, string? name) ParseRustPlusLink(string link)
    {
        // Beispiele tolerieren:
        // rustplus://connect?ip=1.2.3.4&port=28082&playerId=7656…&playerToken=123456
        // rustplus://?ip=…&port=…&playerid=…&playertoken=…
        // rustplus://add?address=…&port=…&playerid=…&token=…
        var l = link.Trim();

        // in normales Schema wandeln, damit Uri es versteht
        if (l.StartsWith("rustplus://", StringComparison.OrdinalIgnoreCase))
            l = "http://" + l["rustplus://".Length..]; // dummy-scheme

        var uri = new Uri(l);
        var q = System.Web.HttpUtility.ParseQueryString(uri.Query);

        string host = q["ip"] ?? q["address"] ?? throw new ArgumentException("ip/address fehlt");
        if (!int.TryParse(q["port"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)) throw new ArgumentException("port fehlt/ungültig");

        var sidStr = q["playerId"] ?? q["playerid"] ?? throw new ArgumentException("playerId fehlt");
        if (!ulong.TryParse(sidStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var playerId)) throw new ArgumentException("playerId ungültig");

        var tokStr = q["playerToken"] ?? q["playertoken"] ?? q["token"] ?? throw new ArgumentException("playerToken fehlt");
        if (!int.TryParse(tokStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var token)) throw new ArgumentException("playerToken ungültig");

        var name = q["name"];
        return (host, port, playerId, token, name);
    }
    private void Pairing_Paired(object? sender, PairingPayload e)
    {
        // Key OHNE EntityId: dient nur für „Server-keepalive“-Erkennung
        var sig = $"{e.Host}:{e.Port}|{e.SteamId64}|{e.PlayerToken}";

        // >>> NUR keepalives ohne EntityId ignorieren
        if (!e.EntityId.HasValue && string.Equals(sig, _lastPairSig, StringComparison.Ordinal))
        {
            AppendLog("[pairing] keepalive for same server+token – ignored.");
            return;
        }

        _lastPairSig = sig;

        // >>> Entity-Pairings NIE über server+token wegfiltern!
        if (e.EntityId.HasValue)
        {
            var id = e.EntityId.Value;
            if (_entityPairSeen.TryGetValue(id, out var last) &&
                (DateTime.UtcNow - last).TotalSeconds < 5)
            {
                AppendLog($"[pairing] duplicate for entity #{id} ignored (5s).");
                return;
            }
            _entityPairSeen[id] = DateTime.UtcNow;
        }

        _lastPairingPingAt = DateTime.UtcNow;
        AppendLog("Pairing_Paired fired");

        Dispatcher.Invoke(() =>
        {
            var keyHost = (e.Host ?? "").Trim();
            var keyPort = e.Port;
            // PREFER the SteamID from the pairing payload if it exists. 
            // Only fallback to _vm.SteamId64 if the payload is missing it.
            var keySteam = !string.IsNullOrEmpty(e.SteamId64) ? e.SteamId64 : _vm.SteamId64;

            // Save SteamID globally if we just received a new one
            if (!string.IsNullOrEmpty(e.SteamId64) && e.SteamId64 != TrackingService.SteamId64)
            {
                TrackingService.SteamId64 = e.SteamId64;
                _vm.SteamId64 = e.SteamId64;
                AppendLog($"[pairing] Captured SteamID {e.SteamId64} from pairing response.");
                // Persist SteamId into the FCM config file so future launches read it
                TrackingService.PatchFcmConfigSteamId(e.SteamId64);
                HydrateSteamUiFromStorage();
            }

            bool datesChanged = false;
            if (!string.IsNullOrEmpty(e.IssueDate))
            {
                if (long.TryParse(e.IssueDate, out var issueTs))
                    TrackingService.FcmIssuedAt = DateTimeOffset.FromUnixTimeMilliseconds(issueTs > 9999999999 ? issueTs : issueTs * 1000).LocalDateTime;
                else if (DateTime.TryParse(e.IssueDate, out var d1))
                    TrackingService.FcmIssuedAt = d1;
                datesChanged = true;
            }

            if (!string.IsNullOrEmpty(e.ExpiryDate))
            {
                if (long.TryParse(e.ExpiryDate, out var expTs))
                    TrackingService.FcmExpiresAt = DateTimeOffset.FromUnixTimeMilliseconds(expTs > 9999999999 ? expTs : expTs * 1000).LocalDateTime;
                else if (DateTime.TryParse(e.ExpiryDate, out var d2))
                    TrackingService.FcmExpiresAt = d2;
                datesChanged = true;
            }
            
            if (datesChanged) _vm.NotifyFcmChanged();

            var prof = _vm.Servers.FirstOrDefault(s =>
                s.Host.Equals(keyHost, StringComparison.OrdinalIgnoreCase) &&
                s.Port == keyPort &&
                s.SteamId64 == keySteam);

            var serverName = string.IsNullOrWhiteSpace(e.ServerName) ? $"{e.Host}:{e.Port}" : e.ServerName!;

            if (prof is null)
            {
                prof = new ServerProfile
                {
                    Name = serverName,
                    Host = e.Host,
                    Port = e.Port,
                    SteamId64 = keySteam,
                    PlayerToken = e.PlayerToken,
                    UseFacepunchProxy = false,
                    Devices = new ObservableCollection<SmartDevice>()
                };
                _vm.AddServer(prof);
                AppendLog($"Pairing received → {prof.Name} ({prof.Host}:{prof.Port})");
            }
            else
            {
                prof.Name = serverName;
                prof.PlayerToken = e.PlayerToken;
                prof.SteamId64 = keySteam;
                prof.Devices ??= new ObservableCollection<SmartDevice>();
                AppendLog($"Pairing updated → {prof.Name}");
            }

            // >>> Geräte zuverlässig hinzufügen/aktualisieren (Switch + Alarm + StorageMonitor)


            if (e.EntityId.HasValue)
            {
                // ------- NEU: robuste Kind-Erkennung -------
                string? kind = e.EntityType;
                string rawType = (e.EntityType ?? "").Trim();
                string rawName = (e.EntityName ?? "").Trim();

                bool TypeHas(string s) =>
                    rawType.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;

                bool NameHas(string s) =>
                    rawName.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;

                // 1) direkte Typ-Matches
                if (TypeHas("Alarm")) kind = "SmartAlarm";
                else if (TypeHas("Switch")) kind = "SmartSwitch";
                else if (TypeHas("Storage")) kind = "StorageMonitor";

                // 2) Falls Typ leer/„server“/„entity“/unklar → nach Name mappen
                if (string.IsNullOrWhiteSpace(kind) ||
                    string.Equals(rawType, "server", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rawType, "entity", StringComparison.OrdinalIgnoreCase))
                {
                    if (NameHas("alarm"))
                        kind = "SmartAlarm";
                    else if (NameHas("storage") || NameHas("monitor") || NameHas("cupboard") || NameHas("tool cupboard") || NameHas("tc"))
                        kind = "StorageMonitor";
                    else
                        kind = "SmartSwitch"; // Default
                }

                // DEBUG
                AppendLog($"[pair] entityId={e.EntityId.Value} rawType='{(string.IsNullOrWhiteSpace(rawType) ? "(null)" : rawType)}' rawName='{(string.IsNullOrWhiteSpace(rawName) ? "(null)" : rawName)}'");
                AppendLog($"[pair] inferred kind='{kind ?? "(null)"}' for entity #{e.EntityId.Value}");
                // ------- /NEU -------

                // ------- /NEU -------

                var dev = FindDeviceById(prof.Devices, e.EntityId.Value);
                if (dev is null)
                {
                    dev = new SmartDevice
                    {
                        EntityId = e.EntityId.Value,
                        Name = string.IsNullOrWhiteSpace(e.EntityName)
                                   ? (string.IsNullOrWhiteSpace(e.ServerName) ? "Smart Device" : e.ServerName)
                                   : e.EntityName,
                        Kind = kind,
                        IsOn = string.Equals(kind, "StorageMonitor", StringComparison.OrdinalIgnoreCase) ? (bool?)null : false,
                        IsMissing = false,
                    };
                    prof.Devices.Add(dev);
                    AppendLog($"Device added → {dev.Display}");
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(e.EntityName)) dev.Name = e.EntityName;

                    if (!string.IsNullOrWhiteSpace(kind))
                    {
                        if (!string.Equals(dev.Kind, "SmartAlarm", StringComparison.OrdinalIgnoreCase))
                            dev.Kind = kind;
                        if (string.Equals(dev.Kind, "StorageMonitor", StringComparison.OrdinalIgnoreCase))
                            dev.IsOn = null;
                    }

                    dev.IsMissing = false;
                    AppendLog($"Device updated → {dev.Display}");
                }

                /* >>>>>>> HIER EINSETZEN (direkt nach dem add/update-Block) <<<<<<< */
                // >>> Cache sofort ins UI + Einmal-Expand + Sub/Poke
                if (string.Equals(dev.Kind, "StorageMonitor", StringComparison.OrdinalIgnoreCase))
                {
                    // 1) Cache → UI (falls vorhanden), sonst Hülle
                    if (_rust is RustPlusClientReal rpc && rpc.TryGetCachedStorage(dev.EntityId, out var cached))
                    {
                        dev.IsMissing = false;
                        Dispatcher.Invoke(() =>
                        {
                            var uiSnap = new StorageSnapshot
                            {
                                UpkeepSeconds = cached.UpkeepSeconds,
                                IsToolCupboard = cached.IsToolCupboard
                            };
                            foreach (var it in cached.Items) uiSnap.Items.Add(it);
                            dev.Storage = uiSnap;
                        });
                       // AppendLog($"[stor/refresh] (cache on pair) #{dev.EntityId} items={cached.Items?.Count ?? 0} upkeep={(cached.UpkeepSeconds?.ToString() ?? "null")}");
                    }
                    else
                    {
                        dev.Storage ??= new StorageSnapshot();
                       // AppendLog($"[stor/refresh] (no cache) #{dev.EntityId} → awaiting event");
                    }

                    // 2) Einmal automatisch aufklappen + abonnieren
                    if (!dev.IsExpanded)
                    {
                        dev.IsExpanded = true;
                        _ = Dispatcher.InvokeAsync(async () =>
                        {
                            try
                            {
                                if (_rust is RustPlusClientReal r2)
                                {
                                    await r2.SubscribeEntityAsync(dev.EntityId);
                                    await r2.PokeEntityAsync(dev.EntityId);
                                  //  AppendLog($"[stor/sub+poke] #{dev.EntityId} queued");
                                }
                            }
                            catch (Exception subEx)
                            {
                                AppendLog($"[stor/sub+poke] #{dev.EntityId} on pair: {subEx.Message}");
                            }
                        });
                    }
                }
                /* >>>>>>> /ENDE Einfügeblock <<<<<<< */

                if (_vm.Selected != prof)
                    _vm.Selected = prof;

                _vm.Save();
            }


        });
    }


    /// <summary>Starts the FCM pairing listener silently — no busy overlay, no blocking.</summary>
    private void StartPairingSilent(bool autoStart = false)
    {
        if (_listenerStarting || _pairing.IsRunning) return;

        if (autoStart)
        {
            if (!TrackingService.IsFcmConfigured())
            {
                AppendLog("[pairing] No FCM config saved. Auto-start disabled.");
                return;
            }
            if (TrackingService.FcmExpiresAt.HasValue && TrackingService.FcmExpiresAt.Value < DateTime.Now)
            {
                AppendLog("[pairing] FCM config has expired. Manual start (Listen) required to re-register.");
                return;
            }
        }

        _listenerStarting = true;
        _vm.IsPairingBusy = true; // Tell UI we are trying to start
        TxtPairingState.Text = "Pairing: starting…";
        _ = Task.Run(async () =>
        {
            try { await _pairing.StartAsync(); }
            catch (Exception ex) 
            { 
                AppendLog("[pairing] silent start error: " + ex.Message); 
                Dispatcher.Invoke(() => { _vm.IsPairingBusy = false; TxtPairingState.Text = Properties.Resources.PairingError; });
            }
            finally { Dispatcher.Invoke(() => { _listenerStarting = false; }); }
        });
    }

    private async Task StartPairingListenerUiAsync()
    {
        // Delegate to silent start — no more busy overlay
        StartPairingSilent(false);
        await Task.CompletedTask;
    }

    // TRY PAIRING WITH EDGE METHOD (Right Click on Listener)

    private async void BtnListenWithEdge_Click(object sender, RoutedEventArgs e)
    {
        if (_listenerStarting || _pairing.IsRunning) { AppendLog("Listener läuft bereits."); return; }
        await StartPairingListenerUiWithEdgeAsync();
    }

    private async Task StartPairingListenerUiWithEdgeAsync()
    {
        if (_pairing.IsRunning)
        {
            _vm.IsPairingBusy = false; _vm.BusyText = "";
            TxtPairingState.Text = Properties.Resources.PairingListening;
            AppendLog("Listener already running.");
            return;
        }
        if (_listenerStarting) return;

        try
        {
            _listenerStarting = true;
            _vm.IsPairingBusy = true;
            _vm.BusyText = "Starting Pairing-Listener (Edge) …";

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler onListen = (_, __) => tcs.TrySetResult(true);
            EventHandler<string> onFail = (_, __) => tcs.TrySetResult(false);

            _pairing.Listening += onListen;
            _pairing.Failed += onFail;

            await _pairing.StartAsyncUsingEdge();   // <— NEU: eigene Methode (siehe unten)

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(8000));
            bool ok = (completed == tcs.Task) && tcs.Task.Result;

            _pairing.Listening -= onListen;
            _pairing.Failed -= onFail;

            _vm.IsPairingBusy = false; _vm.BusyText = "";
            if (ok) { TxtPairingState.Text = "Pairing: listening."; UpdatePairingGuideSnackbar(); }
        }
        finally { _listenerStarting = false; }
    }


    private void Real_Status(object? s, string st)
    {
        Dispatcher.Invoke(() =>
        {
            if (st == "starting") _vm.BusyText = Properties.Resources.StartingPairingListener;
            else if (st == "listening") { TxtPairingState.Text = Properties.Resources.PairingListening; UpdatePairingGuideSnackbar(); }
            else if (st == "error") TxtPairingState.Text = Properties.Resources.PairingError;
        });
    }
    private void Real_Listening(object? s, EventArgs e)
    {
        Dispatcher.Invoke(() => { TxtPairingState.Text = "Pairing: listening."; UpdatePairingGuideSnackbar(); });
    }
    private void Real_Failed(object? s, string msg)
    {
        Dispatcher.Invoke(() =>
        {
            TxtPairingState.Text = Properties.Resources.PairingError;
            AppendLog("[listener] " + msg);
        });
    }

    private void OnListening(object? s, EventArgs e)
    {
        _vm.IsBusy = false;
        _vm.BusyText = "";
        TxtPairingState.Text = Properties.Resources.PairingListening;
        UpdatePairingGuideSnackbar();
    }

    private void OnFailed(object? s, string msg)
    {
        _vm.IsBusy = false;
        _vm.BusyText = "";
        TxtPairingState.Text = Properties.Resources.PairingError;
        AppendLog("[listener] " + msg);
    }

    private void OnStatus(object? s, string st)
    {
        if (st == "starting") _vm.BusyText = "Starting Pairing-Listener …";
    }

    private ServerProfile? _serverToDelete;



    private void Server_Delete_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ServerProfile prof) return;
        
        _serverToDelete = prof;
        TxtDeleteConfirmation.Text = $"Are you sure you want to delete Server \"{prof.Name}\"? This action cannot be undone.";
        DeleteConfirmationOverlay.Visibility = Visibility.Visible;
    }

    private void BtnCancelDelete_Click(object sender, RoutedEventArgs e)
    {
        DeleteConfirmationOverlay.Visibility = Visibility.Collapsed;
        _serverToDelete = null;
    }

    private void BtnConfirmDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_serverToDelete != null)
        {
            var prof = _serverToDelete;
            _vm.Servers.Remove(prof);
            _vm.Save();
            AppendLog($"Server deleted: {prof.Name}");
        }

        DeleteConfirmationOverlay.Visibility = Visibility.Collapsed;
        _serverToDelete = null;
    }


    private async void BtnListenPairing_Click(object sender, RoutedEventArgs e)
    {
        if (_listenerStarting || _pairing.IsRunning) { AppendLog("Listener already running."); return; }
        await StartPairingListenerUiAsync();
    }

    private void BtnOverlayRestore_Click(object sender, RoutedEventArgs e)
    {
        var ask = MessageBox.Show(
            Properties.Resources.RestoreConfirmMessage,
            Properties.Resources.RestoreConfirmTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (ask != MessageBoxResult.Yes) return;

        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "ZIP Archives (*.zip)|*.zip",
            Title = Properties.Resources.RestoreApplicationDataTitle
        };

        if (ofd.ShowDialog() == true)
        {
            string password = "";
            if (RustPlusDesk.Services.Data.BackupDataModule.IsBackupEncrypted(ofd.FileName))
            {
                var dialog = new BackupPasswordDialog { Owner = this };
                dialog.SetMode(true); // Decryption mode

                if (dialog.ShowDialog() == true)
                {
                    password = dialog.Password;
                }
                else
                {
                    // User canceled decryption prompt, abort restore
                    return;
                }
            }

            try
            {
                RustPlusDesk.Services.Data.BackupDataModule.RestoreBackup(ofd.FileName, password);
                ReloadApplicationData();
                MessageBox.Show(Properties.Resources.RestoreSuccessMessage, Properties.Resources.RestoreSuccessTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                AppendLog(Properties.Resources.RestorePasswordErrorLog);
                MessageBox.Show(Properties.Resources.RestorePasswordErrorMessage, Properties.Resources.RestoreFailedTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(Properties.Resources.RestoreErrorLog, ex.Message));
                MessageBox.Show(string.Format(Properties.Resources.RestoreErrorMessage, ex.Message), Properties.Resources.RestoreFailedTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void BtnStopPairing_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_pairing.IsRunning)
            {
                AppendLog("Stopping pairing listener...");
                await Task.Run(async () => await _pairing.StopAsync());
                _vm.IsPairingBusy = false;
            }
        }
        catch (Exception ex)
        {
            AppendLog("Error on stop: " + ex.Message);
            _vm.IsPairingBusy = false;
        }
    }
    public void AppendLog(string line)
    {
        Dispatcher.Invoke(() =>
        {
            TxtLog.AppendText(line + Environment.NewLine);
            TxtLog.ScrollToEnd();
        });
    }

    static readonly JsonSerializerOptions JsonOpt = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };
    // PLAYER DEATH MARKERS AVATAR IMAGE PLAYER DEATH

    private bool _showProfileMarkers = true;
    private bool _showDeathMarkers = false;

    // death pins per player
    private readonly Dictionary<ulong, FrameworkElement> _deathPins = new();



    private void Monuments_Checked(object sender, RoutedEventArgs e)
    {
        ToggleMonuments(true);
        UpdateSelectAllState();
    }

    private void Monuments_Unchecked(object sender, RoutedEventArgs e)
    {
        ToggleMonuments(false);
        UpdateSelectAllState();
    }
    private void ToggleMonuments(bool on)
    {
        _showMonuments = on;
        foreach (var fe in _monEls.Values)
            fe.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateSelectAllState()
    {
        // Placeholder to fix build errors. 
        // Logic to update a 'Select All' checkbox state based on individual filters could go here.
    }



    private void HydrateSteamUiFromStorage()
    {
        // 1. First try global settings
        if (string.IsNullOrWhiteSpace(_vm.SteamId64))
        {
            _vm.SteamId64 = TrackingService.SteamId64;
        }

        // 2. Fallback: derive from existing servers
        if (string.IsNullOrWhiteSpace(_vm.SteamId64))
        {
            var sid = _vm.Servers
                         .Select(s => s.SteamId64)
                         .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (!string.IsNullOrWhiteSpace(sid))
            {
                _vm.SteamId64 = sid;
                TrackingService.SteamId64 = sid; // Backfill if found
            }
        }

        // Update UI Elements
        var sidText = string.IsNullOrWhiteSpace(_vm.SteamId64) ? "Not Logged In" : _vm.SteamId64;
        TxtSteamId.Text = sidText;
        TxtSteamName.Text = string.IsNullOrWhiteSpace(_vm.SteamId64) ? "Steam Account" : "Logged In";
        ImgSteam.ToolTip = TxtSteamName.Text;
        RefreshStreamerModeUI();
        


        // Avatar versuchen zu laden (nur wenn wir eine ID haben)
        _ = TryLoadSteamAvatarAsync(_vm.SteamId64);

        // Notify VM of FCM data (for expiry badge)
        _vm.NotifyFcmChanged();
    }

    public void RefreshStreamerModeUI()
    {
        if (TxtSteamId == null || TxtSteamName == null || _vm == null) return;
        
        var sid = _vm.SteamId64;
        if (string.IsNullOrWhiteSpace(sid))
        {
            TxtSteamId.Text = "(nicht angemeldet)";
            TxtSteamName.Text = "Steam Account";
            return;
        }

        TxtSteamId.Text = _abbreviateNames && sid.Length > 3 ? sid.Substring(0, 3) + "..." : sid;
        
        var originalName = (ImgSteam.ToolTip as string) ?? "Logged In";
        TxtSteamName.Text = _abbreviateNames ? "STREAMER MODE" : originalName;

        if (_vm.IsFollowing && _vm.FollowingSteamId != _mySteamId)
        {
            var fMember = TeamMembers.FirstOrDefault(t => t.SteamId == _vm.FollowingSteamId);
            if (fMember != null)
            {
                _vm.FollowingPlayerName = fMember.DisplayName;
            }
        }
    }

    private void BtnToggleServerArea_Click(object sender, RoutedEventArgs e)
    {
        if (PanelServerArea == null || IconToggleServerArea == null) return;

        if (BtnToggleServerArea.IsChecked == true)
        {
            PanelServerArea.Visibility = Visibility.Collapsed;
            IconToggleServerArea.Symbol = Wpf.Ui.Controls.SymbolRegular.ChevronDown20;
        }
        else
        {
            PanelServerArea.Visibility = Visibility.Visible;
            IconToggleServerArea.Symbol = Wpf.Ui.Controls.SymbolRegular.ChevronUp20;
        }
    }

    private async Task TryLoadSteamAvatarAsync(string? steamId64)
    {
        if (string.IsNullOrWhiteSpace(steamId64))
        {
            _vm.MyAvatar = null;
            return;
        }

        try
        {
            using var http = new HttpClient();
            var xml = await http.GetStringAsync($"https://steamcommunity.com/profiles/{steamId64}?xml=1");

            // sehr einfache Extraktion
            var nameMatch = Regex.Match(xml, "<steamID><!\\[CDATA\\[(.*?)\\]\\]>");
            var avatarMatch = Regex.Match(xml, "<avatarFull><!\\[CDATA\\[(.*?)\\]\\]>");

            if (avatarMatch.Success)
            {
                var uri = new Uri(avatarMatch.Groups[1].Value);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = uri;
                bmp.EndInit();
                bmp.Freeze();

                _vm.MyAvatar = bmp;
            }
            if (nameMatch.Success)
            {
                var originalName = nameMatch.Groups[1].Value;
                ImgSteam.ToolTip = originalName;
                Dispatcher.Invoke(() => RefreshStreamerModeUI());
            }
        }
        catch
        {
            // Avatar optional – bei Fehlern still
            _vm.MyAvatar = null;
        }
    }

    private static string FormatItemName(int id) => /* deine vorhandene Map-Funktion */ ResolveItemName(id, null);
    public static System.Windows.Media.ImageSource? ResolveItemIcon(int itemId, string? shortName, int decodePx = 32)
    {
        EnsureNewItemDbLoaded();

        // Standard-Shortname besorgen, falls fehlt
        if (string.IsNullOrWhiteSpace(shortName) && itemId != 0 && sItemsById.TryGetValue(itemId, out var ii0))
            shortName = ii0.ShortName;

        // Fallback-URL (rusthelp)
        string? rusthelpUrl = null;
        if (itemId != 0 && sItemsById.TryGetValue(itemId, out var ii1)) rusthelpUrl = ii1.IconUrl;
        if (rusthelpUrl == null && !string.IsNullOrWhiteSpace(shortName) && sItemsByShort.TryGetValue(shortName!, out var ii2))
            rusthelpUrl = ii2.IconUrl;

        // Primär-URL (rustclash)
        string? rustclashUrl = !string.IsNullOrWhiteSpace(shortName)
            ? $"https://wiki.rustclash.com/img/items40/{shortName}.png"
            : null;

        // 1) Versuche RustClash (Primär)
        if (rustclashUrl != null)
        {
            if (sIconCache.TryGetValue(rustclashUrl, out var ready)) return ready;
            var path = GetIconCachePath(rustclashUrl);
            if (System.IO.File.Exists(path))
            {
                var img = TryLoadBitmapFromFile(path, decodePx);
                if (img != null) { sIconCache[rustclashUrl] = img; return img; }
            }
        }

        // 2) Versuche RustHelp (Fallback/DB)
        if (rusthelpUrl != null)
        {
            if (sIconCache.TryGetValue(rusthelpUrl, out var ready)) return ready;
            var path = GetIconCachePath(rusthelpUrl);
            if (System.IO.File.Exists(path))
            {
                var img = TryLoadBitmapFromFile(path, decodePx);
                if (img != null) { sIconCache[rusthelpUrl] = img; return img; }
            }
        }

        // 3) Nichts da -> Download (RustClash bevorzugt, RustHelp als Fallback)
        if (rustclashUrl != null)
            QueueIconDownload(rustclashUrl, GetIconCachePath(rustclashUrl), rusthelpUrl);
        else if (rusthelpUrl != null)
            QueueIconDownload(rusthelpUrl, GetIconCachePath(rusthelpUrl), null);

        return null;
    }

    private static string GetIconCachePath(string url)
    {
        Directory.CreateDirectory(sIconCacheDir);
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        return System.IO.Path.Combine(sIconCacheDir, hash + ".png");
    }

    private static ImageSource? TryLoadBitmapFromFile(string path, int decodePx)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(path);
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.DecodePixelWidth = decodePx;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    private static void QueueIconDownload(string url, string targetPath, string? fallbackUrl)
    {
        lock (sPendingDownloads)
        {
            if (!sPendingDownloads.Add(url)) return;
        }

        UpdateIconProgress(-1); // Total erhöhen

        _ = Task.Run(async () =>
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetPath)!);
                using var http = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) };

                // 1) Primär versuchen
                var resp = await http.GetAsync(url).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    var data = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    await System.IO.File.WriteAllBytesAsync(targetPath, data).ConfigureAwait(false);
                }
                else if (fallbackUrl != null)
                {
                    // 2) Fallback versuchen
                    var respF = await http.GetAsync(fallbackUrl).ConfigureAwait(false);
                    if (respF.IsSuccessStatusCode)
                    {
                        var data = await respF.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        await System.IO.File.WriteAllBytesAsync(targetPath, data).ConfigureAwait(false);
                    }
                }
            }
            catch { }
            finally
            {
                UpdateIconProgress(1); // Fertig
            }
        });
    }

    private static void UpdateIconProgress(int deltaFinish)
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            if (Application.Current?.MainWindow is MainWindow mw)
            {
                if (deltaFinish > 0) mw._vm.IconsDownloaded++;
                else mw._vm.IconsTotal++;
            }
        });
    }

    private static Image MakeIcon(string packUri, double size = 32)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.UriSource = new Uri(packUri, UriKind.Absolute);
        bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.EndInit();

        var img = new Image
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Source = bi,
            Tag = packUri
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        return img;
    }
    private static string Beautify(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;

        string lower = s.ToLowerInvariant();
        if (lower.Contains("underwater") || lower.Contains("under water") || lower.Contains("underwaterlab") || lower.Contains("moonpool"))
        {
            return "Underwater Labs";
        }

        if (lower.Contains("harbor_2") || lower.Contains("harbor 2")) return "Harbor";
        if (lower.Contains("harbor")) return "Harbor 2";

        s = s.Replace('\\', '/');
        var last = s.LastIndexOf('/');
        var token = last >= 0 ? s[(last + 1)..] : s;
        return token.Replace(".prefab", "").Replace('_', ' ').Replace("display name", "", StringComparison.OrdinalIgnoreCase).Trim();
    }


    // From worldSize and image size compute the centered playable square in IMAGE PIXELS.
    // The "2000" is the UI canvas padding used by Rust's own Map code (1000 per side).
    private static Rect ComputeWorldRectFromWorldSize(double imgW, double imgH, double worldSize, double padWorld = 2000)
    {
        if (worldSize <= 0) return new Rect(0, 0, imgW, imgH); // fallback

        double minSidePx = Math.Min(imgW, imgH);
        double scale = (double)worldSize / (worldSize + padWorld); // fraction of the image occupied by the world
        double sidePx = minSidePx * scale;

        double ox = (imgW - sidePx) / 2.0; // centered
        double oy = (imgH - sidePx) / 2.0;

        return new Rect(ox, oy, sidePx, sidePx);
    }
    private static string Shorten(string? s, int max = 10)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim();
        if (s.Length <= max) return s;
        return s.Substring(0, Math.Max(1, max - 1)) + "…";
    }
    private int _shopAutoSeq = 1; // Fallback-Sequenz, wenn ID fehlt

    // stabiler Fallback-Key-Hasher (aus X,Y,Label)
    private static uint ShopFallbackKey(double x, double y, string? label)
    {
        unchecked
        {
            // simpler FNV-1a Hash
            uint h = 2166136261;
            void mix(ulong v)
            {
                for (int i = 0; i < 8; i++)
                {
                    h ^= (byte)(v & 0xFF);
                    h *= 16777619;
                    v >>= 8;
                }
            }
            mix(BitConverter.DoubleToUInt64Bits(x));
            mix(BitConverter.DoubleToUInt64Bits(y));
            if (!string.IsNullOrEmpty(label))
            {
                foreach (char c in label)
                {
                    h ^= (byte)c;
                    h *= 16777619;
                }
            }
            // 0 vermeiden
            if (h == 0) h = 1;
            return h;
        }
    }
    private void BtnDonate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.patreon.com/cw/Pronwan",
                UseShellExecute = true   // öffnet im Standard-Browser
            });
        }
        catch (Exception ex)
        {
            AppendLog("Couldn't open Donate Link: " + ex.Message);
        }
    }
    private bool _announceSpawns = false;

    private void ChatAnnounce_Toggle(object sender, RoutedEventArgs e)
    {
        TrackingService.AnnounceSpawnsMaster = ChatAnnounce.IsChecked ?? false;
        _announceSpawns = TrackingService.AnnounceSpawnsMaster;

        SyncAlertMenuItems();
        UpdateShopSearchConfig();
    }

    private void SelectAllAlerts_Click(object sender, RoutedEventArgs e)
    {
        SetAllAlerts(true);
        TrackingService.AnnounceSpawnsMaster = true;
        UpdateMasterToggleState();
        SyncAlertMenuItems();
        UpdateShopSearchConfig();
    }

    private void DeselectAllAlerts_Click(object sender, RoutedEventArgs e)
    {
        SetAllAlerts(false);
        UpdateMasterToggleState();
        SyncAlertMenuItems();
        UpdateShopSearchConfig();
    }

    private void SetAllAlerts(bool val)
    {
        TrackingService.AnnounceCargo = val;
        TrackingService.AnnounceCargoDocking = val;
        TrackingService.AnnounceCargoEgress = val;
        TrackingService.AnnounceCargoArrival = val;
        TrackingService.AnnounceHeli = val;
        TrackingService.AnnounceChinook = val;
        TrackingService.AnnounceVendor = val;
        TrackingService.AnnounceOilRig = val;
        TrackingService.AnnounceDeepSea = val;
        TrackingService.AnnouncePlayerOnline = val;
        TrackingService.AnnouncePlayerOffline = val;
        TrackingService.AnnouncePlayerDeathSelf = val;
        TrackingService.AnnouncePlayerDeathTeam = val;
        TrackingService.AnnouncePlayerRespawnSelf = val;
        TrackingService.AnnouncePlayerRespawnTeam = val;
        TrackingService.AnnounceTracking = val;
        TrackingService.AnnounceNewShops = val;
        TrackingService.AnnounceSuspiciousShops = val;
        TrackingService.AnnounceSmartAlerts = val;
        TrackingService.AnnounceTradeAlerts = val;
    }

    private bool CheckIfAllOff()
    {
        return !TrackingService.AnnounceCargo && !TrackingService.AnnounceCargoDocking &&
               !TrackingService.AnnounceCargoEgress && !TrackingService.AnnounceCargoArrival &&
               !TrackingService.AnnounceHeli && !TrackingService.AnnounceChinook &&
               !TrackingService.AnnounceVendor && !TrackingService.AnnounceOilRig && !TrackingService.AnnounceDeepSea &&
               !TrackingService.AnnouncePlayerOnline && !TrackingService.AnnouncePlayerOffline &&
               !TrackingService.AnnouncePlayerDeathSelf && !TrackingService.AnnouncePlayerDeathTeam &&
               !TrackingService.AnnouncePlayerRespawnSelf && !TrackingService.AnnouncePlayerRespawnTeam &&
               !TrackingService.AnnounceTracking &&
               !TrackingService.AnnounceNewShops && !TrackingService.AnnounceSuspiciousShops &&
               !TrackingService.AnnounceSmartAlerts && !TrackingService.AnnounceTradeAlerts;
    }

    private void UpdateMasterToggleState()
    {
        _announceSpawns = TrackingService.AnnounceSpawnsMaster;
        ChatAnnounce.IsChecked = _announceSpawns;
    }

    private void Alert_MenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string tag)
        {
            bool val = mi.IsChecked;
            switch (tag)
            {
                case "Cargo": TrackingService.AnnounceCargo = val; break;
                case "CargoSpawn": TrackingService.AnnounceCargo = val; break;
                case "CargoDock": TrackingService.AnnounceCargoDocking = val; break;
                case "CargoEgress": TrackingService.AnnounceCargoEgress = val; break;
                case "CargoArrival": TrackingService.AnnounceCargoArrival = val; break;
                case "Heli": TrackingService.AnnounceHeli = val; break;
                case "Chinook": TrackingService.AnnounceChinook = val; break;
                case "Vendor": TrackingService.AnnounceVendor = val; break;
                case "OilRig": TrackingService.AnnounceOilRig = val; break;
                case "DeepSea": TrackingService.AnnounceDeepSea = val; break;
                case "SmartAlerts": TrackingService.AnnounceSmartAlerts = val; break;
                case "PlayerOnline": TrackingService.AnnouncePlayerOnline = val; break;
                case "PlayerOffline": TrackingService.AnnouncePlayerOffline = val; break;
                case "AnnounceTracking": TrackingService.AnnounceTracking = val; break;
                case "PlayerDeathSelf": TrackingService.AnnouncePlayerDeathSelf = val; break;
                case "PlayerDeathTeam": TrackingService.AnnouncePlayerDeathTeam = val; break;
                case "PlayerRespawnSelf": TrackingService.AnnouncePlayerRespawnSelf = val; break;
                case "PlayerRespawnTeam": TrackingService.AnnouncePlayerRespawnTeam = val; break;
                case "NewShops": TrackingService.AnnounceNewShops = val; break;
                case "SuspiciousShops": TrackingService.AnnounceSuspiciousShops = val; break;
            }

            UpdateMasterToggleState();
            UpdateShopSearchConfig();
            SyncAlertMenuItems();
        }
    }

    private void SyncAlertMenuItems()
    {
        bool masterOn = TrackingService.AnnounceSpawnsMaster;
        PopulateTradeAlertsSubMenu(masterOn);

        SyncContextMenu(ChatAnnounce.ContextMenu, masterOn);
        if (ChatAlertsConfigureButton.Flyout is ContextMenu cm)
        {
            SyncContextMenu(cm, masterOn);
        }
    }

    private void SyncContextMenu(ContextMenu menu, bool masterOn)
    {
        if (menu == null) return;

        string host = _rust?.Host ?? "unknown";
        bool hasTravelData = TrackingService.HasAnyCargoTrigger(host);

        foreach (var item in menu.Items)
        {
            if (item is MenuItem mi)
            {
                SyncMenuItemRecursive(mi, masterOn, hasTravelData);
            }
        }
    }

    private void SyncMenuItemRecursive(MenuItem mi, bool masterOn, bool hasTravelData)
    {
        if (mi.Tag is string tag)
        {
            bool isSelected = false;
            switch (tag)
            {
                case "Cargo": 
                case "Partial":
                    bool cs = TrackingService.AnnounceCargo;
                    bool cd = TrackingService.AnnounceCargoDocking;
                    bool ce = TrackingService.AnnounceCargoEgress;
                    bool ca = TrackingService.AnnounceCargoArrival;
                    if (cs && cd && ce && ca) { isSelected = true; mi.Tag = "Cargo"; }
                    else if (cs || cd || ce || ca) { isSelected = true; mi.Tag = "Partial"; }
                    else { isSelected = false; mi.Tag = "Cargo"; }
                    break;
                case "CargoSpawn": isSelected = TrackingService.AnnounceCargo; break;
                case "CargoDock": isSelected = TrackingService.AnnounceCargoDocking; break;
                case "CargoEgress": isSelected = TrackingService.AnnounceCargoEgress; break;
                case "CargoArrival": 
                    isSelected = TrackingService.AnnounceCargoArrival; 
                    mi.Header = hasTravelData ? "Arrival Warning (5m before Dock)" : "Arrival Warning (Unlearned)";
                    mi.IsEnabled = masterOn && hasTravelData; 
                    break;
                case "Heli": isSelected = TrackingService.AnnounceHeli; break;
                case "Chinook": isSelected = TrackingService.AnnounceChinook; break;
                case "Vendor": isSelected = TrackingService.AnnounceVendor; break;
                case "OilRig": isSelected = TrackingService.AnnounceOilRig; break;
                case "DeepSea": isSelected = TrackingService.AnnounceDeepSea; break;
                case "SmartAlerts": isSelected = TrackingService.AnnounceSmartAlerts; break;
                case "PlayerOnline": isSelected = TrackingService.AnnouncePlayerOnline; break;
                case "PlayerOffline": isSelected = TrackingService.AnnouncePlayerOffline; break;
                case "AnnounceTracking": isSelected = TrackingService.AnnounceTracking; break;
                case "PlayerDeathSelf": isSelected = TrackingService.AnnouncePlayerDeathSelf; break;
                case "PlayerDeathTeam": isSelected = TrackingService.AnnouncePlayerDeathTeam; break;
                case "PlayerRespawnSelf": isSelected = TrackingService.AnnouncePlayerRespawnSelf; break;
                case "PlayerRespawnTeam": isSelected = TrackingService.AnnouncePlayerRespawnTeam; break;
                case "NewShops": isSelected = TrackingService.AnnounceNewShops; break;
                case "SuspiciousShops": isSelected = TrackingService.AnnounceSuspiciousShops; break;
            }

            if (tag != "CargoArrival") mi.IsEnabled = masterOn;
            mi.IsChecked = masterOn && isSelected;
        }

        if (mi.HasItems)
        {
            foreach (var sub in mi.Items)
            {
                if (sub is MenuItem smi) SyncMenuItemRecursive(smi, masterOn, hasTravelData);
            }
        }
    }

    private void PopulateTradeAlertsSubMenu(bool masterOn)
    {
        if (TradeAlertsMenuItem == null) return;

        TradeAlertsMenuItem.Items.Clear();
        // Trade Alerts menu is always enabled if rules exist, regardless of master toggle
        TradeAlertsMenuItem.IsEnabled = _alertRules.Count > 0;

        if (_alertRules.Count == 0)
        {
            TradeAlertsMenuItem.Header = $"{Properties.Resources.TradeAlerts} (0)";
            return;
        }

        TradeAlertsMenuItem.SetResourceReference(MenuItem.HeaderProperty, "TradeAlerts");
        foreach (var rule in _alertRules)
        {
            var mi = new MenuItem
            {
                Header = rule.QueryText,
                IsCheckable = true,
                IsChecked = rule.NotifyChat,
                IsEnabled = true, // Always allow manual control
                StaysOpenOnClick = true,
                Style = (Style)FindResource("DarkMenuItem"),
                Tag = rule
            };
            mi.Click += TradeAlertSubItem_Click;
            TradeAlertsMenuItem.Items.Add(mi);
        }
    }

    private void TradeAlertSubItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is ShopAlertRule rule)
        {
            rule.NotifyChat = mi.IsChecked;
            SavePersistentAlerts();
            
            TrackingService.AnnounceSpawnsMaster = true;
            UpdateMasterToggleState();
            UpdateShopSearchConfig();
            _ = PushAlertsToWebViewAsync();
        }
    }

    private async void UpdateShopSearchConfig()
    {
        if (EmbeddedShopSearch?.CoreWebView2 != null)
        {
            try
            {
                var config = new
                {
                    notifyNew = TrackingService.AnnounceNewShops,
                    notifySuspicious = TrackingService.AnnounceSuspiciousShops,
                    notifyTradeAlerts = _alertRules.Any(r => r.NotifyChat)
                };
                string json = JsonSerializer.Serialize(config);
                await EmbeddedShopSearch.CoreWebView2.ExecuteScriptAsync($"if(window.updateConfig) window.updateConfig({json});");
            }
            catch { }
        }
    }



    private static string EventKindText(int type) => type switch
    {
        5 => Properties.Resources.EventCargoShip,
        6 => Properties.Resources.EventTravellingVendor,
        4 => Properties.Resources.EventCH47,
        8 => Properties.Resources.EventPatrolHelicopter,
        9 => Properties.Resources.EventOilrigCrate,
        150 => Properties.Resources.EventOilrigCrate,
        2 => Properties.Resources.EventExplosion,
        7 => Properties.Resources.EventBuildingBlocked,
        _ => Properties.Resources.EventGeneric
    };

    private List<RustPlusClientReal.ShopMarker> _lastShops = new(); // füllen wir beim Polling

    // PATH FINDER WINDOW LOGIK

    private FrameworkElement BuildSearchResultCard(
    RustPlusClientReal.ShopMarker shop,
    IEnumerable<RustPlusClientReal.ShopOrder> offers)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 0)
        };

        var content = new StackPanel();

        // Header: Name + Grid
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        var title = string.IsNullOrWhiteSpace(shop.Label) ? "Shop" : shop.Label;
        header.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock
        {
            Text = $"  [{GetGridLabel(shop)}]",
            Opacity = 0.7,
            Margin = new Thickness(6, 0, 0, 0)
        });
        content.Children.Add(header);

        // Angebotszeilen mit Icons
        int shown = 0;
        foreach (var o in offers)
        {
            content.Children.Add(BuildOfferRowUI(o));
            if (++shown >= 10)
            {
                content.Children.Add(new TextBlock { Text = "…", Opacity = 0.7, Margin = new Thickness(0, 2, 0, 0) });
                break;
            }
        }

        border.Child = content;

        // Optional: Klick auf Karte → Map auf Shop zentrieren
        border.Cursor = Cursors.Hand;
        border.MouseLeftButtonUp += (_, __) =>
        {
            CenterMapOnWorld(shop.X, shop.Y);   // ← hier wird zentriert
                                                // __?.Handled = true;
        };

        return border;
    }

    // SHOP ANALYTICS AND ALARM MECHANICS

   

    private class ShopAlertRule
    {
        public Guid Id { get; } = Guid.NewGuid();

        public string QueryText { get; set; } = "";
        public bool MatchSellSide { get; set; } = true;
        public bool MatchBuySide { get; set; } = false;

        public bool NotifyChat { get; set; } = true;
        public bool NotifySound { get; set; } = true;

        // vom User “gespeichert”? Dann über Neustart hinweg laden
        public bool IsSaved { get; set; } = false;

        // Baseline der schon bekannten Orders beim Anlegen
        public List<AlertSeenOrder> Baseline { get; } = new();

        // NEU: Kennzeichnet, ob der erste Poll nach Erstellung/Laden durch ist.
        // Falls false, unterdrücken wir Alerts für existierende Shops.
        public bool InitializationComplete { get; set; } = false;

        // Anti-Spam pro Order-Key
        public Dictionary<string, DateTime> LastAnnouncements { get; } = new();
    }
    // Liste aller aktiven Alarmregeln
    private readonly List<ShopAlertRule> _alertRules = new();

    // ====== SHOP SEARCH WINDOW UI-Elemente ======
    // Erweiterungen, die wir neu brauchen:

    private DateTime _initialShopSnapshotTime = DateTime.UtcNow; // set beim allerersten erfolgreichen Poll

    private void AddAlertFromCurrentSearch()
    {
        string q = _searchTb?.Text?.Trim() ?? "";
        bool wantSell = _chkSell?.IsChecked != false;
        bool wantBuy = _chkBuy?.IsChecked != false;

        if (string.IsNullOrWhiteSpace(q))
            return;

        var rule = new ShopAlertRule
        {
            QueryText = q,
            MatchSellSide = wantSell,
            MatchBuySide = wantBuy,
            NotifyChat = true,
            NotifySound = true
        };

        // Baseline aufnehmen: alles, was es JETZT schon gibt, gilt als "bekannt"
        foreach (var shop in _lastShops)
        {
            if (shop.Orders == null) continue;
            foreach (var o in shop.Orders)
            {
                bool matchesSide =
                    (rule.MatchSellSide && MatchOrderLeft(o, rule.QueryText)) ||
                    (rule.MatchBuySide && MatchOrderRight(o, rule.QueryText));

                if (!matchesSide) continue;
               
                rule.Baseline.Add(new AlertSeenOrder
                {
                    ShopId = shop.Id,
                    ItemShort = o.ItemShortName,
                    CurrencyShort = o.CurrencyShortName,
                    Stock = o.Stock,
                    Quantity = o.Quantity,
                    CurrencyAmount = o.CurrencyAmount
                });
            }
        }

        _alertRules.Add(rule);

        RefreshAlertListUI();
    }

    // Zeichnet die Alert-Liste (_alertList) neu
    private void RefreshAlertListUI()
    {
        // "pill" style (runde kleine Buttons wie bei dir in der UI Leiste)
        var pillButtonStyle = new Style(typeof(Button));
        pillButtonStyle.Setters.Add(new Setter(Control.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(40, 44, 48))));
        pillButtonStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        pillButtonStyle.Setters.Add(new Setter(Control.BorderBrushProperty,
            new SolidColorBrush(Color.FromArgb(80, 255, 255, 255))));
        pillButtonStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        pillButtonStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 2, 6, 2)));
        pillButtonStyle.Setters.Add(new Setter(Control.FontSizeProperty, 11.0));
        pillButtonStyle.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
        // CornerRadius geht nur über ControlTemplate hacky;
        // Quick&dirty ohne Template: wir lassen’s rechteckig mit 4er Radius über Border below:

        // Push to WebView2 shop search panel (replaces WPF _alertList when window is open)
        _ = PushAlertsToWebViewAsync();

        if (_alertList == null) return;

        _alertList.Items.Clear();

        foreach (var rule in _alertRules.ToList())
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 2),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Textblock: "crude [sell/buy]"
            var modeStr =
                (rule.MatchSellSide && rule.MatchBuySide) ? "sell/buy" :
                (rule.MatchSellSide ? "sell" :
                (rule.MatchBuySide ? "buy" : ""));

            var txt = new TextBlock
            {
                Text = $"{rule.QueryText} [{modeStr}]",
                Foreground = SearchText,
                Width = 160,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            var chkChat = new ToggleButton
            {
                
               
                Width = 28,
                Height = 22,
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Send to team chat",
                IsChecked = rule.NotifyChat,
                Background = new SolidColorBrush(Color.FromRgb(40, 44, 48)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Foreground = Brushes.Black,
                Cursor = Cursors.Hand,
                Content = new TextBlock
                {
                    Style = null,
                    Text = "💬",
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            };
            chkChat.Checked += (_, __) => { rule.NotifyChat = true; SavePersistentAlerts(); };
            chkChat.Unchecked += (_, __) => { rule.NotifyChat = false; SavePersistentAlerts(); };

            var chkSound = new ToggleButton
            {
                Width = 28,
                
                Height = 22,
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Play sound",
                IsChecked = rule.NotifySound,
                Background = new SolidColorBrush(Color.FromRgb(40, 44, 48)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Foreground = Brushes.Black,
                Cursor = Cursors.Hand,
                Content = new TextBlock
                {Style = null,
                    Text = "🔊",
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            };
            chkSound.Checked += (_, __) => { rule.NotifySound = true; SavePersistentAlerts(); };
            chkSound.Unchecked += (_, __) => { rule.NotifySound = false; SavePersistentAlerts(); };

            // Save-Button (💾) - optisch "ausgegraut", wenn schon gespeichert
            var btnSave = new Button
            {
                Width = 28,
                
                Height = 22,
                Margin = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(0),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            // Farben je nach Saved-Status setzen:
            if (rule.IsSaved)
            {
                // saved -> leicht grün getönt
                btnSave.Background = new SolidColorBrush(Color.FromRgb(32, 48, 32));                // sehr dunkles Grün
                btnSave.BorderBrush = new SolidColorBrush(Color.FromRgb(64, 160, 64));              // sattes Grün
                btnSave.ToolTip = "Saved (click to unsave)";
            }
            else
            {
                // nicht saved -> neutral dunkel
                btnSave.Background = new SolidColorBrush(Color.FromRgb(40, 44, 48));                // dein Dark-UI
                btnSave.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));        // dezente helle Kontur
                btnSave.ToolTip = "Save alert";
            }

            // Icon-Farbe (Diskette):
            var saveIcon = new TextBlock
            {
                Style = null,
                Text = "💾",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                // wenn saved -> grünliche Schrift, sonst weiß
                Foreground = rule.IsSaved
                    ? new SolidColorBrush(Color.FromRgb(120, 255, 120)) // hellgrün
                    : Brushes.White
            };

            btnSave.Content = saveIcon;

            // Click toggelt IsSaved, speichert, und baut UI neu auf
            btnSave.Click += (_, __) =>
            {
                rule.IsSaved = !rule.IsSaved;
                SavePersistentAlerts();
                RefreshAlertListUI(); // UI neu zeichnen für neue Farben
            };

            var btnDel = new Button
            {
                
                Width = 28,
                
                Height = 22,
                Margin = new Thickness(4, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(40, 44, 48)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Foreground = Brushes.DarkRed,
                Cursor = Cursors.Hand,
                ToolTip = "Remove alert",
                Content = new TextBlock
                {
                   Style=null,
                    Text = "🗑",
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            btnDel.Click += (_, __) => {
                _alertRules.Remove(rule);
                SavePersistentAlerts();
                RefreshAlertListUI();
            };

            row.Children.Add(txt);
            row.Children.Add(chkChat);
            row.Children.Add(chkSound);
            row.Children.Add(btnSave);
            row.Children.Add(btnDel);

            _alertList.Items.Add(row);
        }
        ApplyThinScrollbar(_alertList);
    }

    private void SavePersistentAlerts()
    {
        try
        {
            var list = _alertRules
                .Where(r => r.IsSaved)
                .Select(r => new PersistedAlertDTO
                {
                    QueryText = r.QueryText,
                    MatchSellSide = r.MatchSellSide,
                    MatchBuySide = r.MatchBuySide,
                    NotifyChat = r.NotifyChat,
                    NotifySound = r.NotifySound
                })
                .ToList();

            string json = System.Text.Json.JsonSerializer.Serialize(
                list,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
            );

            System.IO.File.WriteAllText(GetAlertsSavePath(), json);
        }
        catch
        {
            // absichtlich schlucken - wir wollen hier nicht crashen
        }
    }

    private void LoadPersistentAlerts()
    {
        try
        {
            string path = GetAlertsSavePath();
            if (!System.IO.File.Exists(path)) return;

            string json = System.IO.File.ReadAllText(path);
            var list = System.Text.Json.JsonSerializer.Deserialize<List<PersistedAlertDTO>>(json);
            if (list == null) return;

            foreach (var dto in list)
            {
                var rule = new ShopAlertRule
                {
                    QueryText = dto.QueryText,
                    MatchSellSide = dto.MatchSellSide,
                    MatchBuySide = dto.MatchBuySide,
                    NotifyChat = dto.NotifyChat,
                    NotifySound = dto.NotifySound,
                    IsSaved = true
                };

                // Baseline NICHT von Disk laden, sondern jetzt frisch setzen,
                // damit vorhandene Angebote nicht sofort gespammt werden:
                foreach (var shop in _lastShops)
                {
                    if (shop.Orders == null) continue;
                    foreach (var o in shop.Orders)
                    {
                        bool matchesSide =
                            (rule.MatchSellSide && MatchOrderLeft(o, rule.QueryText)) ||
                            (rule.MatchBuySide && MatchOrderRight(o, rule.QueryText));

                        if (!matchesSide) continue;

                        rule.Baseline.Add(new AlertSeenOrder
                        {
                            ShopId = shop.Id,
                            ItemShort = o.ItemShortName,
                            CurrencyShort = o.CurrencyShortName,
                            Stock = o.Stock,
                            Quantity = o.Quantity,
                            CurrencyAmount = o.CurrencyAmount
                        });
                    }
                }

                _alertRules.Add(rule);
            }
        }
        catch
        {
            // wenn Laden fehlschlägt, egal – wir starten halt ohne gespeicherte Alerts
        }
    }

    private DateTime _lastChatSendUtc = DateTime.MinValue; // Rate-Limit (1/sec)

    // pro Alert merken wir, welche Angebote schon existierten beim Setzen
    private class AlertSeenOrder
    {
        public uint ShopId;
        public string ItemShort;
        public string CurrencyShort;
        public int Quantity;
        public float CurrencyAmount;
        public int Stock;
    }

    private class PersistedAlertDTO
    {
        public string QueryText { get; set; } = "";
        public bool MatchSellSide { get; set; }
        public bool MatchBuySide { get; set; }
        public bool NotifyChat { get; set; }
        public bool NotifySound { get; set; }
    }

    private string GetAlertsSavePath()
    {
        // simple Variante: im gleichen Ordner wie die EXE
        return System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "shop_alerts.json"
        );
    }

    private bool MatchOrderLeft(RustPlusClientReal.ShopOrder o, string q)
    {
        if (string.IsNullOrEmpty(q)) return true;
        var name = ResolveItemName(o.ItemId, o.ItemShortName);
        return name.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchOrderRight(RustPlusClientReal.ShopOrder o, string q)
    {
        if (string.IsNullOrEmpty(q)) return true;
        var name = ResolveItemName(o.CurrencyItemId, o.CurrencyShortName);
        return name.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private async Task CheckAlerts(IReadOnlyList<RustPlusClientReal.ShopMarker> shops)
    {
        foreach (var rule in _alertRules)
        {
            foreach (var shop in shops)
            {
                if (shop.Orders == null) continue;

                foreach (var order in shop.Orders)
            {
                // 1) Passt zur Regel?
                bool matchesSide =
                    (rule.MatchSellSide && MatchOrderLeft(order, rule.QueryText)) ||
                    (rule.MatchBuySide && MatchOrderRight(order, rule.QueryText));

                if (!matchesSide)
                    continue;

                // 2) Baseline-Eintrag für diese Kombo suchen
                var baseline = rule.Baseline.FirstOrDefault(b =>
                    b.ShopId        == shop.Id &&
                    b.ItemShort     == order.ItemShortName &&
                    b.CurrencyShort == order.CurrencyShortName &&
                    b.Quantity      == order.Quantity &&
                    Math.Abs(b.CurrencyAmount - order.CurrencyAmount) < 0.001f
                );

                int prevStock = baseline?.Stock ?? 0;
                int curStock  = order.Stock;

                // 3) Baseline updaten/erzeugen – wir wollen immer den letzten Stock dort haben
                if (baseline == null)
                {
                    baseline = new AlertSeenOrder
                    {
                        ShopId        = shop.Id,
                        ItemShort     = order.ItemShortName,
                        CurrencyShort = order.CurrencyShortName,
                        Quantity      = order.Quantity,
                        CurrencyAmount= order.CurrencyAmount,
                        Stock         = curStock
                    };
                    rule.Baseline.Add(baseline);
                }
                else
                {
                    baseline.Stock = curStock;
                }

                // 4) Wenn aktuell kein Stock → nie alerten, nur Zustand merken
                if (curStock <= 0)
                    continue;

                // 5) Wenn die Regel gerade erst initialisiert wird (erster Poll nach Anlage/Start),
                // unterdrücken wir den Alert, um Massen-Spam beim Programmstart zu vermeiden.
                // Aber: Ein NEUER Shop, der WÄHREND die Regel schon aktiv ist auftaucht,
                // soll natürlich TROTZDEM alerten.
                if (!rule.InitializationComplete)
                    continue;

                // 6) Entscheiden, ob wir das als "neu" werten
                bool isNewDeal   = (baseline != null && prevStock == 0); // entweder ganz neu oder aus 0 kommend
                bool isRestock   = (prevStock <= 0 && curStock > 0);
                bool alreadySeenWithStock = (prevStock > 0);

                if (!isNewDeal && !isRestock && alreadySeenWithStock)
                {
                    // hatten wir schon mit Stock > 0, und es ist kein neuer Preis/Menge → nichts tun
                    continue;
                }

                // 6) Pro-Order Spam-Schutz wie gehabt
                string sig = $"{shop.Id}:{order.ItemShortName}:{order.CurrencyShortName}:{order.Quantity}:{order.CurrencyAmount}";
                if (rule.LastAnnouncements.TryGetValue(sig, out var lastWhen) &&
                    (DateTime.UtcNow - lastWhen).TotalSeconds < 60)
                {
                    continue;
                }

                // 7) Globales Rate Limit
                if ((DateTime.UtcNow - _lastChatSendUtc).TotalSeconds < 1.0)
                    continue;
                _lastChatSendUtc = DateTime.UtcNow;

                // 8) Nachricht bauen + loggen
                string grid         = GetGridLabel(shop);
                string itemName     = ResolveItemName(order.ItemId, order.ItemShortName);
                string currencyName = ResolveItemName(order.CurrencyItemId, order.CurrencyShortName);
                string verb         = rule.MatchSellSide ? Properties.Resources.AlertShopSells : Properties.Resources.AlertShopBuys;
                string shopLabel    = shop.Label ?? Properties.Resources.AlertShopLabelFallback;

                string msg = string.Format(
                    Properties.Resources.AlertShopMatch,
                    shopLabel,
                    grid,
                    verb,
                    order.Quantity,
                    itemName,
                    order.Stock,
                    order.CurrencyAmount,
                    currencyName
                );

                AppendLog($"[{DateTime.Now:HH:mm:ss}] Alert: {msg}");

                if (rule.NotifyChat)
                    await SendTeamChatSafeAsync(msg);

                if (rule.NotifySound)
                    PlayShopAlertSound();

                // 9) Zeitstempel für diese Kombo updaten
                rule.LastAnnouncements[sig] = DateTime.UtcNow;
            }           // end foreach order
            }           // end foreach shop

            // Nach dem ersten Durchlauf markieren wir die Regel als initialisiert
            rule.InitializationComplete = true;
        }
    }
    // ─── ONLINE PLAYERS & TRACKING ───────────────────────────────────────────

    private void RebaselineAllAlertRulesFromCurrentShops(IReadOnlyList<RustPlusClientReal.ShopMarker> shops)
    {
        foreach (var rule in _alertRules)
        {
            // alte bekannte Angebote verwerfen
            rule.Baseline.Clear();
            rule.LastAnnouncements.Clear();

            foreach (var shop in shops)
            {
                if (shop.Orders == null) continue;

                foreach (var o in shop.Orders)
                {
                    if (o.Stock <= 0) continue;

                    bool matchesSide =
                        (rule.MatchSellSide && MatchOrderLeft(o, rule.QueryText)) ||
                        (rule.MatchBuySide && MatchOrderRight(o, rule.QueryText));

                    if (!matchesSide)
                        continue;

                    rule.Baseline.Add(new AlertSeenOrder
                    {
                        ShopId = shop.Id,
                        ItemShort = o.ItemShortName,
                        CurrencyShort = o.CurrencyShortName,
                        Quantity = o.Quantity,
                        CurrencyAmount = o.CurrencyAmount,
                        Stock = o.Stock
                    });
                }
            }
        }
    }

    // Flags, die wir aus den Checkboxes lesen:
    // private bool _notifyNewShopsToChat = false;
    // private bool _notifySuspiciousShops = false;


    // ====== NEW SHOP TRACKING ======
    // für "neue Shops" nach Initial-Poll:
    private HashSet<uint> _knownShopIds = new();
    private DateTime _initialShopSnapshotTimeUtc = DateTime.MinValue;

    // ====== SUSPICIOUS TRACKING ======
    private class ShopLifetimeInfo
    {
        public DateTime FirstSeenUtc;
        public DateTime? LastSeenUtc;
        public bool AnnouncedSuspicious = false;
        public RustPlusClientReal.ShopMarker? LastSnapshot;
    }

    private readonly Dictionary<uint, ShopLifetimeInfo> _shopLifetimes = new();


    // ====== HILFE-FUNKTION SOUND ======
    private System.Media.SoundPlayer? _shopSoundPlayer;

    private void PlayShopAlertSound()
    {
        try
        {
            string baseDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            string path = System.IO.Path.Combine(baseDir, "Assets", "cash.wav");
            if (!System.IO.File.Exists(path)) path = System.IO.Path.Combine(baseDir, "cash.wav");

            if (System.IO.File.Exists(path))
            {
                var fullPath = System.IO.Path.GetFullPath(path);
                // SoundPlayer ist für WAV-Dateien effizienter und verhindert Knirschen
                if (_shopSoundPlayer == null)
                {
                    _shopSoundPlayer = new System.Media.SoundPlayer(fullPath);
                }
                else if (_shopSoundPlayer.SoundLocation != fullPath)
                {
                    _shopSoundPlayer.SoundLocation = fullPath;
                }
                
                _shopSoundPlayer.Play();
            }
        }
        catch { /* ignore */ }
    }

    private BitmapSource ComposeMapWithMarkers(BitmapSource baseBmp)
    {
        // Mapgröße in DIPs
        double wDip = baseBmp.PixelWidth * (96.0 / baseBmp.DpiX);
        double hDip = baseBmp.PixelHeight * (96.0 / baseBmp.DpiY);

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            // 1) Map zeichnen
            dc.DrawImage(baseBmp, new Rect(0, 0, wDip, hDip));

            // 2) Marker (DIP) draufzeichnen
            foreach (var m in _staticMarkers)
            {
                double uDip = m.uPx * (96.0 / baseBmp.DpiX);
                double vDip = m.vPx * (96.0 / baseBmp.DpiY);

                const double r = 10.0; // Radius in DIPs (skaliert mit)
                var fill = Brushes.OrangeRed;
                var stroke = new Pen(Brushes.White, 3);

                dc.DrawEllipse(fill, stroke, new Point(uDip, vDip), r, r);

                if (!string.IsNullOrWhiteSpace(m.label))
                {
                    var ft = new FormattedText(
                        m.label, System.Globalization.CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight, new Typeface("Segoe UI"),
                        12, Brushes.Black, 1.25);
                    dc.DrawText(ft, new Point(uDip + 10, vDip - 8));
                }
            }
        }

        var rtb = new RenderTargetBitmap(
            (int)Math.Ceiling(wDip), (int)Math.Ceiling(hDip), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }


    private double GetCurrentScale()
    {
        var m = MapTransform.Matrix;
        return Math.Sqrt(m.M11 * m.M11 + m.M12 * m.M12);

    }

    public void AddMarker(double uPx, double vPx, string label = "", Brush? color = null)
    {
        if (ImgMap.Source is not BitmapSource src) return;

        double uDip = uPx * 96.0 / src.DpiX;
        double vDip = vPx * 96.0 / src.DpiY;

        const double r = 7.0;
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = r * 2,
            Height = r * 2,
            Fill = color ?? Brushes.OrangeRed,
            Stroke = Brushes.White,
            StrokeThickness = 2,

            RenderTransformOrigin = new Point(0.5, 0.5)
        };

        Canvas.SetLeft(dot, uDip - r);
        Canvas.SetTop(dot, vDip - r);
        Overlay.Children.Add(dot);
    }

    public void AddMarkerPx(double uPx, double vPx, string label = "", Brush? color = null)
    {
        if (ImgMap.Source is not BitmapSource src) return;
        double uDip = uPx * (96.0 / src.DpiX);
        double vDip = vPx * (96.0 / src.DpiY);

        const double r = 7;
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 2 * r,
            Height = 2 * r,
            Fill = color ?? Brushes.OrangeRed,
            Stroke = Brushes.White,
            StrokeThickness = 2,

            ToolTip = string.IsNullOrWhiteSpace(label) ? null : label
        };
        Canvas.SetLeft(dot, uDip - r);
        Canvas.SetTop(dot, vDip - r);
        Overlay.Children.Add(dot);
        // NEU: im Registry merken + gleich korrekt positionieren
        _markers.Add(new MarkerRef(dot, uDip, vDip, r));
        // UpdateMarkerPositions();
    }

    private void RescaleMarkersForCurrentZoom() // optional – nur für konstante Markergröße
    {
        double k = 1.0 / GetCurrentScale();
        foreach (var el in Overlay.Children.OfType<System.Windows.Shapes.Ellipse>())
            el.RenderTransform = new ScaleTransform(k, k, el.Width / 2.0, el.Height / 2.0);
    }

    // NEW CLICK HANDLERS TO DELETE JSON CONFIG

    private static string PairingConfigPath =>
    System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RustPlusDesk", "rustplusjs-config.json");

    private async Task<bool> ResetPairingConfigAsync(bool stopListenerFirst = true)
    {
        try
        {
            if (stopListenerFirst && _pairing.IsRunning)
            {
                AppendLog("Stopping pairing listener …");
                await Task.Run(async () => await _pairing.StopAsync());
                await Task.Delay(200); // kleine Atempause
            }

            if (File.Exists(PairingConfigPath))
            {
                File.Delete(PairingConfigPath);
                AppendLog($"🗑️ Deleted pairing config: {PairingConfigPath}");
            }
            else
            {
                AppendLog("ℹ️ No pairing config found to delete on disk.");
            }

            // Always clear tracking dates on reset
            TrackingService.FcmIssuedAt = null;
            TrackingService.FcmExpiresAt = null;
            _vm.NotifyFcmChanged();

            TxtPairingState.Text = "Pairing: config deleted";
            return true;
        }
        catch (Exception ex)
        {
            AppendLog("❌ Failed to delete pairing config: " + ex.Message);
            return false;
        }
    }

    private async void BtnResetPairing_Click(object sender, RoutedEventArgs e)
    {
        var ask = MessageBox.Show(
            "Delete existing pairing config?\nYou will need to pair again on next start.",
            "Reset pairing", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (ask != MessageBoxResult.Yes) return;

        await ResetPairingConfigAsync(stopListenerFirst: true);
    }

    private async void BtnResetAndListen_Click(object sender, RoutedEventArgs e)
    {
        var ask = MessageBox.Show(
            "Delete pairing config and immediately re-pair/listen?",
            "Reset + Listen", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (ask != MessageBoxResult.Yes) return;

        if (await ResetPairingConfigAsync(stopListenerFirst: true))
            await StartPairingListenerUiAsync(); // dein bestehender Standard-Flow
    }

    private async void BtnResetAndListenEdge_Click(object sender, RoutedEventArgs e)
    {
        var ask = MessageBox.Show(
            "Delete pairing config and immediately re-pair/listen using Edge?",
            "Reset + Listen (Edge)", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (ask != MessageBoxResult.Yes) return;

        if (await ResetPairingConfigAsync(stopListenerFirst: true))
            await StartPairingListenerUiWithEdgeAsync(); // der Edge-Flow aus voriger Antwort
    }

    private CancellationTokenSource? _statusCts;


    // CHECK FOR UPDATES

    // --- Konfiguration ---
    private async Task AutoCheckUpdatesAsync()
    {
        try
        {
            var latestInfo = await _updateService.GetLatestReleaseAsync();
            if (latestInfo is null) return;

            var (latest, tag, dlUrl) = latestInfo.Value;
            var curr = _updateService.VersionForCompare;

            bool updateAvailable = false;
            if (latest > curr) updateAvailable = true;
            else if (latest == curr)
            {
                bool localIsBeta = _updateService.VersionRaw.Contains("-", StringComparison.OrdinalIgnoreCase);
                bool remoteIsBeta = tag.Contains("-", StringComparison.OrdinalIgnoreCase);
                if (localIsBeta && !remoteIsBeta) updateAvailable = true;
            }

            if (updateAvailable)
            {
                Dispatcher.Invoke(() =>
                {
                    _vm.IsUpdateAvailable = true;
                    _vm.UpdateTag = tag;
                    AppendLog($"✨ Update found: {tag}");
                    ShowUpdateSnackbar(tag, dlUrl);
                });
            }
        }
        catch { /* silent */ }
    }

    private void ShowUpdateSnackbar(string tag, string? dlUrl)
    {
        if (RootSnackbar == null) return;

        var snackbar = new WpfUi.Snackbar(RootSnackbar)
        {
            Title = "Update Available",
            Appearance = WpfUi.ControlAppearance.Success,
            Icon = new WpfUi.SymbolIcon(WpfUi.SymbolRegular.ArrowDownload24),
            Timeout = TimeSpan.FromSeconds(7),
            MaxWidth = 350,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(new TextBlock { Text = $"Version {tag} is available. Download now?", Margin = new Thickness(0, 0, 0, 8) });

        if (!string.IsNullOrEmpty(dlUrl))
        {
            var btn = new WpfUi.Button
            {
                Content = "Download & Update on Close",
                Appearance = WpfUi.ControlAppearance.Primary,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            btn.Click += async (s, e) =>
            {
                // In Wpf.Ui 3.0 the Snackbar is controlled via its Visibility or IsShown property if used directly
                // but if it's a separate window/control it might be different. 
                // Using IsOpen = false is common for many popup-like controls in Wpf.Ui
                snackbar.Visibility = Visibility.Collapsed; 
                await PerformUpdateDownloadAsync(tag, dlUrl);
            };
            stack.Children.Add(btn);
        }

        snackbar.Content = stack;
        snackbar.Show();
    }

    public void ApplySettings()
    {
        if (TxtLog != null)
        {
            TxtLog.Visibility = TrackingService.HideConsole ? Visibility.Collapsed : Visibility.Visible;
        }

        if (ColSidebar != null)
        {
            double w = TrackingService.SidebarWidth;
            if (w < 400) w = 400;

            // Ensure we don't squash the map below 800 if window is small
            if (this.ActualWidth > 0)
            {
                double maxW = this.ActualWidth - 850; // 800 map + 50 padding/splitter
                if (w > maxW && maxW > 400) w = maxW;
            }

            ColSidebar.Width = new GridLength(w, GridUnitType.Pixel);
        }
        _announceSpawns = TrackingService.AnnounceSpawnsMaster;

        _showProfileMarkers = TrackingService.MapShowSteamMarkers;
        if (ChkProfileMarkers != null) ChkProfileMarkers.IsChecked = _showProfileMarkers;

        _showDeathMarkers = TrackingService.MapShowDeathTags;
        if (ChkDeathMarkers != null) ChkDeathMarkers.IsChecked = _showDeathMarkers;

        _abbreviateNames = TrackingService.MapAbbreviateNames;
        if (BtnAbbreviateNames != null) BtnAbbreviateNames.IsChecked = _abbreviateNames;

        _playerMarkerScale = TrackingService.MapPlayerIconScale;
        if (SliderPlayerIconSize != null) SliderPlayerIconSize.Value = _playerMarkerScale;

        BuildMonumentOverlays();
    }

    private void ShowInfoSnackbar(string title, string message, WpfUi.ControlAppearance appearance)
    {
        if (RootSnackbar == null) return;
        var snackbar = new WpfUi.Snackbar(RootSnackbar)
        {
            Title = title,
            Content = message,
            Appearance = appearance,
            Icon = new WpfUi.SymbolIcon(WpfUi.SymbolRegular.Info24),
            Timeout = TimeSpan.FromSeconds(5),
            MaxWidth = 350,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        snackbar.Show();
    }

    private WpfUi.Snackbar? _guideSnackbar;

    private void UpdatePairingGuideSnackbar()
    {
        if (RootSnackbar == null) return;
        
        if (_vm.Servers.Count > 0)
        {
            if (_guideSnackbar != null)
            {
                _guideSnackbar.Visibility = Visibility.Collapsed;
                _guideSnackbar = null;
            }
            return;
        }

        bool isListening = TxtPairingState.Text != null && TxtPairingState.Text.Contains("listening");

        string title = isListening ? "Pairing Active" : "Action Required";
        string msg = isListening 
            ? "Please pair your server in-game with Rust+" 
            : "Please pair your Steam account to start.";
        var appearance = isListening ? WpfUi.ControlAppearance.Info : WpfUi.ControlAppearance.Caution;
        var icon = isListening ? WpfUi.SymbolRegular.Phone24 : WpfUi.SymbolRegular.Warning24;

        if (_guideSnackbar == null)
        {
            _guideSnackbar = new WpfUi.Snackbar(RootSnackbar)
            {
                Title = title,
                Content = msg,
                Appearance = appearance,
                Icon = new WpfUi.SymbolIcon(icon),
                Timeout = TimeSpan.FromHours(24),
                MaxWidth = 350,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            _guideSnackbar.Show();
        }
        else
        {
            _guideSnackbar.Title = title;
            _guideSnackbar.Content = msg;
            _guideSnackbar.Appearance = appearance;
            _guideSnackbar.Icon = new WpfUi.SymbolIcon(icon);
            if (_guideSnackbar.Visibility != Visibility.Visible)
                _guideSnackbar.Show();
        }
    }
    private void SidebarSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (ColSidebar != null)
        {
            TrackingService.SidebarWidth = ColSidebar.ActualWidth;
        }
    }

    private void BtnDiscord_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://discord.gg/v4X584wye4") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendLog("❌ Could not open Discord link: " + ex.Message);
        }
    }

    private void BtnPatchNotes_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var patchNotesWin = new PatchNotesWindow { Owner = this };
            patchNotesWin.ShowDialog();
        }
        catch (Exception ex)
        {
            AppendLog("❌ Could not open Patch Notes window: " + ex.Message);
        }
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        if (AppSettingsPanel.Visibility == Visibility.Visible)
        {
            AppSettingsPanel.Visibility = Visibility.Collapsed;
            ApplySettings();
        }
        else
        {
            ProfitTradesPanel.Visibility = Visibility.Collapsed;
            BuyXForYPanel.Visibility = Visibility.Collapsed;
            AppSettingsPanel.LoadSettings();
            AppSettingsPanel.Visibility = Visibility.Visible;
        }
    }

    private void BtnLanguageSettings_Click(object sender, RoutedEventArgs e)
    {
        BtnSettings_Click(sender, e);
    }

    public void UpdateLanguageFlag()
    {
        if (ImgLanguageFlag == null) return;
        string code = TrackingService.SelectedLanguage;
        if (string.IsNullOrEmpty(code))
        {
            ImgLanguageFlag.Visibility = Visibility.Collapsed;
        }
        else
        {
            ImgLanguageFlag.Visibility = Visibility.Visible;

            // Try full code first (e.g. sv-SE.png), then fall back to two-letter prefix (e.g. sv.png)
            // so that most flags (stored as neutral codes) still resolve correctly.
            var candidates = new[] { code, code.Contains('-') ? code.Split('-')[0] : null };
            bool loaded = false;
            foreach (var candidate in candidates)
            {
                if (candidate == null) continue;
                string imageUri = $"pack://application:,,,/Assets/Flags/{candidate}.png";
                try
                {
                    ImgLanguageFlag.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(imageUri));
                    loaded = true;
                    break;
                }
                catch { }
            }
            if (!loaded)
                ImgLanguageFlag.Visibility = Visibility.Collapsed;
        }
    }

    private void InitializeAppSettings()
    {
        if (AppSettingsPanel != null)
        {
            AppSettingsPanel.ParentWindow = this;
        }
    }

    public void OpenChatAlertsFromSettings()
    {
        Dispatcher.BeginInvoke(new Action(() => {
            if (ChatAlertsConfigureButton.Flyout is ContextMenu cm)
            {
                cm.PlacementTarget = ChatAlertsConfigureButton;
                cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
                cm.IsOpen = true;
            }
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    public System.Windows.Controls.Primitives.CustomPopupPlacement[] CenterMegaMenu_Callback(Size popupSize, Size targetSize, Point offset)
    {
        double x = (targetSize.Width - popupSize.Width) / 2;
        double y = targetSize.Height + 4; // Fluent spacing gap
        return new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(x, y), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal) };
    }

    public async void OpenChatCommandsFromSettings()
    {
        if (ChatContentBorder.Visibility != Visibility.Visible)
        {
            _chatOpenedForCommandsOnly = true;
            await OpenChatOverlayAsync();
        }
        else
        {
            _chatOpenedForCommandsOnly = false;
        }
        BtnOpenChatCommands_Click(null, null);
    }

    private async Task PerformUpdateDownloadAsync(string tag, string dlUrl)
    {
        try
        {
            _vm.IsDownloadingUpdate = true;
            var prog = new Progress<DownloadReport>(r =>
            {
                _vm.BusyText = $"Downloading installer … {r.Percentage}";
                _vm.UpdateDownloadProgress = r.Progress * 100;
                _vm.UpdateDownloadSpeed = r.Speed;
                _vm.UpdateDownloadSize = $"{r.BytesReceived} / {r.TotalBytes}";
                _vm.UpdateDownloadPercentage = r.Percentage;
            });

            var path = await _updateService.DownloadInstallerAsync(dlUrl, prog);
            _vm.IsDownloadingUpdate = false;

            if (path == null)
            {
                ShowInfoSnackbar("Update", "Download failed.", WpfUi.ControlAppearance.Danger);
                return;
            }

            _updateService.PendingInstallerPath = path;
            ShowInfoSnackbar("Update Downloaded", "The update will be installed automatically when you close the app.", WpfUi.ControlAppearance.Success);
        }
        catch (Exception ex)
        {
            _vm.IsDownloadingUpdate = false;
            AppendLog("❌ Update download failed: " + ex.Message);
            ShowInfoSnackbar("Update", "Download failed: " + ex.Message, WpfUi.ControlAppearance.Danger);
        }
    }

    private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_listenerStarting || _vm.IsDownloadingUpdate) return;
        if (!string.IsNullOrEmpty(_updateService.PendingInstallerPath))
        {
            ShowInfoSnackbar("Update", "Update already downloaded. It will be installed when you close the app.", WpfUi.ControlAppearance.Info);
            return;
        }

        try
        {
            var curr = _updateService.VersionForCompare;
            var latestInfo = await _updateService.GetLatestReleaseAsync();
            if (latestInfo is null)
            {
                System.Windows.MessageBox.Show(
                    "Could not query latest release. Please try again or open Releases page.",
                    "Update", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var (latest, tag, dlUrl) = latestInfo.Value;
            AppendLog($"Current: {_updateService.VersionShort} | Latest: {latest} ({tag})");

            bool updateAvailable = false;
            if (latest > curr) updateAvailable = true;
            else if (latest == curr)
            {
                bool localIsBeta = _updateService.VersionRaw.Contains("-", StringComparison.OrdinalIgnoreCase);
                bool remoteIsBeta = tag.Contains("-", StringComparison.OrdinalIgnoreCase);
                if (localIsBeta && !remoteIsBeta) updateAvailable = true;
            }

            if (!updateAvailable)
            {
                _vm.IsUpdateAvailable = false;
                System.Windows.MessageBox.Show("You are up to date.", "Update", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _vm.IsUpdateAvailable = true;
            _vm.UpdateTag = tag;

            if (string.IsNullOrWhiteSpace(dlUrl))
            {
                var open = System.Windows.MessageBox.Show(
                    $"New version available: {tag}\nOpen Releases page?",
                    "Update available", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (open == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(UpdateService.LatestReleaseUrl) { UseShellExecute = true });
                return;
            }

            var ask = System.Windows.MessageBox.Show(
                $"New version available: {tag}\nDownload and install now?",
                "Update available", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (ask != MessageBoxResult.Yes) return;

            _vm.IsDownloadingUpdate = true;
            var prog = new Progress<DownloadReport>(r =>
            {
                _vm.BusyText = $"Downloading installer … {r.Percentage}";
                _vm.UpdateDownloadProgress = r.Progress * 100;
                _vm.UpdateDownloadSpeed = r.Speed;
                _vm.UpdateDownloadSize = $"{r.BytesReceived} / {r.TotalBytes}";
                _vm.UpdateDownloadPercentage = r.Percentage;
            });
            var path = await _updateService.DownloadInstallerAsync(dlUrl!, prog);

            _vm.IsDownloadingUpdate = false;

            if (path == null)
            {
                System.Windows.MessageBox.Show("Download failed.", "Update", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            AppendLog("Starting installer …");
            _updateService.StartInstaller(path);
            try { if (_pairing?.IsRunning == true) await Task.Run(async () => await _pairing.StopAsync()); } catch { }
            await Task.Delay(500);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _vm.IsUpdateAvailable = false;
            _vm.IsDownloadingUpdate = false;
            AppendLog("❌ Update check failed: " + ex.Message);
            System.Windows.MessageBox.Show("Update check failed.\n" + ex.Message, "Update", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCheckUpdates_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_vm.IsDownloadingUpdate)
            UpdateDownloadPopup.IsOpen = true;
    }

    private void BtnCheckUpdates_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        UpdateDownloadPopup.IsOpen = false;
    }

    /// DEVICE HOTKEYS
    /// 
    
    private readonly SemaphoreSlim _hotkeySeqGate = new(1, 1);

    private GlobalHotkeyManager? _hotkeyMgr;
    private readonly Dictionary<string, Dictionary<string, List<long>>> _hotkeysByServer
     = new(StringComparer.OrdinalIgnoreCase);
    private static string HotkeyConfigPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                               "RustPlusDesk", "hotkeys.json");

    private string CurrentServerKey()
    {
        var sel = _vm?.Selected;
        if (sel == null) return "default";
        // Beispiel: wenn dein Serverobjekt Host/Port hat:
        return $"{sel.Host}:{sel.Port}";
    }

    private Dictionary<string, List<long>> MapForCurrentServer()
    {
        var key = CurrentServerKey();
        if (!_hotkeysByServer.TryGetValue(key, out var map))
            _hotkeysByServer[key] = map = new(StringComparer.OrdinalIgnoreCase);
        return map;
    }


    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Spätestens hier sollte Windows den Titel im Rahmen akzeptieren
        if (AppTitleBar != null) AppTitleBar.Title = $"RUST+ DESKTOP v{_updateService.VersionRaw}";
        this.Title = $"RUST+ DESKTOP v{_updateService.VersionRaw}";

        var hwnd = new WindowInteropHelper(this).Handle;
        _hotkeyMgr = new GlobalHotkeyManager(hwnd);
        _hotkeyMgr.HotkeyPressed += OnHotkeyPressed;

        HwndSource.FromHwnd(hwnd)!.AddHook(WndProc);

        LoadHotkeys();
        ActivateHotkeysForCurrentServer();   // statt RegisterAllHotkeys()
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && _hotkeyMgr != null)
        {
            _hotkeyMgr.OnWmHotkey(wParam, lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void LoadHotkeys()
    {
        try
        {
            var p = HotkeyConfigPath;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
            _hotkeysByServer.Clear();
            if (!System.IO.File.Exists(p)) return;

            var json = System.IO.File.ReadAllText(p);

            // NEUE Struktur: { "host:port": { "Ctrl+Alt+K": [123,456] } }
            try
            {
                var v = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<long>>>>(json);
                if (v != null && v.Count > 0) { foreach (var kv in v) _hotkeysByServer[kv.Key] = kv.Value; return; }
            }
            catch { /* fall through */ }

            // ALTE Struktur: { "Ctrl+Alt+K": [123,456] } -> nach "default" migrieren
            try
            {
                var old = JsonSerializer.Deserialize<Dictionary<string, List<long>>>(json);
                if (old != null) _hotkeysByServer["default"] = new(old, StringComparer.OrdinalIgnoreCase);
            }
            catch { }
        }
        catch (Exception ex) { AppendLog("Hotkeys load error: " + ex.Message); }
    }

    private void SaveHotkeys()
    {
        try
        {
            // ⬇︎ NEU
            PruneEmptyGesturesAllServers();

            var json = JsonSerializer.Serialize(_hotkeysByServer,
                        new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(HotkeyConfigPath, json);
        }
        catch (Exception ex) { AppendLog("Hotkeys save error: " + ex.Message); }
    }

    private void PruneEmptyGesturesForCurrentServer()
    {
        var map = MapForCurrentServer();
        foreach (var key in map.Where(kv => kv.Value == null || kv.Value.Count == 0)
                               .Select(kv => kv.Key).ToList())
            map.Remove(key);
    }

 

    private void PruneEmptyGesturesAllServers()
    {
        foreach (var srv in _hotkeysByServer.Keys.ToList())
        {
            var map = _hotkeysByServer[srv];
            foreach (var key in map.Where(kv => kv.Value == null || kv.Value.Count == 0)
                                   .Select(kv => kv.Key).ToList())
                map.Remove(key);
        }
    }

    private void RegisterAllHotkeys()
    {
        if (_hotkeyMgr == null) return;

        // ⬇︎ NEU: leere Keys entfernen (verhindert „blockierte“ Gesten)
        PruneEmptyGesturesForCurrentServer();

        _hotkeyMgr.UnregisterAll();
        foreach (var gesture in MapForCurrentServer().Keys)
        {
            if (!_hotkeyMgr.Register(gesture))
                AppendLog($"⚠️ Cannot register hotkey '{gesture}'.");
        }
    }

    private DateTime _lastHotkeyAt = DateTime.MinValue;
    private bool HotkeyThrottle(int ms = 400)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastHotkeyAt).TotalMilliseconds < ms) return true;
        _lastHotkeyAt = now;
        return false;
    }


    private readonly Dictionary<string, DateTime> _lastGestureAt = new(StringComparer.OrdinalIgnoreCase);


    private void OnHotkeyPressed(string gesture)
    {
        // kleiner Debounce pro Geste (falls OS NOREPEAT ignoriert)
        var now = DateTime.UtcNow;
        if (_lastGestureAt.TryGetValue(gesture, out var last) &&
            (now - last).TotalMilliseconds < 350)
            return;
        _lastGestureAt[gesture] = now;

        var map = MapForCurrentServer();
        if (!map.TryGetValue(gesture, out var ids) || ids.Count == 0) return;

        _ = RunHotkeySequenceOnceAsync(ids);
    }

    private async Task RunHotkeySequenceOnceAsync(IReadOnlyCollection<long> ids)
    {
        if (!await _hotkeySeqGate.WaitAsync(0)) // schon eine Sequenz aktiv?
        {
            AppendLog("Hotkey sequence already running – ignored.");
            return;
        }
        try
        {
            await ToggleSequenceAsync(ids.Distinct().ToList()); // doppelte IDs vermeiden
        }
        finally
        {
            _hotkeySeqGate.Release();
        }
    }

    private SmartDevice? FindDevice(long entityId)
    {
        var enumerable = (DataContext as dynamic)?.CurrentDevices as IEnumerable
                         ?? ListDevices.ItemsSource as IEnumerable; 
        if (enumerable == null) return null;

        return FindDeviceRecursive(enumerable.OfType<SmartDevice>(), entityId);
    }

    private SmartDevice? FindDeviceRecursive(IEnumerable<SmartDevice> col, long entityId)
    {
        foreach (var dev in col)
        {
            if (dev.EntityId == entityId) return dev;
            if (dev.IsGroup && dev.Children != null)
            {
                var found = FindDeviceRecursive(dev.Children, entityId);
                if (found != null) return found;
            }
        }
        return null;
    }

    private async Task ToggleSequenceAsync(IEnumerable<long> entityIds)
    {
        foreach (var id in entityIds)
        {
            var rootDev = FindDevice(id);
            if (rootDev == null) continue;

            var targets = new List<SmartDevice>();
            if (rootDev.IsGroup && rootDev.Children != null)
                targets.AddRange(GetSwitchesRecursive(rootDev.Children));
            else if (string.Equals(rootDev.Kind, "SmartSwitch", StringComparison.OrdinalIgnoreCase))
                targets.Add(rootDev);

            foreach (var dev in targets)
            {
                if (dev.IsMissing) continue;

                bool current = dev.IsOn ?? false;
                bool desired = !current; 

                var fakeSender = new System.Windows.FrameworkElement { DataContext = dev };
                await HandleDeviceToggleAsync(fakeSender, desired);

                await Task.Delay(650);
                if (_rust == null) break; 
            }
        }
    }

    private readonly Dictionary<uint, DateTime> _toggleBusy = new();
    private readonly object _toggleBusyLock = new();

    private readonly Dictionary<long, DateTime> _toggleBusySince = new();
    private static readonly TimeSpan ToggleBusyTTL = TimeSpan.FromSeconds(12);

    private bool TryMarkToggleBusy(uint id)
    {
        lock (_toggleBusy)
        {
            if (_toggleBusy.TryGetValue(id, out var ts))
            {
                // Stale? -> übernehmen & weitermachen
                if (DateTime.UtcNow - ts > ToggleBusyTTL)
                {
                    _toggleBusy[id] = DateTime.UtcNow;
                    AppendLog($"(recovered) cleared stale toggle lock for #{id}");
                    return true;
                }
                return false;
            }
            _toggleBusy[id] = DateTime.UtcNow;
            return true;
        }
    }

    private void UnmarkToggleBusy(uint id)
    {
        lock (_toggleBusy) { _toggleBusy.Remove(id); }
    }

    private void ClearAllToggleBusy()
    {
        lock (_toggleBusy) { _toggleBusy.Clear(); }
        _globalToggleBusy = false;
    }

    private void ResetAllBusyStates()
    {
        _globalToggleBusy = false;
        _isDynPollBusy = false;
        _storageTickBusy = false;
        _apiConsecutiveTimeouts = 0;
        System.Threading.Interlocked.Exchange(ref _teamPollBusy, 0);
        System.Threading.Interlocked.Exchange(ref _camThumbBusy, 0);
        AppendLog("[reset] All busy flags cleared.");
    }

    private void BtnHotkeys_Click(object sender, RoutedEventArgs e)
    {
        DeactivateHotkeys();

        IEnumerable? src = (ListDevices.ItemsSource as IEnumerable) ?? (DataContext as IEnumerable);
        if (src == null) { MessageBox.Show(Properties.Resources.NoDevices); return; }
        
        var flatAssignable = GetHotkeyAssignableDevices(src.OfType<SmartDevice>());

        var dlg = new HotkeysWindow(flatAssignable, MapForCurrentServer()) { Owner = this };
        bool? activate = dlg.ShowDialog();

        SaveHotkeys();

        if (activate == true) ActivateHotkeysForCurrentServer();
        else DeactivateHotkeys();
    }

    private IEnumerable<SmartDevice> GetHotkeyAssignableDevices(IEnumerable<SmartDevice> devices)
    {
        var list = new List<SmartDevice>();
        foreach (var d in devices)
        {
            if (d.IsGroup && d.HasGroupSwitches)
                list.Add(d);
            else if (string.Equals(d.Kind, "SmartSwitch", StringComparison.OrdinalIgnoreCase))
                list.Add(d);
                
            if (d.IsGroup && d.Children != null)
                list.AddRange(GetHotkeyAssignableDevices(d.Children));
        }
        return list;
    }

    private bool _hotkeysActive;

    private void UpdateHotkeyButtonUi()
    {
        if (BtnHotkeys == null) return;

        if (_hotkeysActive)
        {
            BtnHotkeys.Content = "Hotkeys active";
            BtnHotkeys.Background = new SolidColorBrush(Color.FromRgb(255, 204, 0));   // gelb
            BtnHotkeys.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 224, 64));
            BtnHotkeys.Foreground = Brushes.Black;
        }
        else
        {
            BtnHotkeys.Content = "Hotkeys";
            BtnHotkeys.ClearValue(Button.BackgroundProperty);
            BtnHotkeys.ClearValue(Button.BorderBrushProperty);
            BtnHotkeys.ClearValue(Button.ForegroundProperty);
        }
    }

    private void DeactivateHotkeys()
    {
        _hotkeyMgr?.UnregisterAll();
        _hotkeysActive = false;
        UpdateHotkeyButtonUi();
    }

    // pruned-Register + Flag setzen
    private void ActivateHotkeysForCurrentServer()
    {
        if (_hotkeyMgr == null) return;

        PruneEmptyGesturesForCurrentServer();

        _hotkeyMgr.UnregisterAll();
        var map = MapForCurrentServer();
        bool any = false;
        foreach (var gesture in map.Keys)
            any |= _hotkeyMgr.Register(gesture);

        _hotkeysActive = any;
        UpdateHotkeyButtonUi();
    }

    // MAP DRAW OVERLAY

    // DTOs moved to Models/SharingModels.cs
    public void ReloadApplicationData()
    {
        _vm.Load();
        LoadCustomCrosshairs();
        HydrateSteamUiFromStorage();
        
        // Re-read FCM configuration from the restored rustplusjs-config.json
        TrackingService.ReadFcmConfig();
        
        // Notify the login overlay to update its visibility state!
        _vm.NotifyFcmChanged();
        
        AppendLog("[SYSTEM] Application data reloaded successfully after restore.");
    }
}

public class RenameDialog : Window
{
    public string InputText { get; private set; }
    public RenameDialog(string defaultText)
    {
        Title = "Rename Custom Crosshair";
        Width = 300; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(24, 26, 30));
        Foreground = Brushes.White;

        var grid = new StackPanel { Margin = new Thickness(15) };
        var tb = new TextBox 
        { 
            Text = defaultText, 
            Margin = new Thickness(0,0,0,15),
            Background = new SolidColorBrush(Color.FromRgb(51, 51, 51)),
            Foreground = Brushes.White,
            Padding = new Thickness(5),
            BorderThickness = new Thickness(0)
        };
        var btn = new Button 
        { 
            Content = "OK", 
            Width = 80, 
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
            Foreground = Brushes.White,
            Padding = new Thickness(5,2,5,2),
            BorderThickness = new Thickness(0)
        };
        btn.Click += (s, e) => { InputText = tb.Text; DialogResult = true; Close(); };
        tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) { InputText = tb.Text; DialogResult = true; Close(); } };
        
        grid.Children.Add(tb);
        grid.Children.Add(btn);
        Content = grid;
        
        Loaded += (s, e) => { tb.Focus(); tb.SelectAll(); };
    }
}

