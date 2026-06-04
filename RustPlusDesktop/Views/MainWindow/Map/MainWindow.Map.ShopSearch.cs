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

    // Proxy controls kept for backward compatibility if referenced elsewhere
    private TextBox?  _searchTb;
    private CheckBox? _chkSell;
    private CheckBox? _chkBuy;
    private ListBox?  _alertList;

    // ── WPF card builder — still used by PathFinder, map hover popup & ShopSearchControl ─────────
    internal FrameworkElement BuildShopSearchCard(
        RustPlusClientReal.ShopMarker s,
        IEnumerable<RustPlusClientReal.ShopOrder> offers,
        bool compact)
    {
        // 1. High-End Linear Gradient Background (Slate to Dark Carbon)
        var glassyBg = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        glassyBg.GradientStops.Add(new GradientStop(Color.FromRgb(45, 50, 60), 0.0)); // Slate
        glassyBg.GradientStops.Add(new GradientStop(Color.FromRgb(29, 32, 38), 1.0)); // Charcoal

        var defaultBorderBrush = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)); // rgba(255, 255, 255, 0.15)
        var hoverBorderBrush = new SolidColorBrush(Color.FromRgb(0, 173, 239)); // Electric Blue #00adef

        var card = new Border
        {
            Background       = glassyBg,
            BorderBrush      = defaultBorderBrush,
            BorderThickness  = new Thickness(1),
            CornerRadius     = new CornerRadius(10),
            Padding          = new Thickness(10),
            Margin           = new Thickness(0, 0, 0, 8),
            Cursor           = Cursors.Hand
        };

        // Reactive Pointer Highlights (Hover states)
        card.MouseEnter += (sender, e) =>
        {
            card.BorderBrush = hoverBorderBrush;
            card.BorderThickness = new Thickness(1.2);
        };
        card.MouseLeave += (sender, e) =>
        {
            card.BorderBrush = defaultBorderBrush;
            card.BorderThickness = new Thickness(1.0);
        };

        var root = new StackPanel { Orientation = Orientation.Vertical };
        card.Child = root;

        // Header Stack
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        
        // Shop Name
        var shopTitle = CleanLabel(s.Label) ?? "Shop";
        
        head.Children.Add(new TextBlock
        {
            Text       = shopTitle,
            Foreground = SearchText,
            FontWeight = FontWeights.Bold,
            FontSize   = compact ? 13 : 15,
            VerticalAlignment = VerticalAlignment.Center
        });

        // Grid coordinates badge pill
        var gridBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(20, 0, 173, 239)), // semi-transparent accent
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 0, 173, 239)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        gridBadge.Child = new TextBlock
        {
            Text = GetGridLabel(s) ?? "",
            Foreground = new SolidColorBrush(Color.FromRgb(0, 173, 239)),
            FontSize = 10,
            FontWeight = FontWeights.Bold
        };
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
            Margin = new Thickness(0, 2, 0, 2),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255))
        };

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
            Foreground = SearchText, 
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12
        };
        nameStack.Children.Add(lt);

        // Stock label directly below name, matching its association with item sold
        var stockPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        stockPanel.Children.Add(new TextBlock
        {
            Text = outOfStock ? "📦 Out of stock" : $"📦 Stock: {o.Stock}",
            Foreground = outOfStock 
                ? new SolidColorBrush(Color.FromRgb(239, 83, 80)) 
                : new SolidColorBrush(Color.FromRgb(102, 187, 106)),
            FontSize = 9.5,
            FontWeight = FontWeights.Bold
        });
        nameStack.Children.Add(stockPanel);
        Grid.SetColumn(nameStack, 1);
        g.Children.Add(nameStack);

        // 2: Dynamic Transaction Flow indicator & Currency (Aligned beautifully at the right)
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(new TextBlock 
        { 
            Text = " →  ", 
            Foreground = new SolidColorBrush(Color.FromRgb(138, 162, 178)), 
            FontWeight = FontWeights.Bold,
            FontSize = 12
        });
        
        var ci = new Image { Width = 14, Height = 14, Margin = new Thickness(0, 0, 4, 0) };
        RenderOptions.SetBitmapScalingMode(ci, BitmapScalingMode.HighQuality);
        BindIcon(ci, o.CurrencyShortName, o.CurrencyItemId);
        right.Children.Add(ci);
        
        right.Children.Add(new TextBlock
        {
            Text       = $"{o.CurrencyAmount} {ResolveItemName(o.CurrencyItemId, o.CurrencyShortName)}",
            Foreground = SearchText,
            FontWeight = FontWeights.SemiBold,
            FontSize   = 12
        });
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
        ToggleShopSearch();
        if (ShopSearchContent.Visibility == Visibility.Visible)
        {
            _ = InitEmbeddedShopSearchAsync();
        }
    }

    private void ToggleShopSearch()
    {
        if (ShopSearchContent.Visibility == Visibility.Collapsed)
        {
            _ = InitEmbeddedShopSearchAsync();
            UpdateShopPollingWarning();
            
            var scaleTrans = new ScaleTransform(1, 0.85);
            var translateTrans = new TranslateTransform(0, 40);
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(scaleTrans);
            transformGroup.Children.Add(translateTrans);
            
            ShopSearchContent.RenderTransform = transformGroup;
            ShopSearchContent.Visibility = Visibility.Visible;
            ShopSearchContent.Opacity = 0;

            var sb = new System.Windows.Media.Animation.Storyboard();
            var fade = new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(250)) { EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
            var scale = new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(300)) { EasingFunction = new System.Windows.Media.Animation.BackEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Amplitude = 0.2 } };
            var slide = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(250)) { EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };

            System.Windows.Media.Animation.Storyboard.SetTarget(fade, ShopSearchContent);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            System.Windows.Media.Animation.Storyboard.SetTarget(scale, scaleTrans);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(scale, new PropertyPath("ScaleY"));
            System.Windows.Media.Animation.Storyboard.SetTarget(slide, translateTrans);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(slide, new PropertyPath("Y"));

            sb.Children.Add(fade);
            sb.Children.Add(scale);
            sb.Children.Add(slide);
            sb.Completed += (s, e) =>
            {
                ShopSearchContent.Opacity = 1.0;
                ShopSearchContent.RenderTransform = null; // Completely clear transforms to prevent subpixel layout flickering when typing/focusing
                ShopSearchContent.BeginAnimation(UIElement.OpacityProperty, null);
            };
            sb.Begin();
        }
        else
        {
            var scaleTrans = new ScaleTransform(1, 1);
            var translateTrans = new TranslateTransform(0, 0);
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(scaleTrans);
            transformGroup.Children.Add(translateTrans);
            
            ShopSearchContent.RenderTransform = transformGroup;

            var sb = new System.Windows.Media.Animation.Storyboard();
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
            var scale = new System.Windows.Media.Animation.DoubleAnimation(0.85, TimeSpan.FromMilliseconds(200));
            var slide = new System.Windows.Media.Animation.DoubleAnimation(40, TimeSpan.FromMilliseconds(200));

            System.Windows.Media.Animation.Storyboard.SetTarget(fade, ShopSearchContent);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            System.Windows.Media.Animation.Storyboard.SetTarget(scale, scaleTrans);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(scale, new PropertyPath("ScaleY"));
            System.Windows.Media.Animation.Storyboard.SetTarget(slide, translateTrans);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(slide, new PropertyPath("Y"));

            sb.Children.Add(fade);
            sb.Children.Add(scale);
            sb.Children.Add(slide);
            sb.Completed += (s, e) =>
            {
                ShopSearchContent.Visibility = Visibility.Collapsed;
                ShopSearchContent.RenderTransform = null;
            };
            sb.Begin();
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
