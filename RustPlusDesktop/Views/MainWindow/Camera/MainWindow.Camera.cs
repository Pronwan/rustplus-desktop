using RustPlusDesk.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
internal readonly HashSet<string> _camBusy = new(StringComparer.OrdinalIgnoreCase);
    private void BtnOpenCamera_Click(object sender, RoutedEventArgs e)
    {
        if (_rust is not RustPlusClientReal real) return;

        // simpler Prompt statt TextBox:
        var id = Microsoft.VisualBasic.Interaction.InputBox(
            "Camera identifier:", "Open camera", "");
        if (string.IsNullOrWhiteSpace(id)) return;

        var w = new RustPlusDesk.Views.CameraWindow(real, id) { Owner = this };
        w.Show();
        real.DebugDumpAppRequestShape();
    }

   


    private ObservableCollection<string> _cameraIds = new();
    private DispatcherTimer? _camThumbTimer;

    private void InitCameraUi()
    {
        BtnAddCam.Click += (_, __) =>
        {
            var input = Microsoft.VisualBasic.Interaction.InputBox("Camera identifier:", "Add camera", "");
            if (string.IsNullOrWhiteSpace(input)) return;
            if (_cameraIds.Any(s => string.Equals(s, input, StringComparison.OrdinalIgnoreCase))) return;

            _cameraIds.Add(input);   // _cameraIds == Selected.CameraIds
            _vm.Save();              // sofort persistieren
            RebuildCameraTiles();
            EnsureCamThumbPolling();
        };
    }




    private void RebuildCameraTiles()
    {
        CamItems.Items.Clear();
        foreach (var id in _cameraIds)
            CamItems.Items.Add(BuildCamTile(id));
    }
    // Wie „nah“ die Mini-Map um den Spieler herum zuschneidet (Anteil der Hauptkarte)
    private const double MINI_VIEW_FRACTION = 0.3; // 40% des sichtbaren Bereichs

    //>>
    private void CenterMiniMapOnPlayer()
    {
        if (_miniMap == null || !_miniMap.IsVisible) return;
        if (WebViewHost == null || Overlay == null) return;

        // Spielerposition besorgen
        var me = TeamMembers.FirstOrDefault(t => t.SteamId == _mySteamId) ?? TeamMembers.FirstOrDefault();
        if (me == null || !me.X.HasValue || !me.Y.HasValue) return;

        // Welt → Bild/Overlay-Koordinaten (so positionierst du auch deine Marker)
        Point pOverlay = WorldToImagePx(me.X.Value, me.Y.Value);

        // Overlay → Host-Koordinaten (inkl. ALLER Transforms/Letterboxing)
        GeneralTransform t = Overlay.TransformToVisual(WebViewHost);
        Point pHost = t.Transform(pOverlay);

        double hostW = Math.Max(1, WebViewHost.ActualWidth);
        double hostH = Math.Max(1, WebViewHost.ActualHeight);

        // Quadratischen Ausschnitt wählen (keine Verzerrung im runden Fenster)
        double side = Math.Min(hostW, hostH) * (MINI_VIEW_FRACTION * Math.Pow(GetEffectiveZoom(), 0.0025));

        // Um den Spieler zentrieren …
        double vx = pHost.X - side / 2.0;
        double vy = pHost.Y - side / 2.0;

        // … innerhalb des Hosts clampen
        vx = Math.Max(0, Math.Min(vx, hostW - side));
        vy = Math.Max(0, Math.Min(vy, hostH - side));

        _miniMap.SetViewbox(new Rect(vx, vy, side, side));
    }

    private MiniMapWindow? _miniMap;
    private VisualBrush? _miniMapBrush;
    // z.B. Click-Handler deines „Mini-Map“-Buttons:
    private void BtnToggleMiniMap_Click(object sender, RoutedEventArgs e)
    {
        if (_miniMap == null || !_miniMap.IsVisible)

        {
            
            // WICHTIG: mapRoot muss dein existierendes Karten-Root-Element sein!
            // Beispiele: SceneGrid, MapRootGrid, OverlayHostGrid – je nach deinem x:Name.
            var mapRoot = WebViewHost;
            var vb = new VisualBrush(WebViewHost)
            {
                // Wir schneiden selbst zu, daher:
                Stretch = Stretch.None,
                ViewboxUnits = BrushMappingMode.Absolute
            };
            _miniMapBrush = vb;


            _miniMap = new MiniMapWindow(mapRoot)
            {
                Owner = this,                         // optional
                Left = SystemParameters.WorkArea.Right - 280,
                Top = SystemParameters.WorkArea.Top + 20
            };

            _miniMap.Show();
            CenterMiniMapOnPlayer();

        }
        else
        {
            _miniMap.Close();
            _miniMap = null;
        }
    }

    private bool TryGetMyWorldPos(out double x, out double y)
    {
        x = y = 0;
        var me = TeamMembers.FirstOrDefault(t => t.SteamId == _mySteamId);
        if (me != null && me.X.HasValue && me.Y.HasValue)
        { x = me.X.Value; y = me.Y.Value; return true; }

        if (_lastPlayersBySid.TryGetValue(_mySteamId, out var p))
        { x = p.Item1; y = p.Item2; return true; }

        return false;
    }

    private FrameworkElement BuildCamTile(string id)
    {
        var root = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Margin = new Thickness(6)
        };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header: Name + Buttons
        var header = new DockPanel();
        var name = new TextBlock { Text = id, FontWeight = FontWeights.SemiBold };
        var spBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnOpen = new Button { Width = 16, Height = 16, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Open" };
        btnOpen.Content = new TextBlock { FontFamily = new FontFamily("Segoe MDL2 Assets"), Text = "\uE8A7" }; // E894
        btnOpen.Click += (_, __) =>
        {
            if (_rust is RustPlusClientReal real)
            {
                var w = new RustPlusDesk.Views.CameraWindow(real, id) { Owner = this };
                _camBusy.Add(id);
                w.Closed += (_, __2) => _camBusy.Remove(id);
                w.Show();
            }
        };

        var btnDel = new Button { Width = 16, Height = 16, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Delete" };
        btnDel.Content = new TextBlock { FontFamily = new FontFamily("Segoe MDL2 Assets"), Text = "" }; // E74D
        btnDel.Click += (_, __) =>
        {
            _cameraIds.Remove(id);
            _vm.Save();
            RebuildCameraTiles();
        };

        spBtns.Children.Add(btnOpen);
        spBtns.Children.Add(btnDel);
        DockPanel.SetDock(spBtns, Dock.Right);
        header.Children.Add(spBtns);
        header.Children.Add(name);
        Grid.SetRow(header, 0);

        // Thumb
        var img = new Image { Stretch = Stretch.UniformToFill, SnapsToDevicePixels = true, UseLayoutRounding = true, Height = 110, ClipToBounds = true };
        img.Tag = id; // damit der Thumb-Refresher weiß, wohin
        Grid.SetRow(img, 1);

        // Status-Zeile
        var status = new TextBlock { Opacity = 0.7, Margin = new Thickness(0, 4, 0, 0) };
        status.Tag = id + "|status";
        Grid.SetRow(status, 2);

        grid.Children.Add(header);
        grid.Children.Add(img);
        grid.Children.Add(status);
        root.Child = grid;
        return root;
    }

    private void EnsureCamThumbPolling()
    {
        _camThumbTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _camThumbTimer.Tick -= CamThumbTimer_Tick;
        _camThumbTimer.Tick += CamThumbTimer_Tick;
        _camThumbTimer.Start();
    }

    private int _camThumbIndex = 0;
    private int _camThumbBusy = 0;

    private async void CamThumbTimer_Tick(object? sender, EventArgs e)
    {
        if (!CamItems.IsVisible || _cameraIds.Count == 0) return;
        if (_rust is not RustPlusClientReal real) return;
        if (System.Threading.Interlocked.Exchange(ref _camThumbBusy, 1) == 1) return;

        try
        {
            if (_camThumbIndex >= CamItems.Items.Count) _camThumbIndex = 0;
            if (_camThumbIndex < 0 || _camThumbIndex >= CamItems.Items.Count) return;

            if (CamItems.Items[_camThumbIndex] is not FrameworkElement cont) return;
            _camThumbIndex++;

            var img = FindDescImage(cont);
            if (img == null) return;
            var id = img.Tag as string;
            if (string.IsNullOrWhiteSpace(id)) return;
            if (_camBusy.Contains(id)) return;   // hier pausieren, wenn live
            var status = FindStatus(cont, id);

            // 1) Node-Fallback zuerst (liefert in der Praxis am zuverlässigsten)
            var frame = await real.GetCameraFrameViaNodeAsync(id, timeoutMs: 6000);
            // 2) optional: klassischer Pfad als Zweitversuch
            if (frame?.Bytes == null)
                frame = await real.GetCameraFrameAsync(id);

            if (frame?.Bytes != null)
            {
                var bi = new BitmapImage();
                using var ms = new MemoryStream(frame.Bytes);
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit(); bi.Freeze();
                img.Source = bi;
                if (status != null) status.Text = (frame.Width > 0 && frame.Height > 0) ? $"{frame.Width}×{frame.Height}" : "snapshot";
            }
            else
            {
                if (status != null) status.Text = "no frame";
            }
        }
        catch (Exception ex)
        {
            // damit wir was sehen
            AppendLog("[cam] " + ex.Message);
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _camThumbBusy, 0);
        }

        static Image? FindDescImage(FrameworkElement root)
        {
            if (root is Image i) return i;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int k = 0; k < n; k++)
                if (VisualTreeHelper.GetChild(root, k) is FrameworkElement fe && FindDescImage(fe) is Image hit) return hit;
            return null;
        }
        static TextBlock? FindStatus(FrameworkElement root, string id)
        {
            var q = new Queue<DependencyObject>();
            q.Enqueue(root);
            while (q.Count > 0)
            {
                var x = q.Dequeue();
                if (x is TextBlock tb && (tb.Tag as string) == id + "|status") return tb;
                int n = VisualTreeHelper.GetChildrenCount(x);
                for (int i = 0; i < n; i++) q.Enqueue(VisualTreeHelper.GetChild(x, i));
            }
            return null;
        }
    }

    // generischer BFS-Finder im VisualTree
    private static T? FindDesc<T>(DependencyObject root, Func<T, bool>? predicate = null) where T : DependencyObject
    {
        var q = new Queue<DependencyObject>();
        q.Enqueue(root);
        while (q.Count > 0)
        {
            var x = q.Dequeue();
            if (x is T t && (predicate == null || predicate(t))) return t;
            int n = VisualTreeHelper.GetChildrenCount(x);
            for (int i = 0; i < n; i++) q.Enqueue(VisualTreeHelper.GetChild(x, i));
        }
        return null;
    }
}
