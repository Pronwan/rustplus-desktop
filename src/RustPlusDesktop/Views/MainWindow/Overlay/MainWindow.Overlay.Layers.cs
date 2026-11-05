using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RustPlusDesk.Views;

// Layers panel: lists my overlay elements grouped by kind (Bases / Icons / Markers /
// Text / Drawings) so I can find, select, edit, hide or delete any of them.
public partial class MainWindow
{
    private readonly ObservableCollection<MapLayerItem> _layerItems = new();
    private readonly HashSet<string> _hiddenLayerCategories = new(StringComparer.Ordinal);
    private bool _layersInitialised;

    // Category display order.
    private static readonly string[] LayerCategoryOrder = { "Bases", "Icons", "Markers", "Text", "Drawings" };
    private static int CategoryRank(string category) => Array.IndexOf(LayerCategoryOrder, category) is var i && i >= 0 ? i : 99;

    private void InitLayersPanel()
    {
        if (_layersInitialised || LayersList == null) return;
        _layersInitialised = true;
        ICollectionView view = CollectionViewSource.GetDefaultView(_layerItems);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MapLayerItem.Category)));
        LayersList.ItemsSource = view;
    }

    private void BtnToggleLayers_Click(object sender, RoutedEventArgs e)
    {
        if (LayersPanel == null) return;
        bool show = LayersPanel.Visibility != Visibility.Visible;
        LayersPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) RefreshLayersPanel();
    }

    private void CloseLayersPanel_Click(object sender, RoutedEventArgs e)
    {
        if (LayersPanel != null) LayersPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>Rebuild the layer list from my current overlay elements. Cheap no-op while the panel is hidden.</summary>
    private void RefreshLayersPanel()
    {
        if (LayersPanel == null || LayersPanel.Visibility != Visibility.Visible) return;

        var items = new List<MapLayerItem>();
        var seenGroups = new HashSet<string>();
        foreach (UIElement child in Overlay.Children)
        {
            if (child is not FrameworkElement fe) continue;
            if (fe.Tag is not OverlayTag meta || meta.OwnerSteamId != _mySteamId || !meta.IsUserEditable) continue;

            // Grouped elements (routes / manual groups) fold into a single row.
            if (meta.GroupId is { } gid)
            {
                if (!seenGroups.Add(gid)) continue;
                int count = GetGroupMembers(gid).Count;
                bool isRoute = gid.StartsWith("route-", StringComparison.Ordinal);
                items.Add(new MapLayerItem
                {
                    Element = fe,
                    Category = "Drawings",
                    Label = isRoute ? $"Route · {count} arrows" : $"Group · {count} items",
                    GroupId = gid,
                    Swatch = fe is Polyline gpl ? (gpl.Stroke as SolidColorBrush) : null,
                    IsHidden = fe.Visibility == Visibility.Collapsed
                });
                continue;
            }

            (string category, string label, bool isText) = CategorizeLayer(fe);
            items.Add(new MapLayerItem
            {
                Element = fe,
                Category = category,
                Label = label,
                IsText = isText,
                Preview = ExtractPreview(fe),
                Swatch = fe is Polyline pl ? (pl.Stroke as SolidColorBrush) : null,
                IsHidden = fe.Visibility == Visibility.Collapsed
            });
        }

        items.Sort((a, b) => CategoryRank(a.Category).CompareTo(CategoryRank(b.Category)));
        _layerItems.Clear();
        foreach (MapLayerItem item in items) _layerItems.Add(item);

        if (LayersEmptyHint != null)
            LayersEmptyHint.Visibility = _layerItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static (string category, string label, bool isText) CategorizeLayer(FrameworkElement fe)
    {
        switch (fe)
        {
            case Polyline pl:
                return ("Drawings", DescribeStroke(pl), false);
            case TextBlock tb:
                return ("Text", string.IsNullOrWhiteSpace(tb.Text) ? "(empty)" : tb.Text, true);
        }

        string? path = (fe.Tag as OverlayTag)?.CustomIconPath;

        if (path != null && path.StartsWith("rust-marker://", StringComparison.OrdinalIgnoreCase))
        {
            string shape = "marker", color = string.Empty;
            try { var u = new Uri(path); shape = u.Host; color = u.AbsolutePath.Trim('/'); } catch { }
            string label = Capitalise(shape) + (color.Length > 0 ? $" ({color})" : string.Empty);
            return ("Markers", label, false);
        }

        if (path != null && (path.Contains("base1", StringComparison.OrdinalIgnoreCase) || path.Contains("base2", StringComparison.OrdinalIgnoreCase)))
        {
            string kind = path.Contains("base1", StringComparison.OrdinalIgnoreCase) ? "Own base" : "Enemy base";
            string? note = (fe.Tag as OverlayTag)?.Note;
            if (!string.IsNullOrWhiteSpace(note)) kind += " — " + note;
            return ("Bases", kind, false);
        }

        return ("Icons", IconLabelFromPath(path), false);
    }

    private static string DescribeStroke(Polyline pl)
    {
        int n = pl.Points.Count;
        bool closed = n > 2 && pl.Points[0] == pl.Points[n - 1];
        return n switch
        {
            <= 2 => "Line",
            >= 40 => "Circle",
            5 when closed => "Rectangle",
            5 => "Arrow",
            _ => "Freehand"
        };
    }

    private static string IconLabelFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Icon";
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        return name switch
        {
            "sam-site" => "SAM site",
            "turret" => "Turret",
            "base1" => "Own base",
            "base2" => "Enemy base",
            _ => Capitalise(name.Replace('-', ' ').Replace('_', ' '))
        };
    }

    private static string Capitalise(string s) =>
        string.IsNullOrEmpty(s) ? s : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s);

    private static ImageSource? ExtractPreview(FrameworkElement fe)
    {
        if (fe is Image img) return img.Source;
        if (fe is Grid grid)
            foreach (UIElement c in grid.Children)
                if (c is Image gi) return gi.Source;
        return null;
    }

    // --- row / group actions ---

    private void LayerRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MapLayerItem item) return;
        if (_currentTool != OverlayToolMode.None) SelectOverlayTool(OverlayToolMode.None);
        SelectElement(item.Element);
        UpdateOptionsPanelVisibility();
    }

    private void LayerEditText_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MapLayerItem { Element: TextBlock tb })
            BeginEditText(tb);
    }

    // Show/hide this layer (or its whole group) on the map — session-only, like a Photoshop eye.
    private void LayerVisibility_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MapLayerItem item) return;
        List<FrameworkElement> targets = item.GroupId is { } gid
            ? GetGroupMembers(gid)
            : new List<FrameworkElement> { item.Element };

        bool hide = !item.IsHidden;
        foreach (FrameworkElement t in targets)
            t.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
        item.IsHidden = hide;
    }

    private void LayerDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MapLayerItem item) return;
        DeleteElement(item.Element);
        RefreshLayersPanel();
    }

    // Group the checked layers into one unit (Photoshop-style). Existing groups are merged in.
    private void GroupCheckedLayers_Click(object sender, RoutedEventArgs e)
    {
        var checkedItems = _layerItems.Where(i => i.IsSelectedInPanel).ToList();
        if (checkedItems.Count < 2) return;

        var members = new List<FrameworkElement>();
        foreach (MapLayerItem item in checkedItems)
            members.AddRange(item.GroupId is { } gid ? GetGroupMembers(gid) : new List<FrameworkElement> { item.Element });

        string newGroup = "grp-" + Guid.NewGuid().ToString("N");
        foreach (FrameworkElement fe in members)
            if (fe.Tag is OverlayTag t) t.GroupId = newGroup;

        DeselectElement();
        SaveOwnOverlayToJson();   // persists + refreshes the panel
    }

    // Ungroup the checked group rows back into individual layers.
    private void UngroupCheckedLayers_Click(object sender, RoutedEventArgs e)
    {
        var groups = _layerItems.Where(i => i.IsSelectedInPanel && i.GroupId != null).ToList();
        if (groups.Count == 0) return;

        foreach (MapLayerItem g in groups)
            foreach (FrameworkElement fe in GetGroupMembers(g.GroupId!))
                if (fe.Tag is OverlayTag t) t.GroupId = null;

        DeselectElement();
        SaveOwnOverlayToJson();
    }

    private void LayerGroupToggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CollectionViewGroup group) return;
        string category = group.Name?.ToString() ?? string.Empty;

        bool nowHidden = _hiddenLayerCategories.Add(category);
        if (!nowHidden) _hiddenLayerCategories.Remove(category);

        Visibility vis = nowHidden ? Visibility.Collapsed : Visibility.Visible;
        foreach (MapLayerItem item in _layerItems.Where(i => i.Category == category))
            item.Element.Visibility = vis;
    }
}

public sealed class MapLayerItem : System.ComponentModel.INotifyPropertyChanged
{
    public required FrameworkElement Element { get; init; }
    public required string Category { get; init; }
    public required string Label { get; init; }
    public bool IsText { get; init; }
    public ImageSource? Preview { get; init; }
    public SolidColorBrush? Swatch { get; init; }
    public string? GroupId { get; init; }
    public bool IsGroup => GroupId != null;
    public bool HasPreview => Preview != null;
    public bool HasNoPreview => Preview == null;

    private bool _checked;
    public bool IsSelectedInPanel
    {
        get => _checked;
        set { if (_checked != value) { _checked = value; PropertyChanged?.Invoke(this, new(nameof(IsSelectedInPanel))); } }
    }

    private bool _hidden;
    public bool IsHidden
    {
        get => _hidden;
        set
        {
            if (_hidden == value) return;
            _hidden = value;
            PropertyChanged?.Invoke(this, new(nameof(IsHidden)));
            PropertyChanged?.Invoke(this, new(nameof(RowOpacity)));
        }
    }
    public double RowOpacity => _hidden ? 0.4 : 1.0;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
