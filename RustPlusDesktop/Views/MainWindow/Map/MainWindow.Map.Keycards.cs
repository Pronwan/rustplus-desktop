using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    /// <summary>
    /// A place on the map that wants keycards, holds one, or both.
    ///
    /// Two separate questions, and the map shows them as two separate layers:
    /// "Card Readers" is which cards the doors here ask for, "Keycards" is which
    /// card a player can walk away with. A monument often does one and not the
    /// other - Launch Site has readers but no card, a supermarket the reverse.
    /// </summary>
    private sealed class KeycardSite
    {
        public string Name = "";
        public double X;
        public double Y;

        /// <summary>Card colours the doors here want.</summary>
        public List<string> Needs = new();

        /// <summary>Card colours findable here.</summary>
        public List<string> Holds = new();

        /// <summary>Separate rooms per colour. Only known for labs.</summary>
        public Dictionary<string, int> Rooms = new();
    }

    private List<KeycardSite>? _keycardSites;
    private readonly List<UIElement> _keycardElements = new();

    /// <summary>Icons already reported as missing, so the log is not flooded on redraw.</summary>
    private readonly HashSet<string> _keycardIconWarnings = new();

    private static readonly string[] KeycardColours = { "green", "blue", "red" };

    /// <summary>
    /// Ticking either layer before the map has been parsed offers to parse it, rather
    /// than presenting a dead control. The boxes stay enabled and are only dimmed, so
    /// the click actually arrives - a disabled CheckBox swallows it.
    /// </summary>
    private async void ChkKeycards_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox box && box.IsChecked == true && _keycardSites == null)
        {
            box.IsChecked = false;

            var profile = _vm?.Selected;
            if (profile == null) return;
            if (!await OfferMapParseForHeatmapAsync(profile)) return;

            // The parse loads the sites and switches both layers on by itself.
            return;
        }

        RedrawKeycards();
    }

    /// <summary>Switches both layers on once there is something to show.</summary>
    private void EnableKeycardLayersAfterParse()
    {
        UpdateKeycardToggleState();
        if (_keycardSites == null) return;

        if (ChkCardReaders?.IsEnabled == true) ChkCardReaders.IsChecked = true;
        if (ChkKeycards?.IsEnabled == true) ChkKeycards.IsChecked = true;
        RedrawKeycards();
    }

    private void ResetKeycardsForServerChange()
    {
        _keycardSites = null;

        foreach (var chk in new[] { ChkCardReaders, ChkKeycards })
        {
            if (chk == null) continue;
            chk.IsChecked = false;
        }

        KeycardLayer?.Children.Clear();
        _keycardElements.Clear();
        UpdateKeycardToggleState();
    }

    private void LoadCachedKeycardSitesForCurrentServer()
    {
        try
        {
            if (_vm?.Selected == null)
            {
                LoadKeycardSitesForCurrentMap(null);
                return;
            }

            string folder = Services.Map3DLocalBuildService.GetPreparedFolderPath(
                _vm.Selected, _vm.Selected.RustMapsMapId);
            LoadKeycardSitesForCurrentMap(folder);
        }
        catch (Exception ex)
        {
            AppendLog($"[Keycards] Could not load: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads keycardSites out of a parsed map. It is absent until the map file has
    /// been parsed, which is what leaves both toggles disabled.
    /// </summary>
    private void LoadKeycardSitesForCurrentMap(string? folderPath)
    {
        _keycardSites = null;

        try
        {
            string? path = string.IsNullOrWhiteSpace(folderPath)
                ? null
                : Path.Combine(folderPath, "map_data.json");

            if (path != null && File.Exists(path))
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var doc = JsonDocument.Parse(fs);

                if (doc.RootElement.TryGetProperty("keycardSites", out var arr) &&
                    arr.ValueKind == JsonValueKind.Array)
                {
                    var sites = new List<KeycardSite>();
                    foreach (var el in arr.EnumerateArray())
                    {
                        if (!el.TryGetProperty("x", out var xEl)) continue;
                        if (!el.TryGetProperty("y", out var yEl)) continue;

                        var site = new KeycardSite
                        {
                            Name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            X = xEl.GetDouble(),
                            Y = yEl.GetDouble(),
                            Needs = ReadStringList(el, "cards"),
                            Holds = ReadStringList(el, "contains")
                        };

                        if (el.TryGetProperty("rooms", out var rooms) &&
                            rooms.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var p in rooms.EnumerateObject())
                            {
                                if (p.Value.TryGetInt32(out int count)) site.Rooms[p.Name] = count;
                            }
                        }

                        if (site.Needs.Count > 0 || site.Holds.Count > 0) sites.Add(site);
                    }

                    if (sites.Count > 0)
                    {
                        _keycardSites = sites;
                        AppendLog($"[Keycards] {sites.Count} places loaded.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[Keycards] Could not read map data: {ex.Message}");
            _keycardSites = null;
        }

        UpdateKeycardToggleState();
        RedrawKeycards();
    }

    private static List<string> ReadStringList(JsonElement el, string property)
    {
        var list = new List<string>();
        if (!el.TryGetProperty(property, out var arr)) return list;
        if (arr.ValueKind != JsonValueKind.Array) return list;

        foreach (var v in arr.EnumerateArray())
        {
            string? s = v.GetString();
            if (!string.IsNullOrEmpty(s)) list.Add(s!);
        }
        return list;
    }

    /// <summary>
    /// Dims each toggle when there is nothing behind it, but leaves it enabled so a
    /// click still arrives - with no parsed map that click offers the parse, and with
    /// a parsed map that simply has no such place it does nothing.
    /// </summary>
    private void UpdateKeycardToggleState()
    {
        bool parsed = _keycardSites != null;
        bool hasNeeds = _keycardSites?.Any(s => s.Needs.Count > 0) == true;
        bool hasHolds = _keycardSites?.Any(s => s.Holds.Count > 0) == true;

        Apply(ChkCardReaders, hasNeeds);
        Apply(ChkKeycards, hasHolds);
        UpdateParseAllMonumentsButton();

        void Apply(System.Windows.Controls.CheckBox? box, bool hasData)
        {
            if (box == null) return;

            box.Opacity = hasData ? 1.0 : 0.45;
            box.ToolTip = parsed
                ? (hasData ? null : Helpers.Loc.Text("UiNoneOnThisMap", "None on this map."))
                : Helpers.Loc.Text("UiParseMapToEnableKeycards", "Parse the map file to enable keycard layers.");

            if (!hasData) box.IsChecked = false;
        }
    }

    /// <summary>
    /// The parse button only makes sense while there is nothing parsed yet.
    /// </summary>
    private void UpdateParseAllMonumentsButton()
    {
        if (BtnParseAllMonuments == null) return;
        BtnParseAllMonuments.Visibility = _keycardSites == null
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void BtnParseAllMonuments_Click(object sender, RoutedEventArgs e)
    {
        var profile = _vm?.Selected;
        if (profile == null) return;
        await OfferMapParseForHeatmapAsync(profile);
    }

    private void RedrawKeycards()
    {
        if (KeycardLayer == null) return;

        foreach (var el in _keycardElements) KeycardLayer.Children.Remove(el);
        _keycardElements.Clear();

        if (_isShowingDeepSeaMap) return;
        if (_keycardSites == null || _worldSizeS <= 0 || _worldRectPx.Width <= 0) return;

        bool showReaders = ChkCardReaders?.IsChecked == true;
        bool showCards = ChkKeycards?.IsChecked == true;
        if (!showReaders && !showCards) return;

        foreach (var site in _keycardSites)
        {
            var pt = CenteredRustToImagePx(site.X, site.Y);

            // Readers above the spot, findable cards below, so a place that does both
            // reads as two rows instead of one ambiguous pile.
            if (showReaders && site.Needs.Count > 0)
            {
                AddCardRow(pt, site.Needs, site.Rooms, isReader: true);
            }

            if (showCards && site.Holds.Count > 0)
            {
                AddCardRow(pt, site.Holds, null, isReader: false);
            }
        }
    }

    /// <summary>
    /// Draws one row of cards at a place. They overlap slightly so three still read as
    /// a set rather than a stripe, and a count badge appears only where the number is
    /// actually known - rooms, not doors.
    /// </summary>
    private void AddCardRow(Point anchor, List<string> colours, Dictionary<string, int>? rooms, bool isReader)
    {
        const double size = 22;

        var ordered = KeycardColours.Where(colours.Contains).ToList();
        if (ordered.Count == 0) return;

        // A badge belongs to the card it sits on, and at the tight spacing it landed
        // between two icons instead. Where counts are in play the cards get more room
        // and the badge moves to the top-right corner, clear of its neighbour.
        bool anyBadge = rooms != null && ordered.Any(c => rooms.TryGetValue(c, out int n) && n > 1);
        double overlap = anyBadge ? 0 : 7;
        double step = size - overlap + (anyBadge ? 3 : 0);

        double totalWidth = size + step * (ordered.Count - 1);
        double startX = anchor.X - totalWidth / 2;
        double y = isReader ? anchor.Y - size - 12 : anchor.Y + 12;

        for (int i = 0; i < ordered.Count; i++)
        {
            string colour = ordered[i];
            string asset = isReader
                ? "pack://application:,,,/Assets/icons/reader_" + colour + ".png"
                : "pack://application:,,,/Assets/keycards/keycard-" + colour + ".png";

            var img = new Image
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                IsHitTestVisible = false,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 4,
                    ShadowDepth = 1,
                    Opacity = 0.85,
                    Color = Colors.Black
                }
            };

            try
            {
                img.Source = new BitmapImage(new Uri(asset, UriKind.Absolute));
            }
            catch (Exception ex)
            {
                // One missing icon must not take the whole overlay with it, but it
                // must not pass unnoticed either: a pack:// URI only fails here, so
                // a forgotten <Resource> entry would otherwise just draw nothing.
                if (_keycardIconWarnings.Add(asset))
                {
                    AppendLog($"[Keycards] Icon missing: {asset} ({ex.Message})");
                }
                continue;
            }

            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            Canvas.SetLeft(img, startX + i * step);
            Canvas.SetTop(img, y);
            Panel.SetZIndex(img, 10 + i);

            KeycardLayer.Children.Add(img);
            _keycardElements.Add(img);

            // Only labs know how many separate rooms there are. For monuments the
            // reader count is doors, which is a different number and would mislead.
            if (rooms != null && rooms.TryGetValue(colour, out int count) && count > 1)
            {
                // Top-right of its own card, overhanging slightly, so it reads as
                // belonging to that one rather than sitting in the gap.
                var badge = BuildCountBadge(count);
                Canvas.SetLeft(badge, startX + i * step + size - 8);
                Canvas.SetTop(badge, y - 5);
                Panel.SetZIndex(badge, 40 + i);

                KeycardLayer.Children.Add(badge);
                _keycardElements.Add(badge);
            }
        }
    }

    private static Border BuildCountBadge(int count)
        => new()
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(3, 0, 3, 0),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = count.ToString(),
                Foreground = Brushes.White,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
}
