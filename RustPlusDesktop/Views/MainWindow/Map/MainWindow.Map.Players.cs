using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private const double PlayerAvatarSize = 24;

    private double _playerMarkerScale = 1.0;
    private bool _abbreviateNames = false;

    private string GetDisplayPlayerName(string name)
    {
        if (!_abbreviateNames || string.IsNullOrWhiteSpace(name)) return name;
        return name.Substring(0, 1) + "...";
    }

    private sealed class PlayerMarkerTag
    {
        public ulong SteamId;
        public TextBlock NameText = null!;
        public string? Name { get; set; }
        public Ellipse? AvatarCircle;
        public Path? ArrowPath;
        public double Radius;
        public bool IsDeathPin { get; set; }
        public bool IsPlayer { get; set; }
        public bool IsDot;
        public bool HasAvatar;

        public double ScaleExp { get; set; } = SHOP_SIZE_EXP;
        public double ScaleBaseMult { get; set; } = 1.0;
        public FrameworkElement? ScaleTarget { get; set; }
        public FrameworkElement? RotationTarget { get; set; }
        public double ScaleCenterX { get; set; }
        public double ScaleCenterY { get; set; }
        public double Rotation { get; set; }
        public TextBlock? TimerText { get; set; }
        public FrameworkElement? TimerContainer { get; set; }
    }

    private readonly HashSet<ulong> _avatarLoading = new();
    private readonly Dictionary<ulong, DateTime> _avatarNextTry = new();
    private static readonly TimeSpan AvatarRetryInterval = TimeSpan.FromSeconds(30);

    private const double PinW = 40;
    private const double PinH = 56;
    private const double Circle = 24;
    private const double CircleTop = 6;

    private const double SHOP_SIZE_EXP = 0.8;

    private ImageSource? GetAvatar(ulong sid)
        => TeamMembers.FirstOrDefault(t => t.SteamId == sid)?.Avatar;

    private ImageSource? GetAvatarForMap(ulong sid)
        => _showProfileMarkers ? GetTeamAvatar(sid) : null;

    private ImageSource? GetTeamAvatar(ulong sid)
    {
        var vm = TeamMembers.FirstOrDefault(t => t.SteamId == sid);
        if (vm?.Avatar != null) return vm.Avatar;

        if (_avatarCache.TryGetValue(sid, out var img) && img != null)
            return img;
        return null;
    }

    private FrameworkElement BuildPlayerDotMarker(ulong sid, string name, bool online, bool dead)
    {
        var isSelf = sid == _mySteamId;
        var brush = dead ? Brushes.IndianRed : (online ? (isSelf ? Brushes.ForestGreen : Brushes.LimeGreen) : Brushes.LightGray);

        var dot = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = brush,
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var arrow = new Path
        {
            Data = Geometry.Parse("M 14,0 L 17,5 L 11,5 Z"),
            Fill = brush,
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = _showPlayerArrows ? Visibility.Visible : Visibility.Collapsed
        };

        var displayName = GetDisplayPlayerName(name);
        var tb = new TextBlock
        {
            Text = displayName,
            Foreground = brush,
            FontSize = 12,
            Margin = new Thickness(6, -2, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        // Dot + arrow stacked in a small 28x28 grid (same layout as BuildPlayerMarker)
        var dotContainer = new Grid
        {
            Width = 14,
            Height = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        dotContainer.Children.Add(dot);

        var arrowContainer = new Grid
        {
            Width = 28,
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        arrowContainer.Children.Add(arrow);

        var markerContainer = new Grid { Width = 28, Height = 28, Margin = new Thickness(0, 0, 4, 0) };
        markerContainer.Children.Add(dotContainer);
        markerContainer.Children.Add(arrowContainer);

        var host = new Grid();
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        host.Children.Add(markerContainer);
        Grid.SetColumn(markerContainer, 0);
        host.Children.Add(tb);
        Grid.SetColumn(tb, 1);

        ToolTipService.SetToolTip(host, name);

        host.Tag = new PlayerMarkerTag
        {
            SteamId = sid,
            Name = name,
            NameText = tb,
            AvatarCircle = dot,
            ArrowPath = arrow,
            RotationTarget = arrowContainer,
            Radius = 14,
            IsDeathPin = false,
            IsPlayer = true,
            IsDot = true,
            ScaleExp = 0.85,
            ScaleBaseMult = 1.0,
            ScaleTarget = host,
            ScaleCenterX = 14.0,
            ScaleCenterY = 14.0
        };

        Panel.SetZIndex(host, 905);
        ApplyCurrentOverlayScale(host);
        return host;
    }

    private FrameworkElement BuildPlayerMarker(ulong sid, string name, bool online, bool dead)
    {
        var isSelf = sid == _mySteamId;
        var brush = dead ? Brushes.IndianRed : (online ? (isSelf ? Brushes.ForestGreen : Brushes.LimeGreen) : Brushes.Gray);
        var avatar = GetAvatar(sid);

        if (avatar == null)
        {
            var displayName = GetDisplayPlayerName(name);
            var tb = new TextBlock { Text = displayName, Foreground = brush, FontSize = 12, Margin = new Thickness(6, -2, 0, 0) };
            
            var circle = new Ellipse
            {
                Width = PlayerAvatarSize,
                Height = PlayerAvatarSize,
                Stroke = Brushes.Black,
                StrokeThickness = 2,
                Fill = brush
            };

            var arrow = new Path
            {
                Data = Geometry.Parse("M 14,0 L 17,5 L 11,5 Z"),
                Fill = brush,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Visibility = _showPlayerArrows ? Visibility.Visible : Visibility.Collapsed
            };

            var host = new Grid();
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var markerContainer = new Grid { Width = 28, Height = 28, Margin = new Thickness(0, 0, 4, 0) };
            
            var circleHost = new Grid
            {
                Width = PlayerAvatarSize,
                Height = PlayerAvatarSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            circleHost.Children.Add(circle);

            var arrowContainer = new Grid
            {
                Width = 28,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            arrowContainer.Children.Add(arrow);

            markerContainer.Children.Add(circleHost);
            markerContainer.Children.Add(arrowContainer);

            host.Children.Add(markerContainer);
            Grid.SetColumn(markerContainer, 0);
            host.Children.Add(tb);
            Grid.SetColumn(tb, 1);

            host.Tag = new PlayerMarkerTag
            {
                SteamId = sid,
                NameText = tb,
                AvatarCircle = circle,
                ArrowPath = arrow,
                Radius = 14.0,
                IsPlayer = true,
                IsDot = false,
                HasAvatar = false,
                ScaleExp = 0.85,
                ScaleBaseMult = 1.0,
                ScaleTarget = host,
                RotationTarget = arrowContainer,
                ScaleCenterX = 14.0,
                ScaleCenterY = 14.0,
            };
            Panel.SetZIndex(host, 905);
            ToolTipService.SetToolTip(host, name);
            ApplyCurrentOverlayScale(host);
            return host;
        }
        else
        {
            var displayName = GetDisplayPlayerName(name);
            var tb = new TextBlock { Text = displayName, Foreground = brush, FontSize = 12, Margin = new Thickness(6, -2, 0, 0) };
            var circle = new Ellipse
            {
                Width = PlayerAvatarSize,
                Height = PlayerAvatarSize,
                Stroke = Brushes.Black,
                StrokeThickness = 2,
                Fill = new ImageBrush(avatar) { Stretch = Stretch.UniformToFill }
            };

            var arrow = new Path
            {
                Data = Geometry.Parse("M 14,0 L 17,5 L 11,5 Z"),
                Fill = brush,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Visibility = _showPlayerArrows ? Visibility.Visible : Visibility.Collapsed
            };

            var host = new Grid();
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var markerContainer = new Grid { Width = 28, Height = 28, Margin = new Thickness(0, 0, 4, 0) };

            var avatarHost = new Grid
            {
                Width = PlayerAvatarSize,
                Height = PlayerAvatarSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            avatarHost.Children.Add(circle);

            var arrowContainer = new Grid
            {
                Width = 28,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            arrowContainer.Children.Add(arrow);

            markerContainer.Children.Add(avatarHost);
            markerContainer.Children.Add(arrowContainer);

            host.Children.Add(markerContainer);
            Grid.SetColumn(markerContainer, 0);
            host.Children.Add(tb);
            Grid.SetColumn(tb, 1);

            host.Tag = new PlayerMarkerTag
            {
                SteamId = sid,
                NameText = tb,
                AvatarCircle = circle,
                ArrowPath = arrow,
                Radius = 14.0,
                IsPlayer = true,
                IsDot = false,
                HasAvatar = true,
                ScaleExp = 0.85,
                ScaleBaseMult = 1.0,
                ScaleTarget = host,
                RotationTarget = arrowContainer,
                ScaleCenterX = 14.0,
                ScaleCenterY = 14.0,
            };
            Panel.SetZIndex(host, 905);
            ToolTipService.SetToolTip(host, name);
            ApplyCurrentOverlayScale(host);

            return host;
        }
    }

    private bool CanTryAvatar(ulong sid)
    {
        if (_avatarLoading.Contains(sid)) return false;
        return !_avatarNextTry.TryGetValue(sid, out var next) || DateTime.UtcNow >= next;
    }

    private FrameworkElement MakePlayerDot(string tooltip, bool online)
    {
        var dot = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = online ? Brushes.LimeGreen : Brushes.LightGray,
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            Margin = new Thickness(0, 0, 4, 0),
        };
        ToolTipService.SetToolTip(dot, tooltip);
        Panel.SetZIndex(dot, 905);
        return dot;
    }

    private void UpdatePlayerMarker(ref FrameworkElement el, uint key, ulong sid, string name, bool online, bool dead)
    {
        if (sid == 0) return;
        var displayName = GetDisplayPlayerName(name);

        if (!_showProfileMarkers)
        {
            var isSelf = sid == _mySteamId;
            var brush = dead ? Brushes.IndianRed : (online ? (isSelf ? Brushes.ForestGreen : Brushes.LimeGreen) : Brushes.LightGray);

            if (el.Tag is not PlayerMarkerTag t || !t.IsDot)
            {
                var newEl = BuildPlayerDotMarker(sid, name, online, dead);
                int idx = Overlay.Children.IndexOf(el);
                if (idx >= 0) { Overlay.Children.RemoveAt(idx); Overlay.Children.Insert(idx, newEl); }
                else Overlay.Children.Add(newEl);
                _dynEls[key] = newEl; el = newEl;
                Panel.SetZIndex(newEl, 905);
            }
            else
            {
                t.NameText.Text = displayName;
                t.NameText.Foreground = brush;
                if (t.AvatarCircle != null) t.AvatarCircle.Fill = brush;
                if (t.ArrowPath != null)
                {
                    t.ArrowPath.Fill = brush;
                    t.ArrowPath.Visibility = _showPlayerArrows ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            ToolTipService.SetToolTip(el, name);
            return;
        }

        var isSelf2 = sid == _mySteamId;
        var brush2 = dead ? Brushes.IndianRed : (online ? (isSelf2 ? Brushes.ForestGreen : Brushes.LimeGreen) : Brushes.LightGray);
        var avatar = GetAvatarForMap(sid);

        if (el.Tag is PlayerMarkerTag tag)
        {
            if (tag.NameText != null) tag.NameText.Text = displayName;
            if (tag.NameText != null) tag.NameText.Foreground = brush2;

            if (tag.ArrowPath != null)
            {
                tag.ArrowPath.Visibility = _showPlayerArrows ? Visibility.Visible : Visibility.Collapsed;
            }

            bool needsRebuild = false;
            if (tag.IsDot)
            {
                needsRebuild = true;
            }
            else
            {
                if (avatar != null && !tag.HasAvatar)
                {
                    needsRebuild = true;
                }
                else if (avatar == null && tag.HasAvatar)
                {
                    needsRebuild = true;
                }
            }

            if (needsRebuild)
            {
                var newEl = BuildPlayerMarker(sid, name, online, dead);
                int idx = Overlay.Children.IndexOf(el);
                if (idx >= 0) { Overlay.Children.RemoveAt(idx); Overlay.Children.Insert(idx, newEl); }
                else Overlay.Children.Add(newEl);
                _dynEls[key] = newEl; el = newEl;
            }
            else if (avatar != null && !tag.IsDot && tag.AvatarCircle != null)
            {
                tag.AvatarCircle.Fill = new ImageBrush(avatar) { Stretch = Stretch.UniformToFill };
            }
            else if (avatar == null && !tag.IsDot && tag.AvatarCircle != null)
            {
                tag.AvatarCircle.Fill = brush2;
            }
        }
        else
        {
            var newEl = BuildPlayerMarker(sid, name, online, dead);
            int idx = Overlay.Children.IndexOf(el);
            if (idx >= 0) { Overlay.Children.RemoveAt(idx); Overlay.Children.Insert(idx, newEl); }
            else Overlay.Children.Add(newEl);
            _dynEls[key] = newEl; el = newEl;
        }
    }

    private void ChkProfileMarkers_Toggled(object? sender, RoutedEventArgs e)
    {
        _showProfileMarkers = ChkProfileMarkers.IsChecked == true;
        if (_vm != null && !_vm.IsInitializing) TrackingService.MapShowSteamMarkers = _showProfileMarkers;

        foreach (var kv in _dynEls.ToList())
        {
            if (kv.Value is FrameworkElement el && el.Tag is PlayerMarkerTag tag)
            {
                if (tag.SteamId == 0 || tag.IsDeathPin) continue;
                var sid = tag.SteamId;
                var name = TeamMembers.FirstOrDefault(t => t.SteamId == sid)?.Name ?? "player";
                if (_lastPresence.TryGetValue(sid, out var p))
                {
                    var online = p.Item1;
                    var dead = p.Item2;
                    UpdatePlayerMarker(ref el, kv.Key, sid, name, online, dead);
                }
                else
                {
                    UpdatePlayerMarker(ref el, kv.Key, sid, name, online: false, dead: false);
                }
            }
        }
    }

    private void ChkPlayerArrows_Toggled(object? sender, RoutedEventArgs e)
    {
        _showPlayerArrows = ChkPlayerArrows.IsChecked == true;
        if (_vm != null && !_vm.IsInitializing) TrackingService.MapShowPlayerArrows = _showPlayerArrows;

        foreach (var kv in _dynEls.ToList())
        {
            if (kv.Value is FrameworkElement el && el.Tag is PlayerMarkerTag tag)
            {
                if (tag.SteamId == 0 || tag.IsDeathPin) continue;
                
                if (tag.ArrowPath != null)
                {
                    tag.ArrowPath.Visibility = _showPlayerArrows ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
    }

    private void ChkDeathMarkers_Toggled(object? sender, RoutedEventArgs e)
    {
        _showDeathMarkers = ChkDeathMarkers.IsChecked == true;
        if (_vm != null && !_vm.IsInitializing) TrackingService.MapShowDeathTags = _showDeathMarkers;
        
        RedrawDeathPins();
    }

    private void BtnWipeDeathMarkers_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.Selected != null)
        {
            _vm.Selected.DeathMarkers.Clear();
            _vm.Save();
            RedrawDeathPins();
        }
    }

    private void BtnDeathMarkerSettings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Views.Windows.DeathMarkerSettingsDialog(TrackingService.MaxSelfDeathMarkers, TrackingService.MaxTeamDeathMarkers);
        dlg.Owner = this;
        if (dlg.ShowDialog() == true)
        {
            TrackingService.MaxSelfDeathMarkers = dlg.MaxSelf;
            TrackingService.MaxTeamDeathMarkers = dlg.MaxTeam;
            
            if (_vm?.Selected != null)
            {
                var mySid = TrackingService.SteamId64;
                var markers = _vm.Selected.DeathMarkers;
                
                if (dlg.WipeAll)
                {
                    markers.Clear();
                }
                else if (!string.IsNullOrEmpty(mySid) && ulong.TryParse(mySid, out var sidNum))
                {
                    var myMarkers = markers.Where(m => m.SteamId == sidNum).OrderByDescending(m => m.TimeOfDeath).ToList();
                    while (myMarkers.Count > TrackingService.MaxSelfDeathMarkers)
                    {
                        var oldest = myMarkers.Last();
                        markers.Remove(oldest);
                        myMarkers.Remove(oldest);
                    }
                }
                
                var teamGroups = markers.Where(m => m.SteamId.ToString() != mySid).GroupBy(m => m.SteamId).ToList();
                foreach (var group in teamGroups)
                {
                    var teamMarkers = group.OrderByDescending(m => m.TimeOfDeath).ToList();
                    while (teamMarkers.Count > TrackingService.MaxTeamDeathMarkers)
                    {
                        var oldest = teamMarkers.Last();
                        markers.Remove(oldest);
                        teamMarkers.Remove(oldest);
                    }
                }
                
                _vm.Save();
                RedrawDeathPins();
            }
        }
    }
    private void RefreshAllOverlayScales()
    {
        foreach (var fe in _dynEls.Values)
            ApplyCurrentOverlayScale(fe);

        foreach (var fe in _deathPins.Values)
            ApplyCurrentOverlayScale(fe);

        RefreshShopIconScales();
    }

    private FrameworkElement BuildDeathPin(Guid id, ulong steamId, string label)
    {
        var avatar = GetTeamAvatar(steamId);

        var root = new Grid
        {
            Width = PinW,
            Height = PinH,
            Background = Brushes.Transparent,

            Tag = new PlayerMarkerTag
            {
                SteamId = steamId,
                Name = label,
                IsDeathPin = true,
                ScaleExp = 0.8,
                ScaleBaseMult = 0.72,
                ScaleTarget = null,
                ScaleCenterX = PinW * 0.5,
                ScaleCenterY = PinH
            }
        };

        var pinPath = Geometry.Parse(
            "M20,0 C31,0 40,9 40,20 C40,33 20,56 20,56 C20,56 0,33 0,20 C0,9 9,0 20,0 Z"
        );

        var fill = TryFindResource("DeathPinFill") as Brush ?? Brushes.IndianRed;

        root.Children.Add(new Path
        {
            Data = pinPath,
            Fill = fill,
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            Stretch = Stretch.Fill,
            Width = PinW,
            Height = PinH
        });

        root.Children.Add(new Ellipse
        {
            Width = Circle + 6,
            Height = Circle + 6,
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness((PinW - (Circle + 6)) / 2.0, CircleTop - 3, 0, 0)
        });

        FrameworkElement avatarEl;
        if (avatar != null)
        {
            var holder = new Grid { Width = Circle, Height = Circle };
            holder.Clip = new EllipseGeometry(new Point(Circle / 2.0, Circle / 2.0), Circle / 2.0, Circle / 2.0);
            holder.Children.Add(new Image { Source = avatar, Stretch = Stretch.UniformToFill });
            avatarEl = holder;
        }
        else
        {
            avatarEl = new Ellipse { Width = Circle, Height = Circle, Fill = Brushes.Gray };
        }

        avatarEl.HorizontalAlignment = HorizontalAlignment.Left;
        avatarEl.VerticalAlignment = VerticalAlignment.Top;
        avatarEl.Margin = new Thickness((PinW - Circle) / 2.0, CircleTop, 0, 0);
        root.Children.Add(avatarEl);

        ToolTipService.SetToolTip(root, label);
        
        var cm = new ContextMenu { Style = TryFindResource("DarkContextMenu") as Style };
        var miRename = new MenuItem { Header = Properties.Resources.RenameDeathMarker ?? "Umbenennen", Tag = id };
        miRename.Click += RenameDeathMarker_Click;
        cm.Items.Add(miRename);

        var miDelete = new MenuItem { Header = Properties.Resources.DeleteDeathMarker ?? "Löschen", Tag = id, Foreground = Brushes.Red };
        miDelete.Click += DeleteDeathMarker_Click;
        cm.Items.Add(miDelete);

        root.ContextMenu = cm;
        root.MouseRightButtonDown += (s, e) => 
        {
            cm.PlacementTarget = root;
            cm.IsOpen = true;
            e.Handled = true;
        };
        // MUST allow hit testing for context menu to work
        root.IsHitTestVisible = true;

        ApplyCurrentOverlayScale(root);
        return root;
    }

    private void RedrawDeathPins()
    {
        ClearAllDeathPins();
        
        var hasMarkers = _vm?.Selected?.DeathMarkers?.Count > 0;
        if (WipeDeathMarkersOverlay != null)
        {
            WipeDeathMarkersOverlay.Visibility = _showDeathMarkers && hasMarkers ? Visibility.Visible : Visibility.Collapsed;
        }

        if (!_showDeathMarkers || _vm?.Selected == null) return;
        var groups = _vm.Selected.DeathMarkers.GroupBy(m => m.SteamId);
        foreach (var group in groups)
        {
            var sorted = group.OrderBy(m => m.TimeOfDeath).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                var m = sorted[i];
                var px = WorldToImagePx(m.X, m.Y);
                int number = i + 1;
                
                string displayName = string.IsNullOrEmpty(m.CustomName) ? m.OriginalName : m.CustomName;
                string label = $"{number}. {displayName} ({m.TimeOfDeath:HH:mm})";
                
                var el = BuildDeathPin(m.Id, m.SteamId, label);
                _deathPins[m.Id] = el;
                Overlay.Children.Add(el);
                Panel.SetZIndex(el, 805);
                ApplyCurrentOverlayScale(el);
                var cx = px.X - (PinW / 2.0);
                var cy = px.Y - PinH;
                Canvas.SetLeft(el, cx);
                Canvas.SetTop(el, cy);
            }
        }
    }

    private void RenameDeathMarker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is Guid id && _vm?.Selected != null)
        {
            var marker = _vm.Selected.DeathMarkers.FirstOrDefault(m => m.Id == id);
            if (marker != null)
            {
                var prompt = new Views.Windows.PromptDialog(
                    Properties.Resources.RenameDeathMarker ?? "Rename Death Marker", 
                    string.IsNullOrEmpty(marker.CustomName) ? marker.OriginalName : marker.CustomName)
                {
                    Owner = this
                };
                if (prompt.ShowDialog() == true)
                {
                    marker.CustomName = string.IsNullOrWhiteSpace(prompt.InputText) ? null : prompt.InputText;
                    _vm.Save();
                    RedrawDeathPins();
                }
            }
        }
    }

    private void DeleteDeathMarker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is Guid id && _vm?.Selected != null)
        {
            var marker = _vm.Selected.DeathMarkers.FirstOrDefault(m => m.Id == id);
            if (marker != null)
            {
                _vm.Selected.DeathMarkers.Remove(marker);
                _vm.Save();
                RedrawDeathPins();
            }
        }
    }

    private void PlaceDeathPin(TeamMemberVM vm)
    {
        // deprecated, unused
    }

    private void PlaceOrMoveDeathPin(ulong sid, double worldX, double worldY, string name)
    {
        // This is called by legacy map code. We can convert it into a real marker.
        if (_vm?.Selected != null)
        {
            var list = _vm.Selected.DeathMarkers;
            var newMarker = new Models.DeathMarkerData
            {
                Id = Guid.NewGuid(),
                SteamId = sid,
                OriginalName = name,
                TimeOfDeath = DateTime.Now,
                X = worldX,
                Y = worldY
            };
            list.Add(newMarker);
            _vm.Save();
            Dispatcher.InvokeAsync(() => RedrawDeathPins());
        }
    }

    private void ClearAllDeathPins()
    {
        foreach (var kv in _deathPins) Overlay.Children.Remove(kv.Value);
        _deathPins.Clear();
    }

    private double GetEffectiveZoom()
    {
        var (s, _, _) = GetViewboxScaleAndOffset();
        var m = MapTransform.Matrix;
        double eff = Math.Abs(s * m.M11);
        return eff <= 1e-6 ? 1e-6 : eff;
    }

    private void ApplyCurrentOverlayScale(FrameworkElement el)
    {
        if (el == null) return;

        double eff = GetEffectiveZoom();
        double exp = SHOP_SIZE_EXP, baseMult = SHOP_BASE_MULT;

        FrameworkElement target = el;
        double centerX = -1.0, centerY = -1.0;
        FrameworkElement? rotationTarget = null;

        if (el.Tag is PlayerMarkerTag pt)
        {
            if (pt.ScaleExp > 0) exp = pt.ScaleExp;
            if (pt.ScaleBaseMult > 0) baseMult = pt.ScaleBaseMult;

            if (pt.IsDeathPin)
            {
                target = el;
                centerX = pt.ScaleCenterX;
                centerY = pt.ScaleCenterY;
            }
            else if (pt.ScaleTarget != null)
            {
                target = pt.ScaleTarget;
                centerX = pt.ScaleCenterX;
                centerY = pt.ScaleCenterY;

                if (!ReferenceEquals(target, el))
                    el.RenderTransform = Transform.Identity;
            }

            rotationTarget = pt.RotationTarget;
        }

        bool isPlayerOrDeathMarker = el.Tag is PlayerMarkerTag pmtScale && (pmtScale.IsPlayer || pmtScale.IsDeathPin);
        double scaleMultiplier = isPlayerOrDeathMarker ? _playerMarkerScale : 1.0;
        double scale = CalcOverlayScale(eff, exp, baseMult) * scaleMultiplier;
        double rotation = (el.Tag is PlayerMarkerTag ptRot) ? ptRot.Rotation : 0;

        if (centerX >= 0 && centerY >= 0)
        {
            double w = target.ActualWidth > 0 ? target.ActualWidth : target.Width;
            double h = target.ActualHeight > 0 ? target.ActualHeight : target.Height;

            if (w > 0 && h > 0)
            {
                target.RenderTransformOrigin = new Point(centerX / w, centerY / h);
            }
            else
            {
                target.RenderTransformOrigin = new Point(0.5, 0.5);
            }
        }
        else
        {
            target.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        bool shouldRotateParent = (rotationTarget == null);
        if (el.Tag is PlayerMarkerTag pmtCheck && (pmtCheck.IsPlayer || pmtCheck.IsDeathPin))
        {
            shouldRotateParent = false;
        }

        if (rotationTarget != null)
        {
            var st = target.RenderTransform as ScaleTransform;
            if (st == null)
            {
                target.RenderTransform = new ScaleTransform(scale, scale);
            }
            else
            {
                st.ScaleX = scale;
                st.ScaleY = scale;
            }

            rotationTarget.RenderTransformOrigin = new Point(0.5, 0.5);
            var rt = rotationTarget.RenderTransform as RotateTransform;
            if (rt == null)
            {
                rotationTarget.RenderTransform = new RotateTransform(rotation);
            }
            else
            {
                var source = DependencyPropertyHelper.GetValueSource(rt, RotateTransform.AngleProperty);
                if (!source.IsAnimated)
                {
                    rt.Angle = rotation;
                }
            }
        }
        else if (shouldRotateParent)
        {
            var group = target.RenderTransform as TransformGroup;
            if (group == null || group.Children.Count < 2 || !(group.Children[0] is ScaleTransform) || !(group.Children[1] is RotateTransform))
            {
                group = new TransformGroup();
                group.Children.Add(new ScaleTransform(scale, scale));
                group.Children.Add(new RotateTransform(rotation));
                target.RenderTransform = group;
            }
            else
            {
                var st = (ScaleTransform)group.Children[0];
                st.ScaleX = scale;
                st.ScaleY = scale;

                var rt = (RotateTransform)group.Children[1];
                var source = DependencyPropertyHelper.GetValueSource(rt, RotateTransform.AngleProperty);
                if (!source.IsAnimated)
                {
                    rt.Angle = rotation;
                }
            }
        }
        else
        {
            var st = target.RenderTransform as ScaleTransform;
            if (st == null)
            {
                target.RenderTransform = new ScaleTransform(scale, scale);
            }
            else
            {
                st.ScaleX = scale;
                st.ScaleY = scale;
            }
        }

        // Scale stroke thickness proportionally for player markers
        if (el.Tag is PlayerMarkerTag pmt && pmt.AvatarCircle != null)
        {
            double baseStroke = pmt.IsDot ? 1.0 : 1.0;
            pmt.AvatarCircle.StrokeThickness = Math.Max(0.5, baseStroke / _playerMarkerScale);
        }
    }

    private void SliderPlayerIconSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _playerMarkerScale = e.NewValue;
        if (_vm != null && !_vm.IsInitializing) TrackingService.MapPlayerIconScale = _playerMarkerScale;
        RefreshAllOverlayScales();
    }

    private void BtnAbbreviateNames_Toggled(object sender, RoutedEventArgs e)
    {
        _abbreviateNames = BtnAbbreviateNames.IsChecked == true;
        if (_vm != null && !_vm.IsInitializing) TrackingService.MapAbbreviateNames = _abbreviateNames;
        
        foreach (var t in TeamMembers) t.Abbreviate = _abbreviateNames;
        RefreshStreamerModeUI();

        if (_abbreviateNames)
        {
            if (BtnToggleServerArea != null && BtnToggleServerArea.IsChecked == false)
            {
                BtnToggleServerArea.IsChecked = true;
                BtnToggleServerArea_Click(null, null);
            }

            if (!TrackingService.HideConsole)
            {
                TrackingService.HideConsole = true;
                if (TxtLog != null) TxtLog.Visibility = Visibility.Collapsed;
            }
        }

        foreach (var kv in _dynEls.ToList())
        {
            if (kv.Value is FrameworkElement el && el.Tag is PlayerMarkerTag tag)
            {
                if (tag.SteamId == 0 || tag.IsDeathPin) continue;
                var sid = tag.SteamId;
                var name = TeamMembers.FirstOrDefault(t => t.SteamId == sid)?.Name ?? "player";
                if (_lastPresence.TryGetValue(sid, out var p))
                {
                    var online = p.Item1;
                    var dead = p.Item2;
                    UpdatePlayerMarker(ref el, kv.Key, sid, name, online, dead);
                }
                else
                {
                    UpdatePlayerMarker(ref el, kv.Key, sid, name, online: false, dead: false);
                }
            }
        }
    }

    /// <summary>
    /// Reads all player marker settings from TrackingService and syncs them into
    /// the toolbar controls and internal state. Called by AppSettingsOverlay after
    /// the user changes marker settings there.
    /// </summary>
    public void SyncPlayerSettingsFromTrackingService()
    {
        // Profile markers
        _showProfileMarkers = TrackingService.MapShowSteamMarkers;
        if (ChkProfileMarkers != null) ChkProfileMarkers.IsChecked = _showProfileMarkers;
        ChkProfileMarkers_Toggled(null, null!);

        // Player arrows
        _showPlayerArrows = TrackingService.MapShowPlayerArrows;
        if (ChkPlayerArrows != null) ChkPlayerArrows.IsChecked = _showPlayerArrows;
        ChkPlayerArrows_Toggled(null, null!);

        // Death markers
        _showDeathMarkers = TrackingService.MapShowDeathTags;
        if (ChkDeathMarkers != null) ChkDeathMarkers.IsChecked = _showDeathMarkers;
        RedrawDeathPins();

        // Streamer / abbreviate names
        _abbreviateNames = TrackingService.MapAbbreviateNames;
        if (BtnAbbreviateNames != null) BtnAbbreviateNames.IsChecked = _abbreviateNames;

        // Player icon scale
        _playerMarkerScale = TrackingService.MapPlayerIconScale;
        if (SliderPlayerIconSize != null) SliderPlayerIconSize.Value = _playerMarkerScale;
        RefreshAllOverlayScales();
    }
}
