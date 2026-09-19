using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private void SetupMapScene(BitmapSource bmp)
    {
        double wDip = bmp.PixelWidth * (96.0 / bmp.DpiX);
        double hDip = bmp.PixelHeight * (96.0 / bmp.DpiY);

        const double padPx = 000; // Zeichen-Rand

        ImgMap.Stretch = Stretch.None;
        ImgMap.HorizontalAlignment = HorizontalAlignment.Left;
        ImgMap.VerticalAlignment = VerticalAlignment.Top;
        ImgMap.Width = wDip;
        ImgMap.Height = hDip;
        RenderOptions.SetBitmapScalingMode(ImgMap, BitmapScalingMode.HighQuality);

        ImgHeatmap.Stretch = Stretch.Fill;
        ImgHeatmap.HorizontalAlignment = HorizontalAlignment.Left;
        ImgHeatmap.VerticalAlignment = VerticalAlignment.Top;
        
        var ptTopLeft = WorldToImagePx(0, _worldSizeS);
        var ptBotRight = WorldToImagePx(_worldSizeS, 0);
        if (ptBotRight.X > ptTopLeft.X && ptBotRight.Y > ptTopLeft.Y)
        {
            ImgHeatmap.Width = ptBotRight.X - ptTopLeft.X;
            ImgHeatmap.Height = ptBotRight.Y - ptTopLeft.Y;
            ImgHeatmap.Margin = new Thickness(ptTopLeft.X, ptTopLeft.Y, 0, 0);
        }
        else
        {
            ImgHeatmap.Width = wDip;
            ImgHeatmap.Height = hDip;
        }
        RenderOptions.SetBitmapScalingMode(ImgHeatmap, BitmapScalingMode.HighQuality);

        GridLayer.Width = wDip;
        GridLayer.Height = hDip;
        GridLayer.Opacity = 1.0;   // the wrapper carries the user opacity, see ApplyIndependentLayerVisibility
        GridLayer.IsHitTestVisible = false;

        // Independent of the grid: its own canvas, never dimmed by the grid opacity.
        NoBuildLayer.Width = wDip;
        NoBuildLayer.Height = hDip;
        NoBuildLayer.IsHitTestVisible = false;

        CargoPathLayer.Width = wDip;
        CargoPathLayer.Height = hDip;
        CargoPathLayer.IsHitTestVisible = true;

        KeycardLayer.Width = wDip;
        KeycardLayer.Height = hDip;
        KeycardLayer.IsHitTestVisible = false;

        // WICHTIG: Overlay groesser machen, aber Map nicht anfassen
        Overlay.Width = wDip + padPx * 2;
        Overlay.Height = hDip + padPx * 2;
        Overlay.IsHitTestVisible = true;
        Overlay.Background = Brushes.Transparent;

        // Icons, players and the transient inline panels used to share the Overlay canvas and
        // sort themselves by per-element ZIndex. They are separate canvases now, because the
        // mini-map mirrors each one through its own VisualBrush and a brush can only take a
        // whole visual — the stacking order below reproduces the ZIndex bands they had.
        foreach (var layer in new[] { IconLayer, PlayerLayer, DeathLayer, MapUiLayer })
        {
            layer.Width = Overlay.Width;
            layer.Height = Overlay.Height;
            layer.IsHitTestVisible = true;
            layer.Background = null;   // null, not Transparent: gaps stay click-through to Overlay
        }

        _scene ??= new Grid();
        _scene.Width = wDip + padPx * 2;
        _scene.Height = hDip + padPx * 2;

        (ImgMap.Parent as Panel)?.Children.Remove(ImgMap);
        (ImgHeatmap.Parent as Panel)?.Children.Remove(ImgHeatmap);
        (GridLayer.Parent as Panel)?.Children.Remove(GridLayer);
        (NoBuildLayer.Parent as Panel)?.Children.Remove(NoBuildLayer);
        (CargoPathLayer.Parent as Panel)?.Children.Remove(CargoPathLayer);
        (KeycardLayer.Parent as Panel)?.Children.Remove(KeycardLayer);
        (Overlay.Parent as Panel)?.Children.Remove(Overlay);
        (IconLayer.Parent as Panel)?.Children.Remove(IconLayer);
        (PlayerLayer.Parent as Panel)?.Children.Remove(PlayerLayer);
        (DeathLayer.Parent as Panel)?.Children.Remove(DeathLayer);
        (MapUiLayer.Parent as Panel)?.Children.Remove(MapUiLayer);

        _scene.Children.Clear();

        // Map bei (padPx, padPx)? -> NEIN, jetzt bei (0,0)!
        _scene.Children.Add(ImgMap); Panel.SetZIndex(ImgMap, 0);
        // Wrapped rather than added directly: the image sits on the world rect via a
        // Margin, so its own origin is offset from the scene's. The mini-map mirrors
        // layers through VisualBrushes that all share one absolute viewbox, and an
        // offset origin would slide the heatmap out of place there. The wrapper grid
        // fills the scene, so the brush sees the same coordinates as every other layer.
        _scene.Children.Add(Wrap(ref _heatmapWrapper, ImgHeatmap)); Panel.SetZIndex(_heatmapWrapper!, 1);
        _scene.Children.Add(Wrap(ref _gridWrapper, GridLayer)); Panel.SetZIndex(_gridWrapper!, 2);
        _scene.Children.Add(Wrap(ref _noBuildWrapper, NoBuildLayer)); Panel.SetZIndex(_noBuildWrapper!, 3);
        _scene.Children.Add(CargoPathLayer); Panel.SetZIndex(CargoPathLayer, 4);
        // Above the monument icons it annotates. Shares ZIndex 7 with the death
        // wrapper added below, and loses to it on insertion order, which is what we
        // want: a death marker is news, a keycard icon is reference.
        _scene.Children.Add(KeycardLayer); Panel.SetZIndex(KeycardLayer, 7);
        _scene.Children.Add(Overlay); Panel.SetZIndex(Overlay, 5);
        _scene.Children.Add(IconLayer); Panel.SetZIndex(IconLayer, 6);
        _scene.Children.Add(Wrap(ref _deathWrapper, DeathLayer)); Panel.SetZIndex(_deathWrapper!, 7);
        _scene.Children.Add(PlayerLayer); Panel.SetZIndex(PlayerLayer, 8);
        _scene.Children.Add(MapUiLayer); Panel.SetZIndex(MapUiLayer, 9);

        ApplyIndependentLayerVisibility();

        _scene.RenderTransform = MapTransform;

        if (_mapView == null)
        {
            _mapView = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both };
            WebViewHost.Children.Add(_mapView);
            Panel.SetZIndex(_mapView, 0);
        }
        _mapView.Child = _scene;
        ApplyMapPerformanceSettings();

        // The layers were just reparented; an open mini-map has to be told, or it keeps
        // mirroring whatever its brushes were pointed at before the new map arrived.
        RefreshMiniMapLayers();
    }

    // ── Layers the mini-map can switch on its own ───────────────────────────────
    //
    // The grid, the death markers and the no-build zones are the ones the user can want in one
    // place and not the other. All used to be hidden by emptying or collapsing the layer itself
    // — which the mini-map mirrors, so its own switch could only ever turn them further off.
    //
    // Each now sits in a wrapper that only the main map owns. Hiding means the wrapper goes to
    // zero opacity, and a VisualBrush of the layer inside renders its own subtree without an
    // ancestor's opacity — so the mini-map still sees it. Collapsing the wrapper would not work:
    // a collapsed parent never lays its children out, and the brush would come back empty.

    private Grid? _gridWrapper;
    private Grid? _heatmapWrapper;
    private Grid? _deathWrapper;
    private Grid? _noBuildWrapper;

    private static Grid Wrap(ref Grid? wrapper, UIElement layer)
    {
        wrapper ??= new Grid();
        wrapper.Children.Clear();
        wrapper.Children.Add(layer);
        return wrapper;
    }

    /// <summary>True while an open mini-map is asking for a layer the main map has switched off.</summary>
    private bool MiniMapWantsGrid => _miniMap is { IsVisible: true } m && m.WantsGridLayer;

    private bool MiniMapWantsDeathMarkers => _miniMap is { IsVisible: true } m && m.WantsDeathLayer;

    private bool MiniMapWantsNoBuildZones => _miniMap is { IsVisible: true } m && m.WantsNoBuildLayer;

    /// <summary>
    /// Whether this server's map has building-blocked zones to show at all.
    ///
    /// The checkbox on the main map is the thing that knows, because loading the file is what
    /// enables it. The mini-map's own switch asks here rather than reading the file a second
    /// time, so the two can never disagree about whether there is anything to draw.
    /// </summary>
    public bool NoBuildZonesAvailable => ChkNoBuildZones?.IsEnabled == true;

    /// <summary>
    /// Applies the main map's own choice to the wrappers. Opacity rather than visibility, and
    /// hit testing off with it — a pin nobody can see must not swallow clicks.
    /// </summary>
    private void ApplyIndependentLayerVisibility()
    {
        if (_gridWrapper != null)
        {
            bool on = ChkGrid?.IsChecked == true;
            _gridWrapper.Opacity = on ? TrackingService.MapGridOpacity : 0;
            _gridWrapper.IsHitTestVisible = false;   // the grid never takes the mouse anyway
        }

        if (_deathWrapper != null)
        {
            bool on = _showDeathMarkers;
            _deathWrapper.Opacity = on ? 1 : 0;
            _deathWrapper.IsHitTestVisible = on;
        }

        if (_noBuildWrapper != null)
        {
            bool on = ChkNoBuildZones?.IsChecked == true;
            _noBuildWrapper.Opacity = on ? 1 : 0;
            _noBuildWrapper.IsHitTestVisible = false;   // the zones carry tooltips, not clicks
        }
    }

    /// <summary>
    /// Redraws whatever the mini-map's layer switches just started or stopped asking for.
    /// Both layers are only built when someone wants them, so a change of mind has to rebuild.
    /// </summary>
    internal void RefreshIndependentLayers()
    {
        try { RedrawGrid(); } catch { }
        try { RedrawDeathPins(); } catch { }
        try { RedrawBuildingBlockedZones(); } catch { }
        ApplyIndependentLayerVisibility();
    }

    /// <summary>
    /// Takes an element off whichever map layer holds it.
    ///
    /// Callers that add to a specific layer still remove through here on purpose: the element
    /// may have been placed before a layout change moved its kind to another canvas, and
    /// Children.Remove on a canvas that does not hold it is a no-op. One call that always
    /// works beats a classification that has to stay in sync at 30 removal sites.
    /// </summary>
    private void RemoveFromMapLayers(UIElement? el)
    {
        if (el == null) return;
        Overlay?.Children.Remove(el);
        IconLayer?.Children.Remove(el);
        PlayerLayer?.Children.Remove(el);
        DeathLayer?.Children.Remove(el);
        MapUiLayer?.Children.Remove(el);
    }

    /// <summary>
    /// Swaps an element for its rebuilt version, keeping the position it held in its canvas —
    /// that index is the draw order among same-ZIndex siblings, so losing it makes markers
    /// flicker past each other on every avatar or online-state change.
    /// </summary>
    private void ReplaceOnMapLayer(UIElement oldEl, UIElement newEl, Canvas fallback)
    {
        foreach (var layer in new[] { PlayerLayer, IconLayer, Overlay, DeathLayer, MapUiLayer })
        {
            if (layer == null) continue;
            int idx = layer.Children.IndexOf(oldEl);
            if (idx < 0) continue;
            layer.Children.RemoveAt(idx);
            layer.Children.Insert(idx, newEl);
            return;
        }
        fallback.Children.Add(newEl);
    }

    private void ResetMapDisplay()
    {
        _mapBaseBmp = null;

        ImgMap.Source = null;
        ImgHeatmap.Source = null;
        GridLayer.Children.Clear();
        NoBuildLayer?.Children.Clear();
        CargoPathLayer?.Children.Clear();

        _myPlayerWasInDeepSea = false;
        _isShowingDeepSeaMap = false;

        // The reset can happen while the Deep Sea view is active. Put the main-map visuals
        // back by hand — SetShowingDeepSeaMap would early-out on the flag we just cleared,
        // and the toggle stays visible, so it must start from a consistent state.
        if (ImgMap != null) ImgMap.Visibility = Visibility.Visible;
        if (_scene != null) _scene.Background = null;
        if (ContentDeepSeaToggle != null)
        {
            ContentDeepSeaToggle.Content = new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/Assets/icons/ds_event.png")),
                Width = 20,
                Height = 20
            };
        }

        if (MapPlaceholder != null) MapPlaceholder.Visibility = Visibility.Visible;
        if (_mapView != null) _mapView.Visibility = Visibility.Collapsed;

        if (_miniMap != null)
        {
            _miniMap.Close();
            _miniMap = null;

        }
    }

    private void ShowMapBasic(BitmapSource bmp)
    {
        _mapBaseBmp = bmp;
        if (MapPlaceholder != null) MapPlaceholder.Visibility = Visibility.Collapsed;
        if (_mapView != null) _mapView.Visibility = Visibility.Visible;
        _staticMarkers.Clear();            // << keine Testpunkte

        ImgMap.Source = bmp;               // zunaechst nackte Map
        SetupMapScene(bmp);
        RedrawGrid();
        Dispatcher.BeginInvoke(new System.Action(SaveCurrentPlayerWipeMap), System.Windows.Threading.DispatcherPriority.Background);
    }

    public void ApplyMapPerformanceSettings()
    {
        // 1. BitmapScalingMode
        BitmapScalingMode scalingMode = BitmapScalingMode.HighQuality;
        switch (TrackingService.MapBitmapScalingMode)
        {
            case 1:
                scalingMode = BitmapScalingMode.LowQuality;
                break;
            case 2:
                scalingMode = BitmapScalingMode.NearestNeighbor;
                break;
        }

        if (ImgMap != null)
        {
            RenderOptions.SetBitmapScalingMode(ImgMap, scalingMode);
        }
        if (ImgHeatmap != null)
        {
            RenderOptions.SetBitmapScalingMode(ImgHeatmap, scalingMode);
        }

        // 2. AliasedEdgeMode
        EdgeMode edgeMode = TrackingService.MapUseAliasedEdgeMode ? EdgeMode.Aliased : EdgeMode.Unspecified;
        if (GridLayer != null)
        {
            RenderOptions.SetEdgeMode(GridLayer, edgeMode);
        }
        if (Overlay != null)
        {
            RenderOptions.SetEdgeMode(Overlay, edgeMode);
            if (IconLayer != null) RenderOptions.SetEdgeMode(IconLayer, edgeMode);
            if (PlayerLayer != null) RenderOptions.SetEdgeMode(PlayerLayer, edgeMode);
        }
        RefreshGridLineThickness();

        // 3. CacheMode / RenderScale
        if (_scene != null)
        {
            if (TrackingService.MapUseCacheMode)
            {
                if (_scene.CacheMode is BitmapCache currentCache)
                {
                    currentCache.RenderAtScale = TrackingService.MapRenderScale;
                }
                else
                {
                    _scene.CacheMode = new BitmapCache { RenderAtScale = TrackingService.MapRenderScale };
                }
            }
            else
            {
                _scene.CacheMode = null;
            }
        }
    }
}
