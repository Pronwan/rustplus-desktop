using RustPlusDesk.Models;
using RustPlusDesk.Services.Map;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RustPlusDesk.Views;

/// <summary>
/// Named routes: a path you can call something, colour, hide, and measure.
///
/// Deliberately not the arrow-path tool. That one decorates a stroke; this one is an object with
/// a name and a length, and the difference shows up the moment you want two of them on the same
/// map and need to tell which is which.
///
/// A route owns its geometry rather than living as strokes, because everything that makes it a
/// route — where it starts, where it ends, whether it closes — is a property of the whole path
/// and has nowhere to live on a pile of separate lines.
/// </summary>
public partial class MainWindow
{
    /// <summary>How close the end has to come back to the start before the route becomes a lap.</summary>
    private const double RouteCloseRadiusPx = 22.0;

    private readonly ObservableCollection<MapRouteItem> _routeItems = new();

    private bool _routeMode;

    private string? _activeRouteId;

    /// <summary>The lines and dots currently on the map, by route id, so they can be replaced.</summary>
    private readonly Dictionary<string, List<FrameworkElement>> _routeVisuals = new(StringComparer.Ordinal);

    /// <summary>What the rest of the overlay was showing before route mode hid it.</summary>
    private readonly List<FrameworkElement> _hiddenByRouteMode = new();

    /// <summary>
    /// True only while there is a route to draw into. Without one a stroke stays a stroke:
    /// swallowing it because a panel happens to be open is how a drawing tool loses somebody
    /// their work.
    /// </summary>
    internal bool IsRouteModeActive => _routeMode && _activeRouteId != null;

    // ── Entering and leaving ────────────────────────────────────────────────

    private void BtnToggleRoutes_Click(object sender, RoutedEventArgs e)
    {
        if (_routeMode) ExitRouteMode();
        else EnterRouteMode();
    }

    private void BtnRoutesClose_Click(object sender, RoutedEventArgs e) => ExitRouteMode();

    /// <summary>
    /// Route mode: the routes stay, everything else on the overlay steps aside.
    ///
    /// Hidden rather than removed. A route is drawn over the map, and forty icons and a dozen
    /// strokes between it and the ground make it impossible to read — but they are somebody's
    /// work, and they come back untouched on the way out.
    /// </summary>
    private void EnterRouteMode()
    {
        if (_routeMode) return;
        _routeMode = true;

        if (RoutesList != null) RoutesList.ItemsSource = _routeItems;

        HideNonRouteOverlay();
        RedrawAllRoutes();

        if (RoutesPanel != null) RoutesPanel.Visibility = Visibility.Visible;
        if (LayersPanel != null) LayersPanel.Visibility = Visibility.Collapsed;

        UpdateRoutesPanelState();
    }

    private void ExitRouteMode()
    {
        if (!_routeMode) return;
        _routeMode = false;

        if (RoutesPanel != null) RoutesPanel.Visibility = Visibility.Collapsed;
        if (RouteNameRow != null) RouteNameRow.Visibility = Visibility.Collapsed;

        RestoreNonRouteOverlay();

        // The visible routes stay on the map. Leaving the panel is not the same as putting the
        // routes away, and somebody who ticked two of them wants to see them while they play.
        RedrawAllRoutes();
    }

    private void HideNonRouteOverlay()
    {
        _hiddenByRouteMode.Clear();
        if (Overlay == null) return;

        foreach (UIElement child in Overlay.Children)
        {
            if (child is not FrameworkElement fe) continue;
            if (IsRouteVisual(fe)) continue;
            if (fe.Visibility != Visibility.Visible) continue;

            fe.Visibility = Visibility.Collapsed;
            _hiddenByRouteMode.Add(fe);
        }
    }

    private void RestoreNonRouteOverlay()
    {
        foreach (FrameworkElement fe in _hiddenByRouteMode)
            fe.Visibility = Visibility.Visible;

        _hiddenByRouteMode.Clear();
    }

    private bool IsRouteVisual(FrameworkElement fe)
        => _routeVisuals.Values.Any(list => list.Contains(fe));

    // ── The list ────────────────────────────────────────────────────────────

    private void BtnRouteAdd_Click(object sender, RoutedEventArgs e)
    {
        if (RouteNameRow == null || TxtRouteName == null) return;

        RouteNameRow.Visibility = Visibility.Visible;
        TxtRouteName.Text = "";
        TxtRouteName.Focus();

        UpdateRoutesPanelState();
    }

    private void TxtRouteName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (RouteNameRow != null) RouteNameRow.Visibility = Visibility.Collapsed;
            UpdateRoutesPanelState();
            return;
        }

        if (e.Key != Key.Enter) return;
        e.Handled = true;

        string name = (TxtRouteName?.Text ?? "").Trim();
        if (name.Length == 0) return;

        var item = new MapRouteItem
        {
            Id = "route-" + Guid.NewGuid().ToString("N"),
            Name = name,
            // The colour the pen is set to, so the line and its legend entry match without
            // anybody having to pick twice.
            Color = _drawColor,
            Thickness = Math.Max(_drawThickness, 2.0),
        };

        _routeItems.Add(item);
        _activeRouteId = item.Id;

        if (RouteNameRow != null) RouteNameRow.Visibility = Visibility.Collapsed;

        RefreshRouteRowStates();
        UpdateRoutesPanelState();
        SaveOwnOverlayToJson();
    }

    private void RouteRow_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not MapRouteItem item) return;

        // Clicking the row you are already drawing puts the pen down rather than doing nothing.
        _activeRouteId = _activeRouteId == item.Id ? null : item.Id;

        RefreshRouteRowStates();
        UpdateRoutesPanelState();
    }

    private void BtnRouteVisibility_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not MapRouteItem item) return;

        item.Visible = !item.Visible;
        RedrawRoute(item);
        SaveOwnOverlayToJson();
    }

    private void BtnRouteDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not MapRouteItem item) return;

        ClearRouteVisuals(item.Id);
        _routeItems.Remove(item);

        if (_activeRouteId == item.Id) _activeRouteId = null;

        UpdateRoutesPanelState();
        SaveOwnOverlayToJson();
    }

    private void RefreshRouteRowStates()
    {
        foreach (MapRouteItem item in _routeItems)
            item.IsActive = item.Id == _activeRouteId;
    }

    private void UpdateRoutesPanelState()
    {
        if (RoutesEmptyNotice != null)
            RoutesEmptyNotice.Visibility = _routeItems.Count == 0 && RouteNameRow?.Visibility != Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (RoutesDrawHint != null)
            RoutesDrawHint.Visibility = _activeRouteId != null && RouteNameRow?.Visibility != Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    // ── Drawing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a finished stroke to the route being drawn.
    ///
    /// The first stroke lays the route down: its first point is the start, its last the end.
    /// Every stroke after that continues from whichever end it began nearest to, so drawing on
    /// from the end moves the end and drawing back from the start extends the other way. When the
    /// end lands back on the start the route closes and becomes a lap.
    /// </summary>
    private void AppendStrokeToActiveRoute(Polyline stroke)
    {
        List<Point> pts = stroke.Points.ToList();

        // The stroke itself never stays: the route redraws from its own geometry.
        RemoveOwnElement(stroke);
        Overlay?.Children.Remove(stroke);

        MapRouteItem? item = _routeItems.FirstOrDefault(r => r.Id == _activeRouteId);
        if (item == null || pts.Count < 2) return;

        if (item.Points.Count == 0)
        {
            item.Points.AddRange(pts);
        }
        else if (item.Closed)
        {
            // A lap has no loose end to draw on from. Saying so beats silently ignoring the
            // stroke or, worse, breaking the loop open again.
            return;
        }
        else
        {
            Point first = item.Points[0];
            Point last = item.Points[^1];

            double toEnd = Distance(pts[0], last);
            double toStart = Distance(pts[0], first);

            if (toStart < toEnd)
            {
                // Drawn backwards from the start: the new points lead into it.
                pts.Reverse();
                item.Points.InsertRange(0, pts);
            }
            else
            {
                item.Points.AddRange(pts);
            }
        }

        // Back where it began: the route is a lap, and the two ends become one point.
        if (item.Points.Count > 2 && Distance(item.Points[0], item.Points[^1]) <= RouteCloseRadiusPx)
        {
            item.Points[^1] = item.Points[0];
            item.Closed = true;
        }

        RedrawRoute(item);
        SaveOwnOverlayToJson();
    }

    private static double Distance(Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    // ── Rendering ───────────────────────────────────────────────────────────

    private void RedrawAllRoutes()
    {
        foreach (MapRouteItem item in _routeItems)
            RedrawRoute(item);
    }

    private void ClearRouteVisuals(string routeId)
    {
        if (!_routeVisuals.TryGetValue(routeId, out List<FrameworkElement>? list)) return;

        foreach (FrameworkElement fe in list)
            Overlay?.Children.Remove(fe);

        _routeVisuals.Remove(routeId);
    }

    private void RedrawRoute(MapRouteItem item)
    {
        ClearRouteVisuals(item.Id);
        item.Recompute(MetresPerOverlayPixel());

        if (Overlay == null || !item.Visible || item.Points.Count < 2) return;

        var brush = new SolidColorBrush(item.Color);
        var created = new List<FrameworkElement>();

        var line = new Polyline
        {
            Stroke = brush,
            StrokeThickness = item.Thickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };

        foreach (Point p in item.Points) line.Points.Add(p);

        Overlay.Children.Add(line);
        created.Add(line);

        // The start is filled and the end is hollow, so a route reads in one direction at a
        // glance. A lap has neither: there is nowhere it starts that is not also where it ends.
        if (!item.Closed)
        {
            created.Add(AddRouteDot(item.Points[0], brush, filled: true, item.Thickness));
            created.Add(AddRouteDot(item.Points[^1], brush, filled: false, item.Thickness));
        }
        else
        {
            created.Add(AddRouteDot(item.Points[0], brush, filled: true, item.Thickness));
        }

        _routeVisuals[item.Id] = created;
    }

    private Ellipse AddRouteDot(Point at, Brush brush, bool filled, double thickness)
    {
        double size = Math.Clamp(thickness * 3.2, 9.0, 18.0);

        var dot = new Ellipse
        {
            Width = size,
            Height = size,
            Stroke = brush,
            StrokeThickness = Math.Max(thickness * 0.8, 1.5),
            Fill = filled ? brush : Brushes.Transparent,
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(dot, at.X - (size / 2));
        Canvas.SetTop(dot, at.Y - (size / 2));

        Overlay!.Children.Add(dot);
        return dot;
    }

    /// <summary>
    /// How many metres one overlay pixel covers on the map currently shown.
    ///
    /// Read from what the map already knows rather than assumed from grid squares: the world size
    /// and the pixel rectangle it occupies are both exact, and the deep sea map is the same sum
    /// with its own span.
    /// </summary>
    private double MetresPerOverlayPixel()
    {
        if (_worldRectPx.Width <= 0) return 0;

        double span = _isShowingDeepSeaMap ? DeepSeaCells * DeepSeaCellSize : _worldSizeS;

        return span <= 0 ? 0 : span / _worldRectPx.Width;
    }

    // ── Saving and loading ──────────────────────────────────────────────────

    private List<SavedRoute> BuildSavedRoutes()
        => _routeItems.Select(item => new SavedRoute
        {
            Id = item.Id,
            Name = item.Name,
            Color = item.Color.ToString(),
            Thickness = item.Thickness,
            Points = item.Points.ToList(),
            Closed = item.Closed,
            Visible = item.Visible,
        }).ToList();

    private void ApplySavedRoutes(List<SavedRoute>? routes)
    {
        foreach (string id in _routeVisuals.Keys.ToList())
            ClearRouteVisuals(id);

        _routeItems.Clear();
        _activeRouteId = null;

        if (routes == null) return;

        foreach (SavedRoute saved in routes)
        {
            var item = new MapRouteItem
            {
                Id = string.IsNullOrWhiteSpace(saved.Id) ? "route-" + Guid.NewGuid().ToString("N") : saved.Id,
                Name = saved.Name,
                Thickness = saved.Thickness,
                Closed = saved.Closed,
                Visible = saved.Visible,
                Color = ParseRouteColor(saved.Color),
            };

            item.Points.AddRange(saved.Points);
            _routeItems.Add(item);
        }

        RedrawAllRoutes();
        UpdateRoutesPanelState();
    }

    private static Color ParseRouteColor(string? value)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                ColorConverter.ConvertFromString(value) is Color parsed)
                return parsed;
        }
        catch
        {
            // A colour we cannot read is not worth losing the route over.
        }

        return Color.FromRgb(0x3F, 0xD7, 0xFF);
    }
}

/// <summary>One route as the legend draws it.</summary>
public sealed class MapRouteItem : System.ComponentModel.INotifyPropertyChanged
{
    public string Id { get; init; } = "";

    private string _name = "";

    public string Name
    {
        get => _name;
        set { _name = value; Raise(nameof(Name)); }
    }

    public Color Color { get; set; } = Color.FromRgb(0x3F, 0xD7, 0xFF);

    public double Thickness { get; set; } = 3.0;

    public List<Point> Points { get; } = new();

    public bool Closed { get; set; }

    private bool _visible = true;

    public bool Visible
    {
        get => _visible;
        set { _visible = value; Raise(nameof(Visible)); Raise(nameof(EyeGlyph)); Raise(nameof(EyeOpacity)); }
    }

    private bool _isActive;

    /// <summary>The one being drawn into. Only ever one, and it is the one shown in bold.</summary>
    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; Raise(nameof(IsActive)); Raise(nameof(NameWeight)); Raise(nameof(RowBackground)); }
    }

    private string _measurement = "";

    /// <summary>Length and running time, already formatted.</summary>
    public string Measurement
    {
        get => _measurement;
        private set { _measurement = value; Raise(nameof(Measurement)); }
    }

    public Brush Swatch => new SolidColorBrush(Color);

    public FontWeight NameWeight => IsActive ? FontWeights.Bold : FontWeights.Normal;

    public Brush RowBackground => new SolidColorBrush(
        IsActive ? Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF));

    /// <summary>Segoe MDL2: an open eye, or a struck-through one.</summary>
    public string EyeGlyph => Visible ? "\uE7B3" : "\uED1A";

    public double EyeOpacity => Visible ? 1.0 : 0.45;

    /// <summary>
    /// Recomputes the length and the time it takes to run it.
    ///
    /// Called whenever the geometry changes, and again whenever the map does — the same route is
    /// worth a different number of metres on a 3000 map than on a 4500 one.
    /// </summary>
    public void Recompute(double metresPerPixel)
    {
        if (Points.Count < 2 || metresPerPixel <= 0)
        {
            Measurement = "";
            return;
        }

        double metres = RouteMeasure.PathLength(Points) * metresPerPixel;

        Measurement = RouteMeasure.FormatDistance(metres)
            + "  ·  "
            + RouteMeasure.FormatDuration(RouteMeasure.SprintTime(metres));
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}
