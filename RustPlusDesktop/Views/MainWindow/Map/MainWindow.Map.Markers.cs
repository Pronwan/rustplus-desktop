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

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private void BuildMonumentOverlays()
    {
        if (Overlay == null || _worldSizeS <= 0 || _worldRectPx.Width <= 0) return;

        foreach (var kv in _monEls) Overlay.Children.Remove(kv.Value);
        _monEls.Clear();

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

    private FrameworkElement BuildEventIconHost(FrameworkElement inner, string? tooltip, int size)
    {
        var host = new Grid { Width = size, Height = size, IsHitTestVisible = true };
        if (tooltip != null) ToolTipService.SetToolTip(host, tooltip);

        host.Children.Add(inner);

        host.Tag = new PlayerMarkerTag
        {
            Radius = size * 0.5,
            ScaleExp = SHOP_SIZE_EXP,
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
    }

    private void UpdateEventDock(IReadOnlyList<RustPlusClientReal.DynMarker> markers)
    {
        if (EventDock == null) return;

        var activeEvents = new List<EventDockItem>();

        // 1. Patrol Heli (Type 8)
        var heli = markers.FirstOrDefault(m => m.Type == 8);
        activeEvents.Add(new EventDockItem { Name = "Patrol Heli", Icon = "pack://application:,,,/icons/animat-Icons/patrol_helicopter.png", Active = heli.Id != 0, Id = heli.Id, X = heli.X, Y = heli.Y, Trackable = true });

        // 2. Cargo Ship (Type 5)
        var cargo = markers.FirstOrDefault(m => m.Type == 5);
        activeEvents.Add(new EventDockItem { Name = "Cargo Ship", Icon = "pack://application:,,,/icons/cargo.png", Active = cargo.Id != 0, Id = cargo.Id, X = cargo.X, Y = cargo.Y, Trackable = true });

        // 3. Chinook (Type 4)
        var chinook = markers.FirstOrDefault(m => m.Type == 4);
        activeEvents.Add(new EventDockItem { Name = "Chinook", Icon = "pack://application:,,,/icons/ch47.png", Active = chinook.Id != 0, Id = chinook.Id, X = chinook.X, Y = chinook.Y, Trackable = true });

        // 4. Vendor (Type 6)
        var vendor = markers.FirstOrDefault(m => m.Type == 6);
        activeEvents.Add(new EventDockItem { Name = "Travelling Vendor", Icon = "pack://application:,,,/icons/vendor.png", Active = vendor.Id != 0, Id = vendor.Id, X = vendor.X, Y = vendor.Y, Trackable = true });

        // 5. Deep Sea (Using native _deepSeaActive logic)
        activeEvents.Add(new EventDockItem { Name = "Deep Sea Event", Icon = "pack://application:,,,/icons/ds_event.png", Active = _deepSeaActive, Id = 0, X = 0, Y = 0, Trackable = false });

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
                        var lb = item.Children.OfType<TextBlock>().FirstOrDefault();
                        if (lb != null) {
                            lb.Visibility = Visibility.Visible;
                            lb.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
                        }
                    }
                };
                mainBorder.MouseLeave += (s, e) => {
                    var items = stack.Children.OfType<Grid>().ToList();
                    foreach (var item in items) {
                        var lb = item.Children.OfType<TextBlock>().FirstOrDefault();
                        if (lb != null) {
                            var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
                            anim.Completed += (s2, e2) => lb.Visibility = Visibility.Collapsed;
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
                }
                else
                {
                    itemRow = new Grid { Height = 36, Margin = new Thickness(0, 2, 0, 2), UseLayoutRounding = true, SnapsToDevicePixels = true };
                    itemRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                    itemRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    
                    // Add components once
                    var glow = new System.Windows.Shapes.Ellipse { Width = 32, Height = 32, Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 10 }, HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
                    Grid.SetColumn(glow, 0); itemRow.Children.Add(glow);

                    var img = new Image { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    Grid.SetColumn(img, 0); itemRow.Children.Add(img);

                    var txt = new TextBlock { Foreground = Brushes.White, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 12, 0), Visibility = mainBorder.IsMouseOver ? Visibility.Visible : Visibility.Collapsed, Opacity = mainBorder.IsMouseOver ? 1 : 0 };
                    Grid.SetColumn(txt, 1); itemRow.Children.Add(txt);

                    var dot = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 4, 4) };
                    Grid.SetColumn(dot, 0); itemRow.Children.Add(dot);

                    stack.Children.Add(itemRow);
                }

                // Update states
                itemRow.Cursor = isClickable ? Cursors.Hand : Cursors.Arrow;
                itemRow.Opacity = ev.Active ? 1.0 : 0.35;
                itemRow.Tag = ev; // Store for click handler

                var uiGlow = (System.Windows.Shapes.Ellipse)itemRow.Children[0];
                uiGlow.Fill = new SolidColorBrush(Color.FromArgb(40, 0, 200, 255));
                uiGlow.Visibility = ev.Active ? Visibility.Visible : Visibility.Collapsed;

                var uiImg = (Image)itemRow.Children[1];
                if (uiImg.Source == null || uiImg.Tag as string != ev.Icon) {
                    try { uiImg.Source = new BitmapImage(new Uri(ev.Icon)); uiImg.Tag = ev.Icon; } catch {}
                }

                // Add direct icon glow for active events
                if (ev.Active)
                {
                    uiImg.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Color.FromRgb(0, 200, 255),
                        BlurRadius = 8,
                        ShadowDepth = 0,
                        Opacity = 0.8
                    };
                }
                else
                {
                    uiImg.Effect = null;
                }

                var uiTxt = (TextBlock)itemRow.Children[2];
                uiTxt.Text = ev.Name;
                uiTxt.FontWeight = ev.Active ? FontWeights.SemiBold : FontWeights.Normal;

                var uiDot = (System.Windows.Shapes.Ellipse)itemRow.Children[3];
                uiDot.Fill = ev.Active ? Brushes.Cyan : Brushes.Transparent;

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
        _lastMarkers = markers;
        if (Overlay == null || _worldSizeS <= 0 || _worldRectPx.Width <= 0) return;

        var incoming = new HashSet<uint>();

        foreach (var m in markers)
        {
            if (m.Type == 0 && m.SteamId == 0) continue;

            bool isPlayer = (m.Type == 1);
            if (isPlayer && m.SteamId != 0)
                _lastPlayersBySid[m.SteamId] = (m.X, m.Y, ResolvePlayerName(m));

            if (isPlayer && !_showPlayers) continue;

            bool knownEventType = !isPlayer && sDynIconByType.ContainsKey(m.Type);
            uint key = m.Id != 0 ? m.Id : DynFallbackKey(m.X, m.Y, m.Label ?? m.Kind, m.Type);
            incoming.Add(key);

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

            if (!_dynEls.TryGetValue(key, out var el))
            {
                try
                {
                    if (m.Type == 150)
                    {
                        var img = MakeIcon("pack://application:,,,/icons/crate3.png", 48);
                        var host = BuildEventIconHost(img, m.Label, 48);

                        el = host;
                        _dynEls[key] = el;
                        Overlay.Children.Add(el);
                        Panel.SetZIndex(el, 2000);

                        var pPos = WorldToImagePx(m.X, m.Y);
                        Canvas.SetLeft(el, pPos.X - 24);
                        Canvas.SetTop(el, pPos.Y - 24);

                        ApplyCurrentOverlayScale(el);
                    }
                    else if (m.Type == 8) // Patrol Helicopter
                    {
                        el = BuildAnimatedHeliMarker(m);
                        AttachTrackingHandler(el, m.Id); // Enable tracking
                        _dynEls[key] = el;
                        Overlay.Children.Add(el);
                        Panel.SetZIndex(el, 920);
                        ApplyCurrentOverlayScale(el);
                    }
                    else if (isPlayer)
                    {
                        if (_showProfileMarkers) el = BuildPlayerMarker(m.SteamId, nameNow, online, dead);
                        else el = BuildPlayerDotMarker(m.SteamId, nameNow, online, dead);

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
                            try
                            {
                                var img = MakeIcon(sDynIconByType[m.Type], 64);
                                host = BuildEventIconHost(img, m.Label ?? m.Kind, 64);
                            }
                            catch
                            {
                                host = BuildEventDot($"{m.Kind} ({m.Type})", 14);
                            }
                        }
                        else host = BuildEventDot($"{m.Kind} ({m.Type})", 14);

                        _dynEls[key] = host;
                        Overlay.Children.Add(host);
                        Panel.SetZIndex(host, 920);
                        ApplyCurrentOverlayScale(host);

                        // Enable tracking for specific large events
                        if (m.Type == 5 || m.Type == 4 || m.Type == 6)
                        {
                            AttachTrackingHandler(host, m.Id);
                        }

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
                catch { continue; }
            }
            else
            {
                if (m.Type == 150)
                {
                    if (el is FrameworkElement fe)
                    {
                        fe.ToolTip = m.Label;
                    }

                    var pPos = WorldToImagePx(m.X, m.Y);
                    Canvas.SetLeft(el, pPos.X - 24);
                    Canvas.SetTop(el, pPos.Y - 24);
                    continue;
                }

                if (isPlayer) UpdatePlayerMarker(ref el, key, m.SteamId, nameNow, online, dead);
                else if (el.Tag is not PlayerMarkerTag) el.Tag = m;

                // Update rotation for helicopters
                if (m.Type == 8 && el.Tag is PlayerMarkerTag pmt)
                {
                    pmt.Rotation = m.Rotation;
                    ApplyCurrentOverlayScale(el);
                }
            }

            if (m.Type != 150)
            {
                var p = WorldToImagePx(m.X, m.Y);
                if (!(el.Tag is PlayerMarkerTag t && t.IsDeathPin))
                {
                    double off = (el.Tag is PlayerMarkerTag t2 && t2.Radius > 0) ? t2.Radius : 5.0;
                    
                    // Specific adjustment for the larger Heli container
                    if (m.Type == 8 && el is Grid) off = 64; 

                    Canvas.SetLeft(el, p.X - off);
                    Canvas.SetTop(el, p.Y - off);
                }
                if (isPlayer) el.Visibility = _showPlayers ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        CenterMiniMapOnPlayer();
        var gone = _dynEls.Keys.Where(id => !incoming.Contains(id)).ToList();
        foreach (var id in gone)
        {
            if (_dynEls.TryGetValue(id, out var el))
            {
                Overlay.Children.Remove(el);
                _dynEls.Remove(id);
                if (_trackingEntityId == id) _trackingEntityId = null; // Stop tracking if entity is gone
            }
        }

        // AUTO-FOLLOW TRACKING LOGIC
        if (_trackingEntityId.HasValue && !_isAnimatingMap)
        {
            var target = markers.FirstOrDefault(m => m.Id == _trackingEntityId.Value);
            if (target.Id != 0)
            {
                CenterMapOnWorld(target.X, target.Y);
            }
        }
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
                if (Math.Abs(GetEffectiveZoom() - MAP_FOCUS_ZOOM) < 0.1)
                {
                    CenterMapOnWorld(target.Value.X, target.Value.Y);
                }
                else
                {
                    CenterMapOnWorldAnimated(target.Value.X, target.Value.Y, false, true);
                }
                
                // Set the ID LAST so it isn't cleared by the StopTracking inside CenterMapOnWorldAnimated
                _trackingEntityId = id;
            }
        };
    }
    private FrameworkElement BuildAnimatedHeliMarker(RustPlusClientReal.DynMarker m)
    {
        var grid = new Grid { Width = 128, Height = 128, ClipToBounds = false };
        if (m.Label != null) ToolTipService.SetToolTip(grid, m.Label);

        var bodyUri = "pack://application:,,,/icons/animat-Icons/patrol_helicopter.png";
        var bladesUri = "pack://application:,,,/icons/animat-Icons/chinook_map_blades.png";

        var body = MakeIcon(bodyUri, 48);
        body.HorizontalAlignment = HorizontalAlignment.Center;
        body.VerticalAlignment = VerticalAlignment.Center;
        body.Margin = new Thickness(0, 20, 0, 0); // Nudge body UP to bring rotor to center
        grid.Children.Add(body);

        var blades = MakeIcon(bladesUri, 48);
        blades.HorizontalAlignment = HorizontalAlignment.Center;
        blades.VerticalAlignment = VerticalAlignment.Center;
        // blades.Margin = new Thickness(0, 0, 0, 0); // Center blades on grid center
        blades.RenderTransformOrigin = new Point(0.5, 0.5);
        var rtBlades = new RotateTransform(0);
        blades.RenderTransform = rtBlades;
        grid.Children.Add(blades);

        // Clockwise animation for blades (Lower seconds = faster spin)
        var anim = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromSeconds(0.5),
            RepeatBehavior = RepeatBehavior.Forever
        };
        rtBlades.BeginAnimation(RotateTransform.AngleProperty, anim);

        // Apply base rotation to the whole grid
        grid.RenderTransformOrigin = new Point(0.5, 0.5);
        grid.RenderTransform = new RotateTransform(m.Rotation);

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
}
