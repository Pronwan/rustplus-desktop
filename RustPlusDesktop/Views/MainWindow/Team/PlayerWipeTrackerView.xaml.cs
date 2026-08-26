using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using RustPlusDesk.Services.PlayerWipeTracker;

namespace RustPlusDesk.Views;

// Full-screen workspace (embedded in MainWindow like the Raid Calculator) for the Player Wipe
// Tracker. It intentionally owns its palette and templates and uses only native WPF controls.
// The host feeds it the live tracker + current wipe map through Initialize().
public partial class PlayerWipeTrackerView : UserControl
{
    private PlayerWipeTrackerService? _tracker;
    private ulong _ownSteamId;
    private ImageSource? _wipeMapImage;
    private int _worldSize;
    private Rect _worldRectPixels;
    private string? _loadedWipeKey;
    private string? _liveConnectedWipeKey;
    private readonly DispatcherTimer _replayTimer;
    private readonly DispatcherTimer _liveTimer;
    private IReadOnlyList<TrackerPoint> _points = Array.Empty<TrackerPoint>();
    private IReadOnlyList<TrackerMonument> _monuments = Array.Empty<TrackerMonument>();
    private double _replaySpeed = 1;
    private int _replayIndex;
    private bool _refreshing;
    private bool _showUnknown;
    private bool _showMonuments = true;
    private Grid? _dragViewport;
    private TranslateTransform? _dragTranslate;
    private Point _dragStart;
    private Point _dragOrigin;

    public PlayerWipeTrackerView()
    {
        _replayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _replayTimer.Tick += ReplayTimer_Tick;
        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _liveTimer.Tick += LiveTimer_Tick;

        InitializeComponent();
        IsVisibleChanged += PlayerWipeTrackerView_IsVisibleChanged;
    }

    public event RoutedEventHandler? CloseRequested;

    // Called by the host each time the workspace is opened. The tracker instance is stable, so
    // this mainly refreshes the wipe map context (which follows server switches) and re-renders.
    public void Initialize(
        PlayerWipeTrackerService tracker,
        ulong ownSteamId,
        ImageSource? wipeMapImage,
        int worldSize,
        Rect worldRectPixels,
        IReadOnlyList<TrackerMonument>? monuments = null)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _ownSteamId = ownSteamId;
        _liveConnectedWipeKey = tracker.CurrentWipeKey;
        _wipeMapImage = wipeMapImage;
        _worldSize = worldSize;
        _worldRectPixels = worldRectPixels;
        _monuments = monuments ?? Array.Empty<TrackerMonument>();
        _loadedWipeKey = tracker.CurrentWipeKey;
        ApplyWipeMapImage();
        PopulateWipeSelector();
        if (IsLoaded)
        {
            _liveTimer.Start();
            Refresh();
        }
    }

    private void View_Loaded(object sender, RoutedEventArgs e)
    {
        if (_tracker is null)
            return;
        _liveTimer.Start();
        PopulateWipeSelector();
        Refresh();
    }

    private void PlayerWipeTrackerView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_tracker is null)
            return;
        if (IsVisible)
        {
            _liveTimer.Start();
            PopulateWipeSelector();
            LiveTimer_Tick(this, EventArgs.Empty);
        }
        else
        {
            _liveTimer.Stop();
            _replayTimer.Stop();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, e);

    // Live tick keeps the workspace current with the running session and, critically, with
    // server switches: reconnecting to another server re-keys the tracker, and Refresh()
    // reloads that wipe's map, players, and data. It backs off while a route replay plays
    // so it never fights the user's scrub position.
    private void LiveTimer_Tick(object? sender, EventArgs e)
    {
        if (_tracker is null || _replayTimer.IsEnabled || ReferenceEquals(TrackerTabs.SelectedItem, ReplayTab))
            return;

        if (!string.Equals(_tracker.CurrentWipeKey, _liveConnectedWipeKey, StringComparison.Ordinal))
        {
            _liveConnectedWipeKey = _tracker.CurrentWipeKey;
            PopulateWipeSelector();
        }

        var selectedItem = WipeSelector.SelectedItem as StoredWipeItem;
        if (selectedItem is not null && !selectedItem.IsLive && !string.Equals(selectedItem.Summary.WipeKey, _tracker.CurrentWipeKey, StringComparison.Ordinal))
            return;

        Refresh();
    }

    private void PopulateWipeSelector()
    {
        if (_tracker is null)
            return;

        var currentWipeKey = _tracker.CurrentWipeKey;
        var stored = _tracker.GetStoredWipes();
        var selectedItem = WipeSelector.SelectedItem as StoredWipeItem;
        var targetWipeKey = selectedItem?.Summary.WipeKey ?? currentWipeKey;

        var items = new List<StoredWipeItem>();

        if (!string.IsNullOrWhiteSpace(currentWipeKey) && _tracker.TrackedPlayers.Count > 0 && stored.All(s => s.WipeKey != currentWipeKey))
        {
            var serverKey = _tracker.CurrentServerKey ?? "current";
            var serverName = PlayerWipeTrackerStore.ResolveServerName(serverKey);
            var liveSummary = new StoredWipeSummary(
                serverKey,
                serverName,
                currentWipeKey,
                _tracker.CurrentWipeStartedAtUtc,
                DateTime.UtcNow,
                _tracker.TrackedPlayers.Count,
                0,
                0,
                _tracker.HasCurrentWipeMap);
            items.Add(new StoredWipeItem(liveSummary, isLive: true));
        }

        foreach (var s in stored)
        {
            var isLive = !string.IsNullOrWhiteSpace(currentWipeKey) && string.Equals(s.WipeKey, currentWipeKey, StringComparison.Ordinal);
            items.Add(new StoredWipeItem(s, isLive));
        }

        _refreshing = true;
        try
        {
            WipeSelector.ItemsSource = items;
            var match = items.FirstOrDefault(i => string.Equals(i.Summary.WipeKey, targetWipeKey, StringComparison.Ordinal))
                        ?? items.FirstOrDefault(i => i.IsLive)
                        ?? items.FirstOrDefault();
            WipeSelector.SelectedItem = match;

            if (match is not null)
            {
                if (!string.Equals(match.Summary.WipeKey, _tracker.CurrentWipeKey, StringComparison.Ordinal))
                {
                    _tracker.SwitchWipe(match.Summary.ServerKey, match.Summary.WipeKey, match.Summary.WipeStartedAtUtc);
                }
                _loadedWipeKey = null;
                ReloadWipeContextFromService();
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void WipeSelector_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _tracker is null || WipeSelector.SelectedItem is not StoredWipeItem item)
            return;

        if (string.Equals(item.Summary.WipeKey, _tracker.CurrentWipeKey, StringComparison.Ordinal) && _loadedWipeKey is not null)
            return;

        _tracker.SwitchWipe(item.Summary.ServerKey, item.Summary.WipeKey, item.Summary.WipeStartedAtUtc);
        _loadedWipeKey = null;
        ReloadWipeContextFromService();
        Refresh();
    }

    // Pulls the current wipe's stored map from the tracker so the workspace follows a server
    // switch instead of keeping the map it was opened with.
    private void ReloadWipeContextFromService()
    {
        if (_tracker is null)
            return;
        var wipeKey = _tracker.CurrentWipeKey;
        if (string.Equals(wipeKey, _loadedWipeKey, StringComparison.Ordinal) && _wipeMapImage is not null)
            return;

        var map = _tracker.LoadCurrentWipeMap();
        if (map is null)
        {
            _loadedWipeKey = wipeKey;
            _wipeMapImage = null;
            _worldSize = 0;
            _worldRectPixels = Rect.Empty;
            _monuments = Array.Empty<TrackerMonument>();
            ApplyWipeMapImage();
            return;
        }

        var image = DecodeMap(map.PngBytes);
        _loadedWipeKey = wipeKey;
        _wipeMapImage = image;
        _worldSize = map.WorldSize;
        _worldRectPixels = new Rect(map.WorldRectX, map.WorldRectY, map.WorldRectWidth, map.WorldRectHeight);
        _monuments = map.Monuments ?? Array.Empty<TrackerMonument>();
        ApplyWipeMapImage();
    }

    private void ApplyWipeMapImage()
    {
        ReplayMapImage.Source = _wipeMapImage;
        HeatmapMapImage.Source = _wipeMapImage;
        CompareMapImage.Source = _wipeMapImage;
    }

    private static BitmapImage? DecodeMap(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 })
            return null;
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderReplay();
        RenderHeatmap();
        RenderComparison();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void PlayerSelector_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing)
            Refresh();
    }

    public void Refresh()
    {
        if (_tracker is null)
            return;
        ReloadWipeContextFromService();
        var capabilities = _tracker.Capabilities;
        PremiumBanner.Text = capabilities.CanUseAdvancedViews
            ? $"{capabilities.PlanCode.ToUpperInvariant()} FIELD CONSOLE · Cloud backup is opt-in. Route replay, heatmap, comparison, export, and restore are enabled."
            : "FREE FIELD CONSOLE · Self-only local history is available. Premium route replay, heatmap, comparison, export, and cloud restore are locked.";

        ReplayTab.IsEnabled = capabilities.CanUseRouteReplay;
        HeatmapTab.IsEnabled = capabilities.CanUseAdvancedViews;
        CompareTab.IsEnabled = capabilities.CanUseAdvancedViews;
        ExportTab.IsEnabled = capabilities.CanExport;
        CloudTab.IsEnabled = capabilities.CanUseCloudSync;
        RestoreCloudButton.IsEnabled = capabilities.CanUseCloudSync;
        ExportJsonButton.IsEnabled = capabilities.CanExport;
        ExportCsvButton.IsEnabled = capabilities.CanExport;

        var selected = SelectedPlayerId(PlayerSelector);
        var items = BuildPlayerItems();
        _refreshing = true;
        try
        {
            SetPlayerItems(PlayerSelector, items, selected ?? _ownSteamId);
            SetPlayerItems(CompareASelector, items, selected ?? _ownSteamId);
            SetPlayerItems(CompareBSelector, items, items.FirstOrDefault(item => item.SteamId != (selected ?? _ownSteamId))?.SteamId ?? selected ?? _ownSteamId);
        }
        finally
        {
            _refreshing = false;
        }

        RefreshSelectedPlayer();
    }

    private void RefreshSelectedPlayer()
    {
        if (_tracker is null)
            return;
        var steamId = SelectedPlayerId(PlayerSelector) ?? _ownSteamId;
        var observations = _tracker.GetObservations(steamId);
        _points = ToPoints(observations);
        var summary = _tracker.GetSummary(steamId);

        CoverageText.Text = Format(summary.Coverage);
        UnknownText.Text = Format(summary.Unknown);
        DistanceText.Text = $"{summary.EstimatedDistance:N0}";
        DeathsText.Text = summary.Deaths.ToString();
        PointCountText.Text = _points.Count.ToString();
        StorageText.Text = FormatBytes(_tracker.StorageBytes);
        TimelineList.ItemsSource = _tracker.GetSegments(steamId)
            .OrderByDescending(segment => segment.StartUtc)
            .Select(segment => new
            {
                Start = segment.StartUtc.ToLocalTime().ToString("g"),
                Duration = Format(segment.EndUtc - segment.StartUtc),
                State = segment.State.ToString(),
                Location = segment.LocationName is not null
                    ? (segment.LocationType == TrackerLocationType.Monument ? RustPlusDesk.Services.MonumentFormatter.Beautify(segment.LocationName) : segment.LocationName)
                    : segment.LocationType.ToString(),
            }).ToArray();
        VisitsList.ItemsSource = summary.MonumentVisits
            .OrderByDescending(visit => visit.StartUtc)
            .Select(visit => new { Name = RustPlusDesk.Services.MonumentFormatter.Beautify(visit.Name), Duration = Format(visit.EstimatedDuration) })
            .ToArray();

        RenderActivityRibbon(steamId);
        RenderStateBreakdown(summary);
        PopulateInsights(steamId);

        _replayTimer.Stop();
        _replayIndex = Math.Max(0, _points.Count - 1);
        ReplayPlayButton.Content = "Play";
        ReplayProgress.Maximum = Math.Max(0, _points.Count - 1);
        ReplayProgress.Value = _replayIndex;
        ReplayProgress.IsEnabled = _points.Count > 1 && _tracker.Capabilities.CanUseRouteReplay;
        RenderReplay();
        RenderHeatmap();
        RenderComparison();
    }

    private List<PlayerItem> BuildPlayerItems()
    {
        if (_tracker is null)
            return new List<PlayerItem>();

        var ids = new HashSet<ulong>(_tracker.TrackedPlayers);
        if (_tracker.GetObservations(_ownSteamId).Count > 0 || ids.Count == 0)
            ids.Add(_ownSteamId);

        return ids.Where(id => id != 0)
            .Select(id => new PlayerItem(id, _tracker.GetPlayerName(id)))
            .OrderBy(item => item.SteamId != _ownSteamId)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void SetPlayerItems(ComboBox selector, IReadOnlyList<PlayerItem> items, ulong steamId)
    {
        selector.ItemsSource = items.ToArray();
        selector.SelectedItem = items.FirstOrDefault(item => item.SteamId == steamId) ?? items.FirstOrDefault();
    }

    private static ulong? SelectedPlayerId(Selector selector)
        => (selector.SelectedItem as PlayerItem)?.SteamId;

    private void ReplayPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_tracker is null || _points.Count < 2 || !_tracker.Capabilities.CanUseRouteReplay)
            return;
        if (_replayIndex >= _points.Count - 1)
            _replayIndex = 0;
        if (_replayTimer.IsEnabled)
        {
            _replayTimer.Stop();
            ReplayPlayButton.Content = "Play";
        }
        else
        {
            _replayTimer.Start();
            ReplayPlayButton.Content = "Pause";
        }
        RenderReplay();
    }

    private void ReplayTimer_Tick(object? sender, EventArgs e)
    {
        _replayIndex = Math.Min(_points.Count - 1, _replayIndex + Math.Max(1, (int)Math.Round(_replaySpeed)));
        ReplayProgress.Value = _replayIndex;
        if (_replayIndex >= _points.Count - 1)
        {
            _replayTimer.Stop();
            ReplayPlayButton.Content = "Play";
        }
        RenderReplay();
    }

    private void ReplayProgress_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _replayIndex = Math.Clamp((int)Math.Round(e.NewValue), 0, Math.Max(0, _points.Count - 1));
        RenderReplay();
    }

    private void ReplaySpeed_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ReplaySpeedSelector.SelectedItem is ComboBoxItem item && double.TryParse(item.Tag?.ToString(), out var speed))
            _replaySpeed = speed;
    }

    private void RenderReplay()
    {
        if (!IsLoaded || !ReplayCanvas.IsVisible)
            return;
        ReplayCanvas.Children.Clear();
        if (!TryGetMapProjection(ReplayCanvas, out var projection))
        {
            AddEmptyState(ReplayCanvas, "Load the current wipe map to view route replay.");
            ReplayStateText.Text = "Wipe map unavailable";
            ReplayTimeText.Text = "No map context";
            return;
        }

        DrawWipeGrid(ReplayCanvas, projection);
        DrawMonuments(ReplayCanvas, projection);
        if (_points.Count == 0)
        {
            AddEmptyState(ReplayCanvas, "No valid coordinates recorded yet.");
            ReplayStateText.Text = "Waiting for a valid coordinate";
            ReplayTimeText.Text = "No route data";
            return;
        }

        DrawTrack(ReplayCanvas, _points, point => Project(projection, point.X, point.Y), Color.FromArgb(55, 96, 205, 255), 2, 1, dashed: true);
        var visible = _points.Take(Math.Min(_replayIndex + 1, _points.Count)).ToArray();
        DrawTrack(ReplayCanvas, visible, point => Project(projection, point.X, point.Y), Color.FromRgb(96, 205, 255), 3, 1);
        DrawEventMarkers(ReplayCanvas, _points, point => Project(projection, point.X, point.Y), Color.FromRgb(255, 90, 54));
        if (visible.Length > 0)
        {
            var point = Project(projection, visible[^1].X, visible[^1].Y);
            var marker = new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(Color.FromRgb(255, 184, 76)), Stroke = Brushes.White, StrokeThickness = 2 };
            Canvas.SetLeft(marker, point.X - marker.Width / 2);
            Canvas.SetTop(marker, point.Y - marker.Height / 2);
            Panel.SetZIndex(marker, 4);
            ReplayCanvas.Children.Add(marker);
            ReplayStateText.Text = $"{visible[^1].State} · {visible[^1].LocationName ?? visible[^1].LocationType.ToString()}";
            ReplayTimeText.Text = $"{visible[^1].TimestampUtc.ToLocalTime():g} · {visible.Length}/{_points.Count}";
        }
    }

    private void RenderHeatmap()
    {
        if (!IsLoaded || !HeatmapCanvas.IsVisible)
            return;
        HeatmapCanvas.Children.Clear();
        if (!TryGetMapProjection(HeatmapCanvas, out var projection))
        {
            AddEmptyState(HeatmapCanvas, "Load the current wipe map to view movement density.");
            HeatmapText.Text = "Wipe map unavailable";
            return;
        }

        DrawWipeGrid(HeatmapCanvas, projection);
        DrawMonuments(HeatmapCanvas, projection);
        if (_points.Count == 0)
        {
            AddEmptyState(HeatmapCanvas, "No coordinates recorded yet.");
            HeatmapText.Text = "0 coordinate observations";
            return;
        }

        const double gridSize = 150;
        var columns = Math.Max(1, (int)Math.Ceiling(_worldSize / gridSize));
        var rows = columns;
        var cells = new Dictionary<(int X, int Y), int>();
        foreach (var point in _points)
        {
            var cell = (
                Math.Clamp((int)Math.Floor(point.X / gridSize), 0, columns - 1),
                Math.Clamp((int)Math.Floor((_worldSize - point.Y) / gridSize), 0, rows - 1));
            cells[cell] = cells.TryGetValue(cell, out var count) ? count + 1 : 1;
        }

        var max = cells.Values.DefaultIfEmpty(1).Max();
        var cellTopLeft = Project(projection, 0, _worldSize);
        var cellBottomRight = Project(projection, Math.Min(gridSize, _worldSize), Math.Max(0, _worldSize - gridSize));
        var cellWidth = Math.Max(10, Math.Abs(cellBottomRight.X - cellTopLeft.X) * 1.4);
        var cellHeight = Math.Max(10, Math.Abs(cellBottomRight.Y - cellTopLeft.Y) * 1.4);
        foreach (var (cell, count) in cells)
        {
            var ratio = (double)count / max;
            var cellWest = cell.X * gridSize;
            var cellEast = Math.Min(_worldSize, cellWest + gridSize);
            var cellNorthOffset = cell.Y * gridSize;
            var cellSouthOffset = Math.Min(_worldSize, cellNorthOffset + gridSize);
            var center = Project(
                projection,
                (cellWest + cellEast) / 2,
                _worldSize - (cellNorthOffset + cellSouthOffset) / 2);
            var ellipse = new Ellipse
            {
                Width = cellWidth,
                Height = cellHeight,
                Fill = new SolidColorBrush(HeatColor(ratio)),
                Opacity = 0.3 + ratio * 0.58,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(ellipse, center.X - ellipse.Width / 2);
            Canvas.SetTop(ellipse, center.Y - ellipse.Height / 2);
            Panel.SetZIndex(ellipse, 2);
            HeatmapCanvas.Children.Add(ellipse);
        }

        DrawTrack(HeatmapCanvas, _points, point => Project(projection, point.X, point.Y), Color.FromArgb(110, 255, 255, 255), 1, 0.65, dashed: true);
        HeatmapText.Text = $"{_points.Count:N0} coordinate observations · hottest 150u grid {max:N0} revisits";
    }

    private void CompareSelector_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing)
            RenderComparison();
    }

    private void RenderComparison()
    {
        if (_tracker is null || !IsLoaded || !CompareCanvas.IsVisible)
            return;
        CompareCanvas.Children.Clear();
        if (!TryGetMapProjection(CompareCanvas, out var projection))
        {
            AddEmptyState(CompareCanvas, "Load the current wipe map to compare routes.");
            CompareSummaryText.Text = "Wipe map unavailable";
            return;
        }
        DrawWipeGrid(CompareCanvas, projection);
        DrawMonuments(CompareCanvas, projection);
        var a = SelectedPlayerId(CompareASelector) ?? _ownSteamId;
        var b = SelectedPlayerId(CompareBSelector) ?? a;
        var pointsA = ToPoints(_tracker.GetObservations(a));
        var pointsB = ToPoints(_tracker.GetObservations(b));
        var all = pointsA.Concat(pointsB).ToArray();
        if (all.Length == 0)
        {
            AddEmptyState(CompareCanvas, "Two recorded routes are needed for a comparison.");
            CompareSummaryText.Text = "No comparison data";
            return;
        }

        DrawTrack(CompareCanvas, pointsA, point => Project(projection, point.X, point.Y), Color.FromRgb(96, 205, 255), 3, 1);
        DrawTrack(CompareCanvas, pointsB, point => Project(projection, point.X, point.Y), Color.FromRgb(255, 175, 69), 3, 1);
        DrawEventMarkers(CompareCanvas, pointsA, point => Project(projection, point.X, point.Y), Color.FromRgb(96, 205, 255));
        DrawEventMarkers(CompareCanvas, pointsB, point => Project(projection, point.X, point.Y), Color.FromRgb(255, 175, 69));
        CompareSummaryText.Text = $"{ShortName(a)} {pointsA.Count:N0} pts / {Distance(pointsA):N0}u   ·   {ShortName(b)} {pointsB.Count:N0} pts / {Distance(pointsB):N0}u";
    }

    private void ActivityCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        => RenderActivityRibbon(SelectedPlayerId(PlayerSelector) ?? _ownSteamId);

    private void ToggleUnknown_Click(object sender, RoutedEventArgs e)
    {
        if (_tracker is null)
            return;
        _showUnknown = !_showUnknown;
        ToggleUnknownButton.Content = _showUnknown ? "Hide unknown" : "Show unknown";
        var steamId = SelectedPlayerId(PlayerSelector) ?? _ownSteamId;
        RenderActivityRibbon(steamId);
        RenderStateBreakdown(_tracker.GetSummary(steamId));
    }

    private void ToggleMonuments_Click(object sender, RoutedEventArgs e)
    {
        _showMonuments = !_showMonuments;
        UpdateMonumentsToggleButtons();
        RenderReplay();
        RenderHeatmap();
        RenderComparison();
    }

    private void UpdateMonumentsToggleButtons()
    {
        var text = _showMonuments ? "Hide monuments" : "Show monuments";
        if (ReplayToggleMonumentsButton != null) ReplayToggleMonumentsButton.Content = text;
        if (HeatmapToggleMonumentsButton != null) HeatmapToggleMonumentsButton.Content = text;
        if (CompareToggleMonumentsButton != null) CompareToggleMonumentsButton.Content = text;
    }

    private void RenderActivityRibbon(ulong steamId)
    {
        if (_tracker is null || (!IsLoaded && !ActivityRibbonCanvas.IsLoaded))
            return;

        ActivityRibbonCanvas.Children.Clear();
        BuildStateLegend(ActivityLegend);

        var segments = _tracker.GetSegments(steamId)
            .Where(segment => segment.EndUtc > segment.StartUtc)
            .OrderBy(segment => segment.StartUtc)
            .ToArray();

        var width = ActivityRibbonCanvas.ActualWidth;
        var height = ActivityRibbonCanvas.ActualHeight;
        if (segments.Length == 0)
        {
            RibbonSpanText.Text = "No activity recorded yet.";
            if (width > 20 && height > 10)
                AddCanvasCenterText(ActivityRibbonCanvas, "Connect to the server to start recording this player's wipe.");
            return;
        }

        var spanStart = segments[0].StartUtc;
        var spanEnd = segments[^1].EndUtc;
        var total = (spanEnd - spanStart).TotalSeconds;
        RibbonSpanText.Text = total <= 0
            ? string.Empty
            : $"{spanStart.ToLocalTime():g}  →  {spanEnd.ToLocalTime():g}   ·   {Format(spanEnd - spanStart)} tracked";

        if (width <= 20 || height <= 10 || total <= 0)
            return;

        const double axisHeight = 16;
        var barHeight = height - axisHeight;

        foreach (var segment in segments)
        {
            if (!_showUnknown && segment.State == PlayerActivityState.Unknown)
                continue;
            var x = (segment.StartUtc - spanStart).TotalSeconds / total * width;
            var w = Math.Max(1, (segment.EndUtc - segment.StartUtc).TotalSeconds / total * width);
            var rect = new Rectangle
            {
                Width = w,
                Height = barHeight,
                Fill = new SolidColorBrush(StateColor(segment.State)),
                ToolTip = $"{StateLabel(segment.State)} · {segment.LocationName ?? segment.LocationType.ToString()}\n" +
                          $"{segment.StartUtc.ToLocalTime():g} → {segment.EndUtc.ToLocalTime():g}  ({Format(segment.EndUtc - segment.StartUtc)})",
            };
            if (segment.State == PlayerActivityState.Unknown)
                rect.Opacity = 0.5;
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, 0);
            ActivityRibbonCanvas.Children.Add(rect);
        }

        DrawRibbonAxis(spanStart, spanEnd, width, barHeight, axisHeight);
    }

    private void DrawRibbonAxis(DateTime spanStart, DateTime spanEnd, double width, double barHeight, double axisHeight)
    {
        var totalHours = (spanEnd - spanStart).TotalHours;
        var stepHours = totalHours switch
        {
            <= 6 => 1,
            <= 14 => 2,
            <= 30 => 6,
            <= 80 => 12,
            _ => 24,
        };

        var localStart = spanStart.ToLocalTime();
        var tick = new DateTime(localStart.Year, localStart.Month, localStart.Day, localStart.Hour, 0, 0, DateTimeKind.Local);
        if (tick < localStart)
            tick = tick.AddHours(1);
        while ((tick.Hour % stepHours) != 0)
            tick = tick.AddHours(1);

        var axisBrush = new SolidColorBrush(Color.FromArgb(70, 200, 220, 230));
        var labelBrush = new SolidColorBrush(Color.FromArgb(190, 190, 205, 214));
        var spanSeconds = (spanEnd - spanStart).TotalSeconds;
        var localSpanEnd = spanEnd.ToLocalTime();
        for (; tick <= localSpanEnd; tick = tick.AddHours(stepHours))
        {
            var x = (tick.ToUniversalTime() - spanStart).TotalSeconds / spanSeconds * width;
            if (x < 0 || x > width)
                continue;
            ActivityRibbonCanvas.Children.Add(new Line
            {
                X1 = x, X2 = x, Y1 = 0, Y2 = barHeight,
                Stroke = axisBrush, StrokeThickness = 0.7, IsHitTestVisible = false,
            });
            var label = new TextBlock
            {
                Text = stepHours >= 24 ? tick.ToString("M/d") : tick.ToString("HH:mm"),
                Foreground = labelBrush, FontSize = 9, IsHitTestVisible = false,
            };
            Canvas.SetLeft(label, Math.Min(width - 30, x + 2));
            Canvas.SetTop(label, barHeight + 1);
            ActivityRibbonCanvas.Children.Add(label);
        }
    }

    private void RenderStateBreakdown(TrackerSummary summary)
    {
        BreakdownBar.ColumnDefinitions.Clear();
        BreakdownBar.Children.Clear();
        BreakdownLegend.Children.Clear();

        var buckets = new List<(PlayerActivityState State, TimeSpan Duration)>
        {
            (PlayerActivityState.Moving, summary.Moving),
            (PlayerActivityState.Stationary, summary.Stationary),
            (PlayerActivityState.Afk, summary.Afk),
            (PlayerActivityState.Dead, summary.Dead),
            (PlayerActivityState.Offline, summary.Offline),
        };
        if (_showUnknown)
            buckets.Add((PlayerActivityState.Unknown, summary.Unknown));
        var total = buckets.Sum(bucket => bucket.Duration.TotalSeconds);
        if (total <= 0)
        {
            BreakdownBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var placeholder = new Border { Background = new SolidColorBrush(StateColor(PlayerActivityState.Unknown)), Opacity = 0.4 };
            Grid.SetColumn(placeholder, 0);
            BreakdownBar.Children.Add(placeholder);
            BreakdownLegend.Children.Add(new TextBlock
            {
                Text = "No classified time yet.",
                Foreground = (Brush)FindResource("TextSubtle"),
                FontSize = 11,
            });
            return;
        }

        var column = 0;
        foreach (var (state, duration) in buckets)
        {
            if (duration.TotalSeconds <= 0)
                continue;
            var share = duration.TotalSeconds / total;
            BreakdownBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(duration.TotalSeconds, GridUnitType.Star) });
            var cell = new Border
            {
                Background = new SolidColorBrush(StateColor(state)),
                ToolTip = $"{StateLabel(state)} · {Format(duration)} ({share:P0})",
            };
            if (state == PlayerActivityState.Unknown)
                cell.Opacity = 0.5;
            Grid.SetColumn(cell, column++);
            BreakdownBar.Children.Add(cell);
            BreakdownLegend.Children.Add(BuildLegendChip(StateColor(state), $"{StateLabel(state)}  {share:P0}", $"({Format(duration)})"));
        }
    }

    private void PopulateInsights(ulong steamId)
    {
        if (_tracker is null)
            return;
        InsightsPanel.Children.Clear();
        var insights = _tracker.GetInsights(steamId);
        UpdateStatusChip(insights);

        if (insights.FirstSeenUtc is null)
        {
            InsightsPanel.Children.Add(new TextBlock
            {
                Text = "No observations recorded for this player yet.",
                Foreground = (Brush)FindResource("TextSubtle"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
            });
            return;
        }

        AddInsightRow("Currently", $"{StateLabel(insights.CurrentState)}"
            + (string.IsNullOrWhiteSpace(insights.CurrentLocationName) ? string.Empty : $" · {insights.CurrentLocationName}"));
        AddInsightRow("First seen", insights.FirstSeenUtc?.ToLocalTime().ToString("g") ?? "—");
        AddInsightRow("Last seen", insights.LastSeenUtc is null
            ? "—"
            : $"{insights.LastSeenUtc.Value.ToLocalTime():g} ({Ago(insights.LastSeenUtc.Value)})");
        AddInsightRow("Play sessions", insights.SessionCount.ToString("N0"));
        AddInsightRow("Favourite spot", insights.TopMonument is null
            ? "—"
            : $"{RustPlusDesk.Services.MonumentFormatter.Beautify(insights.TopMonument)} · {Format(insights.TopMonumentDuration)} over {insights.TopMonumentVisits} visit(s)");
        AddInsightRow("Peak hours", insights.PeakHourLocal is null
            ? "—"
            : $"{insights.PeakHourLocal:00}:00–{(insights.PeakHourLocal + 1) % 24:00}:00 local ({Format(insights.PeakHourActive)})");
        AddInsightRow("Longest blind gap", insights.LongestBlindGap <= TimeSpan.Zero
            ? "—"
            : $"{Format(insights.LongestBlindGap)}" + (insights.LongestBlindGapStartUtc is null ? string.Empty : $" from {insights.LongestBlindGapStartUtc.Value.ToLocalTime():t}"));
    }

    private void UpdateStatusChip(TrackerInsights insights)
    {
        if (insights.CurrentAsOfUtc is null)
        {
            StatusChipDot.Fill = new SolidColorBrush(StateColor(PlayerActivityState.Unknown));
            StatusChipText.Text = "No data";
            StatusChipSubText.Text = string.Empty;
            return;
        }

        var live = insights.IsLikelyOnline;
        StatusChipDot.Fill = new SolidColorBrush(live ? StateColor(insights.CurrentState) : StateColor(PlayerActivityState.Offline));
        StatusChipText.Text = live ? StateLabel(insights.CurrentState) : "Last seen";
        var where = string.IsNullOrWhiteSpace(insights.CurrentLocationName)
            ? (string.IsNullOrWhiteSpace(insights.CurrentGrid) ? null : insights.CurrentGrid)
            : insights.CurrentLocationName;
        StatusChipSubText.Text = live
            ? (where is null ? "live" : $"· {where}")
            : Ago(insights.CurrentAsOfUtc.Value);
    }

    private void AddInsightRow(string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = (Brush)FindResource("TextSubtle"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var valueBlock = new TextBlock
        {
            Text = value,
            Foreground = (Brush)FindResource("TextPrimary"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
        InsightsPanel.Children.Add(grid);
    }

    private void BuildStateLegend(WrapPanel panel)
    {
        panel.Children.Clear();
        var states = new List<PlayerActivityState>
        {
            PlayerActivityState.Moving, PlayerActivityState.Stationary, PlayerActivityState.Afk,
            PlayerActivityState.Dead, PlayerActivityState.Offline,
        };
        if (_showUnknown)
            states.Add(PlayerActivityState.Unknown);
        foreach (var state in states)
            panel.Children.Add(BuildLegendChip(StateColor(state), StateLabel(state), null));
    }

    private FrameworkElement BuildLegendChip(Color color, string label, string? suffix)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 4) };
        panel.Children.Add(new Border
        {
            Width = 11, Height = 11, CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });
        panel.Children.Add(new TextBlock
        {
            Text = label, FontSize = 11, Foreground = (Brush)FindResource("TextPrimary"), VerticalAlignment = VerticalAlignment.Center,
        });
        if (!string.IsNullOrWhiteSpace(suffix))
        {
            panel.Children.Add(new TextBlock
            {
                Text = suffix, FontSize = 11, Foreground = (Brush)FindResource("TextSubtle"),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0),
            });
        }
        return panel;
    }

    private static void AddCanvasCenterText(Canvas canvas, string message)
    {
        var text = new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Color.FromArgb(170, 200, 212, 220)),
            FontSize = 12,
        };
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(text, Math.Max(12, (canvas.ActualWidth - text.DesiredSize.Width) / 2));
        Canvas.SetTop(text, Math.Max(8, (canvas.ActualHeight - text.DesiredSize.Height) / 2));
        canvas.Children.Add(text);
    }

    private static string Ago(DateTime utc)
    {
        var delta = DateTime.UtcNow - utc.ToUniversalTime();
        if (delta < TimeSpan.Zero)
            delta = TimeSpan.Zero;
        if (delta.TotalMinutes < 1)
            return "just now";
        if (delta.TotalHours < 1)
            return $"{(int)delta.TotalMinutes}m ago";
        if (delta.TotalDays < 1)
            return $"{(int)delta.TotalHours}h {delta.Minutes}m ago";
        return $"{(int)delta.TotalDays}d {delta.Hours}h ago";
    }

    internal static Color StateColor(PlayerActivityState state) => state switch
    {
        PlayerActivityState.Moving => Color.FromRgb(0x5B, 0xE7, 0xA9),
        PlayerActivityState.Stationary => Color.FromRgb(0x62, 0xD6, 0xFF),
        PlayerActivityState.Afk => Color.FromRgb(0xC9, 0xA6, 0xFF),
        PlayerActivityState.Dead => Color.FromRgb(0xFF, 0x6B, 0x57),
        PlayerActivityState.Offline => Color.FromRgb(0x5B, 0x6B, 0x78),
        _ => Color.FromRgb(0x33, 0x41, 0x4D),
    };

    internal static string StateLabel(PlayerActivityState state) => state switch
    {
        PlayerActivityState.Moving => "Moving",
        PlayerActivityState.Stationary => "Stationary",
        PlayerActivityState.Afk => "AFK",
        PlayerActivityState.Dead => "Dead",
        PlayerActivityState.Offline => "Offline",
        _ => "Unknown",
    };

    private static bool TryGetMapTransforms(Grid viewport, out ScaleTransform scale, out TranslateTransform translate)
    {
        var surface = viewport.Children.OfType<Grid>().FirstOrDefault();
        if (surface?.RenderTransform is TransformGroup group && group.Children.Count >= 2 &&
            group.Children[0] is ScaleTransform foundScale && group.Children[1] is TranslateTransform foundTranslate)
        {
            scale = foundScale;
            translate = foundTranslate;
            return true;
        }
        scale = null!;
        translate = null!;
        return false;
    }

    private void MapViewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Grid viewport || !TryGetMapTransforms(viewport, out var scale, out var translate))
            return;
        var next = Math.Clamp(scale.ScaleX * (e.Delta > 0 ? 1.15 : 1 / 1.15), 1, 8);
        var pointer = e.GetPosition(viewport);
        var ratio = next / scale.ScaleX;
        translate.X = pointer.X - (pointer.X - translate.X) * ratio;
        translate.Y = pointer.Y - (pointer.Y - translate.Y) * ratio;
        scale.ScaleX = scale.ScaleY = next;
        if (next == 1)
            translate.X = translate.Y = 0;
        e.Handled = true;
    }

    private void MapViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Grid viewport || !TryGetMapTransforms(viewport, out var scale, out var translate))
            return;
        if (e.ClickCount == 2)
        {
            scale.ScaleX = scale.ScaleY = 1;
            translate.X = translate.Y = 0;
            return;
        }
        _dragViewport = viewport;
        _dragTranslate = translate;
        _dragStart = e.GetPosition(viewport);
        _dragOrigin = new Point(translate.X, translate.Y);
        viewport.CaptureMouse();
    }

    private void MapViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragViewport is null || _dragTranslate is null || !_dragViewport.IsMouseCaptured)
            return;
        var delta = e.GetPosition(_dragViewport) - _dragStart;
        _dragTranslate.X = _dragOrigin.X + delta.X;
        _dragTranslate.Y = _dragOrigin.Y + delta.Y;
    }

    private void MapViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragViewport?.ReleaseMouseCapture();
        _dragViewport = null;
        _dragTranslate = null;
    }

    private void TrackerTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, TrackerTabs) || _tracker is null)
            return;

        if (ReferenceEquals(TrackerTabs.SelectedItem, CloudTab))
        {
            _ = LoadCloudArchivesAsync();
        }
        else
        {
            RefreshSelectedPlayer();
        }
    }

    private void CloudRestoreTab_Click(object sender, RoutedEventArgs e)
    {
        TrackerTabs.SelectedItem = CloudTab;
        _ = LoadCloudArchivesAsync();
    }

    private async void RefreshCloud_Click(object sender, RoutedEventArgs e) => await LoadCloudArchivesAsync();

    private async Task LoadCloudArchivesAsync()
    {
        if (_tracker is null || !_tracker.Capabilities.CanUseCloudSync)
        {
            CloudStatusText.Text = "Cloud restore is available on premium plans only.";
            return;
        }

        CloudStatusText.Text = "Loading cloud archives…";
        try
        {
            var archives = await _tracker.GetCloudArchivesAsync();
            var items = archives.Select(archive => new CloudArchiveItem(archive)).ToArray();
            CloudArchiveSelector.ItemsSource = items;
            CloudArchiveSelector.SelectedIndex = items.Length > 0 ? 0 : -1;
            CloudStatusText.Text = items.Length == 0 ? "No cloud archives found." : $"{items.Length} archive(s) available.";
        }
        catch (Exception ex)
        {
            CloudStatusText.Text = $"Cloud archive load failed: {ex.Message}";
        }
    }

    private void CloudArchive_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_tracker is null || CloudArchiveSelector.SelectedItem is not CloudArchiveItem item)
        {
            CloudArchiveDetailsPanel.Visibility = Visibility.Collapsed;
            CloudArchiveEmptyPanel.Visibility = Visibility.Visible;
            RestoreCloudButton.IsEnabled = false;
            return;
        }

        CloudArchiveDetailsPanel.Visibility = Visibility.Visible;
        CloudArchiveEmptyPanel.Visibility = Visibility.Collapsed;
        ArchiveServerNameText.Text = item.ServerName;
        ArchiveWipeDateText.Text = item.WipeDateFormatted;
        ArchiveObservedRangeText.Text = item.TrackingWindowFormatted;
        ArchivePlayersText.Text = item.PlayerCountFormatted;
        ArchiveStorageText.Text = item.StoredSizeFormatted;
        RestoreCloudButton.IsEnabled = _tracker.Capabilities.CanUseCloudSync;
    }

    private async void RestoreCloud_Click(object sender, RoutedEventArgs e)
    {
        if (_tracker is null || CloudArchiveSelector.SelectedItem is not CloudArchiveItem item || !_tracker.Capabilities.CanUseCloudSync)
            return;

        RestoreCloudButton.IsEnabled = false;
        CloudStatusText.Text = "Restoring archive day by day…";
        try
        {
            var result = await _tracker.RestoreCloudArchiveAsync(item.Archive.Id);
            CloudStatusText.Text = result.IsCurrentWipe
                ? $"Restored {result.Observations:N0} observations across {result.Days} day(s). Current views refreshed."
                : $"Restored {result.Observations:N0} observations across {result.Days} day(s). Reconnect to that server/wipe to view the imported history.";
            Refresh();
        }
        catch (Exception ex)
        {
            CloudStatusText.Text = $"Restore failed: {ex.Message}";
        }
        finally
        {
            RestoreCloudButton.IsEnabled = _tracker.Capabilities.CanUseCloudSync;
        }
    }

    private void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        if (_tracker is null || !CanExport())
            return;
        var dialog = new SaveFileDialog { Filter = "Tracker JSON (*.json)|*.json", FileName = ExportFileName("json") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        var steamId = SelectedPlayerId(PlayerSelector) ?? _ownSteamId;
        var document = BuildExportDocument(steamId);
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        ExportStatusText.Text = $"Exported {_points.Count:N0} observations to {System.IO.Path.GetFileName(dialog.FileName)}.";
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_tracker is null || !CanExport())
            return;
        var dialog = new SaveFileDialog { Filter = "Tracker CSV (*.csv)|*.csv", FileName = ExportFileName("csv") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        var steamId = SelectedPlayerId(PlayerSelector) ?? _ownSteamId;
        var csv = new StringBuilder("timestamp_utc,steam_id,x,y,state,location_type,location_name,grid,event,session_id\n");
        foreach (var point in ToPoints(_tracker.GetObservations(steamId)))
        {
            csv.Append(string.Join(',',
                Csv(point.TimestampUtc.ToString("O")),
                steamId,
                point.X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                point.Y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                Csv(point.State.ToString().ToLowerInvariant()),
                Csv(point.LocationType.ToString().ToLowerInvariant()),
                Csv(point.LocationName),
                Csv(point.Grid),
                Csv(point.Event),
                Csv(point.SessionId)));
            csv.Append('\n');
        }
        File.WriteAllText(dialog.FileName, csv.ToString(), Encoding.UTF8);
        ExportStatusText.Text = $"Exported {_points.Count:N0} observations to {System.IO.Path.GetFileName(dialog.FileName)}.";
    }

    private bool CanExport()
    {
        if (_tracker is not null && _tracker.Capabilities.CanExport)
            return true;
        ExportStatusText.Text = "Export is available on premium plans only.";
        return false;
    }

    private object BuildExportDocument(ulong steamId)
    {
        var summary = _tracker!.GetSummary(steamId);
        return new
        {
            schema_version = 1,
            generated_at = DateTime.UtcNow.ToString("O"),
            player_steam_id = steamId.ToString(),
            player_name = _tracker.GetPlayerName(steamId),
            wipe_key = _tracker.CurrentWipeKey,
            summary = new
            {
                coverage_seconds = (long)summary.Coverage.TotalSeconds,
                unknown_seconds = (long)summary.Unknown.TotalSeconds,
                moving_seconds = (long)summary.Moving.TotalSeconds,
                stationary_seconds = (long)summary.Stationary.TotalSeconds,
                afk_seconds = (long)summary.Afk.TotalSeconds,
                dead_seconds = (long)summary.Dead.TotalSeconds,
                offline_seconds = (long)summary.Offline.TotalSeconds,
                estimated_distance = summary.EstimatedDistance,
                deaths = summary.Deaths,
            },
            observations = ToPoints(_tracker.GetObservations(steamId)),
            segments = _tracker.GetSegments(steamId),
            monument_visits = summary.MonumentVisits,
        };
    }

    private string ExportFileName(string extension)
        => $"player-wipe-{(SelectedPlayerId(PlayerSelector) ?? _ownSteamId)}-{DateTime.Now:yyyyMMdd-HHmm}.{extension}";

    private static string Csv(string? value)
    {
        var text = value ?? string.Empty;
        return text.Contains(',') || text.Contains('"') || text.Contains('\n')
            ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : text;
    }

    private string ShortName(ulong steamId)
    {
        var name = _tracker!.GetPlayerName(steamId);
        return name.Length > 16 ? name[..16] : name;
    }

    private static IReadOnlyList<TrackerPoint> ToPoints(IReadOnlyList<PlayerObservation> observations)
    {
        var points = new List<TrackerPoint>();
        PlayerObservation? previous = null;
        foreach (var observation in observations)
        {
            var state = PlayerWipeTrackerEngine.Classify(observation);
            if (previous is not null && observation.X is not null && observation.Y is not null && previous.X is not null && previous.Y is not null)
            {
                var distance = Math.Sqrt(Math.Pow(observation.X.Value - previous.X.Value, 2) + Math.Pow(observation.Y.Value - previous.Y.Value, 2));
                state = PlayerWipeTrackerEngine.Classify(observation, distance);
            }
            var eventName = previous is not null && !previous.Dead && observation.Dead ? "death" :
                previous is not null && previous.Dead && !observation.Dead ? "respawn" : null;
            if (observation.X is not null && observation.Y is not null)
            {
                points.Add(new TrackerPoint(
                    observation.TimestampUtc,
                    observation.X.Value,
                    observation.Y.Value,
                    state,
                    observation.LocationType,
                    observation.LocationName,
                    observation.Grid,
                    eventName,
                    observation.SessionId));
            }
            previous = observation;
        }
        return points;
    }

    private static double Distance(IReadOnlyList<TrackerPoint> points)
    {
        var distance = 0d;
        for (var i = 1; i < points.Count; i++)
        {
            if (points[i].SessionId != points[i - 1].SessionId)
                continue;
            distance += Math.Sqrt(Math.Pow(points[i].X - points[i - 1].X, 2) + Math.Pow(points[i].Y - points[i - 1].Y, 2));
        }
        return distance;
    }

    private bool TryGetMapProjection(Canvas canvas, out TrackerMapProjection projection)
    {
        var imageWidth = _wipeMapImage?.Width ?? 0;
        var imageHeight = _wipeMapImage?.Height ?? 0;
        var worldRect = _worldRectPixels.Width > 0 && _worldRectPixels.Height > 0
            ? _worldRectPixels
            : new Rect(0, 0, imageWidth, imageHeight);
        projection = new TrackerMapProjection(
            canvas.ActualWidth,
            canvas.ActualHeight,
            imageWidth,
            imageHeight,
            worldRect.X,
            worldRect.Y,
            worldRect.Width,
            worldRect.Height,
            _worldSize);
        return projection.IsValid;
    }

    private static Point Project(TrackerMapProjection projection, double worldX, double worldY)
    {
        var point = projection.Project(worldX, worldY);
        return new Point(point.X, point.Y);
    }

    private void DrawWipeGrid(Canvas canvas, TrackerMapProjection projection)
    {
        const double gridSize = 150;
        var cells = Math.Max(1, (int)Math.Ceiling(_worldSize / gridSize));
        var gridBrush = new SolidColorBrush(Color.FromArgb(100, 225, 240, 245));
        var labelBrush = new SolidColorBrush(Color.FromArgb(210, 240, 248, 250));

        for (var i = 0; i <= cells; i++)
        {
            var world = Math.Min(_worldSize, i * gridSize);
            var verticalTop = Project(projection, world, _worldSize);
            var verticalBottom = Project(projection, world, 0);
            var horizontalLeft = Project(projection, 0, _worldSize - world);
            var horizontalRight = Project(projection, _worldSize, _worldSize - world);
            canvas.Children.Add(new Line { X1 = verticalTop.X, X2 = verticalBottom.X, Y1 = verticalTop.Y, Y2 = verticalBottom.Y, Stroke = gridBrush, StrokeThickness = 0.7, IsHitTestVisible = false });
            canvas.Children.Add(new Line { X1 = horizontalLeft.X, X2 = horizontalRight.X, Y1 = horizontalLeft.Y, Y2 = horizontalRight.Y, Stroke = gridBrush, StrokeThickness = 0.7, IsHitTestVisible = false });
        }

        var firstCell = Project(projection, 0, _worldSize);
        var secondCell = Project(projection, Math.Min(gridSize, _worldSize), Math.Max(0, _worldSize - gridSize));
        var displayedCellSize = Math.Min(Math.Abs(secondCell.X - firstCell.X), Math.Abs(secondCell.Y - firstCell.Y));
        var labelStep = displayedCellSize < 20 ? 3 : displayedCellSize < 30 ? 2 : 1;
        for (var column = 0; column < cells; column += labelStep)
        {
            for (var row = 0; row < cells; row += labelStep)
            {
                var position = Project(
                    projection,
                    Math.Min(_worldSize, column * gridSize + 5),
                    Math.Max(0, _worldSize - row * gridSize - 5));
                var label = new TextBlock
                {
                    Text = $"{ColumnLabel(column)}{row}",
                    Foreground = labelBrush,
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(label, position.X + 2);
                Canvas.SetTop(label, position.Y + 1);
                Panel.SetZIndex(label, 1);
                canvas.Children.Add(label);
            }
        }
    }

    /// <summary>Overlays the wipe map's monuments (dot + name) onto a preview canvas.</summary>
    private void DrawMonuments(Canvas canvas, TrackerMapProjection projection)
    {
        if (!_showMonuments || _monuments.Count == 0)
            return;

        var dotBrush = new SolidColorBrush(Color.FromRgb(255, 138, 76));
        var textBrush = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255));

        foreach (var monument in _monuments)
        {
            var position = Project(projection, monument.X, monument.Y);

            var dot = new Ellipse
            {
                Width = 5,
                Height = 5,
                Fill = dotBrush,
                Stroke = Brushes.Black,
                StrokeThickness = 0.8,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(dot, position.X - 2.5);
            Canvas.SetTop(dot, position.Y - 2.5);
            Panel.SetZIndex(dot, 3);
            canvas.Children.Add(dot);

            var label = new TextBlock
            {
                Text = RustPlusDesk.Services.MonumentFormatter.Beautify(monument.Name),
                Foreground = textBrush,
                FontSize = 8.5,
                FontWeight = FontWeights.SemiBold,
                IsHitTestVisible = false,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 3,
                    ShadowDepth = 0,
                    Opacity = 0.95,
                },
            };
            Canvas.SetLeft(label, position.X + 4);
            Canvas.SetTop(label, position.Y - 6);
            Panel.SetZIndex(label, 3);
            canvas.Children.Add(label);
        }
    }

    private static string ColumnLabel(int index)
    {
        var label = string.Empty;
        for (index++; index > 0; index /= 26)
        {
            index--;
            label = (char)('A' + index % 26) + label;
        }
        return label;
    }

    private static void DrawTrack(Canvas canvas, IReadOnlyList<TrackerPoint> points, Func<TrackerPoint, Point> mapPoint, Color color, double thickness, double opacity, bool dashed = false)
    {
        if (points.Count < 2)
            return;
        var batch = new List<Point>();
        TrackerPoint? previous = null;
        foreach (var point in points)
        {
            if (previous is not null && (point.SessionId != previous.SessionId || point.TimestampUtc - previous.TimestampUtc > TimeSpan.FromSeconds(PlayerWipeTrackerEngine.MaxContinuityGapSeconds)))
            {
                AddPolyline(canvas, batch, color, thickness, opacity, dashed);
                batch.Clear();
            }
            batch.Add(mapPoint(point));
            previous = point;
        }
        AddPolyline(canvas, batch, color, thickness, opacity, dashed);
    }

    private static void AddPolyline(Canvas canvas, IReadOnlyList<Point> points, Color color, double thickness, double opacity, bool dashed)
    {
        if (points.Count < 2)
            return;
        var line = new Polyline
        {
            Points = new PointCollection(points),
            Stroke = new SolidColorBrush(color),
            StrokeThickness = thickness,
            Opacity = opacity,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
        if (dashed)
            line.StrokeDashArray = new DoubleCollection { 3, 4 };
        Panel.SetZIndex(line, 3);
        canvas.Children.Add(line);
    }

    private static void DrawEventMarkers(Canvas canvas, IReadOnlyList<TrackerPoint> points, Func<TrackerPoint, Point> mapPoint, Color color)
    {
        foreach (var point in points.Where(point => point.Event == "death"))
        {
            var marker = new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(color), Stroke = Brushes.White, StrokeThickness = 1, IsHitTestVisible = false };
            var position = mapPoint(point);
            Canvas.SetLeft(marker, position.X - marker.Width / 2);
            Canvas.SetTop(marker, position.Y - marker.Height / 2);
            Panel.SetZIndex(marker, 5);
            canvas.Children.Add(marker);
        }
    }

    private static Color HeatColor(double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        return ratio < 0.5
            ? Blend(Color.FromRgb(22, 63, 106), Color.FromRgb(39, 200, 178), ratio * 2)
            : Blend(Color.FromRgb(39, 200, 178), Color.FromRgb(255, 90, 54), (ratio - 0.5) * 2);
    }

    private static Color Blend(Color from, Color to, double amount)
        => Color.FromRgb(
            (byte)(from.R + (to.R - from.R) * amount),
            (byte)(from.G + (to.G - from.G) * amount),
            (byte)(from.B + (to.B - from.B) * amount));

    private static void AddEmptyState(Canvas canvas, string message)
    {
        var text = new TextBlock { Text = message, Foreground = new SolidColorBrush(Color.FromArgb(180, 220, 230, 235)), FontSize = 14 };
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(text, Math.Max(16, (canvas.ActualWidth - text.DesiredSize.Width) / 2));
        Canvas.SetTop(text, Math.Max(16, (canvas.ActualHeight - text.DesiredSize.Height) / 2));
        canvas.Children.Add(text);
    }

    private static string Format(TimeSpan value)
        => value.TotalHours >= 1 ? $"{(int)value.TotalHours}h {value.Minutes}m" : $"{value.Minutes}m {value.Seconds}s";

    private static string FormatBytes(long bytes)
        => bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:N1} MB" : $"{bytes / 1024d:N1} KB";

    private sealed record PlayerItem(ulong SteamId, string Name)
    {
        public override string ToString() => Name == SteamId.ToString() ? SteamId.ToString() : $"{Name} · {SteamId}";
    }

    private sealed class CloudArchiveItem
    {
        public CloudArchiveItem(CloudArchiveSummary archive) => Archive = archive;
        public CloudArchiveSummary Archive { get; }
        public string ServerName => string.IsNullOrWhiteSpace(Archive.ServerName) ? "Unknown Server" : Archive.ServerName;
        public string WipeDateShortFormatted => Archive.WipeStartedAtUtc?.ToLocalTime().ToString("MMM d, yyyy") ?? "Unknown wipe";
        public string WipeDateFormatted => Archive.WipeStartedAtUtc?.ToLocalTime().ToString("MMM d, yyyy · h:mm tt") ?? "Unknown wipe date";
        public string TrackingWindowFormatted
        {
            get
            {
                if (!Archive.FirstObservedAtUtc.HasValue || !Archive.LastObservedAtUtc.HasValue)
                    return "No observations recorded";

                var first = Archive.FirstObservedAtUtc.Value.ToLocalTime();
                var last = Archive.LastObservedAtUtc.Value.ToLocalTime();
                var duration = last - first;

                if (duration < TimeSpan.FromSeconds(30))
                    return $"{first:MMM d, yyyy · h:mm tt} (snapshot)";

                if (first.Date == last.Date)
                    return $"{first:MMM d, yyyy} · {first:h:mm tt} → {last:h:mm tt} ({FormatDuration(duration)})";

                return $"{first:MMM d, yyyy · h:mm tt} → {last:MMM d, yyyy · h:mm tt} ({FormatDuration(duration)})";
            }
        }
        public string PlayerCountFormatted => $"{Archive.PlayerCount ?? Archive.Players.Count} player(s)";
        public string StoredSizeFormatted => FormatBytes(Archive.StoredBytes ?? 0);
        public string Details => $"{Archive.ServerName}\nWipe: {WipeDateFormatted}\nObserved: {TrackingWindowFormatted}\nPlayers: {PlayerCountFormatted} · Stored: {StoredSizeFormatted}";
        public override string ToString() => $"{Archive.ServerName} · {WipeDateShortFormatted} · {PlayerCountFormatted}";

        private static string FormatDuration(TimeSpan span)
        {
            if (span.TotalDays >= 1)
                return $"{span.TotalDays:0.#}d active";
            if (span.TotalHours >= 1)
                return $"{(int)span.TotalHours}h {span.Minutes}m active";
            if (span.TotalMinutes >= 1)
                return $"{Math.Max(1, (int)span.TotalMinutes)}m active";
            return $"{Math.Max(1, (int)span.TotalSeconds)}s active";
        }
    }

    private sealed class StoredWipeItem
    {
        public StoredWipeItem(StoredWipeSummary summary, bool isLive)
        {
            Summary = summary;
            IsLive = isLive;
        }

        public StoredWipeSummary Summary { get; }
        public bool IsLive { get; }
        public string ServerName => !string.IsNullOrWhiteSpace(Summary.ServerName) && !PlayerWipeTrackerStore.IsRawServerKey(Summary.ServerName)
            ? Summary.ServerName
            : PlayerWipeTrackerStore.ResolveServerName(Summary.ServerKey);
        public string WipeDateFormatted
        {
            get
            {
                if (Summary.WipeStartedAtUtc.HasValue)
                    return Summary.WipeStartedAtUtc.Value.ToLocalTime().ToString("MMM d, yyyy");
                if (Summary.LastObservedAtUtc.HasValue)
                    return Summary.LastObservedAtUtc.Value.ToLocalTime().ToString("MMM d, yyyy");
                return "Saved Wipe";
            }
        }
        public string PlayerTracksFormatted => $"{Summary.PlayerCount} player(s)";
        public string DisplayText => IsLive
            ? $"● {ServerName} · {WipeDateFormatted} ({PlayerTracksFormatted} · Live)"
            : $"{ServerName} · {WipeDateFormatted} ({PlayerTracksFormatted})";

        public override string ToString() => DisplayText;
    }
}
