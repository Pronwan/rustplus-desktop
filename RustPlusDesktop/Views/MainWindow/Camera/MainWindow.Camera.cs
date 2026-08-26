using RustPlusDesk.Services;
using RustPlusDesk.Services.Camera;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        var id = AddCameraDialog.Prompt(this);
        if (string.IsNullOrWhiteSpace(id)) return;

        var w = new RustPlusDesk.Views.CameraWindow(real, id) { Owner = this };
        w.Show();
    }

   


    private ObservableCollection<string> _cameraIds = new();
    private Dictionary<string, string> _cameraNames = new();
    private string? _editingCamId;
    private string? _deletingCamId;
    private DispatcherTimer? _camThumbTimer;

    /// <summary>Friendly display name for a camera id, falling back to the id itself.</summary>
    private string CamDisplayName(string id)
        => _cameraNames != null && _cameraNames.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n) ? n : id;

    private void InitCameraUi()
    {
        BtnAddCam.Click += (_, __) =>
        {
            AddCamErr.IsOpen = false;
            AddCamText.Text = string.Empty;
            AddCamName.Text = string.Empty;
            AddCamPopup.IsOpen = true;
            AddCamText.Focus();
        };
    }

    // ----- Add camera -----

    private void AddCamConfirm_Click(object sender, RoutedEventArgs e) => CommitAddCamera();

    private void AddCamCancel_Click(object sender, RoutedEventArgs e) => AddCamPopup.IsOpen = false;

    private void AddCamText_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) { CommitAddCamera(); e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.Escape) { AddCamPopup.IsOpen = false; e.Handled = true; }
    }

    private void CommitAddCamera()
    {
        var input = (AddCamText.Text ?? string.Empty).Trim();
        var name = (AddCamName.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            AddCamErr.Message = "Please enter a camera identifier.";
            AddCamErr.IsOpen = true;
            AddCamText.Focus();
            return;
        }
        if (_cameraIds.Any(s => string.Equals(s, input, StringComparison.OrdinalIgnoreCase)))
        {
            AddCamErr.Message = "That camera is already added.";
            AddCamErr.IsOpen = true;
            return;
        }

        _cameraIds.Add(input);   // _cameraIds == Selected.CameraIds
        if (!string.IsNullOrWhiteSpace(name)) _cameraNames[input] = name;
        _vm.Save();              // persist immediately
        RebuildCameraTiles();
        EnsureCamThumbPolling();
        AddCamPopup.IsOpen = false;
    }

    // ----- Edit camera (id + name) -----

    private void OpenEditCam(string id, FrameworkElement anchor)
    {
        _editingCamId = id;
        EditCamErr.IsOpen = false;
        EditCamText.Text = id;
        EditCamName.Text = _cameraNames.TryGetValue(id, out var n) ? n : string.Empty;
        EditCamPopup.PlacementTarget = anchor;
        EditCamPopup.IsOpen = true;
        EditCamText.Focus();
    }

    private void EditCamConfirm_Click(object sender, RoutedEventArgs e) => CommitEditCamera();

    private void EditCamCancel_Click(object sender, RoutedEventArgs e) { EditCamPopup.IsOpen = false; _editingCamId = null; }

    private void EditCamText_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) { CommitEditCamera(); e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.Escape) { EditCamPopup.IsOpen = false; _editingCamId = null; e.Handled = true; }
    }

    private void CommitEditCamera()
    {
        var oldId = _editingCamId;
        if (string.IsNullOrEmpty(oldId)) { EditCamPopup.IsOpen = false; return; }

        var newId = (EditCamText.Text ?? string.Empty).Trim();
        var newName = (EditCamName.Text ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(newId))
        {
            EditCamErr.Message = "Please enter a camera identifier.";
            EditCamErr.IsOpen = true;
            EditCamText.Focus();
            return;
        }

        bool idChanged = !string.Equals(newId, oldId, StringComparison.Ordinal);
        if (idChanged)
        {
            if (_cameraIds.Any(s => string.Equals(s, newId, StringComparison.OrdinalIgnoreCase)))
            {
                EditCamErr.Message = "That camera is already added.";
                EditCamErr.IsOpen = true;
                return;
            }
            var idx = _cameraIds.IndexOf(oldId);
            if (idx >= 0) _cameraIds[idx] = newId; else _cameraIds.Add(newId);
            _cameraNames.Remove(oldId);
        }

        if (string.IsNullOrWhiteSpace(newName)) _cameraNames.Remove(newId);
        else _cameraNames[newId] = newName;

        _vm.Save();
        // Close before rebuild — the popover is anchored to a tile button that RebuildCameraTiles destroys.
        EditCamPopup.IsOpen = false;
        _editingCamId = null;
        RebuildCameraTiles();
    }

    // ----- Delete camera (with confirmation) -----

    private void OpenDeleteCam(string id, FrameworkElement anchor)
    {
        _deletingCamId = id;
        DeleteCamText.Text = $"Delete “{CamDisplayName(id)}”? This removes it from this server.";
        DeleteCamPopup.PlacementTarget = anchor;
        DeleteCamPopup.IsOpen = true;
    }

    private void DeleteCamCancel_Click(object sender, RoutedEventArgs e) { DeleteCamPopup.IsOpen = false; _deletingCamId = null; }

    private void DeleteCamConfirm_Click(object sender, RoutedEventArgs e)
    {
        var id = _deletingCamId;
        DeleteCamPopup.IsOpen = false;
        _deletingCamId = null;
        if (string.IsNullOrEmpty(id)) return;

        _cameraIds.Remove(id);
        _cameraNames.Remove(id);
        _vm.Save();
        RebuildCameraTiles();
    }




    private void RebuildCameraTiles()
    {
        CamItems.Items.Clear();
        foreach (var id in _cameraIds)
            CamItems.Items.Add(BuildCamTile(id));
    }
    // Wie „nah“ die Mini-Map um den Spieler herum zuschneidet (Anteil der Hauptkarte)
    private const double MINI_VIEW_FRACTION = 0.3; // 40% des sichtbaren Bereichs

    private bool TryGetFollowingWorldPos(out double worldX, out double worldY)
    {
        worldX = 0; worldY = 0;
        ulong sid = _vm.FollowingSteamId ?? _mySteamId;

        // 1. Check dynamic markers (high priority for movement)
        if (TryResolvePosFromDynMarkers(sid, out worldX, out worldY)) return true;

        // 2. Check team member state (static/last known)
        var member = TeamMembers.FirstOrDefault(t => t.SteamId == sid);
        if (member != null && member.X.HasValue && member.Y.HasValue)
        {
            worldX = member.X.Value;
            worldY = member.Y.Value;
            return true;
        }

        return false;
    }

    public void CenterMiniMapOnPlayer()
    {
        if (_miniMap == null || WebViewHost == null || Overlay == null) return;

        double mapX = 0, mapY = 0;
        
        // If the Main Map is currently smooth-following, the Mini Map should just look at the Main Map's camera center!
        // This prevents double-panning and stutter.
        if ((_vm.IsFollowing || _trackingEntityId.HasValue) && _currentCamX.HasValue && _currentCamY.HasValue)
        {
            mapX = _currentCamX.Value;
            mapY = _currentCamY.Value;
        }
        else if (!TryGetFollowingWorldPos(out mapX, out mapY)) 
        {
            return;
        }

        Point pHost;
        try
        {
            Point pOverlay = WorldToImagePx(mapX, mapY);
            pHost = Overlay.TransformToVisual(WebViewHost).Transform(pOverlay);

            // Add the VisualBrush parent layout offset (Grid Rows and Margins) dynamically
            try
            {
                var offset = VisualTreeHelper.GetOffset(WebViewHost);
                pHost.X += offset.X;
                pHost.Y += offset.Y;
            }
            catch { }
        }
        catch
        {
            pHost = new Point(WebViewHost.ActualWidth * 0.5, WebViewHost.ActualHeight * 0.5);
        }

        double hostW = Math.Max(1, WebViewHost.ActualWidth);
        double hostH = Math.Max(1, WebViewHost.ActualHeight);

        // Quadratischen Ausschnitt wählen
        double side = Math.Min(hostW, hostH) * (MINI_VIEW_FRACTION * Math.Pow(GetEffectiveZoom(), 0.0025));

        // Um den Punkt zentrieren - OHNE CLAMPING, damit der Spieler IMMER 100% in der Mitte bleibt!
        double vx = pHost.X - side / 2.0;
        double vy = pHost.Y - side / 2.0;

        _miniMap.SetViewbox(new Rect(vx, vy, side, side), _isSmoothingFollow);
    }

    private MiniMapWindow? _miniMap;
    private VisualBrush? _miniMapBrush;
    // z.B. Click-Handler deines „Mini-Map“-Buttons:
    public void EnsureMiniMapOpen()
    {
        if (_miniMap == null || !_miniMap.IsVisible)
        {
            BtnToggleMiniMap_Click(null, null);
        }
    }

    private async void BtnToggleMiniMap_Click(object? sender, RoutedEventArgs? e)
    {
        if (_vm.Selected?.IsFullConnected != true)
        {
            var prompt = new Wpf.Ui.Controls.MessageBox
            {
                Title = Properties.Resources.GetString("Tutorials.Step.minimap.intro.Title") ?? "Serververbindung erforderlich",
                Content = Properties.Resources.GetString("Tutorials.Step.minimap.intro.Description") ?? "Bitte verbinde dich zuerst mit einem Server, um die Mini-Map zu nutzen.",
                CloseButtonText = "OK",
                ShowTitle = true,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            await prompt.ShowDialogAsync();
            return;
        }

        if (_isMap3DActive)
            CloseMap3DView();

        if (_miniMap == null || !_miniMap.IsVisible)

        {
            
            // WICHTIG: mapRoot muss dein existierendes Karten-Root-Element sein!
            // Beispiele: SceneGrid, MapRootGrid, OverlayHostGrid – je nach deinem x:Name.
            var mapRoot = WebViewHost;
            var vb = new VisualBrush(mapRoot)
            {
                // Wir schneiden selbst zu, daher:
                Stretch = Stretch.None,
                ViewboxUnits = BrushMappingMode.Absolute
            };
            _miniMapBrush = vb;


            _miniMap = new MiniMapWindow(mapRoot)
            {
                Left = SystemParameters.WorkArea.Right - 280,
                Top = SystemParameters.WorkArea.Top + 20,
                DataContext = _vm
            };

            _miniMap.OnClicked = () =>
            {
                // Wenn wir jemandem folgen -> auf diesen zentrieren
                if (_vm.IsFollowing && _vm.FollowingSteamId.HasValue)
                {
                    if (TryResolvePosFromDynMarkers(_vm.FollowingSteamId.Value, out var fx, out var fy))
                        CenterMapOnWorldInstant(fx, fy);
                }
                else
                {
                    // Ansonsten auf mich selbst
                    if (TryGetMyWorldPos(out var mx, out var my))
                        CenterMapOnWorldInstant(mx, my);
                }
            };

            _miniMap.Closed += (s, ev) =>
            {
                _miniMap = null;
                UpdateMapViewSelector();
            };

            _miniMap.Show();
            CenterMiniMapOnPlayer();
            UpdateMapViewSelector();

            // Auto-start tutorial if not seen and connected
            if (e != null)
            {
                _ = AutoStartMiniMapTutorialAsync();
            }
        }
        else
        {
            _miniMap.Close();
        }
    }

    private async System.Threading.Tasks.Task AutoStartMiniMapTutorialAsync()
    {
        var mapTutDef = _tutorialRegistry?.Find("mini-map");
        if (mapTutDef != null && _tutorialProgressStore != null && _tutorialService != null)
        {
            var progress = await _tutorialProgressStore.GetAsync(mapTutDef);
            if (progress.Status != RustPlusDesk.Features.Tutorials.TutorialStatus.Completed && 
                progress.Status != RustPlusDesk.Features.Tutorials.TutorialStatus.Skipped)
            {
                await _tutorialService.StartAsync("mini-map");
            }
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
        void OpenCam()
        {
            if (_rust is RustPlusClientReal real)
            {
                var w = new RustPlusDesk.Views.CameraWindow(real, id) { Owner = this };
                _camBusy.Add(id);
                w.Closed += (_, __2) => _camBusy.Remove(id);
                w.Show();
            }
        }

        var subtle = TryFindResource("TextSubtle") as Brush ?? Brushes.Gray;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header (name + id / type)
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // thumbnail + controls

        // ---- Header: name, then id and camera type, above the thumbnail ----
        var header = new StackPanel { Margin = new Thickness(2, 0, 2, 8) };

        var display = CamDisplayName(id);
        var nameLine = new DockPanel { LastChildFill = true };
        var camIcon = new Wpf.Ui.Controls.SymbolIcon
        {
            Symbol = Wpf.Ui.Controls.SymbolRegular.CameraDome24,
            FontSize = 16, Margin = new Thickness(0, 0, 8, 0), Foreground = subtle, VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(camIcon, Dock.Left);
        nameLine.Children.Add(camIcon);
        nameLine.Children.Add(new TextBlock
        {
            Text = display, FontWeight = FontWeights.SemiBold, FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
        });

        var idLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 2, 0, 0) };
        idLine.Children.Add(new TextBlock { Text = id, FontSize = 11.5, Foreground = subtle, Opacity = 0.9, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 160 });
        var sep = new TextBlock { Text = "  \u2022  ", FontSize = 11.5, Foreground = subtle, Opacity = 0.55 };
        var typeText = new TextBlock { FontSize = 11.5, Foreground = subtle, Opacity = 0.9 };
        typeText.Tag = id + "|type";
        idLine.Children.Add(sep);
        idLine.Children.Add(typeText);

        header.Children.Add(nameLine);
        header.Children.Add(idLine);
        Grid.SetRow(header, 0);

        // ---- Content: thumbnail (fills) + vertical control stack with resolution under ----
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var img = new Image
        {
            Stretch = Stretch.UniformToFill,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        img.Tag = id; // the thumb refresher locates the target via this tag
        var imgBorder = new Border
        {
            Height = 150,
            MinWidth = 56,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromRgb(10, 10, 12)),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = img
        };
        imgBorder.MouseDown += (s, ev) => { if (ev.ChangedButton == System.Windows.Input.MouseButton.Left) OpenCam(); };
        Grid.SetColumn(imgBorder, 0);

        var controls = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };

        Wpf.Ui.Controls.Button MakeBtn(Wpf.Ui.Controls.SymbolRegular sym, string tip, double topMargin)
            => new Wpf.Ui.Controls.Button
            {
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = sym },
                Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent,
                Width = 40, Height = 36, Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, topMargin, 0, 0), ToolTip = tip
            };

        var btnEdit = MakeBtn(Wpf.Ui.Controls.SymbolRegular.Rename24, "Edit id / name", 0);
        btnEdit.Click += (_, __) => OpenEditCam(id, btnEdit);
        var btnOpen = MakeBtn(Wpf.Ui.Controls.SymbolRegular.WindowNew24, "Open", 4);
        btnOpen.Click += (_, __) => OpenCam();
        var btnDel = MakeBtn(Wpf.Ui.Controls.SymbolRegular.DeleteDismiss24, "Delete", 4);
        btnDel.Click += (_, __) => OpenDeleteCam(id, btnDel);

        var resText = new TextBlock
        {
            FontSize = 11, Foreground = subtle, Opacity = 0.8,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0),
            TextAlignment = TextAlignment.Center
        };
        resText.Tag = id + "|status";

        controls.Children.Add(btnEdit);
        controls.Children.Add(btnOpen);
        controls.Children.Add(btnDel);
        controls.Children.Add(resText);
        Grid.SetColumn(controls, 1);

        content.Children.Add(imgBorder);
        content.Children.Add(controls);
        Grid.SetRow(content, 1);

        root.Children.Add(header);
        root.Children.Add(content);

        return new Wpf.Ui.Controls.Card { Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(10), Content = root };
    }

    private void EnsureCamThumbPolling()
    {
        _camThumbTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _camThumbTimer.Tick -= CamThumbTimer_Tick;
        _camThumbTimer.Tick += CamThumbTimer_Tick;
        _camThumbTimer.Start();
    }

    // Grabs a single thumbnail by briefly opening a native camera session, letting a few frames
    // accumulate (the image sharpens over successive frames), then disposing it.
    private static async Task<(byte[]? png, int w, int h, string kind)> GrabCameraSnapshotAsync(RustPlusClientReal real, string id)
    {
        CameraSession? session = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            session = await real.CreateCameraSessionAsync(id, cts.Token);
            session.TargetFps = 15;

            var kind = session.IsDrone ? "Drone"
                     : session.IsAutoTurret ? "Auto-turret"
                     : session.IsPtzCamera ? "PTZ"
                     : session.IsStaticCamera ? "Static" : "Camera";

            byte[]? latest = null;
            int frames = 0;
            // The renderer accumulates samples across frames, so a thumbnail needs a couple of
            // seconds of frames to fill in (a handful of frames looks like sparse noise).
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnFrame(byte[] p) { latest = p; if (++frames >= 40) ready.TrySetResult(true); }

            session.FrameRendered += OnFrame;
            try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(8)); } catch { /* use whatever accumulated */ }
            session.FrameRendered -= OnFrame;

            return (latest, session.Width, session.Height, kind);
        }
        catch { return (null, 0, 0, string.Empty); }
        finally
        {
            if (session != null) { try { await session.DisposeAsync(); } catch { } }
        }
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
            var status = FindTagged(cont, id + "|status");
            var typeTb = FindTagged(cont, id + "|type");

            var (png, fw, fh, kind) = await GrabCameraSnapshotAsync(real, id);
            if (png != null)
            {
                var bi = new BitmapImage();
                using var ms = new MemoryStream(png);
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit(); bi.Freeze();
                img.Source = bi;
            }
            // Resolution shows whenever the camera reported dimensions (even for a black frame).
            if (status != null) status.Text = (fw > 0 && fh > 0) ? $"{fw}×{fh}" : RustPlusDesk.Properties.Resources.GetString("CodeUiNoFrame");
            if (typeTb != null && !string.IsNullOrEmpty(kind)) typeTb.Text = kind;
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
        static TextBlock? FindTagged(FrameworkElement root, string tag)
        {
            var q = new Queue<DependencyObject>();
            q.Enqueue(root);
            while (q.Count > 0)
            {
                var x = q.Dequeue();
                if (x is TextBlock tb && (tb.Tag as string) == tag) return tb;
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
