using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;

namespace RustPlusDesk
{
    /// <summary>
    /// The server clock and the player count, as small as they go.
    ///
    /// The mini-map has had both of these along its bottom edge for a long time — a glyph and a
    /// figure, nothing else — and people put them there because that is all they want to know at
    /// a glance. The dock's own clock and server tiles are the fuller version: a caption, how
    /// long until the light changes, a population graph. Both are worth having; these are the
    /// pair that costs one cell.
    ///
    /// Deliberately the same two readings from the same two sources as the mini-map's, so a dock
    /// with both on it never shows two different times.
    /// </summary>
    public partial class MiniMapWindow
    {
        /// <summary>The clock style that means "just the time".</summary>
        public const int ClockStyleMinimal = 3;

        /// <summary>Set while the window is only on screen because a key is being held.</summary>
        private bool _peeking;

        /// <summary>
        /// Lifts the dock over the game for as long as the peek key is held.
        ///
        /// Topmost rather than shown and hidden. The window stays where it is and keeps every
        /// tile alive; what changes is whether it is in front of a full-screen game or behind
        /// it. Hiding it outright was the first attempt and it was wrong twice over — the
        /// tiles went with it on a second monitor where nothing was in the way, and a window
        /// left hidden had no way back.
        /// </summary>
        public void ShowForPeek()
        {
            _peeking = true;

            Topmost = true;
            Activate();
        }

        /// <summary>
        /// Sets whether the dock sits in front of everything, or only while the key is held.
        ///
        /// Called when the window opens and whenever the key changes. With a key set the dock
        /// gives up being topmost, so a full-screen game covers it until the key brings it
        /// back; with no key it is in front as it always was.
        ///
        /// Visibility is forced on either way. An earlier version of this hid the window
        /// instead, and anybody who tried it is left with a dock that is still hidden.
        /// </summary>
        public void ApplyPeekVisibility()
        {
            bool peek = !string.IsNullOrWhiteSpace(Services.TrackingService.MiniMapPeekHotkey);

            _peeking = false;
            Visibility = Visibility.Visible;
            Topmost = !peek;
        }

        /// <summary>Lets the game cover it again, unless it was in front before the key.</summary>
        public void HideAfterPeek()
        {
            if (!_peeking) return;

            _peeking = false;
            Topmost = false;
        }

        private FrameworkElement BuildMinimalClockTile(CommandDockTile tile)
        {
            var style = StyleFor(tile);
            var shell = TileShell(style);
            shell.Tag = tile;

            // One cell is about sixty pixels of usable width once the frame is off it, so the
            // glyph and the figure sit on one line and the whole thing scales with the tile
            // rather than wrapping.
            double grow = System.Math.Min(2.6, 1 + (System.Math.Min(tile.ColSpan, tile.RowSpan) - 1) * 0.7);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            shell.Child = row;

            var glyph = new TextBlock
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = style.Size(13 * grow),
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Effect = style.TextShadow,
            };
            row.Children.Add(glyph);

            var time = new TextBlock
            {
                FontSize = style.Size(14 * grow),
                FontWeight = FontWeights.SemiBold,
                Foreground = style.TextMain,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = style.TextShadow,
            };
            row.Children.Add(time);

            _tileRefreshers.Add(() =>
            {
                var (reading, isDay, _) = DockHost?.DockServerTime ?? ("—", true, (System.TimeSpan?)null);

                time.Text = tile.Clock12Hour ? To12Hour(reading) : reading;

                // Sun or moon, in the colour the mini-map uses for each, because the colour is
                // half the reading: at a glance it says night before the digits are read.
                glyph.Text = isDay ? "" : "";
                glyph.Foreground = isDay
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F))
                    : new SolidColorBrush(Color.FromRgb(0x90, 0xCA, 0xF9));
            });

            return shell;
        }

        private FrameworkElement BuildMinimalPopulationTile(CommandDockTile tile)
        {
            var style = StyleFor(tile);
            var shell = TileShell(style);
            shell.Tag = tile;

            double grow = System.Math.Min(2.6, 1 + (System.Math.Min(tile.ColSpan, tile.RowSpan) - 1) * 0.7);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            shell.Child = row;

            row.Children.Add(new TextBlock
            {
                Text = "",   // people
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = style.Size(13 * grow),
                Margin = new Thickness(0, 0, 5, 0),
                Foreground = style.TextSub,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = style.TextShadow,
            });

            var count = new TextBlock
            {
                FontSize = style.Size(14 * grow),
                FontWeight = FontWeights.SemiBold,
                Foreground = style.TextMain,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = style.TextShadow,
            };
            row.Children.Add(count);

            _tileRefreshers.Add(() =>
            {
                var (current, max, queue) = DockHost?.DockPopulation ?? (0, 0, "");

                // The capacity as well, where the server reports one: "128" alone says nothing
                // about whether that is a full server or an empty one.
                count.Text = current > 0 || max > 0
                    ? (max > 0 ? $"{current}/{max}" : current.ToString())
                    : "—";

                // A queue is the one thing worth stealing space for, because it is the thing
                // that decides whether to disconnect.
                ToolTipService.SetToolTip(shell, string.IsNullOrEmpty(queue)
                    ? null
                    : string.Format(Loc.Text("CommandDockServerQueue", "{0} waiting"), queue));
            });

            return shell;
        }
    }
}
