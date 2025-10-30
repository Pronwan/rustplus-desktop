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
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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


namespace RustPlusDesk.Views;



public partial class MainWindow : Window
{

    private readonly MainViewModel _vm = new();
    private readonly SteamLoginService _steam = new();
    private DateTime _lastPairingPingAt = DateTime.MinValue;
    private readonly IRustPlusClient _rust;  // Interface statt fester Klasse
    private WebView2? _webView;
    private IPairingListener _pairing;
    private readonly Dictionary<uint, DateTime> _entityPairSeen = new();
    private bool _pairingStarting;
    private string? _lastPairSig;
    private bool _listenerWired; // damit Events nur einmal verdrahtet werden
    private bool _listenerStarting; // Schutz gegen Doppelklicks
    private readonly System.Windows.Threading.DispatcherTimer _statusTimer =
    new() { Interval = TimeSpan.FromSeconds(10) };
    // Chat-Inkrement-Marker pro Fenster
    private long _chatLastTicks = 0;          // zuletzt gesehener Timestamp (Ticks)
    private string? _chatLastKey = null;      // Fallback bei exakt gleichem Timestamp
    private int _statusBusy = 0;
    private ChatWindow? _chatWin;
    private CancellationTokenSource? _chatCts;
    private readonly HashSet<string> _chatSeenKeys = new(); // Dedup-Cache pro Session
    private Viewbox? _mapView;     // skaliert Inhalt Uniform in den Host
    private Grid? _scene;       // enthält Image + Overlay in Bildgröße (DIPs)                                                    // Zoom/Pan-State
    private bool _isPanning;
    private Point _panLast;
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

    // Camera thumbs: Throttling & "in-flight"-Wächter
    internal readonly HashSet<string> _camBusy = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<OverlayToolMode, Button> _toolButtons;
    private void BtnOpenCamera_Click(object sender, RoutedEventArgs e)
    {
        if (_rust is not RustPlusClientReal real) return;

        // simpler Prompt statt TextBox:
        var id = Microsoft.VisualBasic.Interaction.InputBox(
            "Camera identifier:", "Open camera", "");
        if (string.IsNullOrWhiteSpace(id)) return;

        var w = new RustPlusDesk.Views.CameraWindow(real, id) { Owner = this };
        w.Show();
        real.DebugDumpAppRequestShape();
    }

   


    private ObservableCollection<string> _cameraIds = new();
    private DispatcherTimer? _camThumbTimer;

    private void InitCameraUi()
    {
        BtnAddCam.Click += (_, __) =>
        {
            var input = Microsoft.VisualBasic.Interaction.InputBox("Camera identifier:", "Add camera", "");
            if (string.IsNullOrWhiteSpace(input)) return;
            if (_cameraIds.Any(s => string.Equals(s, input, StringComparison.OrdinalIgnoreCase))) return;

            _cameraIds.Add(input);   // _cameraIds == Selected.CameraIds
            _vm.Save();              // sofort persistieren
            RebuildCameraTiles();
            EnsureCamThumbPolling();
        };
    }




    private void RebuildCameraTiles()
    {
        CamItems.Items.Clear();
        foreach (var id in _cameraIds)
            CamItems.Items.Add(BuildCamTile(id));
    }
    // Wie „nah“ die Mini-Map um den Spieler herum zuschneidet (Anteil der Hauptkarte)
    private const double MINI_VIEW_FRACTION = 0.3; // 40% des sichtbaren Bereichs

    //>>
    private void CenterMiniMapOnPlayer()
    {
        if (_miniMap == null || !_miniMap.IsVisible) return;
        if (WebViewHost == null || Overlay == null) return;

        // Spielerposition besorgen
        var me = TeamMembers.FirstOrDefault(t => t.SteamId == _mySteamId) ?? TeamMembers.FirstOrDefault();
        if (me == null || !me.X.HasValue || !me.Y.HasValue) return;

        // Welt → Bild/Overlay-Koordinaten (so positionierst du auch deine Marker)
        Point pOverlay = WorldToImagePx(me.X.Value, me.Y.Value);

        // Overlay → Host-Koordinaten (inkl. ALLER Transforms/Letterboxing)
        GeneralTransform t = Overlay.TransformToVisual(WebViewHost);
        Point pHost = t.Transform(pOverlay);

        double hostW = Math.Max(1, WebViewHost.ActualWidth);
        double hostH = Math.Max(1, WebViewHost.ActualHeight);

        // Quadratischen Ausschnitt wählen (keine Verzerrung im runden Fenster)
        double side = Math.Min(hostW, hostH) * (MINI_VIEW_FRACTION * Math.Pow(GetEffectiveZoom(), 0.0025));

        // Um den Spieler zentrieren …
        double vx = pHost.X - side / 2.0;
        double vy = pHost.Y - side / 4.0;

        // … innerhalb des Hosts clampen
        vx = Math.Max(0, Math.Min(vx, hostW - side));
        vy = Math.Max(0, Math.Min(vy, hostH - side));

        _miniMap.SetViewbox(new Rect(vx, vy, side, side));
    }

    private MiniMapWindow? _miniMap;
    private VisualBrush? _miniMapBrush;
    // z.B. Click-Handler deines „Mini-Map“-Buttons:
    private void BtnToggleMiniMap_Click(object sender, RoutedEventArgs e)
    {
        if (_miniMap == null || !_miniMap.IsVisible)
        {
            // WICHTIG: mapRoot muss dein existierendes Karten-Root-Element sein!
            // Beispiele: SceneGrid, MapRootGrid, OverlayHostGrid – je nach deinem x:Name.
            var mapRoot = WebViewHost;
            var vb = new VisualBrush(WebViewHost)
            {
                // Wir schneiden selbst zu, daher:
                Stretch = Stretch.None,
                ViewboxUnits = BrushMappingMode.Absolute
            };
            _miniMapBrush = vb;


            _miniMap = new MiniMapWindow(mapRoot)
            {
                Owner = this,                         // optional
                Left = SystemParameters.WorkArea.Right - 280,
                Top = SystemParameters.WorkArea.Top + 20
            };

            _miniMap.Show();
            CenterMiniMapOnPlayer();

        }
        else
        {
            _miniMap.Close();
            _miniMap = null;
        }
    }

    private bool TryGetMyWorldPos(out double x, out double y)
    {
        x = y = 0;
        var me = TeamMembers.FirstOrDefault(t => t.SteamId == _mySteamId);
        if (me != null && me.X.HasValue && me.Y.HasValue)
        { x = me.X.Value; y = me.Y.Value; return true; }

        if (_lastPlayersBySid.TryGetValue(_mySteamId, out var p))
        { x = p.Item1; y = p.Item2; return true; }

        return false;
    }

    private FrameworkElement BuildCamTile(string id)
    {
        var root = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Margin = new Thickness(6)
        };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header: Name + Buttons
        var header = new DockPanel();
        var name = new TextBlock { Text = id, FontWeight = FontWeights.SemiBold };
        var spBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnOpen = new Button { Width = 16, Height = 16, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Open" };
        btnOpen.Content = new TextBlock { FontFamily = new FontFamily("Segoe MDL2 Assets"), Text = "\uE8A7" }; // E894
        btnOpen.Click += (_, __) =>
        {
            if (_rust is RustPlusClientReal real)
            {
                var w = new RustPlusDesk.Views.CameraWindow(real, id) { Owner = this };
                _camBusy.Add(id);
                w.Closed += (_, __2) => _camBusy.Remove(id);
                w.Show();
            }
        };

        var btnDel = new Button { Width = 16, Height = 16, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Delete" };
        btnDel.Content = new TextBlock { FontFamily = new FontFamily("Segoe MDL2 Assets"), Text = "" }; // E74D
        btnDel.Click += (_, __) =>
        {
            _cameraIds.Remove(id);
            _vm.Save();
            RebuildCameraTiles();
        };

        spBtns.Children.Add(btnOpen);
        spBtns.Children.Add(btnDel);
        DockPanel.SetDock(spBtns, Dock.Right);
        header.Children.Add(spBtns);
        header.Children.Add(name);
        Grid.SetRow(header, 0);

        // Thumb
        var img = new Image { Stretch = Stretch.UniformToFill, SnapsToDevicePixels = true, UseLayoutRounding = true, Height = 110, ClipToBounds = true };
        img.Tag = id; // damit der Thumb-Refresher weiß, wohin
        Grid.SetRow(img, 1);

        // Status-Zeile
        var status = new TextBlock { Opacity = 0.7, Margin = new Thickness(0, 4, 0, 0) };
        status.Tag = id + "|status";
        Grid.SetRow(status, 2);

        grid.Children.Add(header);
        grid.Children.Add(img);
        grid.Children.Add(status);
        root.Child = grid;
        return root;
    }

    private void EnsureCamThumbPolling()
    {
        _camThumbTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _camThumbTimer.Tick -= CamThumbTimer_Tick;
        _camThumbTimer.Tick += CamThumbTimer_Tick;
        _camThumbTimer.Start();
    }

    private int _camThumbIndex = 0;
    private int _camThumbBusy = 0;

    private async void CamThumbTimer_Tick(object? sender, EventArgs e)
    {
        if (!CamItems.IsVisible || _cameraIds.Count == 0) return;
        if (_rust is not RustPlusClientReal real) return;
        if (System.Threading.Interlocked.Exchange(ref _camThumbBusy, 1) == 1) return;

        try
        {
            if (_camThumbIndex >= CamItems.Items.Count) _camThumbIndex = 0;
            if (_camThumbIndex < 0 || _camThumbIndex >= CamItems.Items.Count) return;

            if (CamItems.Items[_camThumbIndex] is not FrameworkElement cont) return;
            _camThumbIndex++;

            var img = FindDescImage(cont);
            if (img == null) return;
            var id = img.Tag as string;
            if (string.IsNullOrWhiteSpace(id)) return;
            if (_camBusy.Contains(id)) return;   // hier pausieren, wenn live
            var status = FindStatus(cont, id);

            // 1) Node-Fallback zuerst (liefert in der Praxis am zuverlässigsten)
            var frame = await real.GetCameraFrameViaNodeAsync(id, timeoutMs: 6000);
            // 2) optional: klassischer Pfad als Zweitversuch
            if (frame?.Bytes == null)
                frame = await real.GetCameraFrameAsync(id);

            if (frame?.Bytes != null)
            {
                var bi = new BitmapImage();
                using var ms = new MemoryStream(frame.Bytes);
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit(); bi.Freeze();
                img.Source = bi;
                if (status != null) status.Text = (frame.Width > 0 && frame.Height > 0) ? $"{frame.Width}×{frame.Height}" : "snapshot";
            }
            else
            {
                if (status != null) status.Text = "no frame";
            }
        }
        catch (Exception ex)
        {
            // damit wir was sehen
            AppendLog("[cam] " + ex.Message);
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _camThumbBusy, 0);
        }

        static Image? FindDescImage(FrameworkElement root)
        {
            if (root is Image i) return i;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int k = 0; k < n; k++)
                if (VisualTreeHelper.GetChild(root, k) is FrameworkElement fe && FindDescImage(fe) is Image hit) return hit;
            return null;
        }
        static TextBlock? FindStatus(FrameworkElement root, string id)
        {
            var q = new Queue<DependencyObject>();
            q.Enqueue(root);
            while (q.Count > 0)
            {
                var x = q.Dequeue();
                if (x is TextBlock tb && (tb.Tag as string) == id + "|status") return tb;
                int n = VisualTreeHelper.GetChildrenCount(x);
                for (int i = 0; i < n; i++) q.Enqueue(VisualTreeHelper.GetChild(x, i));
            }
            return null;
        }
    }

    // generischer BFS-Finder im VisualTree
    private static T? FindDesc<T>(DependencyObject root, Func<T, bool>? predicate = null) where T : DependencyObject
    {
        var q = new Queue<DependencyObject>();
        q.Enqueue(root);
        while (q.Count > 0)
        {
            var x = q.Dequeue();
            if (x is T t && (predicate == null || predicate(t))) return t;
            int n = VisualTreeHelper.GetChildrenCount(x);
            for (int i = 0; i < n; i++) q.Enqueue(VisualTreeHelper.GetChild(x, i));
        }
        return null;
    }


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
    private System.Windows.Threading.DispatcherTimer? _teamTimer;
    public ObservableCollection<TeamMemberVM> TeamMembers { get; } = new();

    private int _teamBusy = 0;
    //------------- TEAM UI --------------------------
    private bool _iAmLeader = false;

    // optional: Avatar-Cache, damit wir nicht dauernd laden
    private readonly Dictionary<ulong, ImageSource> _avatarCache = new();
    // Zugriff auf die echte Client-Klasse
    private RustPlusClientReal? _real => _rust as RustPlusClientReal;

    // Liste, die das Team-Tab anzeigt

    public sealed class TeamMemberVM : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // Schlüssel / Stammdaten
        public ulong SteamId { get; init; }

        private string _name = "(player)";
        public string Name
        {
            get => _name;
            set { if (_name == value) return; _name = value; OnChanged(nameof(Name)); }
        }

        private bool _isLeader;
        public bool IsLeader
        {
            get => _isLeader;
            set { if (_isLeader == value) return; _isLeader = value; OnChanged(nameof(IsLeader)); }
        }

        private bool _isOnline;
        public bool IsOnline
        {
            get => _isOnline;
            set
            {
                if (_isOnline == value) return;
                _isOnline = value;
                OnChanged(nameof(IsOnline));
                OnChanged(nameof(IsOnlineAndAlive)); // abgeleitet
            }
        }

        private bool _isDead;
        public bool IsDead
        {
            get => _isDead;
            set
            {
                if (_isDead == value) return;
                _isDead = value;
                OnChanged(nameof(IsDead));
                OnChanged(nameof(IsOnlineAndAlive)); // abgeleitet
            }
        }

        // Abgeleitet: nur dann grün, wenn online und nicht tot
        public bool IsOnlineAndAlive => IsOnline && !IsDead;

        public double? X { get; set; }
        public double? Y { get; set; }

        private ImageSource? _avatar;
        public ImageSource? Avatar
        {
            get => _avatar;
            set { if (_avatar == value) return; _avatar = value; OnChanged(nameof(Avatar)); }
        }
    }

    // Letzte bekannten Player-Positionen (aus Dyn-Markern)
    private readonly Dictionary<ulong, (double x, double y, string name)> _lastPlayersBySid = new();
    // Merker für letzte Präsenz zum Melden von Änderungen
    private readonly Dictionary<ulong, (bool online, bool dead)> _lastPresence = new();

    // eigenes SteamId64 robust
    private ulong _mySteamId => (ulong.TryParse(_vm?.SteamId64, out var v) ? v : 0UL);


    private void StartTeamPolling()
    {
        if (_teamTimer != null) return; // schon aktiv
        _teamTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _teamTimer.Tick += TeamTimer_Tick;
        _teamTimer.Start();
    }

    private void StopTeamPolling()
    {
        var t = _teamTimer;
        if (t == null) return;
        t.Tick -= TeamTimer_Tick;
        t.Stop();
        _teamTimer = null;
    }

    private int _teamPollBusy = 0;
    private async void TeamTimer_Tick(object? sender, EventArgs e)
    {
        if (System.Threading.Interlocked.Exchange(ref _teamPollBusy, 1) == 1) return;
        try { await LoadTeamAsync(); }
        finally { System.Threading.Interlocked.Exchange(ref _teamPollBusy, 0); }
        CenterMiniMapOnPlayer();
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

    private static object? P(object? o, string name) =>
    o?.GetType().GetProperty(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(o);

    private static T Get<T>(object? o, T def, params string[] names)
    {
        foreach (var n in names)
        {
            var v = P(o, n);
            if (v is null) continue;
            try
            {
                if (typeof(T) == typeof(string)) return (T)(object)(v.ToString() ?? "");
                return (T)Convert.ChangeType(v, typeof(T), CultureInfo.InvariantCulture);
            }
            catch { }
        }
        return def;
    }

    // RAW Fallback for Team online info and Leader Info

    private async Task EnsureAvatarAsync(TeamMemberVM vm)
    {
        if (!_avatarLoading.Add(vm.SteamId)) return;
        try
        {
            // 1) Laden (nutzt DEINE vorhandene Methode)
            await LoadAvatarAsync(vm).ConfigureAwait(false);

            // 2) Wenn erfolgreich: Overlay sofort auf Avatar umschalten
            if (vm.Avatar != null)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    // Alle Marker dieses Spielers aktualisieren
                    foreach (var kv in _dynEls.ToList())
                    {
                        if (kv.Value is FrameworkElement fe &&
                            fe.Tag is PlayerMarkerTag t &&
                            t.SteamId == vm.SteamId)
                        {
                            var el = fe; // ref-Arg
                            UpdatePlayerMarker(ref el, kv.Key, vm.SteamId, vm.Name, vm.IsOnline, vm.IsDead);
                            // Skalierung bleibt konsistent:
                            ApplyCurrentOverlayScale(el);
                        }
                    }
                });
                // erfolgreicher Load -> Retry-Fenster löschen
                _avatarNextTry.Remove(vm.SteamId);
            }
            else
            {
                // kein Bild bekommen -> später nochmal probieren
                _avatarNextTry[vm.SteamId] = DateTime.UtcNow + AvatarRetryInterval;
            }
        }
        catch
        {
            // Fehler -> später nochmal probieren
            _avatarNextTry[vm.SteamId] = DateTime.UtcNow + AvatarRetryInterval;
        }
        finally
        {
            _avatarLoading.Remove(vm.SteamId);
        }
    }


    private async Task LoadTeamAsync()
    {
        if (_real is null) return;

        try
        {
            var team = await _real.GetTeamInfoAsync();
            if (team is null) return;

            var leaderId = team.LeaderSteamId;
            var seen = new HashSet<ulong>();

            foreach (var m in team.Members)
            {
                var sid = m.SteamId;
                if (sid == 0) continue;
                seen.Add(sid);

                var vm = TeamMembers.FirstOrDefault(t => t.SteamId == sid);
                if (vm == null)
                {
                    vm = new TeamMemberVM { SteamId = sid };
                    TeamMembers.Add(vm);
                    _ = LoadAvatarAsync(vm); // nutzt deine vorhandene Avatar-Funktion
                    if (vm.Avatar == null && CanTryAvatar(sid))
                    {
                        _ = EnsureAvatarAsync(vm); // siehe unten
                    }
                }

                // Vorheriger Zustand für Announcements
                var hadPrev = _lastPresence.TryGetValue(sid, out var prev);

                vm.Name = string.IsNullOrWhiteSpace(m.Name) ? "(player)" : m.Name!;
                vm.IsLeader = (leaderId != 0 && sid == leaderId);
                vm.IsOnline = m.Online;
                vm.IsDead = m.Dead;
                vm.X = m.X; vm.Y = m.Y;

                var now = (m.Online, m.Dead);
                _lastPresence[sid] = now;

                if (hadPrev && prev != now)
                    _ = AnnouncePresenceChangeAsync(vm, prev, now);

            }

            // Entferne nicht mehr vorhandene
            for (int i = TeamMembers.Count - 1; i >= 0; i--)
                if (!seen.Contains(TeamMembers[i].SteamId))
                    TeamMembers.RemoveAt(i);

            // Log hilft bei der Rechteprüfung LEADER etc.
            //  var iAmLeader = TeamMembers.Any(t => t.SteamId == _mySteamId && t.IsLeader);
            // AppendLog($"[team] leader={leaderId} me={_mySteamId} -> iAmLeader={iAmLeader} members={TeamMembers.Count}");
        }
        catch (Exception ex)
        {
            AppendLog("[team] " + ex.Message);
        }

        if (_overlayToolsVisible)
        {
            // UI muss auf UI-Thread aktualisiert werden
            await Dispatcher.InvokeAsync(() =>
            {
                RebuildOverlayTeamBar();
            });
        }

    }

    private async Task AnnouncePresenceChangeAsync(TeamMemberVM vm, (bool online, bool dead) prev, (bool online, bool dead) now)
    {
        try
        {
            // Online/Offline (wenn dieser Part auch vom Button abhängen soll, ebenfalls mit _announceSpawns verknüpfen)
            if (prev.online != now.online  && _announceSpawns )
            {
                var where = (vm.X.HasValue && vm.Y.HasValue) ? GetGridLabel(vm.X.Value, vm.Y.Value) : "unknown";
                var txt = now.online ? $"{vm.Name} came online @ {where}"
                                     : $"{vm.Name} went offline";
                await SendTeamChatSafeAsync(txt);
            }

            // Death/Respawn
            if (prev.dead != now.dead)
            {
                // Position robust bestimmen (immer versuchen, unabhängig vom Toggle)
                double? px = vm.X, py = vm.Y;
                if ((!px.HasValue || !py.HasValue) && TryResolvePosFromDynMarkers(vm.SteamId, out var dx, out var dy))
                { px = dx; py = dy; }

                // ---> Meldung nur senden, wenn Button aktiv
                if (_announceSpawns)
                {
                    var where = (px.HasValue && py.HasValue) ? GetGridLabel(px.Value, py.Value) : "unknown";
                    var txt = now.dead ? $"{vm.Name} died @ {where}" : $"{vm.Name} respawned @ {where}";
                    await SendTeamChatSafeAsync(txt);
                }

                // Death-Pin weiter unabhängig vom Toggle behandeln
                if (_showDeathMarkers && now.dead && px.HasValue && py.HasValue)
                {
                    PlaceOrMoveDeathPin(vm.SteamId, px.Value, py.Value, vm.Name);
                }
                else if (!now.dead)
                {
                    // RemoveDeathPins(vm.SteamId);
                }
            }
        }
        catch { }
    }



    // Avatar laden (ohne API-Key; via ?xml=1) + Cache
    private static readonly HttpClient _http = new HttpClient(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    });


    private static ImageSource? BytesToImage(byte[] bytes)
    {
        try
        {
            var bi = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    private static async Task<ImageSource?> FetchSteamAvatarAsync(ulong steamId)
    {
        if (steamId == 0) return null;
        try
        {
            using var http = new HttpClient();
            // 1) Avatar-URL aus dem XML-Profil ziehen (braucht keinen API-Key)
            var xml = await http.GetStringAsync($"https://steamcommunity.com/profiles/{steamId}?xml=1");
            string url = "";
            var mFull = Regex.Match(xml, @"<avatarFull><!\[CDATA\[(.*?)\]\]></avatarFull>", RegexOptions.IgnoreCase);
            var mMedium = Regex.Match(xml, @"<avatarMedium><!\[CDATA\[(.*?)\]\]></avatarMedium>", RegexOptions.IgnoreCase);
            if (mFull.Success) url = mFull.Groups[1].Value;
            else if (mMedium.Success) url = mMedium.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(url)) return null;

            // 2) Bild laden
            var bytes = await http.GetByteArrayAsync(url);
            return BytesToImage(bytes);
        }
        catch { return null; }
    }

    private async Task LoadAvatarAsync(TeamMemberVM vm)
    {
        try
        {
            if (vm.SteamId == 0 || vm.Avatar != null) return;

            if (_avatarCache.TryGetValue(vm.SteamId, out var cached) && cached != null)
            { vm.Avatar = cached; return; }

            var img = await FetchSteamAvatarAsync(vm.SteamId);
            if (img != null)
            {
                _avatarCache[vm.SteamId] = img;
                vm.Avatar = img;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[avatar] {vm.SteamId}: {ex.Message}");
        }
    }

    // Linksklick: auf Karte zentrieren
    private void TeamItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TeamMemberVM vm)
            CenterOnMember(vm);
    }

    // Kontextmenü – Zentrieren
    private void Team_Center_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TeamMemberVM vm)
            CenterOnMember(vm);
    }

    private void Team_OpenProfile_Click(object sender, RoutedEventArgs e)
    {
        var vm = VMFromSender(sender); if (vm == null) return;
        try
        {
            var url = $"https://steamcommunity.com/profiles/{vm.SteamId}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    // Kontextmenü – Promote
    private bool IAmLeaderNow() => TeamMembers.Any(t => t.SteamId == _mySteamId && t.IsLeader);
    private TeamMemberVM? VMFromSender(object sender)
        => (sender as FrameworkElement)?.DataContext as TeamMemberVM ?? TeamList?.SelectedItem as TeamMemberVM;

    private async void Team_Promote_Click(object sender, RoutedEventArgs e)
    {
        var vm = VMFromSender(sender); if (vm == null) return;
        if (!IAmLeaderNow()) { AppendLog("Only Leader can promote."); return; }
        if (vm.SteamId == _mySteamId) return;
        try { await (_real as RustPlusClientReal)?.PromoteToLeaderAsync(vm.SteamId); }
        catch (Exception ex) { AppendLog("[team] promote error: " + ex.Message); }
    }



    private void CenterOnMember(TeamMemberVM vm)
    {
        if (vm.X.HasValue && vm.Y.HasValue)
        {
            CenterMapOnWorld(vm.X.Value, vm.Y.Value);
            return;
        }
        if (TryResolvePosFromDynMarkers(vm.SteamId, out var x, out var y))
        {
            CenterMapOnWorld(x, y);
            return;
        }
        MessageBox.Show("Keine Position verfügbar (offline oder nicht gespawnt).");
    }

    private bool TryResolvePosFromDynMarkers(ulong sid, out double x, out double y)
    {
        if (_lastPlayersBySid.TryGetValue(sid, out var pos))
        {
            x = pos.x; y = pos.y; return true;
        }
        x = y = 0; return false;
    }

    // Kontextmenü – Kick
    private async void Team_Kick_Click(object sender, RoutedEventArgs e)
    {
        var vm = VMFromSender(sender); if (vm == null) return;
        if (!IAmLeaderNow()) { AppendLog("Only Leader can kick."); return; }
        if (vm.SteamId == _mySteamId) return;
        try { await (_real as RustPlusClientReal)?.KickTeamMemberAsync(vm.SteamId); }
        catch (Exception ex) { AppendLog("[team] kick error: " + ex.Message); }
    }
    public static class AppInfo
    {
        public static string VersionRaw =>
            Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()      // 4-teilig
            ?? "0.0.0";

        public static string VersionShort => Normalize(VersionRaw);

        public static Version VersionForCompare =>
            Version.TryParse(VersionShort, out var v) ? v : new Version(0, 0, 0);

        private static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "0.0.0";
            s = s.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s[1..];
            int cut = s.IndexOfAny(new[] { '-', '+' }); // "-rc1" oder "+commit"
            return cut > 0 ? s[..cut] : s;
        }
    }

    private static string ChatKey(TeamChatMessage m)
        => $"{m.Timestamp.ToUniversalTime().Ticks}|{m.Author.Trim()}|{m.Text.Trim()}";
    private BitmapSource? _mapBaseBmp; // Original-Map ohne Marker
    private readonly List<(double uPx, double vPx, string? label)> _staticMarkers = new();
    // SteamId -> Name Cache
    private readonly Dictionary<ulong, string> _steamNames = new();

    // throttle: nicht bei jedem Tick refreshe n
    private DateTime _lastTeamRefresh = DateTime.MinValue;
    public MainWindow()
    {
        // Nur freiwillig zum Diagnostizieren:
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        InitializeComponent();
        this.Title = $"RustPlusDesk v{AppInfo.VersionShort}";

        if (FindName("TxtVersion") is TextBlock txt)
            txt.Text = $"v{AppInfo.VersionShort}";
        InitCameraUi();
        _selectedMonitor = WinMonitors.All().Count > 0 ? WinMonitors.All()[0] : null;
        AppendLog($"[items-new] baseDir={baseDir}");
        EnsureNewItemDbLoaded();
        AppendLog($"[items-new] source={sNewDbSource} items={sItemsById.Count} byShort={sItemsByShort.Count}");
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
        DataContext = _vm;
        _vm.Load();
        // NEU: einmalig auf die aktuell ausgewählte Server-Instanz „umstecken“
        SwitchCameraSourceTo(_vm.Selected);

        // NEU: bei jedem späteren Serverwechsel Kameraliste umhängen
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Selected))
                SwitchCameraSourceTo(_vm.Selected);
        };

        // MapTransform.Changed += (_, __) => UpdateMarkerPositions();
        HydrateSteamUiFromStorage();   // <= HIER
        _statusTimer.Tick += async (_, __) => await UpdateServerStatusAsync();
        ListServers.ItemsSource = _vm.Servers;
        // Einmal erzeugen (falls du den Stub behalten willst: try/fallback – aber nur EINMAL zuweisen)

        _pairing = new PairingListenerRealProcess(AppendLog);

        _pairing.Paired += Pairing_Paired;

        // EINMALIG auf AlarmReceived hören:
        if (_pairing is PairingListenerRealProcess pr)
        {
            pr.AlarmReceived += (_, a) => Dispatcher.Invoke(() => ShowAlarmPopup(a));
        }

        // Status → UI
        _pairing.Listening += (_, __) => Dispatcher.Invoke(() =>
        {
            _vm.IsBusy = false; _vm.BusyText = "";
            _vm.IsPairingRunning = true;
            TxtPairingState.Text = "Pairing: listening…";
        });
        _pairing.Stopped += (_, __) => Dispatcher.Invoke(() =>
        {
            _vm.IsPairingRunning = false;
            TxtPairingState.Text = "Pairing: stopped";
        });
        _pairing.Failed += (_, msg) => Dispatcher.Invoke(() =>
        {
            _vm.IsBusy = false; _vm.BusyText = "";
            _vm.IsPairingRunning = false;
            TxtPairingState.Text = "Pairing: error";
            AppendLog("[listener] " + msg);
        });


        _rust = new RustPlusClientReal(AppendLog);

        if (_rust is RustPlusClientReal real)
        {

            real.DeviceStateEvent += async (id, isOn, kindFromApi) =>
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    var dev = _vm.Selected?.Devices?.FirstOrDefault(d => d.EntityId == id);
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
                        if (isOn)
                        {
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
                        }
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


        TxtSteamId.Text = string.IsNullOrEmpty(_vm.SteamId64) ? "(nicht angemeldet)" : _vm.SteamId64;

        this.Closing += MainWindow_Closing;
        _ = EnsureWebView2Async();
        ClearAllToggleBusy();
        this.Closed += MainWindow_Closed;

        _toolButtons = new Dictionary<OverlayToolMode, Button>
    {
        { OverlayToolMode.Draw,  ToolDrawButton },
        { OverlayToolMode.Text,  ToolTextButton },
        { OverlayToolMode.Icon,  ToolIconButton },
        { OverlayToolMode.Erase, ToolEraseButton }
    };

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
    }

    private void HideOverlay()
    {
        if (_overlay != null)
        {
            _overlay.Close();    // statt Hide()
            _overlay = null;
        }
        _visible = false;
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
        var btn = (Button)sender;
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
        UpdateStyleChecks();
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
        foreach (var t in new[] { "GreenDot", "MiniGreen", "OpenCrossRG", "ThinRedCircle", "SquareDot", "MagentaDot", "MagentaOpenCross", "RangeLine" })
        {
            var mi = FindStyleItem(t);
            if (mi != null) mi.IsChecked = false;
        }
        var currentTag = _currentStyle.ToString(); // nutzt die Enum-Namen
        var cur = FindStyleItem(currentTag);
        if (cur != null) cur.IsChecked = true;
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

            if (_visible && _overlay != null)
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
    { 5, "pack://application:,,,/icons/cargo.png"  },
    { 6, "pack://application:,,,/icons/vendor.png"  },
    { 7, "pack://application:,,,/icons/blocked.png"   }, // Building areas
    { 8, "pack://application:,,,/icons/patrol.png" },
    { 9, "pack://application:,,,/icons/crate.png"  }, // alt crate id seen on some builds
    { 4, "pack://application:,,,/icons/ch47.png"   }, // optional safety
    { 2, "pack://application:,,,/icons/explosion.png"   }, // optional safety
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
        }
        catch (Exception ex)
        { }
    }

    private string ResolvePlayerName(RustPlusClientReal.DynMarker m)
    {
        // 1) schon befüllt?
        if (!string.IsNullOrWhiteSpace(m.Name)) return m.Name;
        if (!string.IsNullOrWhiteSpace(m.Label)) return m.Label;

        // 2) Cache nach steamId
        if (m.SteamId != 0 && _steamNames.TryGetValue(m.SteamId, out var n) && !string.IsNullOrWhiteSpace(n))
            return n;

        // 3) lazy refresh anstoßen (nicht blockierend)
        if (DateTime.UtcNow - _lastTeamRefresh > TimeSpan.FromSeconds(5))
            _ = RefreshTeamNamesAsync();

        return "(player)";
    }
    private void SwitchCameraSourceTo(ServerProfile? srv)
    {
        if (srv == null)
        {
            _cameraIds = new ObservableCollection<string>();
            RebuildCameraTiles();
            return;
        }

        srv.CameraIds ??= new ObservableCollection<string>();
        _cameraIds = srv.CameraIds;          // gleiche Instanz → eine Wahrheit
        RebuildCameraTiles();
        EnsureCamThumbPolling();
    }
    private async Task RefreshTeamNamesAsync()
    {
        _lastTeamRefresh = DateTime.UtcNow;

        if (_rust is not RustPlusClientReal real) return;

        try
        {
            var team = await real.GetTeamInfoAsync(); // <— deine bestehende Methode
            if (team?.Members != null)
            {
                foreach (var m in team.Members)
                {
                    if (m.SteamId != 0 && !string.IsNullOrWhiteSpace(m.Name))
                        _steamNames[m.SteamId] = m.Name!;
                }
            }
        }
        catch
        {
            // still ok
        }
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
    private static bool sNewDbLoaded = false;
    private static string sNewDbSource = "(unbekannt)";

    private static void EnsureNewItemDbLoaded()
    {
        if (sNewDbLoaded) return;

        sItemsById.Clear();
        sItemsByShort.Clear();
        sNewDbSource = "(unbekannt)";

        bool loaded = false;

        // 1) Disk-Kandidaten
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string currDir = Environment.CurrentDirectory;
        string? entryDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly()!.Location);

        var diskCandidates = new[]
        {
        System.IO.Path.Combine(baseDir, "rust-item-list.json"),
        System.IO.Path.Combine(currDir, "rust-item-list.json"),
        entryDir is null ? null : System.IO.Path.Combine(entryDir, "rust-item-list.json"),
        // häufige Ordner:
        System.IO.Path.Combine(baseDir, "assets", "rust-item-list.json"),
        System.IO.Path.Combine(baseDir, "data",   "rust-item-list.json"),
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
            $"pack://application:,,,/{asmName};component/rust-item-list.json",
            $"pack://application:,,,/{asmName};component/assets/rust-item-list.json",
            $"pack://application:,,,/{asmName};component/data/rust-item-list.json",
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
            FontSize = 13
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

    // Eine kompakte, einzeilige Offer-Row für die Suchliste (16x16-Icons)
    private FrameworkElement BuildOfferRowSearchUI(RustPlusClientReal.ShopOrder o, bool compact)
    {
        bool outOfStock = o.Stock <= 0;

        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // L-Icon
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // L-Text
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // Spacer
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // R-Panel

        // left icon (item)
        var leftIcon = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) };
        BindIcon(leftIcon, o.ItemShortName, o.ItemId);
        Grid.SetColumn(leftIcon, 0);
        grid.Children.Add(leftIcon);

        // ---------- LEFT: name + (xN) + stock ----------
        var leftText = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        if (o.Quantity > 1)
        {
            leftText.Children.Add(new TextBlock
            {
                Text = $"x{o.Quantity} ",
                Foreground = SearchSubtle,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        // Voller Name ermitteln …
        var fullItemName = ResolveItemName(o.ItemId, o.ItemShortName);
        // … im compact-Modus kürzen
        var displayItemName = compact ? Shorten(fullItemName, 12) : fullItemName;

        var itemNameTb = new TextBlock
        {
            Text = displayItemName,
            Foreground = SearchText,
            VerticalAlignment = VerticalAlignment.Center
        };
        // Tooltip mit vollem Namen, damit man’s bei Bedarf sieht
        if (compact && displayItemName != fullItemName)
            ToolTipService.SetToolTip(itemNameTb, fullItemName);

        leftText.Children.Add(itemNameTb);

        leftText.Children.Add(new TextBlock
        {
            Text = $"   ·   Stock {o.Stock}",
            Foreground = SearchSubtle,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(leftText, 1);
        grid.Children.Add(leftText);

        // ---------- RIGHT: currency + name + amount ----------
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(new TextBlock { Text = "→  ", Foreground = SearchSubtle, VerticalAlignment = VerticalAlignment.Center });

        var curIcon = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) };
        BindIcon(curIcon, o.CurrencyShortName, o.CurrencyItemId);
        right.Children.Add(curIcon);

        var fullCurrencyName = ResolveItemName(o.CurrencyItemId, o.CurrencyShortName);
        var displayCurrencyName = compact ? Shorten(fullCurrencyName, 12) : fullCurrencyName;

        var curNameTb = new TextBlock
        {
            Text = displayCurrencyName + " ",
            Foreground = SearchText,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (compact && displayCurrencyName != fullCurrencyName)
            ToolTipService.SetToolTip(curNameTb, fullCurrencyName);

        right.Children.Add(curNameTb);

        right.Children.Add(new TextBlock
        {
            Text = o.CurrencyAmount.ToString(),
            Foreground = SearchText,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (outOfStock) grid.Opacity = 0.7;

        Grid.SetColumn(right, 3);
        grid.Children.Add(right);

        return grid;
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
    { "stone quarry",            "pack://application:,,,/icons/stonequarry.png" },
    { "hqm quarry",              "pack://application:,,,/icons/hqmquarry.png" },
    { "sulfur quarry",           "pack://application:,,,/icons/sulfurquarry.png" },
    { "excavator",               "pack://application:,,,/icons/excavator.png" },
    { "train tunnel",            "pack://application:,,,/icons/traintunnel2.png" },
    { "train tunnel link",       "pack://application:,,,/icons/traintunnel.png" },
    { "supermarket",             "pack://application:,,,/icons/supermarket.png" },
    { "abandoned military base", "pack://application:,,,/icons/militarybase.png" },
    { "large fishing village",   "pack://application:,,,/icons/fishingvillagelarge.png" },
    { "power plant",             "pack://application:,,,/icons/powerplant.png" },
    { "mining outpost",          "pack://application:,,,/icons/miningoutpost.png" },
    { "military tunnel",         "pack://application:,,,/icons/militarytunnel.png" },
    { "gas station",             "pack://application:,,,/icons/gasstation.png" },
    { "arctic base",             "pack://application:,,,/icons/arcticresearch.png" },
    { "sewer branch",            "pack://application:,,,/icons/sewerbranch.png" },
    { "airfield",                "pack://application:,,,/icons/airfield.png" },
    { "radtown",                 "pack://application:,,,/icons/radtown.png" },
    { "stables a",               "pack://application:,,,/icons/stable.png" },
    { "stables b",               "pack://application:,,,/icons/barn.png" },
    { "dome",                    "pack://application:,,,/icons/dome.png" },
    { "harbor",                  "pack://application:,,,/icons/harbour.png" },
    { "harbor 2",                "pack://application:,,,/icons/harbour2.png" },
    { "lighthouse",              "pack://application:,,,/icons/lighthouse.png" },
    { "fishing village",         "pack://application:,,,/icons/fishingvillage.png" },
    { "missile silo",            "pack://application:,,,/icons/missilesilo.png" },
    { "ferry terminal",          "pack://application:,,,/icons/ferryterminal.png" },
    { "train yard",              "pack://application:,,,/icons/trainyard.png" },
    { "satellite dish",          "pack://application:,,,/icons/satellitedish.png" },
    { "outpost",                 "pack://application:,,,/icons/outpost.png" },
    { "launch site",             "pack://application:,,,/icons/launchsite.png" },
    { "water treatment plant",   "pack://application:,,,/icons/watertreatment.png" },
    { "large oil rig",           "pack://application:,,,/icons/largeoilrig.png" },
    { "small oil rig",           "pack://application:,,,/icons/oilrig.png" },
    { "underwater lab",          "pack://application:,,,/icons/underwater.png" },
    { "underwater lab b",          "pack://application:,,,/icons/underwater.png" },
    { "junkyard",                "pack://application:,,,/icons/junkyard.png" },
    { "bandit camp",             "pack://application:,,,/icons/banditcamp.png" },
    { "swamp",                   "pack://application:,,,/icons/swamp.png" },
    { "jungle ziggurat",         "pack://application:,,,/icons/jungle.png" },
};

    private static double CalcOverlayScale(double effZoom, double exp, double baseMult = 1.0)
    {
        // Gegen-Skalierung (1 / effZoom^exp) + Baseline + Clamp
        var s = Math.Pow(effZoom, -exp) * baseMult;
        return Math.Clamp(s, ICON_SCALE_MIN, ICON_SCALE_MAX);
    }
    private Popup? _shopsHoverPopup;
    private WrapPanel? _shopsHoverWrap;
    private readonly HashSet<FrameworkElement> _shopIconSet = new(); // alle Icon-Roots
    private void ApplyMonumentScale(FrameworkElement el)
    {
        if (el == null) return;
        double eff = GetEffectiveZoom();               // dein eff ist perfekt
        double scale = CalcOverlayScale(eff, MON_SIZE_EXP, MON_BASE_MULT);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = new ScaleTransform(scale, scale);
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
        // Variante A/B/C beim Tooltip behalten – vorher sichern:
        var low = raw?.ToLowerInvariant() ?? "";
        if (System.Text.RegularExpressions.Regex.IsMatch(low, @"\s+a\s*$")) variant = "A";
        else if (System.Text.RegularExpressions.Regex.IsMatch(low, @"\s+b\s*$")) variant = "B";
        else if (System.Text.RegularExpressions.Regex.IsMatch(low, @"\s+c\s*$")) variant = "C";

        return Canon(raw); // <- macht die eigentliche harte Arbeit
    }

    private static string Canon(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.ToLowerInvariant();

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


    private FrameworkElement BuildShopSearchCard(
    RustPlusClientReal.ShopMarker s,
    IEnumerable<RustPlusClientReal.ShopOrder> offers,
    bool compact)
    {
        var card = new Border
        {
            Background = SearchCardBg,
            BorderBrush = SearchCardBrd,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 6, 0, 6)
        };

        var root = new StackPanel { Orientation = Orientation.Vertical };
        card.Child = root;

        // header
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        head.Children.Add(new TextBlock
        {
            Text = CleanLabel(s.Label) ?? "Shop",
            Foreground = SearchText,
            FontWeight = FontWeights.SemiBold,
            FontSize = compact ? 13 : 14   // optional: mini tweak im compact mode
        });
        head.Children.Add(new TextBlock
        {
            Text = $"   [{GetGridLabel(s)}]",
            Foreground = SearchSubtle,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = compact ? 12 : 12
        });
        root.Children.Add(head);

        // lines – HIER compact WEITERGEBEN
        foreach (var o in offers)
            root.Children.Add(BuildOfferRowSearchUI(o, compact));

        // click → zur Position zentrieren (Zoom unverändert)
        card.MouseLeftButtonUp += (_, __) => CenterMapOnWorld(s.X, s.Y);

        return card;
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
    private static string ResolveItemName(int itemId, string? shortName)
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
    private readonly Queue<string> _chatSeenOrder = new();
    private const int ChatSeenCapacity = 600; // Ringpuffer-Größe (genug für lange Sessions)
    private DateTime? _lastChatTsForCurrentServer;
    private AlarmWindow? _alarmWin; // nicht AlarmPopupWindow
    private readonly ObservableCollection<AlarmNotification> _alarmFeed = new();
    private void ShowAlarmPopup(AlarmNotification n)
    {
        if (_alarmWin is null || !_alarmWin.IsLoaded)
        {
            _alarmWin = new AlarmWindow { Owner = this };
            _alarmWin.Closed += (_, __) => _alarmWin = null;
            _alarmWin.Show();
        }
        _alarmWin.Add(n);
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

    // Host (Maus) → Scene-VOR-MapTransform (Pivot-Koordinaten für ScaleAt)
    private Point HostToScenePreTransform(Point hostPt)
    {
        var (s, offX, offY) = GetViewboxScaleAndOffset();

        // erst Viewbox "abziehen"
        var p = new Point(
            (hostPt.X - offX) / s,
            (hostPt.Y - offY) / s);

        // dann MapTransform invertieren
        var m = MapTransform.Matrix;
        if (m.HasInverse)
        {
            m.Invert();
            p = m.Transform(p);
        }
        return p;
    }

    // Host-Delta → Scene-Delta (damit Panning pixelgenau folgt)
    private Vector HostDeltaToSceneDelta(Vector dHost)
    {
        var (s, _, _) = GetViewboxScaleAndOffset();

        // MapTransform skaliert zusätzlich; Translation findet in Scene-Einheiten statt
        var m = MapTransform.Matrix;
        double sx = m.M11, sy = m.M22;
        if (Math.Abs(sx) < 1e-9) sx = 1;
        if (Math.Abs(sy) < 1e-9) sy = 1;

        return new Vector(
            dHost.X / s,
            dHost.Y / s);
    }
    // erkennt "zusammengeklebte" Orders-Zeilen, die kein echter Shop-Name sind
    private static bool LooksLikeOrdersLabel(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.ToLowerInvariant();
        return t.Contains("item#") || t.Contains("curr#") || t.Contains("→") || t.Contains(";") || t.Contains("stock");
    }
    // kürzt Label & filtert Orders-Zeilen raus
    private static string? CleanLabel(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().Replace('\r', ' ').Replace('\n', ' ');
        if (LooksLikeOrdersLabel(s)) return null;     // nicht als Name verwenden
        if (s.Length > 48) s = s.Substring(0, 48) + "…";
        return s;
    }

    // Seen-Set pflegen (Ringpuffer)
    private void RememberChatKey(string key)
    {
        if (_chatSeenKeys.Contains(key)) return;
        _chatSeenKeys.Add(key);
        _chatSeenOrder.Enqueue(key);
        if (_chatSeenOrder.Count > ChatSeenCapacity)
            _chatSeenKeys.Remove(_chatSeenOrder.Dequeue());
    }

    private async void StatusTimer_Tick(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _statusBusy, 1) == 1) return;
        try
        {
            await UpdateServerStatusAsync();
        }
        finally { Interlocked.Exchange(ref _statusBusy, 0); }
    }
    private void BtnDeleteDevice_Click(object sender, RoutedEventArgs e)
    {
        if (ListDevices.SelectedItem is not SmartDevice d) return;
        if (!d.IsMissing)
        {
            MessageBox.Show("Only missing devices can be deleted.");
            return;
        }
        _vm.Selected?.Devices?.Remove(d);   // oder _vm.Devices.Remove(d) – je nach Variante
        _vm.Save();
        AppendLog($"Device #{d.EntityId} removed.");
    }
    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
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
            var p = ParseRustPlusLink(link);
            Pairing_Paired(this, new PairingPayload
            {
                Host = p.host,
                Port = p.port,
                SteamId64 = string.IsNullOrEmpty(_vm.SteamId64) ? p.playerId.ToString() : _vm.SteamId64,
                PlayerToken = p.playerToken.ToString(),
                ServerName = p.name
            });
            AppendLog("RustPlus-Link processed.");
        }
        catch (Exception ex)
        {
            AppendLog("RustPlus-Link-Error: " + ex.Message);
            MessageBox.Show("Unable to read RustPlus-Link: " + ex.Message);
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
        if (!int.TryParse(q["port"], out var port)) throw new ArgumentException("port fehlt/ungültig");

        var sidStr = q["playerId"] ?? q["playerid"] ?? throw new ArgumentException("playerId fehlt");
        if (!ulong.TryParse(sidStr, out var playerId)) throw new ArgumentException("playerId ungültig");

        var tokStr = q["playerToken"] ?? q["playertoken"] ?? q["token"] ?? throw new ArgumentException("playerToken fehlt");
        if (!int.TryParse(tokStr, out var token)) throw new ArgumentException("playerToken ungültig");

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

        // Merker updaten (damit der nächste echte Server-Ping als keepalive erkannt wird)
        _lastPairSig = sig;

        // >>> Entity-Pairings NIE über server+token wegfiltern!
        // Optional: sehr kurze Dedupe pro Entity (z.B. 5s), damit doppelte Toaster nicht doppelt adden
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
            var keySteam = string.IsNullOrEmpty(_vm.SteamId64) ? e.SteamId64 : _vm.SteamId64;

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

            // >>> Geräte zuverlässig hinzufügen/aktualisieren (Switch + Alarm)
            if (e.EntityId.HasValue)
            {
                // Art ableiten – du setzt EntityType bereits im Listener (SmartSwitch/SmartAlarm)
                var kind = string.IsNullOrWhiteSpace(e.EntityType) ||
                           e.EntityType!.Equals("server", StringComparison.OrdinalIgnoreCase)
                           ? (e.EntityName?.IndexOf("alarm", StringComparison.OrdinalIgnoreCase) >= 0 ? "SmartAlarm" : "SmartSwitch")
                           : e.EntityType;

                var dev = prof.Devices.FirstOrDefault(d => d.EntityId == e.EntityId.Value);
                if (dev is null)
                {
                    dev = new SmartDevice
                    {
                        EntityId = e.EntityId.Value,
                        Name = string.IsNullOrWhiteSpace(e.EntityName) ? (string.IsNullOrWhiteSpace(e.ServerName) ? "Smart Device" : e.ServerName) : e.EntityName,
                        Kind = kind
                    };
                    prof.Devices.Add(dev);
                    AppendLog($"Device added → {dev.Display}");
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(e.EntityName)) dev.Name = e.EntityName;
                    if (!string.IsNullOrWhiteSpace(kind)) dev.Kind = kind;
                    AppendLog($"Device updated → {dev.Display}");
                }
            }

            if (_vm.Selected != prof)
                _vm.Selected = prof;

            _vm.Save();
        });
    }


    private async Task StartPairingListenerUiAsync()
    {
        // Wenn schon gestartet: Busy sicherheitshalber runter + Status setzen
        if (_pairing.IsRunning)
        {
            _vm.IsBusy = false;
            _vm.BusyText = "";
            TxtPairingState.Text = "Pairing: listening…";
            AppendLog("Listener already running.");
            return;
        }
        if (_listenerStarting) return;

        try
        {
            _listenerStarting = true;
            _vm.IsBusy = true;
            _vm.BusyText = "Starting Pairing-Listener …";

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler onListen = (_, __) => tcs.TrySetResult(true);
            EventHandler<string> onFail = (_, __) => tcs.TrySetResult(false);

            _pairing.Listening += onListen;
            _pairing.Failed += onFail;

            await _pairing.StartAsync();

            // Kleines Safety-Timeout, damit das Overlay nie hängen bleibt
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(8000));
            bool ok = (completed == tcs.Task) && tcs.Task.Result;

            // Aufräumen
            _pairing.Listening -= onListen;
            _pairing.Failed -= onFail;

            _vm.IsBusy = false;
            _vm.BusyText = "";
            if (ok) TxtPairingState.Text = "Pairing: listening…";
        }
        finally
        {
            _listenerStarting = false;
        }
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
            _vm.IsBusy = false; _vm.BusyText = "";
            TxtPairingState.Text = "Pairing: listening…";
            AppendLog("Listener already running.");
            return;
        }
        if (_listenerStarting) return;

        try
        {
            _listenerStarting = true;
            _vm.IsBusy = true;
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

            _vm.IsBusy = false; _vm.BusyText = "";
            if (ok) TxtPairingState.Text = "Pairing: listening…";
        }
        finally { _listenerStarting = false; }
    }


    private void Real_Status(object? s, string st)
    {
        Dispatcher.Invoke(() =>
        {
            if (st == "starting") _vm.BusyText = "Starte Pairing-Listener …";
            else if (st == "listening") TxtPairingState.Text = "Pairing: listening…";
            else if (st == "error") TxtPairingState.Text = "Pairing: error";
        });
    }
    private void Real_Listening(object? s, EventArgs e)
    {
        Dispatcher.Invoke(() => TxtPairingState.Text = "Pairing: listening…");
    }
    private void Real_Failed(object? s, string msg)
    {
        Dispatcher.Invoke(() =>
        {
            TxtPairingState.Text = "Pairing: error";
            AppendLog("[listener] " + msg);
        });
    }

    private void OnListening(object? s, EventArgs e)
    {
        _vm.IsBusy = false;
        _vm.BusyText = "";
        TxtPairingState.Text = "Pairing: listening…";
    }

    private void OnFailed(object? s, string msg)
    {
        _vm.IsBusy = false;
        _vm.BusyText = "";
        TxtPairingState.Text = "Pairing: error";
        AppendLog("[listener] " + msg);
    }

    private void OnStatus(object? s, string st)
    {
        if (st == "starting") _vm.BusyText = "Starte Pairing-Listener …";
    }

    private void Server_Delete_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ServerProfile prof) return;
        var ok = MessageBox.Show(
            $"Server „{prof.Name}“ wirklich löschen?",
            "Löschen bestätigen", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;

        _vm.Servers.Remove(prof);
        _vm.Save();
        AppendLog($"Server gelöscht: {prof.Name}");
    }


    private async void BtnListenPairing_Click(object sender, RoutedEventArgs e)
    {
        if (_listenerStarting || _pairing.IsRunning) { AppendLog("Listener läuft bereits."); return; }
        await StartPairingListenerUiAsync();
    }



    private void AppendLog(string line)
    {
        Dispatcher.Invoke(() =>
        {
            TxtLog.AppendText(line + Environment.NewLine);
            TxtLog.ScrollToEnd();
        });
    }

    private async Task EnsureWebView2Async()
    {
        var dataFolder = IOPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RustPlusDesk", "WebView2");
        Directory.CreateDirectory(dataFolder);

        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: dataFolder);
        _webView = new WebView2();
        WebViewHost.Background = (Brush)FindResource("SurfaceAlt");
        WebViewHost.Children.Add(_webView);
        Panel.SetZIndex(_webView, 0);           // WebView standardmäßig unten

        await _webView.EnsureCoreWebView2Async(env);
        _webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;

        // Optional: etwas „normaleren“ UA setzen
        _webView.CoreWebView2.Settings.UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
        _webView.NavigationCompleted += WebView_NavigationCompleted;
    }

    private async void BtnSteamLogin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Fallback nutzen:
            var loopback = new SteamOpenIdLoopbackService();
            var sid = await loopback.SignInAsync(); // ggf. Port anpassen
            _vm.SteamId64 = sid;
            TxtSteamId.Text = sid;
            AppendLog($"Steam angemeldet (Loopback): {sid}");
            _vm.Save();
            HydrateSteamUiFromStorage();   // Label, Button, Avatar aktualisieren
        }
        catch (Exception ex)
        {
            MessageBox.Show("Steam-Login fehlgeschlagen: " + ex.Message);
        }
    }
    private async Task UpdateServerStatusAsync()
    {
        try
        {
            if (_rust is RustPlusClientReal real && _vm.Selected?.IsConnected == true)
            {
                var st = await real.GetServerStatusAsync();
                if (st != null)
                {
                    // st ist eine Klasseninstanz → direkt auf die Properties zugreifen
                    _vm.ServerPlayers = (st.Players >= 0 && st.MaxPlayers >= 0)
                        ? $"{st.Players}/{st.MaxPlayers}" : "–";

                    _vm.ServerQueue = (st.Queue >= 0)
                        ? st.Queue.ToString()
                        : "–";

                    _vm.ServerTime = string.IsNullOrWhiteSpace(st.TimeString)
                        ? "–"
                        : st.TimeString;

                    //_vm.ServerWipe = st.WipeUtc?.ToLocalTime().ToString("dd.MM.yyyy") ?? "–";
                }
            }
        }
        catch
        {
            // leise weiter – der Poll läuft einfach erneut
        }
    }
    private void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_webView?.Source is null) return;
        var url = _webView.Source.ToString();
        if (_steam.TryExtractSteamId64FromReturnUrl(url, out var sid))
        {
            _vm.SteamId64 = sid;
            TxtSteamId.Text = sid;
            AppendLog($"Steam angemeldet: {sid}");

            // optional: gleich speichern
            _vm.Save();
        }
    }

    private void BtnAddServer_Click(object sender, RoutedEventArgs e)
    {
        var host = Microsoft.VisualBasic.Interaction.InputBox("Server IP/Host:", "Server hinzufügen", "127.0.0.1");
        var portStr = Microsoft.VisualBasic.Interaction.InputBox("Companion-Port:", "Server hinzufügen", "28082");
        var token = Microsoft.VisualBasic.Interaction.InputBox("Player-Token (Rust+):", "Server hinzufügen", "");
        var proxy = Microsoft.VisualBasic.Interaction.InputBox("Facepunch-Proxy verwenden? (y/n)", "Server hinzufügen", "n");

        if (int.TryParse(portStr, out var port) && !string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(token))
        {
            var prof = new ServerProfile
            {
                Name = $"{host}:{port}",
                Host = host,
                Port = port,
                SteamId64 = _vm.SteamId64,
                PlayerToken = token,
                UseFacepunchProxy = proxy.Trim().ToLowerInvariant().StartsWith("y")
            };
            _vm.AddServer(prof);
            _vm.Save();
        }
        else
        {
            MessageBox.Show("Ungültige Eingaben.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }



    private void ListServers_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {

        _lastChatTsForCurrentServer = null;
        _chatSeenKeys.Clear();
        // Aus der aktuellen Auswahl im VM lesen
        if (_vm.Selected is { } prof && !string.IsNullOrWhiteSpace(prof.SteamId64))
            _vm.SteamId64 = prof.SteamId64;

        HydrateSteamUiFromStorage();   // Label/Avatar aktualisieren
        // absichtlich leer – Binding Selected → CurrentDevices übernimmt das Umschalten
        RegisterAllHotkeys();
        ActivateHotkeysForCurrentServer();
    }

    static readonly JsonSerializerOptions JsonOpt = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };


    private async Task PollServerStatusLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_rust is RustPlusClientReal real)
                {
                    var st = await real.GetServerStatusAsync(ct);
                    if (st != null)
                    {
                        _vm.ServerPlayers = (st.Players >= 0 && st.MaxPlayers >= 0)
                            ? $"{st.Players}/{st.MaxPlayers}" : "–";

                        _vm.ServerQueue = (st.Queue >= 0) ? st.Queue.ToString() : "–";
                        _vm.ServerTime = string.IsNullOrWhiteSpace(st.TimeString) ? "–" : st.TimeString;
                    }
                }
            }
            catch { /* leise weiterversuchen */ }

            try { await Task.Delay(TimeSpan.FromSeconds(10), ct); } catch { }
        }
    }

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        // stop polls from previous server
        _shopTimer?.Stop();
        _shopTimer = null;
        StopDynPolling();
        StopTeamPolling();
        TeamMembers.Clear();
        _avatarCache.Clear();
        _lastPresence.Clear();
        ClearAllDeathPins();
        ClearAllToggleBusy();
        // Shopspezifische State-Tracker leeren:
        _lastShops.Clear();
        _shopLifetimes.Clear();
        _knownShopIds.Clear();
        _initialShopSnapshotTimeUtc = DateTime.MinValue;
        // Alerts rebaselinen beim nächsten Poll
        _alertsNeedRebaseline = true;
        // Rate-Limiter zurücksetzen
        _lastChatSendUtc = DateTime.MinValue;

        // Overlay auf der Map leeren, weil die alten Shop-UI-Elemente vom alten Server sind:
        foreach (var el in _shopEls.Values)
            Overlay.Children.Remove(el);
        _shopEls.Clear();
        

        //if (_shopTimer =  _shopTimer.Stop();
        // StopDynPolling();

        if (_vm.Selected is null)
        {
            MessageBox.Show("Please chose a server.");
            return;
        }

        try
        {
            _vm.IsBusy = true;
            _vm.BusyText = "Connecting …";

            AppendLog($"Connecting to ws://{_vm.Selected.Host}:{_vm.Selected.Port} …");
            await _rust.ConnectAsync(_vm.Selected);
            _vm.Selected.IsConnected = true;
            AppendLog("Verbunden.");

            // EINMAL casten und überall dieselbe Variable verwenden
            var real = _rust as RustPlusClientReal;

            // Chat-Marker reset
            _lastChatTsForCurrentServer = null;
            _chatSeenKeys.Clear();

            // (1) Chat-Historie einmalig + Live-Stream primen (keine Event-Hooks hier)
            if (real != null)
            {
                try
                {
                    var hist = await real.GetTeamChatHistoryAsync(null, 200);
                    int added = 0;
                    foreach (var m in hist.OrderBy(x => x.Timestamp))
                    {
                        var key = $"{m.Timestamp.Ticks}|{m.Author}|{m.Text}";
                        if (_chatSeenKeys.Add(key))
                        {
                            _chatWin?.AddIncoming(m.Author, m.Text, m.Timestamp);
                            if (!_lastChatTsForCurrentServer.HasValue || m.Timestamp > _lastChatTsForCurrentServer.Value)
                                _lastChatTsForCurrentServer = m.Timestamp;
                            added++;
                        }
                    }
                    AppendLog($"[chat] history loaded: {added} items.");
                    await real.PrimeTeamChatAsync(); // Live-Events freischalten
                }
                catch (Exception ex)
                {
                    AppendLog("[chat] history error: " + ex.Message);
                }
            }

            // (2) Overlay schließen, Map laden, Pairing-Listener starten
            _vm.IsBusy = false;
            _vm.BusyText = "";
            await LoadMapAsync();
            await StartPairingListenerUiAsync();

            // 3. altes User-Zeug aus vorherigem Server entfernen
            ClearUserOverlayElements();

            // 4. Team-Overlay-Layer auch leeren (optional aber sinnvoll,
            //    damit du nicht die PNG vom alten Server siehst)
            // _visibleOverlayOwners.Clear();
            // foreach (var img in _playerOverlayElements.Values)
            //  {
            //     Overlay.Children.Remove(img);
            //  }
            //   _playerOverlayElements.Clear();
            // 5. eigenes Overlay (falls vorhanden) aus JSON dieses Servers laden
            // sicherstellen, dass ich sichtbar bin
            _visibleOverlayOwners.Add(_mySteamId);
            // von Disk lesen
            LoadOverlayFromDiskForPlayer(_mySteamId);

            // (3) Geräte in-place rehydrieren (Collection-Instanz behalten)
            RehydrateDevicesFromStorageInto(_vm.Selected);
            _vm.NotifyDevicesChanged();
            AppendLog($"Devices rehydrated: {_vm.Selected.Devices?.Count ?? 0}");

            // (3b) Kameras rehydrieren und UI darauf umstecken
            RehydrateCamerasFromStorageInto(_vm.Selected);
            SwitchCameraSourceTo(_vm.Selected);
            AppendLog($"Cams rehydrated: {_vm.Selected.CameraIds?.Count ?? 0}");

            // (4) Subscriptions GENAU EINMAL primen (nachdem Devices gesetzt sind)
            if (real != null && _vm.Selected?.Devices?.Any() == true)
            {
                try
                {
                    await real.PrimeSubscriptionsAsync(_vm.Selected.Devices.Select(d => d.EntityId));
                    AppendLog($"PrimeSubscriptions: {_vm.Selected.Devices.Count} IDs.");
                }
                catch (Exception ex)
                {
                    AppendLog("PrimeSubscriptions Error: " + ex.Message);
                }
            }

            // (5) Status-Poll
            _statusCts?.Cancel();
            _statusCts = new CancellationTokenSource();
            _ = PollServerStatusLoopAsync(_statusCts.Token);
            await UpdateServerStatusAsync();
            _statusTimer.Start();

            // (6) Team-Polling starten
            StartTeamPolling();
            await LoadTeamAsync();
            if (_overlayToolsVisible)
            {
                RebuildOverlayTeamBar();
            }

        }
        catch (Exception ex)
        {
            _vm.IsBusy = false;
            _vm.BusyText = "";
            AppendLog("Fehler: " + ex.Message);
            MessageBox.Show($"Connection failed: {ex.Message}");
        }
    }

    // PLAYER DEATH MARKERS AVATAR IMAGE PLAYER DEATH

    private bool _showProfileMarkers = true;
    private bool _showDeathMarkers = false;

    // death pins per player
    private readonly Dictionary<ulong, FrameworkElement> _deathPins = new();




    private async Task<bool> EnsureConnectedAsync()
    {
        if (_vm.Selected is null) { AppendLog("No server selected."); return false; }
        if (_vm.Selected.IsConnected) return true;

        AppendLog($"Verbinde zu ws://{_vm.Selected.Host}:{_vm.Selected.Port} …");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        try
        {
            await _rust.ConnectAsync(_vm.Selected, cts.Token);
            _vm.Selected.IsConnected = true;
            AppendLog("Connected.");
            return true;
        }
        catch (Exception ex)
        {
            AppendLog("Connect failed: " + ex.Message);
            return false;
        }
    }
    private async void DeviceToggle_Click(object sender, RoutedEventArgs e)
    {
        lock (_toggleBusy)
        {
            foreach (var id in _toggleBusy.Where(kv => DateTime.UtcNow - kv.Value > ToggleBusyTTL).Select(kv => kv.Key).ToList())
                _toggleBusy.Remove(id);
        }
        if ((sender as ToggleButton)?.DataContext is not SmartDevice d) return;
        if (!await EnsureConnectedAsync()) { ((ToggleButton)sender).IsChecked = d.IsOn; return; }

        var desired = ((ToggleButton)sender).IsChecked == true;
        try
        {
            await _rust.ToggleSmartSwitchAsync(d.EntityId, desired);
            // Verify holst du dir ja schon – zur Sicherheit:
            d.IsOn = desired;
        }
        catch (Exception ex)
        {
            AppendLog("Control error: " + ex.Message);
            ((ToggleButton)sender).IsChecked = d.IsOn; // UI zurücksetzen
        }
    }


    private bool _suppressToggleHandler;
    private async void DeviceToggle_Checked(object sender, RoutedEventArgs e)
        => await HandleDeviceToggleAsync(sender, true);

    private async void DeviceToggle_Unchecked(object sender, RoutedEventArgs e)
        => await HandleDeviceToggleAsync(sender, false);

    // 1) Toggle-Handler bleibt so – ohne Refresh-Aufruf
    private static bool LooksLikeNotConnected(Exception ex)
    {
        var s = ex.Message?.ToLowerInvariant() ?? "";
        return ex is WebSocketException
            || ex is IOException
            || s.Contains("nicht verbunden")
            || s.Contains("not connected")
            || s.Contains("websocket")
            || s.Contains("closed")
            || s.Contains("aborted");
    }

    private async Task HandleDeviceToggleAsync(object sender, bool on)
    {
        if (_suppressToggleHandler) return;

        if ((sender as FrameworkElement)?.DataContext is not SmartDevice dev) return;
        if (!string.Equals(dev.Kind, "SmartSwitch", StringComparison.OrdinalIgnoreCase)) return;

        // Lock erst NACH erfolgreichem Connect setzen? → dein Flow kann bleiben,
        // ABER ganz wichtig: Timeout um das Toggle, damit der Task nicht hängt.
        if (!TryMarkToggleBusy(dev.EntityId))
        {
            AppendLog($"(skip) Toggle #{dev.EntityId} already in progress");
            return;
        }

        try
        {
            if (!await EnsureConnectedAsync()) return;

            if ((DateTime.UtcNow - _lastPairingPingAt).TotalMilliseconds < 1200)
            {
                AppendLog("Pairing ping just arrived – delaying toggle 1.2s …");
                await Task.Delay(1200);
            }

            AppendLog($"Sending {(on ? "ON" : "OFF")} to #{dev.EntityId} …");

            try
            {
                // *** WICHTIG: immer mit Timeout aufrufen ***
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await _rust.ToggleSmartSwitchAsync(dev.EntityId, on, cts.Token);
            }
            catch (OperationCanceledException)
            {
                AppendLog("Toggle timeout (8s).");
            }
            catch (Exception ex)
            {
                AppendLog($"{(on ? "ON" : "OFF")} Error: " + ex.Message);

                // Optional: einmaliger Reconnect-Retry bei „nicht verbunden“
                if (LooksLikeNotConnected(ex) && await EnsureConnectedAsync())
                {
                    using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                    try { await _rust.ToggleSmartSwitchAsync(dev.EntityId, on, cts2.Token); }
                    catch (Exception ex2) { AppendLog("Retry failed: " + ex2.Message); }
                }
            }
            finally
            {
                await RefreshDeviceStateAsync(dev);
            }
        }
        finally
        {
            UnmarkToggleBusy(dev.EntityId); // <<< wird garantiert ausgeführt
        }
    }


    // Ein Gerät *generisch* neu einlesen (wie der Info-Button).
    // - Für SmartSwitch: schneller Pfad über GetSmartSwitchStateAsync
    // - Für alle anderen (oder Fallback): ProbeEntityAsync (setzt auch Kind/IsMissing)
    private async Task RefreshDeviceStateAsync(SmartDevice dev, bool log = true)
    {
        if (_rust is not RustPlusClientReal real) return;

        // Fast-Path nur für echte Switches
        if (string.Equals(dev.Kind, "SmartSwitch", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var s = await real.GetSmartSwitchStateAsync(dev.EntityId);
                if (s is bool b)
                {
                    _suppressToggleHandler = true;
                    dev.IsOn = b;
                    _suppressToggleHandler = false;

                    if (log) AppendLog($"State #{dev.EntityId}: {(b ? "ON" : "OFF")}");
                    return;
                }
            }
            catch (Exception ex)
            {
                if (log) AppendLog("GetSmartSwitchStateAsync: " + ex.Message);
            }
        }

        // Generisch (SmartAlarm & Co.)
        try
        {
            var r = await _rust.ProbeEntityAsync(dev.EntityId);

            // Kind nur setzen, NIE SmartAlarm wegschreiben
            if (!string.IsNullOrWhiteSpace(r.Kind) &&
                !string.Equals(dev.Kind, "SmartAlarm", StringComparison.OrdinalIgnoreCase))
            {
                dev.Kind = r.Kind;
            }

            dev.IsMissing = !r.Exists;

            _suppressToggleHandler = true;
            if ((dev.Kind ?? r.Kind)?.Equals("SmartAlarm", StringComparison.OrdinalIgnoreCase) == true)
            {
                // ⚠️ Alarm: NICHT aus Probe übernehmen (liefert meist "armed=true")
                // Variante 1 (empfohlen): Anzeige so lassen wie sie ist
                // dev.IsOn = dev.IsOn;

                // Variante 2: bewusst auf INAKTIV zurücknehmen, wenn keine Auslösung läuft:
                dev.IsOn = false;
            }
            else
            {
                dev.IsOn = r.IsOn;
            }
            _suppressToggleHandler = false;

            if (log)
            {
                if (!r.Exists)
                    AppendLog($"#{dev.EntityId}: not reachable / demoved");
                else
                    AppendLog($"State #{dev.EntityId}: {(r.IsOn is bool b ? (b ? "ON" : "OFF") : "–")} ({r.Kind ?? "?"})");
            }
        }
        catch (Exception ex)
        {
            if (log) AppendLog("Probe-Error: " + ex.Message);
        }
    }




    private async void BtnOpenChat_Click(object sender, RoutedEventArgs e)
    {
        // 1) _rust vorhanden?
        if (_rust is not RustPlusClientReal real)
        {
            MessageBox.Show("Not connected.");
            return;
        }

        // 2) Schneller UI-Check: ist im ViewModel ein Server verbunden?
        if (!(_vm.Selected?.IsConnected ?? false))
        {
            MessageBox.Show("Please connect to a server first.");
            return;
        }

        // 3) Bevor wir ein Fenster öffnen: Events anmelden + Chat „primen“ testen.
        try
        {
            // doppelte Anmeldungen vermeiden
            real.TeamChatReceived -= Real_TeamChatReceived;
            real.TeamChatReceived += Real_TeamChatReceived;

            // wirft InvalidOperationException, wenn _api==null → dann kein Fenster öffnen
            await real.PrimeTeamChatAsync();
        }
        catch (InvalidOperationException) // "Nicht verbunden."
        {
            MessageBox.Show("Please connect to a server first.");
            return; // kein Fenster öffnen
        }
        catch (Exception ex)
        {
            AppendLog("PrimeChat failed: " + ex.Message);
            MessageBox.Show("Chat is not available right now.");
            return; // sicherheitshalber auch hier abbrechen
        }

        // 4) Ab hier sind wir „chat-ready“ → Fenster öffnen (einmalig)
        if (_chatWin == null || !_chatWin.IsLoaded)
        {
            _chatWin = new Views.ChatWindow(async msg => await real.SendTeamMessageAsync(msg))
            {
                Owner = this
            };
            _chatWin.Closed += (_, __) => _chatWin = null;
            _chatWin.Show();
        }

        // 5) History laden (tolerant)
        try
        {
            var history = await real.GetTeamChatHistoryAsync(_lastChatTsForCurrentServer, limit: 120);
            foreach (var m in history.OrderBy(m => m.Timestamp))
                AppendChatIfNew(m);

            if (history.Count > 0)
                _lastChatTsForCurrentServer = history.Max(x => x.Timestamp);
        }
        catch (Exception ex)
        {
            AppendLog("Chat-History Error: " + ex.Message);
        }
    }
    private void Real_TeamChatReceived(object? sender, TeamChatMessage m)
    {
        Dispatcher.Invoke(() => AppendChatIfNew(m));
    }

    private void AppendChatIfNew(TeamChatMessage m)
    {
        var key = ChatKey(m);
        if (_chatSeenKeys.Contains(key)) return;

        _chatSeenKeys.Add(key);
        _lastChatTsForCurrentServer =
            !_lastChatTsForCurrentServer.HasValue || m.Timestamp > _lastChatTsForCurrentServer.Value
            ? m.Timestamp : _lastChatTsForCurrentServer;

        _chatWin?.AddIncoming(m.Author, m.Text, m.Timestamp.ToLocalTime()); // Anzeige in Lokalzeit
    }

    private void OnTeamChatReceived(object? _, RustPlusDesk.Models.TeamChatMessage m)
    {
        if (_chatWin is null || !_chatWin.IsLoaded) return;
        Dispatcher.Invoke(() => _chatWin.AddIncoming(m.Author, m.Text, m.Timestamp));
    }

    private void Monuments_Checked(object sender, RoutedEventArgs e)
    {
        ToggleMonuments(true);
    }

    private void Monuments_Unchecked(object sender, RoutedEventArgs e)
    {
        ToggleMonuments(false);
    }
    private void ToggleMonuments(bool on)
    {
        _showMonuments = on;
        foreach (var fe in _monEls.Values)
            fe.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnChatReceived(object? sender, TeamChatMessage e)
    {
        // Nur anzeigen, wenn das Chatfenster offen ist
        if (_chatWin is { IsLoaded: true })
            _chatWin.AddIncoming(e.Author, e.Text);
    }

    private async void BtnDeviceRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null) { AppendLog("No Server Selected."); return; }
        if (!await EnsureConnectedAsync()) return;

        // NICHT ItemsSource im Code setzen – XAML-Binding soll aktiv bleiben!
        var list = _vm.Selected.Devices;
        if (list == null || list.Count == 0)
        {
            AppendLog("No Devices Available.");
            return;
        }

        AppendLog("Updating Device Status…");
        foreach (var d in list)
        {
            try
            {
                var r = await _rust.ProbeEntityAsync(d.EntityId);

                d.Kind = r.Kind ?? d.Kind;

                d.IsMissing = !r.Exists;
                if ((d.Kind ?? r.Kind)?.Equals("SmartAlarm", StringComparison.OrdinalIgnoreCase) == true)
                {
                    // Alarm: null => inaktiv deuten
                    d.IsOn = false;
                }
                else
                {
                    d.IsOn = r.IsOn;
                }

                if (!r.Exists)
                    AppendLog($"#{d.EntityId}: not reachable / removed");
                else
                    AppendLog($"#{d.EntityId} ({d.Kind ?? "?"}): {(r.IsOn is bool b ? (b ? "ON" : "OFF") : "–")}");
            }
            catch (Exception ex)
            {
                d.IsMissing = true;
                AppendLog($"#{d.EntityId}: Status Request Failed → {ex.Message}");
            }
        }

        // Kein Items.Refresh nötig, wenn SmartDevice INotifyPropertyChanged feuert (inkl. Display).
        _vm.Save();
        AppendLog("Refresh completed.");
    }

    private async void BtnDeviceInfo_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedDevice is null) { AppendLog("No Device Selected."); return; }
        if (!await EnsureConnectedAsync()) return;

        try
        {
            await RefreshDeviceStateAsync(_vm.SelectedDevice); // <-- einheitlicher Pfad
        }
        catch (Exception ex)
        {
            AppendLog("Info-Error: " + ex.Message);
        }
    }

    private void RehydrateCamerasFromStorageInto(ServerProfile current)
    {
        try
        {
            var all = StorageService.LoadProfiles();
            var saved = all.FirstOrDefault(p =>
                p.Host.Equals(current.Host, StringComparison.OrdinalIgnoreCase) &&
                p.Port == current.Port &&
                p.SteamId64 == current.SteamId64);

            current.CameraIds ??= new ObservableCollection<string>();

            if (saved?.CameraIds is { Count: > 0 })
            {
                foreach (var id in saved.CameraIds)
                    if (!current.CameraIds.Any(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase)))
                        current.CameraIds.Add(id);
            }
        }
        catch (Exception ex)
        {
            AppendLog("RehydrateCams-Error: " + ex.Message);
        }
    }

    private void RehydrateDevicesFromStorageInto(ServerProfile current)
    {
        try
        {
            var all = StorageService.LoadProfiles();
            var saved = all.FirstOrDefault(p =>
                p.Host.Equals(current.Host, StringComparison.OrdinalIgnoreCase) &&
                p.Port == current.Port &&
                p.SteamId64 == current.SteamId64);

            current.Devices ??= new();

            if (saved?.Devices is { Count: > 0 })
            {
                // 1) upsert
                foreach (var s in saved.Devices)
                {
                    var ex = current.Devices.FirstOrDefault(d => d.EntityId == s.EntityId);
                    if (ex == null)
                    {
                        current.Devices.Add(s);
                    }
                    else
                    {
                        ex.Name = s.Name;
                        ex.Kind = s.Kind;
                        ex.IsOn = s.IsOn;
                        // ⚠️ SmartAlarm nie als "true" aus Storage hochholen
                        if (string.Equals(ex.Kind, "SmartAlarm", StringComparison.OrdinalIgnoreCase))
                            ex.IsOn = false;
                        ex.IsMissing = s.IsMissing;
                        ex.Alias = s.Alias;
                    }
                }

                // 2) optional: entfernen, was im Storage nicht mehr existiert
                // for (int i = current.Devices.Count - 1; i >= 0; i--)
                //     if (!saved.Devices.Any(d => d.EntityId == current.Devices[i].EntityId))
                //         current.Devices.RemoveAt(i);
            }

            _vm.NotifyDevicesChanged();
        }
        catch (Exception ex)
        {
            AppendLog("Rehydrate-Error: " + ex.Message);
        }
    }

    private void HydrateSteamUiFromStorage()
    {
        // Falls ViewModel keine SteamID hat: aus gespeicherten Servern ableiten
        if (string.IsNullOrWhiteSpace(_vm.SteamId64))
        {
            var sid = _vm.Servers
                         .Select(s => s.SteamId64)
                         .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (!string.IsNullOrWhiteSpace(sid))
                _vm.SteamId64 = sid;
        }

        // Label setzen + Login-Button ggf. deaktivieren
        var sidText = string.IsNullOrWhiteSpace(_vm.SteamId64) ? "(not connected)" : _vm.SteamId64;
        TxtSteamId.Text = sidText;
        BtnSteamLogin.IsEnabled = string.IsNullOrWhiteSpace(_vm.SteamId64);

        // Avatar versuchen zu laden (nur wenn wir eine ID haben)
        _ = TryLoadSteamAvatarAsync(_vm.SteamId64);
    }

    private async Task TryLoadSteamAvatarAsync(string? steamId64)
    {
        if (string.IsNullOrWhiteSpace(steamId64))
        {
            ImgSteam.Source = null;
            ImgSteam.Visibility = Visibility.Collapsed;
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

                ImgSteam.Source = bmp;
                ImgSteam.Visibility = Visibility.Visible;
            }
            if (nameMatch.Success)
                ImgSteam.ToolTip = nameMatch.Groups[1].Value;
        }
        catch
        {
            // Avatar optional – bei Fehlern still
            ImgSteam.Source = null;
            ImgSteam.Visibility = Visibility.Collapsed;
        }
    }

    private void Device_Rename_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SmartDevice dev) return;

        var preset = string.IsNullOrWhiteSpace(dev.Alias) ? (dev.Name ?? "") : dev.Alias!;
        var input = PromptText(this, "Rename Device",
                               $"New name for #{dev.EntityId}:", preset);

        if (input == null) return;                   // Abgebrochen
        dev.Alias = string.IsNullOrWhiteSpace(input) ? null : input.Trim();
        _vm.Save();                                  // Profile inkl. Alias persistieren
    }
    private const double MIN_S = 1.0;   // nicht kleiner als "fit"
    private const double MAX_S = 8.0;
    // Mini-Prompt, keine zusätzlichen XAML-Dateien nötig
    private static string? PromptText(Window owner, string title, string message, string initial = "")
    {
        var win = new Window
        {
            Title = title,
            Width = 380,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false
        };

        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var tbMsg = new TextBlock { Text = message, Margin = new Thickness(0, 0, 0, 6) };
        var box = new TextBox { Text = initial, MinWidth = 300 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 100, IsCancel = true };

        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        Grid.SetRow(tbMsg, 0); grid.Children.Add(tbMsg);
        Grid.SetRow(box, 1); grid.Children.Add(box);
        Grid.SetRow(buttons, 2); grid.Children.Add(buttons);

        string? result = null;
        ok.Click += (_, __) => { result = box.Text; win.DialogResult = true; };
        win.Content = grid;

        return win.ShowDialog() == true ? result : null;
    }

    private void WebViewHost_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_scene == null) return;

        double zoom = e.Delta > 0 ? 1.10 : (1.0 / 1.10);

        // Pivot korrekt in Scene-VOR-Transform:
        var hostPos = e.GetPosition(WebViewHost);
        var pivot = HostToScenePreTransform(hostPos);

        var m = MapTransform.Matrix;
        m.ScaleAt(zoom, zoom, pivot.X, pivot.Y);
        MapTransform.Matrix = m;
        RefreshAllOverlayScales();
        RefreshMonumentOverlayPositions();
        CenterMiniMapOnPlayer();
        e.Handled = true;
    }



    // Welt→Bild (Pixel im Bildkoordinatensystem – vor Zoom/Pan)
    private const double PAD_WORLD = 2000.0; // exakt der Wert, den du zum Fit benutzt

    private Point WorldToImagePx(double x, double y)
    {
        // on-grid clamp – so wie bei den eingebrannten Monuments
        x = Math.Clamp(x, 0, _worldSizeS);
        y = Math.Clamp(y, 0, _worldSizeS);

        double u = _worldRectPx.X + (x / _worldSizeS) * _worldRectPx.Width;
        double v = _worldRectPx.Y + ((_worldSizeS - y) / _worldSizeS) * _worldRectPx.Height;
        return new Point(u, v);
    }

    // Weltkoordinate -> Bildpixel (vor Zoom/Pan), benutzt _worldRectPx/_worldSizeS


    // (optional) Rückweg, falls du ihn brauchst
    private Point ImagePxToWorld(double u, double v)
    {
        if (_worldSizeS <= 0 || _worldRectPx.Width <= 0 || _worldRectPx.Height <= 0) return new Point(0, 0);

        double x = (u - _worldRectPx.X) / _worldRectPx.Width * _worldSizeS;
        double y = _worldSizeS - (v - _worldRectPx.Y) / _worldRectPx.Height * _worldSizeS;
        return new Point(x, y);
    }

    private void ChkGrid_Checked(object sender, RoutedEventArgs e) => RedrawGrid();

    private void RedrawGrid()
    {
        GridLayer.Children.Clear();
        if (ChkGrid.IsChecked != true || _worldSizeS <= 0 || _worldRectPx.Width <= 0) return;

        // Zellen: 150 world units
        int cells = Math.Max(1, (int)Math.Round(_worldSizeS / 150.0));

        double ox = _worldRectPx.X, oy = _worldRectPx.Y;
        double ow = _worldRectPx.Width, oh = _worldRectPx.Height;
        double step = ow / cells;

        var stroke = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
        double thin = 1.0, thick = 2.0;

        // senkrecht
        for (int i = 0; i <= cells; i++)
        {
            double x = ox + i * step;
            var line = new System.Windows.Shapes.Line
            {
                X1 = x,
                Y1 = oy,
                X2 = x,
                Y2 = oy + oh,
                Stroke = stroke,
                StrokeThickness = (i % 5 == 0) ? thick : thin
            };
            GridLayer.Children.Add(line);
        }

        // waagrecht
        for (int j = 0; j <= cells; j++)
        {
            double y = oy + j * step;
            var line = new System.Windows.Shapes.Line
            {
                X1 = ox,
                Y1 = y,
                X2 = ox + ow,
                Y2 = y,
                Stroke = stroke,
                StrokeThickness = (j % 5 == 0) ? thick : thin
            };
            GridLayer.Children.Add(line);
        }

        // Labels (oben-links in jeder Zelle): "A0", "A1", ..., "Z25", "AA...", etc.
        for (int i = 0; i < cells; i++)
        {
            string col = ColumnLabel(i);
            for (int j = 0; j < cells; j++)
            {
                var tb = new TextBlock
                {
                    Text = $"{col}{j}",
                    Foreground = Brushes.White,
                    FontSize = 10,
                    Margin = new Thickness(2, 2, 0, 0),
                    Background = new SolidColorBrush(Color.FromArgb(96, 0, 0, 0)), // dezente Box
                    Padding = new Thickness(2, 0, 2, 0)
                };

                double x = ox + i * step + 1; // etwas Abstand von der Linie
                double y = oy + j * step + 1;

                GridLayer.Children.Add(tb);
                Canvas.SetLeft(tb, x);
                Canvas.SetTop(tb, y);
            }
        }
    }

    private static string ColumnLabel(int index)
    {
        // 0 -> A, 25 -> Z, 26 -> AA, …
        var s = "";
        index++;
        while (index > 0)
        {
            index--;
            s = (char)('A' + (index % 26)) + s;
            index /= 26;
        }
        return s;
    }

    private bool TryGetGridRef(double x, double y, out string label)
    {
        label = "";
        if (_worldSizeS <= 0) return false;

        // Off-Grid (Oilrig, Labs) markieren wir als “off-grid”
        //   if (x < 0 || y < 0 || x > _worldSizeS || y > _worldSizeS)
        //  {
        //      label = "off-grid";
        //      return false;
        //  }

        // Anzahl Zellen entlang einer Kante – so zeichnest du auch das Grid
        int cells = Math.Max(1, (int)Math.Round(_worldSizeS / 150.0));
        double cell = _worldSizeS / (double)cells;

        int col = Math.Clamp((int)Math.Floor(x / cell), 0, cells - 1);
        int row = Math.Clamp((int)Math.Floor((_worldSizeS - y) / cell), 0, cells - 1);

        label = $"{ColumnLabel(col)}{row}";
        return true;
    }

    private string GetGridLabel(RustPlusClientReal.ShopMarker s)
        => TryGetGridRef(s.X, s.Y, out var g) ? g : "off-grid";

    private static string FormatItemName(int id) => /* deine vorhandene Map-Funktion */ ResolveItemName(id, null);
    private static ImageSource? ResolveItemIcon(int itemId, string? shortName, int decodePx = 32)
    {
        EnsureNewItemDbLoaded();

        string? url = null;
        if (itemId != 0 && sItemsById.TryGetValue(itemId, out var ii1)) url = ii1.IconUrl;
        if (url == null && !string.IsNullOrWhiteSpace(shortName) && sItemsByShort.TryGetValue(shortName!, out var ii2))
            url = ii2.IconUrl;

        if (string.IsNullOrWhiteSpace(url)) return null;

        // Memory-Cache?
        if (sIconCache.TryGetValue(url!, out var ready)) return ready;

        // File-Cache?
        var file = GetIconCachePath(url!);
        if (System.IO.File.Exists(file))
        {
            var img = TryLoadBitmapFromFile(file, decodePx);
            if (img != null)
            {
                sIconCache[url!] = img;
                return img;
            }
        }

        // Noch kein Cache → Download im Hintergrund starten (nicht blockieren)
        QueueIconDownload(url!, file);

        return null; // beim nächsten Tooltip-/Hover-Versuch ist das Icon meist schon da
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

    private static void QueueIconDownload(string url, string targetPath)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetPath)!);
                using var http = new HttpClient() { Timeout = TimeSpan.FromSeconds(5) };
                var data = await http.GetByteArrayAsync(url);
                await System.IO.File.WriteAllBytesAsync(targetPath, data);
            }
            catch { /* tolerant */ }
        });
    }

    private static void PrefetchShopIcons(IEnumerable<RustPlusClientReal.ShopMarker> shops)
    {
        foreach (var s in shops)
        {
            if (s.Orders == null) continue;
            foreach (var o in s.Orders)
            {
                _ = ResolveItemIcon(o.ItemId, o.ItemShortName);
                _ = ResolveItemIcon(o.CurrencyItemId, o.CurrencyShortName);
            }
        }
    }

    private static Image MakeIcon(string packUri, double size = 32)
    {
        return new Image
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Source = new BitmapImage(new Uri(packUri, UriKind.Absolute))
        };
    }
    private Point _panLastHost;
    private Point _lastHost;
    private void WebViewHost_MouseDown(object? sender, MouseButtonEventArgs e)
    {

        var hostPos = e.GetPosition(WebViewHost);
        var mapPos = HostToScenePreTransform(hostPos); // du hast die Helperfunktion schon

        if (_overlayToolsVisible && _currentTool != OverlayToolMode.None)
        {
            HandleOverlayMouseDown(e, mapPos);
            e.Handled = true;
            return;
        }
        if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right)
        {
            _isPanning = true;
            _panLastHost = e.GetPosition(WebViewHost);
            WebViewHost.CaptureMouse();
            e.Handled = true;
        }
    }
    private Point _panLastWorld; // Maus in "Welt"/Scene-Koordinaten (vor der Transform)
    private void WebViewHost_MouseMove(object? sender, MouseEventArgs e)
    {

        var hostPos = e.GetPosition(WebViewHost);
        var mapPos = HostToScenePreTransform(hostPos);

        if (_overlayToolsVisible && _currentTool != OverlayToolMode.None)
        {
            HandleOverlayMouseMove(e, mapPos);
            e.Handled = true;
            return;
        }
        if (!_isPanning) return;

        var hostNow = e.GetPosition(WebViewHost);
        var dHost = hostNow - _panLastHost;
        _panLastHost = hostNow;

        // Delta sauber in Scene-Einheiten umrechnen!
        var dScene = HostDeltaToSceneDelta(dHost);

        var m = MapTransform.Matrix;
        m.Translate(dScene.X, dScene.Y);
        MapTransform.Matrix = m;
        CenterMiniMapOnPlayer();
        e.Handled = true;
    }

    private void WebViewHost_MouseUp(object? sender, MouseButtonEventArgs e)
    {
        var hostPos = e.GetPosition(WebViewHost);
        var mapPos = HostToScenePreTransform(hostPos);

        if (_overlayToolsVisible && _currentTool != OverlayToolMode.None)
        {
            HandleOverlayMouseUp(e, mapPos);
            e.Handled = true;
            return;
        }

        if (_isPanning && (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right))
        {
            _isPanning = false;
            WebViewHost.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void BuildMonumentOverlays()
    {
        if (Overlay == null || _worldSizeS <= 0 || _worldRectPx.Width <= 0) return;

        // alles neu aufbauen (einmalig nach Map-Load reicht i. d. R.)
        foreach (var kv in _monEls) Overlay.Children.Remove(kv.Value);
        _monEls.Clear();

        foreach (var m in _monData)
        {
            // world→image
            var p = WorldToImagePx(m.X, m.Y);

            // Name normalisieren + Variante (A/B/C)
            var key = NormalizeMonName(m.Name, out var variant);
            var nice = Beautify(m.Name); // deine vorhandene Beautify-Funktion
            var tt = string.IsNullOrEmpty(variant) ? nice : $"{nice} ({variant})";

            var fe = MakeMonIcon(key, tt, 28);
            fe.Tag = m;

            Overlay.Children.Add(fe);
            Panel.SetZIndex(fe, 800); // unter Dyn-Events (900), aber über Map
            _monEls[key + "@" + p.X.ToString("0") + "," + p.Y.ToString("0")] = fe;

            ApplyCurrentOverlayScale(fe);
            Canvas.SetLeft(fe, p.X - 14); // halb Größe
            Canvas.SetTop(fe, p.Y - 14);
            fe.Visibility = _showMonuments ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    private void RefreshMonumentOverlayPositions()
    {
        if (_monEls.Count == 0) return;

        foreach (var fe in _monEls.Values)
        {
            if (fe.Tag is ValueTuple<double, double, string> m)
            {
                var p = WorldToImagePx(m.Item1, m.Item2); // Item1 = X, Item2 = Y
                ApplyMonumentScale(fe);
                Canvas.SetLeft(fe, p.X - fe.RenderSize.Width / 2);
                Canvas.SetTop(fe, p.Y - fe.RenderSize.Height / 2);
                Panel.SetZIndex(fe, 800);

            }
            else if (fe.Tag != null)
            {
                // fallback: dynamic oder anonyme Typen
                dynamic d = fe.Tag;
                var p = WorldToImagePx((double)d.X, (double)d.Y);
                ApplyCurrentOverlayScale(fe);
                Canvas.SetLeft(fe, p.X - 14);
                Canvas.SetTop(fe, p.Y - 14);
                Panel.SetZIndex(fe, 800);

            }
        }
    }
    private void SetupMapScene(BitmapSource bmp)
    {
        double wDip = bmp.PixelWidth * (96.0 / bmp.DpiX);
        double hDip = bmp.PixelHeight * (96.0 / bmp.DpiY);

        ImgMap.Stretch = Stretch.None;
        ImgMap.HorizontalAlignment = HorizontalAlignment.Left;
        ImgMap.VerticalAlignment = VerticalAlignment.Top;
        ImgMap.Width = wDip;
        ImgMap.Height = hDip;

        GridLayer.Width = wDip;
        GridLayer.Height = hDip;
        GridLayer.IsHitTestVisible = false;

        Overlay.Width = wDip;
        Overlay.Height = hDip;
        Overlay.IsHitTestVisible = true;
        Overlay.Background = Brushes.Transparent;
        EnsureShopsHoverPopup();

        // Szene aufbauen (wie bei dir)
        _scene ??= new Grid();
        _scene.Width = wDip;
        _scene.Height = hDip;

        (ImgMap.Parent as Panel)?.Children.Remove(ImgMap);
        (GridLayer.Parent as Panel)?.Children.Remove(GridLayer);
        (Overlay.Parent as Panel)?.Children.Remove(Overlay);

        _scene.Children.Clear();
        _scene.Children.Add(ImgMap); Panel.SetZIndex(ImgMap, 0);
        _scene.Children.Add(GridLayer); Panel.SetZIndex(GridLayer, 1);
        _scene.Children.Add(Overlay); Panel.SetZIndex(Overlay, 2);
        _scene.RenderTransform = MapTransform;
        if (_mapView == null)
        {
            _mapView = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both };
            WebViewHost.Children.Add(_mapView);
            Panel.SetZIndex(_mapView, 0);
        }
        _mapView.Child = _scene;

        // WICHTIG: Transform NUR am gemeinsamen Parent


        // NICHT mehr:
        // ImgMap.RenderTransform = ...
        // GridLayer.RenderTransform = ...
        // Overlay.RenderTransform = ...
    }
    private void ShowMapBasic(BitmapSource bmp)
    {
        if (_webView != null) _webView.Visibility = Visibility.Collapsed;

        _mapBaseBmp = bmp;
        _staticMarkers.Clear();            // << keine Testpunkte

        ImgMap.Source = bmp;               // zunächst nackte Map
        SetupMapScene(bmp);
        RedrawGrid();
    }

    private bool _mapReady;
    private static string Beautify(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        s = s.Replace('\\', '/');
        var last = s.LastIndexOf('/');
        var token = last >= 0 ? s[(last + 1)..] : s;
        return token.Replace(".prefab", "").Replace('_', ' ');
    }



    // From worldSize and image size compute the centered playable square in IMAGE PIXELS.
    // The "2000" is the UI canvas padding used by Rust's own Map code (1000 per side).
    private static Rect ComputeWorldRectFromWorldSize(int imgW, int imgH, int worldSize, int padWorld = 2000)
    {
        if (worldSize <= 0) return new Rect(0, 0, imgW, imgH); // fallback

        int minSidePx = Math.Min(imgW, imgH);
        double scale = (double)worldSize / (worldSize + padWorld); // fraction of the image occupied by the world
        double sidePx = minSidePx * scale;

        double ox = (imgW - sidePx) / 2.0; // centered
        double oy = (imgH - sidePx) / 2.0;

        return new Rect(ox, oy, sidePx, sidePx);
    }
    private void EnsureShopsHoverPopup()
    {
        if (_shopsHoverPopup != null) return;

        Overlay.Background = Brushes.Transparent;

        _shopsHoverWrap = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = SHOP_CARD_WIDTH,
            ItemHeight = double.NaN,
            Margin = new Thickness(SHOP_GAP),
        };

        var border = new Border
        {
            Background = PopupBg,    // <<< dunkel statt weiß
            BorderBrush = PopupBrd,   // dezente Linie
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6),
            Child = _shopsHoverWrap,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                ShadowDepth = 2,
                BlurRadius = 10,
                Opacity = 0.5
            }
        };

        // Wrap nach N Karten pro Zeile
        double maxContentWidth = SHOPS_WRAP_COLUMNS * (SHOP_CARD_WIDTH + SHOP_GAP);
        border.MaxWidth = maxContentWidth + 12 /*Padding*/ + 2 /*Border*/;

        _shopsHoverPopup = new Popup
        {
            Placement = PlacementMode.RelativePoint,
            PlacementTarget = Overlay,
            StaysOpen = true,
            AllowsTransparency = true,
            IsHitTestVisible = false,
            Child = border
        };

        Overlay.MouseMove += Overlay_MouseMove_ShowMultiShopCards;
        Overlay.MouseLeave += (_, __) =>
        {
            if (_shopsHoverPopup != null) _shopsHoverPopup.IsOpen = false;
            EnableSingleShopTooltips(true);
        };
    }

    private FrameworkElement? FindShopIconRoot(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is FrameworkElement fe && _shopIconSet.Contains(fe))
                return fe;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    private static string Shorten(string? s, int max = 10)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim();
        if (s.Length <= max) return s;
        return s.Substring(0, Math.Max(1, max - 1)) + "…";
    }

    private void Overlay_MouseMove_ShowMultiShopCards(object? sender, MouseEventArgs e)
    {
        if (_shopsHoverPopup == null || _shopsHoverWrap == null) return;

        var pt = e.GetPosition(Overlay);
        var hits = new List<FrameworkElement>();

        VisualTreeHelper.HitTest(
            Overlay,
            d =>
            {
                if (d is UIElement uie && uie.Visibility != Visibility.Visible)
                    return HitTestFilterBehavior.ContinueSkipSelfAndChildren;
                return HitTestFilterBehavior.Continue;
            },
            r =>
            {
                var root = FindShopIconRoot(r.VisualHit);
                if (root != null && !hits.Contains(root)) hits.Add(root);
                return HitTestResultBehavior.Continue;
            },
            new PointHitTestParameters(pt)
        );

        if (hits.Count > 1)
        {
            // Einzel-Tooltips komplett ausschalten, solange Aggregat offen ist:
            EnableSingleShopTooltips(false);

            _shopsHoverWrap.Children.Clear();

            foreach (var fe in hits)
            {
                if (fe.Tag is RustPlusClientReal.ShopMarker s)
                {
                    var offers = s.Orders ?? Enumerable.Empty<RustPlusClientReal.ShopOrder>();
                    var card = BuildShopSearchCard(s, offers, compact: true);    // deine „volle“ Karte
                    card.Width = SHOP_CARD_WIDTH;                   // feste Breite fürs Wrappen
                    ToolTipService.SetIsEnabled(card, false);       // Karte selbst ohne Tooltip
                    _shopsHoverWrap.Children.Add(card);
                }
            }

            _shopsHoverPopup.HorizontalOffset = pt.X + 16;
            _shopsHoverPopup.VerticalOffset = pt.Y + 16;
            _shopsHoverPopup.IsOpen = true;
        }
        else
        {
            // Aggregat zu → Einzel-Tooltips wieder an
            _shopsHoverPopup.IsOpen = false;
            EnableSingleShopTooltips(true);
        }
    }
    private FrameworkElement BuildOfferRowSearchUI(RustPlusClientReal.ShopOrder o)
    => BuildOfferRowSearchUI(o, compact: false);
    private void EnableSingleShopTooltips(bool on)
    {
        foreach (var fe in _shopIconSet)
            ToolTipService.SetIsEnabled(fe, on);
    }

    private async Task LoadMapAsync()
    {
        if (_rust is not RustPlusClientReal real) return;

        var map = await real.GetMapWithMonumentsAsync();
        if (map == null) { AppendLog("Map: no data received."); return; }

        await Dispatcher.InvokeAsync(() =>
        {
            // 1) Map anzeigen
            ShowMapBasic(map.Bitmap);
            SetupMapScene(map.Bitmap);
            _worldSizeS = map.WorldSize;
            _worldRectPx = ComputeWorldRectFromWorldSize(map.PixelWidth, map.PixelHeight, _worldSizeS, /*padWorld:*/ 2000);
            RedrawGrid();
            Dispatcher.InvokeAsync(() =>
            {
                RefreshAllOverlayScales();          // Spieler/Death/Shops skalieren
                RefreshMonumentOverlayPositions();  // Monuments nach Layout korrekt setzen + skalieren
            }, System.Windows.Threading.DispatcherPriority.Loaded); // oder Render
            StartDynPolling();


            // Layer-Größen & gleiche Transform wie Bild
            Overlay.Width = ImgMap.Width;
            Overlay.Height = ImgMap.Height;
            GridLayer.Width = ImgMap.Width;
            GridLayer.Height = ImgMap.Height;
            //  _scene.RenderTransform = MapTransform;
            //Overlay.RenderTransform = MapTransform;
            // GridLayer.RenderTransform = MapTransform;

            // Grid initial zeichnen, je nach Checkbox
            RedrawGrid();

            // Shops-Timer ggf. starten/stoppen
            //StartShopPolling();

            int imgW = map.PixelWidth;
            int imgH = map.PixelHeight;
            int S = map.WorldSize;
            _monData = map.Monuments.ToList();
            BuildMonumentOverlays();
            // 2) Centered playable square purely from worldSize (no pixel scanning)
            var worldRectPx = ComputeWorldRectFromWorldSize(imgW, imgH, S, padWorld: 2000);
            AppendLog($"worldRectPx(fromS)=[{(int)worldRectPx.X},{(int)worldRectPx.Y},{(int)worldRectPx.Width}x{(int)worldRectPx.Height}] img={imgW}x{imgH} S={S}");

            // 3) Monuments (no heuristics, no trimming)
            var mons = map.Monuments.Where(m => !string.IsNullOrWhiteSpace(m.Name)).ToList();

            // _staticMarkers.Clear();

            foreach (var m in mons)
            {
                bool off = (m.X < 0) || (m.Y < 0) || (m.X > S) || (m.Y > S);

                // clamp into world bounds for drawing; still flag off-grid
                double cx = Math.Clamp(m.X, 0, S);
                double cy = Math.Clamp(m.Y, 0, S);

                // linear map into the centered square (Y inverted)
                double u = worldRectPx.X + (cx / S) * worldRectPx.Width;
                double v = worldRectPx.Y + ((S - cy) / S) * worldRectPx.Height;

                // optional: show off-grid (oilrig/uw-lab) with a tiny outward nudge so it's visibly "outside"
                if (off)
                {
                    const double nudge = 150;
                    if (m.X < 0) u -= nudge; else if (m.X > S) u += nudge;
                    if (m.Y < 0) v += nudge; else if (m.Y > S) v -= nudge;
                }

                //  _staticMarkers.Add((u, v, Beautify(m.Name)));
            }

            // 4) Einmal rendern
            // ImgMap.Source = ComposeMapWithMarkers(_mapBaseBmp!);
        });
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
    private void StartDynPolling()
    {
        _dynTimer?.Stop();
        _dynTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _dynTimer.Tick += async (_, __) => await PollDynMarkersOnceAsync();
        _dynTimer.Start();

    }

    private void StopDynPolling()
    {
        _dynTimer?.Stop();
        _dynTimer = null;

        foreach (var kv in _dynEls) Overlay.Children.Remove(kv.Value);
        _dynEls.Clear();
        _dynKnown.Clear();
    }

    private void ChkPlayers_Checked(object sender, RoutedEventArgs e)
    {
        _showPlayers = (ChkPlayers.IsChecked != false);
        // just reapply visibility now
        foreach (var kv in _dynEls)
        {
            if (kv.Value.Tag is RustPlusClientReal.DynMarker dm)
            {
                if (dm.Type == 1) // player
                    kv.Value.Visibility = _showPlayers ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private FrameworkElement BuildEventIconHost(FrameworkElement inner, string? tooltip, int size)
    {
        // Host bleibt unskaliert
        var host = new Grid { Width = size, Height = size, IsHitTestVisible = true };
        if (tooltip != null) ToolTipService.SetToolTip(host, tooltip);

        host.Children.Add(inner);

        // Inneres wird skaliert (Pivot = Mitte)
        host.Tag = new PlayerMarkerTag
        {
            Radius = size * 0.5,          // für zentrierte Positionierung
            ScaleExp = SHOP_SIZE_EXP,     // Reaktion wie Shops (passt gut für Events)
            ScaleBaseMult = SHOP_BASE_MULT,
            ScaleTarget = inner,
            ScaleCenterX = size * 0.5,
            ScaleCenterY = size * 0.5
        };

        return host;
    }

    private FrameworkElement BuildEventDot(string tooltip, int size = 14)
    {
        var dot = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = Brushes.Orange,
            Stroke = Brushes.Black,
            StrokeThickness = 1.5
        };
        return BuildEventIconHost(dot, tooltip, size);
    }

    private async Task PollDynMarkersOnceAsync()
    {
        if (_rust is not RustPlusClientReal real) return;
        if (_worldSizeS <= 0 || _worldRectPx.Width <= 0) return;

        try
        {
            var list = await real.GetDynamicMapMarkersAsync();
            if (list.Count > 0)
            {
                var d0 = list[0];
                // AppendLog($"dyn[0]: type={d0.Type} kind={d0.Kind} xy=({d0.X:0},{d0.Y:0}) label={d0.Label ?? "-"}");
                // Verteilung
                var cPlayers = list.Count(m => m.Type == 1);
                var cCargo = list.Count(m => m.Type == 5);
                var cCrate = list.Count(m => m.Type == 6);
                var cCH47 = list.Count(m => m.Type == 4);
                var cPatrol = list.Count(m => m.Type == 8);
                //AppendLog($"dyn: total={list.Count} players={cPlayers} ch47={cCH47} cargo={cCargo} crate={cCrate} patrol={cPatrol}");
            }

            UpdateDynUI(list);
            Dispatcher.InvokeAsync(() => RefreshAllOverlayScales(),
    System.Windows.Threading.DispatcherPriority.Loaded);
        }
        catch
        {
            // ignore, next tick
        }
    }

    private static uint DynFallbackKey(double x, double y, string? label, int type)
    {
        unchecked
        {
            uint h = 2166136261;
            void mix(ulong v) { for (int i = 0; i < 8; i++) { h ^= (byte)(v & 0xFF); h *= 16777619; v >>= 8; } }
            // Position etwas runden, um Jitter zu vermeiden:
            double rx = Math.Round(x, 1), ry = Math.Round(y, 1);
            mix(BitConverter.DoubleToUInt64Bits(rx));
            mix(BitConverter.DoubleToUInt64Bits(ry));
            h ^= (byte)type; h *= 16777619;
            if (!string.IsNullOrEmpty(label))
                foreach (char c in label) { h ^= (byte)c; h *= 16777619; }
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
                FileName = "https://streamelements.com/pronwan/tip",
                UseShellExecute = true   // öffnet im Standard-Browser
            });
        }
        catch (Exception ex)
        {
            AppendLog("Couldn't open Donate Link: " + ex.Message);
        }
    }
    private bool _announceSpawns = false;

    private void ChatAnnounce_Checked(object sender, RoutedEventArgs e) => _announceSpawns = true;
    private void ChatAnnounce_Unchecked(object sender, RoutedEventArgs e) => _announceSpawns = false;
    private void UpdateDynUI(IReadOnlyList<RustPlusClientReal.DynMarker> markers)
    {



        if (Overlay == null || _worldSizeS <= 0 || _worldRectPx.Width <= 0) return;

        // ---- helper: stabiler Key für "Id==0" ----
        static uint DynFallbackKey(double x, double y, string? label, int type)
        {
            unchecked
            {
                uint h = 2166136261;
                void mix(ulong v) { for (int i = 0; i < 8; i++) { h ^= (byte)(v & 0xFF); h *= 16777619; v >>= 8; } }
                double rx = Math.Round(x, 1), ry = Math.Round(y, 1);
                mix(BitConverter.DoubleToUInt64Bits(rx));
                mix(BitConverter.DoubleToUInt64Bits(ry));
                h ^= (byte)type; h *= 16777619;
                if (!string.IsNullOrEmpty(label)) foreach (char c in label) { h ^= (byte)c; h *= 16777619; }
                if (h == 0) h = 1;
                return h;
            }
        }

        // ---- helper: generischer Fallback-Dot (orange) ----
        static FrameworkElement MakeDot(string tooltip, int size = 14)
        {
            var dot = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = Brushes.Orange,
                Stroke = Brushes.Black,
                StrokeThickness = 1.5,

            };
            ToolTipService.SetToolTip(dot, tooltip);
            return dot;
        }

        // ---- helper: Spieler-Dot (grün/grau) für ProfileMarkers=off ----
        static FrameworkElement MakePlayerDot(string tooltip, bool online)
        {
            var dot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = online ? Brushes.LimeGreen : Brushes.LightGray,
                Stroke = Brushes.Black,
                StrokeThickness = 2,
                Margin = new Thickness(0, 0, 4, 0),

            };
            ToolTipService.SetToolTip(dot, tooltip);
            return dot;
        }

        var incoming = new HashSet<uint>();

        foreach (var m in markers)
        {
            bool isPlayer = (m.Type == 1);
            if (isPlayer && m.SteamId != 0)
                _lastPlayersBySid[m.SteamId] = (m.X, m.Y, ResolvePlayerName(m));

            // Spieler komplett ausblenden, wenn global deaktiviert
            if (isPlayer && !_showPlayers) continue;

            bool knownEventType = !isPlayer && sDynIconByType.ContainsKey(m.Type);

            // stabiler Key
            uint key = m.Id != 0 ? m.Id : DynFallbackKey(m.X, m.Y, m.Label ?? m.Kind, m.Type);
            incoming.Add(key);

            // Präsenz (online/dead) aus letztem Poll
            bool online = false, dead = false;
            if (_lastPresence.TryGetValue(m.SteamId, out var pr))
            {
                online = pr.Item1;
                dead = pr.Item2;
            }
            // --- Zusatz-Trigger für DeathPins aus Dyn-Updates ---
            if (_showDeathMarkers)
            {
                if (_lastPresence.TryGetValue(m.SteamId, out var prevPresence))
                {
                    // Wenn wir eine Änderung auf "dead" erkennen, sofort Pin setzen
                    if (!prevPresence.dead && dead) // prev false -> now true
                    {
                        // sichere vm holen (Name/Koords); falls keine, mit unmittelbaren m.X/m.Y arbeiten
                        var vm = TeamMembers.FirstOrDefault(t => t.SteamId == m.SteamId);
                        if (vm != null)
                        {
                            // Positions-Update aus Dyn-Stream (falls TeamPosition veraltet ist)
                            vm.X = m.X; vm.Y = m.Y;
                            Dispatcher.Invoke(() => PlaceDeathPin(vm));
                        }
                        else
                        {
                            // Fallback ohne VM (z.B. seltenes Timing)
                            Dispatcher.Invoke(() =>
                                PlaceOrMoveDeathPin(m.SteamId, m.X, m.Y, ResolvePlayerName(m)));
                        }
                    }
                }
            }

            var nameNow = ResolvePlayerName(m);

            if (!_dynEls.TryGetValue(key, out var el))
            {
                try
                {
                    if (isPlayer)
                    {
                        // Direkt korrektes Visual anlegen (Avatar-Marker ODER Dot)
                        if (_showProfileMarkers)
                            el = BuildPlayerMarker(m.SteamId, nameNow, online, dead); // your existing avatar pin (has name)
                        else
                            el = BuildPlayerDotMarker(m.SteamId, nameNow, online, dead); // <— show name with the dot

                        _dynEls[key] = el;
                        Overlay.Children.Add(el);
                        Panel.SetZIndex(el, 950);
                        ApplyCurrentOverlayScale(el);
                    }
                    else
                    {
                        FrameworkElement host;
                        if (knownEventType)
                        {
                            var uri = sDynIconByType[m.Type];
                            try
                            {
                                var img = MakeIcon(uri, 64);              // <- inner
                                host = BuildEventIconHost(img, m.Label ?? m.Kind, 64);
                            }
                            catch
                            {
                                host = BuildEventDot($"{m.Kind} ({m.Type})", 14);
                            }
                        }
                        else
                        {
                            host = BuildEventDot($"{m.Kind} ({m.Type})", 14);
                        }

                        _dynEls[key] = host;
                        Overlay.Children.Add(host);
                        Panel.SetZIndex(host, 920);
                        ApplyCurrentOverlayScale(host);

                        // einmaliges Spawn-Announcement
                        if (_announceSpawns && !_dynKnown.Contains(key))
                        {
                            _dynKnown.Add(key);
                            var grid = GetGridLabel(m.X, m.Y);
                            var kind = EventKindText(m.Type);
                            _ = SendTeamChatSafeAsync($"{kind} spawned in at {grid}");
                        }

                        el = host;
                    }
                }
                catch
                {
                    // Marker-spezifische Fehler nicht den gesamten Frame killen
                    continue;
                }
            }
            else
            {
                // bestehendes Element
                if (isPlayer)
                {
                    // WICHTIG: Diese Methode schaltet selbst auf Dot um, falls _showProfileMarkers=false
                    UpdatePlayerMarker(ref el, key, m.SteamId, nameNow, online, dead);
                }
                else
                {
                    // Nicht das Tag des Event-Hosts zerstören!
                    if (el.Tag is not PlayerMarkerTag)
                        el.Tag = m; // nur setzen, wenn es kein Host mit PlayerMarkerTag ist
                }
            }

            // Position setzen
            var p = WorldToImagePx(m.X, m.Y);

            if (el.Tag is PlayerMarkerTag t && t.IsDeathPin)
            {
                // Death-Pins NICHT hier bewegen – die setzt PlaceOrMoveDeathPin().
                // (Falls du sie doch hier setzen willst, nutze dein PinW/CircleTop/Circle.)
            }
            else
            {
                // normale Spieler/Events: zentriert mit Radius (falls gesetzt)
                double off = (el.Tag is PlayerMarkerTag t2 && t2.Radius > 0) ? t2.Radius : 5.0;
                Canvas.SetLeft(el, p.X - off);
                Canvas.SetTop(el, p.Y - off);
            }

            if (isPlayer)
                el.Visibility = _showPlayers ? Visibility.Visible : Visibility.Collapsed;
        }

        // Nicht mehr vorhandene Marker entfernen
        CenterMiniMapOnPlayer();
        var gone = _dynEls.Keys.Where(id => !incoming.Contains(id)).ToList();
        foreach (var id in gone)
        {
            if (_dynEls.TryGetValue(id, out var el))
            {
                Overlay.Children.Remove(el);
                _dynEls.Remove(id);
            }
            _dynKnown.Remove(id);
        }
    }

    // avatar size on the map
    private const double PlayerAvatarSize = 24;

    private sealed class PlayerMarkerTag
    {
        public ulong SteamId;
        public TextBlock NameText = null!;
        public string? Name { get; set; }
        public Ellipse? AvatarCircle;
        public double Radius;
        public bool IsDeathPin { get; set; }
        public bool IsDot;

        // Per-Element-Scaling:
        public double ScaleExp { get; set; } = SHOP_SIZE_EXP; // stärker/schwächer je Typ
        public double ScaleBaseMult { get; set; } = 1.0;      // Grundgröße
        public FrameworkElement? ScaleTarget { get; set; }
        public double ScaleCenterX { get; set; }  // Pivot X
        public double ScaleCenterY { get; set; }  // Pivot Y
    }





    // quick lookup from your Team list (no extra web calls here)
    private ImageSource? GetAvatar(ulong sid)
        => TeamMembers.FirstOrDefault(t => t.SteamId == sid)?.Avatar;

    private ImageSource? GetAvatarForMap(ulong sid)
    => _showProfileMarkers ? GetTeamAvatar(sid) : null;

    private ImageSource? GetTeamAvatar(ulong sid)
    {
        var vm = TeamMembers.FirstOrDefault(t => t.SteamId == sid);
        if (vm?.Avatar != null) return vm.Avatar;

        if (_avatarCache.TryGetValue(sid, out var img) && img != null)
            return img;
        return null;

    }

    // Small dot + name label (used when Profile markers is OFF)
    private FrameworkElement BuildPlayerDotMarker(ulong sid, string name, bool online, bool dead)
    {
        var brush = dead ? Brushes.IndianRed : (online ? Brushes.LimeGreen : Brushes.LightGray);

        var dot = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = brush,
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            Margin = new Thickness(0, 0, 4, 0),

        };

        var tb = new TextBlock
        {
            Text = name,
            Foreground = brush,
            FontSize = 12,
            Margin = new Thickness(6, -2, 0, 0)
        };

        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(dot);
        sp.Children.Add(tb);
        ToolTipService.SetToolTip(sp, name);

        // keep Radius=5 so centering uses the dot’s radius, not the label width
        sp.Tag = new PlayerMarkerTag
        {
            SteamId = sid,
            Name = name,
            NameText = tb,
            AvatarCircle = null,
            Radius = 5,
            IsDeathPin = false,
            IsDot = true, // <— new flag to distinguish dot vs avatar
            ScaleExp = 1.05,
            ScaleBaseMult = 1.0,
            ScaleTarget = sp,          // << gesamten Inhalt skalieren (Dot + Name)
            ScaleCenterX = 5.0,        // << Dot.Width / 2.0 (10/2)
            ScaleCenterY = 5.0         // << Dot.Height / 2.0
        };

        return sp;
    }

    // build a player marker (avatar circle if available, dot otherwise)
    private FrameworkElement BuildPlayerMarker(ulong sid, string name, bool online, bool dead)
    {
        var brush = dead ? Brushes.IndianRed : (online ? Brushes.LimeGreen : Brushes.Gray);
        var avatar = GetAvatar(sid);

        if (avatar == null)
        {
            var dot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = brush,
                Stroke = Brushes.Black,
                StrokeThickness = 2,
                Margin = new Thickness(0, 0, 4, 0)
            };
            var tb = new TextBlock { Text = name, Foreground = brush, FontSize = 12, Margin = new Thickness(6, -2, 0, 0) };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(dot);
            sp.Children.Add(tb);
            sp.Tag = new PlayerMarkerTag
            {
                SteamId = sid,
                NameText = tb,
                AvatarCircle = null,
                Radius = 5,
                IsDot = true,
                ScaleExp = 1.05,
                ScaleBaseMult = 1.0
            };
            Panel.SetZIndex(sp, 905);
            ApplyCurrentOverlayScale(sp);
            return sp;
        }
        else
        {
            var tb = new TextBlock { Text = name, Foreground = brush, FontSize = 12, Margin = new Thickness(6, -2, 0, 0) };
            var circle = new Ellipse
            {
                Width = PlayerAvatarSize,
                Height = PlayerAvatarSize,
                Stroke = Brushes.Black,
                StrokeThickness = 2,
                Fill = new ImageBrush(avatar) { Stretch = Stretch.UniformToFill }
            };

            var host = new Grid();
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var avatarHost = new Grid { Width = PlayerAvatarSize, Height = PlayerAvatarSize, Margin = new Thickness(0, 0, 4, 0) };
            avatarHost.Children.Add(circle);

            host.Children.Add(avatarHost);
            Grid.SetColumn(avatarHost, 0);
            host.Children.Add(tb);
            Grid.SetColumn(tb, 1);

            host.Tag = new PlayerMarkerTag
            {
                SteamId = sid,
                NameText = tb,
                AvatarCircle = circle,
                Radius = PlayerAvatarSize * 0.5,
                ScaleExp = 0.85,
                ScaleBaseMult = 1.0,
                ScaleTarget = host,              // << beide (Avatar + Name) skalieren
                ScaleCenterX = PlayerAvatarSize * 0.5, // << Zentrum des Avatars (links)
                ScaleCenterY = PlayerAvatarSize * 0.5,

            };
            Panel.SetZIndex(host, 905);
            ToolTipService.SetToolTip(host, name);
            ApplyCurrentOverlayScale(host);

            return host;
        }
    }

    private readonly HashSet<ulong> _avatarLoading = new();
    private readonly Dictionary<ulong, DateTime> _avatarNextTry = new();
    private static readonly TimeSpan AvatarRetryInterval = TimeSpan.FromSeconds(30);

    private bool CanTryAvatar(ulong sid)
    {
        if (_avatarLoading.Contains(sid)) return false;
        return !_avatarNextTry.TryGetValue(sid, out var next) || DateTime.UtcNow >= next;
    }

    private FrameworkElement MakePlayerDot(string tooltip, bool online)
    {
        var dot = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = online ? Brushes.LimeGreen : Brushes.LightGray,
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            Margin = new Thickness(0, 0, 4, 0),

        };
        ToolTipService.SetToolTip(dot, tooltip);
        Panel.SetZIndex(dot, 905);
        return dot;
    }

    // update existing marker (swap to avatar when it becomes available, recolor & rename)
    private void UpdatePlayerMarker(ref FrameworkElement el, uint key, ulong sid, string name, bool online, bool dead)
    {
        if (sid == 0) return; // niemals Events o.ä. „umwandeln“
        // Checkbox respektieren:
        if (!_showProfileMarkers)
        {
            var brush = dead ? Brushes.IndianRed : (online ? Brushes.LimeGreen : Brushes.LightGray);

            // Wenn noch kein "Dot+Name"-Marker existiert (oder ein Avatar aktiv war): neu bauen
            if (el.Tag is not PlayerMarkerTag t || !t.IsDot)
            {
                var newEl = BuildPlayerDotMarker(sid, name, online, dead);
                int idx = Overlay.Children.IndexOf(el);
                if (idx >= 0) { Overlay.Children.RemoveAt(idx); Overlay.Children.Insert(idx, newEl); }
                else Overlay.Children.Add(newEl);
                _dynEls[key] = newEl; el = newEl;
                Panel.SetZIndex(newEl, 905);
            }
            else
            {
                // Bereits ein Dot-StackPanel vorhanden -> nur Name/Brush aktualisieren
                t.NameText.Text = name;
                t.NameText.Foreground = brush;

                // Dot (erste Ellipse im StackPanel) umfärben
                if (t.NameText.Parent is Panel sp)
                {
                    var dot = sp.Children.OfType<Ellipse>().FirstOrDefault();
                    Panel.SetZIndex(dot, 905);
                    if (dot != null) dot.Fill = brush;
                }
            }

            // Tooltip konsistent halten (optional)
            ToolTipService.SetToolTip(el, name);
            return; // <— wichtig
        }

        // (Rest: deine bisherige Logik für Avatar-/Name-Update)
        var brush2 = dead ? Brushes.IndianRed : (online ? Brushes.LimeGreen : Brushes.LightGray);
        var avatar = GetAvatarForMap(sid);

        if (el.Tag is PlayerMarkerTag tag)
        {
            if (tag.NameText != null) tag.NameText.Text = name;
            if (tag.NameText != null) tag.NameText.Foreground = brush2;

            if (avatar != null && tag.AvatarCircle == null ||
                avatar == null && tag.AvatarCircle != null)
            {
                var newEl = BuildPlayerMarker(sid, name, online, dead);
                int idx = Overlay.Children.IndexOf(el);
                if (idx >= 0) { Overlay.Children.RemoveAt(idx); Overlay.Children.Insert(idx, newEl); }
                else Overlay.Children.Add(newEl);
                _dynEls[key] = newEl; el = newEl;
            }
            else if (tag.AvatarCircle != null && avatar != null)
            {
                tag.AvatarCircle.Fill = new ImageBrush(avatar) { Stretch = Stretch.UniformToFill };
            }
        }
        else
        {
            var newEl = BuildPlayerMarker(sid, name, online, dead);
            int idx = Overlay.Children.IndexOf(el);
            if (idx >= 0) { Overlay.Children.RemoveAt(idx); Overlay.Children.Insert(idx, newEl); }
            else Overlay.Children.Add(newEl);
            _dynEls[key] = newEl; el = newEl;
        }
    }

    private void ChkProfileMarkers_Toggled(object? sender, RoutedEventArgs e)
    {
        _showProfileMarkers = ChkProfileMarkers.IsChecked == true;

        // alle existierenden Player-Marker umschalten
        foreach (var kv in _dynEls.ToList())
        {
            if (kv.Value is FrameworkElement el && el.Tag is PlayerMarkerTag tag)
            {
                if (tag.SteamId == 0 || tag.IsDeathPin) continue; // keine Events/DeathPins anfassen
                var sid = tag.SteamId;
                var name = TeamMembers.FirstOrDefault(t => t.SteamId == sid)?.Name ?? "player";
                //var st = _lastPresence.TryGetValue(sid, out var p) ? p : (false, false);
                if (_lastPresence.TryGetValue(sid, out var p))
                {
                    var online = p.Item1;
                    var dead = p.Item2;
                    UpdatePlayerMarker(ref el, kv.Key, sid, name, online, dead);
                }
                else
                {
                    UpdatePlayerMarker(ref el, kv.Key, sid, name, online: false, dead: false);
                }
            }
        }
    }

    private void ChkDeathMarkers_Toggled(object? sender, RoutedEventArgs e)
    {
        _showDeathMarkers = ChkDeathMarkers.IsChecked == true;
        if (!_showDeathMarkers) ClearAllDeathPins(); // „Deaktivieren cleart“
    }

    private const double PinW = 40;   // Gesamtbreite
    private const double PinH = 56;   // Gesamthöhe (inkl. Spitze)
    private const double Circle = 24; // Durchmesser der Kreisfläche im Kopf
    private const double CircleTop = 6; // Abstand von oben bis Kreis-OBERKANTE


    private void RefreshAllOverlayScales()
    {
        // Player & sonstige dynamische Elemente
        foreach (var fe in _dynEls.Values)
            ApplyCurrentOverlayScale(fe);

        // DeathPins
        foreach (var fe in _deathPins.Values)
            ApplyCurrentOverlayScale(fe);

        // Shops (falls du sie weiter separat halten willst)
        RefreshShopIconScales();
    }
    private FrameworkElement BuildDeathPin(ulong sid, string name, ImageSource? avatarFromCaller = null)
    {
        var avatar = GetTeamAvatar(sid);

        var root = new Grid
        {
            Width = PinW,
            Height = PinH,

            Tag = new PlayerMarkerTag
            {
                SteamId = sid,
                Name = name,
                IsDeathPin = true,

                // wichtig fürs Skalieren:
                ScaleExp = 0.8,
                ScaleBaseMult = 0.9,
                ScaleTarget = null,                 // null => wir skalieren root selbst
                ScaleCenterX = PinW * 0.5,          // Pivot X = Mitte unten (Spitze)
                ScaleCenterY = PinH                 // Pivot Y = Unterkante (Spitze)
            }
        };

        // rote Tropfenform (Path ist auf Containergröße gestretcht)
        var pinPath = Geometry.Parse(
            "M20,0 C31,0 40,9 40,20 C40,33 20,56 20,56 C20,56 0,33 0,20 C0,9 9,0 20,0 Z"
        );

        var fill = TryFindResource("DeathPinFill") as Brush ?? Brushes.IndianRed;

        root.Children.Add(new System.Windows.Shapes.Path
        {
            Data = pinPath,
            Fill = fill,
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            Stretch = Stretch.Fill,
            Width = PinW,
            Height = PinH
        });

        // schwarzer Ring um die Kreisfläche
        root.Children.Add(new Ellipse
        {
            Width = Circle + 6,
            Height = Circle + 6,
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness((PinW - (Circle + 6)) / 2.0, CircleTop - 3, 0, 0)
        });

        // Avatar (kreisförmig geclippt) – HORIZONTAL zentriert, TOP = CircleTop
        FrameworkElement avatarEl;
        if (avatar != null)
        {
            var holder = new Grid { Width = Circle, Height = Circle };
            holder.Clip = new EllipseGeometry(new Point(Circle / 2.0, Circle / 2.0), Circle / 2.0, Circle / 2.0);
            holder.Children.Add(new Image { Source = avatar, Stretch = Stretch.UniformToFill });
            avatarEl = holder;
        }
        else
        {
            avatarEl = new Ellipse { Width = Circle, Height = Circle, Fill = Brushes.Gray };
        }

        avatarEl.HorizontalAlignment = HorizontalAlignment.Left;
        avatarEl.VerticalAlignment = VerticalAlignment.Top;
        avatarEl.Margin = new Thickness((PinW - Circle) / 2.0, CircleTop, 0, 0);
        root.Children.Add(avatarEl);

        // Tooltip
        ToolTipService.SetToolTip(root, $"{name} (death)");
        ApplyCurrentOverlayScale(root);
        return root;
    }

    private void PlaceDeathPin(TeamMemberVM vm)
    {
        if (!_showDeathMarkers) return;
        if (!(vm.X.HasValue && vm.Y.HasValue)) return;

        var px = WorldToImagePx(vm.X.Value, vm.Y.Value);
        var el = BuildDeathPin(vm.SteamId, vm.Name, GetTeamAvatar(vm.SteamId));

        // ersetzen, falls derselbe Spieler erneut stirbt
        if (_deathPins.TryGetValue(vm.SteamId, out var old))
        {
            Overlay.Children.Remove(old);
            _deathPins.Remove(vm.SteamId);
        }

        Overlay.Children.Add(el);
        Panel.SetZIndex(el, 805); // über Events, unter Namen
        ApplyCurrentOverlayScale(el);

        // Pinspitze auf Position
        var cx = px.X - PinW / 2.0;
        var cy = px.Y - (CircleTop + Circle / 2.0);
        Canvas.SetLeft(el, cx);
        Canvas.SetTop(el, cy);

        _deathPins[vm.SteamId] = el;
    }

    private void PlaceOrMoveDeathPin(ulong sid, double worldX, double worldY, string name)
    {
        var px = WorldToImagePx(worldX, worldY);
        var el = _deathPins.TryGetValue(sid, out var exist) ? exist : null;

        if (el == null)
        {
            el = BuildDeathPin(sid, name);
            _deathPins[sid] = el;
            Overlay.Children.Add(el);
            Panel.SetZIndex(el, 805);
            ApplyCurrentOverlayScale(el);
        }

        // Spitze auf Position
        var cx = px.X - (PinW / 2.0);
        var cy = px.Y - PinH;
        Canvas.SetLeft(el, cx);
        Canvas.SetTop(el, cy);
    }

    private void ClearAllDeathPins()
    {
        foreach (var kv in _deathPins) Overlay.Children.Remove(kv.Value);
        _deathPins.Clear();
    }

    private async Task SendTeamChatSafeAsync(string text)
    {
        try
        {
            if (_rust == null) return;
            var t = _rust.GetType();
            // try the common ones
            var m =
                t.GetMethod("SendTeamMessageAsync", new[] { typeof(string), typeof(CancellationToken) }) ??
                t.GetMethod("SendTeamMessageAsync", new[] { typeof(string) }) ??
                t.GetMethod("SendChatMessageAsync", new[] { typeof(string) }) ??
                t.GetMethod("SendMessageAsync", new[] { typeof(string) });

            if (m != null)
            {
                object? ret = (m.GetParameters().Length == 2)
                    ? m.Invoke(_rust, new object[] { text, CancellationToken.None })
                    : m.Invoke(_rust, new object[] { text });

                if (ret is Task task) await task.ConfigureAwait(false);
            }
        }
        catch { /* ignore */ }
    }
    private string GetGridLabel(RustPlusClientReal.DynMarker m) => GetGridLabel(m.X, m.Y);

    private string GetGridLabel(double x, double y)
    => TryGetGridRef(x, y, out var g) ? g : "off-grid";
    private static string EventKindText(int type) => type switch
    {
        5 => "Cargo Ship",
        //6 => "Locked Crate",
        6 => "Travelling Vendor",
        4 => "CH47",
        8 => "Patrol Helicopter",
        9 => "Oilrig Crate",
        2 => "Explosion",
        7 => "Building Blocked",
        _ => "Event"
    };

    private async void ChkShops_Checked(object sender, RoutedEventArgs e)
    {
        // nur starten, wenn Map-Kontext steht
        if (ChkShops.IsChecked == true && _worldSizeS > 0 && _worldRectPx.Width > 0)
        {
            _shopTimer?.Stop();
            _shopTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _shopTimer.Tick += async (_, __) => await PollShopsOnceAsync();
            _shopTimer.Start();
            //AppendLog("Shops: Polling an (20s).");

            await PollShopsOnceAsync(); // sofort einmal
            await Dispatcher.InvokeAsync(() =>
            {
                RefreshShopIconScales(); // oder RefreshAllOverlayScales(), wenn du es einheitlich halten willst
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else
        {
            _shopTimer?.Stop();
            _shopTimer = null;

            // UI leeren (optional)
            foreach (var kv in _shopEls) Overlay.Children.Remove(kv.Value);
            _shopEls.Clear();
            //AppendLog("Shops: Polling aus.");
        }
    }

    private bool _didInitialOverlayRefresh;

    private async void EnsureInitialOverlayRefresh()
    {
        if (_didInitialOverlayRefresh || Overlay == null) return;
        _didInitialOverlayRefresh = true;

        // Einen Tick warten, bis Measure/Arrange/Render einmal durch sind
        await Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Loaded);
        // alternativ: DispatcherPriority.Render

        RefreshAllOverlayScales();          // skaliert Spieler/Death/Shops
        RefreshMonumentOverlayPositions();  // falls du Monumente separat setzt
    }

    // wie stark du die Größe kompensierst:
    // 0.0 = gar nicht (verhält sich wie Map), 1.0 = am Bildschirm immer gleich groß,
    // 0.7–0.9 = "etwas" größer raus / etwas kleiner rein
    private const double SHOP_SIZE_EXP = 0.8;

    // Gesamtzoom (Viewbox * MapTransform)
    private double GetEffectiveZoom()
    {
        var (s, _, _) = GetViewboxScaleAndOffset(); // hast du schon
        var m = MapTransform.Matrix;
        double eff = Math.Abs(s * m.M11);
        return eff <= 1e-6 ? 1e-6 : eff;
    }

    // auf ein einzelnes Shop-Element anwenden
    private void ApplyCurrentOverlayScale(FrameworkElement el)
    {
        if (el == null) return;

        double eff = GetEffectiveZoom();
        double exp = SHOP_SIZE_EXP, baseMult = SHOP_BASE_MULT;

        FrameworkElement target = el;
        double centerX = 0.0, centerY = 0.0;

        if (el.Tag is PlayerMarkerTag pt)
        {
            if (pt.ScaleExp > 0) exp = pt.ScaleExp;
            if (pt.ScaleBaseMult > 0) baseMult = pt.ScaleBaseMult;

            // für DeathPins wollen wir root skalieren:
            if (pt.IsDeathPin)
            {
                target = el;
                centerX = pt.ScaleCenterX;   // = PinW/2
                centerY = pt.ScaleCenterY;   // = PinH
            }
            else if (pt.ScaleTarget != null)
            {
                // (Spieler: Avatar/Dot+Name etc.)
                target = pt.ScaleTarget;
                centerX = pt.ScaleCenterX;
                centerY = pt.ScaleCenterY;

                if (!ReferenceEquals(target, el))
                    el.RenderTransform = Transform.Identity; // Root nicht zusätzlich skalieren
            }
        }

        double scale = CalcOverlayScale(eff, exp, baseMult);

        // Pivot exakt setzen – kein RenderTransformOrigin nötig
        target.RenderTransform = new ScaleTransform(scale, scale)
        {
            CenterX = centerX,
            CenterY = centerY
        };
    }

    // auf alle Shops anwenden (z.B. nach einem Zoom)
    private void RefreshShopIconScales()
    {
        double eff = GetEffectiveZoom();
        double scale = CalcOverlayScale(eff, SHOP_SIZE_EXP, SHOP_BASE_MULT);

        foreach (var fe in _shopEls.Values)
        {
            fe.RenderTransformOrigin = new Point(0.5, 0.5);
            fe.RenderTransform = new ScaleTransform(scale, scale);
        }
    }

    private async Task PollShopsOnceAsync()
    {
        if (_rust is not RustPlusClientReal real) return;

        try
        {
            var shops = await real.GetVendingShopsAsync();

            UpdateShopsUI(shops);
            _lastShops = shops;

            // Track lifetimes (für suspicious) und bekannte Shops:
            UpdateShopLifetimes(shops);

            // Falls wir frisch mit einem neuen Server verbunden haben:
            if (_alertsNeedRebaseline)
            {
                RebaselineAllAlertRulesFromCurrentShops(shops);

                // detect new shops: wir setzen die baseline für new-shops auch neu
                _initialShopSnapshotTimeUtc = DateTime.UtcNow;
                _knownShopIds.Clear();
                foreach (var s in shops)
                    _knownShopIds.Add(s.Id);

                _alertsNeedRebaseline = false;
            }

            // Neue Shops melden:
            await DetectNewShopsAsync(shops);

            // Alerts prüfen:
            await CheckAlerts(shops);

            // Falls Fenster offen ist und sichtbar -> Liste refreshen
            if (_shopSearchWin?.IsVisible == true)
                RefreshShopSearchResults();
        }
        catch (Exception ex)
        {
            //AppendLog("Shops poll: " + ex.Message);
        }
    }


    private async Task DetectNewShopsAsync(IReadOnlyList<RustPlusClientReal.ShopMarker> shops)
    {
        // Erster erfolgreicher Snapshot?
        if (_initialShopSnapshotTimeUtc == DateTime.MinValue)
        {
            _initialShopSnapshotTimeUtc = DateTime.UtcNow;
            foreach (var s in shops)
                _knownShopIds.Add(s.Id);
            return;
        }

        if (!_notifyNewShopsToChat)
            return;

        foreach (var s in shops)
        {
            if (_knownShopIds.Contains(s.Id))
                continue; // schon bekannt

            _knownShopIds.Add(s.Id);

            // kurze Preview bauen (bis zu 2 Orders mit Stock > 0)
            var preview = s.Orders?
                .Where(o => o.Stock > 0)
                .Take(3)
                .Select(o =>
                {
                    var left = ResolveItemName(o.ItemId, o.ItemShortName);
                    var right = ResolveItemName(o.CurrencyItemId, o.CurrencyShortName);
                    return $"{left} for {o.CurrencyAmount} {right}";
                })
                .ToList();

            string offersShort = (preview != null && preview.Count > 0)
                ? string.Join(", ", preview)
                : "no stock";

            string msg =
                $"New shop {(s.Label ?? "Shop")} [{GetGridLabel(s)}]: {offersShort}";
            AppendLog($"[{DateTime.Now:HH:mm:ss}] Alert [new shop] {(s.Label ?? "Shop")} [{GetGridLabel(s)}]: {offersShort}");
            await SendTeamChatSafeAsync(msg);
        }
    }

    private class TwoStepFlip
    {
        public RustPlusClientReal.ShopMarker ShopFirst;
        public RustPlusClientReal.ShopMarker ShopSecond;
        public RustPlusClientReal.ShopOrder OfferFirst;
        public RustPlusClientReal.ShopOrder OfferSecond;

        public string StartCurrencyName = "";  // Währung, mit der wir anfangen und am Ende wieder rauskommen
        public string MidItemName = "";        // Zwischen-Item

        public int RunsFirst;                  // wie oft wir Schritt1 ausgeführt haben
        public int RunsSecond;                 // wie oft Schritt2

        public int StartSpent;                 // wieviel StartCurrency wir investiert haben
        public int StartBack;                  // wieviel StartCurrency wir zurückbekommen haben
        public int Profit;                     // StartBack - StartSpent

        public int MidProduced;                // wieviel MidItem nach Schritt1 insgesamt
        public int MidConsumed;                // wieviel MidItem von Schritt2 verbraucht
        public int MidLeftover;                // Rest MidItem danach
    }

    // Simuliert: erst (shop1,o1) mehrfach laufen lassen, dann (shop2,o2) benutzen.
    // Gibt BESTE profitable Kombination zurück oder null.
    private TwoStepFlip? SimulateSequence(
    RustPlusClientReal.ShopMarker shop1, RustPlusClientReal.ShopOrder o1,
    RustPlusClientReal.ShopMarker shop2, RustPlusClientReal.ShopOrder o2)
    {
        // Daten aus o1
        int pay1Id = o1.CurrencyItemId;
        string pay1Name = ResolveItemName(o1.CurrencyItemId, o1.CurrencyShortName ?? "");
        int pay1Amt = (int)o1.CurrencyAmount;

        int get1Id = o1.ItemId;
        string get1Name = ResolveItemName(o1.ItemId, o1.ItemShortName ?? "");
        int get1Amt = o1.Quantity;

        int stock1 = o1.Stock;

        // Daten aus o2
        int pay2Id = o2.CurrencyItemId;
        string pay2Name = ResolveItemName(o2.CurrencyItemId, o2.CurrencyShortName ?? "");
        int pay2Amt = (int)o2.CurrencyAmount;

        int get2Id = o2.ItemId;
        string get2Name = ResolveItemName(o2.ItemId, o2.ItemShortName ?? "");
        int get2Amt = o2.Quantity;

        int stock2 = o2.Stock;

        // Guards
        if (pay1Amt <= 0 || get1Amt <= 0 || stock1 <= 0) return null;
        if (pay2Amt <= 0 || get2Amt <= 0 || stock2 <= 0) return null;

        // Loop-Bedingung jetzt über IDs, nicht Strings:
        // Schritt1 produziert get1Id -> muss Schritt2 bezahlen pay2Id
        if (get1Id != pay2Id) return null;
        // Schritt2 produziert get2Id -> muss wieder der Ursprungs-Start pay1Id sein
        if (get2Id != pay1Id) return null;

        TwoStepFlip? best = null;

        for (int runs1 = 1; runs1 <= stock1; runs1++)
        {
            int spentStart = runs1 * pay1Amt;   // wieviel Startwährung investiert
            int midProduced = runs1 * get1Amt;   // wieviel Zwischen-Item bekommen

            int maxByMid = midProduced / pay2Amt;
            int runs2 = Math.Min(maxByMid, stock2);
            if (runs2 <= 0) continue;

            int midConsumed = runs2 * pay2Amt;
            int midLeft = midProduced - midConsumed;
            int startBack = runs2 * get2Amt;

            int profit = startBack - spentStart;
            if (profit <= 0) continue;

            if (best == null || profit > best.Profit)
            {
                best = new TwoStepFlip
                {
                    ShopFirst = shop1,
                    ShopSecond = shop2,
                    OfferFirst = o1,
                    OfferSecond = o2,

                    StartCurrencyName = pay1Name,
                    MidItemName = get1Name,

                    RunsFirst = runs1,
                    RunsSecond = runs2,

                    StartSpent = spentStart,
                    StartBack = startBack,
                    Profit = profit,

                    MidProduced = midProduced,
                    MidConsumed = midConsumed,
                    MidLeftover = midLeft
                };
            }
        }

        return best;
    }

    private List<TwoStepFlip> FindTwoStepFlips(List<RustPlusClientReal.ShopMarker> shops)
    {
        var flips = new List<TwoStepFlip>();

        // zum Deduplizieren
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < shops.Count; i++)
        {
            var s1 = shops[i];
            if (s1.Orders == null) continue;

            for (int j = 0; j < shops.Count; j++)
            {
                var s2 = shops[j];
                if (s2.Orders == null) continue;

                foreach (var o1 in s1.Orders)
                {
                    if (o1.Stock <= 0) continue;
                    foreach (var o2 in s2.Orders)
                    {
                        if (o2.Stock <= 0) continue;

                        // Versuch A->B->A
                        var fwd = SimulateSequence(s1, o1, s2, o2);
                        if (fwd != null)
                        {
                            string sig = MakeFlipSignature(fwd);
                            if (seen.Add(sig))
                                flips.Add(fwd);
                        }

                        // Versuch B->A->B (umgekehrt)
                        var rev = SimulateSequence(s2, o2, s1, o1);
                        if (rev != null)
                        {
                            string sig = MakeFlipSignature(rev);
                            if (seen.Add(sig))
                                flips.Add(rev);
                        }
                    }
                }
            }
        }

        // sortiere: höchster Profit zuerst
        flips.Sort((a, b) => b.Profit.CompareTo(a.Profit));
        return flips;
    }

    // Eindeutige Signatur, damit wir Duplikate filtern
    private string MakeFlipSignature(TwoStepFlip f)
    {
        // Wir sortieren die Shop-IDs, damit A→B & B→A gleich behandelt werden
        uint a = f.ShopFirst.Id;
        uint b = f.ShopSecond.Id;
        if (a > b) { var tmp = a; a = b; b = tmp; }

        // Wir nehmen die ItemIds, nicht die hübschen Namen
        int startId = f.OfferFirst.CurrencyItemId; // Startwährung
        int midId = f.OfferFirst.ItemId;         // Zwischen-Item

        return $"{startId}|{midId}|{a}|{b}";
    }

    private FrameworkElement BuildFlipCard(TwoStepFlip f)
    {
        // äußerer Rahmen des gesamten Flips
        var outerBorder = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(28, 30, 33)), // dunkler statt halbtransparent
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var outerStack = new StackPanel
        {
            Orientation = Orientation.Vertical
        };
        outerBorder.Child = outerStack;

        // === HEADER mit großem Icon + Profit-Text ===
        var headerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(40, 44, 48)), // dunkler Streifen
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        headerBorder.Child = headerRow;

        // Großes Icon für die Start-Ressource (das, wo du am Ende mehr davon hast)
        var bigIcon = new Image
        {
            Width = 32,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        // OfferFirst.Currency = das Start-Item
        BindIcon(
            bigIcon,
            f.OfferFirst.CurrencyShortName,
            f.OfferFirst.CurrencyItemId
        );
        headerRow.Children.Add(bigIcon);

        // Rechts daneben Textblock(e)
        var headerTextStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center
        };

        // fette Profit-Zeile
        headerTextStack.Children.Add(new TextBlock
        {
            Text = $"Profit: +{f.Profit} {f.StartCurrencyName}",
            Foreground = new SolidColorBrush(Color.FromRgb(255, 220, 100)), // gold-ish
            FontWeight = FontWeights.SemiBold,
            FontSize = 14
        });

        // Kurze Zusammenfassung Start -> End
        headerTextStack.Children.Add(new TextBlock
        {
            Text =
        $"Start with {f.StartSpent} {f.StartCurrencyName} → end with {f.StartBack} {f.StartCurrencyName} " +
        $"(Step1 x{f.RunsFirst}, Step2 x{f.RunsSecond})",
            Foreground = Brushes.White,
            Margin = new Thickness(0, 2, 0, 0),
            FontSize = 12
        });

        headerRow.Children.Add(headerTextStack);

        outerStack.Children.Add(headerBorder);

        // === Info über die Zwischen-Ressource ===
        // z.B. "Intermediate Crude Oil: made 100, used 15, leftover 85"
        outerStack.Children.Add(new TextBlock
        {
            Text =
                $"Intermediate {f.MidItemName}: made {f.MidProduced}, used {f.MidConsumed}, leftover {f.MidLeftover}",
            Foreground = new SolidColorBrush(Color.FromArgb(200, 220, 220, 220)),
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 12
        });

        // === Zwei Spalten: Step 1 links, Step 2 rechts ===
        var stepsGrid = new Grid
        {
            Margin = new Thickness(0, 0, 0, 0)
        };
        stepsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stepsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Linke Spalte = Step1
        var step1Panel = BuildFlipStepPanel(
            stepLabel: $"Step 1 x{f.RunsFirst}",
            shop: f.ShopFirst,
            order: f.OfferFirst
        );
        Grid.SetColumn(step1Panel, 0);
        stepsGrid.Children.Add(step1Panel);

        // Rechte Spalte = Step2
        var step2Panel = BuildFlipStepPanel(
            stepLabel: $"Step 2 x{f.RunsSecond}",
            shop: f.ShopSecond,
            order: f.OfferSecond
        );
        Grid.SetColumn(step2Panel, 1);
        stepsGrid.Children.Add(step2Panel);

        outerStack.Children.Add(stepsGrid);

        return outerBorder;
    }

    private FrameworkElement BuildFlipStepPanel(string stepLabel,
    RustPlusClientReal.ShopMarker shop,
    RustPlusClientReal.ShopOrder order)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 8, 0)
        };

        // Step headline
        panel.Children.Add(new TextBlock
        {
            Text = stepLabel,
            Foreground = SearchText,
            FontWeight = FontWeights.SemiBold
        });

        // Shop row with [Go] button
        var shopRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 4),
            VerticalAlignment = VerticalAlignment.Center
        };

        shopRow.Children.Add(new TextBlock
        {
            Text = $"{(shop.Label ?? "Shop")} [{GetGridLabel(shop)}]",
            Foreground = SearchText,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        var btnGo = new Button
        {
            Content = "Go",
            Margin = new Thickness(0, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        btnGo.Click += (_, __) => CenterMapOnWorld(shop.X, shop.Y);

        shopRow.Children.Add(btnGo);

        panel.Children.Add(shopRow);

        // Offer card itself (re-use BuildOfferRowUI so wir kriegen Icons/Stock/→ usw.)
        var offerCard = BuildOfferRowUI(order);
        // BuildOfferRowUI gibt dir schon einen Border mit Grid drin.
        // Wir wollen es nur etwas einrücken:
        if (offerCard is FrameworkElement fe)
            fe.Margin = new Thickness(0, 0, 8, 8);

        panel.Children.Add(offerCard);

        return panel;
    }


    private void UpdateShopLifetimes(IReadOnlyList<RustPlusClientReal.ShopMarker> shops)
    {
        // Step 1: Markiere erstmal alle als "nicht gesehen in diesem Poll"
        foreach (var kv in _shopLifetimes)
        {
            kv.Value.LastSeenUtc = null;
        }

        // Step 2: Gesehene aktualisieren / neu anlegen
        foreach (var s in shops)
        {
            if (!_shopLifetimes.TryGetValue(s.Id, out var life))
            {
                life = new ShopLifetimeInfo
                {
                    FirstSeenUtc = DateTime.UtcNow,
                    LastSnapshot = s
                };
                _shopLifetimes[s.Id] = life;
            }

            life.LastSeenUtc = DateTime.UtcNow;
            life.LastSnapshot = s;
        }

        // Step 3: Jetzt checken wir auf "suspicious" für die, die NICHT gesehen wurden
        if (_notifySuspiciousShops)
        {
            foreach (var kv in _shopLifetimes.ToList())
            {
                var id = kv.Key;
                var life = kv.Value;

                // War im letzten Poll da, jetzt nicht gesehen = offline gegangen
                if (life.LastSeenUtc == null && !life.AnnouncedSuspicious)
                {
                    string grid = life.LastSnapshot != null ? GetGridLabel(life.LastSnapshot) : "unknown";
                    AppendLog($"[dbg] Shop {life.LastSnapshot?.Label ?? "(no label)"} [{grid}] offline after {(DateTime.UtcNow - life.FirstSeenUtc).TotalSeconds:0}s");
                    var lived = DateTime.UtcNow - life.FirstSeenUtc;
                    if (lived.TotalSeconds <= 60.0)
                    {
                        // Alarm
                        var snap = life.LastSnapshot;
                        if (snap != null)
                        {
                            var preview = snap.Orders?
                                .Where(o => o.Stock > 0)
                                .Take(3)
                                .Select(o =>
                                {
                                    var left = ResolveItemName(o.ItemId, o.ItemShortName);
                                    var right = ResolveItemName(o.CurrencyItemId, o.CurrencyShortName);
                                    return $"{left} for {o.CurrencyAmount} {right}";
                                })
                                .ToList();

                            string firstFew = (preview != null && preview.Count > 0)
                                ? string.Join(", ", preview)
                                : "nothing in stock";

                            string msg =
                                $"Suspicious shop {(snap.Label ?? "Shop")} " +
                                $"[{GetGridLabel(snap)}] was online {Math.Round(lived.TotalSeconds)}s, sold {firstFew}";

                            // fire & forget, kein await hier weil wir in Loop sind
                            _ = SendTeamChatSafeAsync(msg);
                        }

                        life.AnnouncedSuspicious = true;
                    }
                }
            }
        }
    }

    private void RunShopAnalysis()
    {
        if (_analysisList == null) return;

        // TODO: tatsächliche Arbitrage-Logik bauen.
        // Für jetzt nur ein Placeholder, damit's kompiliert.
        _analysisList.Items.Clear();
        _analysisList.Items.Add(new TextBlock
        {
            Text = "Analysis coming soon...",
            Foreground = SearchText
        });
        _analysisList.Visibility = Visibility.Visible;
    }

    private void StopShopPolling()
    {
        if (_shopTimer != null)
        {
            _shopTimer.Stop();
            _shopTimer.Tick -= null;
            _shopTimer = null;
            AppendLog("Shops: Polling off.");
        }
        // optional: bestehende UI leeren
        foreach (var el in _shopEls.Values) Overlay.Children.Remove(el);
        _shopEls.Clear();
    }



    private void UpdateShopsUI(IReadOnlyList<RustPlusClientReal.ShopMarker> shops)
    {
        if (Overlay == null || _worldSizeS <= 0 || _worldRectPx.Width <= 0) return;

        // Index eingehender Marker
        var incoming = new HashSet<uint>();
        foreach (var s in shops)
        {

            incoming.Add(s.Id);
            var p = WorldToImagePx(s.X, s.Y);

            if (!_shopEls.TryGetValue(s.Id, out var el))
            {
                // UI-Element: Punkt + Text nebeneinander
                // var dot = new Ellipse
                //  { 
                var icon = MakeIcon("pack://application:,,,/icons/vending.png", 18);
                //    Width = 10,
                //   Height = 10,
                //    Fill = Brushes.OrangeRed,
                //    Stroke = Brushes.White,
                //    StrokeThickness = 2,
                //    Margin = new Thickness(0, 0, 4, 0)
                //  };

                var txt = new TextBlock
                {
                    // Text = string.IsNullOrWhiteSpace(s.Label) ? "(shop)" : s.Label,
                    Text = "",
                    Foreground = Brushes.White,
                    FontSize = 12,
                    Margin = new Thickness(6, -2, 0, 0)
                };

                var sp = new StackPanel { Orientation = Orientation.Horizontal, Tag = s, Cursor = Cursors.Hand };
                sp.Children.Add(icon);
                sp.Children.Add(txt);

                // Tooltip mit Angeboten
                sp.ToolTip = BuildShopTooltip(s);

                // Klick
                sp.MouseLeftButtonUp += ShopElement_Click;

                _shopEls[s.Id] = sp;
                Overlay.Children.Add(sp);
                Panel.SetZIndex(sp, 850);
                el = sp;
                _shopIconSet.Add(sp);
                ApplyCurrentOverlayScale(el);
            }
            else
            {
                // Daten/Tooltip aktualisieren
                el.Tag = s;
                if (el is FrameworkElement fe) fe.ToolTip = BuildShopTooltip(s);

                // (optional) sichtbaren Namen aktualisieren
                if (el is StackPanel sp && sp.Children.Count >= 2 && sp.Children[1] is TextBlock tb)
                    tb.Text = "";
                // tb.Text = string.IsNullOrWhiteSpace(s.Label) ? "(shop)" : s.Label;
                if (el is FrameworkElement fe2) _shopIconSet.Add(fe2);
            }

            // Position setzen (in Bild-Pixeln; Overlay liegt in der Szene und bekommt keinen eigenen Transform!)
            Canvas.SetLeft(el, p.X - 5);
            Canvas.SetTop(el, p.Y - 5);
        }

        // Entfernen, was nicht mehr kommt
        var toRemove = _shopEls.Keys.Where(id => !incoming.Contains(id)).ToList();
        foreach (var id in toRemove)
        {
            if (_shopEls.TryGetValue(id, out var el))
            {
                Overlay.Children.Remove(el);
                _shopEls.Remove(id);
                if (el is FrameworkElement fe) _shopIconSet.Remove(fe);
            }
        }
        PrefetchShopIcons(shops);
    }

    private object BuildShopTooltip(RustPlusClientReal.ShopMarker s)
    {

        // Container (dunkel, rund, Shadow)
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(32, 36, 40)),
            CornerRadius = new CornerRadius(10),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 12,
                ShadowDepth = 0,
                Opacity = 0.45,
                Color = Colors.Black
            }
        };

        var root = new StackPanel { Orientation = Orientation.Vertical };
        card.Child = root;

        // Titelzeile: Name + Grid
        var title = string.IsNullOrWhiteSpace(s.Label) || LooksLikeOrdersLabel(s.Label) ? "Shop" : s.Label;
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 2, 8) };
        header.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14
        });
        header.Children.Add(new TextBlock
        {
            Text = "  [" + GetGridLabel(s) + "]",
            Foreground = new SolidColorBrush(Color.FromArgb(200, 200, 200, 200)),
            FontSize = 12,
            Margin = new Thickness(6, 0, 0, 0)
        });
        root.Children.Add(header);

        // Orders
        if (s.Orders is { Count: > 0 })
        {
            int shown = 0;
            foreach (var o in s.Orders)
            {
                root.Children.Add(BuildOfferRowUI(o));
                if (++shown >= 12) { root.Children.Add(new TextBlock { Text = "…", Opacity = 0.7, Foreground = Brushes.White }); break; }
            }
        }
        else
        {
            root.Children.Add(new TextBlock
            {
                Text = "(No offers created)",
                Foreground = Brushes.White,
                Opacity = 0.7
            });
        }

        return card; // ToolTip.Content darf ein UIElement sein
    }




    private Window? _shopSearchWin;
    private TextBox? _searchTb;
    private CheckBox? _chkSell;
    private CheckBox? _chkBuy;
    private ListBox? _searchList;
    private List<RustPlusClientReal.ShopMarker> _lastShops = new(); // füllen wir beim Polling

    private Style BuildNiceButtonStyle(
    Color bgNormal,
    Color bgHover,
    Color bgPressed,
    Color borderNormal,
    Color borderPressed,
    Color fgNormal,
    Color fgPressed,
    CornerRadius corner,
    Thickness padding)
    {
        // Border im ControlTemplate
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.Name = "Bd";
        borderFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(bgNormal));
        borderFactory.SetValue(Border.BorderBrushProperty, new SolidColorBrush(borderNormal));
        borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        borderFactory.SetValue(Border.CornerRadiusProperty, corner);

        var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        contentFactory.SetValue(Control.PaddingProperty, padding);

        borderFactory.AppendChild(contentFactory);

        // ControlTemplate für Button
        var template = new ControlTemplate(typeof(Button));
        template.VisualTree = borderFactory;

        // Trigger: Hover
        {
            var triggerHover = new Trigger
            {
                Property = Button.IsMouseOverProperty,
                Value = true
            };
            triggerHover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(bgHover), "Bd"));
            template.Triggers.Add(triggerHover);
        }
        // Trigger: Pressed
        {
            var triggerPressed = new Trigger
            {
                Property = Button.IsPressedProperty,
                Value = true
            };
            triggerPressed.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(bgPressed), "Bd"));
            triggerPressed.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(borderPressed), "Bd"));
            triggerPressed.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(fgPressed)));
            template.Triggers.Add(triggerPressed);
        }
        // Trigger: Disabled
        {
            var triggerDisabled = new Trigger
            {
                Property = UIElement.IsEnabledProperty,
                Value = false
            };
            triggerDisabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5, "Bd"));
            template.Triggers.Add(triggerDisabled);
        }

        // Style-Objekt zusammenbauen
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(fgNormal)));
        style.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0))); // Border machen wir im Template
        style.Setters.Add(new Setter(Control.TemplateProperty, template));

        return style;
    }
    private static void StyleHeaderCheckBox(CheckBox cb, string labelText, bool isInitiallyChecked)
    {
        cb.IsChecked = isInitiallyChecked;
        cb.Cursor = Cursors.Hand;
        cb.ToolTip = labelText;
        cb.Margin = new Thickness(4, 0, 8, 0);
        cb.VerticalAlignment = VerticalAlignment.Center;
        cb.Foreground = Brushes.White;
        cb.Background = Brushes.Transparent;
        cb.BorderThickness = new Thickness(0);
        cb.Focusable = false;

        // Wir bauen das Template einmal hier inline:
        var circleBorder = new FrameworkElementFactory(typeof(Border));
        circleBorder.SetValue(Border.WidthProperty, 16.0);
        circleBorder.SetValue(Border.HeightProperty, 16.0);
        circleBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        circleBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        circleBorder.SetValue(Border.MarginProperty, new Thickness(0, 0, 4, 0));
        circleBorder.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        circleBorder.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center);

        // BorderBrush + Background kommen über Trigger
        circleBorder.SetValue(Border.BorderBrushProperty,
            new SolidColorBrush(Color.FromArgb(160, 255, 255, 255))); // default
        circleBorder.SetValue(Border.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(40, 44, 48))); // default dark bg

        // innerer Punkt
        var dot = new FrameworkElementFactory(typeof(Ellipse));
        dot.SetValue(Ellipse.WidthProperty, 8.0);
        dot.SetValue(Ellipse.HeightProperty, 8.0);
        dot.SetValue(Ellipse.FillProperty,
            new SolidColorBrush(Color.FromRgb(0, 200, 255))); // cyan-ish
        dot.SetValue(Ellipse.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        dot.SetValue(Ellipse.VerticalAlignmentProperty, VerticalAlignment.Center);

        // dot ist nur sichtbar wenn checked -> Trigger unten
        dot.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);

        // Border enthält den Punkt
        var borderPanel = new FrameworkElementFactory(typeof(Grid));
        borderPanel.AppendChild(dot);
        circleBorder.AppendChild(borderPanel);

        // daneben der Text ("Sells", "Buys", etc.)
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, labelText);
        text.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        text.SetValue(TextBlock.FontSizeProperty, 12.0);
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

        // horizontal zusammenbauen
        var rootPanel = new FrameworkElementFactory(typeof(StackPanel));
        rootPanel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        rootPanel.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);
        rootPanel.AppendChild(circleBorder);
        rootPanel.AppendChild(text);

        // Template definieren
        var tpl = new ControlTemplate(typeof(CheckBox));
        tpl.VisualTree = rootPanel;

        // Trigger: wenn Checked == true, dann Border in aktiv-Farbe und Punkt sichtbar
        var tIsChecked = new Trigger
        {
            Property = ToggleButton.IsCheckedProperty,
            Value = true
        };
        tIsChecked.Setters.Add(new Setter(Border.BorderBrushProperty,
            new SolidColorBrush(Color.FromRgb(0, 200, 255)), "circle"));
        tIsChecked.Setters.Add(new Setter(Ellipse.VisibilityProperty,
            Visibility.Visible, dot.Name ?? ""));
        // Trick: wir müssen den Childs Namen geben, damit wir sie im Setter ansprechen können

        // => wir geben denen Namen:
        circleBorder.Name = "circle";
        dot.Name = "dot";

        // jetzt nochmal Trigger definieren MIT Names:
        tIsChecked = new Trigger
        {
            Property = ToggleButton.IsCheckedProperty,
            Value = true
        };
        tIsChecked.Setters.Add(new Setter(Border.BorderBrushProperty,
            new SolidColorBrush(Color.FromRgb(0, 200, 255)), "circle"));
        tIsChecked.Setters.Add(new Setter(Border.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(20, 30, 36)), "circle")); // leicht blauer bg
        tIsChecked.Setters.Add(new Setter(UIElement.VisibilityProperty,
            Visibility.Visible, "dot"));

        tpl.Triggers.Add(tIsChecked);

        cb.Template = tpl;
    }

    private void BtnShopSearch_Click(object sender, RoutedEventArgs e)
    {
        if (_shopSearchWin == null) CreateShopSearchWindow();
        _shopSearchWin.Show();
        _shopSearchWin.Activate();
        RefreshShopSearchResults(); // später: Live-Filter
        _shopSearchWin.Closed += (sender, args) => _shopSearchWin = null;
    }



    private void CreateShopSearchWindow()
    {

        // Farb-Palette ähnlich deinem globalen Theme
        Color colBgNormal = Color.FromRgb(30, 87, 111);   // SurfaceAlt
        Color colBgHover = Color.FromRgb(42, 48, 52);   // Surface
        Color colBgPressed = Color.FromRgb(10, 58, 74);   // AccentDark-ish (leicht blaugrün)
        Color colBorderNorm = Color.FromRgb(79, 195, 247); // helle dünne Linie
        Color colBorderDown = Color.FromRgb(10, 58, 74);   // gleich wie Pressed BG = "eingedrückt"
        Color colFgNormal = Colors.White;                // TextPrimary
        Color colFgPressed = Colors.White;

        var corner = new CornerRadius(10); // dein Radius
        var pad = new Thickness(8, 4, 8, 4);

        Style niceBtnStyle = BuildNiceButtonStyle(
            colBgNormal,
            colBgHover,
            colBgPressed,
            colBorderNorm,
            colBorderDown,
            colFgNormal,
            colFgPressed,
            corner,
            pad
        );

        var w = new Window
        {
            Title = "Shop Search",
            Width = 560,
            Height = 560,
            Owner = this,
            Background = SearchWinBg,
            Foreground = SearchText
        };

        // Root -> vertikal
        var root = new DockPanel
        {
            Margin = new Thickness(10)
        };

        // ========== OBERER BLOCK (Suchleiste, Buttons) ==========
        // Wir machen hier einen StackPanel vertical, damit wir 2 Zeilen + Alerts + Filterzeile etc. haben
        var headerWrap = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 8)
        };

        // --- Zeile 1: [TextBox][Search][Analyze] ---
        var row1 = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4)
        };

      

        // Icon links
        // Farben im Stil deiner UI
        var colOuterBg = Color.FromRgb(24, 26, 28);        // Gesamtfeld-Hintergrund (dunkel)
        var colIconBg = Color.FromRgb(18, 20, 22);        // extra dunkler Block hinter dem Icon
        var colBorder = Color.FromArgb(160, 0, 173, 239); // dein Cyan-ish Border (kannst du anpassen)
        var colText = Colors.White;

        // Äußere Border = komplette Suchleiste mit runden Ecken
        var searchOuter = new Border
        {
            Background = new SolidColorBrush(colOuterBg),
            BorderBrush = new SolidColorBrush(colBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 8, 0), // Abstand rechts zu "Profit Trades"
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true
        };

        // Innenlayout: 2 Spalten (Icon | TextBox)
        var innerGrid = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center
        };
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Icon-Panel links
        var iconHost = new Border
        {
            Background = new SolidColorBrush(colIconBg),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 4, 6, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "🔍",
                Foreground = new SolidColorBrush(colText),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(iconHost, 0);
        innerGrid.Children.Add(iconHost);

        // TextBox rechts – ohne eigenen Border, damit’s wie ein Control wirkt
        _searchTb = new TextBox
        {
            Width = 260,
            Background = Brushes.Transparent, // Wichtig!
            BorderThickness = new Thickness(0),     // kein eigener Rand
            Foreground = new SolidColorBrush(colText),
            Padding = new Thickness(4, 4, 6, 4),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _searchTb.TextChanged += (_, __) => RefreshShopSearchResults();
        Grid.SetColumn(_searchTb, 1);
        innerGrid.Children.Add(_searchTb);

        // pack Grid in die äußere Border
        searchOuter.Child = innerGrid;

        _btnAnalyze = new Button
        {
            Content = " 💰 Profit Trades ",
            Margin = new Thickness(4, 2, 4, 2),
            Style = niceBtnStyle
        };

        _btnPathFinder = new Button
        {
            Content = " 🔍 Buy X for Y ",
            Margin = new Thickness(4, 2, 4, 2),
            Style = niceBtnStyle
        };

        //btnGo.Click += (_, __) => RefreshShopSearchResults();
        _searchTb.TextChanged += (_, __) => RefreshShopSearchResults();

        _btnAnalyze.Click += (_, __) => OpenAnalysisWindow();
        _btnPathFinder.Click += (_, __) => OpenPathFinderWindow();


        row1.Children.Add(searchOuter);
      //  row1.Children.Add(btnGo);
        row1.Children.Add(_btnAnalyze);
        row1.Children.Add(_btnPathFinder);
        headerWrap.Children.Add(row1);

        // --- Zeile 2: Filter-Checkboxen ---
        var row2 = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4)
        };

        _chkSell = new CheckBox();
        StyleHeaderCheckBox(_chkSell, "Sells💰", true);
        

        _chkBuy = new CheckBox();
        StyleHeaderCheckBox(_chkBuy, "Buys🛒", true);
       

        _chkHideEmpty = new CheckBox();
        StyleHeaderCheckBox(_chkHideEmpty, "Hide 0-stock", false);
        
        
        _chkNewShopAlerts = new CheckBox();
        StyleHeaderCheckBox(_chkNewShopAlerts, "New Shops → chat", false);
       

        _chkSuspiciousAlerts = new CheckBox();
        StyleHeaderCheckBox(_chkSuspiciousAlerts, "Suspicious → chat", false);
        

        // Events für Filter/Aktionen
        _chkSell.Checked += (_, __) => RefreshShopSearchResults();
        _chkSell.Unchecked += (_, __) => RefreshShopSearchResults();
        _chkBuy.Checked += (_, __) => RefreshShopSearchResults();
        _chkBuy.Unchecked += (_, __) => RefreshShopSearchResults();
        _chkHideEmpty.Checked += (_, __) => RefreshShopSearchResults();
        _chkHideEmpty.Unchecked += (_, __) => RefreshShopSearchResults();

        // Events für globale Chat-Alarm-Flags
        _chkNewShopAlerts.Checked += (_, __) => { _notifyNewShopsToChat = true; };
        _chkNewShopAlerts.Unchecked += (_, __) => { _notifyNewShopsToChat = false; };

        _chkSuspiciousAlerts.Checked += (_, __) => { _notifySuspiciousShops = true; };
        _chkSuspiciousAlerts.Unchecked += (_, __) => { _notifySuspiciousShops = false; };

        row2.Children.Add(_chkSell);
        row2.Children.Add(_chkBuy);
        row2.Children.Add(_chkHideEmpty);
        row2.Children.Add(_chkNewShopAlerts);
        row2.Children.Add(_chkSuspiciousAlerts);

        headerWrap.Children.Add(row2);

        // --- Zeile 3: Alerts / "+ Alert" Button + Alert-Liste ---
        var row3 = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 4)
        };

        // Add-Alert Button
        _btnAddAlert = new Button
        {
            Content = "＋ Alert (watch this)",
            Margin = new Thickness(0, 0, 0, 4)
        };

        _btnAddAlert.Click += (_, __) => AddAlertFromCurrentSearch();

        // Alert-Liste
        _alertList = new ListBox
        {
            Background = SearchWinBg,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            Foreground = SearchText,
            Height = 70
        };

        row3.Children.Add(_btnAddAlert);
        row3.Children.Add(_alertList);

        headerWrap.Children.Add(row3);

        // Dock das oben rein
        DockPanel.SetDock(headerWrap, Dock.Top);
        root.Children.Add(headerWrap);

        // PATH FINDER WINDOW



        // ========== MAIN SEARCH RESULT LIST ==========
        _searchList = new ListBox
        {
            Background = SearchWinBg,
            BorderThickness = new Thickness(0),
            Foreground = SearchText
        };

        // dezente Auswahl wie vorher
        _searchList.ItemContainerStyle = new Style(typeof(ListBoxItem))
        {
            Setters =
        {
            new Setter(Control.BackgroundProperty, Brushes.Transparent),
            new Setter(Control.BorderThicknessProperty, new Thickness(0)),
            new Setter(Control.PaddingProperty, new Thickness(0))
        }
        };

        // wir packen unten drunter noch eine Analyse-Liste (erstmal collapsed)
        var resultWrap = new StackPanel
        {
            Orientation = Orientation.Vertical
        };

        resultWrap.Children.Add(_searchList);

        // Separator
        resultWrap.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 8, 0, 8),
            Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255))
        });

        _analysisList = new ListBox
        {
            Background = SearchWinBg,
            BorderThickness = new Thickness(0),
            Foreground = SearchText,
            Visibility = Visibility.Collapsed // erst sichtbar machen, wenn Analyze geklickt
        };

        resultWrap.Children.Add(_analysisList);

        var scroll = new ScrollViewer
        {
            Content = resultWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        ApplyThinScrollbar(scroll);

        // Wichtig: kein eigenes Scroll-Handling mehr hier!
        root.Children.Add(scroll);

        // Jetzt sorgen wir dafür, dass das Fenster-Mausrad an den ScrollViewer geht:
        w.PreviewMouseWheel += (snd, e) =>
        {
            // nur eingreifen, wenn der Cursor irgendwo im Scroll-Bereich ist
            // (damit du oben in den Controls nicht plötzlich scrollst, wenn du eigentlich Text selektierst)
            if (!scroll.IsMouseOver) return;

            // standardmäßiges Verhalten: 1 "Wheel-Delta" (120) = ~3-4 Zeilen
            // Wir machen kleine Schritte, nicht riesige Sprünge.
            const double step = 30.0; // Pixel pro Wheel Tick, fühlt sich meist gut an
            double direction = e.Delta > 0 ? -1 : 1;

            double newOffset = scroll.VerticalOffset + direction * step;
            if (newOffset < 0) newOffset = 0;
            if (newOffset > scroll.ScrollableHeight) newOffset = scroll.ScrollableHeight;

            scroll.ScrollToVerticalOffset(newOffset);
            e.Handled = true;
        };

        w.Content = root;
        _shopSearchWin = w;

        // wenn Fenster geschlossen wird, Referenz löschen
        _shopSearchWin.Closed += (_, __) => _shopSearchWin = null;

        // ganz am Ende: initial einmal UI auffüllen
        // nur laden, wenn wir noch keine saved alerts drin haben
        if (_alertRules.All(r => !r.IsSaved))
        {
            LoadPersistentAlerts();
        }
        RefreshAlertListUI();
        RefreshShopSearchResults();
    }

    // PATH FINDER WINDOW LOGIK

    private Window? _pathFinderWin;
    private TextBox? _wantTb;   // linke Suche (Ziel-Item das du haben willst)
    private TextBox? _payTb;    // rechte Suche (Währung / Item das du zahlen willst)
   // private ComboBox? _depthCb; // max Tiefe
    private Button? _runPathBtn;
    private ListBox? _pathResultList;
    private ListBox? _wantPreviewList;
    private ListBox? _payPreviewList;
    private void OpenPathFinderWindow()
    {


        if (_pathFinderWin != null)
        {
            _pathFinderWin.Activate();
            return;
        }

        var w = new Window
        {
            Title = "Buy X for Y",
            Width = 900,
            Height = 600,
            Owner = this,
            Background = SearchWinBg,
            Foreground = SearchText
        };

        // Root dock
        var root = new DockPanel { Margin = new Thickness(10) };

        // === Kopfbereich: zwei Suchfelder nebeneinander + Tiefe + Analyze ===
        var header = new Grid
        {
            Margin = new Thickness(0, 0, 0, 8)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // left want
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // right pay
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // depth
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // analyze btn
        DockPanel.SetDock(header, Dock.Top);

        // SUCHFELD LINKS ("I want to GET ...")
        var wantControl = BuildRoundedSearchField(out _wantTb, "🎯", "I want to GET (e.g. Crude Oil)");
        Grid.SetColumn(wantControl, 0);
        header.Children.Add(wantControl);

        // SUCHFELD RECHTS ("...pay WITH ...")
        var payControl = BuildRoundedSearchField(out _payTb, "💰", "I want to PAY WITH (e.g. Scrap)");
        Grid.SetColumn(payControl, 1);
        header.Children.Add(payControl);

        _wantTb.TextChanged += (_, __) => RefreshPathfinderPreviews();
        _payTb.TextChanged += (_, __) => RefreshPathfinderPreviews();

        // Tiefe-Auswahl
     //   _depthCb = new ComboBox
     //   {

     //       ItemsSource = new[] { "max 2 steps", "max 3 steps", "max 4 steps" },
     //       SelectedIndex = 1, // default 3 steps
     //       Margin = new Thickness(8, 0, 8, 0),
      //      Background = SearchWinBg,
      //      Foreground = SearchText,
      //      BorderBrush = new SolidColorBrush(Color.FromArgb(160, 0, 173, 239)),
     //       BorderThickness = new Thickness(1),
     //       Padding = new Thickness(6, 4, 6, 4),
      //      Width = 80
     //   };
     //   _depthCb.Resources.Add(SystemColors.WindowBrushKey, new SolidColorBrush(Color.FromRgb(24, 26, 28)));
    //    _depthCb.Resources.Add(SystemColors.ControlTextBrushKey, Brushes.White);
     //   _depthCb.Resources.Add(SystemColors.HighlightBrushKey, new SolidColorBrush(Color.FromRgb(0, 173, 239)));
    //    _depthCb.Resources.Add(SystemColors.HighlightTextBrushKey, Brushes.Black);
    //    Grid.SetColumn(_depthCb, 2);
   //     header.Children.Add(_depthCb);

        // Analyze Button
        _runPathBtn = MakeHeaderPillButton(" Analyze ");
        Grid.SetColumn(_runPathBtn, 3);
        _runPathBtn.Click += (_, __) => RunPathAnalysis();
        header.Children.Add(_runPathBtn);

        root.Children.Add(header);

        // Preview-Zweispalter unter den Suchfeldern
        var previewGrid = new Grid
        {
            Margin = new Thickness(0, 0, 0, 8)
        };
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        DockPanel.SetDock(previewGrid, Dock.Top);

        // left preview: what you WANT
        _wantPreviewList = new ListBox
        {
            Background = SearchWinBg,
            BorderThickness = new Thickness(0),
            Foreground = SearchText,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Height = 120 // nicht riesig, nur Vorschau
        };
        Grid.SetColumn(_wantPreviewList, 0);
        previewGrid.Children.Add(_wantPreviewList);
        // Die beiden Preview-Listen oben (optional auch weich scrollen)
        _wantPreviewList.SetValue(VirtualizingStackPanel.IsVirtualizingProperty, true);
        _wantPreviewList.SetValue(VirtualizingStackPanel.ScrollUnitProperty, ScrollUnit.Pixel);
        ApplyThinScrollbar(_wantPreviewList);


        // right preview: what you can PAY
        _payPreviewList = new ListBox
        {
            Background = SearchWinBg,
            BorderThickness = new Thickness(0),
            Foreground = SearchText,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Height = 120
        };
        Grid.SetColumn(_payPreviewList, 1);
        previewGrid.Children.Add(_payPreviewList);
        // Die beiden Preview-Listen oben (optional auch weich scrollen)
        _payPreviewList.SetValue(VirtualizingStackPanel.IsVirtualizingProperty, true);
        _payPreviewList.SetValue(VirtualizingStackPanel.ScrollUnitProperty, ScrollUnit.Pixel);
        ApplyThinScrollbar(_payPreviewList);
        root.Children.Add(previewGrid);

        // === Ergebnisliste unten ===
        _pathResultList = new ListBox
        {
            Background = SearchWinBg,
            BorderThickness = new Thickness(0),
            Foreground = SearchText,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        // weiches Pixel-Scrolling + Virtualisierung
        _pathResultList.SetValue(VirtualizingStackPanel.IsVirtualizingProperty, true);
        _pathResultList.SetValue(VirtualizingStackPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
        _pathResultList.SetValue(VirtualizingStackPanel.ScrollUnitProperty, ScrollUnit.Pixel);
        _pathResultList.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);

        ApplyThinScrollbar(_pathResultList);

        root.Children.Add(_pathResultList);

       

        w.Content = root;
        _pathFinderWin = w;
        w.Closed += (_, __) =>
        {
            _pathFinderWin = null;
            _wantTb = null;
            _payTb = null;
          //  _depthCb = null;
            _runPathBtn = null;
            _pathResultList = null;
        };

        w.Show();
        w.Activate();
    }
    // Style for Scroll bars
   
    private static void ApplyThinScrollbar(FrameworkElement target)
    {
        const string xaml = @"
<ResourceDictionary
    xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">

    <!-- ========= Ultra-Slim Scrollbars ========= -->
    <SolidColorBrush x:Key=""ScrollbarTrackBrush"" Color=""#141820""/>
    <SolidColorBrush x:Key=""ScrollbarThumbBrush"" Color=""#2C3548""/>
    <SolidColorBrush x:Key=""ScrollbarThumbBrushHover"" Color=""#3A4663""/>
    <SolidColorBrush x:Key=""ScrollbarThumbBrushActive"" Color=""#4C5A7A""/>

    <Style x:Key=""SlimThumb"" TargetType=""{x:Type Thumb}"">
        <Setter Property=""Background"" Value=""{StaticResource ScrollbarThumbBrush}""/>
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""{x:Type Thumb}"">
                    <Border x:Name=""B"" Background=""{TemplateBinding Background}"" CornerRadius=""2""/>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""IsMouseOver"" Value=""True"">
                            <Setter TargetName=""B"" Property=""Background"" Value=""{StaticResource ScrollbarThumbBrushHover}""/>
                        </Trigger>
                        <Trigger Property=""IsDragging"" Value=""True"">
                            <Setter TargetName=""B"" Property=""Background"" Value=""{StaticResource ScrollbarThumbBrushActive}""/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType=""{x:Type ScrollBar}"">
        <Setter Property=""Background"" Value=""Transparent""/>
        <Setter Property=""MinWidth"" Value=""0""/>
        <Setter Property=""MinHeight"" Value=""0""/>
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""{x:Type ScrollBar}"">
                    <Grid SnapsToDevicePixels=""True"">
                        <Border Background=""{StaticResource ScrollbarTrackBrush}"" CornerRadius=""2""/>
                        <Track x:Name=""PART_Track""
                               Orientation=""{TemplateBinding Orientation}""
                               IsDirectionReversed=""True""
                               Focusable=""False"">
                            <Track.DecreaseRepeatButton>
                                <RepeatButton Opacity=""0"" IsHitTestVisible=""False""/>
                            </Track.DecreaseRepeatButton>
                            <Track.IncreaseRepeatButton>
                                <RepeatButton Opacity=""0"" IsHitTestVisible=""False""/>
                            </Track.IncreaseRepeatButton>
                            <Track.Thumb>
                                <Thumb Style=""{StaticResource SlimThumb}""/>
                            </Track.Thumb>
                        </Track>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>

        <Style.Triggers>
            <Trigger Property=""Orientation"" Value=""Vertical"">
                <Setter Property=""Width"" Value=""4""/>
                <Setter Property=""Margin"" Value=""0,2,2,2""/>
            </Trigger>
            <Trigger Property=""Orientation"" Value=""Horizontal"">
                <Setter Property=""Height"" Value=""4""/>
                <Setter Property=""Margin"" Value=""2,0,2,2""/>
            </Trigger>
        </Style.Triggers>
    </Style>
</ResourceDictionary>";

        // Dictionary aus XAML parsen
        var dict = (ResourceDictionary)XamlReader.Parse(xaml);

        // ScrollBar-Style aus dem Dictionary holen
        var sbStyle = (Style)dict[typeof(ScrollBar)];
        var thumbStyle = (Style)dict["SlimThumb"];

        // ins lokale Resource-Dict des Elements kippen
        target.Resources[typeof(ScrollBar)] = sbStyle;
        target.Resources["SlimThumb"] = thumbStyle;

        // sicherheitshalber Auto
        ScrollViewer.SetVerticalScrollBarVisibility(target, ScrollBarVisibility.Auto);
    }

    private void RefreshPathfinderPreviews()
    {
        if (_lastShops == null) return;
        if (_wantPreviewList == null || _payPreviewList == null) return;

        string wantTxt = _wantTb?.Text?.Trim() ?? "";
        string payTxt = _payTb?.Text?.Trim() ?? "";

        _wantPreviewList.Items.Clear();
        _payPreviewList.Items.Clear();

        // helper wie in RefreshShopSearchResults
        bool MatchGets(RustPlusClientReal.ShopOrder o, string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return false;
            var pretty = ResolveItemName(o.ItemId, o.ItemShortName);
            return pretty.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        bool MatchPays(RustPlusClientReal.ShopOrder o, string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return false;
            var pretty = ResolveItemName(o.CurrencyItemId, o.CurrencyShortName);
            return pretty.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // LEFT LIST: shops that SELL my wanted item
        if (!string.IsNullOrWhiteSpace(wantTxt))
        {
            foreach (var shop in _lastShops)
            {
                if (shop.Orders == null) continue;

                var matchingOffers = shop.Orders
                    .Where(o => o.Stock > 0 && MatchGets(o, wantTxt))
                    .ToList();

                if (matchingOffers.Count == 0) continue;

                _wantPreviewList.Items.Add(
                    BuildShopSearchCard(shop, matchingOffers, compact: true) // compact für Vorschau
                );
            }

            if (_wantPreviewList.Items.Count == 0)
            {
                _wantPreviewList.Items.Add(new TextBlock
                {
                    Text = "No direct seller for that item.",
                    Foreground = SearchText,
                    Opacity = 0.6
                });
            }
        }
        else
        {
            _wantPreviewList.Items.Add(new TextBlock
            {
                Text = "Type what you WANT to get.",
                Foreground = SearchText,
                Opacity = 0.4
            });
        }

        // RIGHT LIST: shops that ACCEPT what I can PAY
        if (!string.IsNullOrWhiteSpace(payTxt))
        {
            foreach (var shop in _lastShops)
            {
                if (shop.Orders == null) continue;

                var matchingOffers = shop.Orders
                    .Where(o => o.Stock > 0 && MatchPays(o, payTxt))
                    .ToList();

                if (matchingOffers.Count == 0) continue;

                _payPreviewList.Items.Add(
                    BuildShopSearchCard(shop, matchingOffers, compact: true)
                );
            }

            if (_payPreviewList.Items.Count == 0)
            {
                _payPreviewList.Items.Add(new TextBlock
                {
                    Text = "Nobody trades for that (as currency).",
                    Foreground = SearchText,
                    Opacity = 0.6
                });
            }
        }
        else
        {
            _payPreviewList.Items.Add(new TextBlock
            {
                Text = "Type what you CAN pay with.",
                Foreground = SearchText,
                Opacity = 0.4
            });
        }
    }

    // wir benutzen denselben Trick wie bei der Search-Leiste im ShopWindow:
    // eine gemeinsame Border mit Icon links + textbox rechts
    private FrameworkElement BuildRoundedSearchField(out TextBox tb, string iconEmoji, string placeholder)
    {
        var colOuterBg = Color.FromRgb(24, 26, 28);
        var colIconBg = Color.FromRgb(18, 20, 22);
        var colBorder = Color.FromArgb(160, 0, 173, 239);

        var outer = new Border
        {
            Background = new SolidColorBrush(colOuterBg),
            BorderBrush = new SolidColorBrush(colBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.IBeam
        };

        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconHost = new Border
        {
            Background = new SolidColorBrush(colIconBg),
            Padding = new Thickness(6, 4, 6, 4),
            IsHitTestVisible = false, // Klicks gehen durch
            Child = new TextBlock
            {
                Text = iconEmoji,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0)
            }
        };
        Grid.SetColumn(iconHost, 0);
        grid.Children.Add(iconHost);

        tb = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            CaretBrush = Brushes.White,
            SelectionBrush = new SolidColorBrush(Color.FromArgb(160, 0, 173, 239)),
            Padding = new Thickness(2, 4, 6, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
            MinWidth = 250,
            ToolTip = placeholder
        };
        Grid.SetColumn(tb, 1);
        grid.Children.Add(tb);

        // <<< WICHTIG: lokale Kopie für die Lambdas
        var tbLocal = tb;
        outer.MouseLeftButtonDown += (_, __) => tbLocal.Focus();
        grid.MouseLeftButtonDown += (_, __) => tbLocal.Focus();

        outer.Child = grid;
        return outer;
    }

    // gleicher Style wie dein Profit-Trades Button-Knopf
    private Button MakeHeaderPillButton(string text)
    {
        return new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 0, 0),
            Padding = new Thickness(10, 4, 10, 4),
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(24, 26, 28)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(160, 0, 173, 239)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Template = BuildRoundedButtonTemplate()
        };
    }

    private ControlTemplate BuildRoundedButtonTemplate()
    {
        // abgerundet wie deine Accent-Buttons
        var template = new ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.Name = "Bd";
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        borderFactory.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        borderFactory.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });

        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

        borderFactory.AppendChild(cp);
        template.VisualTree = borderFactory;

        // simple Hover Trigger
        var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(32, 34, 38)), "Bd"));

        // Pressed Trigger
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0, 173, 239)), "Bd"));
        pressed.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Black));

        template.Triggers.Add(hover);
        template.Triggers.Add(pressed);

        return template;
    }

    private class PathStep
    {
        public RustPlusClientReal.ShopMarker Shop;
        public RustPlusClientReal.ShopOrder Order;

        public string FromItem = "";
        public string ToItem = "";

        public double PayAmount;
        public string PayPrettyName;
        public double GetAmount;
        public string GetPrettyName;

        public string FromKey;
        public string ToKey;
        public int PayItemId;
        public string PayShortName;
        public string GetShortName;
        public int GetItemId;
    }

    private class TradePathResult
    {
        public string OriginKey = "";       // der Startnode dieser Route
        public List<PathStep> Steps = new(); // in Reihenfolge
    }
    // Repräsentiert eine Handels-Kante: du gibst etwas, bekommst etwas.
    private class TradeEdge
    {
        public string PayShortNameRaw = ""; // o.CurrencyShortName
        public string GetShortNameRaw = ""; // o.ItemShortName
        // Graph-Knoten Keys (stabil, zum Routen)
        public string FromKey = "";   // "was du zahlst"-Knoten
        public string ToKey = "";   // "was du bekommst"-Knoten

        // Für die UI / Mengenanzeige
        public double PayAmount;         // CurrencyAmount
        public double GetAmount;         // Quantity
        public string PayPrettyName = "";
        public string GetPrettyName = "";

        public RustPlusClientReal.ShopMarker Shop;
        public RustPlusClientReal.ShopOrder Order;
    }




    private string NormalizePrettyForKey(string pretty)
    {
        // defensiv
        if (string.IsNullOrWhiteSpace(pretty))
            return "";

        // Beispiel:
        // "x1000 Cloth" -> "x1000_cloth"
        // "Sulfur Ore 400" -> "sulfur_ore_400"
        // "Metal Fragments" -> "metal_fragments"
        var s = pretty.Trim().ToLowerInvariant();

        // ersetze Whitespaces durch underscore
        s = System.Text.RegularExpressions.Regex.Replace(s, "\\s+", "_");

        // optional kannst du Sonderzeichen killen, damit Keys stabiler werden:
        s = System.Text.RegularExpressions.Regex.Replace(s, "[^a-z0-9_]+", "");

        return s;
    }

    private string? MakeItemKey(
        int itemId,
        string? shortName,
        string prettyName // <- NEU: wir geben jetzt den ResolveItemName()-Wert mit rein
    )
    {
        // 1. Echte ItemID (beste Variante, garantiert eindeutig)
        if (itemId > 0)
            return "id:" + itemId.ToString();

        // 2. ShortName aus Rust (zweitbeste Variante)
        if (!string.IsNullOrWhiteSpace(shortName))
            return "sn:" + shortName.Trim().ToLowerInvariant();

        // 3. Fallback auf den hübschen Anzeigenamen (damit Wood/Stone/etc. nicht verschwinden)
        if (!string.IsNullOrWhiteSpace(prettyName))
        {
            var norm = NormalizePrettyForKey(prettyName);
            if (!string.IsNullOrWhiteSpace(norm))
                return "pretty:" + norm;
        }

        // 4. gar nix brauchbares -> wir können keinen stabilen Knoten bauen
        return null;
    }
    private List<TradeEdge> BuildTradeGraphSnapshot()
    {
        var edges = new List<TradeEdge>();

        foreach (var shop in _lastShops)
        {
            if (shop.Orders == null) continue;

            foreach (var o in shop.Orders)
            {
                if (o.Stock <= 0) continue;
                if (o.CurrencyAmount <= 0 || o.Quantity <= 0) continue;

                string payPretty = ResolveItemName(o.CurrencyItemId, o.CurrencyShortName); // was man ZAHLT
                string getPretty = ResolveItemName(o.ItemId, o.ItemShortName);        // was man BEKOMMT

                if (string.IsNullOrWhiteSpace(payPretty)) continue;
                if (string.IsNullOrWhiteSpace(getPretty)) continue;

                // WICHTIG: wir geben jetzt payPretty/getPretty an MakeItemKey weiter
                string? fromKey = MakeItemKey(
                    o.CurrencyItemId,
                    o.CurrencyShortName,
                    payPretty
                );

                string? toKey = MakeItemKey(
                    o.ItemId,
                    o.ItemShortName,
                    getPretty
                );

                if (string.IsNullOrWhiteSpace(fromKey)) continue;
                if (string.IsNullOrWhiteSpace(toKey)) continue;
                if (string.Equals(fromKey, toKey, StringComparison.OrdinalIgnoreCase)) continue;

                edges.Add(new TradeEdge
                {
                    FromKey = fromKey,
                    ToKey = toKey,

                    PayShortNameRaw = o.CurrencyShortName ?? "",
                    GetShortNameRaw = o.ItemShortName ?? "",

                    PayPrettyName = payPretty,
                    GetPrettyName = getPretty,

                    PayAmount = o.CurrencyAmount,
                    GetAmount = o.Quantity,

                    Shop = shop,
                    Order = o
                });
            }
        }

        return edges;
    }


    // 1. helper für string matching
    private bool StrongMatch(string pretty, string raw, string user)
    {
        if (string.IsNullOrWhiteSpace(user)) return false;
        if (!string.IsNullOrWhiteSpace(pretty))
        {
            if (pretty.Equals(user, StringComparison.OrdinalIgnoreCase)) return true;
            if (pretty.StartsWith(user, StringComparison.OrdinalIgnoreCase)) return true;
        }
        if (!string.IsNullOrWhiteSpace(raw))
        {
            if (raw.Equals(user, StringComparison.OrdinalIgnoreCase)) return true;
            if (raw.StartsWith(user, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private bool FuzzyMatch(string pretty, string raw, string user)
    {
        if (string.IsNullOrWhiteSpace(user)) return false;
        if (!string.IsNullOrWhiteSpace(pretty) &&
            pretty.IndexOf(user, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (!string.IsNullOrWhiteSpace(raw) &&
            raw.IndexOf(user, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    // akzeptiert auch "x1000 Stones", "Sulfur Ore", "Wood 500", etc.
    private bool LooseMatchName(string itemPretty, string itemAlt, string userQuery)
    {
        if (string.IsNullOrWhiteSpace(userQuery)) return false;

        // normalize
        string uq = userQuery.Trim().ToLowerInvariant();

        // pretty check
        if (!string.IsNullOrWhiteSpace(itemPretty))
        {
            var p = itemPretty.Trim().ToLowerInvariant();
            if (p.Contains(uq)) return true;
        }

        // alt/fallback check
        if (!string.IsNullOrWhiteSpace(itemAlt))
        {
            var a = itemAlt.Trim().ToLowerInvariant();
            if (a.Contains(uq)) return true;
        }

        return false;
    }

    private bool FirstStepMatchesUser(List<PathStep> steps, string haveQ)
    {
        if (steps.Count == 0) return false;
        var first = steps[0];

        // wichtig: Wir schauen NICHT auf "FromItem" vs. "PayPrettyName" separat,
        // sondern matchen "was wir zahlen" locker gegen die User-Eingabe
        return LooseMatchName(first.PayPrettyName, first.FromItem, haveQ);
    }

    private bool LastStepMatchesUser(List<PathStep> steps, string wantQ)
    {
        if (steps.Count == 0) return false;
        var last = steps[steps.Count - 1];

        // gleiches Prinzip: was wir am Ende bekommen
        return LooseMatchName(last.GetPrettyName, last.ToItem, wantQ);
    }


    private List<TradePathResult> FindPathsItemToItem(
    string payItemQuery,   // RIGHT box: what I HAVE / will pay with
    string wantItemQuery,  // LEFT box: what I WANT to end up with
    int maxDepth)
    {
        var edges = BuildTradeGraphSnapshot();
        string haveQ = payItemQuery?.Trim() ?? "";
        string wantQ = wantItemQuery?.Trim() ?? "";

        AppendLog($"=== PATHFINDER RUN === haveQ='{haveQ}' wantQ='{wantQ}'");

        foreach (var e in edges)
        {
            bool payHit =
                StrongMatch(e.PayPrettyName, e.PayShortNameRaw, haveQ) ||
                FuzzyMatch(e.PayPrettyName, e.PayShortNameRaw, haveQ);

            bool getHit =
                StrongMatch(e.GetPrettyName, e.GetShortNameRaw, wantQ) ||
                FuzzyMatch(e.GetPrettyName, e.GetShortNameRaw, wantQ);

            if (payHit)
            {
              //  AppendLog($"START-CANDIDATE MATCH: haveQ='{haveQ}' matches Pay='{e.PayPrettyName}' raw='{e.PayShortNameRaw}' => FromKey={e.FromKey}");
            }

            if (getHit)
            {
              //  AppendLog($"TARGET-CANDIDATE MATCH: wantQ='{wantQ}' matches Get='{e.GetPrettyName}' raw='{e.GetShortNameRaw}' => ToKey={e.ToKey}");
            }
        }

       

        if (string.IsNullOrWhiteSpace(haveQ) || string.IsNullOrWhiteSpace(wantQ))
            return new List<TradePathResult>();

        // 1) mögliche Start-Knoten = Dinge, die du bezahlen kannst
        // helper wie unten beim finalen Filter, aber hier lokal ohne Order:
        bool LoosePayMatch(TradeEdge e, string userQ)
        {
            return LooseMatchName(e.PayPrettyName, e.PayShortNameRaw, userQ);
        }

        bool LooseGetMatch(TradeEdge e, string userQ)
        {
            return LooseMatchName(e.GetPrettyName, e.GetShortNameRaw, userQ);
        }

        // 1) mögliche Start-Knoten = Dinge, die du zahlen KANNST (RIGHT box)
        var startKeys = edges
            .Where(e => LoosePayMatch(e, haveQ))
            .Select(e => e.FromKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (startKeys.Count == 0)
            return new List<TradePathResult>();

        // 2) mögliche Ziel-Knoten = Dinge, die du HABEN WILLST (LEFT box)
        var targetKeys = edges
            .Where(e => LooseGetMatch(e, wantQ))
            .Select(e => e.ToKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (targetKeys.Count == 0)
            return new List<TradePathResult>();

        // --- 3) BFS ab JEDEM startKey (der Rest bleibt wie du ihn jetzt hast)
        var rawResults = new List<TradePathResult>();
       // AppendLog("startKeys:");
        foreach (var startKey in startKeys)
        {
         //   AppendLog("  " + startKey);
            var q = new Queue<(string curKey, List<PathStep> path)>();
            q.Enqueue((startKey, new List<PathStep>()));

            var visited = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [startKey] = 0
            };

            while (q.Count > 0)
            {
                var (curKey, curPath) = q.Dequeue();
                int depth = curPath.Count;
                if (depth >= maxDepth) continue;

                foreach (var edge in edges)
                {
                    // defensive guard
                    if (string.IsNullOrWhiteSpace(edge.FromKey)) continue;
                    if (!edge.FromKey.Equals(curKey, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var step = new PathStep
                    {
                        Shop = edge.Shop,
                        Order = edge.Order,

                        FromItem = edge.PayPrettyName,   // what we PAY this step
                        ToItem = edge.GetPrettyName,   // what we GET this step

                        PayAmount = edge.PayAmount,
                        PayPrettyName = edge.PayPrettyName,
                        GetAmount = edge.GetAmount,
                        GetPrettyName = edge.GetPrettyName,

                        FromKey = edge.FromKey,
                        ToKey = edge.ToKey
                    };

                    var newPath = new List<PathStep>(curPath) { step };
                    var newKey = edge.ToKey;
                    int newDepth = newPath.Count;

                    // haben wir Ziel getroffen?
                    if (targetKeys.Contains(newKey))
                    {
                        rawResults.Add(new TradePathResult
                        {
                            OriginKey = startKey,
                            Steps = newPath
                        });
                    }

                    if (!visited.TryGetValue(newKey, out var oldDepth) || newDepth < oldDepth)
                    {
                        visited[newKey] = newDepth;
                        q.Enqueue((newKey, newPath));
                    }
                }
            }
        }

        // --- 4) Final filtern & deduplizieren -----------------------------

        // Hilfsfunktionen: prüft, ob der erste Step wirklich mit dem zahlt,
        // was der User rechts eingegeben hat (haveQ),
        // und ob der letzte Step wirklich das ausliefert,
        // was der User links eingegeben hat (wantQ).

        bool FirstStepMatchesUser(List<PathStep> steps, string have)
        {
            if (steps.Count == 0) return false;
            var first = steps[0];

            // was du im ersten Schritt BEZAHLST soll ungefähr dem entsprechen,
            // was du rechts eingetippt hast
            return LooseMatchName(first.PayPrettyName, first.Order?.CurrencyShortName ?? "", have);
        }

        bool LastStepMatchesUser(List<PathStep> steps, string want)
        {
            if (steps.Count == 0) return false;
            var last = steps[steps.Count - 1];

            // was du am Ende BEKOMMST soll ungefähr dem entsprechen,
            // was du links eingetippt hast
            return LooseMatchName(last.GetPrettyName, last.Order?.ItemShortName ?? "", want);
        }

        // echte Filterung
        var filtered = rawResults
            .Where(r => FirstStepMatchesUser(r.Steps, haveQ)
                     && LastStepMatchesUser(r.Steps, wantQ))
            .ToList();

        // Dedup nach (OriginKey -> letzter Node + Länge)
        var dedup = new Dictionary<string, TradePathResult>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in filtered)
        {
            if (r.Steps.Count == 0) continue;
            var lastStep = r.Steps[r.Steps.Count - 1];

            string sig = $"{r.OriginKey}->{lastStep.ToKey}#{r.Steps.Count}";
            if (!dedup.ContainsKey(sig))
            {
                dedup[sig] = r;
            }
        }

        // Optionaler Mini-Filter gegen Frankenstein-Pfade:
        // Kill Pfade, in denen ein Mittelschritt zahlt mit etwas,
        // was er gar nicht direkt vorher bekommen hat ODER was nicht die Startwährung war.
        // (Das unterbindet "kaufe L96 Rifle nur damit du sie NIE einsetzt".)
        bool LooksCoherent(TradePathResult p)
        {
            if (p.Steps.Count == 0) return false;

            // wir tracken "was habe ich nach jedem Schritt im Inventar"
            // Start: wir haben nur die erste Währung
            var haveSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            haveSet.Add(p.Steps[0].PayPrettyName); // Startwährung

            foreach (var st in p.Steps)
            {
                // um st zu bezahlen musst du st.PayPrettyName besitzen
                if (!haveSet.Contains(st.PayPrettyName))
                    return false;

                // nach dem Kauf besitzt du außerdem st.GetPrettyName
                haveSet.Add(st.GetPrettyName);
            }

            return true;
        }

        var coherent = dedup.Values
            .Where(LooksCoherent)
            .ToList();

        return coherent;


    }




    private class ItemInfo2
    {
        public string Pretty = "";
        public string Raw = "";
    }

    private Dictionary<string, ItemInfo2> BuildItemDictionary(List<TradeEdge> edges)
    {
        var dict = new Dictionary<string, ItemInfo2>(StringComparer.OrdinalIgnoreCase);
        void Add(string key, string pretty, string raw)
        {
            if (!dict.ContainsKey(key))
            {
                dict[key] = new ItemInfo2 { Pretty = pretty, Raw = raw };
            }
        }

        foreach (var e in edges)
        {
            Add(e.FromKey, e.PayPrettyName, e.PayShortNameRaw);
            Add(e.ToKey, e.GetPrettyName, e.GetShortNameRaw);
        }

        return dict;
    }

    //BERECHNUNG DER MIN- / MAX:

    private class PathRunSummary
    {
        public string StartName = "";
        public string FinalName = "";
        public double[] MinRuns = Array.Empty<double>(); // length = steps
        public double MinStartCost;   // Kosten für die kleinste sinnvolle Kette (mind. 1x Final)
        public double MinFinalGain;   // Output der kleinsten Kette
        public double[] MaxRuns = Array.Empty<double>();
        public double MaxStartCost;   // Kosten bei Bottleneck-Max
        public double MaxFinalGain;   // Output bei Bottleneck-Max
        public double DroneCost;      // Steps * 20
        public double DroneCostMin;   // Steps * 20
        public double DroneCostMax;   // Steps * 20

        public List<(int stepIndex, double runs)> RunsByStep = new();
        public List<(int stepIndex, double runs)> MinRunsByStep = new(); // <-- NEU
        public Dictionary<string, double> Leftovers = new(StringComparer.OrdinalIgnoreCase);
        public bool MinChainFeasible;
        public List<string> Blockers = new();
    }

    // nimmt deinen fertigen Pfad (Steps in Reihenfolge) und rechnet min/max
    private PathRunSummary? ComputePathRunSummaryStrict(TradePathResult path)
    {
        var steps = path.Steps;
        if (steps.Count == 0) return null;

        var sum = new PathRunSummary();
        var first = steps[0];
        var last = steps[^1];

        sum.StartName = first.PayPrettyName ?? "";
        sum.FinalName = last.GetPrettyName ?? "";

        int n = steps.Count;

        // convenient arrays
        var stock = new double[n];
        var payAmt = new double[n];
        var getAmt = new double[n];
        var payNam = new string[n];
        var getNam = new string[n];

        for (int i = 0; i < n; i++)
        {
            var st = steps[i];
            stock[i] = st.Order?.Stock ?? 0;
            payAmt[i] = st.PayAmount;
            getAmt[i] = st.GetAmount;
            payNam[i] = st.PayPrettyName ?? "";
            getNam[i] = st.GetPrettyName ?? "";
        }

        // --- MIN CHAIN ---
        // Force 1 run of the LAST step (buy exactly one final order).
        // Then back-propagate required runs for previous steps via ceil().
        var minRuns = new double[n];
        minRuns[^1] = 1;

        for (int i = n - 2; i >= 0; i--)
        {
            if (getAmt[i] <= 0) return null; // broken offer
                                             // produce enough output for the next step’s total pay
            double needOutNext = minRuns[i + 1] * payAmt[i + 1];
            double req = Math.Ceiling(needOutNext / getAmt[i]);

            // must be positive and within stock
            if (req <= 0 || req > stock[i]) return null; // bottleneck => reject entire path
            minRuns[i] = req;
        }

        // Forward sanity: each step i must have enough input from i-1
        for (int i = 1; i < n; i++)
        {
            double producedPrev = minRuns[i - 1] * getAmt[i - 1];
            double needForThis = minRuns[i] * payAmt[i];
            if (needForThis > producedPrev) return null; // inconsistency => reject
        }

        // MIN numbers are valid
        sum.MinRuns = minRuns;
        sum.MinStartCost = minRuns[0] * payAmt[0];
        sum.MinFinalGain = minRuns[^1] * getAmt[^1];

        // --- MAX CHAIN ---
        // Start optimistic: each step could run at its stock,
        // then iteratively clamp to keep producer/consumer consistent.
        var runs = new double[n];
        for (int i = 0; i < n; i++) runs[i] = stock[i];

        for (int iter = 0; iter < 12; iter++)
        {
            bool changed = false;

            // backward: earlier steps must produce enough for later steps
            for (int i = n - 2; i >= 0; i--)
            {
                double needOutNext = runs[i + 1] * payAmt[i + 1];
                double canPerRun = getAmt[i];
                double reqRunsI = canPerRun > 0 ? Math.Ceiling(needOutNext / canPerRun) : 0;

                if (reqRunsI < 0) reqRunsI = 0;
                if (reqRunsI > stock[i]) reqRunsI = stock[i];

                if (Math.Abs(runs[i] - reqRunsI) > 0.0001)
                {
                    runs[i] = reqRunsI;
                    changed = true;
                }
            }

            // forward: later steps cannot consume more than previous produce
            for (int i = 1; i < n; i++)
            {
                double producedPrev = runs[i - 1] * getAmt[i - 1];
                double maxRunsI = payAmt[i] > 0 ? Math.Floor(producedPrev / payAmt[i]) : 0;
                if (maxRunsI < 0) maxRunsI = 0;
                if (maxRunsI > stock[i]) maxRunsI = stock[i];

                if (runs[i] > maxRunsI + 0.0001)
                {
                    runs[i] = maxRunsI;
                    changed = true;
                }
            }

            if (!changed) break;
        }

        sum.MaxRuns = runs;
        sum.MaxStartCost = runs[0] * payAmt[0];
        sum.MaxFinalGain = runs[^1] * getAmt[^1];

        // Drone costs are PER STEP, not per trade
        sum.DroneCostMin = n * 20.0;
        sum.DroneCostMax = n * 20.0;

        // Leftovers for MAX case (nice to show)
        var leftovers = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < n - 1; i++)
        {
            double produced = runs[i] * getAmt[i];
            double consumed = runs[i + 1] * payAmt[i + 1];
            double lf = produced - consumed;
            if (lf > 0.0001)
            {
                string key = getNam[i];
                leftovers[key] = leftovers.TryGetValue(key, out var v) ? v + lf : lf;
            }
        }
        foreach (var kv in leftovers)
            if (!kv.Key.Equals(sum.FinalName, StringComparison.OrdinalIgnoreCase))
                sum.Leftovers[kv.Key] = kv.Value;

        return sum;
    }



    private FrameworkElement? BuildPathCard(TradePathResult path)
    {

        var summary = ComputePathRunSummaryStrict(path);
        if (summary == null) return null; // reject whole path (true bottleneck)

        var outer = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(24, 26, 28)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        outer.Child = grid;

        // LEFT
        var left = new StackPanel { Orientation = Orientation.Vertical };
        left.Children.Add(new TextBlock
        {
            Text = $"Get {summary.FinalName} starting from {summary.StartName} in {path.Steps.Count} step(s)",
            Foreground = SearchText,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 4)
        });

        // Steps with "Current stock" and "Min to reach goal: xN"
        for (int i = 0; i < path.Steps.Count; i++)
            left.Children.Add(BuildStepRowWithMin(i + 1, path.Steps[i], summary.MinRuns[i]));

        Grid.SetColumn(left, 0);
        grid.Children.Add(left);


        // RIGHT
        var summaryBox = BuildSummaryBoxStrict(summary, path);
        grid.Children.Add(summaryBox);
        Grid.SetColumn(summaryBox, 1);

        return outer;
    }

    // helper to keep earlier style
    private FrameworkElement BuildStepRowWithMin(int idx, PathStep step, double minRunsForStep)
    {
        var rowOuter = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 4, 0, 0)
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rowOuter.Child = row;

        var left = new StackPanel { Orientation = Orientation.Vertical};

        // Zeile mit Icons „pay → get“
        left.Children.Add(new TextBlock
        {
            Text = $"Step {idx}:",
            Foreground = SearchText,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2)
        });
        left.Children.Add(BuildPayGetRow(step));  // <- deine vorhandene Icon-Zeile

        // Zusatzinfos
        left.Children.Add(new TextBlock { Text = $"Current stock: {step.Order?.Stock ?? 0}", Foreground = SearchText, FontSize = 11, Opacity = 0.85 });
        left.Children.Add(new TextBlock { Text = $"Min to reach goal: x{Math.Max(1, Math.Floor(minRunsForStep))}", Foreground = SearchText, FontSize = 11, Opacity = 0.85 });
        left.Children.Add(new TextBlock { Text = $"{(step.Shop.Label ?? "Shop")} [{GetGridLabel(step.Shop)}]", Foreground = SearchText, FontSize = 11, Opacity = 0.8 });

        Grid.SetColumn(left, 0);
        row.Children.Add(left);

        var goBtn = MakeHeaderPillButton("Go");
        goBtn.Margin = new Thickness(8, 0, 0, 0);
        goBtn.Click += (_, __) => CenterMapOnWorld(step.Shop.X, step.Shop.Y);
        Grid.SetColumn(goBtn, 1);
        row.Children.Add(goBtn);

        return rowOuter;
    }

    private static Border UiSeparator(double top = 6, double bottom = 6)
    {
        return new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            Margin = new Thickness(0, top, 0, bottom)
        };
    }

    // "Icon + xMenge" – nutzt DEIN bestehendes BindIcon.
    private FrameworkElement IconWithQty(string? shortName, int itemId, double qty,
                                     double iconSize = 24, double fontSize = 13, bool bold = true)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var img = new Image { Width = iconSize, Height = iconSize, Margin = new Thickness(0, 0, 6, 0) };
        BindIcon(img, shortName, itemId); // <- deine vorhandene Funktion

        var txt = new TextBlock
        {
            Text = $"x{Math.Floor(qty)}",
            Foreground = SearchText,
            FontSize = fontSize,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center
        };

        row.Children.Add(img);
        row.Children.Add(txt);
        return row;
    }

    private FrameworkElement BuildSummaryBoxStrict(PathRunSummary sum, TradePathResult path)
    {
        var first = path.Steps.First();
        var last = path.Steps.Last();

        // Fallbacks falls Order null sein sollte
        string? startShort = first.Order?.CurrencyShortName ?? first.PayPrettyName;
        int startId = first.Order?.CurrencyItemId ?? 0;

        string? finalShort = last.Order?.ItemShortName ?? last.GetPrettyName;
        int finalId = last.Order?.ItemId ?? 0;

        var box = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(40, 44, 48)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 260
        };

        var st = new StackPanel { Orientation = Orientation.Vertical };
        box.Child = st;

        // Titel
        st.Children.Add(new TextBlock
        {
            Text = "Max @ current stock:",
            Foreground = SearchText,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12
        });

        // Große Symbolzeile: [Pay xN] -> [Get xN]
        var big = new Grid { Margin = new Thickness(0, 4, 0, 6) };
        big.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        big.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        big.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var payIcon = IconWithQty(startShort, startId, sum.MaxStartCost, 24, 13, true);
        Grid.SetColumn(payIcon, 0); big.Children.Add(payIcon);

        var arrow = new TextBlock { Text = "  →  ", Foreground = SearchText, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(arrow, 1); big.Children.Add(arrow);

        var getIcon = IconWithQty(finalShort, finalId, sum.MaxFinalGain, 24, 13, true);
        Grid.SetColumn(getIcon, 2); big.Children.Add(getIcon);

        st.Children.Add(big);

        // Klartext-Zeile
        st.Children.Add(new TextBlock
        {
            Text = $"Get {Math.Floor(sum.MaxFinalGain)} {sum.FinalName} for {Math.Floor(sum.MaxStartCost)} {sum.StartName}",
            Foreground = SearchText,
            FontSize = 12
        });

        // Leftovers
        if (sum.Leftovers.Count > 0)
        {
            var leftStr = string.Join(", ", sum.Leftovers.Select(k => $"{Math.Floor(k.Value)} {k.Key}"));
            st.Children.Add(new TextBlock
            {
                Text = $"Leftovers: {leftStr}",
                Foreground = SearchText,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        // Drone costs – nimm DroneCostMax, sonst auf DroneCost/n*20 zurückfallen
        double drone = sum.DroneCostMax > 0 ? sum.DroneCostMax : (sum.DroneCost > 0 ? sum.DroneCost : path.Steps.Count * 20);
        st.Children.Add(new TextBlock
        {
            Text = $"Drone costs: {Math.Floor(drone)} Scrap",
            Foreground = SearchText,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 2)
        });

        st.Children.Add(UiSeparator());

        // Aggregierte Step-Totals für MAX-Plan
        for (int i = 0; i < path.Steps.Count; i++)
        {
            var step = path.Steps[i];
            double r = (sum.MaxRuns != null && i < sum.MaxRuns.Length) ? sum.MaxRuns[i] : 0.0;
            if (r <= 0) continue;

            double totalPay = r * step.PayAmount;
            double totalGet = r * step.GetAmount;

            st.Children.Add(new TextBlock
            {
                Text = $"Step {i + 1}: Pay {Math.Floor(totalPay)} {step.PayPrettyName} → Get {Math.Floor(totalGet)} {step.GetPrettyName}",
                Foreground = SearchText,
                FontSize = 11
            });
        }

        st.Children.Add(UiSeparator());

        // Min-Chain ganz unten
        var minLine = $"Min chain: Pay {Math.Floor(sum.MinStartCost)} {sum.StartName} → Get {Math.Floor(sum.MinFinalGain)} {sum.FinalName}";
        if (!sum.MinChainFeasible && (sum.Blockers?.Count > 0))
            minLine += "  (not feasible at current stock)";

        st.Children.Add(new TextBlock { Text = minLine, Foreground = SearchText, FontSize = 11 });

        if (!sum.MinChainFeasible && (sum.Blockers?.Count > 0))
        {
            st.Children.Add(new TextBlock
            {
                Text = "Bottleneck: " + sum.Blockers[0],
                Foreground = SearchText,
                FontSize = 11,
                Opacity = 0.9
            });
        }

        return box;
    }



    private FrameworkElement BuildSummaryBox(PathRunSummary sum, TradePathResult path)
    {
        var box = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(40, 44, 48)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6),
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 220
        };

        var st = new StackPanel { Orientation = Orientation.Vertical };
        box.Child = st;

        st.Children.Add(new TextBlock
        {
            Text = "Max @ current stock:",
            Foreground = SearchText,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12
        });
       

        st.Children.Add(new TextBlock
        {
            Text = $"Get {Math.Floor(sum.MaxFinalGain)} {sum.FinalName} for {Math.Floor(sum.MaxStartCost)} {sum.StartName}",
            Foreground = SearchText,
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 4)
        });

        st.Children.Add(new TextBlock
        {
            Text = $"Drone costs: {Math.Floor(sum.DroneCost)} Scrap",
            Foreground = SearchText,
            FontSize = 11
        });

        if (sum.Leftovers.Count > 0)
        {
            var leftoverStrs = sum.Leftovers
                .Select(kvp => $"{Math.Floor(kvp.Value)} {kvp.Key}");

            st.Children.Add(new TextBlock
            {
                Text = "Leftovers: " + string.Join(", ", leftoverStrs),
                Foreground = SearchText,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 4)
            });
        }
        else
        {
            st.Children.Add(new TextBlock
            {
                Text = "",
                FontSize = 4
            });
        }

        // Detail-Zeilen pro Step mit aggregierten Mengen
        var steps = path.Steps;
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var (idx, r) = sum.RunsByStep[i]; // idx == i, r == runs[i]

            if (r <= 0) continue; // wenn wir den Step wirklich gar nicht fahren, skippen

            double totalPay = r * step.PayAmount;
            double totalGet = r * step.GetAmount;

            st.Children.Add(new TextBlock
            {
                Text = $"Step {i + 1}: Pay {Math.Floor(totalPay)} {step.PayPrettyName} → Get {Math.Floor(totalGet)} {step.GetPrettyName}",
                Foreground = SearchText,
                FontSize = 11
            });
            if (sum.MinFinalGain > 0)
            {
                var minLine = $"Min chain: Pay {Math.Floor(sum.MinStartCost)} {sum.StartName} → Get {Math.Floor(sum.MinFinalGain)} {sum.FinalName}";
                if (!sum.MinChainFeasible && sum.Blockers.Count > 0)
                    minLine += "  (not feasible at current stock)";

                

                if (!sum.MinChainFeasible && sum.Blockers.Count > 0)
                {
                    st.Children.Add(new TextBlock
                    {
                        Text = "Bottleneck: " + sum.Blockers[0],
                        Foreground = SearchText,
                        FontSize = 11,
                        Opacity = 0.9
                    });
                }
            }
        }
        var minLine2 = $"Min chain: Pay {Math.Floor(sum.MinStartCost)} {sum.StartName} → Get {Math.Floor(sum.MinFinalGain)} {sum.FinalName}";
        st.Children.Add(new TextBlock
        {
            Text = minLine2,
            Foreground = SearchText,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 2)
        });

        return box;
    }

    private FrameworkElement BuildStepRow(int idx, PathStep step, PathRunSummary summary)
    {
        var rowOuter = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 4, 0, 0)
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rowOuter.Child = row;

        // LEFT
        var leftStack = new StackPanel { Orientation = Orientation.Vertical };

        // Zeile mit Icons „pay → get“
        leftStack.Children.Add(BuildPayGetRow(step));

        // Current stock
        leftStack.Children.Add(new TextBlock
        {
            Text = $"Current stock: {step.Order?.Stock ?? 0}",
            Foreground = SearchText,
            FontSize = 11,
            Opacity = 0.85
        });

        // Min xN (aus MIN-Summary)
        var minPair = summary.MinRunsByStep.FirstOrDefault(t => t.stepIndex == (idx - 1));
        if (minPair.runs > 0)
        {
            leftStack.Children.Add(new TextBlock
            {
                Text = $"Min to reach goal: x{minPair.runs}",
                Foreground = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
                FontSize = 11
            });
        }

        // Shop Info
        leftStack.Children.Add(new TextBlock
        {
            Text = $"{(step.Shop.Label ?? "Shop")} [{GetGridLabel(step.Shop)}]",
            Foreground = SearchText,
            FontSize = 12,
            Opacity = 0.8
        });

        Grid.SetColumn(leftStack, 0);
        row.Children.Add(leftStack);

        // RIGHT: Go
        var goBtn = MakeHeaderPillButton("Go");
        goBtn.Margin = new Thickness(8, 0, 0, 0);
        goBtn.Click += (_, __) => CenterMapOnWorld(step.Shop.X, step.Shop.Y);

        Grid.SetColumn(goBtn, 1);
        row.Children.Add(goBtn);

        return rowOuter;
    }

    private FrameworkElement BuildPayGetRow(PathStep st)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var payIcon = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 0, 6, 0) };
        BindIcon(payIcon, st.Order?.CurrencyShortName, st.Order?.CurrencyItemId ?? 0);
        g.Children.Add(payIcon);

        var payTxt = new TextBlock
        {
            Text = $"Pay {st.PayAmount} {st.PayPrettyName}",
            Foreground = SearchText,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(payTxt, 1);
        g.Children.Add(payTxt);

        var arrow = new TextBlock
        {
            Text = "  →  ",
            Margin = new Thickness(6, 0, 6, 0),
            Foreground = SearchText,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(arrow, 2);
        g.Children.Add(arrow);

        var getIcon = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 0, 6, 0) };
        BindIcon(getIcon, st.Order?.ItemShortName, st.Order?.ItemId ?? 0);
        Grid.SetColumn(getIcon, 3);
        g.Children.Add(getIcon);

        var getTxt = new TextBlock
        {
            Text = $"Get {st.GetAmount} {st.GetPrettyName}",
            Foreground = SearchText,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(getTxt, 4);
        g.Children.Add(getTxt);

        return g;
    }

    private void RunPathAnalysis()
    {
        if (_pathFinderWin == null || _pathResultList == null) return;

        string wantTxt = _wantTb?.Text ?? "";
        string payTxt = _payTb?.Text ?? "";

        int depth = 4;
       // if (_depthCb?.SelectedIndex == 0) depth = 2;
      //  else if (_depthCb?.SelectedIndex == 1) depth = 3;
      //  else if (_depthCb?.SelectedIndex == 2) depth = 4;

        _pathResultList.Items.Clear();
        _pathResultList.Items.Add(new TextBlock
        {
            Text = "Searching...",
            Foreground = SearchText
        });

        // aktuell synchron, was bei deiner Scale noch okay ist
        var paths = FindPathsItemToItem(payTxt, wantTxt, depth);

        _pathResultList.Items.Clear();

        if (paths.Count == 0)
        {
            _pathResultList.Items.Add(new TextBlock
            {
                Text = "No route found.",
                Foreground = SearchText
            });
            return;
        }

        // Sort shortest first, keep up to 30, but only those that pass strict summary
        int shown = 0;
        foreach (var p in paths.OrderBy(p => p.Steps.Count))
        {
            var card = BuildPathCard(p);
            if (card != null)
            {
                _pathResultList.Items.Add(card);
                if (++shown >= 30) break;
            }
        }
        if (shown == 0)
        {
            _pathResultList.Items.Add(new TextBlock { Text = "No valid route (bottlenecks).", Foreground = SearchText });
        }
    }

    private void RefreshShopSearchResults()
    {
        if (_shopSearchWin == null || _searchList == null) return;

        string q = _searchTb?.Text?.Trim() ?? "";

        bool wantSell = _chkSell?.IsChecked != false;
        bool wantBuy = _chkBuy?.IsChecked != false;

        bool hideEmpty = _chkHideEmpty?.IsChecked == true;

        // Helper zum Text-Match
        bool MatchesLeft(RustPlusClientReal.ShopOrder o)
            => string.IsNullOrEmpty(q)
               || ResolveItemName(o.ItemId, o.ItemShortName)
                  .Contains(q, StringComparison.OrdinalIgnoreCase);

        bool MatchesRight(RustPlusClientReal.ShopOrder o)
            => string.IsNullOrEmpty(q)
               || ResolveItemName(o.CurrencyItemId, o.CurrencyShortName)
                  .Contains(q, StringComparison.OrdinalIgnoreCase);

        _searchList.Items.Clear();

        foreach (var s in _lastShops)
        {
            if (s.Orders == null || s.Orders.Count == 0) continue;

            // Deine angepasste Filter-Logik:
            var offers = s.Orders
                .Where(o =>
                    ((wantSell && MatchesLeft(o)) || (wantBuy && MatchesRight(o))) &&
                    (!hideEmpty || o.Stock > 0)
                )
                .ToList();

            if (hideEmpty && offers.Count == 0)
                continue; // ganzer Shop raus, weil nur 0-stock

            if (offers.Count == 0)
                continue;

            // Karte bauen und hinzufügen
            _searchList.Items.Add(BuildShopSearchCard(s, offers, compact: false));
        }
    }

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

    // Weltpunkt (x,y) in die Mitte des sichtbaren Bereichs schieben – Zoom bleibt unverändert
    private void CenterMapOnWorld(double x, double y)
    {
        if (_worldSizeS <= 0) return;

        // 1) Welt -> Szenenpixel (Bildkoordinaten innerhalb des Welt-Quadrats)
        var p = WorldToImagePx(x, y); // benutzt _worldRectPx + _worldSizeS

        // 2) Viewbox-Skalierung + Offsets (Uniform-Scale + evtl. Letterbox-Ränder)
        var (s, offX, offY) = GetViewboxScaleAndOffset(); // <- hast du schon (wird auch fürs Panning benutzt)

        // 3) Gewünschte Host-Mitte
        double hostCx = WebViewHost.ActualWidth * 0.5;
        double hostCy = WebViewHost.ActualHeight * 0.5;

        // 4) Aktuelle Matrix lesen, nur die Translation neu setzen
        var m = MapTransform.Matrix;
        double sx = Math.Abs(m.M11) < 1e-9 ? 1 : m.M11;
        double sy = Math.Abs(m.M22) < 1e-9 ? 1 : m.M22;

        // Wir wollen: off + s * (scale * p + offset) == HostCenter  ⇒  offset = (HostCenter - off)/s - scale*p
        m.OffsetX = (hostCx - offX) / s - sx * p.X;
        m.OffsetY = (hostCy - offY) / s - sy * p.Y;

        MapTransform.Matrix = m;
    }

    private void ShopElement_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is RustPlusClientReal.ShopMarker s)
        {
            CenterMapOnWorld(s.X, s.Y);
            AppendLog($"Shop clicked: {s.Label ?? "(no Label)"} @ ({s.X:0},{s.Y:0}) | offers={s.Orders?.Count ?? 0}");
            // hier könntest du später ein richtiges Popup öffnen
        }
    }

    // SHOP ANALYTICS AND ALARM MECHANICS

   

    private Window? _analysisWin;
    private ListBox? _analysisListBox;
    private void OpenAnalysisWindow()
    {
        if (_analysisWin != null)
        {
            _analysisWin.Activate();
            RefreshAnalysisWindow();
            return;
        }

        var w = new Window
        {
            Title = "Profit Trades",
            Width = 900,
            Height = 600,
            Owner = this,
            Background = SearchWinBg,
            Foreground = SearchText,
            WindowStyle = WindowStyle.SingleBorderWindow,
            ResizeMode = ResizeMode.CanResizeWithGrip
        };

        // Root Dock
        var root = new DockPanel
        {
            LastChildFill = true,
            Margin = new Thickness(0)
        };
        w.Content = root;

        // ===== HEADER BAR (oben dunkel, mit Icon links + Titel + Refresh-Button rechts) =====
        var headerBar = new Grid
        {
            Background = new SolidColorBrush(Color.FromRgb(20, 22, 25)),
            Height = 32,
        };
        headerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Linker Teil vom Header: kleines Icon + "Profit Trades"
        var headerLeft = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };

        // Du kannst hier ein echtes Bild/Icon nehmen – ich nehme erstmal ein Emoji
        var headerIcon = new TextBlock
        {
            Text = "💰",
            Foreground = Brushes.Gold,
            FontSize = 14,
            Margin = new Thickness(0, -1, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var headerTitle = new TextBlock
        {
            Text = "Profit Trades",
            Foreground = Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        headerLeft.Children.Add(headerIcon);
        headerLeft.Children.Add(headerTitle);

        Grid.SetColumn(headerLeft, 0);
        headerBar.Children.Add(headerLeft);

        // Rechts im Header: der "runde Refresh"-Button
        var btnRefresh = new Button
        {
            Width = 24,
            Height = 24,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(40, 44, 48)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = "Refresh profit scan",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Content = new TextBlock
            {
                Text = "⟳", // kannst auch ein eigenes Icon-Bild einsetzen
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };

       

        btnRefresh.Click += (_, __) => RefreshAnalysisWindow();

        Grid.SetColumn(btnRefresh, 1);
        headerBar.Children.Add(btnRefresh);

        DockPanel.SetDock(headerBar, Dock.Top);
        root.Children.Add(headerBar);

        // ===== INFO BAR direkt unter Header =====
        var infoBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(28, 30, 33)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 8, 10, 8)
        };

        var infoText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromArgb(220, 220, 220, 220)),
            FontSize = 12,
            Text =
                "Direct arbitrage opportunities | buy low → sell high." +
                
                " (Click Go to center shop.)"
        };

        infoBar.Child = infoText;

        DockPanel.SetDock(infoBar, Dock.Top);
        root.Children.Add(infoBar);

        // ===== MAIN SCROLL AREA =====
        _analysisListBox = new ListBox
        {
            Background = SearchWinBg,
            BorderThickness = new Thickness(0),
            Foreground = SearchText,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10),
        };

        // dünnere Scrollbar für diese ListBox
        var thinScrollStyle = new Style(typeof(ScrollBar));
        thinScrollStyle.Setters.Add(new Setter(ScrollBar.WidthProperty, 6.0));
        thinScrollStyle.Setters.Add(new Setter(Control.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))));
        thinScrollStyle.Setters.Add(new Setter(Control.ForegroundProperty,
            new SolidColorBrush(Color.FromArgb(160, 255, 255, 255))));
        _analysisListBox.Resources.Add(typeof(ScrollBar), thinScrollStyle);

        var scrollHost = new ScrollViewer
        {
            Content = _analysisListBox,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = SearchWinBg,
        };
        ApplyThinScrollbar(scrollHost);

        w.PreviewMouseWheel += (s, e) =>
        {
            // nur scrollen, wenn Maus auch über dem Scrollbereich ist
            if (!scrollHost.IsMouseOver) return;

            const double step = 30;               // 30px pro Tick, kannst du ändern
            double dir = e.Delta > 0 ? -1 : 1;    // nach oben / nach unten
            double target = scrollHost.VerticalOffset + dir * step;

            if (target < 0) target = 0;
            if (target > scrollHost.ScrollableHeight) target = scrollHost.ScrollableHeight;

            scrollHost.ScrollToVerticalOffset(target);
            e.Handled = true;
        };

        root.Children.Add(scrollHost);

        // Fenster events
        _analysisWin = w;

        w.Closed += (_, __) =>
        {
            _analysisWin = null;
            _analysisListBox = null;
        };

        RefreshAnalysisWindow();

        w.Show();
        w.Activate();
    }
    private void RefreshAnalysisWindow()
    {
        if (_analysisWin == null || _analysisListBox == null) return;

        _analysisListBox.Items.Clear();

        if (_lastShops == null || _lastShops.Count == 0)
        {
            _analysisListBox.Items.Add(new TextBlock
            {
                Text = "No shop data yet.",
                Foreground = SearchText
            });
            return;
        }

        var flips = FindTwoStepFlips(_lastShops);

        _analysisListBox.Items.Add(new TextBlock
        {
            Text = "Possible 2-step profit loops",
            Foreground = SearchText,
            Margin = new Thickness(0, 0, 0, 8)
        });

        if (flips.Count == 0)
        {
            _analysisListBox.Items.Add(new TextBlock
            {
                Text = "No profitable flips found.",
                Foreground = SearchText
            });
            return;
        }

        int shown = 0;
        foreach (var flip in flips)
        {
            _analysisListBox.Items.Add(BuildFlipCard(flip));
            if (++shown >= 20) break;
        }
    }

    

   

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

        // Anti-Spam pro Order-Key
        public Dictionary<string, DateTime> LastAnnouncements { get; } = new();
    }
    // Liste aller aktiven Alarmregeln
    private readonly List<ShopAlertRule> _alertRules = new();

    // ====== SHOP SEARCH WINDOW UI-Elemente ======
    // Erweiterungen, die wir neu brauchen:
    private CheckBox? _chkHideEmpty;            // "Hide 0-stock"
    private CheckBox? _chkNewShopAlerts;        // "New shop alerts to chat"
    private CheckBox? _chkSuspiciousAlerts;     // "Suspicious shop alerts"
    private Button? _btnAddAlert;               // "+ Alert" Button
    private ListBox? _alertList;                // Liste der aktiven Alerts im UI
    private Button? _btnAnalyze;                // "Analyze" Button
    private Button? _btnPathFinder;                // "PathFinder" Button
    private ListBox? _analysisList;             // Ergebnisse der Analyse

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
                    // 1. Passt überhaupt zur Regel?
                    bool matchesSide =
                        (rule.MatchSellSide && MatchOrderLeft(order, rule.QueryText)) ||
                        (rule.MatchBuySide && MatchOrderRight(order, rule.QueryText));

                    if (!matchesSide)
                        continue;

                    // 2. nix melden bei leerem Stock
                    if (order.Stock <= 0)
                        continue;

                    // 3. Haben wir diesen Deal-Typ schon als bekannt markiert?
                    //    "Deal-Typ" definieren wir als: Shop + Item + Währung + Menge pro Trade + Preis.
                    //    (Stock lassen wir absichtlich raus, sonst spammt er bei jeder Stockänderung)
                    bool alreadyKnown = rule.Baseline.Any(b =>
                        b.ShopId == shop.Id &&
                        b.ItemShort == order.ItemShortName &&
                        b.CurrencyShort == order.CurrencyShortName &&
                        b.Quantity == order.Quantity &&
                        Math.Abs(b.CurrencyAmount - order.CurrencyAmount) < 0.001f
                    );

                    if (alreadyKnown)
                        continue;

                    // 4. Pro-Order Spam-Schutz: 1 Meldung pro 60s für exakt diese Kombi
                    string sig = $"{shop.Id}:{order.ItemShortName}:{order.CurrencyShortName}:{order.Quantity}:{order.CurrencyAmount}";
                    if (rule.LastAnnouncements.TryGetValue(sig, out var lastWhen) &&
                        (DateTime.UtcNow - lastWhen).TotalSeconds < 60)
                    {
                        continue;
                    }

                    // 5. Globales Rate Limit: nur 1 Chat-Nachricht pro Sekunde
                    if ((DateTime.UtcNow - _lastChatSendUtc).TotalSeconds < 1.0)
                        continue;
                    _lastChatSendUtc = DateTime.UtcNow;

                    // 6. Chattext bauen
                    string grid = GetGridLabel(shop);

                    string itemName = ResolveItemName(order.ItemId, order.ItemShortName);
                    string currencyName = ResolveItemName(order.CurrencyItemId, order.CurrencyShortName);

                    string verb = rule.MatchSellSide ? "sells" : "buys";

                    string msg =
                        $"{(shop.Label ?? "Shop")} [{grid}] {verb} " +
                        $"x{order.Quantity} {itemName} (Stock {order.Stock}) " +
                        $"for {order.CurrencyAmount} {currencyName}";

                    // Immer ins lokale Log schreiben – unabhängig von Chat/Sound
                    AppendLog($"[{DateTime.Now:HH:mm:ss}] Alert: {msg}");

                    // 7. Ausspielen (Chat + optional Sound)
                    if (rule.NotifyChat)
                    {
                        await SendTeamChatSafeAsync(msg);
                    }

                    if (rule.NotifySound)
                    {
                        PlayShopAlertSound();
                    }

                    // 8. Diesen Deal-Typ jetzt dauerhaft als bekannt markieren,
                    //    damit er NICHT bei jedem Poll wieder als "neu" zählt.
                    rule.Baseline.Add(new AlertSeenOrder
                    {
                        ShopId = shop.Id,
                        ItemShort = order.ItemShortName,
                        CurrencyShort = order.CurrencyShortName,
                        Quantity = order.Quantity,
                        CurrencyAmount = order.CurrencyAmount,
                        Stock = order.Stock
                    });

                    // 9. Cooldown-Zeitpunkt merken
                    rule.LastAnnouncements[sig] = DateTime.UtcNow;
                }
            }
        }
    }

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
    private bool _notifyNewShopsToChat = false;
    private bool _notifySuspiciousShops = false;


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
    private void PlayShopAlertSound()
    {
        try
        {
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cash.wav");
            if (System.IO.File.Exists(path))
            {
                var player = new System.Media.SoundPlayer(path);
                player.Play();
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
                await _pairing.StopAsync();
                await Task.Delay(200); // kleine Atempause
            }

            if (File.Exists(PairingConfigPath))
            {
                File.Delete(PairingConfigPath);
                AppendLog($"🗑️ Deleted pairing config: {PairingConfigPath}");
                TxtPairingState.Text = "Pairing: config deleted";
                return true;
            }
            else
            {
                AppendLog("ℹ️ No pairing config found to delete.");
                return false;
            }
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
    private const string RepoOwner = "Pronwan";
    private const string RepoName = "rustplus-desktop";
    private const string InstallerAssetName = "RustPlusDesk-Setup.exe";

    // aktuelle App-Version ermitteln (fallback auf 0.0.0 wenn nicht gesetzt)
    private static Version GetCurrentVersion()
    {
        try
        {
            // zuerst Produktversion aus Datei
            var fvi = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrWhiteSpace(fvi.ProductVersion) && Version.TryParse(NormalizeVer(fvi.ProductVersion), out var v1))
                return v1;

            // dann AssemblyVersion
            var v2 = Assembly.GetExecutingAssembly().GetName().Version;
            if (v2 != null) return v2;

            return new Version(0, 0, 0);
        }
        catch { return new Version(0, 0, 0); }
    }

    private static string NormalizeVer(string s)
    {
        // entfernt leading 'v' und schneidet evtl. Suffixe (z.B. "-beta") ab
        if (string.IsNullOrWhiteSpace(s)) return "0.0.0";
        s = s.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s[1..];
        // „1.2.3-beta+build“ -> „1.2.3“
        int dash = s.IndexOfAny(new[] { '-', '+' });
        if (dash > 0) s = s[..dash];
        return s;
    }

    private sealed record GitHubRelease(string TagName, string? Name, string? Body, List<GitHubAsset> Assets);
    private sealed record GitHubAsset(string Name, string BrowserDownloadUrl);

    private async Task<(Version latest, string tag, string? downloadUrl)?> GetLatestReleaseAsync()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RustPlusDesk", GetCurrentVersion().ToString()));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
        using var resp = await http.GetAsync(url);
        if (!resp.IsSuccessStatusCode)
        {
            AppendLog($"❌ GitHub API error: {(int)resp.StatusCode} {resp.ReasonPhrase}");
            return null;
        }

        using var stream = await resp.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var assets = root.GetProperty("assets").EnumerateArray();

        string? dl = null;
        foreach (var a in assets)
        {
            var name = a.GetProperty("name").GetString() ?? "";
            if (string.Equals(name, InstallerAssetName, StringComparison.OrdinalIgnoreCase))
            {
                dl = a.GetProperty("browser_download_url").GetString();
                break;
            }
        }

        var v = NormalizeVer(tag);
        if (!Version.TryParse(v, out var latest))
        {
            AppendLog($"⚠️ Could not parse version from tag “{tag}”.");
            return null;
        }
        return (latest, tag, dl);
    }

    private async Task<string?> DownloadInstallerAsync(string url, IProgress<double>? progress = null)
    {
        var target = System.IO.Path.Combine(System.IO.Path.GetTempPath(), InstallerAssetName);
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RustPlusDesk", GetCurrentVersion().ToString()));

            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();

            var total = resp.Content.Headers.ContentLength;
            using var input = await resp.Content.ReadAsStreamAsync();
            using var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long readTotal = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await file.WriteAsync(buffer, 0, read);
                readTotal += read;
                if (total.HasValue) progress?.Report(readTotal / (double)total.Value);
            }
            return target;
        }
        catch (Exception ex)
        {
            AppendLog("❌ Download failed: " + ex.Message);
            return null;
        }
    }

    private async Task<bool> StartInstallerAndExitAsync(string installerPath)
    {
        try
        {
            AppendLog("Starting installer …");
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
                Verb = "runas" // UAC prompt, falls nötig
            };
            Process.Start(psi);

            // optional: Listener ordentlich stoppen
            try { if (_pairing?.IsRunning == true) await _pairing.StopAsync(); } catch { }

            // kleines Delay, dann App schließen
            await Task.Delay(500);
            System.Windows.Application.Current.Shutdown();
            return true;
        }
        catch (Exception ex)
        {
            AppendLog("❌ Could not start installer: " + ex.Message);
            return false;
        }
    }
    private void BtnPatchNotes_Click(object sender, RoutedEventArgs e)
    {
        var win = new Views.PatchNotesWindow
        {
            Owner = this
        };
        win.Show();
        win.Activate();
    }
    private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_listenerStarting) return;

        try
        {
            _vm.IsBusy = true;
            _vm.BusyText = "Checking GitHub release …";

            var curr = AppInfo.VersionForCompare;
            var latestInfo = await GetLatestReleaseAsync();
            if (latestInfo is null)
            {
                _vm.IsBusy = false; _vm.BusyText = "";
                System.Windows.MessageBox.Show(
                    "Could not query latest release. Please try again or open Releases page.",
                    "Update", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var (latest, tag, dlUrl) = latestInfo.Value;
            AppendLog($"Current: {AppInfo.VersionShort} | Latest: {latest} ({tag})");

            if (latest <= curr)
            {
                _vm.IsBusy = false; _vm.BusyText = "";
                System.Windows.MessageBox.Show("You are up to date.", "Update", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // neuere Version vorhanden
            if (string.IsNullOrWhiteSpace(dlUrl))
            {
                _vm.IsBusy = false; _vm.BusyText = "";
                var open = System.Windows.MessageBox.Show(
                    $"New version available: {tag}\nOpen Releases page?",
                    "Update available", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (open == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo($"https://github.com/{RepoOwner}/{RepoName}/releases/latest") { UseShellExecute = true });
                return;
            }

            var ask = System.Windows.MessageBox.Show(
                $"New version available: {tag}\nDownload and install now?",
                "Update available", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (ask != MessageBoxResult.Yes)
            {
                _vm.IsBusy = false; _vm.BusyText = "";
                return;
            }

            // Download mit einfachem Progress in BusyText
            var prog = new Progress<double>(p =>
            {
                _vm.BusyText = $"Downloading installer … {(int)(p * 100)}%";
            });
            var path = await DownloadInstallerAsync(dlUrl!, prog);

            _vm.IsBusy = false; _vm.BusyText = "";

            if (path == null)
            {
                System.Windows.MessageBox.Show("Download failed.", "Update", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await StartInstallerAndExitAsync(path);
        }
        catch (Exception ex)
        {
            _vm.IsBusy = false;
            _vm.BusyText = "";
            AppendLog("❌ Update check failed: " + ex.Message);
            System.Windows.MessageBox.Show("Update check failed.\n" + ex.Message, "Update", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
        // 1) Versuche aus dem DataContext (VM)
        var enumerable = (DataContext as dynamic)?.CurrentDevices as IEnumerable
                         ?? ListDevices.ItemsSource as IEnumerable; // 2) Fallback: direkt aus der ListBox
        if (enumerable == null) return null;

        foreach (var obj in enumerable)
            if (obj is SmartDevice sd && sd.EntityId == entityId)
                return sd;

        return null;
    }

    private async Task ToggleSequenceAsync(IEnumerable<long> entityIds)
    {
        foreach (var id in entityIds)
        {
            var dev = FindDevice(id);
            if (dev == null) continue;
            if (!string.Equals(dev.Kind, "SmartSwitch", StringComparison.OrdinalIgnoreCase)) continue;
            if (dev.IsMissing) continue;

            bool current = dev.IsOn ?? false;
            bool desired = !current; // „wie Klick” → invertieren

            var fakeSender = new System.Windows.FrameworkElement { DataContext = dev };
            await HandleDeviceToggleAsync(fakeSender, desired);

            await Task.Delay(650); // etwas Luft zwischen Requests
            if (_rust == null) break; // Verbindung weg? Abbrechen
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
    }

    private void BtnHotkeys_Click(object sender, RoutedEventArgs e)
    {
        // 1) während des Editierens immer pausieren
        DeactivateHotkeys();

        IEnumerable? src = (ListDevices.ItemsSource as IEnumerable) ?? (DataContext as IEnumerable);
        if (src == null) { MessageBox.Show("No devices."); return; }
        var smartDevices = src.OfType<SmartDevice>();

        // 2) Dialog öffnen
        var dlg = new HotkeysWindow(smartDevices, MapForCurrentServer()) { Owner = this };
        bool? activate = dlg.ShowDialog();       // true = Activate, false/null = Deactivate

        // 3) Änderungen speichern
        SaveHotkeys();

        // 4) je nach Wahl aktivieren/deaktivieren
        if (activate == true) ActivateHotkeysForCurrentServer();
        else DeactivateHotkeys();
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

    private bool _overlayToolsVisible = false;

    // wer ist aktuell ausgewählt als Zeichenwerkzeug?
    private enum OverlayToolMode { None, Draw, Text, Icon, Erase }
    private OverlayToolMode _currentTool = OverlayToolMode.None;

    // Color/Size Settings usw.:
    private Color _drawColor = Colors.Red;
    private double _drawThickness = 2.0;
    private double _eraserSize = 10.0;
    private Color _textColor = Colors.White;
    private double _textSize = 16.0;
    private string _currentIconPath = "pack://application:,,,/icons/map-icons/base1.png";

    // Für Draggen von platzierten Icons/Text
    private FrameworkElement? _draggingElement = null;
    private Point _dragOffset;

    // Stroke-Zeichnen
    private bool _isDrawingStroke = false;
    private Polyline? _currentStroke;

    // Overlays der Teammitglieder
    // wer ist aktuell "eingeblendet" (Avatar aktiv)
    private readonly HashSet<ulong> _visibleOverlayOwners = new();

    // pro Spieler: Liste ALLER FrameworkElements (Polylines, Icons, Text) aus seinem Overlay
    private readonly Dictionary<ulong, List<FrameworkElement>> _playerOverlayElements = new();




    private void RebuildOverlayTeamBar()
    {
        if (OverlayTeamStack == null) return;

        OverlayTeamStack.Children.Clear();

        foreach (var tm in TeamMembers)
        {
            // Wir wollen nur existierende Spieler mit valider SteamID
            if (tm.SteamId == 0)
                continue;

            // Button mit Avatar als Inhalt
            var btn = new Button
            {
                Width = 32,
                Height = 32,
                Margin = new Thickness(4, 0, 4, 0),
                ToolTip = tm.Name,    // Steam-Name als Tooltip
                Tag = tm.SteamId,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(_visibleOverlayOwners.Contains(tm.SteamId) ? 2 : 1),
                BorderBrush = _visibleOverlayOwners.Contains(tm.SteamId)
                                ? Brushes.LimeGreen
                                : Brushes.Gray,
                Background = Brushes.Transparent
            };

            btn.Click += OverlayTeamButton_Click;

            var img = new Image
            {
                Width = 32,
                Height = 32,
                Stretch = Stretch.UniformToFill,
                Source = tm.Avatar ?? GetPlaceholderAvatar(), // falls Avatar noch lädt
                SnapsToDevicePixels = true
            };

            btn.Content = img;
            OverlayTeamStack.Children.Add(btn);
        }
    }

    private ImageSource GetPlaceholderAvatar()
    {
        // Kannst du schöner machen (graues Quadrat, Fragezeichen, etc.)
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.DimGray, null, new Rect(0, 0, 32, 32));
        }
        var bmp = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    private async void OverlayTeamButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        if (b.Tag is not ulong steamId) return;

        AppendLog($"[overlay/ui] Click on {steamId} ({GetServerKey()})");

        // ----- FALL 1: Spieler ist gerade sichtbar -> wir blenden ihn aus
        if (_visibleOverlayOwners.Contains(steamId))
        {
            AppendLog($"[overlay/ui] {steamId} currently visible -> hiding");

            _visibleOverlayOwners.Remove(steamId);

            if (_playerOverlayElements.TryGetValue(steamId, out var listToHide))
            {
                foreach (var fe in listToHide)
                    fe.Visibility = Visibility.Collapsed;
            }

            RebuildOverlayTeamBar();
            return;
        }

        // ----- FALL 2: Spieler ist nicht sichtbar -> wir wollen ihn einblenden
        AppendLog($"[overlay/ui] {steamId} currently NOT visible -> showing / (re)loading if needed");

        _visibleOverlayOwners.Add(steamId);

        var localPath = GetOverlayJsonPathForPlayerServer(steamId);
        bool hadLocalBefore = File.Exists(localPath);
        AppendLog($"[overlay/ui] local file {(hadLocalBefore ? "exists" : "does NOT exist")} at {localPath}");

        // 1. Versuch: vom Server holen (kann 200, 404 oder Fehler sein)
        bool serverGaveNewData = await TryFetchAndUpdateOverlayAsync(steamId);

        AppendLog($"[overlay/ui] serverGaveNewData={serverGaveNewData}");

        // 2. Schauen ob wir schon Canvas-Objekte im Speicher haben
        bool alreadyBuiltInMemory =
            _playerOverlayElements.TryGetValue(steamId, out var existingList)
            && existingList != null
            && existingList.Count > 0;

        AppendLog($"[overlay/ui] alreadyBuiltInMemory={alreadyBuiltInMemory} " +
                  $"(count={(existingList?.Count ?? 0)})");

        // 3. Müssen wir neu bauen?
        bool needRebuild = !alreadyBuiltInMemory || serverGaveNewData;
        AppendLog($"[overlay/ui] needRebuild={needRebuild}");

        if (needRebuild)
        {
            if (File.Exists(localPath))
            {
                // bevor rebuild: alte Elemente entfernen
                if (alreadyBuiltInMemory && existingList != null)
                {
                    AppendLog($"[overlay/ui] Removing {existingList.Count} old canvas elements for {steamId} before rebuild");
                    foreach (var fe in existingList)
                        Overlay.Children.Remove(fe);
                }

                AppendLog($"[overlay/ui] Rebuilding overlay for {steamId} from disk {localPath}");
                LoadOverlayForPlayerFromJson(steamId);
            }
            else
            {
                AppendLog($"[overlay/ui] No local JSON for {steamId} after fetch -> creating empty entry");
                if (!_playerOverlayElements.ContainsKey(steamId))
                    _playerOverlayElements[steamId] = new List<FrameworkElement>();
            }
        }

        // 4. Sichtbar machen, was wir haben
        if (_playerOverlayElements.TryGetValue(steamId, out var listNow))
        {
            AppendLog($"[overlay/ui] Final step: showing {listNow.Count} elements for {steamId}");
            foreach (var fe in listNow)
                fe.Visibility = Visibility.Visible;
        }
        else
        {
            AppendLog($"[overlay/ui][warn] No entry in _playerOverlayElements for {steamId}, creating empty list");
            _playerOverlayElements[steamId] = new List<FrameworkElement>();
        }

        RebuildOverlayTeamBar();
    }

    private void LoadOverlayForPlayerFromJson(ulong steamId)
    {
        var path = GetOverlayJsonPathForPlayerServer(steamId);

        if (!File.Exists(path))
        {
            AppendLog($"[overlay/disk] {steamId}: no local file at {path}, registering empty");
            if (!_playerOverlayElements.ContainsKey(steamId))
                _playerOverlayElements[steamId] = new List<FrameworkElement>();
            return;
        }

        OverlaySaveData? data = null;
        try
        {
            var json = File.ReadAllText(path);
            data = System.Text.Json.JsonSerializer.Deserialize<OverlaySaveData>(json);
        }
        catch (Exception ex)
        {
            AppendLog($"[overlay/disk][err] {steamId}: failed to parse {path}: {ex.Message}");
            data = null;
        }

        if (data == null)
        {
            AppendLog($"[overlay/disk][warn] {steamId}: parsed data == null, registering empty");
            _playerOverlayElements[steamId] = new List<FrameworkElement>();
            return;
        }

        bool editable = (steamId == _mySteamId);

        AppendLog($"[overlay/disk] {steamId}: materializing from {path} " +
                  $"(strokes={data.Strokes.Count}, icons={data.Icons.Count}, texts={data.Texts.Count}, editable={editable})");

        MaterializeOverlayForPlayer(steamId, data, editable);

        // Nach MaterializeOverlayForPlayer weißt du,
        // wie viele Elemente der Spieler jetzt wirklich auf dem Canvas hat:
        if (_playerOverlayElements.TryGetValue(steamId, out var listBuilt))
        {
            AppendLog($"[overlay/disk] {steamId}: materialized {listBuilt.Count} canvas elements");
        }
    }

    private async Task<bool> TryFetchAndUpdateOverlayAsync(ulong steamId)
    {
        try
        {
            var serverKey = GetServerKey();
            var ts = UnixNow().ToString();

            var msg = $"{steamId}|{serverKey}|{ts}";
            var sig = HmacSha256Hex(OVERLAY_SYNC_SECRET_HEX, msg);

            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(5);

                var url = OVERLAY_SYNC_BASEURL
                    + "/fetch"
                    + "?steamId=" + WebUtility.UrlEncode(steamId.ToString())
                    + "&serverKey=" + WebUtility.UrlEncode(serverKey)
                    + "&ts=" + WebUtility.UrlEncode(ts)
                    + "&sig=" + WebUtility.UrlEncode(sig);

               // AppendLog($"[overlay/net] GET {url}");

                var resp = await http.GetAsync(url);

                if (resp.StatusCode == HttpStatusCode.NotFound)
                {
                    AppendLog($"[overlay/net] {steamId}: 404 (no remote overlay available)");
                    return false;
                }
                if (!resp.IsSuccessStatusCode)
                {
                    AppendLog($"[overlay/net][warn] {steamId}: fetch failed HTTP {(int)resp.StatusCode}");
                    return false;
                }

                var bodyJson = await resp.Content.ReadAsStringAsync();
                var doc = System.Text.Json.JsonDocument.Parse(bodyJson);

                if (!doc.RootElement.TryGetProperty("overlayJsonB64", out var b64El))
                {
                    AppendLog($"[overlay/net][warn] {steamId}: server response missing overlayJsonB64");
                    return false;
                }

                var b64 = b64El.GetString();
                if (string.IsNullOrEmpty(b64))
                {
                    AppendLog($"[overlay/net][warn] {steamId}: overlayJsonB64 empty");
                    return false;
                }

                var raw = Convert.FromBase64String(b64);
                var remoteJson = Encoding.UTF8.GetString(raw);

                // remote parsed
                OverlaySaveData? remoteData = null;
                try
                {
                    remoteData = System.Text.Json.JsonSerializer.Deserialize<OverlaySaveData>(remoteJson);
                }
                catch
                {
                    remoteData = null;
                }

                if (remoteData == null)
                {
                    AppendLog($"[overlay/net][warn] {steamId}: remote json invalid");
                    return false;
                }

                long remoteTs = remoteData.LastUpdatedUnix;
                var path = GetOverlayJsonPathForPlayerServer(steamId);

                long localTs = 0;
                OverlaySaveData? localData = null;

                if (File.Exists(path))
                {
                    try
                    {
                        var localJson = File.ReadAllText(path);
                        localData = System.Text.Json.JsonSerializer.Deserialize<OverlaySaveData>(localJson);
                    }
                    catch { /* ignore */ }

                    if (localData != null)
                        localTs = localData.LastUpdatedUnix;
                }

                if (steamId == _mySteamId)
                {
                    // eigenes Overlay -> nur überschreiben, wenn remote neuer ist
                    AppendLog($"[overlay/net] self {steamId}: remoteTs={remoteTs}, localTs={localTs}");

                    if (remoteTs > localTs)
                    {
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                        File.WriteAllText(path, remoteJson);
                        AppendLog($"[overlay/net] self {steamId}: wrote NEWER remote overlay to {path} (remote newer)");
                        return true; // lokal geupdatet
                    }
                    else
                    {
                        AppendLog($"[overlay/net] self {steamId}: kept LOCAL overlay (local newer or same)");
                        return false;
                    }
                }
                else
                {
                    // fremdes Overlay -> immer remote Wahrheit
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, remoteJson);
                    AppendLog($"[overlay/net] teammate {steamId}: wrote remote overlay to {path} (always trust remote)");
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog("[overlay/net][err] fetch error for " + steamId + ": " + ex.Message);
            return false;
        }
    }

    private Image BuildPlayerOverlayImageFor(ulong steamId)
    {
        string path = GetOverlayPngPathForPlayer(steamId);

        BitmapImage? bi = null;
        if (File.Exists(path))
        {
            using (var fs = File.OpenRead(path))
            {
                var tmp = new BitmapImage();
                tmp.BeginInit();
                tmp.CacheOption = BitmapCacheOption.OnLoad;
                tmp.StreamSource = fs;
                tmp.EndInit();
                tmp.Freeze();
                bi = tmp;
            }
        }

        // Fallback: leeres transparentes "nichts", falls noch keine Datei existiert,
        // damit unser Code nicht crasht, wenn jemand klickt ohne PNG zu haben.
        int mapW = (int)(ImgMap?.Source?.Width ?? 2048);   // fallback
        int mapH = (int)(ImgMap?.Source?.Height ?? 2048);  // fallback

        var img = new Image
        {
            Source = bi,
            Width = bi?.PixelWidth ?? mapW,
            Height = bi?.PixelHeight ?? mapH,
            Opacity = 0.8,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,

            // wichtig: gleiche Transform wie Map, damit es mitzoomt/panned
            RenderTransform = MapTransform
        };

        Canvas.SetLeft(img, 0);
        Canvas.SetTop(img, 0);

        return img;
    }

    private string GetOverlayPngPathForPlayer(ulong steamId)
    {
        var baseDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RustPlusDesk",
            "Overlays",
            GetServerKey()
        );
        Directory.CreateDirectory(baseDir);
        return System.IO.Path.Combine(baseDir, $"{steamId}.png");
    }
    // Folgende Methode ersetzt durch LoadOverlayFromDiskForPlayer --
    private void LoadOwnOverlayFromJson()
    {
        // Falls wir für mich (_mySteamId) schon Elemente gebaut haben, nicht nochmal
        if (_playerOverlayElements.ContainsKey(_mySteamId) &&
            _playerOverlayElements[_mySteamId].Count > 0)
        {
            return;
        }

        var path = GetOverlayJsonPathForPlayerServer(_mySteamId);
        if (!File.Exists(path))
        {
            // Stelle sicher, dass wir zumindest einen leeren Eintrag haben,
            // damit spätere Checks nicht glauben "muss noch laden".
            _playerOverlayElements[_mySteamId] = new List<FrameworkElement>();
            return;
        }

        OverlaySaveData? data = null;
        try
        {
            var json = File.ReadAllText(path);
            data = System.Text.Json.JsonSerializer.Deserialize<OverlaySaveData>(json);
        }
        catch
        {
            // kaputte Datei? -> wir tun so, als gäbe es keine
            data = null;
        }

        if (data == null)
        {
            _playerOverlayElements[_mySteamId] = new List<FrameworkElement>();
            return;
        }

        // Wir bauen jetzt meine Shapes.
        var myList = new List<FrameworkElement>();

        // 1) Strokes
        foreach (var stroke in data.Strokes)
        {
            var pl = new Polyline
            {
                StrokeThickness = stroke.Thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeStartLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
                Opacity = 1.0,
                Tag = new OverlayTag
                {
                    OwnerSteamId = _mySteamId,
                    IsUserEditable = true
                }
            };

            if (ColorConverter.ConvertFromString(stroke.Color) is Color c)
                pl.Stroke = new SolidColorBrush(c);

            foreach (var p in stroke.Points)
                pl.Points.Add(p);

            Overlay.Children.Add(pl);
            myList.Add(pl);
            RegisterElementForOwner(_mySteamId, pl);
        }

        // 2) Icons
        foreach (var icon in data.Icons)
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(icon.IconPath, UriKind.RelativeOrAbsolute);
            bi.EndInit();
            bi.Freeze();

            var img = new Image
            {
                Source = bi,
                Width = icon.Width,
                Height = icon.Height,
                RenderTransformOrigin = new Point(0.5, 0.5),
                IsHitTestVisible = true, // meine Icons/Text darf ich draggen und löschen
                Opacity = 1.0,
                Tag = new OverlayTag
                {
                    OwnerSteamId = _mySteamId,
                    IsUserEditable = true
                }
            };

            Canvas.SetLeft(img, icon.X);
            Canvas.SetTop(img, icon.Y);

            Overlay.Children.Add(img);
            myList.Add(img);
        }

        // 3) Texts
        foreach (var txt in data.Texts)
        {
            var tb = new TextBlock
            {
                Text = txt.Content,
                FontSize = txt.FontSize,
                FontWeight = txt.Bold ? FontWeights.Bold : FontWeights.Normal,
                IsHitTestVisible = true,
                Opacity = 1.0,
                Tag = new OverlayTag
                {
                    OwnerSteamId = _mySteamId,
                    IsUserEditable = true
                }
            };

            if (ColorConverter.ConvertFromString(txt.Color) is Color tc)
                tb.Foreground = new SolidColorBrush(tc);

            Canvas.SetLeft(tb, txt.X);
            Canvas.SetTop(tb, txt.Y);

            Overlay.Children.Add(tb);
            myList.Add(tb);
        }

        // Abschluss: registrieren
        _playerOverlayElements[_mySteamId] = myList;

        // Sichtbarkeit / Auswahl-Status:
        // Wir wollen, dass mein Overlay direkt sichtbar und in der Auswahl aktiv ist,
        // also packen wir mich in _visibleOverlayOwners.
        _visibleOverlayOwners.Add(_mySteamId);
    }

    private void HandleOverlayMouseDown(MouseButtonEventArgs e, Point mapPos)
    {
        // 1) DRAW
        if (_currentTool == OverlayToolMode.Draw &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            _isDrawingStroke = true;
            _currentStroke = new Polyline
            {
                Stroke = new SolidColorBrush(_drawColor),
                StrokeThickness = _drawThickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeStartLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
                Tag = new OverlayTag { OwnerSteamId = _mySteamId, IsUserEditable = true }
            };
            _currentStroke.Points.Add(mapPos);
            Overlay.Children.Add(_currentStroke);

            // registrieren
            RegisterElementForOwner(_mySteamId, _currentStroke);
            return;
        }

        // 2) ICON
        if (_currentTool == OverlayToolMode.Icon &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            PlaceIconAt(mapPos);
            return;
        }

        // 3) TEXT
        if (_currentTool == OverlayToolMode.Text &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            PlaceTextAt(mapPos);
            return;
        }

        // 4) ERASE
        if (_currentTool == OverlayToolMode.Erase &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            EraseAt(mapPos);
            return;
        }

        // 5) Drag bestehender Elemente (wenn kein spezielles Tool aktiv ist,
        //    oder wir explizit Drag erlauben bei Icon/Text in anderen Tools außer Draw/Text/Icon/Erase)
        if (e.LeftButton == MouseButtonState.Pressed &&
            _currentTool == OverlayToolMode.None)
        {
            TryBeginDragExistingElement(mapPos);
            return;
        }

        // 6) Rechtsklick zum Löschen von Icons/Text-Blöcken
        if (e.ChangedButton == MouseButton.Right)
        {
            TryDeleteElementAt(mapPos);
            return;
        }
    }

    private void EraseAt(Point mapPos)
    {
        var toRemove = new List<Polyline>();

        for (int i = 0; i < Overlay.Children.Count; i++)
        {
            if (Overlay.Children[i] is Polyline line)
            {
                // nur wenn mir gehörend, sonst Finger weg
                if (line.Tag is OverlayTag meta && meta.OwnerSteamId == _mySteamId && meta.IsUserEditable)
                {
                    double dist = DistancePointToPolyline(mapPos, line);
                    if (dist <= _eraserSize)
                    {
                        toRemove.Add(line);
                    }
                }
            }
        }

        foreach (var line in toRemove)
            Overlay.Children.Remove(line);

        if (toRemove.Count > 0)
            SaveOwnOverlayToJson();
    }

    private void HandleOverlayMouseMove(MouseEventArgs e, Point mapPos)
    {
        // Stroke weiterzeichnen
        if (_currentTool == OverlayToolMode.Draw &&
            _isDrawingStroke &&
            _currentStroke != null)
        {
            _currentStroke.Points.Add(mapPos);
            return;
        }

        if (_currentTool == OverlayToolMode.Erase &&
    e.LeftButton == MouseButtonState.Pressed)
        {
            EraseAt(mapPos);
            return;
        }

        // Drag laufend verschieben
        if (_draggingElement != null &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            Canvas.SetLeft(_draggingElement, mapPos.X - _dragOffset.X);
            Canvas.SetTop(_draggingElement, mapPos.Y - _dragOffset.Y);
            return;
        }
    }

    private void HandleOverlayMouseUp(MouseButtonEventArgs e, Point mapPos)
    {
        if (_currentTool == OverlayToolMode.Draw)
        {
            _isDrawingStroke = false;
            _currentStroke = null;
            SaveOwnOverlayToJson();
        }

        if (_currentTool == OverlayToolMode.Erase)
        {
            SaveOwnOverlayToJson();
        }

        if (_draggingElement != null)
        {
            _draggingElement = null;
            SaveOwnOverlayToJson();
        }
    }

    private void PlaceIconAt(Point mapPos)
    {
        var img = new Image
        {
            Source = new BitmapImage(new Uri(_currentIconPath, UriKind.RelativeOrAbsolute)),
            Width = 64,
            Height = 64,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Tag = new OverlayTag { OwnerSteamId = _mySteamId, IsUserEditable = true }
        };

        Canvas.SetLeft(img, mapPos.X - 16);
        Canvas.SetTop(img, mapPos.Y - 16);

        Overlay.Children.Add(img);
        RegisterElementForOwner(_mySteamId, img);

        SaveOwnOverlayToJson();
    }

    private void PlaceTextAt(Point mapPos)
    {
        var input = PromptText("Enter description:", "Add Text", "");
        if (string.IsNullOrWhiteSpace(input))
            return;

        var tb = new TextBlock
        {
            Text = input,
            Foreground = new SolidColorBrush(_textColor),
            FontSize = _textSize,
            FontWeight = FontWeights.Bold,
            Tag = new OverlayTag { OwnerSteamId = _mySteamId, IsUserEditable = true }
        };

        Canvas.SetLeft(tb, mapPos.X);
        Canvas.SetTop(tb, mapPos.Y);

        Overlay.Children.Add(tb);
        RegisterElementForOwner(_mySteamId, tb);

        SaveOwnOverlayToJson();
    }

    private void TryBeginDragExistingElement(Point mapPos)
    {
        for (int i = Overlay.Children.Count - 1; i >= 0; i--)
        {
            if (Overlay.Children[i] is FrameworkElement fe)
            {
                // nur meine editierbaren Elemente dürfen gezogen werden
                if (fe.Tag is not OverlayTag meta) continue;
                if (meta.OwnerSteamId != _mySteamId) continue;
                if (!meta.IsUserEditable) continue;

                double x = Canvas.GetLeft(fe);
                double y = Canvas.GetTop(fe);

                double w = fe is Image img ? img.Width :
                           (fe.ActualWidth > 0 ? fe.ActualWidth : 32);
                double h = fe is Image img2 ? img2.Height :
                           (fe.ActualHeight > 0 ? fe.ActualHeight : 16);

                if (mapPos.X >= x && mapPos.X <= x + w &&
                    mapPos.Y >= y && mapPos.Y <= y + h)
                {
                    _draggingElement = fe;
                    _dragOffset = new Point(mapPos.X - x, mapPos.Y - y);
                    break;
                }
            }
        }
    }

    private void TryDeleteElementAt(Point mapPos)
    {
        // Lösche Icon/Text bei Rechtsklick, aber nur mein eigenes Zeug
        for (int i = Overlay.Children.Count - 1; i >= 0; i--)
        {
            if (Overlay.Children[i] is FrameworkElement fe)
            {
                // Lines (Polyline) ignorieren wir hier weiter, die macht Eraser.
                if (fe is Polyline) continue;

                // Besitz prüfen
                if (fe.Tag is not OverlayTag meta) continue;
                if (meta.OwnerSteamId != _mySteamId) continue;
                if (!meta.IsUserEditable) continue;

                double x = Canvas.GetLeft(fe);
                double y = Canvas.GetTop(fe);

                // Wenn WPF noch kein ActualWidth/Height gemessen hat, fallback:
                double w = fe is Image img ? img.Width : (fe.ActualWidth > 0 ? fe.ActualWidth : 32);
                double h = fe is Image img2 ? img2.Height : (fe.ActualHeight > 0 ? fe.ActualHeight : 16);

                if (mapPos.X >= x && mapPos.X <= x + w &&
                    mapPos.Y >= y && mapPos.Y <= y + h)
                {
                    Overlay.Children.RemoveAt(i);

                    // auch aus _playerOverlayElements[_mySteamId] rauswerfen
                    if (_playerOverlayElements.TryGetValue(_mySteamId, out var mine))
                    {
                        mine.Remove(fe);
                    }

                    SaveOwnOverlayToJson();
                    break;
                }
            }
        }
    }

    private void RegisterElementForOwner(ulong owner, FrameworkElement fe)
    {
        if (!_playerOverlayElements.TryGetValue(owner, out var list))
        {
            list = new List<FrameworkElement>();
            _playerOverlayElements[owner] = list;
        }
        list.Add(fe);
    }

    private void Icon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            _draggingElement = fe;
            var mousePos = HostToScenePreTransform(e.GetPosition(WebViewHost));
            _dragOffset = new Point(mousePos.X - Canvas.GetLeft(fe), mousePos.Y - Canvas.GetTop(fe));
            e.Handled = true;
        }
    }

    private void Icon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggingElement = null;
        e.Handled = true;
    }

    // Doppelklick -> rename
    private void Icon_MouseDoubleClickCheck(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement fe)
        {
            // Popup InputBox -> setze fe.Tag = newName etc.
            e.Handled = true;
        }
    }

    // Rechtsklick -> löschen
    private void Icon_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            Overlay.Children.Remove(fe);
            e.Handled = true;
        }
    }

    private void BtnToggleOverlayTools_Click(object sender, RoutedEventArgs e)
    {
        _overlayToolsVisible = !_overlayToolsVisible;

        OverlayToolbox.Visibility = _overlayToolsVisible ? Visibility.Visible : Visibility.Collapsed;
        OverlayTeamBar.Visibility = _overlayToolsVisible ? Visibility.Visible : Visibility.Collapsed;

        if (!_overlayToolsVisible)
        {
            _currentTool = OverlayToolMode.None;
            _draggingElement = null;
            UpdateToolButtonHighlights();
        }
        else
        {
            RebuildOverlayTeamBar();
            UpdateToolButtonHighlights();
        }
    }

    private void ToolDrawButton_Click(object sender, RoutedEventArgs e)
    {
        SetCurrentTool(OverlayToolMode.Draw);
    }

    private void ToolDrawButton_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowDrawSettingsContextMenu(sender as FrameworkElement);
    }

    private void ToolTextButton_Click(object sender, RoutedEventArgs e)
    {
        SetCurrentTool(OverlayToolMode.Text);
    }

    private void ToolTextButton_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowTextSettingsContextMenu(sender as FrameworkElement);
    }

    private void ToolIconButton_Click(object sender, RoutedEventArgs e)
    {
        SetCurrentTool(OverlayToolMode.Icon);
    }

    private void ToolIconButton_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowIconSelectContextMenu(sender as FrameworkElement);
    }

    private void ToolEraseButton_Click(object sender, RoutedEventArgs e)
    {
        SetCurrentTool(OverlayToolMode.Erase);
    }

    private void ToolEraseButton_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowEraserSettingsContextMenu(sender as FrameworkElement);
    }

    private void ToolTrashButton_Click(object sender, RoutedEventArgs e)
    {
        // 1. alle meine Elemente raus aus Overlay
        if (_playerOverlayElements.TryGetValue(_mySteamId, out var mine))
        {
            foreach (var el in mine)
                Overlay.Children.Remove(el);

            mine.Clear();
        }

        // 2. auch sicherheitshalber wirklich physisch alle übriggebliebenen Ownerelemente killen:
        var cleanup = new List<UIElement>();
        foreach (var child in Overlay.Children)
        {
            if (child is FrameworkElement fe && fe.Tag is OverlayTag meta && meta.OwnerSteamId == _mySteamId)
                cleanup.Add(fe);
        }
        foreach (var dead in cleanup)
            Overlay.Children.Remove(dead);

        // 3. Leeres JSON speichern
        var empty = new OverlaySaveData();
        var json = System.Text.Json.JsonSerializer.Serialize(
            empty,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        var path = GetOverlayJsonPathForPlayerServer(_mySteamId);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);

        // 4. und falls ich gerade sichtbar war -> sichtbar bleiben (ist dann eh leer)
        SaveOwnOverlayToJson(); // optional redundant
        UploadOwnOverlayToTeam();
    }

    private void ToolUploadButton_Click(object sender, RoutedEventArgs e)
    {
        UploadOwnOverlayToTeam();
    }

    // private void SaveOwnOverlayToPng()
    // {
    // 1. Zielgröße bestimmen
    //     int pixelW = (int)ImgMap.Source.Width;
    //int pixelH = (int)ImgMap.Source.Height;

    //var exportCanvas = new Canvas
    //    {
    //Width = pixelW,
    //Height = pixelH,
    //Background = Brushes.Transparent
    //};

    // 2. Kinder kopieren
    //      foreach (var child in Overlay.Children.OfType<UIElement>())
    //    {
    // Fremde Overlays (Team-Layer) sind Image mit RenderTransform = MapTransform etc.
    // Erkennen wir grob so:
    //       if (child is Image img && _playerOverlayImages.Values.Contains(img))
    //      {
    // Das ist ein fremdes Team-Overlay, nicht unser eigenes -> skip
    //          continue;
    //     }

    // ansonsten klonen wir "oberflächlich":
    //    UIElement clone = CloneOverlayElementForExport(child);
    //          if (clone != null)
    //exportCanvas.Children.Add(clone);
    //}

    // 3. Rendern
    // exportCanvas.Measure(new Size(pixelW, pixelH));
    //exportCanvas.Arrange(new Rect(0, 0, pixelW, pixelH));

    //    var rtb = new RenderTargetBitmap(pixelW, pixelH, 96, 96, PixelFormats.Pbgra32);
    //rtb.Render(exportCanvas);

    // 4. PNG speichern
    //   var encoder = new PngBitmapEncoder();
    //encoder.Frames.Add(BitmapFrame.Create(rtb));

    //var mySteamId = _mySteamId; // musst du definieren
    //var path = GetOverlayPngPathForPlayer(mySteamId);
    //    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

    //      using (var fs = File.Create(path))
    //    {
    //encoder.Save(fs);
    //}
    //  }

    // sehr einfache, flache Kopie:
    //private UIElement CloneOverlayElementForExport(UIElement element)
    // {
    //   if (element is Polyline pl)
    //  {
    //var clone = new Polyline
    //        {
    //Stroke = pl.Stroke,
    //StrokeThickness = pl.StrokeThickness,
    //StrokeLineJoin = pl.StrokeLineJoin,
    //StrokeEndLineCap = pl.StrokeEndLineCap,
    //StrokeStartLineCap = pl.StrokeStartLineCap
    //};
    //          foreach (var p in pl.Points)
    //clone.Points.Add(p);

    //    Canvas.SetLeft(clone, Canvas.GetLeft(pl));
    //Canvas.SetTop(clone, Canvas.GetTop(pl));
    //       return clone;
    //}
    //      else if (element is Image img && img.Source != null)
    //     {
    //var clone = new Image
    //        {
    //Source = img.Source,
    //Width = img.Width,
    //Height = img.Height,
    //Stretch = img.Stretch
    //};
    //Canvas.SetLeft(clone, Canvas.GetLeft(img));
    //Canvas.SetTop(clone, Canvas.GetTop(img));
    //        return clone;
    //}
    //      else if (element is TextBlock tb)
    //     {
    //var clone = new TextBlock
    //       {
    //Text = tb.Text,
    //Foreground = tb.Foreground,
    //FontSize = tb.FontSize,
    //FontWeight = tb.FontWeight
    //};
    //Canvas.SetLeft(clone, Canvas.GetLeft(tb));
    //Canvas.SetTop(clone, Canvas.GetTop(tb));
    //        return clone;
    //}

    //      return null;
    // }

    private void ShowDrawSettingsContextMenu(FrameworkElement? fe)
    {
        var m = new ContextMenu();

        // Farbe ändern (nur ein Beispiel)
        var miRed = new MenuItem { Header = "Red" };
        miRed.Click += (_, __) => { _drawColor = Colors.Red; };
        var miGreen = new MenuItem { Header = "Green" };
        miGreen.Click += (_, __) => { _drawColor = Colors.Lime; };
        var miBlue = new MenuItem { Header = "Blue" };
        miBlue.Click += (_, __) => { _drawColor = Colors.DeepSkyBlue; };

        // Stiftdicke
        var miThin = new MenuItem { Header = "Thickness: 3px" };
        miThin.Click += (_, __) => { _drawThickness = 3.0; };
        var miThick = new MenuItem { Header = "Thickness: 10px" };
        miThick.Click += (_, __) => { _drawThickness = 10.0; };

        m.Items.Add(miRed);
        m.Items.Add(miGreen);
        m.Items.Add(miBlue);
        m.Items.Add(new Separator());
        m.Items.Add(miThin);
        m.Items.Add(miThick);

        fe!.ContextMenu = m;
        m.IsOpen = true;
    }

    private void ShowTextSettingsContextMenu(FrameworkElement? fe)
    {
        var m = new ContextMenu();

        var miWhite = new MenuItem { Header = "White" };
        miWhite.Click += (_, __) => { _textColor = Colors.White; };
        var miYellow = new MenuItem { Header = "Red" };
        miYellow.Click += (_, __) => { _textColor = Colors.Red; };

        var miSmall = new MenuItem { Header = "Size: 14" };
        miSmall.Click += (_, __) => { _textSize = 14.0; };
        var miBig = new MenuItem { Header = "Size: 40" };
        miBig.Click += (_, __) => { _textSize = 40.0; };

        m.Items.Add(miWhite);
        m.Items.Add(miYellow);
        m.Items.Add(new Separator());
        m.Items.Add(miSmall);
        m.Items.Add(miBig);

        fe!.ContextMenu = m;
        m.IsOpen = true;
    }

    private void ShowIconSelectContextMenu(FrameworkElement? fe)
    {
        var m = new ContextMenu();

        // map-icons aus deinem Projekt
        m.Items.Add(BuildIconMenuItem("Base #1", "pack://application:,,,/icons/map-icons/base1.png"));
        m.Items.Add(BuildIconMenuItem("Base #2", "pack://application:,,,/icons/map-icons/base2.png"));
        m.Items.Add(BuildIconMenuItem("SAM Site", "pack://application:,,,/icons/map-icons/sam-site.png"));
        m.Items.Add(BuildIconMenuItem("Turret", "pack://application:,,,/icons/map-icons/turret.png"));

        fe!.ContextMenu = m;
        m.IsOpen = true;
    }

    private MenuItem BuildIconMenuItem(string label, string path)
    {
        var mi = new MenuItem { Header = label };
        mi.Click += (_, __) => { _currentIconPath = path; };
        return mi;
    }

    private void ShowEraserSettingsContextMenu(FrameworkElement? fe)
    {
        var m = new ContextMenu();

        var miSmall = new MenuItem { Header = "Eraser small (5px)" };
        miSmall.Click += (_, __) => { _eraserSize = 5.0; };
        var miBig = new MenuItem { Header = "Eraser big (20px)" };
        miBig.Click += (_, __) => { _eraserSize = 20.0; };

        m.Items.Add(miSmall);
        m.Items.Add(miBig);

        fe!.ContextMenu = m;
        m.IsOpen = true;
    }

    private async void UploadOwnOverlayToTeam()
    {
        try
        {
            // 1. mein aktuelles Overlay einsammeln
            var data = BuildCurrentOverlaySaveDataForMe(); // <- gleich checken wir noch

            // JSON bauen
            var json = System.Text.Json.JsonSerializer.Serialize(
                data,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = false });

            // Größenlimit checken
            var rawBytes = Encoding.UTF8.GetBytes(json);
            if (rawBytes.Length > OVERLAY_MAX_BYTES)
            {
                AppendLog("[overlay] Upload too big (>350KB).");
                return;
            }

            // Base64
            var overlayB64 = Convert.ToBase64String(rawBytes);

            // Signaturfelder
            var serverKey = GetServerKey();           // muss exakt derselbe string wie beim Speichern sein
            var ts = UnixNow().ToString();     // Sekunden
            var sigInput = _mySteamId.ToString() + "|" + serverKey + "|" + ts + "|" + overlayB64;
            var sig = HmacSha256Hex(OVERLAY_SYNC_SECRET_HEX, sigInput);

            // Request body
            var payloadObj = new
            {
                steamId = _mySteamId.ToString(),
                serverKey = serverKey,
                ts = ts,
                overlayJsonB64 = overlayB64,
                sig = sig
            };

            var payloadJson = System.Text.Json.JsonSerializer.Serialize(payloadObj);
            var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

            using (var http = new HttpClient())
            {
                var url = OVERLAY_SYNC_BASEURL + "/upload";
                var resp = await http.PostAsync(url, content);
                if (!resp.IsSuccessStatusCode)
                {
                    AppendLog("[overlay] Upload failed: HTTP " + (int)resp.StatusCode);
                    return;
                }
            }

            AppendLog("[overlay] Overlay uploaded.");
        }
        catch (Exception ex)
        {
            AppendLog("[overlay] Upload Error: " + ex.Message);
        }
    }

    private async Task<bool> TryFetchOverlayFromServerAsync(ulong steamId)
    {
        try
        {
            var serverKey = GetServerKey();
            var ts = UnixNow().ToString();

            // Wir signieren dieselbe Formel wie der Server in /fetch prüft:
            // msg = "<steamId>|<serverKey>|<ts>|<overlayB64>"
            // ABER: Wir kennen overlayB64 ja noch nicht vorm Request 🤔
            //
            // Lösung: Wir machen es wie folgt:
            // - Server-Code oben hat overlay_b64 mit in die Signatur genommen.
            //   Das bedeutet: Client muss erst overlay_b64 kennen. Das geht so natürlich nicht.
            //
            // Also müssen wir eine kleine Änderung machen:
            // Variante A (einfach): wir ändern /fetch auf dem Server so, dass er
            // NUR steamId|serverKey|ts signed, OHNE overlayB64.
            //
            // Mach das bitte gleich am Server (fetch-Teil ersetzen):

            // (Wir nehmen jetzt an, du hast den Server so geändert wie unten beschrieben.)
            // Dann bauen wir hier:
            var msg = $"{steamId}|{serverKey}|{ts}";
            var sig = HmacSha256Hex(OVERLAY_SYNC_SECRET_HEX, msg);

            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(5);

                var url = OVERLAY_SYNC_BASEURL
                    + "/fetch"
                    + "?steamId=" + WebUtility.UrlEncode(steamId.ToString())
                    + "&serverKey=" + WebUtility.UrlEncode(serverKey)
                    + "&ts=" + WebUtility.UrlEncode(ts)
                    + "&sig=" + WebUtility.UrlEncode(sig);

                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    AppendLog("[overlay] fetch failed HTTP " + (int)resp.StatusCode);
                    return false;
                }

                var bodyJson = await resp.Content.ReadAsStringAsync();
                // Erwartet: { "overlayJsonB64": "...." }
                var doc = System.Text.Json.JsonDocument.Parse(bodyJson);
                if (!doc.RootElement.TryGetProperty("overlayJsonB64", out var b64El))
                    return false;

                var b64 = b64El.GetString();
                if (string.IsNullOrEmpty(b64))
                    return false;

                var raw = Convert.FromBase64String(b64);
                var jsonOverlay = Encoding.UTF8.GetString(raw);

                // Schreib's lokal so wie SaveOwnOverlayToJson das erwartet:
                var path = GetOverlayJsonPathForPlayerServer(steamId);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                File.WriteAllText(path, jsonOverlay);

                return true;
            }
        }
        catch (Exception ex)
        {
            AppendLog("[overlay] fetch error: " + ex.Message);
            return false;
        }
    }

    private string? PromptText(string message, string title = "Enter Text", string defaultValue = "")
    {
        // kleines Dialogfenster on-the-fly bauen
        var win = new Window
        {
            Title = title,
            Width = 320,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(24, 26, 30)),
            Foreground = Brushes.White,
            ShowInTaskbar = false
        };

        var root = new Grid
        {
            Margin = new Thickness(12)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // label
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // textbox
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // buttons

        var lbl = new TextBlock
        {
            Text = message,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = Brushes.White
        };
        Grid.SetRow(lbl, 0);
        root.Children.Add(lbl);

        var tb = new TextBox
        {
            Text = defaultValue,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(tb, 1);
        root.Children.Add(tb);

        var panelButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var btnOk = new Button
        {
            Content = "OK",
            Width = 60,
            Margin = new Thickness(0, 0, 6, 0),
            IsDefault = true
        };
        var btnCancel = new Button
        {
            Content = "Cancel",
            Width = 80,
            IsCancel = true
        };

        panelButtons.Children.Add(btnOk);
        panelButtons.Children.Add(btnCancel);
        Grid.SetRow(panelButtons, 2);
        root.Children.Add(panelButtons);

        string? result = null;

        btnOk.Click += (_, __) =>
        {
            result = tb.Text;
            win.DialogResult = true;
            win.Close();
        };
        btnCancel.Click += (_, __) =>
        {
            result = null;
            win.DialogResult = false;
            win.Close();
        };

        win.Content = root;

        // modal anzeigen
        win.ShowDialog();

        return result;
    }

    // euklidische Distanz Punkt -> Streckenabschnitt (A,B)
    private static double DistancePointToSegmentSquared(Point p, Point a, Point b)
    {
        // Vektor AB
        double vx = b.X - a.X;
        double vy = b.Y - a.Y;

        // Vektor AP
        double wx = p.X - a.X;
        double wy = p.Y - a.Y;

        // Projektion t = (AP·AB)/|AB|² clamped [0..1]
        double denom = (vx * vx + vy * vy);
        double t = denom <= 0.000001 ? 0.0 : ((wx * vx + wy * vy) / denom);
        if (t < 0.0) t = 0.0;
        else if (t > 1.0) t = 1.0;

        // Nächster Punkt auf AB
        double cx = a.X + t * vx;
        double cy = a.Y + t * vy;

        // Distanz^2 zwischen P und C
        double dx = p.X - cx;
        double dy = p.Y - cy;
        return dx * dx + dy * dy;
    }

    private static double DistancePointToPolyline(Point p, Polyline line)
    {
        var pts = line.Points;
        if (pts.Count == 1)
        {
            // einzelne Punkt-Linie -> Distanz zu diesem Punkt
            double dx = p.X - pts[0].X;
            double dy = p.Y - pts[0].Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        double bestSq = double.MaxValue;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            double d2 = DistancePointToSegmentSquared(p, pts[i], pts[i + 1]);
            if (d2 < bestSq) bestSq = d2;
        }
        return Math.Sqrt(bestSq);
    }

    private void SetCurrentTool(OverlayToolMode modeFromButton)
    {
        if (_currentTool == modeFromButton)
        {
            // toggle off -> zurück in Pan/Zoom Modus
            _currentTool = OverlayToolMode.None;
        }
        else
        {
            _currentTool = modeFromButton;
        }

        // Wenn wir ein Tool aktivieren, abbrechen von evtl. Drag-State
        if (_currentTool != OverlayToolMode.None)
        {
            _draggingElement = null;
        }

        UpdateToolButtonHighlights();
    }
    private void UpdateToolButtonHighlights()
    {
        // Erstmal alle zurücksetzen
        foreach (var kv in _toolButtons)
        {
            var btn = kv.Value;
            btn.Background = Brushes.Transparent;
            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
            btn.BorderThickness = new Thickness(1);
        }

        // Falls gar kein Tool aktiv ist (None) -> fertig
        if (_currentTool == OverlayToolMode.None)
            return;

        // Aktiven Button highlighten
        if (_toolButtons.TryGetValue(_currentTool, out var activeBtn))
        {
            activeBtn.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x4C, 0x8D, 0xFF)); // halbtransparentes Blau
            activeBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF));
            activeBtn.BorderThickness = new Thickness(2);
        }
    }

    public class OverlaySaveData
    {
        public long LastUpdatedUnix { get; set; } = 0; // Unix seconds
        public List<SavedStroke> Strokes { get; set; } = new();
        public List<SavedIcon> Icons { get; set; } = new();
        public List<SavedText> Texts { get; set; } = new();
    }

    public class SavedStroke
    {
        public List<Point> Points { get; set; } = new();
        public string Color { get; set; } = "#FF0000"; // ARGB oder RGB als Hex
        public double Thickness { get; set; } = 2.0;
    }

    public class SavedIcon
    {
        public string IconPath { get; set; } = ""; // z.B. "pack://application:,,,/Icons/map-icons/sam-site.png"
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; } = 32;
        public double Height { get; set; } = 32;

        public string? Label { get; set; } // fürs spätere Umbenennen
    }

    public class SavedText
    {
        public string Content { get; set; } = "";
        public string Color { get; set; } = "#FFFFFFFF";
        public double FontSize { get; set; } = 16.0;
        public double X { get; set; }
        public double Y { get; set; }
        public bool Bold { get; set; } = true;
    }

    private void SaveOwnOverlayToJson()
    {
        var data = new OverlaySaveData();
        data.LastUpdatedUnix = UnixNow(); // <--- NEU

        foreach (var child in Overlay.Children)
        {
            if (child is not FrameworkElement fe) continue;
            if (fe.Tag is not OverlayTag meta) continue;
            if (meta.OwnerSteamId != _mySteamId) continue;

            switch (child)
            {
                case Polyline pl:
                    {
                        var stroke = new SavedStroke
                        {
                            Thickness = pl.StrokeThickness,
                            Color = (pl.Stroke as SolidColorBrush)?.Color.ToString() ?? "#FFFFFFFF"
                        };
                        foreach (var p in pl.Points)
                            stroke.Points.Add(p);
                        data.Strokes.Add(stroke);
                    }
                    break;

                case Image img:
                    {
                        var x = Canvas.GetLeft(img);
                        var y = Canvas.GetTop(img);

                        var bi = img.Source as BitmapImage;

                        var si = new SavedIcon
                        {
                            IconPath = bi?.UriSource?.ToString() ?? _currentIconPath,
                            X = x,
                            Y = y,
                            Width = img.Width,
                            Height = img.Height
                        };
                        data.Icons.Add(si);
                    }
                    break;

                case TextBlock tb:
                    {
                        var x = Canvas.GetLeft(tb);
                        var y = Canvas.GetTop(tb);

                        var st = new SavedText
                        {
                            Content = tb.Text,
                            X = x,
                            Y = y,
                            FontSize = tb.FontSize,
                            Bold = (tb.FontWeight == FontWeights.Bold),
                            Color = (tb.Foreground as SolidColorBrush)?.Color.ToString() ?? "#FFFFFFFF"
                        };
                        data.Texts.Add(st);
                    }
                    break;
            }
        }

        var json = System.Text.Json.JsonSerializer.Serialize(
            data,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        var path = GetOverlayJsonPathForPlayerServer(_mySteamId);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private string GetServerKey()
    {
        // simplest first pass: nimm Host-Port vom aktuell ausgewählten Server
        var prof = _vm?.Selected;
        if (prof == null) return "unknown-server";

        // du hast im Connect-Code `_vm.Selected.Host` und `_vm.Selected.Port`
        return $"{prof.Host}-{prof.Port}";
    }

    private string GetOverlayJsonPathForPlayerServer(ulong steamId)
    {
        var baseDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RustPlusDesk",
            "Overlays",
            GetServerKey()
        );
        Directory.CreateDirectory(baseDir);
        return System.IO.Path.Combine(baseDir, $"{steamId}.json");
    }


    private void ClearUserOverlayElements()
    {
        // 1) Sammeln, nicht während foreach löschen
        var toRemove = new List<UIElement>();

        foreach (var child in Overlay.Children)
        {
            if (child is FrameworkElement fe && fe.Tag is OverlayTag)
            {
                toRemove.Add(fe);
            }
        }

        // 2) Rauswerfen
        foreach (var el in toRemove)
            Overlay.Children.Remove(el);

        // 3) Auch unsere Index-Maps resetten
        _playerOverlayElements.Clear();
        _visibleOverlayOwners.Clear();
    }

    private class OverlayTag
    {
        public ulong OwnerSteamId;
        public bool IsUserEditable; // true = mein eigener Layer
    }

    // --- Overlay Sync Config ---
    private const string OVERLAY_SYNC_SECRET_HEX =
    "23c5a7dbf02b63543da043ca7d6de1fbf706a080c899e334a8cd599206e13fde";

    // dein Server (IP oder DNS + Port). Kein "/" am Ende.
    private const string OVERLAY_SYNC_BASEURL = "http://85.214.193.250:5000";

    // Hard-Limits müssen mit dem Python-Server matchen
    private const int OVERLAY_MAX_BYTES = 350_000; // ~350 KB Limit roh

    // ---- HMAC / Network Helpers ------------------------------------------



    private static byte[] HexToBytes(string hex)
    {
        if (hex.Length % 2 != 0)
            throw new ArgumentException("invalid hex length");

        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < hex.Length; i += 2)
        {
            bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
        }
        return bytes;
    }

    private static string HmacSha256Hex(string hexKey, string dataUtf8)
    {
        // 1) Secret aus Hex in echte Bytes
        var keyBytes = HexToBytes(hexKey);

        // 2) Daten als UTF8-Bytes
        var payloadBytes = Encoding.UTF8.GetBytes(dataUtf8);

        // 3) HMAC-SHA256
        using (var hmac = new System.Security.Cryptography.HMACSHA256(keyBytes))
        {
            var hash = hmac.ComputeHash(payloadBytes);

            // 4) hex-lowercase string bauen wie Python .hexdigest()
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    private static long UnixNow()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    // Liest lokales Overlay (mich) als OverlaySaveData
    private OverlaySaveData BuildCurrentOverlaySaveDataForMe()
    {
        var data = new OverlaySaveData();
        data.LastUpdatedUnix = UnixNow(); // NEU: stamp jetzt

        foreach (var child in Overlay.Children)
        {
            if (child is not FrameworkElement fe) continue;
            if (fe.Tag is not OverlayTag meta) continue;
            if (meta.OwnerSteamId != _mySteamId) continue;

            switch (child)
            {
                case Polyline pl:
                    {
                        var stroke = new SavedStroke
                        {
                            Thickness = pl.StrokeThickness,
                            Color = (pl.Stroke as SolidColorBrush)?.Color.ToString() ?? "#FFFFFFFF"
                        };

                        foreach (var p in pl.Points)
                            stroke.Points.Add(p);

                        data.Strokes.Add(stroke);
                        break;
                    }

                case Image img:
                    {
                        var x = Canvas.GetLeft(img);
                        var y = Canvas.GetTop(img);

                        var bi = img.Source as BitmapImage;

                        var si = new SavedIcon
                        {
                            IconPath = bi?.UriSource?.ToString() ?? _currentIconPath,
                            X = x,
                            Y = y,
                            Width = img.Width,
                            Height = img.Height
                        };
                        data.Icons.Add(si);
                        break;
                    }

                case TextBlock tb:
                    {
                        var x = Canvas.GetLeft(tb);
                        var y = Canvas.GetTop(tb);

                        var st = new SavedText
                        {
                            Content = tb.Text,
                            X = x,
                            Y = y,
                            FontSize = tb.FontSize,
                            Bold = (tb.FontWeight == FontWeights.Bold),
                            Color = (tb.Foreground as SolidColorBrush)?.Color.ToString() ?? "#FFFFFFFF"
                        };
                        data.Texts.Add(st);
                        break;
                    }
            }
        }

        return data;
    }

    // speichert OverlaySaveData von steamId (z.B. teammate) lokal in %APPDATA%\RustPlusDesk\Overlays\<serverKey>\<steamId>.json
    private void WriteOverlaySaveDataLocal(ulong steamId, OverlaySaveData data)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            data,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        var path = GetOverlayJsonPathForPlayerServer(steamId);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    // baut aus einem OverlaySaveData echte UI-Elemente auf der Canvas für einen Spieler
    // und cached sie in _playerOverlayElements[steamId]
    private void MaterializeOverlayForPlayer(ulong steamId, OverlaySaveData data, bool editableIfMine)
    {
        // falls schon Elemente für den Spieler existieren -> erstmal killen
        if (_playerOverlayElements.TryGetValue(steamId, out var existing))
        {
            foreach (var el in existing)
                Overlay.Children.Remove(el);
        }

        var list = new List<FrameworkElement>();
        _playerOverlayElements[steamId] = list;

        foreach (var stroke in data.Strokes)
        {
            var pl = new Polyline
            {
                StrokeThickness = stroke.Thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeStartLineCap = PenLineCap.Round,
                IsHitTestVisible = editableIfMine ? false : false, // Linien eh nicht direkt draggen
                Opacity = editableIfMine ? 1.0 : 0.8,
                Tag = new OverlayTag
                {
                    OwnerSteamId = steamId,
                    IsUserEditable = editableIfMine
                },
                Visibility = _visibleOverlayOwners.Contains(steamId)
                             ? Visibility.Visible
                             : Visibility.Collapsed
            };

            if (ColorConverter.ConvertFromString(stroke.Color) is Color c)
                pl.Stroke = new SolidColorBrush(c);

            foreach (var p in stroke.Points)
                pl.Points.Add(p);

            Overlay.Children.Add(pl);
            list.Add(pl);
        }

        foreach (var icon in data.Icons)
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(icon.IconPath, UriKind.RelativeOrAbsolute);
            bi.EndInit();
            bi.Freeze();

            var img = new Image
            {
                Source = bi,
                Width = icon.Width,
                Height = icon.Height,
                RenderTransformOrigin = new Point(0.5, 0.5),
                IsHitTestVisible = editableIfMine, // meine Icons kann ich anfassen
                Opacity = editableIfMine ? 1.0 : 0.8,
                Tag = new OverlayTag
                {
                    OwnerSteamId = steamId,
                    IsUserEditable = editableIfMine
                },
                Visibility = _visibleOverlayOwners.Contains(steamId)
                             ? Visibility.Visible
                             : Visibility.Collapsed
            };

            Canvas.SetLeft(img, icon.X);
            Canvas.SetTop(img, icon.Y);

            Overlay.Children.Add(img);
            list.Add(img);
        }

        foreach (var txt in data.Texts)
        {
            var tb = new TextBlock
            {
                Text = txt.Content,
                FontSize = txt.FontSize,
                FontWeight = txt.Bold ? FontWeights.Bold : FontWeights.Normal,
                IsHitTestVisible = editableIfMine,
                Opacity = editableIfMine ? 1.0 : 0.8,
                Tag = new OverlayTag
                {
                    OwnerSteamId = steamId,
                    IsUserEditable = editableIfMine
                },
                Visibility = _visibleOverlayOwners.Contains(steamId)
                             ? Visibility.Visible
                             : Visibility.Collapsed
            };

            if (ColorConverter.ConvertFromString(txt.Color) is Color tc)
                tb.Foreground = new SolidColorBrush(tc);

            Canvas.SetLeft(tb, txt.X);
            Canvas.SetTop(tb, txt.Y);

            Overlay.Children.Add(tb);
            list.Add(tb);
        }
    }

    // holt OverlaySaveData aus JSON-Text
    private static OverlaySaveData? ParseOverlayJson(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<OverlaySaveData>(json);
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> TryFetchOverlayForPlayerFromServerAsync(ulong steamId)
    {
        try
        {
            var serverKey = GetServerKey();
            var ts = UnixNow().ToString();
            // Signatur beim GET: steamId|serverKey|ts
            var sigInput = steamId.ToString() + "|" + serverKey + "|" + ts;
            var sig = HmacSha256Hex(OVERLAY_SYNC_SECRET_HEX, sigInput);

            var url = $"{OVERLAY_SYNC_BASEURL}/fetch" +
                      $"?steamId={Uri.EscapeDataString(steamId.ToString())}" +
                      $"&serverKey={Uri.EscapeDataString(serverKey)}" +
                      $"&ts={Uri.EscapeDataString(ts)}" +
                      $"&sig={Uri.EscapeDataString(sig)}";

            using (var http = new HttpClient())
            {
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    // 404 ist einfach "hat nix hochgeladen" -> kein Fehler ins Log spammen
                    if ((int)resp.StatusCode != 404)
                        AppendLog("[overlay] Fetch HTTP " + (int)resp.StatusCode);
                    return false;
                }

                var body = await resp.Content.ReadAsStringAsync();
                // body hat {"overlayJsonB64": "..."}
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("overlayJsonB64", out var b64El))
                    return false;

                var b64 = b64El.GetString();
                if (string.IsNullOrEmpty(b64)) return false;

                byte[] decoded = Convert.FromBase64String(b64);
                if (decoded.Length > OVERLAY_MAX_BYTES)
                {
                    AppendLog("[overlay] Remote Overlay too big.");
                    return false;
                }

                var jsonOverlay = Encoding.UTF8.GetString(decoded);
                var data = ParseOverlayJson(jsonOverlay);
                if (data == null)
                {
                    AppendLog("[overlay] Remote Overlay broken.");
                    return false;
                }

                // lokal speichern für diesen Spieler
                WriteOverlaySaveDataLocal(steamId, data);

                // Canvas-Objekte bauen / ersetzen
                bool editable = (steamId == _mySteamId);
                MaterializeOverlayForPlayer(steamId, data, editable);

                AppendLog("[overlay] Overlay loaded from " + steamId + ".");
                return true;
            }
        }
        catch (Exception ex)
        {
            AppendLog("[overlay] Fetch Error: " + ex.Message);
            return false;
        }
    }

    private void LoadOverlayFromDiskForPlayer(ulong steamId)
    {
        var path = GetOverlayJsonPathForPlayerServer(steamId);
        if (!File.Exists(path))
        {
            // registriere leere Liste (damit wir nicht endlos neu versuchen)
            if (!_playerOverlayElements.ContainsKey(steamId))
                _playerOverlayElements[steamId] = new List<FrameworkElement>();
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var data = ParseOverlayJson(json);
            if (data == null)
            {
                if (!_playerOverlayElements.ContainsKey(steamId))
                    _playerOverlayElements[steamId] = new List<FrameworkElement>();
                return;
            }

            bool editable = (steamId == _mySteamId);
            MaterializeOverlayForPlayer(steamId, data, editable);
        }
        catch
        {
            if (!_playerOverlayElements.ContainsKey(steamId))
                _playerOverlayElements[steamId] = new List<FrameworkElement>();
        }


    }



}

