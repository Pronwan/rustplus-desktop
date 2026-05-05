using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private const double MAP_DEFAULT_ZOOM = 1.0;
    private const double MAP_FOCUS_ZOOM   = 6.0;
 
    private Point _panLastHost;

    // Host (Maus) -> Scene-VOR-MapTransform (Pivot-Koordinaten fuer ScaleAt)
    private Point HostToScenePreTransform(Point hostPt)
    {
        var (s, offX, offY) = GetViewboxScaleAndOffset();

        var p = new Point(
            (hostPt.X - offX) / s,
            (hostPt.Y - offY) / s);

        var m = MapTransform.Matrix;
        if (m.HasInverse)
        {
            m.Invert();
            p = m.Transform(p);
        }
        return p;
    }

    // Host-Delta -> Scene-Delta (damit Panning pixelgenau folgt)
    private Vector HostDeltaToSceneDelta(Vector dHost)
    {
        var (s, _, _) = GetViewboxScaleAndOffset();

        var m = MapTransform.Matrix;
        double sx = m.M11, sy = m.M22;
        if (Math.Abs(sx) < 1e-9) sx = 1;
        if (Math.Abs(sy) < 1e-9) sy = 1;

        return new Vector(
            dHost.X / s,
            dHost.Y / s);
    }

    private void ZoomAtHostPosition(Point hostPos, double factor)
    {
        if (_scene == null) return;

        var pivot = HostToScenePreTransform(hostPos);

        var m = MapTransform.Matrix;
        double newScale = m.M11 * factor;
        
        // Clamp zoom level between 1.0 and 20.0
        if (newScale < 1.0) factor = 1.0 / m.M11;
        if (newScale > 20.0) factor = 20.0 / m.M11;

        m.ScaleAt(factor, factor, pivot.X, pivot.Y);
        MapTransform.Matrix = m;

        RefreshAllOverlayScales();
        RefreshMonumentOverlayPositions();
        RefreshUserOverlayIcons();
        CenterMiniMapOnPlayer();
    }

    private double _zoomAnimationTarget = -1;
    private Point _zoomAnimationPivot;
    private bool _isZooming = false;

    private async void ZoomAtHostPositionAnimated(Point hostPos, double factor)
    {
        if (_scene == null) return;

        var m = MapTransform.Matrix;
        double currentScale = m.M11;
        double targetScale = currentScale * factor;
        targetScale = Math.Max(1.0, Math.Min(20.0, targetScale));

        _zoomAnimationTarget = targetScale;
        _zoomAnimationPivot = hostPos;

        if (_isZooming) return;
        _isZooming = true;

        while (Math.Abs(MapTransform.Matrix.M11 - _zoomAnimationTarget) > 0.001)
        {
            var curM = MapTransform.Matrix;
            double curS = curM.M11;
            double diff = _zoomAnimationTarget - curS;
            
            // Move 25% of the way each frame for smooth easing
            double stepScale = curS + diff * 0.25;
            double stepFactor = stepScale / curS;

            ZoomAtHostPosition(_zoomAnimationPivot, stepFactor);
            await Task.Delay(16);
            
            // If the map was reloaded or scene cleared, stop
            if (_scene == null || !_isZooming) break;
        }
        
        // Final snap to target
        if (_scene != null && Math.Abs(MapTransform.Matrix.M11 - _zoomAnimationTarget) > 0)
        {
            ZoomAtHostPosition(_zoomAnimationPivot, _zoomAnimationTarget / MapTransform.Matrix.M11);
        }

        _isZooming = false;
    }

    private void WebViewHost_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_scene == null) return;

        double zoom = e.Delta > 0 ? 1.25 : (1.0 / 1.25);
        var hostPos = e.GetPosition(WebViewHost);

        if (e.Delta < 0) StopTracking(); // Only stop tracking on zoom OUT
        ZoomAtHostPositionAnimated(hostPos, zoom);
        e.Handled = true;
    }

    // Welt->Bild (Pixel im Bildkoordinatensystem - vor Zoom/Pan)
    private const double PAD_WORLD = 2000.0;

    private Point WorldToImagePx(double x, double y)
    {
        if (_worldSizeS <= 0 || _worldRectPx.Width <= 0 || _worldRectPx.Height <= 0)
            return new Point(0, 0);

        double totalWorld = _worldSizeS + PAD_WORLD;
        double halfPad = PAD_WORLD * 0.5;

        double xx = Math.Clamp(x, -halfPad, _worldSizeS + halfPad);
        double yy = Math.Clamp(y, -halfPad, _worldSizeS + halfPad);

        double fullSidePx = _worldRectPx.Width * (totalWorld / _worldSizeS);

        double imgW = _scene?.Width > 0 ? _scene.Width : ImgMap.Width;
        double imgH = _scene?.Height > 0 ? _scene.Height : ImgMap.Height;
        double fullOx = (imgW - fullSidePx) / 2.0;
        double fullOy = (imgH - fullSidePx) / 2.0;

        double u = fullOx + ((xx + halfPad) / totalWorld) * fullSidePx;
        double v = fullOy + (((_worldSizeS - yy) + halfPad) / totalWorld) * fullSidePx;

        return new Point(u, v);
    }

    private Point ImagePxToWorld(double u, double v)
    {
        if (_worldSizeS <= 0 || _worldRectPx.Width <= 0 || _worldRectPx.Height <= 0) return new Point(0, 0);

        double x = (u - _worldRectPx.X) / _worldRectPx.Width * _worldSizeS;
        double y = _worldSizeS - (v - _worldRectPx.Y) / _worldRectPx.Height * _worldSizeS;
        return new Point(x, y);
    }

    private void WebViewHost_MouseDown(object? sender, MouseButtonEventArgs e)
    {
        WebViewHost.Focus();
        var hostPos = e.GetPosition(WebViewHost);
        var mapPos = HostToScenePreTransform(hostPos);

        if (_overlayToolsVisible && _currentTool != OverlayToolMode.None)
        {
            HandleOverlayMouseDown(e, mapPos);
            e.Handled = true;
            return;
        }
        if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right)
        {
            StopTracking(); // Stop tracking on pan
            _isPanning = true;
            _panLastHost = e.GetPosition(WebViewHost);
            WebViewHost.CaptureMouse();
            e.Handled = true;
        }
    }

    private void WebViewHost_KeyDown(object sender, KeyEventArgs e)
    {
        if (_scene == null) return;

        bool zoomIn = e.Key == Key.Add || e.Key == Key.OemPlus;
        bool zoomOut = e.Key == Key.Subtract || e.Key == Key.OemMinus;

        if (!zoomIn && !zoomOut)
            return;

        var hostPos = Mouse.GetPosition(WebViewHost);
        if (hostPos.X < 0 || hostPos.Y < 0 ||
            hostPos.X > WebViewHost.ActualWidth ||
            hostPos.Y > WebViewHost.ActualHeight)
            return;

        double factor = zoomIn ? 1.25 : (1.0 / 1.25);

        if (zoomOut) StopTracking();
        ZoomAtHostPositionAnimated(hostPos, factor);
        e.Handled = true;
    }

    private void WebViewHost_MouseMove(object? sender, MouseEventArgs e)
    {
        var hostPos = e.GetPosition(WebViewHost);
        var mapPos = HostToScenePreTransform(hostPos);

        if (_overlayToolsVisible && _currentTool != OverlayToolMode.None)
        {
            HandleOverlayMouseMove(e, mapPos);
            e.Handled = true;
            return;
        }
        if (!_isPanning) return;

        var hostNow = e.GetPosition(WebViewHost);
        var dHost = hostNow - _panLastHost;
        _panLastHost = hostNow;

        var dScene = HostDeltaToSceneDelta(dHost);

        var m = MapTransform.Matrix;
        m.Translate(dScene.X, dScene.Y);
        MapTransform.Matrix = m;
        CenterMiniMapOnPlayer();
        e.Handled = true;
    }

    private void WebViewHost_MouseUp(object? sender, MouseButtonEventArgs e)
    {
        var hostPos = e.GetPosition(WebViewHost);
        var mapPos = HostToScenePreTransform(hostPos);

        if (_overlayToolsVisible && _currentTool != OverlayToolMode.None)
        {
            HandleOverlayMouseUp(e, mapPos);
            e.Handled = true;
            return;
        }

        if (_isPanning && (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right))
        {
            _isPanning = false;
            WebViewHost.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private bool _isAnimatingMap = false;

    // Smoothly fly to a world coordinate (x,y) with a zoom dip
    private async void CenterMapOnWorldAnimated(double targetX, double targetY, bool allowDip = true, bool fast = false, bool keepTracking = false, double? targetZoom = null)
    {
        if (_worldSizeS <= 0) return;

        if (!keepTracking) StopTracking(); // Reset any active follow-lock when manually animating somewhere else
        
        _isAnimatingMap = false; // Stop any current animation
        await Task.Delay(20);    // Give it a moment to stop
        _isAnimatingMap = true;

        var targetP = WorldToImagePx(targetX, targetY);

        var (s, offX, offY) = GetViewboxScaleAndOffset();

        double hostCx = WebViewHost.ActualWidth * 0.5;
        double hostCy = WebViewHost.ActualHeight * 0.5;

        var startM = MapTransform.Matrix;
        double startSx = Math.Abs(startM.M11) < 1e-9 ? 1 : startM.M11;
        double startSy = Math.Abs(startM.M22) < 1e-9 ? 1 : startM.M22;

        // Current world point at the center of the screen
        double startPx = ((hostCx - offX) / s - startM.OffsetX) / startSx;
        double startPy = ((hostCy - offY) / s - startM.OffsetY) / startSy;

        // Target zoom level
        double targetSx = targetZoom ?? (fast ? MAP_FOCUS_ZOOM : startSx); 
        double targetSy = targetZoom ?? (fast ? MAP_FOCUS_ZOOM : startSy);

        // Calculate distance to decide if we should do the "overview dip"
        double dx = targetP.X - startPx;
        double dy = targetP.Y - startPy;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        bool shouldDip = allowDip && dist > 500; // Only dip if allowed and target is far away

        try
        {
            int steps = fast ? 30 : 60; // Faster steps for better responsiveness
            for (int i = 0; i <= steps; i++)
            {
                if (!_isAnimatingMap) break;

                double t = i / (double)steps;
                double ease = t * t * (3 - 2 * t); // Smoothstep

                // Interpolate position
                double curPx = startPx + dx * ease;
                double curPy = startPy + dy * ease;

                // Base linear lerp of scale
                double baseSx = startSx + (targetSx - startSx) * ease;
                double baseSy = startSy + (targetSy - startSy) * ease;

                double curSx = baseSx;
                double curSy = baseSy;

                if (shouldDip)
                {
                    // Parabola for the zoom dip (peaks at 1.0 when ease=0.5)
                    double zoomDip = 4 * ease * (1 - ease); 
                    
                    // Pronounced zoom out: dip towards 0.6 scale in the middle
                    double dipScale = 0.6;
                    curSx = baseSx - (baseSx - dipScale) * zoomDip;
                    curSy = baseSy - (baseSy - dipScale) * zoomDip;

                    // Don't zoom out too far if already zoomed out
                    if (curSx > baseSx) curSx = baseSx; 
                    if (curSy > baseSy) curSy = baseSy;
                }

                var m = MapTransform.Matrix;
                m.M11 = curSx;
                m.M22 = curSy;
                m.OffsetX = (hostCx - offX) / s - curSx * curPx;
                m.OffsetY = (hostCy - offY) / s - curSy * curPy;
                MapTransform.Matrix = m;

                if (i % 2 == 0) // Update UI every other frame
                {
                    RefreshAllOverlayScales();
                    RefreshMonumentOverlayPositions();
                }

                await Task.Delay(16);
            }

            // Final snap
            RefreshAllOverlayScales();
            RefreshMonumentOverlayPositions();
            CenterMiniMapOnPlayer();
        }
        catch { }
        finally
        {
            _isAnimatingMap = false;
        }
    }

    // Weltpunkt (x,y) in die Mitte des sichtbaren Bereichs schieben - Zoom bleibt unveraendert
    private void CenterMapOnWorld(double x, double y)
    {
        if (_worldSizeS <= 0) return;

        var p = WorldToImagePx(x, y);
        var (s, offX, offY) = GetViewboxScaleAndOffset();

        double hostCx = WebViewHost.ActualWidth * 0.5;
        double hostCy = WebViewHost.ActualHeight * 0.5;

        var m = MapTransform.Matrix;
        double sx = Math.Abs(m.M11) < 1e-9 ? 1 : m.M11;
        double sy = Math.Abs(m.M22) < 1e-9 ? 1 : m.M22;

        m.OffsetX = (hostCx - offX) / s - sx * p.X;
        m.OffsetY = (hostCy - offY) / s - sy * p.Y;

        MapTransform.Matrix = m;
        RefreshAllOverlayScales();
        RefreshMonumentOverlayPositions();
    }
 
    private void BtnResetMap_Click(object sender, RoutedEventArgs e)
    {
        ResetMapZoom();
        AppendLog("Map reset.");
    }
 
    private void ResetMapZoom()
    {
        if (_worldSizeS > 0)
        {
            CenterMapOnWorldAnimated(_worldSizeS / 2, _worldSizeS / 2, allowDip: false, fast: false, targetZoom: MAP_DEFAULT_ZOOM);
        }
        else
        {
            MapTransform.Matrix = new Matrix(MAP_DEFAULT_ZOOM, 0, 0, MAP_DEFAULT_ZOOM, 0, 0);
            RefreshAllOverlayScales();
            RefreshMonumentOverlayPositions();
            RefreshUserOverlayIcons();
            CenterMiniMapOnPlayer();
        }
    }
}
