using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private bool _isShowingDeepSeaMap = false;
    private bool _myPlayerWasInDeepSea = false;
    private readonly List<FrameworkElement> _deepSeaOverlayElements = new();

    private void SetShowingDeepSeaMap(bool show)
    {
        if (_isShowingDeepSeaMap == show) return;
        _isShowingDeepSeaMap = show;

        Dispatcher.Invoke(() =>
        {
            // 1. Toggle background & Map Visibility
            if (_isShowingDeepSeaMap)
            {
                if (ImgMap != null) ImgMap.Visibility = Visibility.Collapsed;
                if (_scene != null) _scene.Background = new SolidColorBrush(Color.FromRgb(24, 43, 73)); // Deep sea blue
                
                // Update toggle button icon to Map icon (Wpf.Ui Map symbol)
                if (ContentDeepSeaToggle != null)
                {
                    ContentDeepSeaToggle.Content = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Map20);
                }
            }
            else
            {
                if (ImgMap != null) ImgMap.Visibility = Visibility.Visible;
                if (_scene != null) _scene.Background = null;
                
                // Update toggle button icon to ds_event.png
                if (ContentDeepSeaToggle != null)
                {
                    ContentDeepSeaToggle.Content = new Image 
                    { 
                        Source = new BitmapImage(new Uri("pack://application:,,,/Assets/icons/ds_event.png")), 
                        Width = 20, 
                        Height = 20 
                    };
                }
            }

            // 2. Redraw grid
            RedrawGrid();

            // 3. Update Monument visibility
            if (Monuments != null)
            {
                Monuments.IsEnabled = !_isShowingDeepSeaMap;
            }
            if (ChkNoBuildZones != null)
            {
                ChkNoBuildZones.IsEnabled = !_isShowingDeepSeaMap;
            }
            if (_monEls != null)
            {
                foreach (var fe in _monEls.Values)
                {
                    fe.Visibility = (_showMonuments && !_isShowingDeepSeaMap) ? Visibility.Visible : Visibility.Collapsed;
                }
                // Immediately update positions in the new coordinate system
                RefreshMonumentOverlayPositions();
            }

            // 4. Update Shop visibility
            RefreshShopVisibility();
            // Immediately update positions in the new coordinate system
            RefreshShopPositions();

            // 5. Update Overlay Drawing Elements visibility
            RefreshOverlayElementsVisibility();

            // 6. Force position update of all dynamic markers (players and events)
            if (_lastMarkers != null)
            {
                UpdateDynUI(_lastMarkers);
            }

            // 7. Redraw death markers for the current map view
            RedrawDeathPins();
        });
    }

    private void BtnDeepSeaToggle_Click(object sender, RoutedEventArgs e)
    {
        SetShowingDeepSeaMap(!_isShowingDeepSeaMap);
    }

    private void RefreshShopPositions()
    {
        if (_shopEls == null) return;
        foreach (var kv in _shopEls)
        {
            var el = kv.Value;
            if (el is Grid g && g.Tag is List<RustPlusClientReal.ShopMarker> cluster && cluster.Count > 0)
            {
                double avgX = 0;
                double avgY = 0;
                try
                {
                    avgX = System.Linq.Enumerable.Average(cluster, s => s.X);
                    avgY = System.Linq.Enumerable.Average(cluster, s => s.Y);
                }
                catch
                {
                    avgX = cluster[0].X;
                    avgY = cluster[0].Y;
                }
                var p = WorldToImagePx(avgX, avgY);
                Canvas.SetLeft(el, p.X - 12);
                Canvas.SetTop(el, p.Y - 12);
            }
        }
    }

    private void RefreshShopVisibility()
    {
        if (_shopEls == null) return;
        foreach (var kv in _shopEls)
        {
            if (kv.Value is Grid g && g.Tag is List<RustPlusClientReal.ShopMarker> cluster)
            {
                double avgX = 0;
                try 
                { 
                    avgX = System.Linq.Enumerable.Average(cluster, s => s.X); 
                } 
                catch 
                { 
                    if (cluster.Count > 0) avgX = cluster[0].X;
                }
                bool isDeepSea = avgX < 0;
                g.Visibility = (_isShowingDeepSeaMap == isDeepSea) ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void RefreshOverlayElementsVisibility()
    {
        if (_playerOverlayElements != null)
        {
            foreach (var kv in _playerOverlayElements)
            {
                ulong steamId = kv.Key;
                bool normalVisible = _visibleOverlayOwners != null && _visibleOverlayOwners.Contains(steamId);
                foreach (var el in kv.Value)
                {
                    el.Visibility = (!_isShowingDeepSeaMap && normalVisible) ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        if (_deepSeaOverlayElements != null)
        {
            foreach (var el in _deepSeaOverlayElements)
            {
                el.Visibility = _isShowingDeepSeaMap ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
