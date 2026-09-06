using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using RustPlusDesk.Services;
using RustPlusDesk.Services.Sidebar;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

/// <summary>
/// Data-driven sidebar rail: renders the persisted <see cref="RailLayoutData"/> (ordered tabs and
/// Discord-style folders) into the compact rail. Reuses the Fluent styles/popover defined in
/// CompactSidebarRail.Resources so the output matches the original hand-authored rail. Drag-and-drop
/// reordering/foldering is layered on in Phase 2; this file owns building and refreshing the visuals.
/// </summary>
public partial class MainWindow
{
    private RailLayoutData? _railLayout;

    // Collapsed folder tiles keyed by folder id, so selection changes can re-tint them.
    private readonly List<RailFolderVisual> _railFolderVisuals = new();

    private sealed record RailFolderVisual(Border Tile, IReadOnlyList<string> ChildIds);

    // Discord-style neutral panel behind a collapsed folder's 2x2 preview.
    private static readonly Brush FolderTileIdleBrush = Frozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
    private static readonly Brush FolderTileIdleBorder = Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
    private static readonly Brush FolderTileActiveBrush = Frozen(Color.FromArgb(0x38, 0x60, 0xCD, 0xFF));
    private static readonly Brush FolderTileActiveBorder = Frozen(Color.FromArgb(0x90, 0x60, 0xCD, 0xFF));
    private static readonly Brush MiniIconBrush = Frozen(Color.FromRgb(0xEC, 0xF0, 0xF6));

    // Discord-style neutral pill container for expanded folders, matching the dark theme rail.
    private static readonly Brush FolderColumnBackground = Frozen(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
    private static readonly Brush FolderColumnBorder = Frozen(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
    private static readonly Brush FolderHeaderIconBrush = Frozen(Color.FromRgb(0x60, 0xCD, 0xFF));

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private string ToolsFolderDefaultName =>
        TryFindResource("SidebarToolsFolder") as string ?? "Tools";

    /// <summary>Resolves a tab id to its backing <see cref="TabItem"/> in the window name scope.</summary>
    private TabItem? ResolveTabItem(RailTabInfo info) => FindName(info.TabItemName) as TabItem;

    /// <summary>True when a catalog tab should currently be shown (handles the Players opt-in).</summary>
    private bool IsRailTabVisible(RailTabInfo info) => !info.IsPlayersOptIn || TrackingService.ShowPlayersTab;

    /// <summary>(Re)builds the entire row-1 rail from the persisted layout.</summary>
    public void RebuildRail()
    {
        if (RailItemsHost is null) return;

        HookRailDrag();
        _railLayout = TrackingService.GetRailLayout(ToolsFolderDefaultName);
        _railFolderVisuals.Clear();
        RailItemsHost.Children.Clear();

        bool firstTabTagged = false;
        bool secondTabTagged = false;
        foreach (var node in _railLayout.Nodes)
        {
            if (node.IsFolder)
            {
                var folder = BuildFolderEntry(node);
                if (folder is not null) RailItemsHost.Children.Add(folder);
            }
            else if (node.TabId is { } tabId && RailCatalog.Find(tabId) is { } info)
            {
                if (!IsRailTabVisible(info)) continue;
                var entry = BuildTabEntry(info);
                if (!firstTabTagged)
                {
                    Features.Tutorials.Tutorial.SetTargetId(entry, "Sidebar.SingleTab");
                    firstTabTagged = true;
                }
                else if (!secondTabTagged)
                {
                    Features.Tutorials.Tutorial.SetTargetId(entry, "Sidebar.SecondTab");
                    secondTabTagged = true;
                }
                RailItemsHost.Children.Add(entry);
            }
        }

        UpdateRailFolderHighlights();
    }

    // ── Tab entry (button + hover popover), mirroring the original static XAML ──────────────
    private FrameworkElement BuildTabEntry(RailTabInfo info, string? parentFolderId = null)
    {
        var dragRef = new RailDragRef { TabId = info.Id, ParentFolderId = parentFolderId };
        var grid = new Grid { Tag = dragRef, Width = 48 };
        var button = BuildRailButton(info);
        dragRef.GhostSource = button;
        grid.Children.Add(button);
        grid.Children.Add(BuildRailPopover(button));
        return grid;
    }

    private WpfUi.Button BuildRailButton(RailTabInfo info)
    {
        var button = new WpfUi.Button
        {
            Style = (Style)RailItemsHost.FindResource("SidebarRailButton"),
            Tag = ResolveTabItem(info),
        };
        button.Click += CompactSidebarTab_Click;

        // Name / help text: prefer a live localization key, fall back to the literal.
        if (info.NameKey is not null && TryFindResource(info.NameKey) is not null)
            button.SetResourceReference(System.Windows.Automation.AutomationProperties.NameProperty, info.NameKey);
        else
            System.Windows.Automation.AutomationProperties.SetName(button, info.NameFallback);

        if (info.HelpKey is not null && TryFindResource(info.HelpKey) is not null)
            button.SetResourceReference(System.Windows.Automation.AutomationProperties.HelpTextProperty, info.HelpKey);
        else
            System.Windows.Automation.AutomationProperties.SetHelpText(button, info.HelpFallback);

        if (info.GeometryPath is { } geo)
            button.Content = BuildGeometryIcon(geo, button);
        else
            button.Icon = new WpfUi.SymbolIcon { Symbol = info.Symbol };

        return button;
    }

    /// <summary>Custom geometry icon (Death stats) whose fill tracks the button foreground.</summary>
    private static Viewbox BuildGeometryIcon(string geometry, WpfUi.Button owner)
    {
        var path = new Path { Data = Geometry.Parse(geometry), Stretch = Stretch.Uniform };
        path.SetBinding(Shape.FillProperty, new Binding(nameof(Control.Foreground)) { Source = owner });
        return new Viewbox { Width = 16, Height = 16, Child = path };
    }

    private Popup BuildRailPopover(FrameworkElement target)
    {
        var popup = new Popup
        {
            AllowsTransparency = true,
            HorizontalOffset = 8,
            IsHitTestVisible = false,
            Placement = PlacementMode.Right,
            PlacementTarget = target,
            PopupAnimation = PopupAnimation.Fade,
            StaysOpen = true,
        };
        popup.SetBinding(Popup.IsOpenProperty,
            new Binding(nameof(UIElement.IsMouseOver)) { Source = target, Mode = BindingMode.OneWay });
        popup.Opened += SidebarTabPopover_Opened;

        // With a ContentTemplate set, Content becomes the template's DataContext (no reparenting).
        popup.Child = new ContentControl
        {
            Content = target,
            ContentTemplate = (DataTemplate)RailItemsHost.FindResource("SidebarTabPopoverTemplate"),
        };
        return popup;
    }

    // ── Folder entry (Discord-style: unified capsule with fluid fold / unfold animation) ──
    private sealed class RailFolderEntryController
    {
        public required RailNodeData Node { get; init; }
        public required RailDragRef DragRef { get; init; }
        public required Border Container { get; init; }
        public required SolidColorBrush ContainerBackgroundBrush { get; init; }
        public required SolidColorBrush ContainerBorderBrush { get; init; }
        public required FrameworkElement CollapsedVisual { get; init; }
        public required FrameworkElement ExpandedVisual { get; init; }
        public required WpfUi.Button CollapsedButton { get; init; }
        public required WpfUi.Button ExpandedButton { get; init; }
        public required Border Tray { get; init; }
        public required StackPanel ChildPanel { get; init; }
        public required TranslateTransform TrayTransform { get; init; }
        public required Border Tile { get; init; }
        public bool IsAnimating { get; set; }
    }

    private FrameworkElement? BuildFolderEntry(RailNodeData node)
    {
        var childInfos = node.Children
            .Select(RailCatalog.Find)
            .Where(i => i is not null && IsRailTabVisible(i!))
            .Cast<RailTabInfo>()
            .ToList();

        if (childInfos.Count == 0) return null;

        var folderRef = new RailDragRef { FolderId = node.FolderId };

        var bgBrush = new SolidColorBrush(node.Expanded
            ? Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF));
        var borderBrush = new SolidColorBrush(node.Expanded
            ? Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF));

        var container = new Border
        {
            Width = 48,
            CornerRadius = new CornerRadius(16),
            Background = bgBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0, 3, 0, 3),
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            Tag = folderRef,
        };

        var inner = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        container.Child = inner;

        // Header slot (48x44): hosts both the collapsed 2x2 preview button and the expanded folder glyph.
        var headerGrid = new Grid { Width = 48, Height = 44 };
        inner.Children.Add(headerGrid);

        // Collapsed header: neutral rounded tile with a 2x2 preview of the first four child icons.
        var collapsedGrid = new Grid();
        var collapsedButton = new WpfUi.Button
        {
            Style = (Style)RailItemsHost.FindResource("SidebarRailButton"),
            Margin = new Thickness(0),
            Padding = new Thickness(0),
        };
        bool isDefaultTools = node.FolderId == RailCatalog.DefaultFolderId;

        if (isDefaultTools)
            collapsedButton.SetResourceReference(System.Windows.Automation.AutomationProperties.NameProperty, "SidebarToolsFolder");
        else
            System.Windows.Automation.AutomationProperties.SetName(collapsedButton, node.Name ?? (TryFindResource("SidebarNewFolder") as string ?? "New folder"));

        collapsedButton.SetResourceReference(System.Windows.Automation.AutomationProperties.HelpTextProperty, "SidebarFolderHelp");

        var preview = new UniformGrid { Rows = 2, Columns = 2 };
        foreach (var info in childInfos.Take(4))
            preview.Children.Add(BuildMiniIcon(info));

        var tile = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(13),
            Background = FolderTileIdleBrush,
            BorderBrush = FolderTileIdleBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5),
            Child = preview,
        };
        collapsedButton.Content = tile;
        collapsedGrid.Children.Add(collapsedButton);
        collapsedGrid.Children.Add(BuildFolderPopover(collapsedButton));
        headerGrid.Children.Add(collapsedGrid);

        // Expanded header: folder glyph button that collapses the folder back down.
        var expandedGrid = new Grid();
        var expandedButton = new WpfUi.Button
        {
            Style = (Style)RailItemsHost.FindResource("SidebarRailButton"),
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            Icon = new WpfUi.SymbolIcon { Symbol = WpfUi.SymbolRegular.Folder24, FontSize = 20 },
            Foreground = FolderHeaderIconBrush,
        };

        if (isDefaultTools)
            expandedButton.SetResourceReference(System.Windows.Automation.AutomationProperties.NameProperty, "SidebarToolsFolder");
        else
            System.Windows.Automation.AutomationProperties.SetName(expandedButton, node.Name ?? (TryFindResource("SidebarNewFolder") as string ?? "New folder"));

        expandedButton.SetResourceReference(System.Windows.Automation.AutomationProperties.HelpTextProperty, "SidebarFolderCollapseHelp");
        expandedGrid.Children.Add(expandedButton);
        expandedGrid.Children.Add(BuildFolderPopover(expandedButton));
        headerGrid.Children.Add(expandedGrid);

        // Child tray (accordion drawer): clips items cleanly while expanding/collapsing.
        var childPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var trayTransform = new TranslateTransform();
        childPanel.RenderTransform = trayTransform;

        foreach (var info in childInfos)
            childPanel.Children.Add(BuildTabEntry(info, node.FolderId));

        var tray = new Border
        {
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
            Child = childPanel,
        };
        inner.Children.Add(tray);

        var controller = new RailFolderEntryController
        {
            Node = node,
            DragRef = folderRef,
            Container = container,
            ContainerBackgroundBrush = bgBrush,
            ContainerBorderBrush = borderBrush,
            CollapsedVisual = collapsedGrid,
            ExpandedVisual = expandedGrid,
            CollapsedButton = collapsedButton,
            ExpandedButton = expandedButton,
            Tray = tray,
            ChildPanel = childPanel,
            TrayTransform = trayTransform,
            Tile = tile,
        };

        collapsedButton.Click += (_, _) => ToggleFolderAnimated(controller);
        expandedButton.Click += (_, _) => ToggleFolderAnimated(controller);

        ApplyFolderStateInstant(controller);

        _railFolderVisuals.Add(new RailFolderVisual(tile, node.Children.ToList()));
        TagFolderTutorialTarget(container, node);
        return container;
    }

    private static void ApplyFolderStateInstant(RailFolderEntryController c)
    {
        if (c.Node.Expanded)
        {
            c.ContainerBackgroundBrush.Color = Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF);
            c.ContainerBorderBrush.Color = Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF);

            c.CollapsedVisual.Visibility = Visibility.Collapsed;
            c.CollapsedVisual.Opacity = 0.0;
            c.CollapsedVisual.IsHitTestVisible = false;

            c.ExpandedVisual.Visibility = Visibility.Visible;
            c.ExpandedVisual.Opacity = 1.0;
            c.ExpandedVisual.IsHitTestVisible = true;

            c.Tray.Visibility = Visibility.Visible;
            c.Tray.Opacity = 1.0;
            c.Tray.Height = double.NaN;
            c.Tray.IsHitTestVisible = true;
            c.TrayTransform.Y = 0.0;

            c.DragRef.GhostSource = c.ExpandedButton;
        }
        else
        {
            c.ContainerBackgroundBrush.Color = Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF);
            c.ContainerBorderBrush.Color = Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF);

            c.CollapsedVisual.Visibility = Visibility.Visible;
            c.CollapsedVisual.Opacity = 1.0;
            c.CollapsedVisual.IsHitTestVisible = true;

            c.ExpandedVisual.Visibility = Visibility.Collapsed;
            c.ExpandedVisual.Opacity = 0.0;
            c.ExpandedVisual.IsHitTestVisible = false;

            c.Tray.Visibility = Visibility.Collapsed;
            c.Tray.Opacity = 0.0;
            c.Tray.Height = 0.0;
            c.Tray.IsHitTestVisible = false;
            c.TrayTransform.Y = -16.0;

            c.DragRef.GhostSource = c.CollapsedButton;
        }
    }

    private void ToggleFolderAnimated(RailFolderEntryController c)
    {
        if (c.IsAnimating) return;

        c.Node.Expanded = !c.Node.Expanded;
        if (_railLayout is not null) TrackingService.SaveRailLayout(_railLayout);

        if (TrackingService.ReduceUiEffects)
        {
            ApplyFolderStateInstant(c);
        }
        else
        {
            ApplyFolderStateAnimated(c);
        }

        UpdateRailFolderHighlights();
    }

    private void ApplyFolderStateAnimated(RailFolderEntryController c)
    {
        if (c.Node.Expanded)
        {
            // Expanding (unfold)
            c.ChildPanel.Measure(new Size(48, double.PositiveInfinity));
            double targetHeight = c.ChildPanel.DesiredSize.Height;
            if (targetHeight <= 0)
                targetHeight = Math.Max(48, c.ChildPanel.Children.Count * 48);

            double startHeight = c.Tray.ActualHeight > 0 && c.Tray.Visibility == Visibility.Visible
                ? c.Tray.ActualHeight
                : 0.0;

            c.Tray.Visibility = Visibility.Visible;
            c.Tray.IsHitTestVisible = false;

            c.ExpandedVisual.Visibility = Visibility.Visible;
            c.ExpandedVisual.IsHitTestVisible = false;
            c.CollapsedVisual.Visibility = Visibility.Visible;
            c.CollapsedVisual.IsHitTestVisible = false;
            c.IsAnimating = true;

            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
            var heightAnim = new DoubleAnimation(startHeight, targetHeight, new Duration(TimeSpan.FromMilliseconds(220)))
            {
                EasingFunction = easeOut
            };
            var trayOpacityAnim = new DoubleAnimation(c.Tray.Opacity, 1.0, new Duration(TimeSpan.FromMilliseconds(180)))
            {
                EasingFunction = easeOut
            };
            var slideAnim = new DoubleAnimation(c.TrayTransform.Y, 0.0, new Duration(TimeSpan.FromMilliseconds(220)))
            {
                EasingFunction = easeOut
            };

            var fadeOutCollapsed = new DoubleAnimation(c.CollapsedVisual.Opacity, 0.0, new Duration(TimeSpan.FromMilliseconds(120)));
            var fadeInExpanded = new DoubleAnimation(c.ExpandedVisual.Opacity, 1.0, new Duration(TimeSpan.FromMilliseconds(160)));

            var bgAnim = new ColorAnimation(c.ContainerBackgroundBrush.Color, Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF), new Duration(TimeSpan.FromMilliseconds(200)));
            var borderAnim = new ColorAnimation(c.ContainerBorderBrush.Color, Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF), new Duration(TimeSpan.FromMilliseconds(200)));

            heightAnim.Completed += (_, _) =>
            {
                if (!c.Node.Expanded) return;
                c.IsAnimating = false;

                c.Tray.Height = double.NaN;
                c.Tray.BeginAnimation(FrameworkElement.HeightProperty, null);

                c.Tray.Opacity = 1.0;
                c.Tray.BeginAnimation(UIElement.OpacityProperty, null);

                c.TrayTransform.Y = 0.0;
                c.TrayTransform.BeginAnimation(TranslateTransform.YProperty, null);

                c.CollapsedVisual.Opacity = 0.0;
                c.CollapsedVisual.BeginAnimation(UIElement.OpacityProperty, null);
                c.CollapsedVisual.Visibility = Visibility.Collapsed;

                c.ExpandedVisual.Opacity = 1.0;
                c.ExpandedVisual.BeginAnimation(UIElement.OpacityProperty, null);
                c.ExpandedVisual.IsHitTestVisible = true;

                c.ContainerBackgroundBrush.Color = Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF);
                c.ContainerBackgroundBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);

                c.ContainerBorderBrush.Color = Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF);
                c.ContainerBorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);

                c.Tray.IsHitTestVisible = true;
                c.DragRef.GhostSource = c.ExpandedButton;
            };

            c.Tray.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
            c.Tray.BeginAnimation(UIElement.OpacityProperty, trayOpacityAnim);
            c.TrayTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
            c.CollapsedVisual.BeginAnimation(UIElement.OpacityProperty, fadeOutCollapsed);
            c.ExpandedVisual.BeginAnimation(UIElement.OpacityProperty, fadeInExpanded);
            c.ContainerBackgroundBrush.BeginAnimation(SolidColorBrush.ColorProperty, bgAnim);
            c.ContainerBorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, borderAnim);
        }
        else
        {
            // Collapsing (fold)
            double startHeight = c.Tray.ActualHeight > 0
                ? c.Tray.ActualHeight
                : (c.ChildPanel.ActualHeight > 0 ? c.ChildPanel.ActualHeight : Math.Max(48, c.ChildPanel.Children.Count * 48));

            c.Tray.IsHitTestVisible = false;
            c.ExpandedVisual.IsHitTestVisible = false;
            c.CollapsedVisual.Visibility = Visibility.Visible;
            c.CollapsedVisual.IsHitTestVisible = false;
            c.IsAnimating = true;

            var easeInOut = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var heightAnim = new DoubleAnimation(startHeight, 0.0, new Duration(TimeSpan.FromMilliseconds(200)))
            {
                EasingFunction = easeInOut
            };
            var trayOpacityAnim = new DoubleAnimation(c.Tray.Opacity, 0.0, new Duration(TimeSpan.FromMilliseconds(150)))
            {
                EasingFunction = easeInOut
            };
            var slideAnim = new DoubleAnimation(c.TrayTransform.Y, -16.0, new Duration(TimeSpan.FromMilliseconds(200)))
            {
                EasingFunction = easeInOut
            };

            var fadeOutExpanded = new DoubleAnimation(c.ExpandedVisual.Opacity, 0.0, new Duration(TimeSpan.FromMilliseconds(120)));
            var fadeInCollapsed = new DoubleAnimation(c.CollapsedVisual.Opacity, 1.0, new Duration(TimeSpan.FromMilliseconds(160)));

            var bgAnim = new ColorAnimation(c.ContainerBackgroundBrush.Color, Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), new Duration(TimeSpan.FromMilliseconds(180)));
            var borderAnim = new ColorAnimation(c.ContainerBorderBrush.Color, Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), new Duration(TimeSpan.FromMilliseconds(180)));

            heightAnim.Completed += (_, _) =>
            {
                if (c.Node.Expanded) return;
                c.IsAnimating = false;

                c.Tray.Height = 0.0;
                c.Tray.BeginAnimation(FrameworkElement.HeightProperty, null);
                c.Tray.Visibility = Visibility.Collapsed;

                c.Tray.Opacity = 0.0;
                c.Tray.BeginAnimation(UIElement.OpacityProperty, null);

                c.TrayTransform.Y = -16.0;
                c.TrayTransform.BeginAnimation(TranslateTransform.YProperty, null);

                c.ExpandedVisual.Opacity = 0.0;
                c.ExpandedVisual.BeginAnimation(UIElement.OpacityProperty, null);
                c.ExpandedVisual.Visibility = Visibility.Collapsed;

                c.CollapsedVisual.Opacity = 1.0;
                c.CollapsedVisual.BeginAnimation(UIElement.OpacityProperty, null);
                c.CollapsedVisual.IsHitTestVisible = true;

                c.ContainerBackgroundBrush.Color = Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF);
                c.ContainerBackgroundBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);

                c.ContainerBorderBrush.Color = Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF);
                c.ContainerBorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);

                c.DragRef.GhostSource = c.CollapsedButton;
                UpdateRailFolderHighlights();
            };

            c.Tray.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
            c.Tray.BeginAnimation(UIElement.OpacityProperty, trayOpacityAnim);
            c.TrayTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
            c.ExpandedVisual.BeginAnimation(UIElement.OpacityProperty, fadeOutExpanded);
            c.CollapsedVisual.BeginAnimation(UIElement.OpacityProperty, fadeInCollapsed);
            c.ContainerBackgroundBrush.BeginAnimation(SolidColorBrush.ColorProperty, bgAnim);
            c.ContainerBorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, borderAnim);
        }
    }

    private FrameworkElement BuildMiniIcon(RailTabInfo info)
    {
        if (info.GeometryPath is { } geo)
            return new Viewbox
            {
                Width = 14, Height = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Path { Data = Geometry.Parse(geo), Stretch = Stretch.Uniform, Fill = MiniIconBrush },
            };

        return new WpfUi.SymbolIcon
        {
            Symbol = info.Symbol,
            FontSize = 14,
            Foreground = MiniIconBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private Popup BuildFolderPopover(FrameworkElement target)
    {
        var popup = new Popup
        {
            AllowsTransparency = true,
            HorizontalOffset = 8,
            IsHitTestVisible = false,
            Placement = PlacementMode.Right,
            PlacementTarget = target,
            PopupAnimation = PopupAnimation.Fade,
            StaysOpen = true,
        };
        popup.SetBinding(Popup.IsOpenProperty,
            new Binding(nameof(UIElement.IsMouseOver)) { Source = target, Mode = BindingMode.OneWay });
        popup.Opened += SidebarTabPopover_Opened;
        popup.Child = new ContentControl
        {
            Content = target,
            ContentTemplate = (DataTemplate)RailItemsHost.FindResource("SidebarTabPopoverTemplate"),
        };
        return popup;
    }

    /// <summary>Highlights a collapsed folder tile when one of its tabs is the current selection.</summary>
    private void UpdateRailFolderHighlights()
    {
        if (MainTabs?.SelectedItem is not TabItem selected) return;

        string? selectedId = RailCatalog.All
            .FirstOrDefault(i => ResolveTabItem(i) == selected)?.Id;

        foreach (var visual in _railFolderVisuals)
        {
            bool active = selectedId is not null && visual.ChildIds.Contains(selectedId);
            if (active)
            {
                visual.Tile.Background = FolderTileActiveBrush;
                visual.Tile.BorderBrush = FolderTileActiveBorder;
            }
            else
            {
                visual.Tile.Background = FolderTileIdleBrush;
                visual.Tile.BorderBrush = FolderTileIdleBorder;
            }
        }
    }

    /// <summary>Marks the default "Tools" folder as the anchor for the one-time folders tutorial.</summary>
    private static void TagFolderTutorialTarget(FrameworkElement element, RailNodeData node)
    {
        if (node.FolderId == RailCatalog.DefaultFolderId)
            Features.Tutorials.Tutorial.SetTargetId(element, "Sidebar.ToolsFolder");
    }
}
