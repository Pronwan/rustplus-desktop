using RustPlusDesk.Models;
using RustPlusDesk.Services.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using RustPlusDesk.Services;
using RustPlusDesk.Services.Auth;
using Supabase.Realtime;
using System.Windows.Threading;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private Dictionary<OverlayToolMode, Button> _toolButtons;

private bool _overlayToolsVisible = false;

    // wer ist aktuell ausgewaehlt als Zeichenwerkzeug?
    private enum OverlayToolMode { None, Draw, Text, Icon, Erase }
    private OverlayToolMode _currentTool = OverlayToolMode.None;

    // Color/Size Settings usw.:
    private Color _drawColor = Colors.Red;
    private double _drawThickness = 2.0;
    private double _eraserSize = 10.0;
    private Color _textColor = Colors.White;
    private double _textSize = 16.0;
    private string _currentIconPath = "pack://application:,,,/Assets/icons/map-icons/base1.png";
    private string _selectedMarkerShape = "pin";
    private string _selectedMarkerColor = "blue";

    // Fuer Draggen von platzierten Icons/Text
    private FrameworkElement? _draggingElement = null;
    private Point _dragOffset;

    // Stroke-Zeichnen
    private bool _isDrawingStroke = false;
    private Polyline? _currentStroke;

    // Overlays der Teammitglieder
    // wer ist aktuell "eingeblendet" (Avatar aktiv)
    private readonly HashSet<ulong> _visibleOverlayOwners = new();

    // pro Spieler: Liste ALLER FrameworkElements (Polylines, Icons, Text) aus seinem Overlay
    private readonly Dictionary<ulong, List<FrameworkElement>> _playerOverlayElements = new();

    // Live-Polling Timer für sichtbare Teammate-Overlays
    private System.Windows.Threading.DispatcherTimer? _overlayPollTimer;
    private string? _lastDevicesCloudTooltip;
    private string? _lastOverlayCloudTooltip;
    private CancellationTokenSource? _overlaySyncCts;
    private const int OverlaySyncDebounceMs = 800;

    private class TeammatePollState
    {
        public ulong SteamId { get; init; }
        public long LastUpdatedUnix { get; set; } = 0;
        public int ConsecutiveUnchangedCount { get; set; } = 0;
        public int SecondsUntilNextPoll { get; set; } = 0;
        public DateTime LastChangeTime { get; set; } = DateTime.UtcNow;
        public bool IsPaused { get; set; } = false;
    }

    private readonly Dictionary<ulong, TeammatePollState> _teammatePollStates = new();
    private readonly HashSet<ulong> _steamIdsWithOverlays = new();
    private int _pulseStep = 0;





    private void RebuildOverlayTeamBar()
    {
        if (OverlayTeamStack == null) return;

        OverlayTeamStack.Children.Clear();

        foreach (var tm in TeamMembers)
        {
            // Wir wollen nur existierende Spieler mit valider SteamID
            if (tm.SteamId == 0)
                continue;

            bool isSubbed = _visibleOverlayOwners.Contains(tm.SteamId);
            bool hasOverlay = tm.SteamId == _mySteamId || _steamIdsWithOverlays.Contains(tm.SteamId);

            // Button mit Avatar als Inhalt
            var btn = new Button
            {
                Width = 32,
                Height = 32,
                Margin = new Thickness(4, 0, 4, 0),
                ToolTip = tm.Name + (!hasOverlay ? " (No cloud overlay available)" : ""),
                Tag = tm.SteamId,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(isSubbed ? 3 : 1),
                BorderBrush = isSubbed
                                ? Brushes.LimeGreen
                                : Brushes.Gray,
                Background = Brushes.Transparent,
                IsEnabled = true, // Always allow clicking to subscribe/fetch
                Opacity = hasOverlay ? 1.0 : 0.6
            };

            btn.Click += OverlayTeamButton_Click;

            var img = new Image
            {
                Width = 32,
                Height = 32,
                Stretch = Stretch.UniformToFill,
                Source = tm.Avatar ?? GetPlaceholderAvatar(),
                SnapsToDevicePixels = true
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            btn.Content = img;
            OverlayTeamStack.Children.Add(btn);
        }
    }

    private ImageSource GetPlaceholderAvatar()
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.DimGray, null, new Rect(0, 0, 32, 32));
        }
        var bmp = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    private async void OverlayTeamButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        if (b.Tag is not ulong steamId) return;

        AppendLog($"[overlay/ui] Click on {steamId} ({GetServerKey()})");

        // ----- FALL 1: Spieler ist gerade sichtbar -> wir blenden ihn aus
        if (_visibleOverlayOwners.Contains(steamId))
        {
            AppendLog($"[overlay/ui] {steamId} currently visible -> hiding");

            _visibleOverlayOwners.Remove(steamId);
            _teammatePollStates.Remove(steamId);

            if (_playerOverlayElements.TryGetValue(steamId, out var listToHide))
            {
                foreach (var fe in listToHide)
                    fe.Visibility = Visibility.Collapsed;
            }

            // Stop the poll timer if no more teammates are visible
            bool anyTeammates = _visibleOverlayOwners.Any(id => id != _mySteamId);
            if (!anyTeammates)
                StopOverlayPollTimer();

            RebuildOverlayTeamBar();
            UpdateSubscriptionDock();
            UpdateSavedSubscriptionsInProfile();
            return;
        }

        // ----- FALL 2: Spieler ist nicht sichtbar -> wir wollen ihn einblenden
        AppendLog($"[overlay/ui] {steamId} currently NOT visible -> showing / (re)loading if needed");

        if (steamId != _mySteamId)
        {
            int subCount = _visibleOverlayOwners.Count(id => id != _mySteamId);
            if (subCount >= 5)
            {
                AppendLog("[overlay/subscription] Maximum of 5 active subscriptions reached. Please unsubscribe from another player first.");
                MessageBox.Show("Maximum of 5 active teammate subscriptions reached. Please unsubscribe from someone else first.", "Subscription Limit", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        _visibleOverlayOwners.Add(steamId);

        if (!_teammatePollStates.TryGetValue(steamId, out var st))
        {
            st = new TeammatePollState { SteamId = steamId, SecondsUntilNextPoll = 0 };
            _teammatePollStates[steamId] = st;
        }
        else
        {
            st.IsPaused = false;
            st.SecondsUntilNextPoll = 0;
            st.ConsecutiveUnchangedCount = 0;
            st.LastChangeTime = DateTime.UtcNow;
        }

        // Start live-poll timer when a teammate (not ourselves) becomes visible
        if (steamId != _mySteamId)
            StartOverlayPollTimer();

        var localPath = GetOverlayJsonPathForPlayerServer(steamId);
        bool hadLocalBefore = File.Exists(localPath);
        AppendLog($"[overlay/ui] local file {(hadLocalBefore ? "exists" : "does NOT exist")} at {localPath}");

        // 1. Versuch: vom Server holen (kann 200, 404 oder Fehler sein)
        bool serverGaveNewData = await TryFetchAndUpdateOverlayAsync(steamId);

        AppendLog($"[overlay/ui] serverGaveNewData={serverGaveNewData}");

        // 2. Schauen ob wir schon Canvas-Objekte im Speicher haben
        bool alreadyBuiltInMemory =
            _playerOverlayElements.TryGetValue(steamId, out var existingList)
            && existingList != null
            && existingList.Count > 0;

        AppendLog($"[overlay/ui] alreadyBuiltInMemory={alreadyBuiltInMemory} " +
                  $"(count={(existingList?.Count ?? 0)})");

        // 3. Muessen wir neu bauen?
        bool needRebuild = !alreadyBuiltInMemory || serverGaveNewData;
        AppendLog($"[overlay/ui] needRebuild={needRebuild}");

        if (needRebuild)
        {
            if (File.Exists(localPath))
            {
                // bevor rebuild: alte Elemente entfernen
                if (alreadyBuiltInMemory && existingList != null)
                {
                    AppendLog($"[overlay/ui] Removing {existingList.Count} old canvas elements for {steamId} before rebuild");
                    foreach (var fe in existingList)
                        Overlay.Children.Remove(fe);
                }

                AppendLog($"[overlay/ui] Rebuilding overlay for {steamId} from disk {localPath}");
                LoadOverlayForPlayerFromJson(steamId);
            }
            else
            {
                AppendLog($"[overlay/ui] No local JSON for {steamId} after fetch -> creating empty entry");
                if (!_playerOverlayElements.ContainsKey(steamId))
                    _playerOverlayElements[steamId] = new List<FrameworkElement>();
            }
        }

        // 4. Sichtbar machen, was wir haben
        if (_playerOverlayElements.TryGetValue(steamId, out var listNow))
        {
            AppendLog($"[overlay/ui] Final step: showing {listNow.Count} elements for {steamId}");
            foreach (var fe in listNow)
                fe.Visibility = Visibility.Visible;
        }
        else
        {
            AppendLog($"[overlay/ui][warn] No entry in _playerOverlayElements for {steamId}, creating empty list");
            _playerOverlayElements[steamId] = new List<FrameworkElement>();
        }

        RebuildOverlayTeamBar();
        UpdateSubscriptionDock();
        UpdateSavedSubscriptionsInProfile();
    }


    private void LoadOverlayForPlayerFromJson(ulong steamId)
    {
        var path = GetOverlayJsonPathForPlayerServer(steamId);

        if (!File.Exists(path))
        {
            AppendLog($"[overlay/disk] {steamId}: no local file at {path}, registering empty");
            if (!_playerOverlayElements.ContainsKey(steamId))
                _playerOverlayElements[steamId] = new List<FrameworkElement>();
            return;
        }

        OverlaySaveData? data = null;
        try
        {
            var json = File.ReadAllText(path);
            data = System.Text.Json.JsonSerializer.Deserialize<OverlaySaveData>(json);
        }
        catch (Exception ex)
        {
            AppendLog($"[overlay/disk][err] {steamId}: failed to parse {path}: {ex.Message}");
            data = null;
        }

        if (data == null)
        {
            AppendLog($"[overlay/disk][warn] {steamId}: parsed data == null, registering empty");
            _playerOverlayElements[steamId] = new List<FrameworkElement>();
            return;
        }

        bool editable = (steamId == _mySteamId);

        AppendLog($"[overlay/disk] {steamId}: materializing from {path} " +
                  $"(strokes={data.Strokes.Count}, icons={data.Icons.Count}, texts={data.Texts.Count}, editable={editable})");

        MaterializeOverlayForPlayer(steamId, data, editable);

        // Nach MaterializeOverlayForPlayer weisst du,
        // wie viele Elemente der Spieler jetzt wirklich auf dem Canvas hat:
        if (_playerOverlayElements.TryGetValue(steamId, out var listBuilt))
        {
            AppendLog($"[overlay/disk] {steamId}: materialized {listBuilt.Count} canvas elements");
        }
    }


    private async Task<bool> TryFetchAndUpdateOverlayAsync(ulong steamId, bool silent = false)
    {
        try
        {
            var remoteData = await OverlayDataModule.FetchOverlayFromServerAsync(GetServerKey(), steamId);
            if (remoteData == null)
            {
                // Only log when not in silent poll mode (i.e. explicit button click)
                if (!silent)
                    AppendLog($"[overlay/net] {steamId}: no remote overlay found.");
                return false;
            }

            if (steamId == _mySteamId)
            {
                var localData = OverlayDataModule.LoadLocalOverlay(GetServerKey(), steamId);
                long localTs  = localData?.LastUpdatedUnix ?? 0;
                long remoteTs = remoteData.LastUpdatedUnix;

                if (remoteTs > localTs)
                {
                    OverlayDataModule.SaveLocalOverlay(GetServerKey(), steamId, remoteData);
                    if (!silent)
                        AppendLog($"[overlay/net] self: pulled newer cloud overlay (remote={remoteTs} > local={localTs})");
                    return true;
                }
                else
                {
                    if (!silent)
                        AppendLog($"[overlay/net] self: local overlay is newer or same, kept local.");
                    return false;
                }
            }
            else
            {
                // For teammates, always trust remote (they painted it)
                OverlayDataModule.SaveLocalOverlay(GetServerKey(), steamId, remoteData);
                return true;
            }
        }
        catch (Exception ex)
        {
            if (!silent)
                AppendLog("[overlay/net][err] fetch error for " + steamId + ": " + ex.Message);
            return false;
        }
    }

    private Image BuildPlayerOverlayImageFor(ulong steamId)
    {
        string path = GetOverlayPngPathForPlayer(steamId);

        BitmapImage? bi = null;
        if (File.Exists(path))
        {
            using (var fs = File.OpenRead(path))
            {
                var tmp = new BitmapImage();
                tmp.BeginInit();
                tmp.CacheOption = BitmapCacheOption.OnLoad;
                tmp.StreamSource = fs;
                tmp.EndInit();
                tmp.Freeze();
                bi = tmp;
            }
        }

        // Fallback: leeres transparentes "nichts", falls noch keine Datei existiert,
        // damit unser Code nicht crasht, wenn jemand klickt ohne PNG zu haben.
        int mapW = (int)(ImgMap?.Source?.Width ?? 2048);   // fallback
        int mapH = (int)(ImgMap?.Source?.Height ?? 2048);  // fallback

        var img = new Image
        {
            Source = bi,
            Width = bi?.PixelWidth ?? mapW,
            Height = bi?.PixelHeight ?? mapH,
            Opacity = 0.8,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,

            // wichtig: gleiche Transform wie Map, damit es mitzoomt/panned
            RenderTransform = MapTransform
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

        Canvas.SetLeft(img, 0);
        Canvas.SetTop(img, 0);

        return img;
    }

    private string GetOverlayPngPathForPlayer(ulong steamId)
    {
        var baseDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RustPlusDesk",
            "Overlays",
            GetServerKey()
        );
        Directory.CreateDirectory(baseDir);
        return System.IO.Path.Combine(baseDir, $"{steamId}.png");
    }
    // Folgende Methode ersetzt durch LoadOverlayFromDiskForPlayer --
    private void LoadOwnOverlayFromJson()
    {
        // Falls wir fuer mich (_mySteamId) schon Elemente gebaut haben, nicht nochmal
        if (_playerOverlayElements.ContainsKey(_mySteamId) &&
            _playerOverlayElements[_mySteamId].Count > 0)
        {
            return;
        }

        var path = GetOverlayJsonPathForPlayerServer(_mySteamId);
        if (!File.Exists(path))
        {
            // Stelle sicher, dass wir zumindest einen leeren Eintrag haben,
            // damit spaetere Checks nicht glauben "muss noch laden".
            _playerOverlayElements[_mySteamId] = new List<FrameworkElement>();
            return;
        }

        OverlaySaveData? data = null;
        try
        {
            var json = File.ReadAllText(path);
            data = System.Text.Json.JsonSerializer.Deserialize<OverlaySaveData>(json);
        }
        catch
        {
            // kaputte Datei? -> wir tun so, als gaebe es keine
            data = null;
        }

        if (data == null)
        {
            _playerOverlayElements[_mySteamId] = new List<FrameworkElement>();
            return;
        }

        // Wir bauen jetzt meine Shapes.
        var myList = new List<FrameworkElement>();

        // 1) Strokes
        foreach (var stroke in data.Strokes)
        {
            var pl = new Polyline
            {
                StrokeThickness = stroke.Thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeStartLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
                Opacity = 1.0,
                Tag = new OverlayTag
                {
                    OwnerSteamId = _mySteamId,
                    IsUserEditable = true
                }
            };

            if (ColorConverter.ConvertFromString(stroke.Color) is Color c)
                pl.Stroke = new SolidColorBrush(c);

            foreach (var p in stroke.Points)
                pl.Points.Add(p);

            Overlay.Children.Add(pl);
            myList.Add(pl);
            RegisterElementForOwner(_mySteamId, pl);
        }

        // 2) Icons
        foreach (var icon in data.Icons)
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(icon.IconPath.Replace("/icons/", "/Assets/icons/"), UriKind.RelativeOrAbsolute);
            bi.EndInit();
            bi.Freeze();

            var img = new Image
            {
                Source = bi,
                Width = icon.Width,
                Height = icon.Height,
                RenderTransformOrigin = new Point(0.5, 0.5),
                IsHitTestVisible = true, // meine Icons/Text darf ich draggen und loeschen
                Opacity = 1.0,
                Tag = new OverlayTag
                {
                    OwnerSteamId = _mySteamId,
                    IsUserEditable = true
                }
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            Canvas.SetLeft(img, icon.X);
            Canvas.SetTop(img, icon.Y);

            Overlay.Children.Add(img);
            myList.Add(img);
        }

        // 3) Texts
        foreach (var txt in data.Texts)
        {
            var tb = new TextBlock
            {
                Text = txt.Content,
                FontSize = txt.FontSize,
                FontWeight = txt.Bold ? FontWeights.Bold : FontWeights.Normal,
                IsHitTestVisible = true,
                Opacity = 1.0,
                Tag = new OverlayTag
                {
                    OwnerSteamId = _mySteamId,
                    IsUserEditable = true
                }
            };

            if (ColorConverter.ConvertFromString(txt.Color) is Color tc)
                tb.Foreground = new SolidColorBrush(tc);

            Canvas.SetLeft(tb, txt.X);
            Canvas.SetTop(tb, txt.Y);

            Overlay.Children.Add(tb);
            myList.Add(tb);
        }

        // Abschluss: registrieren
        _playerOverlayElements[_mySteamId] = myList;

        // Sichtbarkeit / Auswahl-Status:
        // Wir wollen, dass mein Overlay direkt sichtbar und in der Auswahl aktiv ist,
        // also packen wir mich in _visibleOverlayOwners.
        _visibleOverlayOwners.Add(_mySteamId);
    }

    private void HandleOverlayMouseDown(MouseButtonEventArgs e, Point mapPos)
    {
        // 1) DRAW
        if (_currentTool == OverlayToolMode.Draw &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            _isDrawingStroke = true;
            _currentStroke = new Polyline
            {
                Stroke = new SolidColorBrush(_drawColor),
                StrokeThickness = _drawThickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeStartLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
                Tag = new OverlayTag { OwnerSteamId = _mySteamId, IsUserEditable = true }
            };
            _currentStroke.Points.Add(mapPos);
            Overlay.Children.Add(_currentStroke);

            // registrieren
            RegisterElementForOwner(_mySteamId, _currentStroke);
            return;
        }

        // 2) ICON
        if (_currentTool == OverlayToolMode.Icon &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            PlaceIconAt(mapPos);
            return;
        }

        // 3) TEXT
        if (_currentTool == OverlayToolMode.Text &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            PlaceTextAt(mapPos);
            return;
        }

        // 4) ERASE
        if (_currentTool == OverlayToolMode.Erase &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            EraseAt(mapPos);
            return;
        }

        // 5) Drag bestehender Elemente (wenn kein spezielles Tool aktiv ist,
        //    oder wir explizit Drag erlauben bei Icon/Text in anderen Tools ausser Draw/Text/Icon/Erase)
        if (e.LeftButton == MouseButtonState.Pressed &&
            _currentTool == OverlayToolMode.None)
        {
            TryBeginDragExistingElement(mapPos);
            return;
        }

        // 6) Rechtsklick zum Loeschen von Icons/Text-Bloecken
        if (e.ChangedButton == MouseButton.Right)
        {
            TryDeleteElementAt(mapPos);
            return;
        }
    }

    private void EraseAt(Point mapPos)
    {
        var toRemove = new List<FrameworkElement>();

        for (int i = 0; i < Overlay.Children.Count; i++)
        {
            if (Overlay.Children[i] is FrameworkElement fe)
            {
                // nur wenn mir gehoerend, sonst Finger weg
                if (fe.Tag is OverlayTag meta && meta.OwnerSteamId == _mySteamId && meta.IsUserEditable)
                {
                    if (fe is Polyline line)
                    {
                        double dist = DistancePointToPolyline(mapPos, line);
                        if (dist <= _eraserSize)
                        {
                            toRemove.Add(line);
                        }
                    }
                    else
                    {
                        // Text (TextBlock) oder Icon (Image)
                        double x = Canvas.GetLeft(fe);
                        double y = Canvas.GetTop(fe);

                        // Wenn WPF noch kein ActualWidth/Height gemessen hat, fallback:
                        double w = !double.IsNaN(fe.Width) ? fe.Width : (fe.ActualWidth > 0 ? fe.ActualWidth : 32);
                        double h = !double.IsNaN(fe.Height) ? fe.Height : (fe.ActualHeight > 0 ? fe.ActualHeight : 16);

                        // Check ob der Radierer-Punkt im Bereich des Elements liegt (plus Puffer durch eraserSize)
                        if (mapPos.X >= x - _eraserSize && mapPos.X <= x + w + _eraserSize &&
                            mapPos.Y >= y - _eraserSize && mapPos.Y <= y + h + _eraserSize)
                        {
                            toRemove.Add(fe);
                        }
                    }
                }
            }
        }

        foreach (var fe in toRemove)
        {
            Overlay.Children.Remove(fe);

            // auch aus _playerOverlayElements[_mySteamId] rauswerfen
            if (_playerOverlayElements.TryGetValue(_mySteamId, out var mine))
            {
                mine.Remove(fe);
            }
        }

        if (toRemove.Count > 0)
            SaveOwnOverlayToJson();
    }

    private void HandleOverlayMouseMove(MouseEventArgs e, Point mapPos)
    {
        // Stroke weiterzeichnen
        if (_currentTool == OverlayToolMode.Draw &&
            _isDrawingStroke &&
            _currentStroke != null)
        {
            _currentStroke.Points.Add(mapPos);
            return;
        }

        if (_currentTool == OverlayToolMode.Erase &&
    e.LeftButton == MouseButtonState.Pressed)
        {
            EraseAt(mapPos);
            return;
        }

        // Drag laufend verschieben
        if (_draggingElement != null &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            Canvas.SetLeft(_draggingElement, mapPos.X - _dragOffset.X);
            Canvas.SetTop(_draggingElement, mapPos.Y - _dragOffset.Y);
            return;
        }
    }

    private void HandleOverlayMouseUp(MouseButtonEventArgs e, Point mapPos)
    {
        if (_currentTool == OverlayToolMode.Draw)
        {
            _isDrawingStroke = false;
            _currentStroke = null;
            SaveOwnOverlayToJson();
        }

        if (_currentTool == OverlayToolMode.Erase)
        {
            SaveOwnOverlayToJson();
        }

        if (_draggingElement != null)
        {
            _draggingElement = null;
            SaveOwnOverlayToJson();
        }
    }

    private double _activeIconSize = 24;   // hier stellst du "doppelt so gross" ein
    private const double USER_ICON_MIN_SCALE = 0.2;  // nicht kleiner werden
    private const double USER_ICON_MAX_SCALE = 2.0;   // nicht riesig werden


    private void PlaceIconAt(Point mapPos)
    {
        // 1. aktuellen effektiven Zoom holen (das ist der gleiche wie bei Shops/Playern)
        double eff = GetEffectiveZoom();

        // 2. "gewuenschte" Skalierung aus Zoom ableiten
        double scale = 1.0 / eff;

        // 3. auf min / max clampen - GENAU wie im Refresh
        if (scale < USER_ICON_MIN_SCALE)
            scale = USER_ICON_MIN_SCALE;
        if (scale > USER_ICON_MAX_SCALE)
            scale = USER_ICON_MAX_SCALE;

        // 4. Icon bauen
        FrameworkElement elementToPlace;
        bool isCustom = _currentIconPath.StartsWith("rust-marker://");
        if (isCustom)
        {
            elementToPlace = CreateRustMarkerElement(_currentIconPath, _activeIconSize, _activeIconSize, true, _mySteamId, null, new List<string>());
        }
        else
        {
            var img = new Image
            {
                Source = new BitmapImage(new Uri(_currentIconPath, UriKind.RelativeOrAbsolute)),
                Width = _activeIconSize,
                Height = _activeIconSize,
                RenderTransformOrigin = new Point(0.5, 0.5),
                IsHitTestVisible = false
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            var grid = new Grid
            {
                Width = _activeIconSize,
                Height = _activeIconSize,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                RenderTransformOrigin = new Point(0.5, 0.5),
                IsHitTestVisible = true,
                Tag = new OverlayTag
                {
                    OwnerSteamId = _mySteamId,
                    IsUserEditable = true,
                    BaseSize = _activeIconSize,
                    Note = null,
                    Screenshots = new List<string>(),
                    CustomIconPath = _currentIconPath
                }
            };
            grid.Children.Add(img);
            elementToPlace = grid;
        }

        elementToPlace.RenderTransform = new ScaleTransform(scale, scale);

        bool isBase = !isCustom && (_currentIconPath.Contains("base1.png") || _currentIconPath.Contains("base2.png"));

        if (isBase)
        {
            elementToPlace.MouseEnter += BaseIcon_MouseEnter;
            elementToPlace.MouseLeave += BaseIcon_MouseLeave;
            elementToPlace.MouseLeftButtonUp += BaseIcon_MouseLeftButtonUp;
        }

        // 5. Canvas-Position IMMER aus der Basisgroesse ableiten
        Canvas.SetLeft(elementToPlace, mapPos.X - _activeIconSize / 2);
        Canvas.SetTop(elementToPlace, mapPos.Y - _activeIconSize / 2);

        // 6. ins Overlay
        Overlay.Children.Add(elementToPlace);
        RegisterElementForOwner(_mySteamId, elementToPlace);

        // 7. speichern (nimmt BASIS-W/H, nicht die skalierten Pixel - das ist korrekt!)
        SaveOwnOverlayToJson();

        // 8. Bei Base-Icons: direkt Screenshot-Dialog oeffnen (keine Galerie auf Placement-Klick)
        if (isBase && elementToPlace.Tag is OverlayTag baseMeta)
        {
            int maxScreenshots = Services.Auth.SupabaseAuthManager.GetMaxScreenshotsPerBase();
            if (baseMeta.Screenshots.Count < maxScreenshots)
            {
                var dlg = new Views.Windows.BaseScreenshotWindow { Owner = this };
                if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.Base64Result))
                {
                    baseMeta.Screenshots.Add(dlg.Base64Result);
                    if (elementToPlace is Grid g) UpdateScreenshotIndicator(g, baseMeta.Screenshots);
                    SaveOwnOverlayToJson();
                    UploadOwnOverlayToTeam();
                }
            }
        }
    }

    private void RefreshUserOverlayIcons()
    {
        if (Overlay == null) return;

        double eff = GetEffectiveZoom();
        double scale = 1.0 / eff;

        // Untergrenze
        if (scale < USER_ICON_MIN_SCALE)
            scale = USER_ICON_MIN_SCALE;

        // OBERgrenze - hier kommt dein maxScale hin
        if (scale > USER_ICON_MAX_SCALE)
            scale = USER_ICON_MAX_SCALE;

        foreach (var child in Overlay.Children)
        {
            if (child is FrameworkElement fe && fe.Tag is OverlayTag meta)
            {
                if (fe is Image || fe is Grid)
                {
                    fe.RenderTransformOrigin = new Point(0.5, 0.5);
                    fe.RenderTransform = new ScaleTransform(scale, scale);
                }
            }
        }
    }

    private void PlaceTextAt(Point mapPos)
    {
        var input = PromptText("Enter description:", "Add Text", "");
        if (string.IsNullOrWhiteSpace(input))
            return;

        var tb = new TextBlock
        {
            Text = input,
            Foreground = new SolidColorBrush(_textColor),
            FontSize = _textSize,
            FontWeight = FontWeights.Bold,
            Tag = new OverlayTag { OwnerSteamId = _mySteamId, IsUserEditable = true }
        };

        Canvas.SetLeft(tb, mapPos.X);
        Canvas.SetTop(tb, mapPos.Y);

        Overlay.Children.Add(tb);
        RegisterElementForOwner(_mySteamId, tb);

        SaveOwnOverlayToJson();
    }

    private void TryBeginDragExistingElement(Point mapPos)
    {
        for (int i = Overlay.Children.Count - 1; i >= 0; i--)
        {
            if (Overlay.Children[i] is FrameworkElement fe)
            {
                // nur meine editierbaren Elemente duerfen gezogen werden
                if (fe.Tag is not OverlayTag meta) continue;
                if (meta.OwnerSteamId != _mySteamId) continue;
                if (!meta.IsUserEditable) continue;

                double x = Canvas.GetLeft(fe);
                double y = Canvas.GetTop(fe);

                double w = !double.IsNaN(fe.Width) ? fe.Width :
                           (fe.ActualWidth > 0 ? fe.ActualWidth : 32);
                double h = !double.IsNaN(fe.Height) ? fe.Height :
                           (fe.ActualHeight > 0 ? fe.ActualHeight : 16);

                if (mapPos.X >= x && mapPos.X <= x + w &&
                    mapPos.Y >= y && mapPos.Y <= y + h)
                {
                    _draggingElement = fe;
                    _dragOffset = new Point(mapPos.X - x, mapPos.Y - y);
                    break;
                }
            }
        }
    }

    private void TryDeleteElementAt(Point mapPos)
    {
        // Loesche Icon/Text bei Rechtsklick, aber nur mein eigenes Zeug
        for (int i = Overlay.Children.Count - 1; i >= 0; i--)
        {
            if (Overlay.Children[i] is FrameworkElement fe)
            {
                // Lines (Polyline) ignorieren wir hier weiter, die macht Eraser.
                if (fe is Polyline) continue;

                // Besitz pruefen
                if (fe.Tag is not OverlayTag meta) continue;
                if (meta.OwnerSteamId != _mySteamId) continue;
                if (!meta.IsUserEditable) continue;

                double x = Canvas.GetLeft(fe);
                double y = Canvas.GetTop(fe);

                // Wenn WPF noch kein ActualWidth/Height gemessen hat, fallback:
                double w = !double.IsNaN(fe.Width) ? fe.Width : (fe.ActualWidth > 0 ? fe.ActualWidth : 32);
                double h = !double.IsNaN(fe.Height) ? fe.Height : (fe.ActualHeight > 0 ? fe.ActualHeight : 16);

                if (mapPos.X >= x && mapPos.X <= x + w &&
                    mapPos.Y >= y && mapPos.Y <= y + h)
                {
                    Overlay.Children.RemoveAt(i);

                    // auch aus _playerOverlayElements[_mySteamId] rauswerfen
                    if (_playerOverlayElements.TryGetValue(_mySteamId, out var mine))
                    {
                        mine.Remove(fe);
                    }

                    SaveOwnOverlayToJson();
                    break;
                }
            }
        }
    }

    private void RegisterElementForOwner(ulong owner, FrameworkElement fe)
    {
        if (!_playerOverlayElements.TryGetValue(owner, out var list))
        {
            list = new List<FrameworkElement>();
            _playerOverlayElements[owner] = list;
        }
        list.Add(fe);
    }

    private void Icon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            _draggingElement = fe;
            var mousePos = HostToScenePreTransform(e.GetPosition(WebViewHost));
            _dragOffset = new Point(mousePos.X - Canvas.GetLeft(fe), mousePos.Y - Canvas.GetTop(fe));
            e.Handled = true;
        }
    }

    private void Icon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggingElement = null;
        e.Handled = true;
    }

    // Doppelklick -> rename
    private void Icon_MouseDoubleClickCheck(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement fe)
        {
            // Popup InputBox -> setze fe.Tag = newName etc.
            e.Handled = true;
        }
    }

    // Rechtsklick -> loeschen
    private void Icon_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            Overlay.Children.Remove(fe);
            e.Handled = true;
        }
    }

    private void BtnToggleOverlayTools_Click(object sender, RoutedEventArgs e)
    {
        _overlayToolsVisible = !_overlayToolsVisible;

        OverlayToolbox.Visibility = _overlayToolsVisible ? Visibility.Visible : Visibility.Collapsed;
        OverlayTeamBar.Visibility = _overlayToolsVisible ? Visibility.Visible : Visibility.Collapsed;

        if (!_overlayToolsVisible)
        {
            _currentTool = OverlayToolMode.None;
            _draggingElement = null;
            UpdateToolButtonHighlights();
            UpdateOptionsPanelVisibility();

            BtnToggleOverlayTools.ClearValue(Control.BackgroundProperty);
            BtnToggleOverlayTools.ClearValue(Control.BorderBrushProperty);
        }
        else
        {
            RebuildOverlayTeamBar();
            _ = Task.Run(async () =>
            {
                await FetchSteamIdsWithOverlaysAsync();
                Dispatcher.Invoke(() => RebuildOverlayTeamBar());
            });
            UpdateToolButtonHighlights();
            UpdateOptionsPanelVisibility();

            BtnToggleOverlayTools.Background = new SolidColorBrush(Color.FromArgb(50, 0, 150, 255));
            BtnToggleOverlayTools.BorderBrush = new SolidColorBrush(Colors.DodgerBlue);
        }

    }

    private void ToolDrawButton_Click(object sender, RoutedEventArgs e)
    {
        SetCurrentTool(OverlayToolMode.Draw);
    }

    private void ToolDrawButton_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowDrawSettingsContextMenu(sender as FrameworkElement);
    }

    private void ToolTextButton_Click(object sender, RoutedEventArgs e)
    {
        SetCurrentTool(OverlayToolMode.Text);
    }

    private void ToolTextButton_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowTextSettingsContextMenu(sender as FrameworkElement);
    }

    private void ToolIconButton_Click(object sender, RoutedEventArgs e)
    {
        SetCurrentTool(OverlayToolMode.Icon);
    }

    private void ToolIconButton_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowIconSelectContextMenu(sender as FrameworkElement);
    }

    private void ToolEraseButton_Click(object sender, RoutedEventArgs e)
    {
        SetCurrentTool(OverlayToolMode.Erase);
    }

    private void ToolEraseButton_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowEraserSettingsContextMenu(sender as FrameworkElement);
    }

    private void ToolTrashButton_Click(object sender, RoutedEventArgs e)
    {
        // 1. Alle meine Elemente vom Overlay entfernen
        if (_playerOverlayElements.TryGetValue(_mySteamId, out var mine))
        {
            foreach (var el in mine)
                Overlay.Children.Remove(el);

            mine.Clear();
        }

        // 2. Sicherheits-Cleanup fuer evtl. uebriggebliebene Ownerelemente
        var cleanup = new List<UIElement>();
        foreach (var child in Overlay.Children)
        {
            if (child is FrameworkElement fe &&
                fe.Tag is OverlayTag meta &&
                meta.OwnerSteamId == _mySteamId)
            {
                cleanup.Add(fe);
            }
        }
        foreach (var dead in cleanup)
            Overlay.Children.Remove(dead);

        // 3. Neues leeres Overlay lokal speichern (Devices beibehalten)
        SaveOwnOverlayToJson();

        // 4. Expliziten Wipe in die Cloud pushen (explicitWipe=true umgeht den Wipe-Schutz)
        if (TrackingService.CloudSyncEnabled && Services.Auth.SupabaseAuthManager.Client != null)
        {
            var sk  = GetServerKey();
            var sid = _mySteamId;
            var emptyWithDevices = BuildCurrentOverlaySaveDataForMe(); // Strokes/Icons/Texts=leer, Devices bleiben
            emptyWithDevices.Devices.Clear();
            if (_vm.Selected?.Devices != null)
            {
                foreach (var dev in _vm.Selected.Devices)
                    emptyWithDevices.Devices.Add(MapDeviceToDto(dev));
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    await OverlayDataModule.UploadOverlayAsync(sk, sid, emptyWithDevices, explicitWipe: true);
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => AppendLog("[overlay/cloud] Clear-wipe failed: " + ex.Message));
                }
            });
        }
    }

    private void ToolUploadButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsOverlaySyncLimitExceeded())
        {
            ShowPremiumLimitDialog(Properties.Resources.PremiumInfoMapDesc);
            return;
        }

        if (TrackingService.CloudSyncEnabled)
        {
            // Disable sync
            TrackingService.CloudSyncEnabled = false;
            TrackingService.UploadConsentGiven = false;
            _ = Services.Auth.SupabaseAuthManager.UpdateCloudSyncConsentAsync(false);
        }
        else
        {
            // Enable sync
            if (!TrackingService.UploadConsentGiven)
            {
                var dlg = new CloudDisclaimerWindow { Owner = this };
                dlg.ShowDialog();
                if (!dlg.CloudSyncAccepted)
                {
                    TrackingService.CloudSyncEnabled = false;
                    TrackingService.UploadConsentGiven = false;
                    _ = Services.Auth.SupabaseAuthManager.UpdateCloudSyncConsentAsync(false);
                    UpdateCloudSyncUI();
                    return;
                }
                TrackingService.CloudSyncEnabled = true;
                TrackingService.UploadConsentGiven = true;
                _ = Services.Auth.SupabaseAuthManager.UpdateCloudSyncConsentAsync(true);
            }
            else
            {
                TrackingService.CloudSyncEnabled = true;
                _ = Services.Auth.SupabaseAuthManager.UpdateCloudSyncConsentAsync(true);
            }

            // Immediately sync current state
            UploadOwnOverlayToTeam();
        }
        UpdateCloudSyncUI();
    }

    public void UpdateCloudSyncUI()
    {
        bool isDiscord = Services.Auth.SupabaseAuthManager.IsDiscordAuthenticated;
        bool isEmail = Services.Auth.SupabaseAuthManager.IsEmailAuthenticated;
        _vm.IsCloudConnected = isDiscord || isEmail;
        _vm.CloudAccountActionText = _vm.IsCloudConnected ? "Manage" : "Sign in";
        _vm.CloudAccountStatusText = _vm.IsCloudConnected
            ? $"{(isDiscord ? "Discord" : "Email")} · {Services.Auth.SupabaseAuthManager.CurrentTier.Replace('_', ' ')} plan"
            : "Not signed in · sync and backups unavailable";
        UpdateAppTitle();

        bool deviceLimitExceeded = IsFreeDeviceSyncLimitExceeded();
        int overlaySizeBytes = GetCurrentOverlaySizeBytes();
        bool overlayLimitExceeded = IsOverlaySyncLimitExceeded(overlaySizeBytes);
        bool baseLimitExceeded = IsBaseLimitExceeded();
        bool screenshotLimitExceeded = IsScreenshotLimitExceeded();

        bool totalLimitExceeded = deviceLimitExceeded || overlayLimitExceeded || baseLimitExceeded || screenshotLimitExceeded;

        ApplyCloudButtonState(BtnDevicesExport, TrackingService.CloudSyncEnabled, totalLimitExceeded);
        ApplyCloudButtonState(ToolUploadButton, TrackingService.CloudSyncEnabled, totalLimitExceeded);

        if (BtnDevicesExport != null)
        {
            BtnDevicesExport.ToolTip = !TrackingService.CloudSyncEnabled
                ? CloudText("CloudTooltipActivate", "Activate Cloud-Sync")
                : totalLimitExceeded
                    ? (deviceLimitExceeded 
                        ? (Services.Auth.SupabaseAuthManager.GetMaxDevices() == 10 && Services.Auth.SupabaseAuthManager.CurrentTier == "free"
                            ? CloudText("CloudTooltipDeviceFreeLimit", "10 Devices max in Free Tier")
                            : string.Format("{0} Devices max in {1} Tier", Services.Auth.SupabaseAuthManager.GetMaxDevices() == int.MaxValue ? "Unlimited" : Services.Auth.SupabaseAuthManager.GetMaxDevices().ToString(), Services.Auth.SupabaseAuthManager.CurrentTier.ToUpper()))
                        : CloudText("CloudTooltipSyncLimitReached", "Sync limit reached"))
                    : _lastDevicesCloudTooltip ?? CloudText("CloudTooltipActive", "Cloud-Sync active");
        }

        if (ToolUploadButton != null)
        {
            ToolUploadButton.ToolTip = !TrackingService.CloudSyncEnabled
                ? CloudText("CloudTooltipActivate", "Activate Cloud-Sync")
                : totalLimitExceeded
                    ? (baseLimitExceeded
                        ? (Services.Auth.SupabaseAuthManager.GetMaxBases() == 2 && Services.Auth.SupabaseAuthManager.CurrentTier == "free"
                            ? CloudText("CloudTooltipBaseLimit", "Base limit reached (Free: max 2 bases, Premium: max 10 bases)")
                            : string.Format("Base limit reached ({0} bases max in {1} Tier)", Services.Auth.SupabaseAuthManager.GetMaxBases() == int.MaxValue ? "unlimited" : Services.Auth.SupabaseAuthManager.GetMaxBases().ToString(), Services.Auth.SupabaseAuthManager.CurrentTier.ToUpper()))
                        : (screenshotLimitExceeded
                            ? string.Format("Screenshot limit per base exceeded (Max {0} screenshots per base)", Services.Auth.SupabaseAuthManager.GetMaxScreenshotsPerBase())
                            : (overlayLimitExceeded
                                ? string.Format(CloudText("CloudTooltipOverlayTooBigFormat", "Overlay Size too big for sync ({0} KB)"), BytesToKb(overlaySizeBytes))
                                : CloudText("CloudTooltipSyncLimitReached", "Sync limit reached"))))
                    : _lastOverlayCloudTooltip ?? CloudText("CloudTooltipActive", "Cloud-Sync active");
        }

        if (BtnPremiumInfoDevices != null)
            BtnPremiumInfoDevices.Visibility = Visibility.Collapsed;

        if (BtnPremiumInfoMap != null)
            BtnPremiumInfoMap.Visibility = Visibility.Collapsed;
    }

    private bool IsFreeDeviceSyncLimitExceeded()
    {
        return Services.Data.DeviceDataModule.CountActualDevices(_vm.Selected?.Devices) > Services.Auth.SupabaseAuthManager.GetMaxDevices();
    }

    private bool IsFreeOverlaySyncLimitExceeded()
    {
        return IsOverlaySyncLimitExceeded();
    }

    private bool IsOverlaySyncLimitExceeded()
    {
        return IsOverlaySyncLimitExceeded(GetCurrentOverlaySizeBytes());
    }

    private bool IsOverlaySyncLimitExceeded(int byteSize)
    {
        return byteSize > Services.Auth.SupabaseAuthManager.GetMaxOverlayBytes();
    }

    public int GetCurrentDevicesCount()
    {
        return Services.Data.DeviceDataModule.CountActualDevices(_vm?.Selected?.Devices);
    }

    public int GetCurrentBaseCount()
    {
        int baseCount = 0;
        foreach (var child in Overlay.Children)
        {
            if (child is Image img && img.Source is BitmapImage bi)
            {
                string path = bi.UriSource?.ToString() ?? "";
                if (path.Contains("base1.png") || path.Contains("base2.png"))
                {
                    if (img.Tag is OverlayTag meta && meta.OwnerSteamId == _mySteamId)
                    {
                        baseCount++;
                    }
                }
            }
        }
        return baseCount;
    }

    private bool IsBaseLimitExceeded()
    {
        return GetCurrentBaseCount() > Services.Auth.SupabaseAuthManager.GetMaxBases();
    }

    private bool IsScreenshotLimitExceeded()
    {
        int maxScreenshots = Services.Auth.SupabaseAuthManager.GetMaxScreenshotsPerBase();
        foreach (var child in Overlay.Children)
        {
            if (child is Image img && img.Source is BitmapImage bi)
            {
                string path = bi.UriSource?.ToString() ?? "";
                if (path.Contains("base1.png") || path.Contains("base2.png"))
                {
                    if (img.Tag is OverlayTag meta && meta.OwnerSteamId == _mySteamId)
                    {
                        if (meta.Screenshots != null && meta.Screenshots.Count > maxScreenshots)
                        {
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }

    public int GetCurrentOverlaySizeBytes()
    {
        try
        {
            var data = BuildCurrentOverlaySaveDataForMe();
            return OverlayDataModule.CalculateUncompressedSize(data);
        }
        catch
        {
            return 0;
        }
    }

    private static int BytesToKb(int bytes)
    {
        return Math.Max(1, (int)Math.Ceiling(bytes / 1024.0));
    }

    private static string CloudText(string key, string fallback)
    {
        return RustPlusDesk.Properties.Resources.ResourceManager.GetString(key) ?? fallback;
    }

    private void ApplyCloudButtonState(Control button, bool syncEnabled, bool limitExceeded)
    {
        if (button == null) return;

        if (!syncEnabled)
        {
            StopCloudPulse(button);
            button.Background = new SolidColorBrush(Color.FromRgb(217, 119, 6)); // Solid desaturated orange
            return;
        }

        if (limitExceeded)
        {
            StopCloudPulse(button);
            button.Background = new SolidColorBrush(Color.FromRgb(232, 97, 26)); // Orange warning
            return;
        }

        StopCloudPulse(button);
        button.Background = new SolidColorBrush(Color.FromRgb(46, 125, 50)); // Desaturated Green (#2E7D32)
    }

    private void MarkDevicesCloudSynced(int count)
    {
        _lastDevicesCloudTooltip = string.Format(
            CloudText("CloudTooltipDeviceSyncedFormat", "{0} devices synced {1}"),
            count,
            DateTime.Now.ToString("HH:mm:ss"));
        UpdateCloudSyncUI();
        if (!IsFreeDeviceSyncLimitExceeded())
            FlashCloudButton(BtnDevicesExport);
    }

    private void MarkOverlayCloudSynced(int byteSize)
    {
        _lastOverlayCloudTooltip = string.Format(
            CloudText("CloudTooltipOverlaySyncedFormat", "Overlay Synced {0} KB {1}"),
            BytesToKb(byteSize),
            DateTime.Now.ToString("HH:mm:ss"));
        UpdateCloudSyncUI();
        if (!IsOverlaySyncLimitExceeded(byteSize) && !IsBaseLimitExceeded())
            FlashCloudButton(ToolUploadButton);
    }

    private void FlashCloudButton(Control button)
    {
        if (button == null) return;

        var brush = SetCloudButtonBrush(button, Color.FromRgb(46, 125, 50));

        var animation = new System.Windows.Media.Animation.ColorAnimation
        {
            From = Color.FromRgb(76, 175, 80),
            To = Color.FromRgb(46, 125, 50),
            Duration = TimeSpan.FromMilliseconds(650),
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
        };

        animation.Completed += (_, __) => button.Background = new SolidColorBrush(Color.FromRgb(46, 125, 50));
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    private void StartCloudPulse(Control button)
    {
        var brush = SetCloudButtonBrush(button, Color.FromRgb(46, 125, 50));

        var animation = new System.Windows.Media.Animation.ColorAnimation
        {
            From = Color.FromRgb(46, 125, 50),
            To = Color.FromRgb(224, 49, 49),
            Duration = TimeSpan.FromMilliseconds(650),
            AutoReverse = true,
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
        };

        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    private void StopCloudPulse(Control button)
    {
        if (button.Background is SolidColorBrush brush && !brush.IsFrozen)
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
    }

    private SolidColorBrush SetCloudButtonBrush(Control button, Color color)
    {
        StopCloudPulse(button);

        var brush = new SolidColorBrush(color);
        button.Background = brush;
        return brush;
    }

    private void ShowPremiumLimitDialog(string message)
    {
        var dlg = new Views.Windows.PremiumInfoWindow(message) { Owner = this };
        dlg.ShowDialog();

        if (dlg.Result == Views.Windows.PremiumInfoResult.StopSync)
        {
            TrackingService.CloudSyncEnabled = false;
            TrackingService.UploadConsentGiven = false;
            _ = Services.Auth.SupabaseAuthManager.UpdateCloudSyncConsentAsync(false);
        }

        UpdateCloudSyncUI();
    }

    // private void SaveOwnOverlayToPng()
    // {
    // 1. Zielgroesse bestimmen
    //     int pixelW = (int)ImgMap.Source.Width;
    //int pixelH = (int)ImgMap.Source.Height;

    //var exportCanvas = new Canvas
    //    {
    //Width = pixelW,
    //Height = pixelH,
    //Background = Brushes.Transparent
    //};

    // 2. Kinder kopieren
    //      foreach (var child in Overlay.Children.OfType<UIElement>())
    //    {
    // Fremde Overlays (Team-Layer) sind Image mit RenderTransform = MapTransform etc.
    // Erkennen wir grob so:
    //       if (child is Image img && _playerOverlayImages.Values.Contains(img))
    //      {
    // Das ist ein fremdes Team-Overlay, nicht unser eigenes -> skip
    //          continue;
    //     }

    // ansonsten klonen wir "oberflaechlich":
    //    UIElement clone = CloneOverlayElementForExport(child);
    //          if (clone != null)
    //exportCanvas.Children.Add(clone);
    //}

    // 3. Rendern
    // exportCanvas.Measure(new Size(pixelW, pixelH));
    //exportCanvas.Arrange(new Rect(0, 0, pixelW, pixelH));

    //    var rtb = new RenderTargetBitmap(pixelW, pixelH, 96, 96, PixelFormats.Pbgra32);
    //rtb.Render(exportCanvas);

    // 4. PNG speichern
    //   var encoder = new PngBitmapEncoder();
    //encoder.Frames.Add(BitmapFrame.Create(rtb));

    //var mySteamId = _mySteamId; // musst du definieren
    //var path = GetOverlayPngPathForPlayer(mySteamId);
    //    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

    //      using (var fs = File.Create(path))
    //    {
    //encoder.Save(fs);
    //}
    //  }

    // sehr einfache, flache Kopie:
    //private UIElement CloneOverlayElementForExport(UIElement element)
    // {
    //   if (element is Polyline pl)
    //  {
    //var clone = new Polyline
    //        {
    //Stroke = pl.Stroke,
    //StrokeThickness = pl.StrokeThickness,
    //StrokeLineJoin = pl.StrokeLineJoin,
    //StrokeEndLineCap = pl.StrokeEndLineCap,
    //StrokeStartLineCap = pl.StrokeStartLineCap
    //};
    //          foreach (var p in pl.Points)
    //clone.Points.Add(p);

    //    Canvas.SetLeft(clone, Canvas.GetLeft(pl));
    //Canvas.SetTop(clone, Canvas.GetTop(pl));
    //       return clone;
    //}
    //      else if (element is Image img && img.Source != null)
    //     {
    //var clone = new Image
    //        {
    //Source = img.Source,
    //Width = img.Width,
    //Height = img.Height,
    //Stretch = img.Stretch
    //};
    //Canvas.SetLeft(clone, Canvas.GetLeft(img));
    //Canvas.SetTop(clone, Canvas.GetTop(img));
    //        return clone;
    //}
    //      else if (element is TextBlock tb)
    //     {
    //var clone = new TextBlock
    //       {
    //Text = tb.Text,
    //Foreground = tb.Foreground,
    //FontSize = tb.FontSize,
    //FontWeight = tb.FontWeight
    //};
    //Canvas.SetLeft(clone, Canvas.GetLeft(tb));
    //Canvas.SetTop(clone, Canvas.GetTop(tb));
    //        return clone;
    //}

    //      return null;
    // }

    private void ShowDrawSettingsContextMenu(FrameworkElement? fe)
    {
        var m = new ContextMenu()
        {
            // wichtig: dein Style aus App.xaml anwenden
            Style = (Style)FindResource("DarkContextMenu")
        };

        // Farbe aendern (nur ein Beispiel)
        var miRed = new MenuItem { Header = "Red" };
        miRed.Click += (_, __) => { _drawColor = Colors.Red; };
        var miGreen = new MenuItem { Header = "Green" };
        miGreen.Click += (_, __) => { _drawColor = Colors.Lime; };
        var miBlue = new MenuItem { Header = "Blue" };
        miBlue.Click += (_, __) => { _drawColor = Colors.DeepSkyBlue; };

        // Stiftdicke
        var miThin = new MenuItem { Header = "Thickness: 3px" };
        miThin.Click += (_, __) => { _drawThickness = 3.0; };
        var miThick = new MenuItem { Header = "Thickness: 10px" };
        miThick.Click += (_, __) => { _drawThickness = 10.0; };

        m.Items.Add(miRed);
        m.Items.Add(miGreen);
        m.Items.Add(miBlue);
        m.Items.Add(new Separator());
        m.Items.Add(miThin);
        m.Items.Add(miThick);

        fe!.ContextMenu = m;
        m.IsOpen = true;
    }

    private void ShowTextSettingsContextMenu(FrameworkElement? fe)
    {
        var m = new ContextMenu() { Style = (Style)FindResource("DarkContextMenu") };

        var miWhite = new MenuItem { Header = "White" };
        miWhite.Click += (_, __) => { _textColor = Colors.White; };
        var miYellow = new MenuItem { Header = "Red" };
        miYellow.Click += (_, __) => { _textColor = Colors.Red; };

        var miSmall = new MenuItem { Header = "Size: 14" };
        miSmall.Click += (_, __) => { _textSize = 14.0; };
        var miBig = new MenuItem { Header = "Size: 40" };
        miBig.Click += (_, __) => { _textSize = 40.0; };

        m.Items.Add(miWhite);
        m.Items.Add(miYellow);
        m.Items.Add(new Separator());
        m.Items.Add(miSmall);
        m.Items.Add(miBig);

        fe!.ContextMenu = m;
        m.IsOpen = true;
    }

    private void ShowIconSelectContextMenu(FrameworkElement? fe)
    {
        var m = new ContextMenu()
        {
            // wichtig: dein Style aus App.xaml anwenden
            Style = (Style)FindResource("DarkContextMenu")
        };

        // map-icons aus deinem Projekt
        m.Items.Add(BuildIconMenuItem("Base #1", "pack://application:,,,/Assets/icons/map-icons/base1.png"));
        m.Items.Add(BuildIconMenuItem("Base #2", "pack://application:,,,/Assets/icons/map-icons/base2.png"));
        m.Items.Add(BuildIconMenuItem("SAM Site", "pack://application:,,,/Assets/icons/map-icons/sam-site.png"));
        m.Items.Add(BuildIconMenuItem("Turret", "pack://application:,,,/Assets/icons/map-icons/turret.png"));

        fe!.ContextMenu = m;
        m.IsOpen = true;
    }

    private MenuItem BuildIconMenuItem(string label, string path)
    {
        var mi = new MenuItem { Header = label };
        mi.Click += (_, __) => { _currentIconPath = path; };
        return mi;
    }

    private void ShowEraserSettingsContextMenu(FrameworkElement? fe)
    {
        var m = new ContextMenu() { Style = (Style)FindResource("DarkContextMenu") };

        var miSmall = new MenuItem { Header = "Eraser small (5px)" };
        miSmall.Click += (_, __) => { _eraserSize = 5.0; };
        var miBig = new MenuItem { Header = "Eraser big (20px)" };
        miBig.Click += (_, __) => { _eraserSize = 20.0; };

        m.Items.Add(miSmall);
        m.Items.Add(miBig);

        fe!.ContextMenu = m;
        m.IsOpen = true;
    }

    private async void UploadOwnOverlayToTeam()
    {
        try
        {
            if (!_ownCloudRestoreReady)
            {
                AppendLog("[overlay/cloud] Upload skipped until cloud restore is complete.");
                return;
            }

            if (IsBaseLimitExceeded())
            {
                AppendLog("[overlay/cloud] Upload skipped: base count limit reached.");
                UpdateCloudSyncUI();
                return;
            }

            if (IsScreenshotLimitExceeded())
            {
                AppendLog("[overlay/cloud] Upload skipped: screenshot limit per base exceeded.");
                UpdateCloudSyncUI();
                return;
            }

            // optional, aber sinnvoll: sicherstellen, dass die lokale Datei "aktuell" ist
            try
            {
                await TryFetchAndUpdateOverlayAsync(_mySteamId);
            }
            catch { /* nicht kritisch */ }

            // 0) vorhandene JSON (fuer Devices) einlesen
            // 1) aktuelles Overlay aus dem Canvas bauen
            var data = BuildCurrentOverlaySaveDataForMe();

            // 2) Devices aus unserem aktuellen Profil (Authoritative List) uebernehmen!
            data.Devices.Clear();
            if (_vm.Selected?.Devices != null)
            {
                foreach (var dev in _vm.Selected.Devices)
                {
                    data.Devices.Add(MapDeviceToDto(dev));
                }
            }

            // 3) modularer Upload
            var overlayByteSize = OverlayDataModule.CalculateUncompressedSize(data);
            var uploaded = await OverlayDataModule.UploadOverlayAsync(GetServerKey(), _mySteamId, data);
            if (uploaded)
                MarkOverlayCloudSynced(overlayByteSize);
        }
        catch (Exception ex)
        {
            AppendLog("[overlay] Upload Error: " + ex.Message);
        }
    }



    private string? PromptText(string message, string title = "Enter Text", string defaultValue = "")
    {
        // kleines Dialogfenster on-the-fly bauen
        var win = new Window
        {
            Title = title,
            Width = 320,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(24, 26, 30)),
            Foreground = Brushes.White,
            ShowInTaskbar = false
        };

        var root = new Grid
        {
            Margin = new Thickness(12)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // label
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // textbox
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // buttons

        var lbl = new TextBlock
        {
            Text = message,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = Brushes.White
        };
        Grid.SetRow(lbl, 0);
        root.Children.Add(lbl);

        var tb = new TextBox
        {
            Text = defaultValue,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(tb, 1);
        root.Children.Add(tb);

        var panelButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var btnOk = new Button
        {
            Content = "OK",
            Width = 60,
            Margin = new Thickness(0, 0, 6, 0),
            IsDefault = true
        };
        var btnCancel = new Button
        {
            Content = "Cancel",
            Width = 80,
            IsCancel = true
        };

        panelButtons.Children.Add(btnOk);
        panelButtons.Children.Add(btnCancel);
        Grid.SetRow(panelButtons, 2);
        root.Children.Add(panelButtons);

        string? result = null;

        btnOk.Click += (_, __) =>
        {
            result = tb.Text;
            win.DialogResult = true;
            win.Close();
        };
        btnCancel.Click += (_, __) =>
        {
            result = null;
            win.DialogResult = false;
            win.Close();
        };

        win.Content = root;

        // modal anzeigen
        win.ShowDialog();

        return result;
    }

    // euklidische Distanz Punkt -> Streckenabschnitt (A,B)
    private static double DistancePointToSegmentSquared(Point p, Point a, Point b)
    {
        // Vektor AB
        double vx = b.X - a.X;
        double vy = b.Y - a.Y;

        // Vektor AP
        double wx = p.X - a.X;
        double wy = p.Y - a.Y;

        // Projektion t = (AP*AB)/|AB|^2 clamped [0..1]
        double denom = (vx * vx + vy * vy);
        double t = denom <= 0.000001 ? 0.0 : ((wx * vx + wy * vy) / denom);
        if (t < 0.0) t = 0.0;
        else if (t > 1.0) t = 1.0;

        // Naechster Punkt auf AB
        double cx = a.X + t * vx;
        double cy = a.Y + t * vy;

        // Distanz^2 zwischen P und C
        double dx = p.X - cx;
        double dy = p.Y - cy;
        return dx * dx + dy * dy;
    }

    private static double DistancePointToPolyline(Point p, Polyline line)
    {
        var pts = line.Points;
        if (pts.Count == 1)
        {
            // einzelne Punkt-Linie -> Distanz zu diesem Punkt
            double dx = p.X - pts[0].X;
            double dy = p.Y - pts[0].Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        double bestSq = double.MaxValue;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            double d2 = DistancePointToSegmentSquared(p, pts[i], pts[i + 1]);
            if (d2 < bestSq) bestSq = d2;
        }
        return Math.Sqrt(bestSq);
    }

    private void SetCurrentTool(OverlayToolMode modeFromButton)
    {
        if (_currentTool == modeFromButton)
        {
            // toggle off -> zurueck in Pan/Zoom Modus
            _currentTool = OverlayToolMode.None;
        }
        else
        {
            _currentTool = modeFromButton;
        }

        // Wenn wir ein Tool aktivieren, abbrechen von evtl. Drag-State
        if (_currentTool != OverlayToolMode.None)
        {
            _draggingElement = null;
        }

        UpdateToolButtonHighlights();
        UpdateOptionsPanelVisibility();
    }

    private void UpdateOptionsPanelVisibility()
    {
        if (OverlayToolOptionsPanel == null) return;

        DrawOptionsPanel.Visibility = Visibility.Collapsed;
        TextOptionsPanel.Visibility = Visibility.Collapsed;
        IconOptionsPanel.Visibility = Visibility.Collapsed;
        EraserOptionsPanel.Visibility = Visibility.Collapsed;

        if (_currentTool == OverlayToolMode.None)
        {
            OverlayToolOptionsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        OverlayToolOptionsPanel.Visibility = Visibility.Visible;

        switch (_currentTool)
        {
            case OverlayToolMode.Draw:
                DrawOptionsPanel.Visibility = Visibility.Visible;
                if (SliderDrawThickness != null) SliderDrawThickness.Value = _drawThickness;
                HighlightActiveColor(DrawOptionsPanel, _drawColor);
                break;
            case OverlayToolMode.Text:
                TextOptionsPanel.Visibility = Visibility.Visible;
                if (SliderTextSize != null) SliderTextSize.Value = _textSize;
                HighlightActiveColor(TextOptionsPanel, _textColor);
                break;
            case OverlayToolMode.Icon:
                IconOptionsPanel.Visibility = Visibility.Visible;
                if (SliderIconSize != null) SliderIconSize.Value = _activeIconSize;
                HighlightActiveIcon(_currentIconPath);
                break;
            case OverlayToolMode.Erase:
                EraserOptionsPanel.Visibility = Visibility.Visible;
                if (SliderEraserSize != null) SliderEraserSize.Value = _eraserSize;
                break;
        }
    }

    private void HighlightActiveColor(StackPanel panel, Color activeColor)
    {
        foreach (var child in panel.Children)
        {
            if (child is Button btn && btn.Tag is string hex)
            {
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(hex);
                    if (c == activeColor)
                    {
                        btn.BorderBrush = Brushes.White;
                        btn.BorderThickness = new Thickness(2);
                    }
                    else
                    {
                        btn.BorderBrush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));
                        btn.BorderThickness = new Thickness(1);
                    }
                }
                catch {}
            }
        }
    }

    private void HighlightActiveIcon(string activePath)
    {
        var btns = new[] { BtnIconBase1, BtnIconBase2, BtnIconSam, BtnIconTurret };
        foreach (var btn in btns)
        {
            if (btn == null) continue;
            string tag = btn.Tag as string ?? "";
            if (tag == activePath)
            {
                btn.Background = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255));
                btn.BorderBrush = Brushes.DodgerBlue;
                btn.BorderThickness = new Thickness(2);
            }
            else
            {
                btn.Background = Brushes.Transparent;
                btn.BorderBrush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));
                btn.BorderThickness = new Thickness(1);
            }
        }

        // Custom Rust Marker selectors
        bool isCustom = activePath.StartsWith("rust-marker://");
        string activeShape = "pin";
        string activeColor = "blue";
        if (isCustom)
        {
            try
            {
                var uri = new Uri(activePath);
                activeShape = uri.Host;
                activeColor = uri.AbsolutePath.Trim('/');
            }
            catch {}
        }

        if (RustMarkerSelectorPanel != null)
        {
            foreach (var child in RustMarkerSelectorPanel.Children)
            {
                if (child is Button btn && btn.Tag is string shape)
                {
                    if (isCustom && shape.Equals(activeShape, StringComparison.OrdinalIgnoreCase))
                    {
                        btn.Background = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255));
                        btn.BorderBrush = Brushes.DodgerBlue;
                        btn.BorderThickness = new Thickness(2);
                    }
                    else
                    {
                        btn.Background = Brushes.Transparent;
                        btn.BorderBrush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));
                        btn.BorderThickness = new Thickness(1);
                    }
                }
            }
        }

        if (RustColorSelectorPanel != null)
        {
            foreach (var child in RustColorSelectorPanel.Children)
            {
                if (child is Button btn && btn.Tag is string colorName)
                {
                    if (isCustom && colorName.Equals(activeColor, StringComparison.OrdinalIgnoreCase))
                    {
                        btn.BorderBrush = Brushes.White;
                        btn.BorderThickness = new Thickness(2);
                    }
                    else
                    {
                        btn.BorderBrush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));
                        btn.BorderThickness = new Thickness(1);
                    }
                }
            }
        }

        UpdateMarkerSelectorPreviews();
    }

    private void UpdateMarkerSelectorPreviews()
    {
        if (RustMarkerSelectorPanel == null) return;

        foreach (var child in RustMarkerSelectorPanel.Children)
        {
            if (child is Button btn && btn.Tag is string shape)
            {
                var preview = CreateRustMarkerElement($"rust-marker://{shape}/{_selectedMarkerColor}", 22, 22, false, 0, null, null);
                btn.Content = preview;
            }
        }
    }

    private void RustMarkerSelection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string shape)
        {
            _selectedMarkerShape = shape;
            _currentIconPath = $"rust-marker://{_selectedMarkerShape}/{_selectedMarkerColor}";
            HighlightActiveIcon(_currentIconPath);
        }
    }

    private void RustColorSelection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string colorName)
        {
            _selectedMarkerColor = colorName;
            _currentIconPath = $"rust-marker://{_selectedMarkerShape}/{_selectedMarkerColor}";
            HighlightActiveIcon(_currentIconPath);
        }
    }

    private static class RustMarkerColors
    {
        public static readonly Dictionary<string, (string Bright, string Dark)> Colors = new(StringComparer.OrdinalIgnoreCase)
        {
            { "yellow", ("#CDD054", "#454519") },
            { "blue",   ("#2F71C4", "#11243F") },
            { "green",  ("#76A739", "#243410") },
            { "red",    ("#BD3838", "#3B120F") },
            { "purple", ("#B65CC4", "#371B3B") },
            { "pink",   ("#e25cba", "#1e0618") }, // Legacy support
            { "cyan",   ("#06EDC2", "#094B3B") }
        };
    }

    private BitmapImage LoadBitmapResource(string packUri)
    {
        var uri = new Uri(packUri, UriKind.RelativeOrAbsolute);
        var sri = Application.GetResourceStream(uri);
        if (sri == null)
        {
            throw new System.IO.IOException($"Cannot locate resource: {packUri}");
        }
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = sri.Stream;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void UpdateNoteVisibilityAndText(Grid container, string? note)
    {
        bool isBase = container.Tag is OverlayTag tag &&
                      tag.CustomIconPath != null &&
                      (tag.CustomIconPath.Contains("base1.png") || tag.CustomIconPath.Contains("base2.png"));
        int maxLen = isBase ? 100 : 10;

        if (note != null && note.Length > maxLen)
        {
            note = note.Substring(0, maxLen);
        }

        var noteBorder = container.Children.OfType<Border>().FirstOrDefault(b => (b.Tag as string) == "NoteBorder");
        if (noteBorder != null)
        {
            noteBorder.VerticalAlignment = VerticalAlignment.Bottom;
            noteBorder.Margin = new Thickness(-100, 0, -100, -18);
            var textBlock = noteBorder.Child as TextBlock;
            if (textBlock != null)
            {
                textBlock.TextWrapping = TextWrapping.NoWrap;
                textBlock.TextTrimming = TextTrimming.None;
                if (string.IsNullOrWhiteSpace(note))
                {
                    noteBorder.Visibility = Visibility.Collapsed;
                }
                else
                {
                    textBlock.Text = note;
                    noteBorder.Visibility = Visibility.Visible;
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(note))
        {
            var textBlock = new TextBlock
            {
                Text = note,
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None
            };
            var border = new Border
            {
                Tag = "NoteBorder",
                Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(-100, 0, -100, -18),
                Child = textBlock,
                IsHitTestVisible = false
            };
            container.Children.Add(border);
        }
    }

    private void UpdateScreenshotIndicator(Grid container, List<string>? screenshots)
    {
        bool hasScreenshots = screenshots != null && screenshots.Count > 0;

        var existingBadge = container.Children.OfType<Border>().FirstOrDefault(b => (b.Tag as string) == "SSBadge");
        if (!hasScreenshots)
        {
            if (existingBadge != null)
                container.Children.Remove(existingBadge);
            return;
        }

        string countText = screenshots!.Count > 99 ? "99+" : screenshots.Count.ToString();

        if (existingBadge != null)
        {
            var existingTb = existingBadge.Child as TextBlock;
            if (existingTb != null)
                existingTb.Text = countText;
        }
        else
        {
            var textBlock = new TextBlock
            {
                Text = countText,
                Foreground = Brushes.White,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false
            };
            var badge = new Border
            {
                Tag = "SSBadge",
                Background = new SolidColorBrush(Color.FromArgb(200, 180, 40, 40)),
                CornerRadius = new CornerRadius(9),
                Width = 18,
                Height = 18,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -6, -6, 0),
                Child = textBlock,
                IsHitTestVisible = false
            };
            container.Children.Add(badge);
        }
    }

    private FrameworkElement CreateRustMarkerElement(string iconPath, double width, double height, bool isHitTestVisible, ulong ownerSteamId, string? note, List<string>? screenshots)
    {
        try
        {
            string shape = "pin";
            string colorName = "blue";

            try
            {
                var uri = new Uri(iconPath);
                if (uri.Scheme == "rust-marker")
                {
                    shape = uri.Host;
                    colorName = uri.AbsolutePath.Trim('/');
                }
            }
            catch {}

            if (!RustMarkerColors.Colors.TryGetValue(colorName, out var colorPair))
            {
                colorPair = RustMarkerColors.Colors["blue"];
            }

            var brightBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorPair.Bright));
            var darkBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorPair.Dark));

            var grid = new Grid
            {
                Width = width,
                Height = height,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                RenderTransformOrigin = new Point(0.5, 0.5),
                IsHitTestVisible = isHitTestVisible,
                Tag = new OverlayTag
                {
                    OwnerSteamId = ownerSteamId,
                    IsUserEditable = isHitTestVisible,
                    Note = note,
                    Screenshots = screenshots ?? new List<string>(),
                    CustomIconPath = iconPath
                }
            };

            RenderOptions.SetBitmapScalingMode(grid, BitmapScalingMode.HighQuality);

            if (shape.Equals("pin", StringComparison.OrdinalIgnoreCase))
            {
                // 1. Background layer: Filled with the bright color, masked by the pin background PNG.
                var bgRect = new Rectangle
                {
                    Width = width,
                    Height = height,
                    Fill = brightBrush,
                    OpacityMask = new ImageBrush(LoadBitmapResource("pack://application:,,,/Assets/icons/map-markers/assets_markers_iconmappinbg.png"))
                };
                grid.Children.Add(bgRect);

                // 2. Foreground layer: Rendered as-is (original PNG colors) to draw the black outlines and shadows natively.
                var fgRect = new Rectangle
                {
                    Width = width,
                    Height = height,
                    Fill = new ImageBrush(LoadBitmapResource("pack://application:,,,/Assets/icons/map-markers/assets_markers_iconmappinfg.png"))
                };
                grid.Children.Add(fgRect);
            }
            else
            {
                // 1. Background layer (dark color, masked by circular background mask)
                var bgRect = new Rectangle
                {
                    Width = width,
                    Height = height,
                    Fill = darkBrush,
                    OpacityMask = new ImageBrush(LoadBitmapResource("pack://application:,,,/Assets/icons/map-markers/assets_markers_iconmapbackground.png"))
                };
                grid.Children.Add(bgRect);

                // 2. Foreground layer (bright color, masked by circular foreground mask/ring)
                var fgRect = new Rectangle
                {
                    Width = width,
                    Height = height,
                    Fill = brightBrush,
                    OpacityMask = new ImageBrush(LoadBitmapResource("pack://application:,,,/Assets/icons/map-markers/assets_markers_iconmapforeground.png"))
                };
                grid.Children.Add(fgRect);

                // 3. Icon layer (White color, masked by icon mask)
                string iconResPath = $"pack://application:,,,/Assets/icons/map-markers/assets_markers_iconmap_{shape.ToLowerInvariant()}.png";
                var iconRect = new Rectangle
                {
                    Width = width,
                    Height = height,
                    Fill = Brushes.White,
                    OpacityMask = new ImageBrush(LoadBitmapResource(iconResPath))
                };
                grid.Children.Add(iconRect);
            }

            if (!string.IsNullOrWhiteSpace(note))
            {
                UpdateNoteVisibilityAndText(grid, note);
            }

            return grid;
        }
        catch
        {
            // Fail-safe fallback: Red ellipse to prevent crash
            var fallback = new Ellipse
            {
                Width = width,
                Height = height,
                Fill = Brushes.Red,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                Tag = new OverlayTag
                {
                    OwnerSteamId = ownerSteamId,
                    IsUserEditable = isHitTestVisible,
                    Note = note,
                    Screenshots = screenshots ?? new List<string>(),
                    CustomIconPath = iconPath
                }
            };
            return fallback;
        }
    }

    private void SliderDrawThickness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _drawThickness = e.NewValue;
    }

    private void SliderTextSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _textSize = e.NewValue;
    }

    private void SliderIconSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _activeIconSize = e.NewValue;
    }

    private void SliderEraserSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _eraserSize = e.NewValue;
    }

    private void DrawColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex)
        {
            try
            {
                _drawColor = (Color)ColorConverter.ConvertFromString(hex);
                HighlightActiveColor(DrawOptionsPanel, _drawColor);
            }
            catch {}
        }
    }

    private void TextColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex)
        {
            try
            {
                _textColor = (Color)ColorConverter.ConvertFromString(hex);
                HighlightActiveColor(TextOptionsPanel, _textColor);
            }
            catch {}
        }
    }

    private void IconSelection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path)
        {
            _currentIconPath = path;
            HighlightActiveIcon(_currentIconPath);
        }
    }
    private void UpdateToolButtonHighlights()
    {
        // Erstmal alle zuruecksetzen
        foreach (var kv in _toolButtons)
        {
            var btn = kv.Value;
            btn.Background = Brushes.Transparent;
            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
            btn.BorderThickness = new Thickness(1);
        }

        // Falls gar kein Tool aktiv ist (None) -> fertig
        if (_currentTool == OverlayToolMode.None)
            return;

        // Aktiven Button highlighten
        if (_toolButtons.TryGetValue(_currentTool, out var activeBtn))
        {
            activeBtn.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x4C, 0x8D, 0xFF)); // halbtransparentes Blau
            activeBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF));
            activeBtn.BorderThickness = new Thickness(2);
        }
    }

    private void SaveOwnOverlayToJson()
    {
        try
        {
            // 1) aktuelles Overlay aus dem Canvas bauen
            var data = BuildCurrentOverlaySaveDataForMe();

            // 2) Devices aus der Authoritative List (Profile) uebernehmen
            data.Devices.Clear();
            if (_vm.Selected?.Devices != null)
            {
                foreach (var dev in _vm.Selected.Devices)
                {
                    data.Devices.Add(MapDeviceToDto(dev));
                }
            }

            // 3) modularer Save
            OverlayDataModule.SaveLocalOverlay(GetServerKey(), _mySteamId, data);
            UpdateCloudSyncUI();
            var overlayByteSize = OverlayDataModule.CalculateUncompressedSize(data);

            // 4) Debounced Cloud upload if enabled (anon key works, no Discord needed)
            if (TrackingService.CloudSyncEnabled && RustPlusDesk.Services.Auth.SupabaseAuthManager.Client != null)
            {
                if (IsOverlaySyncLimitExceeded(overlayByteSize))
                    return;

                _overlaySyncCts?.Cancel();
                _overlaySyncCts?.Dispose();
                _overlaySyncCts = new CancellationTokenSource();
                var token = _overlaySyncCts.Token;
                var sk = GetServerKey();
                var sid = _mySteamId;
                var capturedData = data;
                var capturedSize = overlayByteSize;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(OverlaySyncDebounceMs, token).ConfigureAwait(false);
                        if (token.IsCancellationRequested) return;
                        var uploaded = await OverlayDataModule.UploadOverlayAsync(sk, sid, capturedData);
                        if (uploaded)
                            Dispatcher.Invoke(() => MarkOverlayCloudSynced(capturedSize));
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => AppendLog("[overlay/cloud] Sync failed: " + ex.Message));
                    }
                });
            }

        }
        catch (Exception ex)
        {
            AppendLog("[overlay] SaveOwnOverlayToJson error: " + ex.Message);
        }
    }

    private string GetServerKey()
    {
        // simplest first pass: nimm Host-Port vom aktuell ausgewaehlten Server
        var prof = _vm?.Selected;
        if (prof == null) return "unknown-server";

        // du hast im Connect-Code `_vm.Selected.Host` und `_vm.Selected.Port`
        return $"{prof.Host}-{prof.Port}";
    }

    private string GetOverlayJsonPathForPlayerServer(ulong steamId)
    {
        return DataManager.GetOverlayJsonPath(GetServerKey(), steamId);
    }


    private void ClearUserOverlayElements()
    {
        // 1) Sammeln, nicht waehrend foreach loeschen
        var toRemove = new List<UIElement>();

        foreach (var child in Overlay.Children)
        {
            if (child is FrameworkElement fe && fe.Tag is OverlayTag)
            {
                toRemove.Add(fe);
            }
        }

        // 2) Rauswerfen
        foreach (var el in toRemove)
            Overlay.Children.Remove(el);

        // 3) Auch unsere Index-Maps resetten
        _playerOverlayElements.Clear();
        _visibleOverlayOwners.Clear();
    }

    private class OverlayTag
    {
        public ulong OwnerSteamId;
        public bool IsUserEditable;
        public double? BaseSize;   // nur fuer Icons, wird NICHT gespeichert
        public string? Note;
        public List<string> Screenshots = new();
        public string? CustomIconPath;
    }

    // Liest lokales Overlay (mich) als OverlaySaveData
    private OverlaySaveData BuildCurrentOverlaySaveDataForMe()
    {
        var data = new OverlaySaveData();
        data.LastUpdatedUnix = DataManager.UnixNow(); // NEU: stamp jetzt

        foreach (var child in Overlay.Children)
        {
            if (child is not FrameworkElement fe) continue;
            if (fe.Tag is not OverlayTag meta) continue;
            if (meta.OwnerSteamId != _mySteamId) continue;

            switch (child)
            {
                case Polyline pl:
                    {
                        var stroke = new SavedStroke
                        {
                            Thickness = pl.StrokeThickness,
                            Color = (pl.Stroke as SolidColorBrush)?.Color.ToString() ?? "#FFFFFFFF"
                        };

                        foreach (var p in pl.Points)
                            stroke.Points.Add(p);

                        data.Strokes.Add(stroke);
                        break;
                    }

                case Image img:
                    {
                        var x = Canvas.GetLeft(img);
                        var y = Canvas.GetTop(img);

                        var bi = img.Source as BitmapImage;

                        var si = new SavedIcon
                        {
                            IconPath = meta.CustomIconPath ?? bi?.UriSource?.ToString() ?? _currentIconPath,
                            X = x,
                            Y = y,
                            Width = img.Width,
                            Height = img.Height,
                            Note = meta?.Note,
                            Screenshots = meta?.Screenshots
                        };
                        data.Icons.Add(si);
                        break;
                    }

                case Grid grid:
                    {
                        var x = Canvas.GetLeft(grid);
                        var y = Canvas.GetTop(grid);

                        var si = new SavedIcon
                        {
                            IconPath = meta.CustomIconPath ?? _currentIconPath,
                            X = x,
                            Y = y,
                            Width = grid.Width,
                            Height = grid.Height,
                            Note = meta?.Note,
                            Screenshots = meta?.Screenshots
                        };
                        data.Icons.Add(si);
                        break;
                    }

                case TextBlock tb:
                    {
                        var x = Canvas.GetLeft(tb);
                        var y = Canvas.GetTop(tb);

                        var st = new SavedText
                        {
                            Content = tb.Text,
                            X = x,
                            Y = y,
                            FontSize = tb.FontSize,
                            Bold = (tb.FontWeight == FontWeights.Bold),
                            Color = (tb.Foreground as SolidColorBrush)?.Color.ToString() ?? "#FFFFFFFF"
                        };
                        data.Texts.Add(st);
                        break;
                    }
            }
        }

        return data;
    }

    // speichert OverlaySaveData von steamId (z.B. teammate) lokal in %APPDATA%\RustPlusDesk\Overlays\<serverKey>\<steamId>.json
    private void WriteOverlaySaveDataLocal(ulong steamId, OverlaySaveData data)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            data,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        var path = GetOverlayJsonPathForPlayerServer(steamId);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    // baut aus einem OverlaySaveData echte UI-Elemente auf der Canvas fuer einen Spieler
    // und cached sie in _playerOverlayElements[steamId]
    private void MaterializeOverlayForPlayer(ulong steamId, OverlaySaveData data, bool editableIfMine)
    {
        // falls schon Elemente fuer den Spieler existieren -> erstmal killen
        if (_playerOverlayElements.TryGetValue(steamId, out var existing))
        {
            foreach (var el in existing)
                Overlay.Children.Remove(el);
        }

        var list = new List<FrameworkElement>();
        _playerOverlayElements[steamId] = list;

        foreach (var stroke in data.Strokes)
        {
            var pl = new Polyline
            {
                StrokeThickness = stroke.Thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeStartLineCap = PenLineCap.Round,
                IsHitTestVisible = editableIfMine ? false : false, // Linien eh nicht direkt draggen
                Opacity = editableIfMine ? 1.0 : 0.8,
                Tag = new OverlayTag
                {
                    OwnerSteamId = steamId,
                    IsUserEditable = editableIfMine
                },
                Visibility = _visibleOverlayOwners.Contains(steamId)
                             ? Visibility.Visible
                             : Visibility.Collapsed
            };

            if (ColorConverter.ConvertFromString(stroke.Color) is Color c)
                pl.Stroke = new SolidColorBrush(c);

            foreach (var p in stroke.Points)
                pl.Points.Add(p);

            Overlay.Children.Add(pl);
            list.Add(pl);
        }

        foreach (var icon in data.Icons)
        {
            FrameworkElement elementToPlace;
            bool isCustom = icon.IconPath.StartsWith("rust-marker://");

            if (isCustom)
            {
                elementToPlace = CreateRustMarkerElement(icon.IconPath, icon.Width, icon.Height, editableIfMine, steamId, icon.Note, icon.Screenshots);
            }
            else
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(icon.IconPath, UriKind.RelativeOrAbsolute);
                bi.EndInit();
                bi.Freeze();

                bool isBase = icon.IconPath.Contains("base1.png") || icon.IconPath.Contains("base2.png");
                var img = new Image
                {
                    Source = bi,
                    Width = icon.Width,
                    Height = icon.Height,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    IsHitTestVisible = false
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

                var grid = new Grid
                {
                    Width = icon.Width,
                    Height = icon.Height,
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    IsHitTestVisible = editableIfMine || isBase,
                    Opacity = editableIfMine ? 1.0 : 0.8,
                    Tag = new OverlayTag
                    {
                        OwnerSteamId = steamId,
                        IsUserEditable = editableIfMine,
                        Note = icon.Note,
                        Screenshots = icon.Screenshots ?? new List<string>(),
                        CustomIconPath = icon.IconPath
                    },
                    Visibility = _visibleOverlayOwners.Contains(steamId)
                                 ? Visibility.Visible
                                 : Visibility.Collapsed
                };
                grid.Children.Add(img);

                if (isBase)
                {
                    grid.MouseEnter += BaseIcon_MouseEnter;
                    grid.MouseLeave += BaseIcon_MouseLeave;
                    grid.MouseLeftButtonUp += BaseIcon_MouseLeftButtonUp;
                }

                if (!string.IsNullOrWhiteSpace(icon.Note))
                {
                    UpdateNoteVisibilityAndText(grid, icon.Note);
                }

                if (isBase)
                {
                    UpdateScreenshotIndicator(grid, icon.Screenshots);
                }

                elementToPlace = grid;
            }

            Canvas.SetLeft(elementToPlace, icon.X);
            Canvas.SetTop(elementToPlace, icon.Y);

            Overlay.Children.Add(elementToPlace);
            list.Add(elementToPlace);
        }

        foreach (var txt in data.Texts)
        {
            var tb = new TextBlock
            {
                Text = txt.Content,
                FontSize = txt.FontSize,
                FontWeight = txt.Bold ? FontWeights.Bold : FontWeights.Normal,
                IsHitTestVisible = editableIfMine,
                Opacity = editableIfMine ? 1.0 : 0.8,
                Tag = new OverlayTag
                {
                    OwnerSteamId = steamId,
                    IsUserEditable = editableIfMine
                },
                Visibility = _visibleOverlayOwners.Contains(steamId)
                             ? Visibility.Visible
                             : Visibility.Collapsed
            };

            if (ColorConverter.ConvertFromString(txt.Color) is Color tc)
                tb.Foreground = new SolidColorBrush(tc);

            Canvas.SetLeft(tb, txt.X);
            Canvas.SetTop(tb, txt.Y);

            Overlay.Children.Add(tb);
            list.Add(tb);
        }
    }

    private async Task<bool> TryFetchOverlayForPlayerFromServerAsync(ulong steamId, bool silent = false)
    {
        try
        {
            var data = await OverlayDataModule.FetchOverlayFromServerAsync(GetServerKey(), steamId);
            if (data == null) return false;

            // Canvas-Objekte bauen / ersetzen
            bool editable = (steamId == _mySteamId);
            MaterializeOverlayForPlayer(steamId, data, editable);

            if (!silent)
                AppendLog("[overlay] Overlay loaded from " + steamId + ".");
            return true;
        }
        catch (Exception ex)
        {
            if (!silent)
                AppendLog("[overlay] Fetch Error: " + ex.Message);
            return false;
        }
    }

    private void LoadOverlayFromDiskForPlayer(ulong steamId)
    {
        try
        {
            var data = OverlayDataModule.LoadLocalOverlay(GetServerKey(), steamId);
            if (data == null)
            {
                // registriere leere Liste (damit wir nicht endlos neu versuchen)
                if (!_playerOverlayElements.ContainsKey(steamId))
                    _playerOverlayElements[steamId] = new List<FrameworkElement>();
                return;
            }

            bool editable = (steamId == _mySteamId);
            MaterializeOverlayForPlayer(steamId, data, editable);
        }
        catch
        {
            if (!_playerOverlayElements.ContainsKey(steamId))
                _playerOverlayElements[steamId] = new List<FrameworkElement>();
        }
    }

    /// <summary>
    /// Smart init for own overlay on server connect:
    /// 1) Load local JSON
    /// 2) Fetch from Cloud (anon key works, no Discord needed)
    /// 3) Cloud newer → use Cloud; Local newer → upload Local to Cloud; Both empty → nothing
    /// </summary>
    private async Task InitOwnOverlayAsync()
    {
        try
        {
            var serverKey = GetServerKey();
            var localData = OverlayDataModule.LoadLocalOverlay(serverKey, _mySteamId);

            bool localHasContent = localData != null
                && ((localData.Strokes?.Count ?? 0) > 0
                 || (localData.Icons?.Count   ?? 0) > 0
                 || (localData.Texts?.Count   ?? 0) > 0
                 || (localData.Devices?.Count ?? 0) > 0);

            OverlaySaveData? cloudData = null;
            if (TrackingService.CloudSyncEnabled && Services.Auth.SupabaseAuthManager.Client != null)
            {
                try { cloudData = await OverlayDataModule.FetchOverlayFromServerAsync(serverKey, _mySteamId); }
                catch { /* offline or error – ignore */ }
            }

            if (cloudData?.Devices?.Count > 0 && localData != null)
            {
                var merged = MergeMissingCloudDevicesInto(localData, cloudData);
                if (merged > 0)
                {
                    AppendLog($"[dev/init] Merged {merged} missing cloud devices into local cache before sync decisions.");
                    OverlayDataModule.SaveLocalOverlay(serverKey, _mySteamId, localData);
                    localHasContent = true;
                }
            }

            if (cloudData == null
                && OverlayDataModule.LastFetchHadError
                && TrackingService.CloudSyncEnabled
                && Services.Auth.SupabaseAuthManager.Client != null)
            {
                AppendLog("[overlay/init] Cloud fetch failed; device autosync stays paused to avoid overwriting cloud data.");
                return;
            }

            bool cloudHasContent = cloudData != null
                && ((cloudData.Strokes?.Count ?? 0) > 0
                 || (cloudData.Icons?.Count   ?? 0) > 0
                 || (cloudData.Texts?.Count   ?? 0) > 0
                 || (cloudData.Devices?.Count ?? 0) > 0);

            OverlaySaveData? toUse;

            if (!localHasContent && cloudHasContent)
            {
                // Fresh install or cleared local → restore from Cloud
                AppendLog("[overlay/init] Local empty, Cloud has content → restoring from Cloud.");
                toUse = cloudData!;
                OverlayDataModule.SaveLocalOverlay(serverKey, _mySteamId, toUse);
            }
            else if (localHasContent && cloudHasContent)
            {
                long localTs = localData!.LastUpdatedUnix;
                long cloudTs = cloudData!.LastUpdatedUnix;

                if (cloudTs > localTs)
                {
                    AppendLog($"[overlay/init] Cloud newer ({cloudTs} > {localTs}) → pulling Cloud.");
                    toUse = cloudData;
                    OverlayDataModule.SaveLocalOverlay(serverKey, _mySteamId, toUse);
                }
                else
                {
                    AppendLog($"[overlay/init] Local newer or same ({localTs} >= {cloudTs}) → pushing Local to Cloud.");
                    toUse = localData!;
                    // Push local to cloud (fire and forget, respects limits)
                    _ = Task.Run(() => OverlayDataModule.UploadOverlayAsync(serverKey, _mySteamId, toUse));
                }
            }
            else if (localHasContent)
            {
                // Local has content, cloud empty → push local up
                AppendLog("[overlay/init] Local has content, Cloud empty → pushing to Cloud.");
                toUse = localData!;
                _ = Task.Run(() => OverlayDataModule.UploadOverlayAsync(serverKey, _mySteamId, toUse));
            }
            else
            {
                // Both empty → nothing to do
                AppendLog("[overlay/init] Both local and cloud are empty. Starting fresh.");
                _playerOverlayElements[_mySteamId] = new List<FrameworkElement>();
                _ownCloudRestoreReady = true;
                return;
            }

            Dispatcher.Invoke(() =>
            {
                RestoreOwnDevicesFromCloudIfMissing(toUse);
                bool editable = true;
                MaterializeOverlayForPlayer(_mySteamId, toUse, editable);
            });
            _ownCloudRestoreReady = true;
        }
        catch (Exception ex)
        {
            AppendLog("[overlay/init] Error: " + ex.Message);
            if (!_playerOverlayElements.ContainsKey(_mySteamId))
                _playerOverlayElements[_mySteamId] = new List<FrameworkElement>();
        }
        finally
        {
            if (!TrackingService.CloudSyncEnabled || Services.Auth.SupabaseAuthManager.Client == null)
                _ownCloudRestoreReady = true;
        }
    }

    private void RestoreOwnDevicesFromCloudIfMissing(OverlaySaveData? data)
    {
        if (data?.Devices == null || data.Devices.Count == 0 || _vm.Selected?.Devices == null)
            return;

        int imported = 0;
        foreach (var dto in data.Devices)
        {
            if (FindDeviceById(_vm.Selected.Devices, dto.EntityId) != null)
                continue;

            _vm.Selected.Devices.Add(MapDtoToDeviceFiltered(dto));
            imported++;
        }

        if (imported <= 0) return;

        _vm.NotifyDevicesChanged();
        _vm.Save();
        AppendLog($"[dev/init] Restored {imported} own devices from cloud.");
    }

    private int MergeMissingCloudDevicesInto(OverlaySaveData localData, OverlaySaveData cloudData)
    {
        if (cloudData.Devices == null || cloudData.Devices.Count == 0)
            return 0;

        localData.Devices ??= new List<ExportedDeviceDto>();
        int added = 0;
        foreach (var cloudDevice in cloudData.Devices)
        {
            if (ContainsExportedDevice(localData.Devices, cloudDevice))
                continue;

            localData.Devices.Add(cloudDevice);
            added++;
        }

        if (added > 0 && cloudData.LastUpdatedUnix > localData.LastUpdatedUnix)
            localData.LastUpdatedUnix = cloudData.LastUpdatedUnix;

        return added;
    }

    private static bool ContainsExportedDevice(IEnumerable<ExportedDeviceDto> existing, ExportedDeviceDto candidate)
    {
        foreach (var item in existing)
        {
            if (!candidate.IsGroup && !item.IsGroup && item.EntityId == candidate.EntityId)
                return true;

            if (candidate.IsGroup && item.IsGroup &&
                string.Equals(item.Name, candidate.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Alias, candidate.Alias, StringComparison.OrdinalIgnoreCase))
                return true;

            if (item.Children != null && ContainsExportedDevice(item.Children, candidate))
                return true;
        }

        return false;
    }

    /// <summary>Starts the 1-second live-polling timer for visible teammate overlays.</summary>
    private void StartOverlayPollTimer()
    {
        if (_overlayPollTimer != null) return; // already running

        _overlayPollTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _overlayPollTimer.Tick += async (_, __) =>
        {
            // Pulse animation logic in timer tick
            _pulseStep = (_pulseStep + 1) % 2;
            double targetBlur = _pulseStep == 0 ? 6 : 14;
            if (SubscriptionStack != null)
            {
                foreach (FrameworkElement child in SubscriptionStack.Children)
                {
                    if (child is Grid g && g.Tag is System.Windows.Media.Effects.DropShadowEffect glow)
                    {
                        glow.BlurRadius = targetBlur;
                    }
                }
            }

            if (!TrackingService.CloudSyncEnabled) return;

            // Skip HTTP database fetches if WebSocket is active and connected
            if (TeamSyncWebSocketService.IsActive) return;

            var teammates = _visibleOverlayOwners.Where(id => id != _mySteamId).ToList();
            bool isMePremium = SupabaseAuthManager.IsPremium;
            bool anyStateUpdated = false;

            foreach (var sid in teammates)
            {
                if (!_teammatePollStates.TryGetValue(sid, out var state))
                {
                    state = new TeammatePollState { SteamId = sid, SecondsUntilNextPoll = 0 };
                    _teammatePollStates[sid] = state;
                }

                var tm = TeamMembers.FirstOrDefault(t => t.SteamId == sid);
                bool isOnline = tm?.IsOnline ?? false;

                if (!isOnline)
                {
                    if (!state.IsPaused)
                    {
                        state.IsPaused = true;
                        anyStateUpdated = true;
                    }
                    continue;
                }

                if (state.IsPaused)
                {
                    continue;
                }

                state.SecondsUntilNextPoll--;
                if (state.SecondsUntilNextPoll <= 0)
                {
                    state.SecondsUntilNextPoll = 999; // prevent double triggers

                    try
                    {
                        var data = await OverlayDataModule.FetchOverlayFromServerAsync(GetServerKey(), sid);
                        if (data != null)
                        {
                            bool isChanged = data.LastUpdatedUnix != state.LastUpdatedUnix;
                            state.LastUpdatedUnix = data.LastUpdatedUnix;

                            if (isChanged)
                            {
                                state.ConsecutiveUnchangedCount = 0;
                                state.LastChangeTime = DateTime.UtcNow;
                                state.IsPaused = false;

                                MaterializeOverlayForPlayer(sid, data, false);
                                anyStateUpdated = true;
                            }
                            else
                            {
                                state.ConsecutiveUnchangedCount++;
                            }

                            int nextIntervalSeconds = 10;
                            var elapsedSinceChange = DateTime.UtcNow - state.LastChangeTime;

                            if (isMePremium)
                            {
                                if (state.ConsecutiveUnchangedCount < 3)
                                    nextIntervalSeconds = 3;
                                else if (state.ConsecutiveUnchangedCount == 3)
                                    nextIntervalSeconds = 10;
                                else
                                    nextIntervalSeconds = 30;

                                if (elapsedSinceChange.TotalHours >= 2.0)
                                {
                                    nextIntervalSeconds = 60;
                                }
                            }
                            else
                            {
                                if (state.ConsecutiveUnchangedCount < 3)
                                    nextIntervalSeconds = 10;
                                else if (state.ConsecutiveUnchangedCount == 3)
                                    nextIntervalSeconds = 30;
                                else
                                    nextIntervalSeconds = 60;

                                if (elapsedSinceChange.TotalHours >= 1.0)
                                {
                                    state.IsPaused = true;
                                    anyStateUpdated = true;
                                    AppendLog($"[overlay/poll] Auto-paused polling for {tm?.Name ?? sid.ToString()} (no changes for 1 hour).");
                                }
                            }

                            state.SecondsUntilNextPoll = nextIntervalSeconds;
                        }
                        else
                        {
                            // Unsubscribe if overlay was deleted / empty
                            _visibleOverlayOwners.Remove(sid);
                            _teammatePollStates.Remove(sid);
                            anyStateUpdated = true;
                            if (_playerOverlayElements.TryGetValue(sid, out var listToHide))
                            {
                                foreach (var fe in listToHide)
                                    fe.Visibility = Visibility.Collapsed;
                            }
                            AppendLog($"[overlay/poll] Unsubscribed from {tm?.Name ?? sid.ToString()}: No overlay found on server.");
                        }
                    }
                    catch
                    {
                        state.SecondsUntilNextPoll = 10;
                    }
                }
            }

            if (anyStateUpdated)
            {
                Dispatcher.Invoke(() =>
                {
                    RebuildOverlayTeamBar();
                    UpdateSubscriptionDock();
                    UpdateSavedSubscriptionsInProfile();
                });
            }
        };

        _overlayPollTimer.Start();
        AppendLog("[overlay/poll] Teammate overlay live-polling started (1s interval).");
    }

    /// <summary>Stops the teammate overlay poll timer (call on disconnect or all teammates hidden).</summary>
    public void StopOverlayPollTimer()
    {
        if (_overlayPollTimer == null) return;
        _overlayPollTimer.Stop();
        _overlayPollTimer = null;
        AppendLog("[overlay/poll] Teammate overlay live-polling stopped.");
    }

    private async Task FetchSteamIdsWithOverlaysAsync()
    {
        if (SupabaseAuthManager.Client == null) return;
        try
        {
            var serverKey = GetServerKey();
            var ids = TeamMembers.Select(tm => tm.SteamId.ToString()).ToList();
            if (ids.Count == 0) return;

            var response = await SupabaseAuthManager.Client
                .From<MapOverlayModel>()
                .Filter("server_key", Postgrest.Constants.Operator.Equals, serverKey)
                .Filter("steam_id", Postgrest.Constants.Operator.In, ids)
                .Get();

            _steamIdsWithOverlays.Clear();
            if (response.Models != null)
            {
                foreach (var m in response.Models)
                {
                    if (ulong.TryParse(m.SteamId, out ulong sid))
                    {
                        _steamIdsWithOverlays.Add(sid);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog("[overlay/db] Error checking who has overlays: " + ex.Message);
        }
    }

    private void UpdateSavedSubscriptionsInProfile()
    {
        if (_vm?.Selected == null) return;

        var currentSubs = _visibleOverlayOwners.Where(id => id != _mySteamId).ToList();
        _vm.Selected.SubscribedTeammateSteamIds = currentSubs;
        _vm.Save();
    }

    private void UpdateSubscriptionDock()
    {
        if (SubscriptionDock == null || SubscriptionStack == null) return;

        SubscriptionStack.Children.Clear();

        var activeSubs = _visibleOverlayOwners.Where(id => id != _mySteamId).ToList();

        if (activeSubs.Count == 0)
        {
            SubscriptionDock.Visibility = Visibility.Collapsed;
            return;
        }

        SubscriptionDock.Visibility = Visibility.Visible;

        foreach (var steamId in activeSubs)
        {
            var tm = TeamMembers.FirstOrDefault(t => t.SteamId == steamId);
            if (tm == null) continue;

            // Create a Container Grid for the avatar and its glow
            var containerGrid = new Grid
            {
                Margin = new Thickness(0, 4, 0, 4),
                Width = 36,
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = Brushes.Transparent // ensures hit testing works on the whole area
            };

            // Green pulsing/glow border or effect
            var border = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.LimeGreen,
                Background = Brushes.Transparent
            };

            // Check if the user is paused or offline
            bool isPaused = false;
            if (_teammatePollStates.TryGetValue(steamId, out var state))
            {
                isPaused = state.IsPaused;
            }

            bool isOnline = tm.IsOnline;

            // If offline or paused, change border color and opacity
            if (!isOnline)
            {
                border.BorderBrush = Brushes.Gray;
                containerGrid.Opacity = 0.5;
            }
            else if (isPaused)
            {
                border.BorderBrush = Brushes.Orange;
                containerGrid.Opacity = 0.7;
            }

            // Add a DropShadowEffect for the pulse/glow
            var glow = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = !isOnline ? Colors.Gray : (isPaused ? Colors.Orange : Colors.LimeGreen),
                BlurRadius = 10,
                ShadowDepth = 0,
                Opacity = 0.8
            };
            border.Effect = glow;
            containerGrid.Tag = glow; // store for pulsing

            // Avatar Image
            var img = new Image
            {
                Width = 30,
                Height = 30,
                Stretch = Stretch.UniformToFill,
                Source = tm.Avatar ?? GetPlaceholderAvatar(),
                Clip = new EllipseGeometry(new Point(15, 15), 15, 15)
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            // Delete/Trash button overlay
            var trashBtn = new Button
            {
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Color.FromArgb(180, 200, 30, 30)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Cursor = Cursors.Hand
            };
            var trashImg = new Image
            {
                Width = 14,
                Height = 14,
                Stretch = Stretch.UniformToFill,
                Source = new BitmapImage(new Uri("pack://application:,,,/Assets/icons/trash.png")),
                SnapsToDevicePixels = true
            };
            RenderOptions.SetBitmapScalingMode(trashImg, BitmapScalingMode.HighQuality);
            trashBtn.Content = trashImg;

            containerGrid.Children.Add(border);
            containerGrid.Children.Add(img);
            containerGrid.Children.Add(trashBtn);

            // Hover events to show/hide the trash button
            containerGrid.MouseEnter += (s, e) =>
            {
                trashBtn.Visibility = Visibility.Visible;
            };
            containerGrid.MouseLeave += (s, e) =>
            {
                trashBtn.Visibility = Visibility.Collapsed;
            };

            // Left click on avatar triggers refresh if online/paused
            containerGrid.MouseLeftButtonDown += async (s, e) =>
            {
                if (trashBtn.IsMouseOver) return;

                e.Handled = true;

                AppendLog($"[overlay/poll] Quick-refresh triggered for {tm.Name}.");
                if (_teammatePollStates.TryGetValue(steamId, out var st))
                {
                    st.SecondsUntilNextPoll = 0;
                    st.IsPaused = false;
                    st.ConsecutiveUnchangedCount = 0;
                    st.LastChangeTime = DateTime.UtcNow;
                }
                await TryFetchOverlayForPlayerFromServerAsync(steamId);
                UpdateSubscriptionDock();
            };

            // Click on trash button unsubscribes
            trashBtn.Click += (s, e) =>
            {
                e.Handled = true;
                AppendLog($"[overlay/subscription] Unsubscribed from {tm.Name}.");
                _visibleOverlayOwners.Remove(steamId);
                _teammatePollStates.Remove(steamId);
                if (_playerOverlayElements.TryGetValue(steamId, out var listToHide))
                {
                    foreach (var fe in listToHide)
                        fe.Visibility = Visibility.Collapsed;
                }
                RebuildOverlayTeamBar();
                UpdateSubscriptionDock();
                UpdateSavedSubscriptionsInProfile();
            };

            // Tooltip
            string statusText = tm.Name;
            if (!isOnline) statusText += " (Offline - Paused)";
            else if (isPaused) statusText += " (Subscription Paused - No changes for 1 hour. Click avatar to resume.)";
            else statusText += " (Active - Click avatar to refresh)";
            containerGrid.ToolTip = statusText;

            SubscriptionStack.Children.Add(containerGrid);
        }
    }

    private void DockEditSubscriptionsButton_Click(object sender, RoutedEventArgs e)
    {
        BtnToggleOverlayTools_Click(BtnToggleOverlayTools, new RoutedEventArgs());
    }

    private async void ToolRefreshOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        AppendLog("[overlay/ui] Manually refreshing all visible overlays...");

        // 1. Fetch who has overlays in database to update greying out
        await FetchSteamIdsWithOverlaysAsync();
        RebuildOverlayTeamBar();

        // 2. Refresh each subscribed teammate immediately
        var teammates = _visibleOverlayOwners.Where(id => id != _mySteamId).ToList();
        foreach (var sid in teammates)
        {
            if (_teammatePollStates.TryGetValue(sid, out var st))
            {
                st.SecondsUntilNextPoll = 0;
                st.IsPaused = false;
                st.ConsecutiveUnchangedCount = 0;
                st.LastChangeTime = DateTime.UtcNow;
            }
            await TryFetchOverlayForPlayerFromServerAsync(sid);
        }

        UpdateSubscriptionDock();
    }

    public async Task RefreshTeammateOverlayAsync(ulong steamId)
    {
        if (!TrackingService.CloudSyncEnabled) return;

        try
        {
            await FetchSteamIdsWithOverlaysAsync();
            
            if (_teammatePollStates.TryGetValue(steamId, out var state))
            {
                state.SecondsUntilNextPoll = 999;
                state.IsPaused = false;
                state.ConsecutiveUnchangedCount = 0;
                state.LastChangeTime = DateTime.UtcNow;
            }
            
            await TryFetchOverlayForPlayerFromServerAsync(steamId);

            Dispatcher.Invoke(() =>
            {
                RebuildOverlayTeamBar();
                UpdateSubscriptionDock();
            });
        }
        catch (Exception ex)
        {
            AppendLog($"[overlay/websocket/error] Failed to refresh overlay for {steamId}: {ex.Message}");
        }
    }

    public void ApplyInlineOverlayData(ulong steamId, string serverKey, string overlayDataJson, string? markerDataJson, string? deviceDataJson, long updatedAt)
    {
        try
        {
            if (serverKey != GetServerKey())
            {
                AppendLog($"[overlay/broadcast] Skipping inline overlay for {steamId}: server key mismatch ({serverKey} vs {GetServerKey()})");
                return;
            }

            var data = new OverlaySaveData { LastUpdatedUnix = updatedAt };

            if (!string.IsNullOrEmpty(overlayDataJson))
            {
                var mapData = System.Text.Json.JsonSerializer.Deserialize<OverlaySaveData>(overlayDataJson);
                if (mapData != null)
                {
                    data.Strokes = mapData.Strokes ?? new();
                    data.Icons = mapData.Icons ?? new();
                    data.Texts = mapData.Texts ?? new();
                    data.Devices = mapData.Devices ?? new();
                }
            }

            if (!string.IsNullOrEmpty(markerDataJson))
            {
                var baseIcons = System.Text.Json.JsonSerializer.Deserialize<List<SavedIcon>>(markerDataJson);
                if (baseIcons?.Count > 0)
                    data.Icons.AddRange(baseIcons);
            }

            if (!string.IsNullOrEmpty(deviceDataJson))
            {
                var devs = System.Text.Json.JsonSerializer.Deserialize<List<ExportedDeviceDto>>(deviceDataJson);
                if (devs != null)
                    data.Devices = devs;
            }

            bool editable = (steamId == _mySteamId);
            MaterializeOverlayForPlayer(steamId, data, editable);

            OverlayDataModule.SaveLocalOverlay(serverKey, steamId, data);

            if (_teammatePollStates.TryGetValue(steamId, out var state))
            {
                state.SecondsUntilNextPoll = 999;
                state.IsPaused = false;
                state.ConsecutiveUnchangedCount = 0;
                state.LastChangeTime = DateTime.UtcNow;
            }

            Dispatcher.Invoke(() =>
            {
                RebuildOverlayTeamBar();
                UpdateSubscriptionDock();
            });

            AppendLog($"[overlay/broadcast] Inline overlay applied for {steamId}");
        }
        catch (Exception ex)
        {
            AppendLog($"[overlay/broadcast/error] Failed to apply inline overlay for {steamId}: {ex.Message}");
        }
    }


    // --- BASE HOVER, GALLERY, LOUPE, & CONTEXT MENU LOGIC ---

    private DispatcherTimer? _baseDetailHideTimer;
    private DispatcherTimer? _baseDetailShowTimer;
    private FrameworkElement? _activeBaseHoverAnchor;
    private OverlayTag? _activeBaseHoverMeta;
    private FrameworkElement? _activeGalleryAnchor;
    private OverlayTag? _activeGalleryMeta;

    private void BaseIcon_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is OverlayTag meta)
        {
            _baseDetailHideTimer?.Stop();
            _baseDetailShowTimer?.Stop();

            _activeBaseHoverAnchor = fe;
            _activeBaseHoverMeta = meta;

            _baseDetailShowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _baseDetailShowTimer.Tick += (s, ev) =>
            {
                _baseDetailShowTimer.Stop();
                ShowBaseHoverDetails(_activeBaseHoverAnchor, _activeBaseHoverMeta);
            };
            _baseDetailShowTimer.Start();
        }
    }

    private void BaseIcon_MouseLeave(object sender, MouseEventArgs e)
    {
        _baseDetailShowTimer?.Stop();
        StartBaseDetailHideTimer();
    }

    private void BaseHoverPopup_MouseEnter(object sender, MouseEventArgs e)
    {
        _baseDetailHideTimer?.Stop();
    }

    private void BaseHoverPopup_MouseLeave(object sender, MouseEventArgs e)
    {
        StartBaseDetailHideTimer();
    }

    private void StartBaseDetailHideTimer()
    {
        _baseDetailHideTimer?.Stop();
        _baseDetailHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _baseDetailHideTimer.Tick += (s, e) =>
        {
            if (BaseHoverPopup != null && !BaseHoverPopup.IsMouseOver)
            {
                BaseHoverPopup.Visibility = Visibility.Collapsed;
            }
            _baseDetailHideTimer.Stop();
        };
        _baseDetailHideTimer.Start();
    }

    private void BtnCloseBaseHover_Click(object sender, RoutedEventArgs e)
    {
        if (BaseHoverPopup != null) BaseHoverPopup.Visibility = Visibility.Collapsed;
    }

    private void ShowBaseHoverDetails(FrameworkElement anchor, OverlayTag meta)
    {
        if (BaseHoverPopup == null || TxtBaseHoverNote == null || ImgBaseHoverScreenshot == null || BorderBaseHoverImage == null) return;

        string noteText = meta.Note ?? "";
        if (noteText.Length > 50)
        {
            noteText = noteText.Substring(0, 50) + " [...]";
        }

        TxtBaseHoverNote.Text = noteText;
        TxtBaseHoverNote.Visibility = string.IsNullOrWhiteSpace(noteText) ? Visibility.Collapsed : Visibility.Visible;

        if (meta.Screenshots != null && meta.Screenshots.Count > 0)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(meta.Screenshots[0]);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.StreamSource = new MemoryStream(bytes);
                bi.EndInit();
                bi.Freeze();

                ImgBaseHoverScreenshot.Source = bi;
                BorderBaseHoverImage.Visibility = Visibility.Visible;
            }
            catch
            {
                BorderBaseHoverImage.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            BorderBaseHoverImage.Visibility = Visibility.Collapsed;
        }

        if (string.IsNullOrWhiteSpace(noteText) && (meta.Screenshots == null || meta.Screenshots.Count == 0))
        {
            BaseHoverPopup.Visibility = Visibility.Collapsed;
            return;
        }

        BaseHoverPopup.Visibility = Visibility.Visible;
        BaseHoverPopup.UpdateLayout();

        var pos = anchor.TranslatePoint(new Point(30, -20), WebViewHost);
        double left = Math.Min(pos.X, WebViewHost.ActualWidth - BaseHoverPopup.ActualWidth - 20);
        double top = Math.Min(pos.Y, WebViewHost.ActualHeight - BaseHoverPopup.ActualHeight - 20);
        BaseHoverPopup.Margin = new Thickness(Math.Max(10, left), Math.Max(10, top), 0, 0);
    }

    private void BaseIcon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is OverlayTag meta)
        {
            if (BaseHoverPopup != null) BaseHoverPopup.Visibility = Visibility.Collapsed;
            ShowBaseGallery(fe, meta);
            e.Handled = true;
        }
    }

    private void ShowBaseGallery(FrameworkElement anchor, OverlayTag meta)
    {
        if (BaseGalleryPopup == null || TxtBaseGalleryNote == null || ImgBaseGalleryMain == null || BaseGalleryThumbsPanel == null || BaseGalleryNoImagePlaceholder == null) return;

        _activeGalleryAnchor = anchor;
        _activeGalleryMeta = meta;

        TxtBaseGalleryNote.Text = string.IsNullOrWhiteSpace(meta.Note)
            ? CloudText("BaseNoNotesYet", "No notes added yet.")
            : meta.Note;
        BaseGalleryThumbsPanel.Children.Clear();

        if (meta.Screenshots != null && meta.Screenshots.Count > 0)
        {
            BaseGalleryNoImagePlaceholder.Visibility = Visibility.Collapsed;
            ImgBaseGalleryMain.Visibility = Visibility.Visible;

            for (int i = 0; i < meta.Screenshots.Count; i++)
            {
                int index = i;
                try
                {
                    byte[] bytes = Convert.FromBase64String(meta.Screenshots[i]);
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.StreamSource = new MemoryStream(bytes);
                    bi.EndInit();
                    bi.Freeze();

                    var border = new Border
                    {
                        Width = 56,
                        Height = 44,
                        Margin = new Thickness(0, 0, 0, 8),
                        BorderThickness = new Thickness(1),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)),
                        CornerRadius = new CornerRadius(4),
                        ClipToBounds = true,
                        Background = Brushes.Black,
                        Cursor = Cursors.Hand
                    };

                    var imgThumb = new Image { Source = bi, Stretch = Stretch.Uniform };
                    border.Child = imgThumb;

                    border.MouseLeftButtonDown += (s, ev) =>
                    {
                        SetActiveGalleryImage(index, meta);
                    };

                    BaseGalleryThumbsPanel.Children.Add(border);
                }
                catch {}
            }

            SetActiveGalleryImage(0, meta);
        }
        else
        {
            ImgBaseGalleryMain.Source = null;
            ImgBaseGalleryMain.Visibility = Visibility.Collapsed;
            BaseGalleryNoImagePlaceholder.Visibility = Visibility.Visible;
        }

        BaseGalleryPopup.Visibility = Visibility.Visible;
    }

    private void SetActiveGalleryImage(int index, OverlayTag meta)
    {
        if (meta.Screenshots == null || index < 0 || index >= meta.Screenshots.Count) return;

        try
        {
            byte[] bytes = Convert.FromBase64String(meta.Screenshots[index]);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.StreamSource = new MemoryStream(bytes);
            bi.EndInit();
            bi.Freeze();

            ImgBaseGalleryMain.Source = bi;

            for (int i = 0; i < BaseGalleryThumbsPanel.Children.Count; i++)
            {
                if (BaseGalleryThumbsPanel.Children[i] is Border b)
                {
                    b.BorderBrush = (i == index) ? Brushes.DodgerBlue : new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));
                    b.BorderThickness = (i == index) ? new Thickness(2) : new Thickness(1);
                }
            }
        }
        catch {}
    }

    private void BtnCloseBaseGallery_Click(object sender, RoutedEventArgs e)
    {
        if (BaseGalleryPopup != null) BaseGalleryPopup.Visibility = Visibility.Collapsed;
    }

    private void MainImage_MouseEnter(object sender, MouseEventArgs e)
    {
        if (ImgBaseGalleryMain == null || ImgBaseGalleryMain.Source == null || BaseGalleryMagnifierLens == null) return;
        BaseGalleryMagnifierLens.Visibility = Visibility.Visible;
    }

    private void MainImage_MouseLeave(object sender, MouseEventArgs e)
    {
        if (BaseGalleryMagnifierLens != null)
            BaseGalleryMagnifierLens.Visibility = Visibility.Collapsed;
    }

    private void MainImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (ImgBaseGalleryMain == null || ImgBaseGalleryMain.Source == null || BaseGalleryMagnifierLens == null || BaseGalleryMagnifierBrush == null) return;

        var container = BaseGalleryMainImageArea;
        var mousePos = e.GetPosition(container);

        Canvas.SetLeft(BaseGalleryMagnifierLens, mousePos.X - 75);
        Canvas.SetTop(BaseGalleryMagnifierLens, mousePos.Y - 75);

        var imgMousePos = e.GetPosition(ImgBaseGalleryMain);

        double viewWidth = 75;
        double viewHeight = 75;
        double viewX = imgMousePos.X - viewWidth / 2;
        double viewY = imgMousePos.Y - viewHeight / 2;

        BaseGalleryMagnifierBrush.Viewbox = new Rect(viewX, viewY, viewWidth, viewHeight);
    }

    public bool TryHandleOverlayRightClick(Point mapPos)
    {
        if (Overlay == null) return false;

        for (int i = Overlay.Children.Count - 1; i >= 0; i--)
        {
            if (Overlay.Children[i] is FrameworkElement fe && fe.Tag is OverlayTag meta)
            {
                if (meta.OwnerSteamId != _mySteamId || !meta.IsUserEditable) continue;
                if (fe is Polyline) continue;

                double x = Canvas.GetLeft(fe);
                double y = Canvas.GetTop(fe);
                double w = fe.Width > 0 ? fe.Width : 32;
                double h = fe.Height > 0 ? fe.Height : 32;

                if (mapPos.X >= x && mapPos.X <= x + w &&
                    mapPos.Y >= y && mapPos.Y <= y + h)
                {
                    string path = meta.CustomIconPath ?? "";
                    bool isBase = path.Contains("base1.png") || path.Contains("base2.png");

                    if (isBase)
                    {
                        ShowBaseContextMenu(fe, meta);
                    }
                    else
                    {
                        ShowGenericIconContextMenu(fe, meta);
                    }
                    return true;
                }
            }
        }
        return false;
    }

    private void ReplaceOverlayElement(FrameworkElement oldEl, FrameworkElement newEl)
    {
        try
        {
            // Copy Canvas position
            double left = Canvas.GetLeft(oldEl);
            double top = Canvas.GetTop(oldEl);
            Canvas.SetLeft(newEl, left);
            Canvas.SetTop(newEl, top);

            // Copy RenderTransform
            newEl.RenderTransform = oldEl.RenderTransform;

            // Copy BaseSize if present
            if (oldEl.Tag is OverlayTag oldMeta && newEl.Tag is OverlayTag newMeta)
            {
                newMeta.BaseSize = oldMeta.BaseSize;
            }

            // Replace in Overlay Canvas children
            int index = Overlay.Children.IndexOf(oldEl);
            if (index != -1)
            {
                Overlay.Children.RemoveAt(index);
                Overlay.Children.Insert(index, newEl);
            }
            else
            {
                Overlay.Children.Add(newEl);
            }

            // Replace in _playerOverlayElements list
            if (_playerOverlayElements.TryGetValue(_mySteamId, out var list))
            {
                int listIdx = list.IndexOf(oldEl);
                if (listIdx != -1)
                {
                    list[listIdx] = newEl;
                }
                else
                {
                    list.Add(newEl);
                }
            }

            SaveOwnOverlayToJson();
            UploadOwnOverlayToTeam();

            // Reapply the zoom-based scale so the new element looks correct at the current map zoom
            RefreshUserOverlayIcons();
        }
        catch (Exception ex)
        {
            AppendLog("[ReplaceOverlayElement] error: " + ex.Message);
        }
    }

    private void ShowGenericIconContextMenu(FrameworkElement iconEl, OverlayTag meta)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("DarkContextMenu") };

        // --- Add / Edit Note (inline on map) ---
        var noteHeader = string.IsNullOrEmpty(meta.Note)
            ? CloudText("BaseAddNote", "Add Note")
            : CloudText("BaseEditNote", "Edit Note");
        var miNote = new MenuItem { Header = noteHeader };
        miNote.Click += (s, e) =>
        {
            menu.IsOpen = false;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                ShowInlineNotePicker(iconEl, meta)));
        };
        menu.Items.Add(miNote);

        if (!string.IsNullOrEmpty(meta.Note))
        {
            var miDelNote = new MenuItem { Header = CloudText("BaseDeleteNote", "Delete Note") };
            miDelNote.Click += (s, e) =>
            {
                meta.Note = null;
                if (iconEl is Grid g)
                    UpdateNoteVisibilityAndText(g, null);
                SaveOwnOverlayToJson();
                UploadOwnOverlayToTeam();
            };
            menu.Items.Add(miDelNote);
        }

        bool isCustomMarker = meta.CustomIconPath != null && meta.CustomIconPath.StartsWith("rust-marker://");
        if (isCustomMarker)
        {
            menu.Items.Add(new Separator());

            string shape = "pin";
            string color = "blue";
            try
            {
                if (string.IsNullOrEmpty(meta.CustomIconPath))
                    throw new InvalidOperationException("Custom marker path is empty.");
                var uri = new Uri(meta.CustomIconPath);
                if (uri.Scheme == "rust-marker")
                {
                    shape = uri.Host;
                    color = uri.AbsolutePath.Trim('/');
                }
            }
            catch { }

            // Change Icon – opens inline picker
            var miChangeIcon = new MenuItem { Header = "Change Icon" };
            miChangeIcon.Click += (s, e) =>
            {
                menu.IsOpen = false;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                    ShowInlineIconPicker(iconEl, meta, shape, color)));
            };
            menu.Items.Add(miChangeIcon);

            // Change Color – opens inline picker
            var miChangeColor = new MenuItem { Header = "Change Color" };
            miChangeColor.Click += (s, e) =>
            {
                menu.IsOpen = false;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                    ShowInlineColorPicker(iconEl, meta, shape, color)));
            };
            menu.Items.Add(miChangeColor);
        }

        menu.Items.Add(new Separator());

        var miDelete = new MenuItem { Header = CloudText("Delete", "Delete") };
        miDelete.Click += (s, e) =>
        {
            Overlay.Children.Remove(iconEl);
            if (_playerOverlayElements.TryGetValue(_mySteamId, out var mine))
                mine.Remove(iconEl);
            SaveOwnOverlayToJson();
            UploadOwnOverlayToTeam();
        };
        menu.Items.Add(miDelete);

        iconEl.ContextMenu = menu;
        menu.IsOpen = true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Inline overlay pickers (no modal dialogs / broken sub-menus)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Shared helper: anchors an inline panel on the Overlay canvas near a marker and
    /// adds a transparent full-canvas dismiss layer behind it so a click elsewhere removes it.</summary>
    private (Border panel, Border dismissLayer) CreateInlinePanel(FrameworkElement anchorEl, UIElement content)
    {
        // Dismiss backdrop – NOT tagged as OverlayTag so RefreshUserOverlayIcons skips it
        var dismissLayer = new Border
        {
            Background = Brushes.Transparent,
            Width  = Overlay.ActualWidth  > 0 ? Overlay.ActualWidth  : 2000,
            Height = Overlay.ActualHeight > 0 ? Overlay.ActualHeight : 2000,
            Tag    = "InlinePickerPanel",
        };
        Canvas.SetLeft(dismissLayer, 0);
        Canvas.SetTop(dismissLayer,  0);
        Panel.SetZIndex(dismissLayer, 9990);

        // Panel – also NOT tagged as OverlayTag
        var panel = new Border
        {
            Background   = new SolidColorBrush(Color.FromArgb(230, 22, 26, 32)),
            BorderBrush  = new SolidColorBrush(Color.FromArgb(90,  255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding      = new Thickness(10),
            Tag          = "InlinePickerPanel",
            Effect       = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color     = Colors.Black,
                Opacity   = 0.7,
                BlurRadius = 18,
                ShadowDepth = 3
            },
            Child = content
        };
        Panel.SetZIndex(panel, 9999);

        // Position: just below & to the right of the anchor
        double anchorLeft = Canvas.GetLeft(anchorEl);
        double anchorTop  = Canvas.GetTop(anchorEl);
        double anchorH    = !double.IsNaN(anchorEl.Height) ? anchorEl.Height
                            : (anchorEl.ActualHeight > 0 ? anchorEl.ActualHeight : 32);
        Canvas.SetLeft(panel, anchorLeft + 4);
        Canvas.SetTop(panel,  anchorTop  + anchorH + 4);

        return (panel, dismissLayer);
    }

    private void RemoveInlinePanel(Border panel, Border dismissLayer)
    {
        Overlay.Children.Remove(panel);
        Overlay.Children.Remove(dismissLayer);
    }

    private void ShowInlineIconPicker(FrameworkElement iconEl, OverlayTag meta, string currentShape, string currentColor)
    {
        string[] shapes = { "pin", "dollar", "gun", "home", "loot", "parachute", "rock", "scope", "shield", "skull", "sleep", "zzz" };

        // Section label
        var header = new Wpf.Ui.Controls.TextBlock
        {
            Text        = "Change Icon",
            FontSize    = 10,
            FontWeight  = FontWeights.SemiBold,
            Foreground  = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            Margin      = new Thickness(0, 0, 0, 6),
        };

        // 4-column WrapPanel of rendered marker thumbnails
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Width = 196 };

        Border? panel = null;
        Border? dismissLayer = null;

        foreach (var sh in shapes)
        {
            string captured = sh;
            bool isCurrent = sh == currentShape;

            // Build a real 36×36 marker element (not hit-testable) as thumbnail
            var thumb = CreateRustMarkerElement($"rust-marker://{sh}/{currentColor}", 36, 36, false, 0, null, null);
            thumb.IsHitTestVisible = false;
            thumb.RenderTransform = Transform.Identity; // no zoom scaling on thumbnail

            var btn = new Border
            {
                Width           = 46,
                Height          = 46,
                Margin          = new Thickness(2),
                CornerRadius    = new CornerRadius(6),
                Background      = isCurrent
                    ? new SolidColorBrush(Color.FromArgb(100, 96, 205, 255))
                    : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderBrush     = isCurrent ? Brushes.White : new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(isCurrent ? 2 : 1),
                Cursor          = Cursors.Hand,
                ToolTip         = sh[0].ToString().ToUpper() + sh.Substring(1),
                Child           = new Viewbox { Child = thumb, Margin = new Thickness(4) },
            };
            btn.MouseLeftButtonDown += (_, __) =>
            {
                try
                {
                    double w = !double.IsNaN(iconEl.Width)  ? iconEl.Width  : (_activeIconSize > 0 ? _activeIconSize : 32);
                    double h = !double.IsNaN(iconEl.Height) ? iconEl.Height : (_activeIconSize > 0 ? _activeIconSize : 32);
                    string newPath = $"rust-marker://{captured}/{currentColor}";
                    var newEl = CreateRustMarkerElement(newPath, w, h, true, _mySteamId, meta.Note, meta.Screenshots);
                    ReplaceOverlayElement(iconEl, newEl);
                }
                catch (Exception ex) { AppendLog("[IconPicker] " + ex.Message); }
                finally { RemoveInlinePanel(panel!, dismissLayer!); }
            };
            wrap.Children.Add(btn);
        }

        var container = new StackPanel();
        container.Children.Add(header);
        container.Children.Add(wrap);

        (panel, dismissLayer) = CreateInlinePanel(iconEl, container);
        dismissLayer.MouseLeftButtonDown += (_, __) => RemoveInlinePanel(panel, dismissLayer);

        Overlay.Children.Add(dismissLayer);
        Overlay.Children.Add(panel);
    }

    private void ShowInlineColorPicker(FrameworkElement iconEl, OverlayTag meta, string currentShape, string currentColor)
    {
        var colorMap = new (string name, Color swatch)[] {
            ("yellow",  Color.FromRgb(0xCD, 0xD0, 0x54)),
            ("blue",    Color.FromRgb(0x2F, 0x71, 0xC4)),
            ("green",   Color.FromRgb(0x76, 0xA7, 0x39)),
            ("red",     Color.FromRgb(0xBD, 0x38, 0x38)),
            ("purple",  Color.FromRgb(0xB6, 0x5C, 0xC4)),
            ("cyan",    Color.FromRgb(0x06, 0xED, 0xC2)),
        };

        var header = new Wpf.Ui.Controls.TextBlock
        {
            Text       = "Change Color",
            FontSize   = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            Margin     = new Thickness(0, 0, 0, 6),
        };

        var swatchRow = new StackPanel { Orientation = Orientation.Horizontal };

        Border? panel = null;
        Border? dismissLayer = null;

        foreach (var (name, swatch) in colorMap)
        {
            string capturedName = name;
            bool isCurrent = name == currentColor;
            var pill = new Border
            {
                Width           = 34,
                Height          = 34,
                CornerRadius    = new CornerRadius(17),
                Background      = new SolidColorBrush(swatch),
                BorderBrush     = isCurrent ? Brushes.White : new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(isCurrent ? 3 : 1),
                Margin          = new Thickness(3),
                Cursor          = Cursors.Hand,
                ToolTip         = name[0].ToString().ToUpper() + name.Substring(1),
            };
            // Show a checkmark on the selected swatch
            if (isCurrent)
            {
                pill.Child = new Wpf.Ui.Controls.SymbolIcon
                {
                    Symbol             = Wpf.Ui.Controls.SymbolRegular.Checkmark12,
                    Foreground         = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                    FontSize           = 14,
                };
            }
            pill.MouseLeftButtonDown += (_, __) =>
            {
                try
                {
                    double w = !double.IsNaN(iconEl.Width)  ? iconEl.Width  : (_activeIconSize > 0 ? _activeIconSize : 32);
                    double h = !double.IsNaN(iconEl.Height) ? iconEl.Height : (_activeIconSize > 0 ? _activeIconSize : 32);
                    string newPath = $"rust-marker://{currentShape}/{capturedName}";
                    var newEl = CreateRustMarkerElement(newPath, w, h, true, _mySteamId, meta.Note, meta.Screenshots);
                    ReplaceOverlayElement(iconEl, newEl);
                }
                catch (Exception ex) { AppendLog("[ColorPicker] " + ex.Message); }
                finally { RemoveInlinePanel(panel!, dismissLayer!); }
            };
            swatchRow.Children.Add(pill);
        }

        var container = new StackPanel();
        container.Children.Add(header);
        container.Children.Add(swatchRow);

        (panel, dismissLayer) = CreateInlinePanel(iconEl, container);
        dismissLayer.MouseLeftButtonDown += (_, __) => RemoveInlinePanel(panel, dismissLayer);

        Overlay.Children.Add(dismissLayer);
        Overlay.Children.Add(panel);
    }

    private void ShowInlineNotePicker(FrameworkElement iconEl, OverlayTag meta)
    {
        // Colorable rust-marker icons always-show their note, so keep it short (max 10)
        // Old base/pin icons use a dialog and have a longer limit
        bool isColorableMarker = meta.CustomIconPath?.StartsWith("rust-marker://") == true;
        int maxLen = isColorableMarker ? 10 : 100;

        var header = new Wpf.Ui.Controls.TextBlock
        {
            Text       = isColorableMarker ? $"Note (max {maxLen} chars)" : "Note",
            FontSize   = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            Margin     = new Thickness(0, 0, 0, 4),
        };

        var tb = new TextBox
        {
            Text            = meta.Note ?? "",
            Width           = 200,
            MinHeight       = 50,
            MaxHeight       = 100,
            MaxLength       = maxLen,
            AcceptsReturn   = !isColorableMarker, // single-line for short notes
            TextWrapping    = isColorableMarker ? TextWrapping.NoWrap : TextWrapping.Wrap,
            VerticalScrollBarVisibility = isColorableMarker ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
            Foreground      = Brushes.White,
            Background      = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(6, 4, 6, 4),
            FontSize        = 12,
            CaretBrush      = Brushes.White,
            SelectionBrush  = new SolidColorBrush(Color.FromArgb(120, 96, 205, 255)),
        };

        // Character counter for colorable markers
        var charCounter = new Wpf.Ui.Controls.TextBlock
        {
            Text       = $"{tb.Text.Length}/{maxLen}",
            FontSize   = 9,
            Foreground = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin     = new Thickness(0, 2, 0, 0),
            Visibility = isColorableMarker ? Visibility.Visible : Visibility.Collapsed,
        };
        tb.TextChanged += (_, __) =>
        {
            charCounter.Text = $"{tb.Text.Length}/{maxLen}";
            charCounter.Foreground = tb.Text.Length >= maxLen
                ? new SolidColorBrush(Color.FromRgb(220, 80, 80))
                : new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
        };

        Border? panel = null;
        Border? dismissLayer = null;

        // Save button with WPF UI icon
        var saveBtnContent = new StackPanel { Orientation = Orientation.Horizontal };
        saveBtnContent.Children.Add(new Wpf.Ui.Controls.SymbolIcon
        {
            Symbol   = Wpf.Ui.Controls.SymbolRegular.Save16,
            FontSize = 13,
            Margin   = new Thickness(0, 0, 5, 0),
        });
        saveBtnContent.Children.Add(new System.Windows.Controls.TextBlock { Text = "Save", VerticalAlignment = VerticalAlignment.Center });

        var saveBtn = new Button
        {
            Content      = saveBtnContent,
            Margin       = new Thickness(0, 8, 0, 0),
            Height       = 30,
            Foreground   = Brushes.White,
            Background   = new SolidColorBrush(Color.FromRgb(37, 99, 185)),
            BorderBrush  = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Cursor       = Cursors.Hand,
        };

        Action doSave = () =>
        {
            meta.Note = string.IsNullOrWhiteSpace(tb.Text) ? null : tb.Text.Trim();
            if (iconEl is Grid g)
                UpdateNoteVisibilityAndText(g, meta.Note);
            SaveOwnOverlayToJson();
            UploadOwnOverlayToTeam();
            RemoveInlinePanel(panel!, dismissLayer!);
        };

        saveBtn.Click += (_, __) => doSave();

        // Ctrl+Enter also saves
        tb.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Return && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                doSave();
                ke.Handled = true;
            }
        };

        var container = new StackPanel();
        container.Children.Add(header);
        container.Children.Add(tb);
        container.Children.Add(charCounter);
        container.Children.Add(saveBtn);

        (panel, dismissLayer) = CreateInlinePanel(iconEl, container);
        // Clicking the dismiss layer saves & closes
        dismissLayer.MouseLeftButtonDown += (_, __) => doSave();

        Overlay.Children.Add(dismissLayer);
        Overlay.Children.Add(panel);

        // Focus and select-all after layout
        panel.Loaded += (_, __) => { tb.Focus(); tb.SelectAll(); };
    }

    private void ShowBaseContextMenu(FrameworkElement baseImg, OverlayTag meta)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("DarkContextMenu") };

        var miNote = new MenuItem { Header = string.IsNullOrEmpty(meta.Note) ? CloudText("BaseAddNote", "Add Note") : CloudText("BaseEditNote", "Edit Note") };
        miNote.Click += (s, e) =>
        {
            var dlg = new Views.Windows.BaseNoteWindow(meta.Note) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                meta.Note = dlg.NoteResult;
                if (baseImg is Grid g)
                    UpdateNoteVisibilityAndText(g, meta.Note);
                SaveOwnOverlayToJson();
                UploadOwnOverlayToTeam();
            }
        };
        menu.Items.Add(miNote);

        if (!string.IsNullOrEmpty(meta.Note))
        {
            var miDelNote = new MenuItem { Header = CloudText("BaseDeleteNote", "Delete Note") };
            miDelNote.Click += (s, e) =>
            {
                meta.Note = null;
                if (baseImg is Grid g)
                    UpdateNoteVisibilityAndText(g, null);
                SaveOwnOverlayToJson();
                UploadOwnOverlayToTeam();
            };
            menu.Items.Add(miDelNote);
        }

        menu.Items.Add(new Separator());

        int maxScreenshots = Services.Auth.SupabaseAuthManager.GetMaxScreenshotsPerBase();

        for (int i = 0; i < meta.Screenshots.Count; i++)
        {
            int index = i;
            var miDelScreen = new MenuItem
            {
                Header = string.Format(CloudText("BaseDeleteScreenshotFormat", "Delete Screenshot {0}"), index + 1)
            };
            miDelScreen.Click += (s, e) =>
            {
                meta.Screenshots.RemoveAt(index);
                if (baseImg is Grid g2) UpdateScreenshotIndicator(g2, meta.Screenshots);
                if (_activeBaseHoverAnchor == baseImg && BaseHoverPopup != null)
                    BaseHoverPopup.Visibility = Visibility.Collapsed;
                if (_activeGalleryAnchor == baseImg && BaseGalleryPopup != null)
                    BaseGalleryPopup.Visibility = Visibility.Collapsed;
                SaveOwnOverlayToJson();
                UploadOwnOverlayToTeam();
            };
            menu.Items.Add(miDelScreen);
        }

        if (meta.Screenshots.Count < maxScreenshots)
        {
            var miAddScreen = new MenuItem { Header = CloudText("BaseAddScreenshot", "Add Screenshot") };
            miAddScreen.Click += (s, e) =>
            {
                var dlg = new Views.Windows.BaseScreenshotWindow { Owner = this };
                if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.Base64Result))
                {
                    meta.Screenshots.Add(dlg.Base64Result);
                    if (baseImg is Grid g3) UpdateScreenshotIndicator(g3, meta.Screenshots);
                    SaveOwnOverlayToJson();
                    UploadOwnOverlayToTeam();
                }
            };
            menu.Items.Add(miAddScreen);
        }
        else if (Services.Auth.SupabaseAuthManager.CurrentTier == "free")
        {
            var miLockedScreen = new MenuItem
            {
                Header = string.Format(CloudText("BaseAddScreenshotLockedFormat", "Add Screenshot {0} (Premium required)"), maxScreenshots + 1),
                IsEnabled = true
            };
            miLockedScreen.Foreground = Brushes.Gray;
            miLockedScreen.Click += (s, e) =>
            {
                ShowPremiumLimitDialog(Properties.Resources.PremiumInfoMapDesc);
            };
            menu.Items.Add(miLockedScreen);
        }

        menu.Items.Add(new Separator());

        var miDeleteBase = new MenuItem { Header = CloudText("Delete", "Delete") };
        miDeleteBase.Click += (s, e) =>
        {
            Overlay.Children.Remove(baseImg);
            if (_playerOverlayElements.TryGetValue(_mySteamId, out var mine))
            {
                mine.Remove(baseImg);
            }
            if (_activeBaseHoverAnchor == baseImg && BaseHoverPopup != null)
                BaseHoverPopup.Visibility = Visibility.Collapsed;
            if (_activeGalleryAnchor == baseImg && BaseGalleryPopup != null)
                BaseGalleryPopup.Visibility = Visibility.Collapsed;
            SaveOwnOverlayToJson();
            UploadOwnOverlayToTeam();
        };
        menu.Items.Add(miDeleteBase);

        baseImg.ContextMenu = menu;
        menu.IsOpen = true;
    }
}
