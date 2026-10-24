using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RustPlusDesk.Services;
using RustPlusDesk.Services.Sidebar;

namespace RustPlusDesk.Views;

/// <summary>
/// Phase 2: Discord-style drag-and-drop for the sidebar rail. A custom mouse-driven drag (so a
/// plain click still selects/expands) reorders tabs and folders, drops a tab onto another tab to
/// create a folder, drops onto a folder to add, and drags a tab out to ungroup. A Fluent ghost and
/// an insertion/into indicator are drawn on the adorner layer; every change persists immediately.
/// </summary>
public partial class MainWindow
{
    internal sealed class RailDragRef
    {
        public string? TabId { get; init; }
        public string? FolderId { get; init; }
        public string? ParentFolderId { get; init; }
        public bool IsFolder => FolderId is not null;

        /// <summary>The button to snapshot for the drag ghost (set at build time).</summary>
        public FrameworkElement? GhostSource { get; set; }
    }



    private bool _railDragHooked;
    private Point _railPressPoint;
    private FrameworkElement? _railPressedEntry;
    private RailDragRef? _railPressedRef;
    private bool _railDragging;
    private FrameworkElement? _railDimmedEntry;
    private Image? _railGhostImage;
    private Border? _railInsertLine;
    private Border? _railIntoBox;
    private static readonly Brush RailAccentBrush = MakeAccentBrush();

    private static Brush MakeAccentBrush()
    {
        var b = new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF));
        b.Freeze();
        return b;
    }

    private enum DropRegion { Before, Into, After }

    private void HookRailDrag()
    {
        if (_railDragHooked || RailItemsHost is null) return;
        _railDragHooked = true;
        RailItemsHost.PreviewMouseLeftButtonDown += RailHost_PreviewMouseLeftButtonDown;
        RailItemsHost.PreviewMouseMove += RailHost_PreviewMouseMove;
        RailItemsHost.PreviewMouseLeftButtonUp += RailHost_PreviewMouseLeftButtonUp;
        // NOTE: we deliberately do NOT capture the mouse to the rail host, nor auto-cancel on
        // LostMouseCapture. The rail buttons capture the mouse themselves on press; stealing it
        // caused a capture tug-of-war that cancelled every drag. Preview mouse events still route
        // through the host (an ancestor of the captured button), which is all we need.
    }

    private void RailHost_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var (entry, dragRef) = FindEntryFromSource(e.OriginalSource as DependencyObject);
        if (entry is null || dragRef is null) return;
        _railPressPoint = e.GetPosition(RailItemsHost);
        _railPressedEntry = entry;
        _railPressedRef = dragRef;
    }

    private void RailHost_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _railPressedEntry is null) return;

        var pos = e.GetPosition(RailItemsHost);
        if (!_railDragging)
        {
            if (Math.Abs(pos.Y - _railPressPoint.Y) < SystemParameters.MinimumVerticalDragDistance &&
                Math.Abs(pos.X - _railPressPoint.X) < SystemParameters.MinimumHorizontalDragDistance)
                return;
            BeginRailDrag();
        }

        if (Keyboard.IsKeyDown(Key.Escape)) { CancelRailDrag(); return; }
        UpdateRailDrag(pos);
    }

    private void RailHost_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_railDragging)
        {
            CompleteRailDrag(e.GetPosition(RailItemsHost));
            e.Handled = true; // suppress the button's click so the dragged tab isn't also selected
        }
        else ResetRailPress();
    }

    private void BeginRailDrag()
    {
        // Use locals throughout: capturing the mouse / adding adorners can synchronously re-raise
        // input that nulls the pressed-entry fields mid-method, so never touch them after capture.
        var entry = _railPressedEntry;
        var dragRef = _railPressedRef;
        if (entry is null || dragRef is null) return;

        _railDragging = true;

        // Snapshot the button BEFORE dimming so the ghost is crisp. Snapshot the button (not the
        // entry) — the entry hosts a Popup and RenderTargetBitmap throws on Popup-containing visuals.
        var ghostVisual = dragRef.GhostSource;
        var snapshot = ghostVisual is not null ? RenderSnapshot(ghostVisual) : null;

        _railDimmedEntry = entry;
        entry.Opacity = 0.35;

        // Ghost image on the overlay canvas (no adorner layer: FluentWindow has none).
        if (snapshot is not null && ghostVisual is not null && RailDragOverlay is not null)
        {
            _railGhostImage = new Image
            {
                Source = snapshot,
                Width = ghostVisual.ActualWidth,
                Height = ghostVisual.ActualHeight,
                Opacity = 0.9,
                IsHitTestVisible = false,
            };
            _railGhostImage.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 10, ShadowDepth = 0, Opacity = 0.5,
                Color = Color.FromRgb(0, 0, 0),
            };
            RailDragOverlay.Children.Add(_railGhostImage);
        }
        // No CaptureMouse here — the pressed button already holds capture; preview events reach us
        // through the host as its ancestor. Stealing capture cancels the drag (capture tug-of-war).
    }

    // Ghost trails just below/right of the cursor (Discord-style) rather than sitting under it.
    private const double GhostOffsetX = 10;
    private const double GhostOffsetY = 10;

    private void UpdateRailDrag(Point pos)
    {
        if (RailDragOverlay is null) return;

        if (_railGhostImage is not null)
        {
            var o = RailItemsHost.TranslatePoint(pos, RailDragOverlay);
            Canvas.SetLeft(_railGhostImage, o.X - _railGhostImage.Width / 2 + GhostOffsetX);
            Canvas.SetTop(_railGhostImage, o.Y - _railGhostImage.Height / 2 + GhostOffsetY);
        }

        var target = ResolveDrop(pos);
        if (target is null) { ClearIndicator(); return; }

        var (entry, _, region) = target.Value;
        var bounds = BoundsInHost(entry);
        if (region == DropRegion.Into)
            ShowIntoIndicator(bounds);
        else
            ShowLineIndicator(region == DropRegion.Before ? bounds.Top : bounds.Bottom, bounds.Left, bounds.Width);
    }

    private void ShowLineIndicator(double hostY, double hostX, double width)
    {
        if (_railIntoBox is not null) _railIntoBox.Visibility = Visibility.Collapsed;
        _railInsertLine ??= AddOverlayChild(new Border
        {
            Height = 3,
            CornerRadius = new CornerRadius(1.5),
            Background = RailAccentBrush,
            IsHitTestVisible = false,
        });
        var tl = RailItemsHost.TranslatePoint(new Point(hostX + 4, hostY - 1.5), RailDragOverlay!);
        _railInsertLine.Width = Math.Max(4, width - 8);
        _railInsertLine.Visibility = Visibility.Visible;
        Canvas.SetLeft(_railInsertLine, tl.X);
        Canvas.SetTop(_railInsertLine, tl.Y);
    }

    private void ShowIntoIndicator(Rect hostBounds)
    {
        if (_railInsertLine is not null) _railInsertLine.Visibility = Visibility.Collapsed;
        _railIntoBox ??= AddOverlayChild(new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(2),
            BorderBrush = RailAccentBrush,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0x60, 0xCD, 0xFF)),
            IsHitTestVisible = false,
        });
        var tl = RailItemsHost.TranslatePoint(new Point(hostBounds.Left, hostBounds.Top), RailDragOverlay!);
        _railIntoBox.Width = hostBounds.Width;
        _railIntoBox.Height = hostBounds.Height;
        _railIntoBox.Visibility = Visibility.Visible;
        Canvas.SetLeft(_railIntoBox, tl.X);
        Canvas.SetTop(_railIntoBox, tl.Y);
    }

    private T AddOverlayChild<T>(T child) where T : UIElement
    {
        RailDragOverlay!.Children.Add(child);
        return child;
    }

    private void ClearIndicator()
    {
        if (_railInsertLine is not null) _railInsertLine.Visibility = Visibility.Collapsed;
        if (_railIntoBox is not null) _railIntoBox.Visibility = Visibility.Collapsed;
    }

    private void CompleteRailDrag(Point pos)
    {
        var src = _railPressedRef;
        var target = ResolveDrop(pos);
        EndRailDragVisuals();

        if (src is not null && target is not null && _railLayout is not null)
        {
            var (_, targetRef, region) = target.Value;
            if (ApplyDrop(src, targetRef, region))
            {
                DissolveSingletonFolders();
                TrackingService.SaveRailLayout(_railLayout);
                RebuildRail();
            }
        }
        ResetRailPress();
    }

    private void CancelRailDrag()
    {
        if (!_railDragging) { ResetRailPress(); return; }
        EndRailDragVisuals();
        ResetRailPress();
    }

    private void EndRailDragVisuals()
    {
        // Clear the flag first: releasing capture fires LostMouseCapture -> CancelRailDrag, which
        // must see the drag as already ended so it doesn't re-enter this teardown.
        _railDragging = false;
        if (_railDimmedEntry is not null) { _railDimmedEntry.Opacity = 1.0; _railDimmedEntry = null; }
        // Release whatever holds capture (the pressed button) so it doesn't stay stuck-pressed.
        if (System.Windows.Input.Mouse.Captured is not null) System.Windows.Input.Mouse.Capture(null);
        RailDragOverlay?.Children.Clear();
        _railGhostImage = null;
        _railInsertLine = null;
        _railIntoBox = null;
    }

    private void ResetRailPress()
    {
        _railPressedEntry = null;
        _railPressedRef = null;
        _railDragging = false;
    }

    // ── Hit-testing ────────────────────────────────────────────────────────────────────────
    private (FrameworkElement? entry, RailDragRef? dragRef) FindEntryFromSource(DependencyObject? source)
    {
        while (source is not null && source != RailItemsHost)
        {
            if (source is FrameworkElement fe && fe.Tag is RailDragRef r)
                return (fe, r);
            source = VisualTreeHelper.GetParent(source);
        }
        return (null, null);
    }

    /// <summary>All draggable/droppable entries under the host, in vertical order.</summary>
    private List<(FrameworkElement entry, RailDragRef dragRef, Rect bounds)> CollectEntries()
    {
        var list = new List<(FrameworkElement, RailDragRef, Rect)>();
        CollectEntriesRecursive(RailItemsHost, list);
        return list.OrderBy(x => x.Item3.Top).ToList();
    }

    private void CollectEntriesRecursive(DependencyObject node, List<(FrameworkElement, RailDragRef, Rect)> acc)
    {
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is FrameworkElement fe)
            {
                if (fe.Visibility != Visibility.Visible) continue;
                if (fe.Tag is RailDragRef r)
                {
                    acc.Add((fe, r, BoundsInHost(fe)));
                    // An expanded folder is a panel with a RailDragRef tag; descend to reach its child tabs.
                    if (r.IsFolder) CollectEntriesRecursive(child, acc);
                }
                else
                {
                    CollectEntriesRecursive(child, acc);
                }
            }
            else
            {
                CollectEntriesRecursive(child, acc);
            }
        }
    }

    private Rect BoundsInHost(FrameworkElement fe)
    {
        var transform = fe.TransformToAncestor(RailItemsHost);
        return transform.TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
    }

    private (FrameworkElement entry, RailDragRef dragRef, DropRegion region)? ResolveDrop(Point pos)
    {
        var entries = CollectEntries();
        if (entries.Count == 0) return null;

        // Prefer the most specific (smallest) entry under the cursor: an expanded folder's
        // container overlaps its child tabs, and the children must win for within-folder reorder.
        var containing = entries
            .Where(e => pos.Y >= e.bounds.Top && pos.Y <= e.bounds.Bottom)
            .OrderBy(e => e.bounds.Height)
            .ToList();

        if (containing.Count == 0)
        {
            // In a gap or below everything → after the bottom-most entry.
            var bottom = entries.OrderBy(e => e.bounds.Bottom).Last();
            return pos.Y >= bottom.bounds.Bottom ? (bottom.entry, bottom.dragRef, DropRegion.After) : null;
        }

        var (entry, dragRef, bounds) = containing[0];
        double rel = bounds.Height > 0 ? (pos.Y - bounds.Top) / bounds.Height : 0.5;

        bool draggingFolder = _railPressedRef?.IsFolder == true;
        bool isChild = dragRef.ParentFolderId is not null;

        // A child tab inside an expanded folder: top/bottom half → reorder within the folder.
        if (isChild && !draggingFolder)
            return (entry, dragRef, rel < 0.5 ? DropRegion.Before : DropRegion.After);

        const double band = 0.30;
        if (rel < band) return (entry, dragRef, DropRegion.Before);
        if (rel > 1 - band) return (entry, dragRef, DropRegion.After);
        // Middle band: fold together (unless dragging a folder — folders don't nest).
        return (entry, dragRef, draggingFolder ? (rel < 0.5 ? DropRegion.Before : DropRegion.After) : DropRegion.Into);
    }

    // ── Layout mutations ─────────────────────────────────────────────────────────────────────
    private bool ApplyDrop(RailDragRef src, RailDragRef target, DropRegion region)
    {
        if (_railLayout is null) return false;

        // No-op: dropping onto itself.
        if (src.TabId is not null && src.TabId == target.TabId) return false;
        if (src.FolderId is not null && src.FolderId == target.FolderId) return false;

        if (src.IsFolder)
            return MoveFolder(src.FolderId!, target, region);

        string tabId = src.TabId!;

        if (target.IsFolder)
        {
            // Before/After the folder at root, or Into it.
            if (region == DropRegion.Into) return AddTabToFolder(tabId, target.FolderId!, anchorChildTabId: null, after: false);
            return PlaceTabAtRoot(tabId, anchorFolderId: target.FolderId, anchorTabId: null, after: region == DropRegion.After);
        }

        // Target is a tab.
        if (target.ParentFolderId is not null)
        {
            // Reorder within (or move into) the folder, positioned relative to the target child.
            return AddTabToFolder(tabId, target.ParentFolderId, anchorChildTabId: target.TabId, after: region == DropRegion.After);
        }

        // Target is a root tab.
        if (region == DropRegion.Into)
            return CreateFolderFromTabs(tabId, target.TabId!);

        return PlaceTabAtRoot(tabId, anchorFolderId: null, anchorTabId: target.TabId, after: region == DropRegion.After);
    }

    private void DetachTab(string tabId)
    {
        var nodes = _railLayout!.Nodes;
        nodes.RemoveAll(n => n.IsTab && n.TabId == tabId);
        foreach (var folder in nodes.Where(n => n.IsFolder))
            folder.Children.RemoveAll(id => id == tabId);
        PruneEmptyFolders();
    }

    private void PruneEmptyFolders() =>
        _railLayout!.Nodes.RemoveAll(n => n.IsFolder && n.Children.Count == 0);

    /// <summary>
    /// A folder needs at least two tabs to justify itself. After a drag, any folder left with a
    /// single tab is dissolved: the remaining tab replaces the folder in place (Discord behaviour).
    /// Runs only as a final step — never mid-operation, or a within-folder reorder would break.
    /// </summary>
    private void DissolveSingletonFolders()
    {
        var nodes = _railLayout!.Nodes;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsFolder && nodes[i].Children.Count == 1)
                nodes[i] = RailNodeData.Tab(nodes[i].Children[0]);
        }
        nodes.RemoveAll(n => n.IsFolder && n.Children.Count == 0);
    }

    private bool PlaceTabAtRoot(string tabId, string? anchorFolderId, string? anchorTabId, bool after)
    {
        DetachTab(tabId);
        var nodes = _railLayout!.Nodes;
        int index = nodes.Count;
        if (anchorFolderId is not null)
        {
            int a = nodes.FindIndex(n => n.IsFolder && n.FolderId == anchorFolderId);
            if (a >= 0) index = after ? a + 1 : a;
        }
        else if (anchorTabId is not null)
        {
            int a = nodes.FindIndex(n => n.IsTab && n.TabId == anchorTabId);
            if (a >= 0) index = after ? a + 1 : a;
        }
        nodes.Insert(Math.Clamp(index, 0, nodes.Count), RailNodeData.Tab(tabId));
        return true;
    }

    private bool AddTabToFolder(string tabId, string folderId, string? anchorChildTabId, bool after)
    {
        DetachTab(tabId);
        var folder = _railLayout!.Nodes.FirstOrDefault(n => n.IsFolder && n.FolderId == folderId);
        if (folder is null) return false;

        int index = folder.Children.Count;
        if (anchorChildTabId is not null)
        {
            int a = folder.Children.IndexOf(anchorChildTabId);
            if (a >= 0) index = after ? a + 1 : a;
        }
        folder.Children.Insert(Math.Clamp(index, 0, folder.Children.Count), tabId);
        return true;
    }

    private bool CreateFolderFromTabs(string dragTabId, string targetTabId)
    {
        DetachTab(dragTabId);
        var nodes = _railLayout!.Nodes;
        int targetIndex = nodes.FindIndex(n => n.IsTab && n.TabId == targetTabId);
        if (targetIndex < 0) return false;

        string id = "folder-" + Guid.NewGuid().ToString("N")[..8];
        string name = RailText("SidebarNewFolder", "New folder");
        string color = RailCatalog.DefaultFolderColor;

        nodes[targetIndex] = RailNodeData.Folder(id, name, color, new[] { targetTabId, dragTabId }, expanded: false);
        return true;
    }

    private bool MoveFolder(string folderId, RailDragRef target, DropRegion region)
    {
        var nodes = _railLayout!.Nodes;
        var folderNode = nodes.FirstOrDefault(n => n.IsFolder && n.FolderId == folderId);
        if (folderNode is null) return false;
        nodes.Remove(folderNode);

        int index = nodes.Count;
        if (target.IsFolder && target.FolderId != folderId)
        {
            int a = nodes.FindIndex(n => n.IsFolder && n.FolderId == target.FolderId);
            if (a >= 0) index = region == DropRegion.After ? a + 1 : a;
        }
        else if (target.TabId is not null && target.ParentFolderId is null)
        {
            int a = nodes.FindIndex(n => n.IsTab && n.TabId == target.TabId);
            if (a >= 0) index = region == DropRegion.After ? a + 1 : a;
        }
        nodes.Insert(Math.Clamp(index, 0, nodes.Count), folderNode);
        return true;
    }

    private static string RailText(string key, string fallback) => Helpers.Loc.Text(key, fallback);

    private static ImageSource? RenderSnapshot(FrameworkElement element)
    {
        try
        {
            int w = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
            int h = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(element);
            rtb.Freeze();
            return rtb;
        }
        catch
        {
            return null; // Ghost is cosmetic; drag still works without it.
        }
    }
}
