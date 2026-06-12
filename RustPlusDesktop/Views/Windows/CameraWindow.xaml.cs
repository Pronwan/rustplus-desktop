using RustPlusDesk.Services;
using RustPlusDesk.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RustPlusDesk.Views
{


    public partial class CameraWindow : Window
    {
        private readonly RustPlusClientReal _real;
        private readonly string _cameraId;
        private bool _running;
        private bool _mouseLookSupported;
        private bool _crosshairSupported;
        private bool _movementSupported;
        private DateTime _lastFrameRenderTime = DateTime.MinValue;
        private int _fpsLimit = 2;
        private System.Diagnostics.Process? _nodeProcess;
        private int _frameCount = 0;
        
        private static readonly string[] EnvWords = { "tree", "bush", "ore", "stone", "hemp", "barrel", "crate", "rock", "node", "stump", "collectible" };

        // candidate type ids that usually mean "player" (adjust after looking at the log)
       

        // cache team names (by name) and ids (by steamId) to color labels
        private readonly HashSet<string> _teamNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<ulong> _teamSteamIds = new();

        // candidate "player" type(s) based on your log: 2
        private static readonly HashSet<int> PlayerTypeIds = new() { 2 };


        private async Task RefreshTeamAsync()
        {
            try
            {
                var team = await _real.GetTeamInfoAsync();
                _teamNames.Clear();
                _teamSteamIds.Clear();
                if (team?.Members != null)
                {
                    foreach (var m in team.Members)
                    {
                        if (!string.IsNullOrWhiteSpace(m.Name)) _teamNames.Add(m.Name!);
                        if (m.SteamId != 0) _teamSteamIds.Add(m.SteamId);
                    }
                }
            }
            catch { /* ignore */ }
        }

        //------- FORMER DRAW METHOD FOR OVERLAY ELLYPSES -------
        //  private static (Brush fill, Brush stroke, double sizePx, bool showLabel) StyleFor(CameraEntity e)
        //   {
        //     Heuristik:
        //   if (e.IsPlayer)
        //         return (Brushes.LimeGreen, Brushes.Black, 10, true);

        // Beispiele für andere Typen, falls du sie später mappen willst:
        // type==3 -> Tiere, type==4 -> Turret (nur Beispiele!):
        //     if (e.Type == 4) // Turret?
        //     return (new SolidColorBrush(Color.FromArgb(220, 30, 144, 255)), Brushes.Black, 9, true); // blau
        //    if (e.Type == 3) // Tier/NPC?
        //        return (new SolidColorBrush(Color.FromArgb(220, 255, 140, 0)), Brushes.Black, 8, true);  // orange

        // Default: Umwelt → klein & halbtransparent, ohne Label
        //    return (new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), Brushes.Transparent, 6, true);
        //  }


        public CameraWindow(RustPlusClientReal real, string cameraId)
        {
            InitializeComponent();
            _real = real;
            _cameraId = cameraId;
            Title = cameraId;
            _real.CameraEntities += OnCameraEntities;
            TxtTitle.Text = cameraId;

            // Hook mouse events for PTZ pan/tilt drag
            Img.MouseDown += Img_MouseDown;
            Img.MouseMove += Img_MouseMove;
            Img.MouseUp += Img_MouseUp;
            Img.MouseLeave += Img_MouseLeave;

            // Hook button press-and-hold events for continuous steering/actions
            BtnCamUp.PreviewMouseDown += (_, __) => {
                if (_movementSupported) StartContinuousInput(CameraButtons.Forward, 0f, 0f);
                else if (_mouseLookSupported) StartContinuousInput(CameraButtons.None, 0f, -5f);
            };
            BtnCamUp.PreviewMouseUp += (_, __) => StopContinuousInput();
            BtnCamUp.MouseLeave += (_, __) => StopContinuousInput();

            BtnCamDown.PreviewMouseDown += (_, __) => {
                if (_movementSupported) StartContinuousInput(CameraButtons.Backward, 0f, 0f);
                else if (_mouseLookSupported) StartContinuousInput(CameraButtons.None, 0f, 5f);
            };
            BtnCamDown.PreviewMouseUp += (_, __) => StopContinuousInput();
            BtnCamDown.MouseLeave += (_, __) => StopContinuousInput();

            BtnCamLeft.PreviewMouseDown += (_, __) => {
                if (_movementSupported) StartContinuousInput(CameraButtons.Left, 0f, 0f);
                else if (_mouseLookSupported) StartContinuousInput(CameraButtons.None, -5f, 0f);
            };
            BtnCamLeft.PreviewMouseUp += (_, __) => StopContinuousInput();
            BtnCamLeft.MouseLeave += (_, __) => StopContinuousInput();

            BtnCamRight.PreviewMouseDown += (_, __) => {
                if (_movementSupported) StartContinuousInput(CameraButtons.Right, 0f, 0f);
                else if (_mouseLookSupported) StartContinuousInput(CameraButtons.None, 5f, 0f);
            };
            BtnCamRight.PreviewMouseUp += (_, __) => StopContinuousInput();
            BtnCamRight.MouseLeave += (_, __) => StopContinuousInput();

            BtnCamJump.PreviewMouseDown += (_, __) => StartContinuousInput(CameraButtons.Jump, 0f, 0f);
            BtnCamJump.PreviewMouseUp += (_, __) => StopContinuousInput();
            BtnCamJump.MouseLeave += (_, __) => StopContinuousInput();

            BtnCamDuck.PreviewMouseDown += (_, __) => StartContinuousInput(CameraButtons.Duck, 0f, 0f);
            BtnCamDuck.PreviewMouseUp += (_, __) => StopContinuousInput();
            BtnCamDuck.MouseLeave += (_, __) => StopContinuousInput();

            BtnCamFire.PreviewMouseDown += (_, __) => {
                CrosshairGrid.Visibility = Visibility.Visible;
                StartContinuousInput(CameraButtons.FirePrimary, 0f, 0f);
            };
            BtnCamFire.PreviewMouseUp += (_, __) => {
                StopContinuousInput();
                if (!_crosshairSupported) CrosshairGrid.Visibility = Visibility.Collapsed;
            };
            BtnCamFire.MouseLeave += (_, __) => {
                StopContinuousInput();
                if (!_crosshairSupported) CrosshairGrid.Visibility = Visibility.Collapsed;
            };

            _real.CameraControlFlagsChanged += OnCameraControlFlagsChanged;

            // FPS Dropdown
            CmbFps.SelectionChanged += (_, __) => ApplyFps();

            Loaded += (_, __) =>
            {
                ApplyFps();
                _running = true;
                Overlay.SizeChanged += (_, __2) => DrawOverlay(); Img.SizeChanged += (_, __2) => DrawOverlay();
            
                _ = RefreshTeamAsync();

                // Start persistent stream using the Node.js process
                _nodeProcess = _real.StartPersistentCameraStream(_cameraId, OnNodeLineReceived, OnNodeErrorReceived);

                // Thumbnails für diese Kamera pausieren (im MainWindow hast du _camBusy)
                if (Owner is MainWindow mw) mw._camBusy.Add(_cameraId);
            };

            Closed += (_, __) =>
            {
                _running = false;
                try
                {
                    _nodeProcess?.Kill();
                    _nodeProcess?.Dispose();
                }
                catch { }
                if (Owner is MainWindow mw) mw._camBusy.Remove(_cameraId);
            };
           
        }

        private IReadOnlyList<CameraEntity> _lastEnts = Array.Empty<CameraEntity>();
        private int _lastW = 160, _lastH = 90; private double _lastVFovDeg = 65;

        private void OnCameraEntities(string camId, double vFovDeg, int w, int h, List<CameraEntity> ents)
        {
            if (!string.Equals(camId, _cameraId, StringComparison.OrdinalIgnoreCase)) return;
            _lastEnts = ents; _lastW = (w > 0 ? w : _lastW); _lastH = (h > 0 ? h : _lastH); _lastVFovDeg = (vFovDeg > 0 ? vFovDeg : _lastVFovDeg);

            // Diagnose
            System.Diagnostics.Debug.WriteLine($"[cam-ui] ents={ents.Count} vfov={_lastVFovDeg} size={_lastW}x{_lastH}");

            Dispatcher.Invoke(DrawOverlay);
        }

        private void DrawOverlay()
        {
            Overlay.Children.Clear();
            if (_lastEnts is null || _lastEnts.Count == 0 || Img.Source is null) return;

            double viewW = Img.ActualWidth, viewH = Img.ActualHeight;
            if (viewW <= 1 || viewH <= 1) return;

            // Bild-auf-Canvas-Mapping (Uniform)
            double scale = Math.Min(viewW / _lastW, viewH / _lastH);
            double offX = (Overlay.ActualWidth - _lastW * scale) / 2.0;
            double offY = (Overlay.ActualHeight - _lastH * scale) / 2.0;

            // Projektions-FOV
            double vf = _lastVFovDeg * Math.PI / 180.0;
            double aspect = _lastW / (double)_lastH;
            double hf = 2.0 * Math.Atan(Math.Tan(vf / 2.0) * aspect);

            const double blobLiftPx = 12; // "optischer Lift", damit der Text auf der grauen Pille sitzt

            foreach (var e in _lastEnts)
            {
                // nur Spieler labeln: (a) Type==2 (laut Log) ODER (b) Name vorhanden
                bool isLikelyPlayer = PlayerTypeIds.Contains(e.Type) || !string.IsNullOrWhiteSpace(e.Label);
                if (!isLikelyPlayer) continue;

                if (e.Z <= 0.01) continue; // hinter der Kamera

                // Pinhole-Projektion
                double xndc = (e.X / e.Z) / Math.Tan(hf / 2.0);
                double yndc = (e.Y / e.Z) / Math.Tan(vf / 2.0);
                double u = (xndc * 0.5 + 0.5) * _lastW;
                double v = (-yndc * 0.5 + 0.5) * _lastH;

                if (u < -10 || u > _lastW + 10 || v < -10 || v > _lastH + 10) continue;

                // Team-Farbe (Name oder SteamID)
                bool isTeam = (e.SteamId != 0 && _teamSteamIds.Contains(e.SteamId))
                           || (!string.IsNullOrWhiteSpace(e.Label) && _teamNames.Contains(e.Label));
                var brush = isTeam ? Brushes.LimeGreen : Brushes.OrangeRed;

                // Anzeige-Text: nur Name, kein Fallback für Umwelt
                var text = string.IsNullOrWhiteSpace(e.Label) ? "player" : e.Label;
                if (!PlayerTypeIds.Contains(e.Type) && string.IsNullOrWhiteSpace(e.Label))
                    continue; // keine Umwelt beschriften

                var tb = new TextBlock
                {
                    Text = text,
                    Foreground = brush,
                    FontSize = 14,
                    Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
                    Padding = new Thickness(4, 1, 4, 1),
                    UseLayoutRounding = true,
                    SnapsToDevicePixels = true
                };

                // Größe messen, damit wir zentrieren können
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var sz = tb.DesiredSize;

                // Zentriert auf dem Blob, leicht nach oben gezogen
                var x = offX + u * scale - sz.Width / 2.0;
                var y = offY + (v - blobLiftPx) * scale - sz.Height / 2.0;

                Overlay.Children.Add(tb);
                Canvas.SetLeft(tb, x);
                Canvas.SetTop(tb, y);
            }
        }
        // einmalig:
        // Loaded += (_,__) => { Overlay.SizeChanged += (_,__) => DrawOverlay(); Img.SizeChanged += (_,__) => DrawOverlay(); };



        private void ApplyFps()
        {
            if (CmbFps.SelectedItem is ComboBoxItem it && int.TryParse(it.Content?.ToString(), out var fps) && fps > 0)
                _fpsLimit = fps;
            else
                _fpsLimit = 2; // default 2 FPS
        }

        private void OnNodeLineReceived(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || !_running) return;

            // FRAME:<b64>
            if (line.StartsWith("FRAME:"))
            {
                try
                {
                    var b64 = line.Substring("FRAME:".Length);
                    var bytes = Convert.FromBase64String(b64);
                    
                    var now = DateTime.UtcNow;
                    var minIntervalMs = 1000.0 / _fpsLimit;
                    if ((now - _lastFrameRenderTime).TotalMilliseconds < minIntervalMs) return;
                    _lastFrameRenderTime = now;

                    Dispatcher.Invoke(() => ShowFrame(new CameraFrame(bytes, null, _lastW, _lastH, _lastEnts)));
                }
                catch { }
                return;
            }

            // ENTS:<b64 json>
            if (line.StartsWith("ENTS:"))
            {
                try
                {
                    var b = Convert.FromBase64String(line.Substring(5));
                    var json = System.Text.Encoding.UTF8.GetString(b);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    
                    double vFov = 65.0;
                    if (doc.RootElement.TryGetProperty("fov", out var vf) && vf.ValueKind == System.Text.Json.JsonValueKind.Number)
                        vFov = vf.GetDouble();

                    var list = new List<CameraEntity>();
                    if (doc.RootElement.TryGetProperty("ents", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var it in arr.EnumerateArray())
                        {
                            double x = it.TryGetProperty("x", out var vx) ? vx.GetDouble() : 0;
                            double y = it.TryGetProperty("y", out var vy) ? vy.GetDouble() : 0;
                            double z = it.TryGetProperty("z", out var vz) ? vz.GetDouble() : 0;
                            int entityId = it.TryGetProperty("id", out var vi) ? (vi.ValueKind == System.Text.Json.JsonValueKind.Number ? vi.GetInt32() : 0) : 0;
                            int type = it.TryGetProperty("type", out var vt) ? (vt.ValueKind == System.Text.Json.JsonValueKind.Number ? vt.GetInt32() : 0) : 0;
                            ulong sid = 0;
                            if (it.TryGetProperty("sidStr", out var vss) && vss.ValueKind == System.Text.Json.JsonValueKind.String)
                                _ = ulong.TryParse(vss.GetString(), out sid);

                            string name = it.TryGetProperty("name", out var vn) ? (vn.GetString() ?? "") : "";
                            list.Add(new CameraEntity(x, y, z, name, entityId, type, sid));
                        }
                    }

                    _lastEnts = list;
                    _lastVFovDeg = vFov;
                    Dispatcher.Invoke(DrawOverlay);
                }
                catch { }
                return;
            }

            // INFO:<b64 json>
            if (line.StartsWith("INFO:"))
            {
                try
                {
                    var b = Convert.FromBase64String(line.Substring(5));
                    var json = System.Text.Encoding.UTF8.GetString(b);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    _lastW = doc.RootElement.TryGetProperty("w", out var w) ? w.GetInt32() : _lastW;
                    _lastH = doc.RootElement.TryGetProperty("h", out var h) ? h.GetInt32() : _lastH;
                    int cf = doc.RootElement.TryGetProperty("cf", out var vcf) ? vcf.GetInt32() : 0;
                    
                    OnCameraControlFlagsChanged(_cameraId, cf);
                }
                catch { }
                return;
            }
        }

        private void OnNodeErrorReceived(string line)
        {
            System.Diagnostics.Debug.WriteLine($"[node-err] {line}");
            Dispatcher.Invoke(() =>
            {
                if (Owner is MainWindow mw)
                {
                    mw.AppendLog($"[node-err] {line}");
                }
            });
        }

        private async Task SendCameraInputAsync(CameraButtons buttons, float mouseDeltaX, float mouseDeltaY)
        {
            try
            {
                var cmd = $"INPUT:{(int)buttons}:{mouseDeltaX.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{mouseDeltaY.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                _nodeProcess?.StandardInput.WriteLine(cmd);
                _nodeProcess?.StandardInput.Flush();
            }
            catch { }
            await Task.CompletedTask;
        }

        private void ShowFrame(CameraFrame frame)
        {
            try
            {
                var bi = new BitmapImage();
                using var ms = new MemoryStream(frame.Bytes);
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                Img.Source = bi;

                _frameCount++;
                TxtTitle.Text = (frame.Width > 0 && frame.Height > 0)
                    ? $"{_cameraId} ({frame.Width}×{frame.Height}, Frames: {_frameCount})"
                    : $"{_cameraId} (snapshot, Frames: {_frameCount})";

                // Wenn du dennoch eine einfache Fallback-Liste willst:
                if ((_lastEnts == null || _lastEnts.Count == 0) && frame.Entities != null && frame.Entities.Count > 0)
                {
                    _lastEnts = frame.Entities;
                    DrawOverlay();
                }
            }
            catch { /* tolerant */ }
        }

        private System.Threading.CancellationTokenSource? _continuousInputCts;

        private void StartContinuousInput(CameraButtons buttons, float dx, float dy)
        {
            StopContinuousInput();
            _continuousInputCts = new System.Threading.CancellationTokenSource();
            var token = _continuousInputCts.Token;

            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        await SendCameraInputAsync(buttons, dx, dy);
                        await System.Threading.Tasks.Task.Delay(100, token);
                    }
                }
                catch (System.Threading.Tasks.TaskCanceledException) { }
                finally
                {
                    await SendCameraInputAsync(CameraButtons.None, 0f, 0f);
                }
            }, token);
        }

        private void StopContinuousInput()
        {
            if (_continuousInputCts != null)
            {
                try { _continuousInputCts.Cancel(); } catch { }
                try { _continuousInputCts.Dispose(); } catch { }
                _continuousInputCts = null;
            }
        }

        private async void BtnCamReload_Click(object sender, RoutedEventArgs e)
        {
            await SendCameraInputAsync(CameraButtons.Reload, 0f, 0f);
        }

        private bool _isMouseDown;
        private Point _lastMousePos;

        private void Img_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_mouseLookSupported) return;
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                _isMouseDown = true;
                _lastMousePos = e.GetPosition(Img);
                Img.CaptureMouse();
            }
        }

        private async void Img_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_mouseLookSupported) return;
            if (_isMouseDown)
            {
                var currentPos = e.GetPosition(Img);
                double dx = currentPos.X - _lastMousePos.X;
                double dy = currentPos.Y - _lastMousePos.Y;
                
                if (Math.Abs(dx) > 0.5 || Math.Abs(dy) > 0.5)
                {
                    float rx = (float)dx * 0.5f;
                    float ry = (float)-dy * 0.5f;
                    
                    await SendCameraInputAsync(CameraButtons.None, rx, ry);
                    _lastMousePos = currentPos;
                }
            }
        }

        private void OnCameraControlFlagsChanged(string camId, int flagsVal)
        {
            if (!string.Equals(camId, _cameraId, StringComparison.OrdinalIgnoreCase)) return;

            Dispatcher.Invoke(() =>
            {
                var flags = (CameraControlFlags)flagsVal;
                
                if (flags == CameraControlFlags.None)
                {
                    string lowerId = _cameraId.ToLowerInvariant();
                    if (lowerId.Contains("turret"))
                    {
                        flags = CameraControlFlags.Mouse | CameraControlFlags.Fire | CameraControlFlags.Reload | CameraControlFlags.Crosshair;
                    }
                    else if (lowerId.Contains("drone"))
                    {
                        flags = CameraControlFlags.Movement | CameraControlFlags.Mouse | CameraControlFlags.SprintAndDuck;
                    }
                }

                if (flags == CameraControlFlags.None)
                {
                    ControlBorder.Visibility = Visibility.Collapsed;
                    _mouseLookSupported = false;
                    _crosshairSupported = false;
                    CrosshairGrid.Visibility = Visibility.Collapsed;
                    return;
                }

                ControlBorder.Visibility = Visibility.Visible;

                _movementSupported = flags.HasFlag(CameraControlFlags.Movement);
                _mouseLookSupported = flags.HasFlag(CameraControlFlags.Mouse);
                MovementPad.Visibility = (_movementSupported || _mouseLookSupported) ? Visibility.Visible : Visibility.Collapsed;

                BtnCamJump.Visibility = flags.HasFlag(CameraControlFlags.SprintAndDuck) ? Visibility.Visible : Visibility.Collapsed;
                BtnCamDuck.Visibility = flags.HasFlag(CameraControlFlags.SprintAndDuck) ? Visibility.Visible : Visibility.Collapsed;
                
                BtnCamFire.Visibility = flags.HasFlag(CameraControlFlags.Fire) ? Visibility.Visible : Visibility.Collapsed;
                BtnCamReload.Visibility = flags.HasFlag(CameraControlFlags.Reload) ? Visibility.Visible : Visibility.Collapsed;

                _crosshairSupported = flags.HasFlag(CameraControlFlags.Crosshair);
                CrosshairGrid.Visibility = _crosshairSupported ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        private void Img_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                _isMouseDown = false;
                Img.ReleaseMouseCapture();
            }
        }

        private void Img_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isMouseDown)
            {
                _isMouseDown = false;
                Img.ReleaseMouseCapture();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            try { DragMove(); } catch { }
        }
    }
}
