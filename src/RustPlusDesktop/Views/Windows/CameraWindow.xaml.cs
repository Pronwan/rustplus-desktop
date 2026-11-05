using RustPlusDesk.Services;
using RustPlusDesk.Services.Camera;
using RustPlusApi.Data.Cameras;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views
{
    public partial class CameraWindow : WpfUi.FluentWindow
    {
        private readonly RustPlusClientReal _real;
        private readonly string _cameraId;

        private CameraSession? _session;
        private CancellationTokenSource? _continuousCts;

        private bool _mouseLookSupported;
        private bool _crosshairSupported;
        private int _frameCount;

        // Team names to colour player labels (native camera entities carry no steam id).
        private readonly HashSet<string> _teamNames = new(StringComparer.OrdinalIgnoreCase);

        // Latest frame state used for the entity overlay.
        private IReadOnlyList<CameraEntity> _lastEnts = Array.Empty<CameraEntity>();
        private int _lastW = 160, _lastH = 90;
        private double _lastVFovDeg = 65;

        public CameraWindow(RustPlusClientReal real, string cameraId)
        {
            InitializeComponent();
            _real = real;
            _cameraId = cameraId;
            Title = cameraId;
            TxtTitle.Text = cameraId;

            // Mouse-look drag on the image.
            Img.MouseDown += Img_MouseDown;
            Img.MouseMove += Img_MouseMove;
            Img.MouseUp += Img_MouseUp;
            Img.MouseLeave += Img_MouseLeave;

            // Analog joystick — proportional look (PTZ/turret) or movement (drone).
            MovementPad.MouseLeftButtonDown += Joy_Down;
            MovementPad.MouseMove += Joy_Move;
            MovementPad.MouseLeftButtonUp += Joy_Up;

            // Drone vertical.
            HookHold(BtnCamJump, () => StartContinuousButtons(CameraButtons.Jump));
            HookHold(BtnCamDuck, () => StartContinuousButtons(CameraButtons.Duck));

            // Auto-turret fire (hold).
            HookHold(BtnCamFire,
                onDown: () =>
                {
                    CrosshairGrid.Visibility = Visibility.Visible;
                    StartContinuousButtons(CameraButtons.FirePrimary);
                },
                onUp: () =>
                {
                    StopContinuous();
                    if (!_crosshairSupported) CrosshairGrid.Visibility = Visibility.Collapsed;
                });

            CmbFps.SelectionChanged += (_, __) => ApplyFps();

            Loaded += CameraWindow_Loaded;
            Closed += CameraWindow_Closed;

            Overlay.SizeChanged += (_, __) => DrawOverlay();
            Img.SizeChanged += (_, __) => DrawOverlay();
        }

        private async void CameraWindow_Loaded(object sender, RoutedEventArgs e)
        {
            TxtStatus.Text = Properties.Resources.GetString("CodeUiConnecting") ?? "Connecting…";
            _ = RefreshTeamAsync();

            if (Owner is MainWindow mw) mw._camBusy.Add(_cameraId);

            try
            {
                _session = await _real.CreateCameraSessionAsync(_cameraId);
            }
            catch (Exception ex)
            {
                TxtStatus.Text = ex.Message;
                ShowBanner(ex.Message, warning: true);
                return;
            }

            _lastW = _session.Width > 0 ? _session.Width : _lastW;
            _lastH = _session.Height > 0 ? _session.Height : _lastH;

            _session.FrameRendered += OnFrameRendered;
            _session.EntitiesUpdated += OnEntitiesUpdated;
            _session.KeepAliveFailed += OnKeepAliveFailed;

            ApplyFps();
            ConfigureHud(_session);
            TxtStatus.Text = string.Empty;
        }

        private async void CameraWindow_Closed(object? sender, EventArgs e)
        {
            StopContinuous();
            var session = _session;
            _session = null;
            if (session != null)
            {
                session.FrameRendered -= OnFrameRendered;
                session.EntitiesUpdated -= OnEntitiesUpdated;
                session.KeepAliveFailed -= OnKeepAliveFailed;
                try { await session.DisposeAsync(); } catch { /* best effort */ }
            }
            if (Owner is MainWindow mw) mw._camBusy.Remove(_cameraId);
        }

        // ---------- Capability-driven HUD ----------

        private void ConfigureHud(CameraSession s)
        {
            var flags = s.ControlFlags;
            bool movement = s.IsDrone || flags.HasFlag(CameraControlFlags.Movement);
            bool look = flags.HasFlag(CameraControlFlags.Mouse);
            bool vertical = flags.HasFlag(CameraControlFlags.SprintAndDuck);
            bool reload = flags.HasFlag(CameraControlFlags.Reload);

            _mouseLookSupported = look;
            _crosshairSupported = flags.HasFlag(CameraControlFlags.Crosshair);

            MovementPad.Visibility = (movement || look) ? Visibility.Visible : Visibility.Collapsed;
            VerticalPad.Visibility = vertical ? Visibility.Visible : Visibility.Collapsed;
            BtnCamZoom.Visibility = s.IsPtzCamera ? Visibility.Visible : Visibility.Collapsed;
            BtnCamFire.Visibility = s.IsAutoTurret ? Visibility.Visible : Visibility.Collapsed;
            BtnCamReload.Visibility = reload ? Visibility.Visible : Visibility.Collapsed;
            CrosshairGrid.Visibility = _crosshairSupported ? Visibility.Visible : Visibility.Collapsed;

            bool anyControl = movement || look || vertical || reload || s.IsPtzCamera || s.IsAutoTurret;
            ControlBorder.Visibility = anyControl ? Visibility.Visible : Visibility.Collapsed;

            string kind;
            WpfUi.SymbolRegular icon;
            if (s.IsDrone) { kind = "Drone"; icon = WpfUi.SymbolRegular.ArrowUpload24; }
            else if (s.IsAutoTurret) { kind = "Auto-turret"; icon = WpfUi.SymbolRegular.TargetArrow24; }
            else if (s.IsPtzCamera) { kind = "PTZ camera"; icon = WpfUi.SymbolRegular.CameraDome24; }
            else if (s.IsStaticCamera) { kind = "Static camera"; icon = WpfUi.SymbolRegular.CameraDome24; }
            else { kind = "Camera"; icon = WpfUi.SymbolRegular.CameraDome24; }
            TxtKind.Text = kind;
            KindIcon.Symbol = icon;
            KindBadge.Visibility = Visibility.Visible;

            if (s.IsStaticCamera)
                ShowBanner("Static camera — no controls");
        }

        // ---------- Frame / entity plumbing ----------

        private void OnFrameRendered(byte[] png)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var bi = new BitmapImage();
                    using var ms = new MemoryStream(png);
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = ms;
                    bi.EndInit();
                    bi.Freeze();
                    Img.Source = bi;

                    _frameCount++;
                    TxtTitle.Text = (_lastW > 0 && _lastH > 0)
                        ? $"{_cameraId} ({_lastW}×{_lastH}, {_frameCount})"
                        : _cameraId;
                }
                catch { /* tolerant */ }
            });
        }

        private void OnEntitiesUpdated(IReadOnlyList<CameraEntity> ents, double vFovDeg)
        {
            _lastEnts = ents ?? Array.Empty<CameraEntity>();
            if (vFovDeg > 0) _lastVFovDeg = vFovDeg;
            Dispatcher.Invoke(DrawOverlay);
        }

        private void OnKeepAliveFailed(string message)
        {
            Dispatcher.Invoke(() => ShowBanner("Stream lost: " + message, warning: true));
        }

        private void ShowBanner(string text, bool warning = false)
        {
            Banner.Message = text;
            Banner.Severity = warning ? WpfUi.InfoBarSeverity.Warning : WpfUi.InfoBarSeverity.Informational;
            Banner.IsOpen = true;
        }

        private void DrawOverlay()
        {
            Overlay.Children.Clear();
            var ents = _lastEnts;
            if (ents.Count == 0 || Img.Source is null) return;

            double viewW = Img.ActualWidth, viewH = Img.ActualHeight;
            if (viewW <= 1 || viewH <= 1) return;

            double scale = Math.Min(viewW / _lastW, viewH / _lastH);
            double offX = (Overlay.ActualWidth - _lastW * scale) / 2.0;
            double offY = (Overlay.ActualHeight - _lastH * scale) / 2.0;

            double vf = _lastVFovDeg * Math.PI / 180.0;
            double aspect = _lastW / (double)_lastH;
            double hf = 2.0 * Math.Atan(Math.Tan(vf / 2.0) * aspect);
            const double blobLiftPx = 12;

            foreach (var e in ents)
            {
                bool isPlayer = e.Type == CameraEntityType.Player || !string.IsNullOrWhiteSpace(e.Name);
                if (!isPlayer) continue;

                double ez = e.Position.Z;
                if (ez <= 0.01) continue; // behind the camera

                double xndc = (e.Position.X / ez) / Math.Tan(hf / 2.0);
                double yndc = (e.Position.Y / ez) / Math.Tan(vf / 2.0);
                double u = (xndc * 0.5 + 0.5) * _lastW;
                double v = (-yndc * 0.5 + 0.5) * _lastH;
                if (u < -10 || u > _lastW + 10 || v < -10 || v > _lastH + 10) continue;

                bool isTeam = !string.IsNullOrWhiteSpace(e.Name) && _teamNames.Contains(e.Name);
                var brush = isTeam ? Brushes.LimeGreen : Brushes.OrangeRed;
                var text = string.IsNullOrWhiteSpace(e.Name) ? "player" : e.Name;

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
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var sz = tb.DesiredSize;

                var x = offX + u * scale - sz.Width / 2.0;
                var y = offY + (v - blobLiftPx) * scale - sz.Height / 2.0;
                Overlay.Children.Add(tb);
                Canvas.SetLeft(tb, x);
                Canvas.SetTop(tb, y);
            }
        }

        private async Task RefreshTeamAsync()
        {
            try
            {
                var team = await _real.GetTeamInfoAsync();
                _teamNames.Clear();
                if (team?.Members != null)
                    foreach (var m in team.Members)
                        if (!string.IsNullOrWhiteSpace(m.Name)) _teamNames.Add(m.Name!);
            }
            catch { /* ignore */ }
        }

        private void ApplyFps()
        {
            if (_session == null) return;
            if (CmbFps.SelectedItem is ComboBoxItem it && int.TryParse(it.Content?.ToString(), out var fps) && fps > 0)
                _session.TargetFps = fps;
        }

        // ---------- Input ----------

        // ----- Analog joystick -----
        private const double JoyCenter = 59;              // MovementPad 118 / 2
        private const double JoyThumbRadius = 24;         // JoyThumb 48 / 2
        private const double JoyMaxRadius = JoyCenter - JoyThumbRadius; // travel limit
        private bool _joyActive;
        private double _joyX, _joyY;                      // normalized -1..1, up positive

        private void Joy_Down(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_session == null) return;
            _joyActive = true;
            MovementPad.CaptureMouse();
            JoyUpdate(e.GetPosition(MovementPad));
            StartContinuous(JoystickTick);
        }

        private void Joy_Move(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_joyActive) JoyUpdate(e.GetPosition(MovementPad));
        }

        private void Joy_Up(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_joyActive) return;
            _joyActive = false;
            MovementPad.ReleaseMouseCapture();
            _joyX = _joyY = 0;
            RecenterThumb();
            StopContinuous();
        }

        private void RecenterThumb()
        {
            Canvas.SetLeft(JoyThumb, JoyCenter - JoyThumbRadius);
            Canvas.SetTop(JoyThumb, JoyCenter - JoyThumbRadius);
        }

        private void JoyUpdate(Point p)
        {
            double dx = p.X - JoyCenter;
            double dy = p.Y - JoyCenter;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len > JoyMaxRadius && len > 0)
            {
                dx = dx / len * JoyMaxRadius;
                dy = dy / len * JoyMaxRadius;
            }
            Canvas.SetLeft(JoyThumb, JoyCenter - JoyThumbRadius + dx);
            Canvas.SetTop(JoyThumb, JoyCenter - JoyThumbRadius + dy);
            _joyX = dx / JoyMaxRadius;    // right positive
            _joyY = -dy / JoyMaxRadius;   // up positive
        }

        // Quadratic response: fine control near the centre, fast toward the edge.
        private static float JoyResponse(double v, double speed)
        {
            double a = Math.Abs(v);
            if (a < 0.08) return 0f;
            return (float)(Math.Sign(v) * a * a * speed);
        }

        private Task JoystickTick(CameraSession s)
        {
            double nx = _joyX, ny = _joyY;
            bool movement = s.IsDrone || s.ControlFlags.HasFlag(CameraControlFlags.Movement);
            if (movement)
            {
                var b = CameraButtons.None;
                if (ny > 0.35) b |= CameraButtons.Forward; else if (ny < -0.35) b |= CameraButtons.Backward;
                if (nx > 0.35) b |= CameraButtons.Right; else if (nx < -0.35) b |= CameraButtons.Left;
                return s.SendInputAsync(b, 0f, 0f);
            }
            if (_mouseLookSupported)
                return s.LookAsync(JoyResponse(nx, 16.0), JoyResponse(ny, 16.0));
            return Task.CompletedTask;
        }

        private void StartContinuousButtons(CameraButtons buttons)
            => StartContinuous(s => s.SendInputAsync(buttons, 0f, 0f));

        private void StartContinuous(Func<CameraSession, Task> action)
        {
            StopContinuous();
            var session = _session;
            if (session == null) return;

            var cts = new CancellationTokenSource();
            _continuousCts = cts;
            var token = cts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        await action(session);
                        await Task.Delay(100, token);
                    }
                }
                catch (TaskCanceledException) { }
                finally
                {
                    try { await session.SendInputAsync(CameraButtons.None, 0f, 0f); } catch { }
                }
            }, token);
        }

        private void StopContinuous()
        {
            var cts = _continuousCts;
            _continuousCts = null;
            if (cts != null)
            {
                try { cts.Cancel(); } catch { }
                try { cts.Dispose(); } catch { }
            }
        }

        private void HookHold(Button b, Action onDown) => HookHold(b, onDown, StopContinuous);

        private void HookHold(Button b, Action onDown, Action onUp)
        {
            b.PreviewMouseDown += (_, __) => onDown();
            b.PreviewMouseUp += (_, __) => onUp();
            b.MouseLeave += (_, __) => onUp();
        }

        private async void BtnCamZoom_Click(object sender, RoutedEventArgs e)
        {
            if (_session != null) { try { await _session.ZoomAsync(); } catch { } }
        }

        private async void BtnCamReload_Click(object sender, RoutedEventArgs e)
        {
            if (_session != null) { try { await _session.ReloadAsync(); } catch { } }
        }

        // Mouse-look drag.
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
            if (!_mouseLookSupported || !_isMouseDown || _session == null) return;
            var pos = e.GetPosition(Img);
            double dx = pos.X - _lastMousePos.X;
            double dy = pos.Y - _lastMousePos.Y;
            if (Math.Abs(dx) > 0.5 || Math.Abs(dy) > 0.5)
            {
                _lastMousePos = pos;
                try { await _session.LookAsync((float)dx * 0.5f, (float)-dy * 0.5f); } catch { }
            }
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

    }
}
