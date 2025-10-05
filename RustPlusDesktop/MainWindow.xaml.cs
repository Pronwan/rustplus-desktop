using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using RustPlusDesk.ViewModels;
using RustPlusDesk.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Text.RegularExpressions;

using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Resources; // für Application.GetResourceStream
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Linq;
using static RustPlusDesk.Services.RustPlusClientReal;
using IOPath = System.IO.Path;
using System.Diagnostics.Eventing.Reader;


namespace RustPlusDesk.Views;



public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly SteamLoginService _steam = new();
    private readonly IRustPlusClient _rust;  // Interface statt fester Klasse
    private WebView2? _webView;
    private IPairingListener _pairing;
    private bool _pairingStarting;
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
   
    private bool _visible;
    // CAMERA TAB

    // Camera thumbs: Throttling & "in-flight"-Wächter
    internal readonly HashSet<string> _camBusy = new(StringComparer.OrdinalIgnoreCase);
    
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
    private const double MINI_VIEW_FRACTION = 0.30; // 40% des sichtbaren Bereichs

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
    }

    private async Task AnnouncePresenceChangeAsync(TeamMemberVM vm, (bool online, bool dead) prev, (bool online, bool dead) now)
    {
        try
        {
            // Online/Offline (wenn dieser Part auch vom Button abhängen soll, ebenfalls mit _announceSpawns verknüpfen)
            if (prev.online != now.online /* && _announceSpawns */)
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
        var mFull   = Regex.Match(xml, @"<avatarFull><!\[CDATA\[(.*?)\]\]></avatarFull>", RegexOptions.IgnoreCase);
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
                    dev.IsOn = isOn;
                });
            };
        }
       // AppendLog($"DEBUG: Selected={_vm.Selected?.Name ?? "(null)"}  Devices={_vm.Selected?.Devices?.Count.ToString() ?? "(null)"}");

        
        TxtSteamId.Text = string.IsNullOrEmpty(_vm.SteamId64) ? "(nicht angemeldet)" : _vm.SteamId64;

        this.Closing += MainWindow_Closing;
        _ = EnsureWebView2Async();
        this.Closed += MainWindow_Closed;

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
             .Replace("oil rig small", "small oil rig");


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
            dHost.Y / s );
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
                    Devices = new ObservableCollection<SmartDevice>() // <- nie null
                };
                _vm.AddServer(prof);                     // ObservableCollection -> UI aktualisiert sich
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

            // optionales Geräte-Pairing (falls entity dabei)
            if (e.EntityId.HasValue)
            {
                var kind = string.IsNullOrWhiteSpace(e.EntityType) ||
                           e.EntityType!.Equals("server", StringComparison.OrdinalIgnoreCase)
                           ? "SmartSwitch" : e.EntityType;

                var dev = prof.Devices.FirstOrDefault(d => d.EntityId == e.EntityId.Value);
                if (dev is null)
                {
                    dev = new SmartDevice
                    {
                        EntityId = e.EntityId.Value,
                        Name = string.IsNullOrWhiteSpace(e.EntityName) ? e.ServerName : e.EntityName,
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

            _vm.Selected = prof;   // Auswahl umschalten → CurrentDevices aktualisiert sich
            _vm.Save();            // speichert _vm.Servers

            // KEINE ItemsSource-Setzung, KEIN Refresh – Binding macht’s.
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
    private async Task HandleDeviceToggleAsync(object sender, bool on)
    {
        if (_suppressToggleHandler) return;

        if ((sender as FrameworkElement)?.DataContext is not SmartDevice dev)
            return;

        // nur echte SmartSwitches schalten
        if (!string.Equals(dev.Kind, "SmartSwitch", StringComparison.OrdinalIgnoreCase))
            return;

        if (!await EnsureConnectedAsync()) return;

        AppendLog($"Sending {(on ? "ON" : "OFF")} to #{dev.EntityId} …");
        try
        {
            await _rust.ToggleSmartSwitchAsync(dev.EntityId, on, CancellationToken.None);

            // Status direkt für dieses Gerät neu laden
            await RefreshDeviceStateAsync(dev);
        }
        catch (Exception ex)
        {
            AppendLog($"{(on ? "ON" : "OFF")} Error: " + ex.Message);
            await RefreshDeviceStateAsync(dev);
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

    private static string FormatItemName(int id) => /* deine vorhandene Map-Funktion */ ResolveItemName(id,null);
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
            Panel.SetZIndex(fe, 500); // unter Dyn-Events (900), aber über Map
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
                
            }
            else if (fe.Tag != null)
            {
                // fallback: dynamic oder anonyme Typen
                dynamic d = fe.Tag;
                var p = WorldToImagePx((double)d.X, (double)d.Y);
                ApplyCurrentOverlayScale(fe);
                Canvas.SetLeft(fe, p.X - 14);
                Canvas.SetTop(fe, p.Y - 14);
                
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
        Overlay.MouseLeave += (_, __) => {
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
    private bool _announceSpawns = true;

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
                        Panel.SetZIndex(el, 900);
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
                        Panel.SetZIndex(host, 900);
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
        Panel.SetZIndex(el, 901); // über Events, unter Namen
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
            Panel.SetZIndex(el, 901);
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
            var shops = await real.GetVendingShopsAsync(); // siehe Teil B unten
            //AppendLog($"Shops: {shops.Count} Marker empfangen.");

            // Debug erster Marker → prüfe Mapping
            if (shops.Count > 0)
            {
                var s0 = shops[0];
                var p0 = WorldToImagePx(s0.X, s0.Y);

                //AppendLog($"Shops dbg: S={_worldSizeS} rect=({_worldRectPx.X:0},{_worldRectPx.Y:0},{_worldRectPx.Width:0}x{_worldRectPx.Height:0}) first=({s0.X:0},{s0.Y:0})->({p0.X:0},{p0.Y:0})");
            }

            UpdateShopsUI(shops);
            _lastShops = shops;
            if (_shopSearchWin?.IsVisible == true) RefreshShopSearchResults();
        }
        catch (Exception ex)
        {
            //AppendLog("Shops poll: " + ex.Message);
        }
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
                Panel.SetZIndex(sp, 1000);
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
        var w = new Window
        {
            Title = "Shop Search",
            Width = 560,
            Height = 560,
            Owner = this,
            Background = SearchWinBg,
            Foreground = SearchText
        };

        var root = new DockPanel { Margin = new Thickness(10) };

        // Top-Bar
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _searchTb = new TextBox { Width = 300, Margin = new Thickness(0, 0, 8, 0) };
        _chkSell = new CheckBox { Content = "Sells", IsChecked = true, Margin = new Thickness(0, 0, 8, 0), Foreground = SearchText };
        _chkBuy = new CheckBox { Content = "Buys", IsChecked = true, Foreground = SearchText };
        var btnGo = new Button { Content = "Search", Margin = new Thickness(8, 0, 0, 0) };

        btnGo.Click += (_, __) => RefreshShopSearchResults();
        _searchTb.TextChanged += (_, __) => RefreshShopSearchResults();
        _chkSell.Checked += (_, __) => RefreshShopSearchResults();
        _chkSell.Unchecked += (_, __) => RefreshShopSearchResults();
        _chkBuy.Checked += (_, __) => RefreshShopSearchResults();
        _chkBuy.Unchecked += (_, __) => RefreshShopSearchResults();

        top.Children.Add(_searchTb);
        top.Children.Add(btnGo);
        top.Children.Add(_chkSell);
        top.Children.Add(_chkBuy);

        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        // List
        _searchList = new ListBox
        {
            Background = SearchWinBg,
            BorderThickness = new Thickness(0),
            Foreground = SearchText
        };
        // dezente Auswahl
        _searchList.ItemContainerStyle = new Style(typeof(ListBoxItem))
        {
            Setters =
        {
            new Setter(Control.BackgroundProperty, Brushes.Transparent),
            new Setter(Control.BorderThicknessProperty, new Thickness(0)),
            new Setter(Control.PaddingProperty, new Thickness(0))
        }
        };

        root.Children.Add(_searchList);
        w.Content = root;

        _shopSearchWin = w;
        _shopSearchWin.Closed += (_, __) => _shopSearchWin = null;
    }

    private void RefreshShopSearchResults()
    {
        if (_shopSearchWin == null || _searchList == null) return;

        string q = _searchTb?.Text?.Trim() ?? "";
        bool wantSell = _chkSell?.IsChecked != false;
        bool wantBuy = _chkBuy?.IsChecked != false;

        // Hilfsfilter
        bool MatchesLeft(RustPlusClientReal.ShopOrder o)
            => string.IsNullOrEmpty(q)
               || ResolveItemName(o.ItemId, o.ItemShortName).Contains(q, StringComparison.OrdinalIgnoreCase);

        bool MatchesRight(RustPlusClientReal.ShopOrder o)
            => string.IsNullOrEmpty(q)
               || ResolveItemName(o.CurrencyItemId, o.CurrencyShortName).Contains(q, StringComparison.OrdinalIgnoreCase);

        _searchList.Items.Clear();

        foreach (var s in _lastShops)
        {
            if (s.Orders == null || s.Orders.Count == 0) continue;

            // Angebote filtern: “Sells” = linke Seite matcht; “Buys” = rechte Seite matcht.
            var offers = s.Orders.Where(o =>
                ((wantSell && MatchesLeft(o)) || (wantBuy && MatchesRight(o)))
            ).ToList();

            if (offers.Count == 0) continue;

            _searchList.Items.Add(BuildShopSearchCard(s, offers, compact:false));
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

    private CancellationTokenSource? _statusCts;
   

}

