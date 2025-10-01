using RustPlusDesk.Services;
using RustPlusDesk.Services;
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
        private readonly DispatcherTimer _timer = new();
        private bool _running;
        
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
            _real.CameraEntities += OnCameraEntities;
            TxtId.Text = cameraId;


            // FPS Dropdown
            CmbFps.SelectionChanged += (_, __) => ApplyFps();

            Loaded += async (_, __) =>
            {
                ApplyFps();
                _running = true;
                Overlay.SizeChanged += (_, __2) => DrawOverlay(); Img.SizeChanged += (_, __2) => DrawOverlay();
            
            await RefreshTeamAsync();

                // optional: erstes Bild
                var first = await _real.GetCameraFrameViaNodeAsync(_cameraId, timeoutMs: 5000);
               
                if (first?.Bytes != null) ShowFrame(first);

                _timer.Tick += Timer_Tick;
                _timer.Start();

                // Thumbnails für diese Kamera pausieren (im MainWindow hast du _camBusy)
                if (Owner is MainWindow mw) mw._camBusy.Add(_cameraId);
            };

            Closed += (_, __) =>
            {
                _running = false;
                _timer.Stop();
                _timer.Tick -= Timer_Tick;
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
                _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / fps);
            else
                _timer.Interval = TimeSpan.FromMilliseconds(500); // default 2 FPS
        }

        private int _snapInFlight = 0;

        private async void Timer_Tick(object? sender, EventArgs e)
        {
            if (!_running) return;
            if (System.Threading.Interlocked.Exchange(ref _snapInFlight, 1) == 1) return;

            try
            {
                var frame = await _real.GetCameraFrameViaNodeAsync(_cameraId, timeoutMs: 4000);
                if (frame?.Bytes != null) ShowFrame(frame);
                else TxtStatus.Text = "no frame";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = ex.Message;
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _snapInFlight, 0);
            }
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

                TxtStatus.Text = (frame.Width > 0 && frame.Height > 0)
                    ? $"{frame.Width}×{frame.Height}"
                    : "snapshot";

                // Wenn du dennoch eine einfache Fallback-Liste willst:
                if ((_lastEnts == null || _lastEnts.Count == 0) && frame.Entities != null && frame.Entities.Count > 0)
                {
                    _lastEnts = frame.Entities;
                    DrawOverlay();
                }
            }
            catch { /* tolerant */ }
        }

    }
}
