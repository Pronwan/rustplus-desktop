using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    // ─── STATE ──────────────────────────────────────────────────────────────

    private readonly Dictionary<string, Border> _groupMarkerEls = new();
    private string? _pendingPinGroupId;
    private Cursor? _savedCursorBeforePinning;
    private bool _markersInitialized;

    /// <summary>Called once from InitTrackerTab to set up the marker subsystem.</summary>
    private void InitGroupMarkers()
    {
        if (_markersInitialized) return;
        _markersInitialized = true;
        // Refresh markers (and tooltip text) whenever online status changes.
        TrackingService.OnOnlinePlayersUpdated += () => Dispatcher.Invoke(UpdateAllMarkerTooltips);
    }

    /// <summary>Hooked into LoadMapAsync after BuildMonumentOverlays so markers appear when map loads.</summary>
    private void RebuildGroupMarkers()
    {
        if (Overlay == null) return;

        foreach (var existing in _groupMarkerEls.Values)
            Overlay.Children.Remove(existing);
        _groupMarkerEls.Clear();

        var serverName = _vm?.Selected?.Name;
        if (string.IsNullOrEmpty(serverName)) return;
        if (_worldSizeS <= 0) return; // map not loaded

        var pins = PlayerGroupsService.GetMapPinsForServer(serverName);
        foreach (var (group, pin) in pins)
        {
            var el = CreateGroupMarkerElement(group, pin);
            Overlay.Children.Add(el);
            Panel.SetZIndex(el, 850);

            var p = WorldToImagePx(pin.X, pin.Y);
            const double r = 14.0;
            Canvas.SetLeft(el, p.X - r);
            Canvas.SetTop(el, p.Y - r);

            ApplyCurrentOverlayScale(el);
            _groupMarkerEls[group.Id] = el;
        }
    }

    /// <summary>Just updates each marker's tooltip text (cheap; no re-layout).</summary>
    private void UpdateAllMarkerTooltips()
    {
        foreach (var kv in _groupMarkerEls)
        {
            var group = PlayerGroupsService.Groups.FirstOrDefault(g => g.Id == kv.Key);
            if (group == null) continue;
            ToolTipService.SetToolTip(kv.Value, BuildGroupMarkerTooltip(group));
        }
    }

    private Border CreateGroupMarkerElement(PlayerGroup g, GroupMapPin pin)
    {
        var letter = string.IsNullOrEmpty(g.Name) ? "?" : g.Name.Substring(0, 1).ToUpper();
        var dot = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = BrushFromHex(g.ColorHex),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            Tag = g.Id,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = letter,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 2,
                    ShadowDepth = 0,
                    Color = Colors.Black,
                    Opacity = 0.7
                }
            }
        };
        ToolTipService.SetToolTip(dot, BuildGroupMarkerTooltip(g));
        ToolTipService.SetInitialShowDelay(dot, 200);
        return dot;
    }

    private FrameworkElement BuildGroupMarkerTooltip(PlayerGroup g)
    {
        var sp = new StackPanel { MinWidth = 200, Margin = new Thickness(4) };
        var subtleBrush = (Brush?)TryFindResource("TextSubtle") ?? Brushes.Gray;
        var textBrush = (Brush?)TryFindResource("TextPrimary") ?? Brushes.White;

        sp.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new Border
                {
                    Width = 12, Height = 12, CornerRadius = new CornerRadius(2),
                    Background = BrushFromHex(g.ColorHex),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                },
                new TextBlock
                {
                    Text = g.Name,
                    FontWeight = FontWeights.Bold,
                    FontSize = 13,
                    Foreground = textBrush,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        });

        var online = TrackingService.LastOnlinePlayers.ToDictionary(p => p.BMId, p => p);
        var tracked = TrackingService.GetTrackedPlayers().ToDictionary(p => p.BMId, p => p);
        int onlineCount = g.BMIds.Count(id => online.ContainsKey(id));
        int offlineCount = g.BMIds.Count - onlineCount;

        sp.Children.Add(new TextBlock
        {
            Text = g.BMIds.Count == 0
                ? "(no members)"
                : $"{onlineCount} online · {offlineCount} offline",
            Foreground = subtleBrush,
            FontSize = 10,
            Margin = new Thickness(0, 2, 0, 6)
        });

        var ordered = g.BMIds
            .Select(id =>
            {
                bool isOn = online.ContainsKey(id);
                string name = isOn ? online[id].Name
                    : tracked.TryGetValue(id, out var t) ? t.Name
                    : "(unknown)";
                string time = isOn ? $" · {online[id].PlayTimeStr}" : "";
                return (isOn, name, time);
            })
            .OrderByDescending(x => x.isOn)
            .ThenBy(x => x.name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var (isOn, name, time) in ordered)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            line.Children.Add(new Ellipse
            {
                Width = 7, Height = 7,
                Fill = isOn ? Brushes.LimeGreen
                    : new SolidColorBrush(Color.FromRgb(0x55, 0x5a, 0x60)),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            line.Children.Add(new TextBlock
            {
                Text = name + time,
                Foreground = isOn ? textBrush : subtleBrush,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });
            sp.Children.Add(line);
        }

        // Wrap in a styled ToolTip so the popup gets a dark background.
        var tt = new ToolTip
        {
            Content = sp,
            Background = (Brush?)TryFindResource("Surface") ?? new SolidColorBrush(Color.FromRgb(0x1C, 0x1F, 0x24)),
            BorderBrush = (Brush?)TryFindResource("CardBorderBrush") ?? new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x48)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            HasDropShadow = true
        };
        return tt;
    }

    // ─── PIN PLACEMENT MODE ─────────────────────────────────────────────────

    private void EnterPinPlacementMode(string groupId)
    {
        if (_vm?.Selected == null || string.IsNullOrEmpty(_vm.Selected.Name))
        {
            AppendLog("[group] Cannot pin: connect to a server first.");
            return;
        }
        if (_worldSizeS <= 0)
        {
            AppendLog("[group] Cannot pin: map not loaded yet.");
            return;
        }

        _pendingPinGroupId = groupId;
        if (Overlay != null)
        {
            _savedCursorBeforePinning = Overlay.Cursor;
            Overlay.Cursor = Cursors.Cross;
            // Use Preview event so we beat the pan/zoom handler on WebViewHost.
            Overlay.PreviewMouseLeftButtonDown += Overlay_PinPlacementClick;
        }

        var group = PlayerGroupsService.Groups.FirstOrDefault(g => g.Id == groupId);
        AppendLog($"[group] Click on the map to pin \"{group?.Name ?? "group"}\". Press Esc to cancel.");
        // Listen for Esc to cancel.
        this.PreviewKeyDown += PinPlacement_KeyDown;
    }

    private void ExitPinPlacementMode()
    {
        _pendingPinGroupId = null;
        if (Overlay != null)
        {
            Overlay.PreviewMouseLeftButtonDown -= Overlay_PinPlacementClick;
            Overlay.Cursor = _savedCursorBeforePinning;
            _savedCursorBeforePinning = null;
        }
        this.PreviewKeyDown -= PinPlacement_KeyDown;
    }

    private void PinPlacement_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _pendingPinGroupId != null)
        {
            AppendLog("[group] Pin placement cancelled.");
            ExitPinPlacementMode();
            e.Handled = true;
        }
    }

    private void Overlay_PinPlacementClick(object? sender, MouseButtonEventArgs e)
    {
        if (_pendingPinGroupId == null) return;

        var hostPos = e.GetPosition(WebViewHost);
        var mapPos = HostToScenePreTransform(hostPos);
        var world = ImagePxToWorld(mapPos.X, mapPos.Y);

        var serverName = _vm?.Selected?.Name;
        if (string.IsNullOrEmpty(serverName))
        {
            ExitPinPlacementMode();
            return;
        }

        var groupId = _pendingPinGroupId;
        PlayerGroupsService.SetMapPin(groupId, serverName, world.X, world.Y);
        var group = PlayerGroupsService.Groups.FirstOrDefault(g => g.Id == groupId);
        AppendLog($"[group] Pinned \"{group?.Name ?? "group"}\" at ({world.X:F0}, {world.Y:F0}) on {serverName}.");

        e.Handled = true;
        ExitPinPlacementMode();
        RebuildGroupMarkers();
    }

    // ─── Group sub-tab buttons ───────────────────────────────────────────────

    private void BtnGroupPin_Click(object sender, RoutedEventArgs e)
    {
        if (CmbGroupSelector?.SelectedItem is not PlayerGroup g) return;
        EnterPinPlacementMode(g.Id);
    }

    private void BtnGroupUnpin_Click(object sender, RoutedEventArgs e)
    {
        if (CmbGroupSelector?.SelectedItem is not PlayerGroup g) return;
        var serverName = _vm?.Selected?.Name;
        if (string.IsNullOrEmpty(serverName))
        {
            AppendLog("[group] Cannot unpin: no server selected.");
            return;
        }
        if (PlayerGroupsService.ClearMapPin(g.Id, serverName))
        {
            AppendLog($"[group] Removed pin for \"{g.Name}\" on {serverName}.");
            RebuildGroupMarkers();
        }
    }
}
