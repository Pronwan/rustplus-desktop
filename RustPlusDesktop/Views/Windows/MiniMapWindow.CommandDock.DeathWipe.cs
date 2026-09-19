using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;

namespace RustPlusDesk
{
    /// <summary>
    /// One cell that clears the map's death markers, and says how many there are.
    ///
    /// A wipe's worth of pins builds up on the map, and until now the only way to clear them was
    /// a button under the team list - four levels away from the map it was about. The count is
    /// the other half: the reason to press this is that the map has become unreadable, and a
    /// number on the tile is what tells you that before you go looking.
    /// </summary>
    public partial class MiniMapWindow
    {
        private const string DeathWipeIconUri =
            "pack://application:,,,/Assets/icons/map-markers/assets_markers_iconmap_skull.png";

        /// <summary>
        /// Decoded once and shared. At one cell the icon is drawn small, and decoding the full
        /// bitmap per rebuild is the kind of cost that only shows up as a stutter while dragging.
        /// </summary>
        private static BitmapImage? _deathWipeIcon;

        private static ImageSource? DeathWipeIcon()
        {
            if (_deathWipeIcon != null) return _deathWipeIcon;

            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = new Uri(DeathWipeIconUri, UriKind.Absolute);
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.DecodePixelWidth = 48;
                img.EndInit();
                img.Freeze();
                _deathWipeIcon = img;
            }
            catch
            {
                // A missing icon costs the picture, not the tile - the count still reads.
                return null;
            }

            return _deathWipeIcon;
        }

        private FrameworkElement BuildDeathWipeTile(CommandDockTile tile)
        {
            var style = StyleFor(tile);
            var shell = TileShell(style);
            shell.Tag = tile;

            var stack = new Grid();

            var icon = new Image
            {
                Source = DeathWipeIcon(),
                Width = style.Size(24),
                Height = style.Size(24),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.9,
                Effect = style.TextShadow,
            };
            stack.Children.Add(icon);

            // Bottom right, out of the icon's way. Mirrors the map rather than the stored list:
            // what the tile counts is what the user can see.
            var count = new TextBlock
            {
                FontSize = style.Size(11),
                FontWeight = FontWeights.SemiBold,
                Foreground = style.TextMain,
                Effect = style.TextShadow,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 2, 0),
            };
            stack.Children.Add(count);

            shell.Child = stack;

            void Apply()
            {
                int n = DockHost?.DockDeathMarkerCount ?? 0;

                count.Text = n > 0 ? n.ToString() : "";

                // Nothing to clear reads as nothing to press. The tile stays where it is rather
                // than disappearing, because a button that comes and goes is a button you cannot
                // build a layout around.
                icon.Opacity = n > 0 ? 0.9 : 0.35;
                shell.Opacity = n > 0 ? 1.0 : 0.6;

                ToolTipService.SetToolTip(shell, DeathWipeTooltip(tile, n));
            }

            Apply();
            _tileRefreshers.Add(Apply);

            return shell;
        }

        private static string DeathWipeTooltip(CommandDockTile tile, int count)
        {
            if (count == 0)
                return Loc.Text("CommandDockDeathWipeEmpty", "No death markers on the map");

            string what = tile.DeathWipeKeepLatest
                ? Loc.Text("CommandDockDeathWipeKeepLatest", "Wipe all but the last one")
                : Loc.Text("CommandDockDeathWipeAll", "Wipe all death markers");

            return $"{what}  ({count})";
        }

        /// <summary>
        /// Clears the markers the tile is set to clear.
        ///
        /// No confirmation, on purpose: the tile is one cell the user put on their own dock and
        /// the count on it says what is about to go. A dialog over the game to confirm clearing
        /// map pins would cost more than getting it wrong does.
        /// </summary>
        internal void WipeDeathMarkers(CommandDockTile tile)
        {
            DockHost?.WipeDockDeathMarkers(tile.DeathWipeKeepLatest);
            RefreshTiles();
        }
    }
}
