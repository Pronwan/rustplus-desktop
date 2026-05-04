using RustPlusDesk.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Text.RegularExpressions;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private bool _firstMarkerPollDone = false;
    private class CargoDockInfo
    {
        public uint Id;
        public double LastX, LastY;
        public DateTime? DockTime;
        public bool AnnouncedDock;
        public bool AnnouncedEgressWarning;
        public string? HarborName;
        public bool IsDocked;
        public bool WasAlreadyDocked;
        public bool SeenAtEdge; // To confirm we saw it spawn
        public bool EverMoved;  // To confirm we saw it in motion
        public DateTime LastSeen;

        // Life Cycle Learning
        public DateTime? FirstSeen;
        public DateTime? LastDeparted;
        public int HarborCount;
        public bool AnnouncedArrivalWarning;
        public List<(DateTime Ts, double X, double Y)> History = new();
    }
    private readonly Dictionary<uint, CargoDockInfo> _cargoDockStates = new();
    private bool _firstPollDyn = true;
    private void BuildMonumentOverlays()
    {
        if (Overlay == null || _worldSizeS <= 0 || _worldRectPx.Width <= 0) return;

        foreach (var kv in _monEls) Overlay.Children.Remove(kv.Value);
        _monEls.Clear();

        string host = _rust?.Host ?? "unknown";
        var currentHarbors = _monData
            .Where(m => m.Name?.Contains("Harbor", StringComparison.OrdinalIgnoreCase) == true)
            .Select(m => new HarborInfo { Name = m.Name!, X = m.X, Y = m.Y })
            .OrderBy(h => h.Name).ToList();

        var savedHarbors = TrackingService.GetServerHarbors(host).OrderBy(h => h.Name).ToList();
        bool wipe = false;
        if (currentHarbors.Count != savedHarbors.Count) wipe = true;
        else
        {
            for (int i = 0; i < currentHarbors.Count; i++)
            {
                if (currentHarbors[i].Name != savedHarbors[i].Name || 
                    Math.Abs(currentHarbors[i].X - savedHarbors[i].X) > 50 || 
                    Math.Abs(currentHarbors[i].Y - savedHarbors[i].Y) > 50)
                {
                    wipe = true; break;
                }
            }
        }
        if (wipe && currentHarbors.Count > 0)
        {
            TrackingService.SetServerHarbors(host, currentHarbors);
        }

        foreach (var m in _monData)
        {
            var p = WorldToImagePx(m.X, m.Y);

            var key = NormalizeMonName(m.Name, out var variant);
            var nice = Beautify(m.Name);
            var tt = string.IsNullOrEmpty(variant) ? nice : $"{nice} ({variant})";

            var fe = MakeMonIcon(key, tt, 28);
            fe.Tag = m;

            Overlay.Children.Add(fe);
            Panel.SetZIndex(fe, 800);
            _monEls[key + "@" + p.X.ToString("0") + "," + p.Y.ToString("0")] = fe;

            ApplyCurrentOverlayScale(fe);
            Canvas.SetLeft(fe, p.X - 14);
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
                var p = WorldToImagePx(m.Item1, m.Item2);
                ApplyMonumentScale(fe);
                Canvas.SetLeft(fe, p.X - fe.RenderSize.Width / 2);
                Canvas.SetTop(fe, p.Y - fe.RenderSize.Height / 2);
                Panel.SetZIndex(fe, 800);
            }
            else if (fe.Tag != null)
            {
                dynamic d = fe.Tag;
                var p = WorldToImagePx((double)d.X, (double)d.Y);
                ApplyCurrentOverlayScale(fe);
                Canvas.SetLeft(fe, p.X - 14);
                Canvas.SetTop(fe, p.Y - 14);
                Panel.SetZIndex(fe, 800);
            }
        }
    }

    private void EnsureShopsHoverPopup()
    {
        // Legacy multi-shop popup disabled in favor of new clustering and interactive details panel.
        /*
        if (_shopsHoverPopup != null) return;
        ...
        */
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
            EnableSingleShopTooltips(false);
            _shopsHoverWrap.Children.Clear();

            foreach (var fe in hits)
            {
                if (fe.Tag is RustPlusClientReal.ShopMarker s)
                {
                    var offers = s.Orders ?? Enumerable.Empty<RustPlusClientReal.ShopOrder>();
                    var card = BuildShopSearchCard(s, offers, compact: true);
                    card.Width = SHOP_CARD_WIDTH;
                    ToolTipService.SetIsEnabled(card, false);
                    _shopsHoverWrap.Children.Add(card);
                }
            }

            _shopsHoverPopup.HorizontalOffset = pt.X + 16;
            _shopsHoverPopup.VerticalOffset = pt.Y + 16;
            _shopsHoverPopup.IsOpen = true;
        }
        else
        {
            _shopsHoverPopup.IsOpen = false;
            EnableSingleShopTooltips(true);
        }
    }

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
            ShowMapBasic(map.Bitmap);
            SetupMapScene(map.Bitmap);
            _worldSizeS = map.WorldSize;
            _worldRectPx = ComputeWorldRectFromWorldSize(map.PixelWidth, map.PixelHeight, _worldSizeS, 2000);
            ResetMapZoom();
            RedrawGrid();
            Dispatcher.InvokeAsync(() =>
            {
                RefreshAllOverlayScales();
                RefreshMonumentOverlayPositions();
            }, DispatcherPriority.Loaded);
            StartDynPolling();
            SyncAlertMenuItems(); // Refresh arrival warning enabled state now that host is known

            Overlay.Width = ImgMap.Width;
            Overlay.Height = ImgMap.Height;
            GridLayer.Width = ImgMap.Width;
            GridLayer.Height = ImgMap.Height;

            RedrawGrid();

            int imgW = map.PixelWidth;
            int imgH = map.PixelHeight;
            int s = map.WorldSize;
            _monData = map.Monuments.ToList();
            BuildMonumentOverlays();
            var worldRectPx = ComputeWorldRectFromWorldSize(imgW, imgH, s, padWorld: 2000);
            AppendLog($"worldRectPx(fromS)=[{(int)worldRectPx.X},{(int)worldRectPx.Y},{(int)worldRectPx.Width}x{(int)worldRectPx.Height}] img={imgW}x{imgH} S={s}");

            var mons = map.Monuments.Where(m => !string.IsNullOrWhiteSpace(m.Name)).ToList();

            foreach (var m in mons)
            {
                bool off = (m.X < 0) || (m.Y < 0) || (m.X > s) || (m.Y > s);
                double cx = Math.Clamp(m.X, 0, s);
                double cy = Math.Clamp(m.Y, 0, s);

                double u = worldRectPx.X + (cx / s) * worldRectPx.Width;
                double v = worldRectPx.Y + ((s - cy) / s) * worldRectPx.Height;

                if (off)
                {
                    const double nudge = 0;
                    if (m.X < 0) u -= nudge; else if (m.X > s) u += nudge;
                    if (m.Y < 0) v += nudge; else if (m.Y > s) v -= nudge;
                }
            }
        });
    }

    private bool _v1MarkerResetDone = false;

    private void StartDynPolling()
    {
        _dynTimer?.Stop();
        _dynTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _dynTimer.Tick += async (_, __) => await PollDynMarkersOnceAsync();
        _dynTimer.Start();
    }

    private void StopDynPolling(bool clearKnown = true)
    {
        _dynTimer?.Stop();
        _dynTimer = null;

        foreach (var kv in _dynEls) Overlay.Children.Remove(kv.Value);
        _dynEls.Clear();
        _dynStates.Clear();
        if (clearKnown) _dynKnown.Clear();
    }

    private void ChkPlayers_Checked(object sender, RoutedEventArgs e)
    {
        _showPlayers = (ChkPlayers.IsChecked != false);
        foreach (var kv in _dynEls)
        {
            if (kv.Value.Tag is RustPlusClientReal.DynMarker dm)
            {
                if (dm.Type == 1)
                    kv.Value.Visibility = _showPlayers ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private FrameworkElement BuildEventIconHost(FrameworkElement inner, string? tooltip, int size, double? scaleExp = null, double? baseMult = null)
    {
        var host = new Grid { Width = size, Height = size, IsHitTestVisible = true };
        if (tooltip != null) ToolTipService.SetToolTip(host, tooltip);

        host.Children.Add(inner);
        
        var timerTxt = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 1, 4, 1)
        };

        var timerBox = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(160, 40, 40, 40)),
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, -22),
            Visibility = Visibility.Collapsed,
            Child = timerTxt
        };
        host.Children.Add(timerBox);

        host.Tag = new PlayerMarkerTag
        {
            Radius = size * 0.5,
            ScaleExp = scaleExp ?? SHOP_SIZE_EXP,
            ScaleBaseMult = baseMult ?? SHOP_BASE_MULT,
            ScaleTarget = inner,
            ScaleCenterX = size * 0.5,
            ScaleCenterY = size * 0.5,
            TimerText = timerTxt,
            TimerContainer = timerBox
        };

        return host;
    }

    private void ProcessCargoDocking(RustPlusClientReal.DynMarker m)
    {
        if (m.Type != 5) return;
        
        string host = _rust?.Host ?? "unknown";

        if (!_cargoDockStates.TryGetValue(m.Id, out var state))
        {
            state = new CargoDockInfo { Id = m.Id, LastX = m.X, LastY = m.Y, FirstSeen = DateTime.UtcNow };
            
            // Check if it's near the edge (Spawn detection)
            double distFromCenter = Math.Sqrt(m.X * m.X + m.Y * m.Y);
            if (distFromCenter > (_worldSizeS * 0.42)) // Approx edge of map
            {
                state.SeenAtEdge = true;
            }

            _cargoDockStates[m.Id] = state;
        }
        state.LastSeen = DateTime.UtcNow;

        state.History.Add((DateTime.UtcNow, m.X, m.Y));
        if (state.History.Count > 150) 
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-12);
            state.History.RemoveAll(h => h.Ts < cutoff);
        }

        double dx = m.X - state.LastX;
        double dy = m.Y - state.LastY;
        double distMoved = Math.Sqrt(dx * dx + dy * dy);
        
        // Threshold for stationary (approx < 0.5m per poll)
        bool isStationary = distMoved < 0.5; 
        if (!isStationary) state.EverMoved = true;
        
        if (isStationary && !state.IsDocked)
        {
            var harbor = _monData.FirstOrDefault(mon => 
                (mon.Name?.Contains("Harbor", StringComparison.OrdinalIgnoreCase) == true) && 
                Math.Sqrt(Math.Pow(mon.X - m.X, 2) + Math.Pow(mon.Y - m.Y, 2)) < 300);
            
            if (harbor.Name != null)
            {
                state.IsDocked = true;
                state.DockTime = DateTime.UtcNow;
                state.HarborName = Beautify(harbor.Name);
                state.AnnouncedDock = false;
                state.AnnouncedEgressWarning = false;
                state.HarborCount++;
                
                // If it's stationary the VERY first time we see it, it was already there
                if (isStationary && !state.EverMoved && state.LastX == m.X && state.LastY == m.Y) 
                {
                    state.WasAlreadyDocked = true;
                    state.AnnouncedDock = true; // Suppress docked alert
                }

                // Learn Trigger Point (Look back 5 minutes)
                if (!state.WasAlreadyDocked)
                {
                    var targetTs = DateTime.UtcNow.AddMinutes(-5);
                    var best = state.History.OrderBy(h => Math.Abs((h.Ts - targetTs).TotalSeconds)).FirstOrDefault();
                    if (best.Ts != default && (DateTime.UtcNow - best.Ts).TotalMinutes > 4)
                    {
                        TrackingService.SetCargoTriggerPoint(host, harbor.Name, best.X, best.Y);
                        // Auto-enable arrival warning now that this harbor's route is known
                        if (!TrackingService.AnnounceCargoArrival && TrackingService.AnnounceSpawnsMaster)
                        {
                            TrackingService.AnnounceCargoArrival = true;
                            _ = Dispatcher.InvokeAsync(SyncAlertMenuItems);
                        }
                    }
                }

                if (_announceSpawns && TrackingService.AnnounceCargoDocking && !state.WasAlreadyDocked)
                {
                    // Will be announced after 5s stationary delay to prevent spam
                }
            }
        }
        else if (distMoved > 2.0 && state.IsDocked)
        {
            if (state.DockTime.HasValue && !state.WasAlreadyDocked)
            {
                var duration = DateTime.UtcNow - state.DockTime.Value;
                if (duration.TotalMinutes > 2)
                {
                    int learned = (int)Math.Round(duration.TotalMinutes);
                    TrackingService.SetLearnedDockingDuration(host, learned);
                }
            }
            // Just departed or moved slightly
            state.LastDeparted = DateTime.UtcNow;
            state.IsDocked = false;
            state.DockTime = null;
            state.AnnouncedDock = false;
            state.AnnouncedEgressWarning = false;
            state.WasAlreadyDocked = false;
            state.AnnouncedArrivalWarning = false; // Reset for next harbor
        }

        // Docking announcement with 5s delay
        if (state.IsDocked && !state.AnnouncedDock && TrackingService.AnnounceCargoDocking && _announceSpawns && state.DockTime.HasValue)
        {
            if ((DateTime.UtcNow - state.DockTime.Value).TotalSeconds >= 5)
            {
                string grid = GetGridLabel(m.X, m.Y);
                _ = SendTeamChatSafeAsync($"Cargo Ship docked at {state.HarborName} ({grid})");
                state.AnnouncedDock = true;
            }
        }

        // Arrival Warning (Trigger Point Detection)
        if (!state.IsDocked && !state.AnnouncedArrivalWarning && TrackingService.AnnounceCargoArrival && _announceSpawns)
        {
            var harbors = _monData.Where(mon => mon.Name?.Contains("Harbor", StringComparison.OrdinalIgnoreCase) == true);
            foreach (var h in harbors)
            {
                var tp = TrackingService.GetCargoTriggerPoint(host, h.Name!);
                if (tp != null)
                {
                    double dToTp = Math.Sqrt(Math.Pow(m.X - tp.X, 2) + Math.Pow(m.Y - tp.Y, 2));
                    if (dToTp < 150) // Proximity to trigger point
                    {
                        double dToH = Math.Sqrt(Math.Pow(m.X - h.X, 2) + Math.Pow(m.Y - h.Y, 2));
                        double dLastToH = Math.Sqrt(Math.Pow(state.LastX - h.X, 2) + Math.Pow(state.LastY - h.Y, 2));

                        if (dToH < dLastToH) // Approaching
                        {
                            string grid = GetGridLabel(h.X, h.Y);
                            _ = SendTeamChatSafeAsync($"Cargo Ship expected to dock at {Beautify(h.Name!)} ({grid}) in approx. 5 minutes.");
                            state.AnnouncedArrivalWarning = true;
                            break;
                        }
                    }
                }
            }
        }

        // Egress warning (5 minutes before expected departure)
        if (state.IsDocked && state.DockTime.HasValue && !state.AnnouncedEgressWarning && TrackingService.AnnounceCargoEgress && _announceSpawns)
        {
            int duration = TrackingService.GetLearnedDockingDuration(host);
            var elapsed = DateTime.UtcNow - state.DockTime.Value;
            if (elapsed.TotalMinutes >= (duration - 5) && duration > 5)
            {
                string grid = GetGridLabel(m.X, m.Y);
               // _ = SendTeamChatSafeAsync($"Cargo Ship departing from Harbor in 5 minutes ({grid})");
                _ = SendTeamChatSafeAsync($"Cargo Ship departing from {state.HarborName} in 5 minutes ({grid})");
                state.AnnouncedEgressWarning = true;
            }
        }

        state.LastX = m.X;
        state.LastY = m.Y;
    }

    private void CleanupCargoDockStates()
    {
        string host = _rust?.Host ?? "unknown";
        var keys = _cargoDockStates.Keys.ToList();
        var now = DateTime.UtcNow;

        foreach (var key in keys)
        {
            var state = _cargoDockStates[key];
            // Grace period: 60 seconds before we forget the ship
            if ((now - state.LastSeen).TotalSeconds > 60)
            {
                if (state.FirstSeen.HasValue && state.HarborCount >= 1 && state.SeenAtEdge) 
                {
                    // Only learn full life if we saw it come in from the edge
                    int total = (int)(now - state.FirstSeen.Value).TotalMinutes;
                    if (total > 20) // Sanity check for a full run
                    {
                        TrackingService.SetLearnedCargoFullLife(host, total);
                    }
                }
                _cargoDockStates.Remove(key);
            }
        }
    }

    private FrameworkElement BuildEventDot(string tooltip, int size = 14, double? scaleExp = null, double? baseMult = null)
    {
        var dot = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = Brushes.Orange,
            Stroke = Brushes.Black,
            StrokeThickness = 1.5
        };
        return BuildEventIconHost(dot, tooltip, size, scaleExp, baseMult);
    }

    private async Task PollDynMarkersOnceAsync()
    {
        if (_rust is not RustPlusClientReal real) return;
        if (_worldSizeS <= 0 || _worldRectPx.Width <= 0) return;

        try
        {
            if (!_monumentWatcher.HasMonuments)
            {
                var staticMons = await real.GetStaticMonumentsAsync();
                if (staticMons != null && staticMons.Count > 0)
                {
                    _monumentWatcher.SetMonuments(staticMons);
                }
            }

            var list = await real.GetDynamicMapMarkersAsync();
            var virtualMarkers = _monumentWatcher.UpdateAndGetVirtualMarkers(list, _dynKnown);

            var combinedList = new List<RustPlusClientReal.DynMarker>(list.Count + virtualMarkers.Count);
            combinedList.AddRange(list);
            combinedList.AddRange(virtualMarkers);

            if (list.Count > 0)
            {
                var cPlayers = list.Count(m => m.Type == 1);
                var cCargo = list.Count(m => m.Type == 5);
                var cCrate = list.Count(m => m.Type == 6);
                var cCH47 = list.Count(m => m.Type == 4);
                var cPatrol = list.Count(m => m.Type == 8);
            }

            UpdateDynUI(combinedList);
            UpdateEventDock(combinedList);

            _firstMarkerPollDone = true;



            _ = Dispatcher.InvokeAsync(() => RefreshAllOverlayScales(), DispatcherPriority.Loaded);
        }
        catch
        {
        }
    }

    private struct EventDockItem
    {
        public string Name;
        public string Icon;
        public bool Active;
        public uint Id;
        public double X;
        public double Y;
        public bool Trackable;
        public int Type;
        public string? TimerText;
        public string? ToolTip;
    }

    private RustPlusClientReal.DynMarker GetPersistentEvent(IReadOnlyList<RustPlusClientReal.DynMarker> markers, int type)
    {
        var m = markers.FirstOrDefault(m => m.Type == type);
        if (m.Id != 0) return m;

        // Fallback: Check persistence in _dynStates
        var entry = _dynStates.FirstOrDefault(kv => kv.Value.Type == type && kv.Value.MissingCount > 0 && kv.Value.MissingCount < 5);
        if (entry.Value != null && entry.Value.History.Count > 0)
        {
            var last = entry.Value.History.Last();
            return new RustPlusClientReal.DynMarker(
                entry.Key, 
                type, 
                EventKindText(type), 
                last.X, 
                last.Y, 
                null, 
                null, 
                0, 
                (float)entry.Value.LastCalculatedAngle
            );
        }
        return default;
    }

    private void UpdateEventDock(IReadOnlyList<RustPlusClientReal.DynMarker> markers)
    {
        if (EventDock == null) return;

        var activeEvents = new List<EventDockItem>();

        // 1. Patrol Heli (Type 8)
        var heli = GetPersistentEvent(markers, 8);
        activeEvents.Add(new EventDockItem { Name = "Patrol Heli", Icon = "pack://application:,,,/icons/animat-Icons/patrol_helicopter.png", Active = heli.Id != 0, Id = heli.Id, X = heli.X, Y = heli.Y, Trackable = true, Type = 8 });
 
        // 2. Cargo Ship (Type 5)
        var cargo = GetPersistentEvent(markers, 5);
        string host = _rust?.Host ?? "unknown";
        int cargoLife = TrackingService.GetLearnedCargoFullLife(host);
        string? cargoTimer = null;
        string? cargoTip = null;

        if (cargo.Id != 0 && _cargoDockStates.TryGetValue(cargo.Id, out var ds))
        {
            if (cargoLife > 0)
            {
                if (ds.SeenAtEdge && ds.FirstSeen.HasValue)
                {
                    // We saw the spawn — remaining time is accurate
                    var remain = TimeSpan.FromMinutes(cargoLife) - (DateTime.UtcNow - ds.FirstSeen.Value);
                    if (remain.TotalSeconds > 0)
                    {
                        cargoTimer = $"{(int)remain.TotalMinutes}:{remain.Seconds:D2}";
                        cargoTip = "Total time remaining on map";
                    }
                }
                else
                {
                    // Connected mid-route — we know the total duration but not how far along cargo is
                    cargoTimer = "??:??";
                    cargoTip = $"Route learned (~{cargoLife}m total), connected mid-event";
                }
            }
        }

        activeEvents.Add(new EventDockItem { Name = "Cargo Ship", Icon = "pack://application:,,,/icons/cargo.png", Active = cargo.Id != 0, Id = cargo.Id, X = cargo.X, Y = cargo.Y, Trackable = true, Type = 5, TimerText = cargoTimer, ToolTip = cargoTip });
 
        // 3. Chinook (Type 4)
        var chinook = GetPersistentEvent(markers, 4);
        activeEvents.Add(new EventDockItem { Name = "Chinook", Icon = "pack://application:,,,/icons/ch47.png", Active = chinook.Id != 0, Id = chinook.Id, X = chinook.X, Y = chinook.Y, Trackable = true, Type = 4 });
 
        // 4. Vendor (Type 6)
        var vendor = GetPersistentEvent(markers, 6);
        activeEvents.Add(new EventDockItem { Name = "Travelling Vendor", Icon = "pack://application:,,,/icons/vendor.png", Active = vendor.Id != 0, Id = vendor.Id, X = vendor.X, Y = vendor.Y, Trackable = true, Type = 6 });
 
        // 5. Deep Sea (Using native _deepSeaActive logic)
        activeEvents.Add(new EventDockItem { Name = "Deep Sea Event", Icon = "pack://application:,,,/icons/ds_event.png", Active = _deepSeaActive, Id = 0, X = 0, Y = 0, Trackable = false, Type = 0 });

        Dispatcher.Invoke(() =>
        {
            // Try to find existing dock or create one
            var mainBorder = EventDock.Children.OfType<Border>().FirstOrDefault(b => b.Tag as string == "MainDock");
            StackPanel stack;

            if (mainBorder == null)
            {
                mainBorder = new Border
                {
                    Tag = "MainDock",
                    Background = new SolidColorBrush(Color.FromArgb(180, 20, 25, 30)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(6),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                stack = new StackPanel { Orientation = Orientation.Vertical };
                mainBorder.Child = stack;
                EventDock.Children.Add(mainBorder);

                // Hover logic once
                mainBorder.MouseEnter += (s, e) => {
                    var items = stack.Children.OfType<Grid>().ToList();
                    foreach (var item in items) {
                        foreach (var lb in item.Children.OfType<TextBlock>()) {
                            lb.Visibility = Visibility.Visible;
                            lb.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
                        }
                    }
                };
                mainBorder.MouseLeave += (s, e) => {
                    var items = stack.Children.OfType<Grid>().ToList();
                    foreach (var item in items) {
                        foreach (var lb in item.Children.OfType<TextBlock>()) {
                            var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
                            var targetLb = lb;
                            anim.Completed += (s2, e2) => targetLb.Visibility = Visibility.Collapsed;
                            lb.BeginAnimation(UIElement.OpacityProperty, anim);
                        }
                    }
                };
            }
            else
            {
                stack = (StackPanel)mainBorder.Child;
            }

            // Sync items
            for (int i = 0; i < activeEvents.Count; i++)
            {
                var ev = activeEvents[i];
                bool isClickable = ev.Active && ev.Trackable;
                Grid itemRow;

                if (i < stack.Children.Count)
                {
                    itemRow = (Grid)stack.Children[i];
                    if (itemRow.Children.Count < 5 || itemRow.RowDefinitions.Count < 2) { stack.Children.Clear(); i = -1; continue; } // Force rebuild on structure change
                }
                else
                {
                    itemRow = new Grid { Margin = new Thickness(0, 2, 0, 2), UseLayoutRounding = true, SnapsToDevicePixels = true };
                    itemRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 0: name
                    itemRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1: timer
                    itemRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                    itemRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    
                    // Add components once
                    var glow = new System.Windows.Shapes.Ellipse { Width = 32, Height = 32, Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 10 }, HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
                    Grid.SetColumn(glow, 0); Grid.SetRowSpan(glow, 2); itemRow.Children.Add(glow);

                    var iconHost = new Grid { Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(iconHost, 0); Grid.SetRowSpan(iconHost, 2); itemRow.Children.Add(iconHost);

                    var img = new Image { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    iconHost.Children.Add(img);

                    // Row 0: event name
                    var txt = new TextBlock { Foreground = Brushes.White, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 2, 12, 0), Visibility = mainBorder.IsMouseOver ? Visibility.Visible : Visibility.Collapsed, Opacity = mainBorder.IsMouseOver ? 1 : 0 };
                    Grid.SetColumn(txt, 1); Grid.SetRow(txt, 0); itemRow.Children.Add(txt);

                    // Row 1: countdown timer (directly below name)
                    var timer = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 255)), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 12, 2), Opacity = mainBorder.IsMouseOver ? 0.9 : 0, Visibility = mainBorder.IsMouseOver ? Visibility.Visible : Visibility.Collapsed };
                    Grid.SetColumn(timer, 1); Grid.SetRow(timer, 1); itemRow.Children.Add(timer);

                    var dot = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 4, 0) };
                    Grid.SetColumn(dot, 0); Grid.SetRowSpan(dot, 2); itemRow.Children.Add(dot);

                    stack.Children.Add(itemRow);
                }

                // Update states
                itemRow.Cursor = isClickable ? Cursors.Hand : Cursors.Arrow;
                itemRow.Opacity = ev.Active ? 1.0 : 0.35;
                itemRow.Tag = ev; // Store for click handler

                var uiGlow = (System.Windows.Shapes.Ellipse)itemRow.Children[0];
                uiGlow.Fill = new SolidColorBrush(Color.FromArgb(40, 0, 200, 255));
                uiGlow.Visibility = ev.Active ? Visibility.Visible : Visibility.Collapsed;

                var uiIconHost = (Grid)itemRow.Children[1];
                var uiImg = (Image)uiIconHost.Children[0];
                if (uiImg.Source == null || uiImg.Tag as string != ev.Icon) {
                    try { 
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = new Uri(ev.Icon);
                        bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.EndInit();
                        uiImg.Source = bi; 
                        uiImg.Tag = ev.Icon; 
                    } catch {}
                }

                // Add direct icon glow for active events
                if (ev.Active)
                {
                    uiImg.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Colors.Cyan,
                        BlurRadius = 8,
                        ShadowDepth = 0,
                        Opacity = 0.8
                    };
                }
                else
                {
                    uiImg.Effect = null;
                }

                // Handle Animated Blades for Heli (Type 8) and Chinook (Type 4)
                if (ev.Type == 8 || ev.Type == 4)
                {
                    int rotorCount = ev.Type == 4 ? 2 : 1;
                    while (uiIconHost.Children.Count - 1 < rotorCount)
                    {
                        var blades = new Image
                        {
                            Width = 24,
                            Height = 24,
                            Source = new BitmapImage(new Uri("pack://application:,,,/icons/animat-Icons/chinook_map_blades.png")),
                            RenderTransformOrigin = new Point(0.5, 0.5),
                            RenderTransform = new RotateTransform(0),
                            IsHitTestVisible = false
                        };
                        RenderOptions.SetBitmapScalingMode(blades, BitmapScalingMode.HighQuality);
                        uiIconHost.Children.Add(blades);
                    }

                    for (int r = 0; r < rotorCount; r++)
                    {
                        var uiBlades = (Image)uiIconHost.Children[r + 1];
                        var rt = (RotateTransform)uiBlades.RenderTransform;

                        if (ev.Active)
                        {
                            if (uiBlades.Tag as string != "Spinning")
                            {
                                var anim = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.5)) { RepeatBehavior = RepeatBehavior.Forever };
                                rt.BeginAnimation(RotateTransform.AngleProperty, anim);
                                uiBlades.Tag = "Spinning";
                            }
                        }
                        else
                        {
                            rt.BeginAnimation(RotateTransform.AngleProperty, null);
                            uiBlades.Tag = null;
                        }

                        // Offsets for rotors
                        if (ev.Type == 4) // Chinook
                        {
                            uiBlades.Margin = r == 0 ? new Thickness(0, 0, 0, 16) : new Thickness(0, 16, 0, 0);
                        }
                        else
                        {
                            uiBlades.Margin = new Thickness(0);
                        }
                    }

                    // Nudge body icon for Heli (Type 8) to align with centered rotor
                    if (ev.Type == 8) uiImg.Margin = new Thickness(0, 8, 0, 0);
                    else uiImg.Margin = new Thickness(0);
                }
                else
                {
                    while (uiIconHost.Children.Count > 1) uiIconHost.Children.RemoveAt(1);
                    uiImg.Margin = new Thickness(0);
                }

                var uiTxt = (TextBlock)itemRow.Children[2];
                uiTxt.Text = ev.Name;
                uiTxt.FontWeight = ev.Active ? FontWeights.SemiBold : FontWeights.Normal;

                var uiTimer = (TextBlock)itemRow.Children[3];
                uiTimer.Text = ev.TimerText ?? "";
                // Visibility is managed by hover logic, but we must update the state
                if (string.IsNullOrEmpty(ev.TimerText) || !ev.Active) uiTimer.Visibility = Visibility.Collapsed;

                var uiDot = (System.Windows.Shapes.Ellipse)itemRow.Children[4];
                uiDot.Fill = ev.Active ? Brushes.Cyan : Brushes.Transparent;
                
                // Tooltip
                itemRow.ToolTip = ev.ToolTip;

                // Refresh Click Handler (clear first to avoid duplicates)
                itemRow.MouseLeftButtonDown -= EventItem_Click;
                if (isClickable) itemRow.MouseLeftButtonDown += EventItem_Click;
            }
        });
    }

    private void EventItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is EventDockItem ev)
        {
            _trackingEntityId = ev.Id;
            CenterMapOnWorldAnimated(ev.X, ev.Y, false, true);
            e.Handled = true;
        }
    }

    private static uint DynFallbackKey(double x, double y, string? label, int type)
    {
        unchecked
        {
            uint h = 2166136261;
            void mix(ulong v) { for (int i = 0; i < 8; i++) { h ^= (byte)(v & 0xFF); h *= 16777619; v >>= 8; } }
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

    private void UpdateDynUI(IReadOnlyList<RustPlusClientReal.DynMarker> markers)
    {
        _announceSpawns = TrackingService.AnnounceSpawnsMaster;
        if (!_v1MarkerResetDone)
        {
            try {
                if (StorageService.LoadCache<bool>("v1_marker_reset_v2") == false) {
                    foreach (var kv in _dynEls.ToList()) Overlay.Children.Remove(kv.Value);
                    _dynEls.Clear();
                    _dynStates.Clear();
                    _dynKnown.Clear();
                    StorageService.SaveCache("v1_marker_reset_v2", true);
                    AppendLog("One-time marker refresh performed.");
                }
            } catch { }
            _v1MarkerResetDone = true;
        }

        _lastMarkers = markers;
        if (Overlay == null || _worldSizeS <= 0 || _worldRectPx.Width <= 0) return;

        var incoming = new HashSet<uint>();

        foreach (var m in markers)
        {
            if (m.Type == 0 && m.SteamId == 0) continue;

            bool isPlayer = (m.Type == 1);
            if (m.Type == 5) ProcessCargoDocking(m);
            if (isPlayer && m.SteamId != 0)
                _lastPlayersBySid[m.SteamId] = (m.X, m.Y, ResolvePlayerName(m));

            if (isPlayer && !_showPlayers) continue;
        }


        foreach (var m in markers)
        {
            if (m.Type == 0 && m.SteamId == 0) continue;
            bool isPlayer = (m.Type == 1);
            if (isPlayer && !_showPlayers) continue;

            bool knownEventType = !isPlayer && sDynIconByType.ContainsKey(m.Type);
            uint key = m.Id != 0 ? m.Id : DynFallbackKey(m.X, m.Y, m.Label ?? m.Kind, m.Type);
            incoming.Add(key);

            // Track state and velocity for smooth transitions and persistence
            if (!_dynStates.TryGetValue(key, out var state))
            {
                state = new DynMarkerState();
                
                // Check if it's near the edge (Spawn detection - outside playable grid)
                double distFromCenter = Math.Sqrt(m.X * m.X + m.Y * m.Y);
                if (distFromCenter > (_worldSizeS * 0.5)) 
                {
                    state.SeenAtEdge = true;
                }

                _dynStates[key] = state;
            }
            state.Type = m.Type;
            if (state.History.Count > 0)
            {
                var last = state.History.Last();
                // Simple velocity calculation based on 1s polling
                state.LastVX = m.X - last.X;
                state.LastVY = m.Y - last.Y;
            }
            state.History.Add((m.X, m.Y));
            if (state.History.Count > 5) state.History.RemoveAt(0);
            state.MissingCount = 0;

            bool online = false, dead = false;
            if (_lastPresence.TryGetValue(m.SteamId, out var pr)) { online = pr.Item1; dead = pr.Item2; }

            if (_showDeathMarkers)
            {
                if (_lastPresence.TryGetValue(m.SteamId, out var prevPresence))
                {
                    if (!prevPresence.dead && dead)
                    {
                        var vm = TeamMembers.FirstOrDefault(t => t.SteamId == m.SteamId);
                        if (vm != null) { vm.X = m.X; vm.Y = m.Y; Dispatcher.Invoke(() => PlaceDeathPin(vm)); }
                        else { Dispatcher.Invoke(() => PlaceOrMoveDeathPin(m.SteamId, m.X, m.Y, ResolvePlayerName(m))); }
                    }
                }
            }

            var nameNow = ResolvePlayerName(m);

            bool isNew = false;
            if (!_dynEls.TryGetValue(key, out var el))
            {
                isNew = true;
                try
                {
                    if (m.Type == 150)
                    {
                        var img = MakeIcon("pack://application:,,,/icons/crate3.png", 48);
                        var host = BuildEventIconHost(img, m.Label, 48);
                        el = host;
                    }
                    else if (m.Type == 8 || m.Type == 4) // Patrol Helicopter or Chinook
                    {
                        el = BuildAnimatedAirVehicleMarker(m);
                        AttachTrackingHandler(el, m.Id); // Enable tracking
                    }
                    else if (isPlayer)
                    {
                        if (_showProfileMarkers) el = BuildPlayerMarker(m.SteamId, nameNow, online, dead);
                        else el = BuildPlayerDotMarker(m.SteamId, nameNow, online, dead);
                    }
                    else
                    {
                        FrameworkElement host;
                        if (knownEventType)
                        {
                            try
                            {
                                // Cargo Ship (Type 5) should scale naturally (grow on zoom in, shrink on zoom out)
                                bool isCargo = (m.Type == 5);
                                int size = isCargo ? 48 : 64;
                                double exp = isCargo ? 0.5 : SHOP_SIZE_EXP;
                                double mult = isCargo ? SHOP_BASE_MULT : SHOP_BASE_MULT;

                                var img = MakeIcon(sDynIconByType[m.Type], size);
                                host = BuildEventIconHost(img, m.Label ?? m.Kind, size, exp, mult);
                            }
                            catch
                            {
                                host = BuildEventDot($"{m.Kind} ({m.Type})", 14);
                            }
                        }
                        else 
                        {
                            bool isCargo = (m.Type == 5);
                            double exp = isCargo ? 0.5 : SHOP_SIZE_EXP;
                            double mult = SHOP_BASE_MULT;
                            host = BuildEventDot($"{m.Kind} ({m.Type})", 14, exp, mult);
                        }

                        // Enable tracking for specific large events
                        if (m.Type == 5 || m.Type == 4 || m.Type == 6)
                        {
                            AttachTrackingHandler(host, m.Id);
                        }
                        el = host;
                    }

                    _dynEls[key] = el;

                    // Announcement Logic for all dynamic events (API types and internal virtual markers)
                    if (!isPlayer && (knownEventType || m.Type == 150) && !_dynKnown.Contains(key))
                    {
                        _dynKnown.Add(key);
                        AppendLog($"[DynEvent] New: Type={m.Type}, Kind={m.Kind}, Label={m.Label}");

                        bool shouldAnnounce = m.Type switch
                        {
                            5 => TrackingService.AnnounceCargo && (state.MissingCount > 0 || state.SeenAtEdge),
                            8 => TrackingService.AnnounceHeli,
                            4 => TrackingService.AnnounceChinook,
                            6 => TrackingService.AnnounceVendor,
                            9 => TrackingService.AnnounceOilRig,
                            _ => true 
                        };

                        if (_announceSpawns && shouldAnnounce && _firstMarkerPollDone && !_firstPollDyn)
                        {
                            var grid = GetGridLabel(m.X, m.Y);
                            var kind = EventKindText(m.Type);
                            _ = SendTeamChatSafeAsync($"{kind} spawned in at {grid}");
                        }
                    }

                    Overlay.Children.Add(el);
                    Panel.SetZIndex(el, m.Type == 150 ? 2000 : (isPlayer ? 950 : 920));

                    if (el.Tag is PlayerMarkerTag pmtNew)
                    {
                        if (m.Type == 5 || m.Type == 8 || m.Type == 4)
                        {
                            pmtNew.Rotation = -m.Rotation;
                        }
                        else
                        {
                            double correction = (m.Type == 6 || m.Type == 3) ? 180 : 0;
                            pmtNew.Rotation = m.Rotation + correction;
                        }
                    }

                    ApplyCurrentOverlayScale(el);
                }
                catch { continue; }
            }
            else
            {
                var oldEl = el;
                if (m.Type == 150)
                {
                    if (el is FrameworkElement fe)
                    {
                        fe.ToolTip = m.Label;
                    }
                }
                else if (isPlayer) 
                {
                    UpdatePlayerMarker(ref el, key, m.SteamId, nameNow, online, dead);
                }
                else if (el.Tag is not PlayerMarkerTag) 
                {
                    el.Tag = m;
                }

                if (el.Tag is PlayerMarkerTag pmt2 && !isPlayer)
                {
                    bool isCargo = (m.Type == 5);
                    pmt2.ScaleExp = isCargo ? 0.5 : SHOP_SIZE_EXP;
                    pmt2.ScaleBaseMult = SHOP_BASE_MULT;
                }

                // If el was replaced (e.g. dot -> avatar), transfer position for smooth transition
                if (!ReferenceEquals(oldEl, el))
                {
                    Canvas.SetLeft(el, Canvas.GetLeft(oldEl));
                    Canvas.SetTop(el, Canvas.GetTop(oldEl));
                }

                // Update rotation smoothly
                if (el.Tag is PlayerMarkerTag pmt)
                {
                    double targetRot;
                    if (m.Type == 5 || m.Type == 8 || m.Type == 4)
                    {
                        targetRot = -m.Rotation;
                    }
                    else
                    {
                        double correction = (m.Type == 6 || m.Type == 3) ? 180 : 0;
                        targetRot = m.Rotation + correction;
                    }
                    
                    if (isNew) 
                    {
                        pmt.Rotation = targetRot;
                        ApplyCurrentOverlayScale(el);
                    }
                    else 
                    {
                        AnimateMarkerRotation(el, targetRot);
                    }
                    state.LastCalculatedAngle = targetRot;
                }
            }

            // Update Position
            if (m.Type == 150 || m.Type != 150) // All dynamic types
            {
                var p = WorldToImagePx(m.X, m.Y);
                if (!(el.Tag is PlayerMarkerTag tag && tag.IsDeathPin))
                {
                    double off = (el.Tag is PlayerMarkerTag t2 && t2.Radius > 0) ? t2.Radius : 5.0;
                    if (m.Type == 150) off = 24;
                    else if (m.Type == 8 && el is Grid) off = 64; 

                    double targetLeft = p.X - off;
                    double targetTop = p.Y - off;

                    if (isNew)
                    {
                        Canvas.SetLeft(el, targetLeft);
                        Canvas.SetTop(el, targetTop);
                    }
                    else
                    {
                        AnimateMarker(el, targetLeft, targetTop);
                    }
                }

                // Update Cargo Timer
                if (m.Type == 5 && el.Tag is PlayerMarkerTag pmtTimer && pmtTimer.TimerText != null && pmtTimer.TimerContainer != null)
                {
                    string host = _rust?.Host ?? "unknown";
                    if (_cargoDockStates.TryGetValue(m.Id, out var ds))
                    {
                        if (ds.IsDocked && ds.DockTime.HasValue)
                        {
                            pmtTimer.TimerContainer.Visibility = Visibility.Visible;
                            if (ds.WasAlreadyDocked)
                            {
                                pmtTimer.TimerText.Text = "??:??";
                            }
                            else
                            {
                                int duration = TrackingService.GetLearnedDockingDuration(host);
                                var remain = TimeSpan.FromMinutes(duration) - (DateTime.UtcNow - ds.DockTime.Value);
                                pmtTimer.TimerText.Text = remain.TotalSeconds > 0 ? $"{(int)remain.TotalMinutes}:{remain.Seconds:D2}" : "0:00";
                                pmtTimer.TimerContainer.Visibility = Visibility.Visible;
                            }
                        }
                        else if (ds.FirstSeen.HasValue)
                        {
                            int fullLife = TrackingService.GetLearnedCargoFullLife(host);
                            if (fullLife > 0)
                            {
                                if (ds.SeenAtEdge)
                                {
                                    var remain = TimeSpan.FromMinutes(fullLife) - (DateTime.UtcNow - ds.FirstSeen.Value);
                                    if (remain.TotalSeconds > 0)
                                    {
                                        pmtTimer.TimerText.Text = $"{(int)remain.TotalMinutes}:{remain.Seconds:D2}";
                                        pmtTimer.TimerContainer.Visibility = Visibility.Visible;
                                    }
                                    else pmtTimer.TimerContainer.Visibility = Visibility.Collapsed;
                                }
                                else
                                {
                                    // Connected mid-route — duration known but elapsed unknown
                                    pmtTimer.TimerText.Text = "??:??";
                                    pmtTimer.TimerContainer.Visibility = Visibility.Visible;
                                }
                            }
                            else pmtTimer.TimerContainer.Visibility = Visibility.Collapsed;
                        }
                        else pmtTimer.TimerContainer.Visibility = Visibility.Collapsed;
                    }
                    else pmtTimer.TimerContainer.Visibility = Visibility.Collapsed;
                }
                if (isPlayer) el.Visibility = _showPlayers ? Visibility.Visible : Visibility.Collapsed;

                // Update Crate Timer (Type 150 or Type 9)
                if ((m.Type == 150 || m.Type == 9) && el.Tag is PlayerMarkerTag pmtCrate && pmtCrate.TimerText != null && pmtCrate.TimerContainer != null)
                {
                    if (m.Type == 150)
                    {
                        // Custom virtual markers (Oil Rig Crate) have the time string directly in the label
                        pmtCrate.TimerText.Text = m.Label ?? "??:??";
                        pmtCrate.TimerContainer.Visibility = Visibility.Visible;
                    }
                    else if (!string.IsNullOrEmpty(m.Label))
                    {
                        // API markers: Match MM:SS or M:SS anywhere in label
                        var match = Regex.Match(m.Label, @"(\d{1,2}:\d{2})");
                        if (match.Success)
                        {
                            pmtCrate.TimerText.Text = match.Groups[1].Value;
                            pmtCrate.TimerContainer.Visibility = Visibility.Visible;
                        }
                        else pmtCrate.TimerContainer.Visibility = Visibility.Collapsed;
                    }
                    else pmtCrate.TimerContainer.Visibility = Visibility.Collapsed;
                }
            }
        }

        CenterMiniMapOnPlayer();
        _firstPollDyn = false;
        var gone = _dynEls.Keys.Where(id => !incoming.Contains(id)).ToList();
        foreach (var id in gone)
        {
            if (_dynStates.TryGetValue(id, out var state))
            {
                state.MissingCount++;
                // Keep marker alive and moving for up to 5 seconds (5 poll cycles)
                if (state.MissingCount < 10)
                {
                    if (_dynEls.TryGetValue(id, out var el))
                    {
                        var last = state.History.Last();
                        double nextX = last.X + state.LastVX;
                        double nextY = last.Y + state.LastVY;

                        state.History.Add((nextX, nextY));
                        if (state.History.Count > 5) state.History.RemoveAt(0);

                        var p = WorldToImagePx(nextX, nextY);
                        double off = (el.Tag is PlayerMarkerTag t2 && t2.Radius > 0) ? t2.Radius : 5.0;
                        if (el is Grid && el.Tag is PlayerMarkerTag t3 && t3.Radius == 64) off = 64; // Heli special case

                        if (state.Type == 5)
                        {
                            // Maintain docking state during ghosting with a fake marker at predicted position
                            var ghost = new RustPlusClientReal.DynMarker(id, 5, "CargoShip", nextX, nextY, "Cargo Ship", null, 0);
                            ProcessCargoDocking(ghost);
                        }

                        AnimateMarker(el, p.X - off, p.Y - off);
                        incoming.Add(id); // Prevent cleanup of state (docking timer etc)
                        continue; // Skip removal for now
                    }
                }
            }

            // Real removal after 5 missing polls or if no state
            if (_dynEls.TryGetValue(id, out var oldEl))
            {
                Overlay.Children.Remove(oldEl);
                _dynEls.Remove(id);
                _dynStates.Remove(id);
                if (_trackingEntityId == id) _trackingEntityId = null; 
            }
        }

        // AUTO-FOLLOW TRACKING LOGIC
        if (_trackingEntityId.HasValue && !_isAnimatingMap)
        {
            var target = markers.FirstOrDefault(m => m.Id == _trackingEntityId.Value);
            if (target.Id != 0)
            {
                CenterMapOnWorldAnimated(target.X, target.Y, allowDip: false, fast: true, keepTracking: true);
            }
        }

        CleanupCargoDockStates();
    }

    private void AttachTrackingHandler(FrameworkElement el, uint id)
    {
        el.Cursor = Cursors.Hand;
        el.MouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            
            // Instant lock if already at focus zoom, otherwise animate in
            var target = _lastMarkers?.FirstOrDefault(m => m.Id == id);
            if (target.HasValue && target.Value.Id != 0)
            {
                CenterMapOnWorldAnimated(target.Value.X, target.Value.Y, false, true);
                
                // Set the ID LAST so it isn't cleared by the StopTracking inside CenterMapOnWorldAnimated
                _trackingEntityId = id;
            }
        };
    }
    private FrameworkElement BuildAnimatedAirVehicleMarker(RustPlusClientReal.DynMarker m)
    {
        var grid = new Grid { Width = 128, Height = 128, ClipToBounds = false };
        if (m.Label != null) ToolTipService.SetToolTip(grid, m.Label);

        bool isChinook = m.Type == 4;
        var bodyUri = isChinook ? "pack://application:,,,/icons/animat-Icons/chinook_animate.png" : "pack://application:,,,/icons/animat-Icons/patrol_helicopter.png";
        var bladesUri = "pack://application:,,,/icons/animat-Icons/chinook_map_blades.png";

        var body = MakeIcon(bodyUri, isChinook ? 64 : 48);
        body.HorizontalAlignment = HorizontalAlignment.Center;
        body.VerticalAlignment = VerticalAlignment.Center;
        
        if (!isChinook)
        {
            body.Margin = new Thickness(0, 20, 0, 0); // Nudge heli body so rotor is centered
        }
        grid.Children.Add(body);

        void AddRotor(Thickness margin)
        {
            var blades = MakeIcon(bladesUri, 48);
            blades.HorizontalAlignment = HorizontalAlignment.Center;
            blades.VerticalAlignment = VerticalAlignment.Center;
            blades.Margin = margin;
            blades.RenderTransformOrigin = new Point(0.5, 0.5);
            var rtBlades = new RotateTransform(0);
            blades.RenderTransform = rtBlades;
            grid.Children.Add(blades);

            var anim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(0.5),
                RepeatBehavior = RepeatBehavior.Forever
            };
            rtBlades.BeginAnimation(RotateTransform.AngleProperty, anim);
        }

        if (isChinook)
        {
            AddRotor(new Thickness(0, 0, 0, 42)); // Front rotor
            AddRotor(new Thickness(0, 42, 0, 0)); // Back rotor
        }
        else
        {
            AddRotor(new Thickness(0, 0, 0, 0)); // Main rotor
        }

        // Base rotation origin
        grid.RenderTransformOrigin = new Point(0.5, 0.5);
        grid.RenderTransform = new RotateTransform(0); // Will be updated by scale/rotation logic

        grid.Tag = new PlayerMarkerTag
        {
            Radius = 64,
            ScaleExp = SHOP_SIZE_EXP,
            ScaleBaseMult = SHOP_BASE_MULT,
            ScaleTarget = grid,
            ScaleCenterX = 64,
            ScaleCenterY = 64,
            Rotation = m.Rotation
        };

        return grid;
    }

    private void AnimateMarker(FrameworkElement el, double targetLeft, double targetTop)
    {
        double currentLeft = Canvas.GetLeft(el);
        double currentTop = Canvas.GetTop(el);

        // If it's the first time or too far (teleport), snap instead of animate
        if (double.IsNaN(currentLeft) || double.IsNaN(currentTop))
        {
            Canvas.SetLeft(el, targetLeft);
            Canvas.SetTop(el, targetTop);
            return;
        }

        double dist = Math.Sqrt(Math.Pow(targetLeft - currentLeft, 2) + Math.Pow(targetTop - currentTop, 2));
        if (dist > 200) // Large jump, snap
        {
            el.BeginAnimation(Canvas.LeftProperty, null);
            el.BeginAnimation(Canvas.TopProperty, null);
            Canvas.SetLeft(el, targetLeft);
            Canvas.SetTop(el, targetTop);
            return;
        }

        // 1000ms animation for 1.0s polling interval to achieve flawless constant velocity
        var animX = new DoubleAnimation(targetLeft, TimeSpan.FromMilliseconds(1000));
        var animY = new DoubleAnimation(targetTop, TimeSpan.FromMilliseconds(1000));

        el.BeginAnimation(Canvas.LeftProperty, animX);
        el.BeginAnimation(Canvas.TopProperty, animY);
    }

    private void AnimateMarkerRotation(FrameworkElement el, double targetAngle)
    {
        if (el == null) return;
        ApplyCurrentOverlayScale(el); // Ensure TransformGroup exists
        
        if (el.Tag is PlayerMarkerTag pt && pt.ScaleTarget != null)
        {
            var group = pt.ScaleTarget.RenderTransform as TransformGroup;
            if (group != null && group.Children.Count >= 2 && group.Children[1] is RotateTransform rt)
            {
                // Use the last logical rotation as the start point
                double current = pt.Rotation;
                
                // Calculate shortest path for rotation
                double diff = (targetAngle - current) % 360;
                if (diff > 180) diff -= 360;
                if (diff < -180) diff += 360;
                
                double normalizedTarget = current + diff;

                var anim = new DoubleAnimation(normalizedTarget, TimeSpan.FromMilliseconds(1000));
                rt.BeginAnimation(RotateTransform.AngleProperty, anim);
                
                // Store the logical target so the next poll (and scaling updates) stay in sync
                pt.Rotation = normalizedTarget;
            }
        }
    }
}
