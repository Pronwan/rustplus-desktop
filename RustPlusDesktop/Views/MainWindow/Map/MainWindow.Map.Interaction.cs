using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private const double MAP_DEFAULT_ZOOM = 1.18;
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
    private Point WorldToImagePx(double x, double y)
    {
        if (_worldSizeS <= 0 || _worldRectPx.Width <= 0 || _worldRectPx.Height <= 0)
            return new Point(0, 0);

        if (_isShowingDeepSeaMap)
        {
            var (minX, maxX, minY, maxY) = GetDeepSeaWorldBox();

            double xx = Math.Clamp(x, minX, maxX);
            double yy = Math.Clamp(y, minY, maxY);

            double u = _worldRectPx.X + ((xx - minX) / (maxX - minX)) * _worldRectPx.Width;
            double v = _worldRectPx.Y + (1.0 - (yy - minY) / (maxY - minY)) * _worldRectPx.Height;

            return new Point(u, v);
        }

        double padWorld = GetCurrentMapPaddingWorld();
        double totalWorld = _worldSizeS + padWorld;
        double halfPad = padWorld * 0.5;

        double xxNormal = Math.Clamp(x, -halfPad, _worldSizeS + halfPad);
        double yyNormal = Math.Clamp(y, -halfPad, _worldSizeS + halfPad);

        double fullSidePx = _worldRectPx.Width * (totalWorld / _worldSizeS);

        double imgWNormal = _scene?.Width > 0 ? _scene.Width : ImgMap.Width;
        double imgHNormal = _scene?.Height > 0 ? _scene.Height : ImgMap.Height;
        double fullOx = (imgWNormal - fullSidePx) / 2.0;
        double fullOy = (imgHNormal - fullSidePx) / 2.0;

        double uNormal = fullOx + ((xxNormal + halfPad) / totalWorld) * fullSidePx;
        double vNormal = fullOy + (((_worldSizeS - yyNormal) + halfPad) / totalWorld) * fullSidePx;

        return new Point(uNormal, vNormal);
    }

    private Point ImagePxToWorld(double u, double v)
    {
        if (_worldSizeS <= 0 || _worldRectPx.Width <= 0 || _worldRectPx.Height <= 0) return new Point(0, 0);

        if (_isShowingDeepSeaMap)
        {
            var (minX, maxX, minY, maxY) = GetDeepSeaWorldBox();

            double x = minX + ((u - _worldRectPx.X) / _worldRectPx.Width) * (maxX - minX);
            double y = minY + (1.0 - (v - _worldRectPx.Y) / _worldRectPx.Height) * (maxY - minY);
            return new Point(x, y);
        }

        double xNormal = (u - _worldRectPx.X) / _worldRectPx.Width * _worldSizeS;
        double yNormal = _worldSizeS - (v - _worldRectPx.Y) / _worldRectPx.Height * _worldSizeS;
        return new Point(xNormal, yNormal);
    }

    private void WebViewHost_MouseDown(object? sender, MouseButtonEventArgs e)
    {
        WebViewHost.Focus();
        var hostPos = e.GetPosition(WebViewHost);
        var mapPos = HostToScenePreTransform(hostPos);

        // Navigation is always available, even while a draw tool is armed.
        // Middle always pans. Right first tries an overlay delete; if nothing was hit, it pans too.
        bool rightPansAfterDelete = e.ChangedButton == MouseButton.Right
            && !(_overlayToolsVisible && TryHandleOverlayRightClick(mapPos));
        // Space held turns left-drag into a temporary hand.
        bool spacePan = e.ChangedButton == MouseButton.Left && _spacePanHeld;

        if (e.ChangedButton == MouseButton.Middle || rightPansAfterDelete || spacePan)
        {
            StopTracking(); // Stop tracking on pan
            HidePanHint();  // they found it — the first-run tip has done its job
            _isPanning = true;
            _panLastHost = hostPos;
            WebViewHost.CaptureMouse();
            e.Handled = true;
            return;
        }

        // The right-click delete above already consumed a hit; nothing more to do.
        if (e.ChangedButton == MouseButton.Right)
        {
            e.Handled = true;
            return;
        }

        // Left button uses the active tool (draw/text/icon/erase), or drags an existing
        // element when the Select tool (None) is active.
        if (_overlayToolsVisible && e.ChangedButton == MouseButton.Left)
        {
            HandleOverlayMouseDown(e, mapPos);
            e.Handled = true;
        }
    }

    private void WebViewHost_KeyDown(object sender, KeyEventArgs e)
    {
        // While typing an inline map label, let the text box handle every key.
        if (_isEditingOverlayText) return;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        // Group the current map selection.
        if (ctrl && e.Key == Key.G && _overlayToolsVisible) { GroupMapSelection(); e.Handled = true; return; }

        // Undo / redo of my map drawings.
        if (ctrl && e.Key == Key.Z) { OverlayUndo(); e.Handled = true; return; }
        if (ctrl && (e.Key == Key.Y || (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)))
        {
            OverlayRedo(); e.Handled = true; return;
        }

        // Hold Space for a temporary hand — left-drag pans without dropping the active tool.
        if (e.Key == Key.Space && !_spacePanHeld)
        {
            _spacePanHeld = true;
            ApplyToolCursor();
            e.Handled = true;
            return;
        }

        // Single-key tool shortcuts — only while the drawing tools are open and no modifier is held.
        if (_overlayToolsVisible && Keyboard.Modifiers == ModifierKeys.None)
        {
            OverlayToolMode? pick = e.Key switch
            {
                Key.D1 or Key.NumPad1 => OverlayToolMode.None,   // Select / navigate
                Key.D2 or Key.NumPad2 => OverlayToolMode.Draw,   // Pen
                Key.D3 or Key.NumPad3 => OverlayToolMode.Line,
                Key.D4 or Key.NumPad4 => OverlayToolMode.Arrow,
                Key.D5 or Key.NumPad5 => OverlayToolMode.Box,    // Rectangle
                Key.D6 or Key.NumPad6 => OverlayToolMode.Circle,
                Key.D7 or Key.NumPad7 => OverlayToolMode.Route,
                Key.D8 or Key.NumPad8 => OverlayToolMode.Text,
                Key.D9 or Key.NumPad9 => OverlayToolMode.Icon,   // Marker
                Key.D0 or Key.NumPad0 => OverlayToolMode.Erase,
                _ => (OverlayToolMode?)null
            };
            if (pick is OverlayToolMode mode)
            {
                SelectOverlayTool(mode);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                CancelActiveOverlayDraw();
                DeselectElement();
                SelectOverlayTool(OverlayToolMode.None);
                UpdateOptionsPanelVisibility();
                e.Handled = true;
                return;
            }
            if (e.Key is Key.Delete or Key.Back && _selectedElement != null)
            {
                DeleteSelectedElement();
                e.Handled = true;
                return;
            }
        }

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

    private void WebViewHost_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _spacePanHeld = false;
            if (_isPanning)
            {
                _isPanning = false;
                WebViewHost.ReleaseMouseCapture();
            }
            ApplyToolCursor();
            e.Handled = true;
        }
    }

    private void WebViewHost_MouseMove(object? sender, MouseEventArgs e)
    {
        var hostPos = e.GetPosition(WebViewHost);
        var mapPos = HostToScenePreTransform(hostPos);

        // Panning always wins, even while a tool is armed.
        if (_isPanning)
        {
            var dHost = hostPos - _panLastHost;
            _panLastHost = hostPos;

            var dScene = HostDeltaToSceneDelta(dHost);

            var m = MapTransform.Matrix;
            m.Translate(dScene.X, dScene.Y);
            MapTransform.Matrix = m;
            CenterMiniMapOnPlayer();
            e.Handled = true;
            return;
        }

        // Tool stroke/erase, or dragging an element under the Select tool.
        if (_overlayToolsVisible)
        {
            HandleOverlayMouseMove(e, mapPos);
            if (_currentTool != OverlayToolMode.None || _draggingElement != null)
                e.Handled = true;
        }
    }

    private void WebViewHost_MouseUp(object? sender, MouseButtonEventArgs e)
    {
        var hostPos = e.GetPosition(WebViewHost);
        var mapPos = HostToScenePreTransform(hostPos);

        // End a pan started by middle, right, or space+left.
        if (_isPanning)
        {
            _isPanning = false;
            WebViewHost.ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        if (_overlayToolsVisible)
        {
            HandleOverlayMouseUp(e, mapPos);
            e.Handled = true;
        }
    }

    private bool _isAnimatingMap = false;

    // Smoothly fly to a world coordinate (x,y) with a zoom dip
    internal async void CenterMapOnWorldAnimated(double targetX, double targetY, bool allowDip = true, bool fast = false, bool keepTracking = false, double? targetZoom = null)
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
    private void CenterMapOnWorld(double x, double y, bool keepTracking = false)
    {
        // Use animated centering for manual/one-shot actions to avoid conflict with the follow loop
        double curZoom = MapTransform.Matrix.M11;
        CenterMapOnWorldAnimated(x, y, allowDip: false, fast: true, keepTracking: keepTracking, targetZoom: curZoom);
    }

    private void CenterMapOnWorldInstant(double x, double y)
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
            CenterMapOnWorldAnimated(_worldSizeS / 2, _worldSizeS / 2, allowDip: false, fast: false, keepTracking: true, targetZoom: MAP_DEFAULT_ZOOM);
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

    private bool _isSmoothingFollow = false;
    private double? _camTargetX, _camTargetY;
    private double? _currentCamX, _currentCamY;

    private void InitSmoothFollowLoop()
    {
        CompositionTarget.Rendering += OnMapRendering;
    }

    private void OnMapRendering(object? sender, EventArgs e)
    {
        if (!_camTargetX.HasValue || !_camTargetY.HasValue || _isAnimatingMap) return;
        if (!_vm.IsFollowing && !_trackingEntityId.HasValue) 
        {
            _camTargetX = _camTargetY = null;
            _currentCamX = _currentCamY = null;
            _isSmoothingFollow = false;
            return;
        }

        // Initialize current position if not set
        if (!_currentCamX.HasValue || !_currentCamY.HasValue)
        {
            var (s, offX, offY) = GetViewboxScaleAndOffset();
            var m = MapTransform.Matrix;
            if (Math.Abs(m.M11) < 1e-4) return; // Wait for valid matrix

            _currentCamX = ((WebViewHost.ActualWidth * 0.5 - offX) / s - m.OffsetX) / m.M11;
            _currentCamY = ((WebViewHost.ActualHeight * 0.5 - offY) / s - m.OffsetY) / m.M22;
            
            // Safety check for Pampa jumps
            if (double.IsNaN(_currentCamX.Value) || Math.Abs(_currentCamX.Value) > 20000) _currentCamX = _camTargetX;
            if (double.IsNaN(_currentCamY.Value) || Math.Abs(_currentCamY.Value) > 20000) _currentCamY = _camTargetY;
        }

        // Spring-damper lerp for premium feel
        double lerpFactor = 0.08; 
        _currentCamX = _currentCamX.Value + (_camTargetX.Value - _currentCamX.Value) * lerpFactor;
        _currentCamY = _currentCamY.Value + (_camTargetY.Value - _currentCamY.Value) * lerpFactor;

        // Apply to Main Map
        CenterMapOnWorldInstant(_currentCamX.Value, _currentCamY.Value);

        // Sync Mini-Map (ensure _isSmoothingFollow is true before calling)
        _isSmoothingFollow = true;
        CenterMiniMapOnPlayer();
    }

    // Smoothly transition the map center to a new world coordinate without zooming or dipping.
    // Useful for following moving entities during polling.
    private void CenterMapOnWorldSmooth(double targetX, double targetY, int durationMs)
    {
        // Wenn wir noch nicht interpolieren oder das Ziel sehr weit weg ist (Teleport/First Click),
        // setzen wir die aktuelle Position sofort auf das Ziel, um den "Zucker" aus der Pampa zu vermeiden.
        if (!_currentCamX.HasValue || !_currentCamY.HasValue)
        {
            _currentCamX = targetX;
            _currentCamY = targetY;
        }
        else
        {
            double dist = Math.Sqrt(Math.Pow(targetX - _currentCamX.Value, 2) + Math.Pow(targetY - _currentCamY.Value, 2));
            if (dist > 500) // Großer Sprung -> sofort synchronisieren
            {
                _currentCamX = targetX;
                _currentCamY = targetY;
            }
        }

        _camTargetX = targetX;
        _camTargetY = targetY;
    }
}
