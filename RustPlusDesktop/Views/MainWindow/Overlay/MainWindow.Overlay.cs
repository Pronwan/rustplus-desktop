using RustPlusDesk.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private Dictionary<OverlayToolMode, Button> _toolButtons;

private bool _overlayToolsVisible = false;

    // wer ist aktuell ausgewählt als Zeichenwerkzeug?
    private enum OverlayToolMode { None, Draw, Text, Icon, Erase }
    private OverlayToolMode _currentTool = OverlayToolMode.None;

    // Color/Size Settings usw.:
    private Color _drawColor = Colors.Red;
    private double _drawThickness = 2.0;
    private double _eraserSize = 10.0;
    private Color _textColor = Colors.White;
    private double _textSize = 16.0;
    private string _currentIconPath = "pack://application:,,,/Assets/icons/map-icons/base1.png";

    // Für Draggen von platzierten Icons/Text
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




    private void RebuildOverlayTeamBar()
    {
        if (OverlayTeamStack == null) return;

        OverlayTeamStack.Children.Clear();

        foreach (var tm in TeamMembers)
        {
            // Wir wollen nur existierende Spieler mit valider SteamID
            if (tm.SteamId == 0)
                continue;

            // Button mit Avatar als Inhalt
            var btn = new Button
            {
                Width = 32,
                Height = 32,
                Margin = new Thickness(4, 0, 4, 0),
                ToolTip = tm.Name,    // Steam-Name als Tooltip
                Tag = tm.SteamId,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(_visibleOverlayOwners.Contains(tm.SteamId) ? 2 : 1),
                BorderBrush = _visibleOverlayOwners.Contains(tm.SteamId)
                                ? Brushes.LimeGreen
                                : Brushes.Gray,
                Background = Brushes.Transparent
            };

            btn.Click += OverlayTeamButton_Click;

            var img = new Image
            {
                Width = 32,
                Height = 32,
                Stretch = Stretch.UniformToFill,
                Source = tm.Avatar ?? GetPlaceholderAvatar(), // falls Avatar noch lädt
                SnapsToDevicePixels = true
            };

            btn.Content = img;
            OverlayTeamStack.Children.Add(btn);
        }
    }

    private ImageSource GetPlaceholderAvatar()
    {
        // Kannst du schöner machen (graues Quadrat, Fragezeichen, etc.)
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

            if (_playerOverlayElements.TryGetValue(steamId, out var listToHide))
            {
                foreach (var fe in listToHide)
                    fe.Visibility = Visibility.Collapsed;
            }

            RebuildOverlayTeamBar();
            return;
        }

        // ----- FALL 2: Spieler ist nicht sichtbar -> wir wollen ihn einblenden
        AppendLog($"[overlay/ui] {steamId} currently NOT visible -> showing / (re)loading if needed");

        _visibleOverlayOwners.Add(steamId);

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

        // 3. Müssen wir neu bauen?
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

        // Nach MaterializeOverlayForPlayer weißt du,
        // wie viele Elemente der Spieler jetzt wirklich auf dem Canvas hat:
        if (_playerOverlayElements.TryGetValue(steamId, out var listBuilt))
        {
            AppendLog($"[overlay/disk] {steamId}: materialized {listBuilt.Count} canvas elements");
        }
    }

    private async Task<bool> TryFetchAndUpdateOverlayAsync(ulong steamId)
    {
        try
        {
            var serverKey = GetServerKey();
            var ts = UnixNow().ToString();

            var msg = $"{steamId}|{serverKey}|{ts}";
            var sig = HmacSha256Hex(OVERLAY_SYNC_SECRET_HEX, msg);

            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(5);

                var url = OVERLAY_SYNC_BASEURL
                    + "/fetch"
                    + "?steamId=" + WebUtility.UrlEncode(steamId.ToString())
                    + "&serverKey=" + WebUtility.UrlEncode(serverKey)
                    + "&ts=" + WebUtility.UrlEncode(ts)
                    + "&sig=" + WebUtility.UrlEncode(sig);

               // AppendLog($"[overlay/net] GET {url}");

                var resp = await http.GetAsync(url);

                if (resp.StatusCode == HttpStatusCode.NotFound)
                {
                    AppendLog($"[overlay/net] {steamId}: 404 (no remote overlay available)");
                    return false;
                }
                if (!resp.IsSuccessStatusCode)
                {
                    AppendLog($"[overlay/net][warn] {steamId}: fetch failed HTTP {(int)resp.StatusCode}");
                    return false;
                }

                var bodyJson = await resp.Content.ReadAsStringAsync();
                var doc = System.Text.Json.JsonDocument.Parse(bodyJson);

                if (!doc.RootElement.TryGetProperty("overlayJsonB64", out var b64El))
                {
                    AppendLog($"[overlay/net][warn] {steamId}: server response missing overlayJsonB64");
                    return false;
                }

                var b64 = b64El.GetString();
                if (string.IsNullOrEmpty(b64))
                {
                    AppendLog($"[overlay/net][warn] {steamId}: overlayJsonB64 empty");
                    return false;
                }

                var raw = Convert.FromBase64String(b64);
                var remoteJson = Encoding.UTF8.GetString(raw);

                // remote parsed
                OverlaySaveData? remoteData = null;
                try
                {
                    remoteData = System.Text.Json.JsonSerializer.Deserialize<OverlaySaveData>(remoteJson);
                }
                catch
                {
                    remoteData = null;
                }

                if (remoteData == null)
                {
                    AppendLog($"[overlay/net][warn] {steamId}: remote json invalid");
                    return false;
                }

                long remoteTs = remoteData.LastUpdatedUnix;
                var path = GetOverlayJsonPathForPlayerServer(steamId);

                long localTs = 0;
                OverlaySaveData? localData = null;

                if (File.Exists(path))
                {
                    try
                    {
                        var localJson = File.ReadAllText(path);
                        localData = System.Text.Json.JsonSerializer.Deserialize<OverlaySaveData>(localJson);
                    }
                    catch { /* ignore */ }

                    if (localData != null)
                        localTs = localData.LastUpdatedUnix;
                }

                if (steamId == _mySteamId)
                {
                    // eigenes Overlay -> nur überschreiben, wenn remote neuer ist
                    AppendLog($"[overlay/net] self {steamId}: remoteTs={remoteTs}, localTs={localTs}");

                    if (remoteTs > localTs)
                    {
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                        File.WriteAllText(path, remoteJson);
                        AppendLog($"[overlay/net] self {steamId}: wrote NEWER remote overlay to {path} (remote newer)");
                        return true; // lokal geupdatet
                    }
                    else
                    {
                        AppendLog($"[overlay/net] self {steamId}: kept LOCAL overlay (local newer or same)");
                        return false;
                    }
                }
                else
                {
                    // fremdes Overlay -> immer remote Wahrheit
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, remoteJson);
                    AppendLog($"[overlay/net] teammate {steamId}: wrote remote overlay to {path} (always trust remote)");
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
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
        // Falls wir für mich (_mySteamId) schon Elemente gebaut haben, nicht nochmal
        if (_playerOverlayElements.ContainsKey(_mySteamId) &&
            _playerOverlayElements[_mySteamId].Count > 0)
        {
            return;
        }

        var path = GetOverlayJsonPathForPlayerServer(_mySteamId);
        if (!File.Exists(path))
        {
            // Stelle sicher, dass wir zumindest einen leeren Eintrag haben,
            // damit spätere Checks nicht glauben "muss noch laden".
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
            // kaputte Datei? -> wir tun so, als gäbe es keine
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
                IsHitTestVisible = true, // meine Icons/Text darf ich draggen und löschen
                Opacity = 1.0,
                Tag = new OverlayTag
                {
                    OwnerSteamId = _mySteamId,
                    IsUserEditable = true
                }
            };

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
        //    oder wir explizit Drag erlauben bei Icon/Text in anderen Tools außer Draw/Text/Icon/Erase)
        if (e.LeftButton == MouseButtonState.Pressed &&
            _currentTool == OverlayToolMode.None)
        {
            TryBeginDragExistingElement(mapPos);
            return;
        }

        // 6) Rechtsklick zum Löschen von Icons/Text-Blöcken
        if (e.ChangedButton == MouseButton.Right)
        {
            TryDeleteElementAt(mapPos);
            return;
        }
    }

    private void EraseAt(Point mapPos)
    {
        var toRemove = new List<Polyline>();

        for (int i = 0; i < Overlay.Children.Count; i++)
        {
            if (Overlay.Children[i] is Polyline line)
            {
                // nur wenn mir gehörend, sonst Finger weg
                if (line.Tag is OverlayTag meta && meta.OwnerSteamId == _mySteamId && meta.IsUserEditable)
                {
                    double dist = DistancePointToPolyline(mapPos, line);
                    if (dist <= _eraserSize)
                    {
                        toRemove.Add(line);
                    }
                }
            }
        }

        foreach (var line in toRemove)
            Overlay.Children.Remove(line);

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

    private const double USER_ICON_BASE_SIZE = 24;   // hier stellst du “doppelt so groß” ein
    private const double USER_ICON_MIN_SCALE = 0.2;  // nicht kleiner werden
    private const double USER_ICON_MAX_SCALE = 2.0;   // nicht riesig werden


    private void PlaceIconAt(Point mapPos)
    {
        // 1. aktuellen effektiven Zoom holen (das ist der gleiche wie bei Shops/Playern)
        double eff = GetEffectiveZoom();

        // 2. “wünschte” Skalierung aus Zoom ableiten
        double scale = 1.0 / eff;

        // 3. auf min / max clampen – GENAU wie im Refresh
        if (scale < USER_ICON_MIN_SCALE)
            scale = USER_ICON_MIN_SCALE;
        if (scale > USER_ICON_MAX_SCALE)
            scale = USER_ICON_MAX_SCALE;

        // 4. Icon bauen
        var img = new Image
        {
            Source = new BitmapImage(new Uri(_currentIconPath, UriKind.RelativeOrAbsolute)),
            Width = USER_ICON_BASE_SIZE,
            Height = USER_ICON_BASE_SIZE,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(scale, scale),
            Tag = new OverlayTag
            {
                OwnerSteamId = _mySteamId,
                IsUserEditable = true,
                BaseSize = USER_ICON_BASE_SIZE
            }
        };

        // 5. Canvas-Position IMMER aus der Basisgröße ableiten
        Canvas.SetLeft(img, mapPos.X - USER_ICON_BASE_SIZE / 2);
        Canvas.SetTop(img, mapPos.Y - USER_ICON_BASE_SIZE / 2);

        // 6. ins Overlay
        Overlay.Children.Add(img);
        RegisterElementForOwner(_mySteamId, img);

        // 7. speichern (nimmt BASIS-W/H, nicht die skalierten Pixel – das ist korrekt!)
        SaveOwnOverlayToJson();
    }

    private class UserIconTag
    {
        public double X;
        public double Y;
    }


    private void RefreshUserOverlayIcons()
    {
        if (Overlay == null) return;

        double eff = GetEffectiveZoom();
        double scale = 1.0 / eff;

        // Untergrenze
        if (scale < USER_ICON_MIN_SCALE)
            scale = USER_ICON_MIN_SCALE;

        // OBERgrenze – hier kommt dein maxScale hin
        if (scale > USER_ICON_MAX_SCALE)
            scale = USER_ICON_MAX_SCALE;

        foreach (var child in Overlay.Children)
        {
            if (child is Image img && img.Tag is OverlayTag meta)
            {
                // nur Icons anfassen – Strokes/Text bleiben wie sie sind
                // wenn du GANZ sicher sein willst, dass es wirklich ein "Overlay-Icon" ist:
                // if (meta.BaseSize is null) continue;

                img.RenderTransformOrigin = new Point(0.5, 0.5);
                img.RenderTransform = new ScaleTransform(scale, scale);
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
                // nur meine editierbaren Elemente dürfen gezogen werden
                if (fe.Tag is not OverlayTag meta) continue;
                if (meta.OwnerSteamId != _mySteamId) continue;
                if (!meta.IsUserEditable) continue;

                double x = Canvas.GetLeft(fe);
                double y = Canvas.GetTop(fe);

                double w = fe is Image img ? img.Width :
                           (fe.ActualWidth > 0 ? fe.ActualWidth : 32);
                double h = fe is Image img2 ? img2.Height :
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
        // Lösche Icon/Text bei Rechtsklick, aber nur mein eigenes Zeug
        for (int i = Overlay.Children.Count - 1; i >= 0; i--)
        {
            if (Overlay.Children[i] is FrameworkElement fe)
            {
                // Lines (Polyline) ignorieren wir hier weiter, die macht Eraser.
                if (fe is Polyline) continue;

                // Besitz prüfen
                if (fe.Tag is not OverlayTag meta) continue;
                if (meta.OwnerSteamId != _mySteamId) continue;
                if (!meta.IsUserEditable) continue;

                double x = Canvas.GetLeft(fe);
                double y = Canvas.GetTop(fe);

                // Wenn WPF noch kein ActualWidth/Height gemessen hat, fallback:
                double w = fe is Image img ? img.Width : (fe.ActualWidth > 0 ? fe.ActualWidth : 32);
                double h = fe is Image img2 ? img2.Height : (fe.ActualHeight > 0 ? fe.ActualHeight : 16);

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

    // Rechtsklick -> löschen
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
        }
        else
        {
            RebuildOverlayTeamBar();
            UpdateToolButtonHighlights();
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

        // 2. Sicherheits-Cleanup für evtl. übriggebliebene Ownerelemente
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

        // 3. Neues Overlay (jetzt leer) speichern,
        //    dabei Devices aus der bestehenden Datei beibehalten
        SaveOwnOverlayToJson();

        // 4. Leeres Overlay + Devices an Team hochladen
        UploadOwnOverlayToTeam();
    }

    private void ToolUploadButton_Click(object sender, RoutedEventArgs e)
    {
        UploadOwnOverlayToTeam();
    }

    // private void SaveOwnOverlayToPng()
    // {
    // 1. Zielgröße bestimmen
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

    // ansonsten klonen wir "oberflächlich":
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

        // Farbe ändern (nur ein Beispiel)
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
            // optional, aber sinnvoll: sicherstellen, dass die lokale Datei "aktuell" ist
            try
            {
                await TryFetchAndUpdateOverlayAsync(_mySteamId);
            }
            catch { /* nicht kritisch */ }

            // 0) vorhandene JSON (für Devices) einlesen
            OverlaySaveData? existing = null;
            var localPath = GetOverlayJsonPathForPlayerServer(_mySteamId);

            if (File.Exists(localPath))
            {
                try
                {
                    var oldJson = File.ReadAllText(localPath);
                    existing = System.Text.Json.JsonSerializer.Deserialize<OverlaySaveData>(oldJson);
                }
                catch (Exception ex)
                {
                    AppendLog("[overlay] Couldn't read existing overlay for merge: " + ex.Message);
                }
            }

            // 1) aktuelles Overlay aus dem Canvas bauen
            var data = BuildCurrentOverlaySaveDataForMe();

            // 2) Devices aus bestehender Datei übernehmen (falls vorhanden)
            if (existing?.Devices != null && existing.Devices.Count > 0)
            {
                data.Devices.Clear(); // falls BuildCurrentOverlaySaveDataForMe() eine leere Liste angelegt hat
                foreach (var dev in existing.Devices)
                    data.Devices.Add(dev);
            }

            data.LastUpdatedUnix = UnixNow();

            // 3) JSON bauen
            var json = System.Text.Json.JsonSerializer.Serialize(
                data,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = false });

            var rawBytes = Encoding.UTF8.GetBytes(json);
            if (rawBytes.Length > OVERLAY_MAX_BYTES)
            {
                AppendLog("[overlay] Upload too big (>350KB).");
                return;
            }

            var overlayB64 = Convert.ToBase64String(rawBytes);

            var serverKey = GetServerKey();
            var ts = UnixNow().ToString();
            var sigInput = _mySteamId.ToString() + "|" + serverKey + "|" + ts + "|" + overlayB64;
            var sig = HmacSha256Hex(OVERLAY_SYNC_SECRET_HEX, sigInput);

            var payloadObj = new
            {
                steamId = _mySteamId.ToString(),
                serverKey = serverKey,
                ts = ts,
                overlayJsonB64 = overlayB64,
                sig = sig
            };

            var payloadJson = System.Text.Json.JsonSerializer.Serialize(payloadObj);
            var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

            using (var http = new HttpClient())
            {
                var url = OVERLAY_SYNC_BASEURL + "/upload";
                var resp = await http.PostAsync(url, content);
                if (!resp.IsSuccessStatusCode)
                {
                    AppendLog("[overlay] Upload failed: HTTP " + (int)resp.StatusCode);
                    return;
                }
            }

            // 4) gleiche JSON auch lokal speichern (damit Import sofort drauf zugreift)
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(localPath)!);
            File.WriteAllText(localPath, json);

            AppendLog($"[overlay] Overlay uploaded (devices preserved: {data.Devices.Count}).");
        }
        catch (Exception ex)
        {
            AppendLog("[overlay] Upload Error: " + ex.Message);
        }
    }

    private async Task<bool> TryFetchOverlayFromServerAsync(ulong steamId)
    {
        try
        {
            var serverKey = GetServerKey();
            var ts = UnixNow().ToString();

            // Wir signieren dieselbe Formel wie der Server in /fetch prüft:
            // msg = "<steamId>|<serverKey>|<ts>|<overlayB64>"
            // ABER: Wir kennen overlayB64 ja noch nicht vorm Request 🤔
            //
            // Lösung: Wir machen es wie folgt:
            // - Server-Code oben hat overlay_b64 mit in die Signatur genommen.
            //   Das bedeutet: Client muss erst overlay_b64 kennen. Das geht so natürlich nicht.
            //
            // Also müssen wir eine kleine Änderung machen:
            // Variante A (einfach): wir ändern /fetch auf dem Server so, dass er
            // NUR steamId|serverKey|ts signed, OHNE overlayB64.
            //
            // Mach das bitte gleich am Server (fetch-Teil ersetzen):

            // (Wir nehmen jetzt an, du hast den Server so geändert wie unten beschrieben.)
            // Dann bauen wir hier:
            var msg = $"{steamId}|{serverKey}|{ts}";
            var sig = HmacSha256Hex(OVERLAY_SYNC_SECRET_HEX, msg);

            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(5);

                var url = OVERLAY_SYNC_BASEURL
                    + "/fetch"
                    + "?steamId=" + WebUtility.UrlEncode(steamId.ToString())
                    + "&serverKey=" + WebUtility.UrlEncode(serverKey)
                    + "&ts=" + WebUtility.UrlEncode(ts)
                    + "&sig=" + WebUtility.UrlEncode(sig);

                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    AppendLog("[overlay] fetch failed HTTP " + (int)resp.StatusCode);
                    return false;
                }

                var bodyJson = await resp.Content.ReadAsStringAsync();
                // Erwartet: { "overlayJsonB64": "...." }
                var doc = System.Text.Json.JsonDocument.Parse(bodyJson);
                if (!doc.RootElement.TryGetProperty("overlayJsonB64", out var b64El))
                    return false;

                var b64 = b64El.GetString();
                if (string.IsNullOrEmpty(b64))
                    return false;

                var raw = Convert.FromBase64String(b64);
                var jsonOverlay = Encoding.UTF8.GetString(raw);

                // Schreib's lokal so wie SaveOwnOverlayToJson das erwartet:
                var path = GetOverlayJsonPathForPlayerServer(steamId);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                File.WriteAllText(path, jsonOverlay);

                return true;
            }
        }
        catch (Exception ex)
        {
            AppendLog("[overlay] fetch error: " + ex.Message);
            return false;
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

        // Projektion t = (AP·AB)/|AB|² clamped [0..1]
        double denom = (vx * vx + vy * vy);
        double t = denom <= 0.000001 ? 0.0 : ((wx * vx + wy * vy) / denom);
        if (t < 0.0) t = 0.0;
        else if (t > 1.0) t = 1.0;

        // Nächster Punkt auf AB
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
            // toggle off -> zurück in Pan/Zoom Modus
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
    }
    private void UpdateToolButtonHighlights()
    {
        // Erstmal alle zurücksetzen
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
            // 0) vorhandene Datei einlesen (falls vorhanden), um Devices zu retten
            OverlaySaveData? existing = null;
            var path = GetOverlayJsonPathForPlayerServer(_mySteamId);

            if (File.Exists(path))
            {
                try
                {
                    var oldJson = File.ReadAllText(path);
                    existing = System.Text.Json.JsonSerializer.Deserialize<OverlaySaveData>(oldJson);
                }
                catch (Exception ex)
                {
                    AppendLog("[overlay] Couldn't read existing overlay for merge (Save): " + ex.Message);
                }
            }

            // 1) aktuelles Overlay aus dem Canvas bauen (ohne Devices)
            var data = BuildCurrentOverlaySaveDataForMe();

            // 2) Devices aus bestehender Datei übernehmen (falls vorhanden)
            if (existing?.Devices != null && existing.Devices.Count > 0)
            {
                data.Devices.Clear();
                foreach (var dev in existing.Devices)
                    data.Devices.Add(dev);
            }

            // 3) (optional) Timestamp aktualisieren – kannst du auch weglassen,
            //    weil BuildCurrentOverlaySaveDataForMe() ihn schon setzt.
            data.LastUpdatedUnix = UnixNow();

            // 4) JSON schreiben
            var json = System.Text.Json.JsonSerializer.Serialize(
                data,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            AppendLog("[overlay] SaveOwnOverlayToJson error: " + ex.Message);
        }
    }

    private string GetServerKey()
    {
        // simplest first pass: nimm Host-Port vom aktuell ausgewählten Server
        var prof = _vm?.Selected;
        if (prof == null) return "unknown-server";

        // du hast im Connect-Code `_vm.Selected.Host` und `_vm.Selected.Port`
        return $"{prof.Host}-{prof.Port}";
    }

    private string GetOverlayJsonPathForPlayerServer(ulong steamId)
    {
        var baseDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RustPlusDesk",
            "Overlays",
            GetServerKey()
        );
        Directory.CreateDirectory(baseDir);
        return System.IO.Path.Combine(baseDir, $"{steamId}.json");
    }


    private void ClearUserOverlayElements()
    {
        // 1) Sammeln, nicht während foreach löschen
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
        public double? BaseSize;   // nur für Icons, wird NICHT gespeichert
    }

    // --- Overlay Sync Config ---
    private const string OVERLAY_SYNC_SECRET_HEX =
    "23c5a7dbf02b63543da043ca7d6de1fbf706a080c899e334a8cd599206e13fde";

    // dein Server (IP oder DNS + Port). Kein "/" am Ende.
    private const string OVERLAY_SYNC_BASEURL = "http://85.214.193.250:5000";

    // Hard-Limits müssen mit dem Python-Server matchen
    private const int OVERLAY_MAX_BYTES = 350_000; // ~350 KB Limit roh

    // ---- HMAC / Network Helpers ------------------------------------------



    private static byte[] HexToBytes(string hex)
    {
        if (hex.Length % 2 != 0)
            throw new ArgumentException("invalid hex length");

        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < hex.Length; i += 2)
        {
            bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
        }
        return bytes;
    }

    private static string HmacSha256Hex(string hexKey, string dataUtf8)
    {
        // 1) Secret aus Hex in echte Bytes
        var keyBytes = HexToBytes(hexKey);

        // 2) Daten als UTF8-Bytes
        var payloadBytes = Encoding.UTF8.GetBytes(dataUtf8);

        // 3) HMAC-SHA256
        using (var hmac = new System.Security.Cryptography.HMACSHA256(keyBytes))
        {
            var hash = hmac.ComputeHash(payloadBytes);

            // 4) hex-lowercase string bauen wie Python .hexdigest()
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    private static long UnixNow()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    // Liest lokales Overlay (mich) als OverlaySaveData
    private OverlaySaveData BuildCurrentOverlaySaveDataForMe()
    {
        var data = new OverlaySaveData();
        data.LastUpdatedUnix = UnixNow(); // NEU: stamp jetzt

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
                            IconPath = bi?.UriSource?.ToString() ?? _currentIconPath,
                            X = x,
                            Y = y,
                            Width = img.Width,
                            Height = img.Height
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

    // baut aus einem OverlaySaveData echte UI-Elemente auf der Canvas für einen Spieler
    // und cached sie in _playerOverlayElements[steamId]
    private void MaterializeOverlayForPlayer(ulong steamId, OverlaySaveData data, bool editableIfMine)
    {
        // falls schon Elemente für den Spieler existieren -> erstmal killen
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
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(icon.IconPath, UriKind.RelativeOrAbsolute);
            bi.EndInit();
            bi.Freeze();

            var img = new Image
            {
                Source = bi,
                Width = icon.Width,
                Height = icon.Height,
                RenderTransformOrigin = new Point(0.5, 0.5),
                IsHitTestVisible = editableIfMine, // meine Icons kann ich anfassen
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

            Canvas.SetLeft(img, icon.X);
            Canvas.SetTop(img, icon.Y);

            Overlay.Children.Add(img);
            list.Add(img);
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

    // holt OverlaySaveData aus JSON-Text
    private static OverlaySaveData? ParseOverlayJson(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<OverlaySaveData>(json);
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> TryFetchOverlayForPlayerFromServerAsync(ulong steamId)
    {
        try
        {
            var serverKey = GetServerKey();
            var ts = UnixNow().ToString();
            // Signatur beim GET: steamId|serverKey|ts
            var sigInput = steamId.ToString() + "|" + serverKey + "|" + ts;
            var sig = HmacSha256Hex(OVERLAY_SYNC_SECRET_HEX, sigInput);

            var url = $"{OVERLAY_SYNC_BASEURL}/fetch" +
                      $"?steamId={Uri.EscapeDataString(steamId.ToString())}" +
                      $"&serverKey={Uri.EscapeDataString(serverKey)}" +
                      $"&ts={Uri.EscapeDataString(ts)}" +
                      $"&sig={Uri.EscapeDataString(sig)}";

            using (var http = new HttpClient())
            {
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    // 404 ist einfach "hat nix hochgeladen" -> kein Fehler ins Log spammen
                    if ((int)resp.StatusCode != 404)
                        AppendLog("[overlay] Fetch HTTP " + (int)resp.StatusCode);
                    return false;
                }

                var body = await resp.Content.ReadAsStringAsync();
                // body hat {"overlayJsonB64": "..."}
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("overlayJsonB64", out var b64El))
                    return false;

                var b64 = b64El.GetString();
                if (string.IsNullOrEmpty(b64)) return false;

                byte[] decoded = Convert.FromBase64String(b64);
                if (decoded.Length > OVERLAY_MAX_BYTES)
                {
                    AppendLog("[overlay] Remote Overlay too big.");
                    return false;
                }

                var jsonOverlay = Encoding.UTF8.GetString(decoded);
                var data = ParseOverlayJson(jsonOverlay);
                if (data == null)
                {
                    AppendLog("[overlay] Remote Overlay broken.");
                    return false;
                }

                // lokal speichern für diesen Spieler
                WriteOverlaySaveDataLocal(steamId, data);

                // Canvas-Objekte bauen / ersetzen
                bool editable = (steamId == _mySteamId);
                MaterializeOverlayForPlayer(steamId, data, editable);

                AppendLog("[overlay] Overlay loaded from " + steamId + ".");
                return true;
            }
        }
        catch (Exception ex)
        {
            AppendLog("[overlay] Fetch Error: " + ex.Message);
            return false;
        }
    }

    private void LoadOverlayFromDiskForPlayer(ulong steamId)
    {
        var path = GetOverlayJsonPathForPlayerServer(steamId);
        if (!File.Exists(path))
        {
            // registriere leere Liste (damit wir nicht endlos neu versuchen)
            if (!_playerOverlayElements.ContainsKey(steamId))
                _playerOverlayElements[steamId] = new List<FrameworkElement>();
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var data = ParseOverlayJson(json);
            if (data == null)
            {
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
}
