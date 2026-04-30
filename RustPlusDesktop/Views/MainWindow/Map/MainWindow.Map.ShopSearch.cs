using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RustPlusDesk.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    // ── Fields ──────────────────────────────────────────────────────────────
    private Window?   _shopSearchWin;
    private WebView2? _shopSearchWebView;

    // Proxy controls – never added to visual tree, kept for compat with
    // AddAlertFromCurrentSearch / RefreshAlertListUI in MainWindow.xaml.cs
    private TextBox?  _searchTb;
    private CheckBox? _chkSell;
    private CheckBox? _chkBuy;
    private ListBox?  _alertList;    // stays null → alerts pushed via WebView2

    // ── WPF card builder — still used by PathFinder & map hover popup ─────────
    private FrameworkElement BuildShopSearchCard(
        RustPlusClientReal.ShopMarker s,
        IEnumerable<RustPlusClientReal.ShopOrder> offers,
        bool compact)
    {
        var card = new Border
        {
            Background       = SearchCardBg,
            BorderBrush      = SearchCardBrd,
            BorderThickness  = new Thickness(1),
            CornerRadius     = new CornerRadius(8),
            Padding          = new Thickness(8),
            Margin           = new Thickness(0, 4, 0, 4)
        };

        var root = new StackPanel { Orientation = Orientation.Vertical };
        card.Child = root;

        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        head.Children.Add(new TextBlock
        {
            Text       = CleanLabel(s.Label) ?? "Shop",
            Foreground = SearchText,
            FontWeight = FontWeights.SemiBold,
            FontSize   = compact ? 12 : 14
        });
        head.Children.Add(new TextBlock
        {
            Text                = $"   [{GetGridLabel(s)}]",
            Foreground          = SearchSubtle,
            VerticalAlignment   = VerticalAlignment.Center,
            FontSize            = 11
        });
        root.Children.Add(head);

        foreach (var o in offers)
            root.Children.Add(BuildOfferRowSearchUI(o, compact));

        card.MouseLeftButtonUp += (_, __) => { CenterMapOnWorldAnimated(s.X, s.Y, false, true); };
        return card;
    }

    private FrameworkElement BuildOfferRowSearchUI(RustPlusClientReal.ShopOrder o, bool compact)
    {
        bool outOfStock = o.Stock <= 0;
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2), Opacity = outOfStock ? 0.6 : 1.0 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var li = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 0, 4, 0) };
        BindIcon(li, o.ItemShortName, o.ItemId);
        Grid.SetColumn(li, 0);
        g.Children.Add(li);

        var name = ResolveItemName(o.ItemId, o.ItemShortName);
        if (compact && name.Length > 14) name = name[..14] + "…";
        var lt = new TextBlock { Text = name, Foreground = SearchText, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(lt, 1);
        g.Children.Add(lt);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(new TextBlock { Text = "→  ", Foreground = SearchSubtle });
        var ci = new Image { Width = 14, Height = 14, Margin = new Thickness(0, 0, 3, 0) };
        BindIcon(ci, o.CurrencyShortName, o.CurrencyItemId);
        right.Children.Add(ci);
        right.Children.Add(new TextBlock
        {
            Text       = $"{o.CurrencyAmount} {ResolveItemName(o.CurrencyItemId, o.CurrencyShortName)}",
            Foreground = SearchText,
            FontWeight = FontWeights.SemiBold
        });
        Grid.SetColumn(right, 2);
        g.Children.Add(right);
        return g;
    }


    private static bool LooksLikeOrdersLabel(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.ToLowerInvariant();
        return t.Contains("item#") || t.Contains("curr#") ||
               t.Contains("->")    || t.Contains(";")      || t.Contains("stock");
    }

    private static string? CleanLabel(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().Replace('\r', ' ').Replace('\n', ' ');
        if (LooksLikeOrdersLabel(s)) return null;
        return s.Length > 48 ? s[..48] + "…" : s;
    }

    // ── Button entry-point ───────────────────────────────────────────────────
    private void BtnShopSearch_Click(object sender, RoutedEventArgs e)
    {
        ToggleShopSearch();
        if (ShopSearchContent.Visibility == Visibility.Visible)
        {
            _ = PushShopsToWebViewAsync();
            _ = PushAlertsToWebViewAsync();
        }
    }

    private void ToggleShopSearch()
    {
        if (ShopSearchContent.Visibility == Visibility.Collapsed)
        {
            if (_shopSearchWebView == null) InitEmbeddedShopSearch();
            
            ShopSearchContent.Visibility = Visibility.Visible;
            ShopSearchContent.Opacity = 0;
            ShopSearchScale.ScaleY = 0.85;
            ShopSearchTranslate.Y = 40;

            var sb = new System.Windows.Media.Animation.Storyboard();
            var fade = new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(250)) { EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
            var scale = new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(300)) { EasingFunction = new System.Windows.Media.Animation.BackEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Amplitude = 0.2 } };
            var slide = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(250)) { EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };

            System.Windows.Media.Animation.Storyboard.SetTarget(fade, ShopSearchContent);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            System.Windows.Media.Animation.Storyboard.SetTarget(scale, ShopSearchScale);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(scale, new PropertyPath("ScaleY"));
            System.Windows.Media.Animation.Storyboard.SetTarget(slide, ShopSearchTranslate);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(slide, new PropertyPath("Y"));

            sb.Children.Add(fade);
            sb.Children.Add(scale);
            sb.Children.Add(slide);
            sb.Begin();
        }
        else
        {
            var sb = new System.Windows.Media.Animation.Storyboard();
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
            var scale = new System.Windows.Media.Animation.DoubleAnimation(0.85, TimeSpan.FromMilliseconds(200));
            var slide = new System.Windows.Media.Animation.DoubleAnimation(40, TimeSpan.FromMilliseconds(200));

            System.Windows.Media.Animation.Storyboard.SetTarget(fade, ShopSearchContent);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            System.Windows.Media.Animation.Storyboard.SetTarget(scale, ShopSearchScale);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(scale, new PropertyPath("ScaleY"));
            System.Windows.Media.Animation.Storyboard.SetTarget(slide, ShopSearchTranslate);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(slide, new PropertyPath("Y"));

            sb.Children.Add(fade);
            sb.Children.Add(scale);
            sb.Children.Add(slide);
            sb.Completed += (s, e) => ShopSearchContent.Visibility = Visibility.Collapsed;
            sb.Begin();
        }
    }

    private void BtnCloseShopSearch_Click(object sender, RoutedEventArgs e)
    {
        ToggleShopSearch();
    }

    private async void InitEmbeddedShopSearch()
    {
        if (_shopSearchWebView != null) return;

        _searchTb  = new TextBox();
        _chkSell   = new CheckBox { IsChecked = true };
        _chkBuy    = new CheckBox { IsChecked = true };

        var wv = EmbeddedShopSearch;
        _shopSearchWebView = wv;

        try
        {
            var dataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RustPlusDesk", "WebView2_ShopSearch");
            Directory.CreateDirectory(dataPath);

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: dataPath);
            await wv.EnsureCoreWebView2Async(env);

            wv.CoreWebView2.Settings.AreDevToolsEnabled = true;
            wv.CoreWebView2.WebMessageReceived += ShopSearch_WebMessageReceived;

            string html = BuildShopSearchHtml();
            if (!string.IsNullOrWhiteSpace(html))
            {
                var iconPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RustPlusDesk", "icons");
                if (Directory.Exists(iconPath))
                    wv.CoreWebView2.SetVirtualHostNameToFolderMapping("rusticons.local", iconPath, CoreWebView2HostResourceAccessKind.Allow);

                wv.CoreWebView2.NavigateToString(html);
            }
            else
                wv.NavigateToString(FallbackHtml());

            wv.NavigationCompleted += async (_, args) =>
            {
                if (!args.IsSuccess) return;
                await Task.Delay(80);
                await InjectItemsAsync();
                await PushShopsToWebViewAsync();
                await PushAlertsToWebViewAsync();
            };
        }
        catch (Exception ex)
        {
            AppendLog($"[ShopSearch] WebView2 Error: {ex.Message}");
        }

        if (_alertRules.All(r => !r.IsSaved))
            LoadPersistentAlerts();
    }

    // ── Refresh (called by PollShopsOnceAsync) ───────────────────────────────
    private void RefreshShopSearchResults()
    {
        if (_shopSearchWebView == null) return;
        _ = PushShopsToWebViewAsync();
    }

    // ── Push data to WebView2 ────────────────────────────────────────────────
    private async Task InjectItemsAsync()
    {
        if (_shopSearchWebView == null) return;
        try
        {
            EnsureNewItemDbLoaded();
            var items = sItemsById.Values
                .Where(ii => !string.IsNullOrWhiteSpace(ii.Display))
                .Select(ii =>
                {
                    string shortName = ii.ShortName ?? "";
                    // Try to find cached icon using the same logic as ResolveItemIcon
                    string clashUrl = !string.IsNullOrWhiteSpace(shortName) 
                        ? $"https://wiki.rustclash.com/img/items40/{shortName}.png" : "";
                    string helpUrl = ii.IconUrl ?? "";

                    string finalUrl = clashUrl;
                    if (!string.IsNullOrWhiteSpace(clashUrl))
                    {
                        var cp = GetIconCachePath(clashUrl);
                        if (File.Exists(cp)) finalUrl = $"http://rusticons.local/{Path.GetFileName(cp)}";
                    }
                    else if (!string.IsNullOrWhiteSpace(helpUrl))
                    {
                        var cp = GetIconCachePath(helpUrl);
                        if (File.Exists(cp)) finalUrl = $"http://rusticons.local/{Path.GetFileName(cp)}";
                        else finalUrl = helpUrl;
                    }

                    return new
                    {
                        id      = ii.Id,
                        sn      = shortName,
                        display = ii.Display,
                        icon    = finalUrl
                    };
                })
                .OrderBy(x => x.display)
                .ToList();

            var json = JsonSerializer.Serialize(items);
            await _shopSearchWebView.ExecuteScriptAsync($"window.loadItems({json})");
        }
        catch { }
    }

    private async Task PushShopsToWebViewAsync()
    {
        if (_shopSearchWebView == null) return;
        try
        {
            var payload = _lastShops
                .Where(s => s.Orders is { Count: > 0 })
                .Select(s => new
                {
                    id     = s.Id,
                    label  = CleanLabel(s.Label) ?? "Shop",
                    grid   = GetGridLabel(s),
                    x      = s.X,
                    y      = s.Y,
                    orders = s.Orders!.Select(o => new
                    {
                        iName  = ResolveItemName(o.ItemId, o.ItemShortName),
                        iIcon  = GetItemIconUrl(o.ItemId, o.ItemShortName),
                        qty    = o.Quantity,
                        stock  = o.Stock,
                        cName  = ResolveItemName(o.CurrencyItemId, o.CurrencyShortName),
                        cIcon  = GetItemIconUrl(o.CurrencyItemId, o.CurrencyShortName),
                        cAmt   = o.CurrencyAmount,
                        bp     = o.IsBlueprint
                    }).ToList()
                }).ToList();

            var json = JsonSerializer.Serialize(payload);
            await _shopSearchWebView.ExecuteScriptAsync($"window.updateShops({json})");
        }
        catch { }
    }

    public async Task PushAlertsToWebViewAsync()
    {
        if (_shopSearchWebView == null) return;
        try
        {
            AppendLog($"[ShopSearch] Pushing {_alertRules.Count} alerts to WebView");
            var alerts = _alertRules.Select(r => new
            {
                id    = r.Id.ToString(),
                query = r.QueryText,
                sell  = r.MatchSellSide,
                buy   = r.MatchBuySide,
                chat  = r.NotifyChat,
                sound = r.NotifySound,
                saved = r.IsSaved
            }).ToList();

            var json = JsonSerializer.Serialize(alerts);
            await _shopSearchWebView.ExecuteScriptAsync($"window.updateAlerts({json})");
        }
        catch { }
    }

    // ── Web message handler ──────────────────────────────────────────────────
    private void ShopSearch_WebMessageReceived(object? sender,
        CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            var doc    = JsonDocument.Parse(json);
            var root   = doc.RootElement;
            var action = root.GetProperty("action").GetString();

            AppendLog($"[ShopSearch] Message received: {action}");

            Dispatcher.Invoke(() =>
            {
                switch (action)
                {
                    case "filterChanged":
                        if (_searchTb != null)
                            _searchTb.Text = root.TryGetProperty("query", out var q)
                                ? q.GetString() ?? "" : "";
                        if (_chkSell != null)
                            _chkSell.IsChecked = root.TryGetProperty("sell", out var sv)
                                && sv.GetBoolean();
                        if (_chkBuy != null)
                            _chkBuy.IsChecked = root.TryGetProperty("buy", out var bv)
                                && bv.GetBoolean();
                        _ = PushShopsToWebViewAsync();
                        break;

                    case "centerMap":
                        double cx = root.GetProperty("x").GetDouble();
                        double cy = root.GetProperty("y").GetDouble();
                        AppendLog($"[ShopSearch] Centering map on {cx}, {cy}");
                        StopTracking();
                        CenterMapOnWorldAnimated(cx, cy);
                        break;

                    case "addAlert":
                        if (root.TryGetProperty("query", out var aq))
                        {
                            string qText = aq.GetString() ?? "";
                            bool aSell = root.TryGetProperty("sell", out var asv) && asv.GetBoolean();
                            bool aBuy  = root.TryGetProperty("buy", out var abv) && abv.GetBoolean();
                            
                            AppendLog($"[ShopSearch] Adding alert for: {qText} (S={aSell}, B={aBuy})");
                            
                            // If we have a query from JS, use it instead of reading from _searchTb
                            var rule = new ShopAlertRule
                            {
                                QueryText = qText,
                                MatchSellSide = aSell,
                                MatchBuySide = aBuy,
                                NotifyChat = true,
                                NotifySound = true
                            };
                            
                            // Baseline logic (matches logic in MainWindow.xaml.cs)
                            foreach (var shop in _lastShops)
                            {
                                if (shop.Orders == null) continue;
                                foreach (var o in shop.Orders)
                                {
                                    rule.Baseline.Add(new AlertSeenOrder
                                    {
                                        ShopId = shop.Id,
                                        ItemShort = o.ItemShortName ?? "",
                                        CurrencyShort = o.CurrencyShortName ?? "",
                                        Quantity = o.Quantity,
                                        CurrencyAmount = o.CurrencyAmount,
                                        Stock = o.Stock
                                    });
                                }
                            }

                            _alertRules.Add(rule);
                            SavePersistentAlerts();
                            RefreshAlertListUI();
                        }
                        else
                        {
                            AddAlertFromCurrentSearch();
                        }
                        break;

                    case "removeAlert":
                        var rid = Guid.Parse(root.GetProperty("id").GetString()!);
                        var rr  = _alertRules.FirstOrDefault(r => r.Id == rid);
                        if (rr != null) { _alertRules.Remove(rr); SavePersistentAlerts(); }
                        _ = PushAlertsToWebViewAsync();
                        break;

                    case "saveAlert":
                        var sid  = Guid.Parse(root.GetProperty("id").GetString()!);
                        var sr   = _alertRules.FirstOrDefault(r => r.Id == sid);
                        if (sr != null)
                        {
                            sr.IsSaved = root.GetProperty("saved").GetBoolean();
                            SavePersistentAlerts();
                        }
                        _ = PushAlertsToWebViewAsync();
                        break;

                    case "alertNotify":
                        var nid = Guid.Parse(root.GetProperty("id").GetString()!);
                        var nr  = _alertRules.FirstOrDefault(r => r.Id == nid);
                        if (nr != null)
                        {
                            nr.NotifyChat  = root.GetProperty("chat").GetBoolean();
                            nr.NotifySound = root.GetProperty("sound").GetBoolean();
                            SavePersistentAlerts();
                        }
                        break;

                    case "openAnalysis":
                        AppendLog("[ShopSearch] Opening Analysis window");
                        OpenAnalysisWindow();
                        break;

                    case "openPathFinder":
                        AppendLog("[ShopSearch] Opening PathFinder window");
                        OpenPathFinderWindow();
                        break;

                    case "notifyNew":
                        _notifyNewShopsToChat = root.GetProperty("on").GetBoolean();
                        break;

                    case "notifySuspicious":
                        _notifySuspiciousShops = root.GetProperty("on").GetBoolean();
                        break;
                }
            });
        }
        catch (Exception ex)
        {
            AppendLog($"[ShopSearch] Error in message handler: {ex.Message}");
        }
    }



    // ── Helpers ──────────────────────────────────────────────────────────────
    private static string GetItemIconUrl(int itemId, string? shortName)
    {
        EnsureNewItemDbLoaded();
        
        // 1) Logic matches ResolveItemIcon: RustClash is primary
        if (string.IsNullOrWhiteSpace(shortName) && itemId != 0 && sItemsById.TryGetValue(itemId, out var ii0))
            shortName = ii0.ShortName;

        if (!string.IsNullOrWhiteSpace(shortName))
        {
            var clashUrl = $"https://wiki.rustclash.com/img/items40/{shortName}.png";
            var cp = GetIconCachePath(clashUrl);
            if (File.Exists(cp)) return $"http://rusticons.local/{Path.GetFileName(cp)}";
        }

        // 2) Fallback to RustHelp (ii.IconUrl)
        string? helpUrl = null;
        if (itemId != 0 && sItemsById.TryGetValue(itemId, out var ii1)) helpUrl = ii1.IconUrl;
        else if (!string.IsNullOrWhiteSpace(shortName) && sItemsByShort.TryGetValue(shortName!, out var ii2)) helpUrl = ii2.IconUrl;

        if (!string.IsNullOrWhiteSpace(helpUrl))
        {
            var cp = GetIconCachePath(helpUrl);
            if (File.Exists(cp)) return $"http://rusticons.local/{Path.GetFileName(cp)}";
            return helpUrl;
        }

        // 3) Default to Clash URL if nothing cached
        return !string.IsNullOrWhiteSpace(shortName) 
            ? $"https://wiki.rustclash.com/img/items40/{shortName}.png" 
            : "";
    }

    // ── HTML builder ──────────────────────────────────────────────────────────
    private static string BuildShopSearchHtml()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/ShopSearch.html");
            var streamInfo = Application.GetResourceStream(uri);
            if (streamInfo != null)
            {
                using var reader = new StreamReader(streamInfo.Stream);
                return reader.ReadToEnd();
            }
        }
        catch { }

        // Fallback for local development if resource loading fails
        string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShopSearch.html");
        if (File.Exists(localPath)) return File.ReadAllText(localPath);

        return FallbackHtml();
    }

    private static string FallbackHtml() =>
        "<html><body style='background:#16181c;color:#fff;padding:20px'>ShopSearch.html not found.</body></html>";
}
