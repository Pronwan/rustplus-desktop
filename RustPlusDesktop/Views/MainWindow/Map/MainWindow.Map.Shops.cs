using RustPlusDesk.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private bool _deepSeaActive = false;

    private void CheckDeepSeaEvent(IEnumerable<RustPlusClientReal.ShopMarker> shops)
    {
        var deepSeaShop = shops.FirstOrDefault(s =>
            s.Label != null && s.Label.Contains("Casino Bar Shopkeeper"));

        if (deepSeaShop != null)
        {
            if (!_deepSeaActive)
            {
                _deepSeaActive = true;
                string dir = GetDeepSeaDirection(deepSeaShop.X, deepSeaShop.Y);
                if (_announceSpawns && TrackingService.AnnounceDeepSea)
                    _ = SendTeamChatSafeAsync($"Deep Sea Event started! (Direction: {dir})");
                AppendLog($"[DEEPSEA] Event detected at {deepSeaShop.X:F0},{deepSeaShop.Y:F0} ({dir})");
            }
        }
        else if (_deepSeaActive)
        {
            _deepSeaActive = false;
            AppendLog("[DEEPSEA] Event ended (shop disappeared).");
        }
    }

    private string GetDeepSeaDirection(double x, double y)
    {
        double size = _worldSizeS > 0 ? _worldSizeS : 4500;
        double margin = 200;

        if (y < margin) return "North";
        if (y > size - margin) return "South";
        if (x < margin) return "West";
        if (x > size - margin) return "East";

        return TryGetGridRef(x, y, out var g) ? g : "Unknown";
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

    private void ShopElement_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            List<RustPlusClientReal.ShopMarker> cluster = null;
            if (fe.Tag is RustPlusClientReal.ShopMarker s) cluster = new List<RustPlusClientReal.ShopMarker> { s };
            else if (fe.Tag is IEnumerable<RustPlusClientReal.ShopMarker> c) cluster = c.ToList();

            if (cluster != null && cluster.Count > 0)
            {
                double avgX = cluster.Average(x => x.X);
                double avgY = cluster.Average(x => x.Y);
                CenterMapOnWorldAnimated(avgX, avgY, false, true);
                ShowShopDetails(cluster, fe);
                e.Handled = true;
            }
        }
    }

    private async void ChkShops_Checked(object sender, RoutedEventArgs e)
    {
        if (ChkShops.IsChecked == true && _worldSizeS > 0 && _worldRectPx.Width > 0)
        {
            _shopTimer?.Stop();
            _shopTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _shopTimer.Tick += async (_, __) => await PollShopsOnceAsync();
            _shopTimer.Start();

            await PollShopsOnceAsync();
            await Dispatcher.InvokeAsync(() =>
            {
                RefreshShopIconScales();
            }, DispatcherPriority.Loaded);
        }
        else
        {
            _shopTimer?.Stop();
            _shopTimer = null;

            foreach (var kv in _shopEls) Overlay.Children.Remove(kv.Value);
            _shopEls.Clear();
        }
    }

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

            UpdateShopLifetimes(shops);

            if (_alertsNeedRebaseline)
            {
                RebaselineAllAlertRulesFromCurrentShops(shops);

                _initialShopSnapshotTimeUtc = DateTime.UtcNow;
                _knownShopIds.Clear();
                foreach (var s in shops)
                    _knownShopIds.Add(s.Id);

                _alertsNeedRebaseline = false;
            }

            await DetectNewShopsAsync(shops);
            await CheckAlerts(shops);

            if (ShopSearchContent.Visibility == Visibility.Visible)
                RefreshShopSearchResults();
        }
        catch (Exception)
        {
        }
    }

    private async Task DetectNewShopsAsync(IReadOnlyList<RustPlusClientReal.ShopMarker> shops)
    {
        if (_initialShopSnapshotTimeUtc == DateTime.MinValue)
        {
            _initialShopSnapshotTimeUtc = DateTime.UtcNow;
            foreach (var s in shops)
                _knownShopIds.Add(s.Id);
            return;
        }

        if (!_announceSpawns || !TrackingService.AnnounceNewShops)
            return;

        foreach (var s in shops)
        {
            if (_knownShopIds.Contains(s.Id))
                continue;

            _knownShopIds.Add(s.Id);

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

    private void UpdateShopLifetimes(IReadOnlyList<RustPlusClientReal.ShopMarker> shops)
    {
        foreach (var kv in _shopLifetimes)
        {
            kv.Value.LastSeenUtc = null;
        }

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

        if (_announceSpawns && TrackingService.AnnounceSuspiciousShops)
        {
            foreach (var kv in _shopLifetimes.ToList())
            {
                var life = kv.Value;

                if (life.LastSeenUtc == null && !life.AnnouncedSuspicious)
                {
                    string grid = life.LastSnapshot != null ? GetGridLabel(life.LastSnapshot) : "unknown";
                    AppendLog($"[dbg] Shop {life.LastSnapshot?.Label ?? "(no label)"} [{grid}] offline after {(DateTime.UtcNow - life.FirstSeenUtc).TotalSeconds:0}s");
                    var lived = DateTime.UtcNow - life.FirstSeenUtc;
                    if (lived.TotalSeconds <= 60.0)
                    {
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

                            _ = SendTeamChatSafeAsync(msg);
                        }

                        life.AnnouncedSuspicious = true;
                    }
                }
            }
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

        foreach (var el in _shopEls.Values) Overlay.Children.Remove(el);
        _shopEls.Clear();
    }

    private void UpdateShopsUI(IReadOnlyList<RustPlusClientReal.ShopMarker> shops)
    {
        if (Overlay == null || _worldSizeS <= 0 || _worldRectPx.Width <= 0) return;

        // 1) Cluster shops by proximity (e.g., within 50 world units to cover typical vending bases)
        var clusters = new List<List<RustPlusClientReal.ShopMarker>>();
        const double CLUSTER_DIST = 50.0; 

        foreach (var s in shops)
        {
            List<RustPlusClientReal.ShopMarker>? target = null;
            foreach (var c in clusters)
            {
                // Compare against cluster average position
                double avgX = c.Average(x => x.X);
                double avgY = c.Average(x => x.Y);
                double dx = avgX - s.X;
                double dy = avgY - s.Y;
                if (Math.Sqrt(dx * dx + dy * dy) < CLUSTER_DIST)
                {
                    target = c;
                    break;
                }
            }

            if (target != null) target.Add(s);
            else clusters.Add(new List<RustPlusClientReal.ShopMarker> { s });
        }

        var incoming = new HashSet<uint>();
        foreach (var cluster in clusters)
        {
            // Use the ID of the first shop as the cluster key
            var primary = cluster[0];
            uint clusterId = primary.Id;
            incoming.Add(clusterId);

            // Average position for the cluster icon
            double avgX = cluster.Average(s => s.X);
            double avgY = cluster.Average(s => s.Y);
            var p = WorldToImagePx(avgX, avgY);

            if (!_shopEls.TryGetValue(clusterId, out var el))
            {
                var grid = new Grid { Tag = cluster, Cursor = Cursors.Hand };
                
                bool allEmpty = cluster.All(s => IsShopEmpty(s));
                string iconUri = allEmpty ? "pack://application:,,,/icons/vending_orange.png" : "pack://application:,,,/icons/vending.png";
                var circleColor = allEmpty ? Color.FromRgb(255, 140, 0) : Color.FromRgb(140, 186, 48);

              

                var icon = MakeIcon(iconUri, cluster.Count > 1 ? 20 : 20); // Slightly larger for single
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                icon.VerticalAlignment = VerticalAlignment.Center;
                icon.Opacity = 1.0; // Full opacity for all icons
                grid.Children.Add(icon);

                var txt = new TextBlock
                {
                    Text = cluster.Count > 1 ? cluster.Count.ToString() : "",
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 1, ShadowDepth = 1, Opacity = 1, Color = Colors.Black }
                };
                grid.Children.Add(txt);

                grid.MouseEnter += ShopElement_MouseEnter;
                grid.MouseLeave += ShopElement_MouseLeave;
                grid.MouseLeftButtonUp += ShopElement_Click;

                _shopEls[clusterId] = grid;
                Overlay.Children.Add(grid);
                Panel.SetZIndex(grid, 850);
                el = grid;
                _shopIconSet.Add(grid);
                ApplyCurrentOverlayScale(el);
            }
            else
            {
                el.Tag = cluster;

                if (el is Grid g)
                {
                    bool allEmpty = cluster.All(s => IsShopEmpty(s));
                    string iconUri = allEmpty ? "pack://application:,,,/icons/vending_orange.png" : "pack://application:,,,/icons/vending.png";
                    var circleColor = allEmpty ? Color.FromRgb(255, 140, 0) : Color.FromRgb(140, 186, 48);

                    var circle = g.Children.OfType<Border>().FirstOrDefault();
                    if (circle != null)
                    {
                        circle.Background = cluster.Count > 1 
                            ? new SolidColorBrush(circleColor) 
                            : Brushes.Transparent;
                        circle.BorderBrush = cluster.Count > 1 ? Brushes.Black : Brushes.Transparent;
                        circle.Effect = cluster.Count > 1 ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.5 } : null;
                    }

                    var icon = g.Children.OfType<Image>().FirstOrDefault();
                    if (icon != null)
                    {
                        icon.Opacity = 1.0;
                        icon.Width = icon.Height = 20;
                        // Update source if needed
                        if (icon.Source is System.Windows.Media.Imaging.BitmapImage bi && !bi.UriSource.ToString().Contains(allEmpty ? "orange" : "vending.png"))
                        {
                            icon.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconUri));
                        }
                    }

                    var tb = g.Children.OfType<TextBlock>().FirstOrDefault();
                    if (tb != null) tb.Text = cluster.Count > 1 ? cluster.Count.ToString() : "";
                }
                if (el is FrameworkElement fe2) _shopIconSet.Add(fe2);
            }

            Canvas.SetLeft(el, p.X - 12); // Centered (24/2)
            Canvas.SetTop(el, p.Y - 12);
        }

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

        CheckDeepSeaEvent(shops);
        PrefetchShopIcons(shops);
    }

    private DispatcherTimer _shopDetailHideTimer;
    private DispatcherTimer _shopDetailShowTimer;

    private void ShopElement_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is List<RustPlusClientReal.ShopMarker> cluster)
        {
            _shopDetailHideTimer?.Stop();
            
            // Delay showing the list so it doesn't flicker when moving mouse
            _shopDetailShowTimer?.Stop();
            _shopDetailShowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _shopDetailShowTimer.Tick += (s, ev) => {
                _shopDetailShowTimer.Stop();
                ShowShopDetails(cluster, fe);
            };
            _shopDetailShowTimer.Start();
        }
    }

    private void ShopElement_MouseLeave(object sender, MouseEventArgs e)
    {
        _shopDetailShowTimer?.Stop();
        StartShopDetailHideTimer();
    }

    private void StartShopDetailHideTimer()
    {
        _shopDetailHideTimer?.Stop();
        _shopDetailHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _shopDetailHideTimer.Tick += (s, e) => {
            if (ShopDetailsPopup != null && !ShopDetailsPopup.IsMouseOver)
            {
                ShopDetailsPopup.Visibility = Visibility.Collapsed;
            }
            _shopDetailHideTimer.Stop();
        };
        _shopDetailHideTimer.Start();
    }

    private void SetupShopPopupHover()
    {
        if (ShopDetailsPopup == null) return;
        ShopDetailsPopup.MouseEnter += (s, e) => _shopDetailHideTimer?.Stop();
        ShopDetailsPopup.MouseLeave += (s, e) => StartShopDetailHideTimer();
    }

    private void ShowShopDetails(List<RustPlusClientReal.ShopMarker> cluster, FrameworkElement anchor = null)
    {
        if (ShopDetailsPopup == null || ShopDetailsContent == null) return;
        
        // One-time setup for popup hover
        if (ShopDetailsPopup.Tag == null) {
            SetupShopPopupHover();
            ShopDetailsPopup.Tag = "setup";
        }

        ShopDetailsContent.Children.Clear();

        foreach (var s in cluster)
        {
            var shopContainer = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 16) };
            
            var title = string.IsNullOrWhiteSpace(s.Label) || LooksLikeOrdersLabel(s.Label) ? "Shop" : s.Label;
            var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleTxt = new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(titleTxt, 0);
            header.Children.Add(titleTxt);

            var gridTxt = new TextBlock
            {
                Text = "[" + GetGridLabel(s) + "]",
                Foreground = new SolidColorBrush(Color.FromRgb(140, 186, 48)), 
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(gridTxt, 1);
            header.Children.Add(gridTxt);

            shopContainer.Children.Add(header);

            if (s.Orders is { Count: > 0 })
            {
                foreach (var o in s.Orders)
                {
                    shopContainer.Children.Add(BuildOfferRowUI(o));
                }
            }
            else
            {
                shopContainer.Children.Add(new TextBlock { Text = "No offers available", Foreground = Brushes.Gray, FontSize = 12, FontStyle = FontStyles.Italic, Margin = new Thickness(4) });
            }

            ShopDetailsContent.Children.Add(shopContainer);
            
            if (s != cluster.Last())
            {
                ShopDetailsContent.Children.Add(new Border 
                { 
                    Height = 1, 
                    Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), 
                    Margin = new Thickness(0, 4, 0, 16) 
                });
            }
        }

        ShopDetailsPopup.Visibility = Visibility.Visible;
        ShopDetailsScroll.ScrollToHome();

        if (anchor != null)
        {
            // Update layout so we have ActualWidth/Height
            ShopDetailsPopup.UpdateLayout();

            var pos = anchor.TranslatePoint(new Point(30, -20), WebViewHost);
            double left = Math.Min(pos.X, WebViewHost.ActualWidth - ShopDetailsPopup.ActualWidth - 20);
            double top = Math.Min(pos.Y, WebViewHost.ActualHeight - ShopDetailsPopup.ActualHeight - 20);
            
            ShopDetailsPopup.Margin = new Thickness(Math.Max(10, left), Math.Max(10, top), 0, 0);
        }
    }

    private void BtnCloseShopDetails_Click(object sender, RoutedEventArgs e)
    {
        if (ShopDetailsPopup != null) ShopDetailsPopup.Visibility = Visibility.Collapsed;
    }

    private bool IsShopEmpty(RustPlusClientReal.ShopMarker s)
    {
        if (s.Orders == null || s.Orders.Count == 0) return true;
        return s.Orders.All(o => o.Quantity <= 0);
    }
}
