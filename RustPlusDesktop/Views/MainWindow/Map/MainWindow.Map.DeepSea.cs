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

    // The Deep Sea is a FIXED instance (same for every server): always 27x27 cells,
    // and it sits at a FIXED position in Unity/world space. It is NOT scaled by the
    // main map size. What changes per map is only the on-screen offset, because
    // Rust+ reports coordinates relative to the map center (serverCoord = Unity + S/2).
    // So the box is anchored to S/2 while its cell size and extent stay constant.
    //
    // Cell size and anchor from cross-map least-squares fits of on-grid-line points
    // in Unity coords (fixed box => single line across all maps). serverCoord = Unity + S/2.
    // Widest clean baseline: 3500 col 24 (-4344.12) <-> col 1 (-7751.9), 23 cells => 148.16 m.
    // X points (col, Unity X): 3750 c19 -5082.80 / -5085.00 ; 3500 c24 -4344.12, c4 -7307.87,
    //                          c3 -7455.86, c1 -7751.90
    // Y points (row, Unity Z): 3750 r8 814.62, r21 -1111.81, r23 -1409.49 ;
    //                          3500 r7 962.93, r6 1111.43
    //   => cell ~= 148.20 m (both axes), minX_unity = -7900.5, maxY_unity = 2000.4.
    // Residuals are sub-meter, i.e. at the limit of how precisely one can stand on a cross
    // + read printpos (the same col-19 line read twice differs by 2.2 m).
    public const int DeepSeaCells = 27;
    public const double DeepSeaCellSize = 148.20;
    private const double DeepSeaBoxSize = DeepSeaCells * DeepSeaCellSize; // ~4001.4

    private (double minX, double maxX, double minY, double maxY) GetDeepSeaWorldBox()
    {
        double half = _worldSizeS / 2.0;
        double minX = half - 7900.5;
        double maxX = minX + DeepSeaBoxSize;
        double maxY = half + 2000.4;
        double minY = maxY - DeepSeaBoxSize;
        return (minX, maxX, minY, maxY);
    }

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
