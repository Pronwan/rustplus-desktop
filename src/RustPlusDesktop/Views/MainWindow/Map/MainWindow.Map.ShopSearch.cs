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
    private Window?   _shopSearchWin { get; set; }

    // Proxy controls kept for backward compatibility if referenced elsewhere
    private TextBox?  _searchTb { get; set; }
    private CheckBox? _chkSell { get; set; }
    private CheckBox? _chkBuy { get; set; }
    private ListBox?  _alertList { get; set; }

    // ── WPF card builder — still used by PathFinder, map hover popup & ShopSearchControl ─────────
    internal FrameworkElement BuildShopSearchCard(
        RustPlusClientReal.ShopMarker s,
        IEnumerable<RustPlusClientReal.ShopOrder> offers,
        bool compact)
    {
        // Shared surface and stroke tokens: the card reads as part of the panel, not a separate theme.
        var card = new Border
        {
            BorderThickness  = new Thickness(1),
            Padding          = new Thickness(10),
            Margin           = new Thickness(0, 0, 0, 8),
            Cursor           = Cursors.Hand
        };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
        card.SetResourceReference(Border.CornerRadiusProperty, "RadiusOverlay");
        card.SetResourceReference(Border.BorderBrushProperty, "StrokeSubtle");

        // Hover only changes the stroke color; a constant thickness keeps the content from shifting.
        card.MouseEnter += (sender, e) => card.SetResourceReference(Border.BorderBrushProperty, "Accent");
        card.MouseLeave += (sender, e) => card.SetResourceReference(Border.BorderBrushProperty, "StrokeSubtle");

        var root = new StackPanel { Orientation = Orientation.Vertical };
        card.Child = root;

        // Header Stack
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        
        // Shop Name
        var shopTitle = CleanLabel(s.Label) ?? Properties.Resources.ShopWord;
        
        var titleText = new TextBlock
        {
            Text       = shopTitle,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        titleText.SetResourceReference(TextBlock.FontSizeProperty, compact ? "TypeBody" : "TypeSubtitle");
        head.Children.Add(titleText);

        // Grid coordinates badge pill
        var gridBadge = new Border
        {
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        gridBadge.SetResourceReference(Border.BackgroundProperty, "AccentTintBrush");
        gridBadge.SetResourceReference(Border.CornerRadiusProperty, "RadiusControl");
        var gridText = new TextBlock
        {
            Text = GetGridLabel(s) ?? "",
            FontWeight = FontWeights.SemiBold
        };
        gridText.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        gridText.SetResourceReference(TextBlock.FontSizeProperty, "TypeCaption");
        gridBadge.Child = gridText;
        head.Children.Add(gridBadge);
        
        root.Children.Add(head);

        // Offer Rows
        foreach (var o in offers)
        {
            root.Children.Add(BuildOfferRowSearchUI(o, compact));
        }

        card.MouseLeftButtonUp += (_, __) => { CenterMapOnWorldAnimated(s.X, s.Y, false, true); };
        return card;
    }

    private FrameworkElement BuildOfferRowSearchUI(RustPlusClientReal.ShopOrder o, bool compact)
    {
        bool outOfStock = o.Stock <= 0;
        
        var rowBorder = new Border
        {
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 2, 0, 2)
        };
        rowBorder.SetResourceReference(Border.CornerRadiusProperty, "RadiusControl");
        rowBorder.SetResourceReference(Border.BackgroundProperty, "FillControl");

        var g = new Grid { Opacity = outOfStock ? 0.65 : 1.0 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 0: Item Icon
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 1: Item Name & Stock stack
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 2: Pay Currency Details

        // 0: Item Icon
        var li = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(li, BitmapScalingMode.HighQuality);
        BindIcon(li, o.ItemShortName, o.ItemId);
        Grid.SetColumn(li, 0);
        g.Children.Add(li);

        // 1: Item Name + Stock Stack (Massive horizontal space, no squeezing)
        var nameStack = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 10, 0) };
        
        var name = ResolveItemName(o.ItemId, o.ItemShortName);
        if (o.Quantity > 1) name = $"x{o.Quantity} {name}";
        if (compact && name.Length > 28) name = name[..28] + "…";
        
        var lt = new TextBlock 
        { 
            Text = name, 
            VerticalAlignment = VerticalAlignment.Center
        };
        lt.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        lt.SetResourceReference(TextBlock.FontSizeProperty, "TypeBody");
        nameStack.Children.Add(lt);

        // Stock label directly below name, matching its association with item sold
        var stockPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        // Stock is quiet secondary text; only "out of stock" earns a status color.
        var stockText = new TextBlock
        {
            Text = outOfStock ? "Out of stock" : $"Stock: {o.Stock}"
        };
        stockText.SetResourceReference(TextBlock.ForegroundProperty, outOfStock ? "DangerBrush" : "TextSecondary");
        stockText.SetResourceReference(TextBlock.FontSizeProperty, "TypeCaption");
        stockPanel.Children.Add(stockText);
        nameStack.Children.Add(stockPanel);
        Grid.SetColumn(nameStack, 1);
        g.Children.Add(nameStack);

        // 2: Dynamic Transaction Flow indicator & Currency (Aligned beautifully at the right)
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var arrowText = new TextBlock { Text = " →  " };
        arrowText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        arrowText.SetResourceReference(TextBlock.FontSizeProperty, "TypeBody");
        right.Children.Add(arrowText);
        
        var ci = new Image { Width = 14, Height = 14, Margin = new Thickness(0, 0, 4, 0) };
        RenderOptions.SetBitmapScalingMode(ci, BitmapScalingMode.HighQuality);
        BindIcon(ci, o.CurrencyShortName, o.CurrencyItemId);
        right.Children.Add(ci);
        
        var priceText = new TextBlock
        {
            Text       = $"{o.CurrencyAmount} {ResolveItemName(o.CurrencyItemId, o.CurrencyShortName)}",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        priceText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        priceText.SetResourceReference(TextBlock.FontSizeProperty, "TypeBody");
        right.Children.Add(priceText);
        Grid.SetColumn(right, 2);
        g.Children.Add(right);

        rowBorder.Child = g;
        return rowBorder;
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
        Ach.Unlock(Ach.Shops);
        ToggleShopSearch();
        if (ShopSearchContent.Visibility == Visibility.Visible)
        {
            _ = InitEmbeddedShopSearchAsync();
        }
    }

    private void ToggleShopSearch()
    {
        // Shops removed from the Rust+ feed, or this server stopped delivering them —
        // never open the shop search (it is also reachable via shortcut and tutorial).
        if (Services.RustApiFeatures.EventsAndShopsRemoved || !_shopDataAvailable)
            return;

        if (ShopSearchContent.Visibility == Visibility.Collapsed)
        {
            _ = InitEmbeddedShopSearchAsync();
            UpdateShopPollingWarning();
            
            // Fluent entrance: a short rise into place with the "fast in, slow to rest" curve.
            // No vertical squash; the panel keeps its shape and only moves and fades.
            var rise = new TranslateTransform(0, 16);
            ShopSearchContent.RenderTransform = rise;
            ShopSearchContent.Visibility = Visibility.Visible;
            ShopSearchContent.Opacity = 0;

            var fade = new System.Windows.Media.Animation.DoubleAnimation(1, Helpers.Motion.For(Helpers.Motion.Fast)) { EasingFunction = Helpers.Motion.Enter };
            var slide = new System.Windows.Media.Animation.DoubleAnimation(0, Helpers.Motion.For(Helpers.Motion.Normal)) { EasingFunction = Helpers.Motion.Enter };
            slide.Completed += (s, e) =>
            {
                ShopSearchContent.Opacity = 1.0;
                ShopSearchContent.RenderTransform = null; // Completely clear transforms to prevent subpixel layout flickering when typing/focusing
                ShopSearchContent.BeginAnimation(UIElement.OpacityProperty, null);
            };
            ShopSearchContent.BeginAnimation(UIElement.OpacityProperty, fade);
            rise.BeginAnimation(TranslateTransform.YProperty, slide);
        }
        else
        {
            // Fluent exit: shorter than the entrance, accelerating away, always with a fade.
            var drop = new TranslateTransform(0, 0);
            ShopSearchContent.RenderTransform = drop;

            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, Helpers.Motion.For(Helpers.Motion.Fast)) { EasingFunction = Helpers.Motion.Exit };
            var slide = new System.Windows.Media.Animation.DoubleAnimation(8, Helpers.Motion.For(Helpers.Motion.Fast)) { EasingFunction = Helpers.Motion.Exit };
            slide.Completed += (s, e) =>
            {
                ShopSearchContent.Visibility = Visibility.Collapsed;
                ShopSearchContent.RenderTransform = null;
                ShopSearchContent.BeginAnimation(UIElement.OpacityProperty, null);
                ShopSearchContent.Opacity = 1.0;
            };
            ShopSearchContent.BeginAnimation(UIElement.OpacityProperty, fade);
            drop.BeginAnimation(TranslateTransform.YProperty, slide);
        }
    }
    private void BtnCloseShopSearch_Click(object sender, RoutedEventArgs e)
    {
        ToggleShopSearch();
    }

    private void UpdateShopPollingWarning()
    {
        if (ShopSearchWarning != null)
        {
            ShopSearchWarning.Visibility = ChkShops.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void BtnActivateShopPolling_Click(object sender, RoutedEventArgs e)
    {
        ChkShops.IsChecked = true;
        UpdateShopPollingWarning();
    }

    // ── Native C# Initialization ──────────────────────────────────────────────
    internal Task InitEmbeddedShopSearchAsync()
    {
        Dispatcher.Invoke(() =>
        {
            EmbeddedShopSearch?.Initialize(this);
            EmbeddedShopSearch?.UpdateFilterButtonsStyles();
        });
        return Task.CompletedTask;
    }

    public void UpdateShopSearchToolHighlights()
    {
        Dispatcher.Invoke(() =>
        {
            EmbeddedShopSearch?.UpdateFilterButtonsStyles();
        });
    }

    // ── Helper getters/setters for ShopSearchControl ─────────────────────────
    internal List<RustPlusClientReal.ShopMarker> GetLastShopsList()
    {
        return _lastShops;
    }

    internal List<(double X, double Y, string Name)> GetMapMonumentsList()
    {
        return _monData ?? new List<(double X, double Y, string Name)>();
    }

    internal List<ShopAlertRule> GetAlertRulesList()
    {
        return _alertRules;
    }

    internal void AddAlertRule(ShopAlertRule rule)
    {
        _alertRules.Add(rule);
        SavePersistentAlerts();
        UpdateMasterToggleState();
        SyncAlertMenuItems();
        EmbeddedShopSearch?.RefreshAlertListUI();
    }

    internal void RemoveAlertRule(ShopAlertRule rule)
    {
        _alertRules.Remove(rule);
        SavePersistentAlerts();
        UpdateMasterToggleState();
        SyncAlertMenuItems();
        EmbeddedShopSearch?.RefreshAlertListUI();
    }

    // Stub method for compatibility if anything in MainWindow tries to invoke alerts pushing
    public Task PushAlertsToWebViewAsync()
    {
        Dispatcher.Invoke(() =>
        {
            EmbeddedShopSearch?.RefreshAlertListUI();
        });
        return Task.CompletedTask;
    }

    private void RefreshShopSearchResults()
    {
        Dispatcher.Invoke(() =>
        {
            EmbeddedShopSearch?.RefreshSearchResults();
        });
    }
}
