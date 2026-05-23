using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private void BtnToggleMonumentsPanel_Click(object sender, RoutedEventArgs e)
    {
        if (MonumentNavPanel == null) return;

        if (MonumentNavPanel.Visibility == Visibility.Collapsed)
        {
            MonumentNavPanel.Visibility = Visibility.Visible;
            TxtMonumentSearch.Text = "";
            PopulateMonumentList();
            TxtMonumentSearch.Focus();
        }
        else
        {
            MonumentNavPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void TxtMonumentSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterMonumentList();
    }

    private void FilterMonumentList()
    {
        if (MonumentListContainer == null) return;

        string query = TxtMonumentSearch?.Text?.Trim()?.ToLower() ?? "";
        foreach (UIElement child in MonumentListContainer.Children)
        {
            if (child is Border itemBorder && itemBorder.Tag is ValueTuple<double, double, string> mon)
            {
                string niceName = Beautify(mon.Item3).ToLower();
                string gridName = GetGridLabel(mon.Item1, mon.Item2).ToLower();
                if (string.IsNullOrEmpty(query) || niceName.Contains(query) || gridName.Contains(query))
                {
                    itemBorder.Visibility = Visibility.Visible;
                }
                else
                {
                    itemBorder.Visibility = Visibility.Collapsed;
                }
            }
        }
    }

    public void PopulateMonumentList()
    {
        if (MonumentListContainer == null || _monData == null) return;

        MonumentListContainer.Children.Clear();

        // Sort monuments alphabetically by their beautified name
        var sortedMons = _monData
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .OrderBy(m => Beautify(m.Name))
            .ToList();

        foreach (var mon in sortedMons)
        {
            string niceName = Beautify(mon.Name);
            string key = NormalizeMonName(mon.Name, out var variant);
            
            // Build Left Indicator Bar (Accent Color, visible on hover)
            var accentBar = new Border
            {
                Width = 3,
                Background = new SolidColorBrush(Color.FromRgb(96, 205, 255)), // WinUI 3 Accent Blue
                CornerRadius = new CornerRadius(1.5),
                Margin = new Thickness(0, 4, 6, 4),
                Visibility = Visibility.Hidden
            };

            // Monument Row Grid
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Accent bar
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Icon
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Name

            // Accent bar placement
            Grid.SetColumn(accentBar, 0);
            grid.Children.Add(accentBar);

            // Icon Element
            FrameworkElement iconEl;
            string canonKey = Canon(key);
            
            if (canonKey.Contains("train tunnel"))
            {
                // Custom Train Icon
                var img = new Image
                {
                    Source = new BitmapImage(new Uri("pack://application:,,,/Assets/icons/assets_markers_train.png")),
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconEl = img;
            }
            else if (sMonIconByKey.TryGetValue(canonKey, out var uri))
            {
                // Standard Monument Icon
                var img = new Image
                {
                    Source = new BitmapImage(new Uri(uri)),
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconEl = img;
            }
            else
            {
                // Fallback: A nice small colored ellipse/dot to keep align alignment clean
                var dot = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = Brushes.SlateGray,
                    Margin = new Thickness(5, 0, 13, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconEl = dot;
            }

            Grid.SetColumn(iconEl, 1);
            grid.Children.Add(iconEl);

            // Text Label
            string gridName = GetGridLabel(mon.X, mon.Y);
            var txt = new TextBlock
            {
                Text = $"{niceName} [{gridName}]",
                Foreground = new SolidColorBrush(Color.FromRgb(240, 243, 247)), // TextPrimary
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            Grid.SetColumn(txt, 2);
            grid.Children.Add(txt);

            // Row Container Border
            var rowBorder = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4, 6, 8, 6),
                Margin = new Thickness(0, 2, 0, 2),
                Cursor = Cursors.Hand,
                Child = grid,
                Tag = mon // Keep reference to coordinate data
            };

            // Hover & Interactive Micro-Animations
            rowBorder.MouseEnter += (s, e) =>
            {
                rowBorder.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)); // Subtle light hover glow
                accentBar.Visibility = Visibility.Visible;
                txt.Foreground = Brushes.White;

                // Scale up the marker on the map for immersive tracking
                var p = WorldToImagePx(mon.X, mon.Y);
                string monKey = key + "@" + p.X.ToString("0") + "," + p.Y.ToString("0");
                if (_monEls.TryGetValue(monKey, out var el))
                {
                    el.RenderTransform = new ScaleTransform(1.6, 1.6);
                    Panel.SetZIndex(el, 999); // Bring to front
                }
            };

            rowBorder.MouseLeave += (s, e) =>
            {
                rowBorder.Background = Brushes.Transparent;
                accentBar.Visibility = Visibility.Hidden;
                txt.Foreground = new SolidColorBrush(Color.FromRgb(240, 243, 247)); // TextPrimary

                // Restore marker scale back to standard
                var p = WorldToImagePx(mon.X, mon.Y);
                string monKey = key + "@" + p.X.ToString("0") + "," + p.Y.ToString("0");
                if (_monEls.TryGetValue(monKey, out var el))
                {
                    ApplyMonumentScale(el);
                    Panel.SetZIndex(el, 800); // Reset ZIndex
                }
            };

            rowBorder.MouseLeftButtonUp += (s, e) =>
            {
                // Smoothly fly to coordinates, overview dip, focus zoom level 6.0!
                CenterMapOnWorldAnimated(mon.X, mon.Y, allowDip: true, fast: false, keepTracking: false, targetZoom: 6.0);
            };

            MonumentListContainer.Children.Add(rowBorder);
        }

        FilterMonumentList();
    }
}
