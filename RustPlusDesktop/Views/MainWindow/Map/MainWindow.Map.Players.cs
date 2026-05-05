using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private const double PlayerAvatarSize = 24;

    private sealed class PlayerMarkerTag
    {
        public ulong SteamId;
        public TextBlock NameText = null!;
        public string? Name { get; set; }
        public Ellipse? AvatarCircle;
        public double Radius;
        public bool IsDeathPin { get; set; }
        public bool IsDot;

        public double ScaleExp { get; set; } = SHOP_SIZE_EXP;
        public double ScaleBaseMult { get; set; } = 1.0;
        public FrameworkElement? ScaleTarget { get; set; }
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
        var brush = dead ? Brushes.IndianRed : (online ? Brushes.LimeGreen : Brushes.LightGray);

        var dot = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = brush,
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            Margin = new Thickness(0, 0, 4, 0),
        };

        var tb = new TextBlock
        {
            Text = name,
            Foreground = brush,
            FontSize = 12,
            Margin = new Thickness(6, -2, 0, 0)
        };

        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(dot);
        sp.Children.Add(tb);
        ToolTipService.SetToolTip(sp, name);

        sp.Tag = new PlayerMarkerTag
        {
            SteamId = sid,
            Name = name,
            NameText = tb,
            AvatarCircle = null,
            Radius = 5,
            IsDeathPin = false,
            IsDot = true,
            ScaleExp = 1.05,
            ScaleBaseMult = 1.0,
            ScaleTarget = sp,
            ScaleCenterX = 5.0,
            ScaleCenterY = 5.0
        };

        return sp;
    }

    private FrameworkElement BuildPlayerMarker(ulong sid, string name, bool online, bool dead)
    {
        var brush = dead ? Brushes.IndianRed : (online ? Brushes.LimeGreen : Brushes.Gray);
        var avatar = GetAvatar(sid);

        if (avatar == null)
        {
            var dot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = brush,
                Stroke = Brushes.Black,
                StrokeThickness = 2,
                Margin = new Thickness(0, 0, 4, 0)
            };
            var tb = new TextBlock { Text = name, Foreground = brush, FontSize = 12, Margin = new Thickness(6, -2, 0, 0) };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(dot);
            sp.Children.Add(tb);
            sp.Tag = new PlayerMarkerTag
            {
                SteamId = sid,
                NameText = tb,
                AvatarCircle = null,
                Radius = 5,
                IsDot = true,
                ScaleExp = 1.05,
                ScaleBaseMult = 1.0,
                ScaleTarget = sp,
                ScaleCenterX = 5.0,
                ScaleCenterY = 5.0
            };
            Panel.SetZIndex(sp, 905);
            ApplyCurrentOverlayScale(sp);
            return sp;
        }
        else
        {
            var tb = new TextBlock { Text = name, Foreground = brush, FontSize = 12, Margin = new Thickness(6, -2, 0, 0) };
            var circle = new Ellipse
            {
                Width = PlayerAvatarSize,
                Height = PlayerAvatarSize,
                Stroke = Brushes.Black,
                StrokeThickness = 2,
                Fill = new ImageBrush(avatar) { Stretch = Stretch.UniformToFill }
            };

            var host = new Grid();
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var avatarHost = new Grid { Width = PlayerAvatarSize, Height = PlayerAvatarSize, Margin = new Thickness(0, 0, 4, 0) };
            avatarHost.Children.Add(circle);

            host.Children.Add(avatarHost);
            Grid.SetColumn(avatarHost, 0);
            host.Children.Add(tb);
            Grid.SetColumn(tb, 1);

            host.Tag = new PlayerMarkerTag
            {
                SteamId = sid,
                NameText = tb,
                AvatarCircle = circle,
                Radius = PlayerAvatarSize * 0.5,
                ScaleExp = 0.85,
                ScaleBaseMult = 1.0,
                ScaleTarget = host,
                ScaleCenterX = PlayerAvatarSize * 0.5,
                ScaleCenterY = PlayerAvatarSize * 0.5,
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
        if (!_showProfileMarkers)
        {
            var brush = dead ? Brushes.IndianRed : (online ? Brushes.LimeGreen : Brushes.LightGray);

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
                t.NameText.Text = name;
                t.NameText.Foreground = brush;

                if (t.NameText.Parent is Panel sp)
                {
                    var dot = sp.Children.OfType<Ellipse>().FirstOrDefault();
                    Panel.SetZIndex(dot, 905);
                    if (dot != null) dot.Fill = brush;
                }
            }

            ToolTipService.SetToolTip(el, name);
            return;
        }

        var brush2 = dead ? Brushes.IndianRed : (online ? Brushes.LimeGreen : Brushes.LightGray);
        var avatar = GetAvatarForMap(sid);

        if (el.Tag is PlayerMarkerTag tag)
        {
            if (tag.NameText != null) tag.NameText.Text = name;
            if (tag.NameText != null) tag.NameText.Foreground = brush2;

            if (avatar != null && tag.AvatarCircle == null ||
                avatar == null && tag.AvatarCircle != null)
            {
                var newEl = BuildPlayerMarker(sid, name, online, dead);
                int idx = Overlay.Children.IndexOf(el);
                if (idx >= 0) { Overlay.Children.RemoveAt(idx); Overlay.Children.Insert(idx, newEl); }
                else Overlay.Children.Add(newEl);
                _dynEls[key] = newEl; el = newEl;
            }
            else if (tag.AvatarCircle != null && avatar != null)
            {
                tag.AvatarCircle.Fill = new ImageBrush(avatar) { Stretch = Stretch.UniformToFill };
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

    private void ChkDeathMarkers_Toggled(object? sender, RoutedEventArgs e)
    {
        _showDeathMarkers = ChkDeathMarkers.IsChecked == true;
        if (!_showDeathMarkers) ClearAllDeathPins();
    }

    private void RefreshAllOverlayScales()
    {
        foreach (var fe in _dynEls.Values)
            ApplyCurrentOverlayScale(fe);

        foreach (var fe in _deathPins.Values)
            ApplyCurrentOverlayScale(fe);

        RefreshShopIconScales();
    }

    private FrameworkElement BuildDeathPin(ulong sid, string name, ImageSource? avatarFromCaller = null)
    {
        var avatar = GetTeamAvatar(sid);

        var root = new Grid
        {
            Width = PinW,
            Height = PinH,

            Tag = new PlayerMarkerTag
            {
                SteamId = sid,
                Name = name,
                IsDeathPin = true,
                ScaleExp = 0.8,
                ScaleBaseMult = 0.9,
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

        ToolTipService.SetToolTip(root, $"{name} (death)");
        ApplyCurrentOverlayScale(root);
        return root;
    }

    private void PlaceDeathPin(TeamMemberVM vm)
    {
        if (!_showDeathMarkers) return;
        if (!(vm.X.HasValue && vm.Y.HasValue)) return;

        var px = WorldToImagePx(vm.X.Value, vm.Y.Value);
        var el = BuildDeathPin(vm.SteamId, vm.Name, GetTeamAvatar(vm.SteamId));

        if (_deathPins.TryGetValue(vm.SteamId, out var old))
        {
            Overlay.Children.Remove(old);
            _deathPins.Remove(vm.SteamId);
        }

        Overlay.Children.Add(el);
        Panel.SetZIndex(el, 805);
        ApplyCurrentOverlayScale(el);

        var cx = px.X - PinW / 2.0;
        var cy = px.Y - (CircleTop + Circle / 2.0);
        Canvas.SetLeft(el, cx);
        Canvas.SetTop(el, cy);

        _deathPins[vm.SteamId] = el;
    }

    private void PlaceOrMoveDeathPin(ulong sid, double worldX, double worldY, string name)
    {
        var px = WorldToImagePx(worldX, worldY);
        var el = _deathPins.TryGetValue(sid, out var exist) ? exist : null;

        if (el == null)
        {
            el = BuildDeathPin(sid, name);
            _deathPins[sid] = el;
            Overlay.Children.Add(el);
            Panel.SetZIndex(el, 805);
            ApplyCurrentOverlayScale(el);
        }

        var cx = px.X - (PinW / 2.0);
        var cy = px.Y - PinH;
        Canvas.SetLeft(el, cx);
        Canvas.SetTop(el, cy);
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
        }

        double scale = CalcOverlayScale(eff, exp, baseMult);
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
}
