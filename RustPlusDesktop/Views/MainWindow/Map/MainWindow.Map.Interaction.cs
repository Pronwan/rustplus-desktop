using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private const double MAP_DEFAULT_ZOOM = 1.18;
    private const double MAP_FOCUS_ZOOM   = 6.0;
    private const double MAP_MIN_ZOOM     = 0.64; // Allows 2 more zoom out levels below 1.0 (1.0 -> 0.8 -> 0.64)
    private const double MAP_MAX_ZOOM     = 20.0;
 
    private Point _panLastHost;
    private readonly Queue<(Point pos, DateTime time)> _panHistory = new();
    private bool _isFlinging = false;
    private bool _isSpringingBack = false;

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
        
        // Clamp zoom level between MAP_MIN_ZOOM and MAP_MAX_ZOOM
        if (newScale < MAP_MIN_ZOOM) factor = MAP_MIN_ZOOM / m.M11;
        if (newScale > MAP_MAX_ZOOM) factor = MAP_MAX_ZOOM / m.M11;

        m.ScaleAt(factor, factor, pivot.X, pivot.Y);
        MapTransform.Matrix = m;

        RefreshAllOverlayScales();
        RefreshMonumentOverlayPositions();
        RefreshUserOverlayIcons();
        CenterMiniMapOnPlayer();
    }

    /// <summary>
    /// Immediately interrupts and cancels all in-flight zoom animations, kinetic flings,
    /// and elastic spring-backs so pan navigation is completely fluid and unblocked.
    /// </summary>
    private void StopAllMapAnimations()
    {
        _isZooming = false;
        _zoomAnimationTarget = -1;
        _isFlinging = false;
        _isSpringingBack = false;
        _isAnimatingMap = false;
    }

    private double _zoomAnimationTarget = -1;
    private Point _zoomAnimationHostPivot;
    private Point _zoomAnimationScenePivot;
    private bool _isZooming = false;

    private async void ZoomAtHostPositionAnimated(Point hostPos, double factor)
    {
        if (_scene == null) return;

        // If user is actively panning, apply immediate direct zoom so animation never fights pan navigation
        if (_isPanning)
        {
            StopAllMapAnimations();
            ZoomAtHostPosition(hostPos, factor);
            return;
        }

        double curScale = MapTransform.Matrix.M11;
        double baseScale = (_isZooming && _zoomAnimationTarget > 0) ? _zoomAnimationTarget : curScale;
        double targetScale = Math.Clamp(baseScale * factor, MAP_MIN_ZOOM, MAP_MAX_ZOOM);

        _zoomAnimationTarget = targetScale;
        _zoomAnimationHostPivot = hostPos;
        _zoomAnimationScenePivot = HostToScenePreTransform(hostPos);

        if (_isZooming) return;
        _isZooming = true;

        var lastTime = DateTime.UtcNow;
        int frame = 0;

        try
        {
            while (Math.Abs(MapTransform.Matrix.M11 - _zoomAnimationTarget) > 0.001)
            {
                await Task.Delay(14);
                // Immediately yield control if user started panning or requested stop
                if (_scene == null || !_isZooming || _isPanning) break;

                var now = DateTime.UtcNow;
                double dt = Math.Min(0.05, Math.Max(0.001, (now - lastTime).TotalSeconds));
                lastTime = now;

                var (s, offX, offY) = GetViewboxScaleAndOffset();
                var curM = MapTransform.Matrix;
                double curS = curM.M11;

                // Continuous exponential approach curve
                double easeFactor = 1.0 - Math.Pow(0.00008, dt);
                double nextScale = curS + (_zoomAnimationTarget - curS) * easeFactor;

                if (Math.Abs(_zoomAnimationTarget - nextScale) < 0.001)
                {
                    nextScale = _zoomAnimationTarget;
                }

                // Double-check pan state before applying any offset adjustments
                if (_isPanning || !_isZooming) break;

                // Perfectly anchor the scene pivot under the cursor with zero jitter
                curM.M11 = nextScale;
                curM.M22 = nextScale;
                curM.OffsetX = (_zoomAnimationHostPivot.X - offX) / s - nextScale * _zoomAnimationScenePivot.X;
                curM.OffsetY = (_zoomAnimationHostPivot.Y - offY) / s - nextScale * _zoomAnimationScenePivot.Y;
                MapTransform.Matrix = curM;

                frame++;
                if (frame % 3 == 0)
                {
                    RefreshAllOverlayScales();
                    RefreshMonumentOverlayPositions();
                }
            }

            // Settle final layout only if user hasn't taken over with manual panning
            if (!_isPanning)
            {
                RefreshAllOverlayScales();
                RefreshMonumentOverlayPositions();
                RefreshUserOverlayIcons();
                CenterMiniMapOnPlayer();
            }
        }
        catch { }
        finally
        {
            _isZooming = false;
        }
    }

    /// <summary>
    /// Computes the allowed matrix offset bounds with generous ocean padding so the map can be
    /// navigated freely at all zoom levels while keeping elastic boundary physics active.
    /// </summary>
    private (double minOffsetX, double maxOffsetX, double minOffsetY, double maxOffsetY) GetAllowedMatrixOffsetBounds()
    {
        if (_scene == null || WebViewHost == null) return (0, 0, 0, 0);

        var (s, offX, offY) = GetViewboxScaleAndOffset();
        double hostW = Math.Max(1, WebViewHost.ActualWidth);
        double hostH = Math.Max(1, WebViewHost.ActualHeight);

        double contentW = _scene.ActualWidth > 0 ? _scene.ActualWidth : (_scene.Width > 0 ? _scene.Width : 2048);
        double contentH = _scene.ActualHeight > 0 ? _scene.ActualHeight : (_scene.Height > 0 ? _scene.Height : 2048);

        var m = MapTransform.Matrix;
        double curScaleX = Math.Max(0.1, m.M11);
        double curScaleY = Math.Max(0.1, m.M22);

        double scaledWHost = s * curScaleX * contentW;
        double scaledHHost = s * curScaleY * contentH;

        // Generous breathing room: allow panning freely across the map, deep sea, and offshore ocean
        double padX = Math.Max(hostW * 0.75, scaledWHost * 0.45);
        double padY = Math.Max(hostH * 0.75, scaledHHost * 0.45);

        double minLeftHost, maxLeftHost;
        if (scaledWHost <= hostW)
        {
            double centerLeft = (hostW - scaledWHost) * 0.5;
            minLeftHost = centerLeft - padX * 0.75;
            maxLeftHost = centerLeft + padX * 0.75;
        }
        else
        {
            minLeftHost = hostW - scaledWHost - padX;
            maxLeftHost = padX;
        }

        double minTopHost, maxTopHost;
        if (scaledHHost <= hostH)
        {
            double centerTop = (hostH - scaledHHost) * 0.5;
            minTopHost = centerTop - padY * 0.75;
            maxTopHost = centerTop + padY * 0.75;
        }
        else
        {
            minTopHost = hostH - scaledHHost - padY;
            maxTopHost = padY;
        }

        double minOffsetX = (minLeftHost - offX) / s;
        double maxOffsetX = (maxLeftHost - offX) / s;
        double minOffsetY = (minTopHost - offY) / s;
        double maxOffsetY = (maxTopHost - offY) / s;

        return (minOffsetX, maxOffsetX, minOffsetY, maxOffsetY);
    }

    /// <summary>
    /// Glides the map smoothly following mouse release velocity with friction damping and boundary resistance.
    /// </summary>
    private async void StartKineticFling(Vector startVel)
    {
        if (_scene == null || _isPanning || _isAnimatingMap) return;
        _isFlinging = true;
        var vel = startVel;
        var lastTime = DateTime.UtcNow;

        try
        {
            while (_isFlinging && !_isPanning && !_isAnimatingMap && vel.Length > 12)
            {
                await Task.Delay(16);
                if (!_isFlinging || _isPanning || _isAnimatingMap || _scene == null) break;

                var now = DateTime.UtcNow;
                double dt = (now - lastTime).TotalSeconds;
                lastTime = now;
                if (dt <= 0 || dt > 0.1) dt = 0.016;

                var (minX, maxX, minY, maxY) = GetAllowedMatrixOffsetBounds();
                var curM = MapTransform.Matrix;

                bool outOfBoundsX = curM.OffsetX < minX || curM.OffsetX > maxX;
                bool outOfBoundsY = curM.OffsetY < minY || curM.OffsetY > maxY;

                if (outOfBoundsX || outOfBoundsY)
                {
                    vel *= Math.Pow(0.0001, dt);
                    if (vel.Length < 35)
                    {
                        break;
                    }
                }
                else
                {
                    vel *= Math.Pow(0.006, dt);
                }

                if (_isPanning || !_isFlinging) break;

                var dHost = vel * dt;
                var dScene = HostDeltaToSceneDelta(dHost);

                var m = MapTransform.Matrix;
                m.Translate(dScene.X, dScene.Y);
                MapTransform.Matrix = m;
                CenterMiniMapOnPlayer();
            }
        }
        catch { }
        finally
        {
            _isFlinging = false;
            if (!_isPanning && !_isSpringingBack)
            {
                var (minX, maxX, minY, maxY) = GetAllowedMatrixOffsetBounds();
                var curM = MapTransform.Matrix;
                if (curM.OffsetX < minX || curM.OffsetX > maxX || curM.OffsetY < minY || curM.OffsetY > maxY)
                {
                    SpringBackToBoundsAnimated();
                }
            }
        }
    }

    /// <summary>
    /// Smoothly springs back to within allowed matrix offset bounds when outside (elastic rebound).
    /// </summary>
    private async void SpringBackToBoundsAnimated()
    {
        if (_scene == null || _isPanning || _isAnimatingMap || _isSpringingBack) return;

        var (minX, maxX, minY, maxY) = GetAllowedMatrixOffsetBounds();
        var curM = MapTransform.Matrix;

        double targetX = Math.Clamp(curM.OffsetX, minX, maxX);
        double targetY = Math.Clamp(curM.OffsetY, minY, maxY);

        double diffX = targetX - curM.OffsetX;
        double diffY = targetY - curM.OffsetY;

        if (Math.Abs(diffX) < 1.0 && Math.Abs(diffY) < 1.0)
            return;

        _isSpringingBack = true;
        try
        {
            for (int i = 0; i < 24; i++)
            {
                if (!_isSpringingBack || _isPanning || _isAnimatingMap || _scene == null) break;

                var m = MapTransform.Matrix;
                double dX = (targetX - m.OffsetX) * 0.18;
                double dY = (targetY - m.OffsetY) * 0.18;

                if (Math.Abs(targetX - m.OffsetX) < 0.5 && Math.Abs(targetY - m.OffsetY) < 0.5)
                {
                    m.OffsetX = targetX;
                    m.OffsetY = targetY;
                    MapTransform.Matrix = m;
                    CenterMiniMapOnPlayer();
                    break;
                }

                if (_isPanning || !_isSpringingBack) break;

                m.OffsetX += dX;
                m.OffsetY += dY;
                MapTransform.Matrix = m;
                CenterMiniMapOnPlayer();

                await Task.Delay(16);
            }
        }
        catch { }
        finally
        {
            _isSpringingBack = false;
        }
    }

    private void WebViewHost_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_isMap3DActive) return;
        if (_scene == null) return;

        // Smooth continuous exponential zoom scaling based on wheel ticks
        double steps = e.Delta / 120.0;
        double zoom = Math.Pow(1.22, steps);
        var hostPos = e.GetPosition(WebViewHost);

        if (e.Delta < 0) StopTracking(); // Only stop tracking on zoom OUT

        if (_isPanning)
        {
            // During active pan navigation, zoom directly without animation so pan is completely uninterrupted
            StopAllMapAnimations();
            ZoomAtHostPosition(hostPos, zoom);
        }
        else
        {
            ZoomAtHostPositionAnimated(hostPos, zoom);
        }
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

        if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y))
            return new Point(0, 0);

        double padWorld = GetCurrentMapPaddingWorld();
        double totalWorld = _worldSizeS + padWorld;
        double halfPad = padWorld * 0.5;

        double fullSidePx = _worldRectPx.Width * (totalWorld / _worldSizeS);

        double imgWNormal = _scene?.Width > 0 ? _scene.Width : ImgMap.Width;
        double imgHNormal = _scene?.Height > 0 ? _scene.Height : ImgMap.Height;
        double fullOx = (imgWNormal - fullSidePx) / 2.0;
        double fullOy = (imgHNormal - fullSidePx) / 2.0;

        double uNormal = fullOx + ((x + halfPad) / totalWorld) * fullSidePx;
        double vNormal = fullOy + (((_worldSizeS - y) + halfPad) / totalWorld) * fullSidePx;

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
        // The embedded 3D browser owns input; do not steal its focus or mouse capture.
        if (_isMap3DActive) return;
        if (!IsMapLoaded) return;
        StopAllMapAnimations();
        WebViewHost.Focus();
        var hostPos = e.GetPosition(WebViewHost);
        var mapPos = HostToScenePreTransform(hostPos);

        // Navigation is always available, even while a draw tool is armed.
        // Middle always pans. Right first tries an overlay delete; if nothing was hit, it pans too.
        bool rightPansAfterDelete = e.ChangedButton == MouseButton.Right
            && !(_overlayToolsVisible && TryHandleOverlayRightClick(mapPos));
        // Space held turns left-drag into a temporary hand.
        bool spacePan = e.ChangedButton == MouseButton.Left && _spacePanHeld;
        // Standard left-click drag pans the map when drawing tools are not in an active shape mode.
        bool leftPan = e.ChangedButton == MouseButton.Left && (!_overlayToolsVisible || _currentTool == OverlayToolMode.None);

        if (e.ChangedButton == MouseButton.Middle || rightPansAfterDelete || spacePan || leftPan)
        {
            StopTracking(); // Stop tracking on pan
            HidePanHint();  // they found it — the first-run tip has done its job
            _isPanning = true;
            _panLastHost = hostPos;
            _panHistory.Clear();
            _panHistory.Enqueue((hostPos, DateTime.UtcNow));
            WebViewHost.CaptureMouse();
            WebViewHost.Cursor = MapCursors.Grabbing;
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
        if (_isMap3DActive) return;
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
            if (!IsMapLoaded) return;
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

        if (_scene == null || !IsMapLoaded) return;

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
        if (_isMap3DActive) return;
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
        if (_isMap3DActive) return;
        if (!IsMapLoaded)
        {
            if (WebViewHost != null && WebViewHost.Cursor != Cursors.Arrow)
            {
                WebViewHost.Cursor = Cursors.Arrow;
            }
            return;
        }

        var hostPos = e.GetPosition(WebViewHost);
        var mapPos = HostToScenePreTransform(hostPos);

        // Panning always wins, even while a tool is armed.
        if (_isPanning)
        {
            StopAllMapAnimations();

            if (WebViewHost.Cursor != MapCursors.Grabbing)
            {
                WebViewHost.Cursor = MapCursors.Grabbing;
            }

            var now = DateTime.UtcNow;
            _panHistory.Enqueue((hostPos, now));
            while (_panHistory.Count > 0 && (now - _panHistory.Peek().time).TotalMilliseconds > 120)
            {
                _panHistory.Dequeue();
            }

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
        if (_isMap3DActive) return;
        var hostPos = e.GetPosition(WebViewHost);
        var mapPos = HostToScenePreTransform(hostPos);

        // End a pan started by left, middle, right, or space+left.
        if (_isPanning)
        {
            _isPanning = false;
            WebViewHost.ReleaseMouseCapture();
            ApplyToolCursor();

            Vector releaseVelocity = new Vector(0, 0);
            if (_panHistory.Count >= 2)
            {
                var oldest = _panHistory.Peek();
                var newest = (hostPos, DateTime.UtcNow);
                double dt = (newest.Item2 - oldest.time).TotalSeconds;
                if (dt > 0.015 && dt < 0.25)
                {
                    var dH = newest.hostPos - oldest.pos;
                    releaseVelocity = dH / dt; // Host px/s
                }
            }
            _panHistory.Clear();

            if (releaseVelocity.Length > 80)
            {
                StartKineticFling(releaseVelocity);
            }
            else
            {
                var (minX, maxX, minY, maxY) = GetAllowedMatrixOffsetBounds();
                var curM = MapTransform.Matrix;
                if (curM.OffsetX < minX || curM.OffsetX > maxX || curM.OffsetY < minY || curM.OffsetY > maxY)
                {
                    SpringBackToBoundsAnimated();
                }
            }

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
        // Translation is carried by the scene transform; marker sizes and map-space positions are unchanged.
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
