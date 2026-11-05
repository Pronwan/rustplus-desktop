using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;

namespace RustPlusDesk
{
    /// <summary>
    /// One cell that puts the whole dock away, and brings it back.
    ///
    /// A dock worth building is a dock in the way — of the game behind it, mostly. Dragging it
    /// off screen loses the arrangement, and closing it loses the dock. This hides every other
    /// tile where it stands, without touching a single cell, so the press that brings them back
    /// puts them back exactly as they were.
    /// </summary>
    public partial class MiniMapWindow
    {
        private const string GlyphCollapse = "";   // chevrons pointing in
        private const string GlyphExpand = "";     // chevrons pointing out

        private FrameworkElement BuildCollapseTile(CommandDockTile tile)
        {
            var style = StyleFor(tile);
            var shell = TileShell(style);
            shell.Tag = tile;

            bool collapsed = IsCollapsed;

            var glyph = new TextBlock
            {
                Text = collapsed ? GlyphExpand : GlyphCollapse,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = style.Size(18),
                Foreground = style.TextMain,
                Effect = style.TextShadow,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            shell.Child = glyph;
            ToolTipService.SetToolTip(shell, CollapseTooltip(collapsed));

            // The whole tile is the button, so the press goes through the tile's own interaction
            // handler rather than a control inside it — which is also what keeps a drag on it a
            // drag while the dock is unlocked.
            _tileRefreshers.Add(() =>
            {
                bool now = IsCollapsed;

                if (glyph.Text != (now ? GlyphExpand : GlyphCollapse))
                    glyph.Text = now ? GlyphExpand : GlyphCollapse;

                ToolTipService.SetToolTip(shell, CollapseTooltip(now));
            });

            return shell;
        }

        private static string CollapseTooltip(bool collapsed) => collapsed
            ? Loc.Text("CommandDockCollapseExpand", "Show the dock again")
            : Loc.Text("CommandDockCollapseHide", "Hide every other tile");

        /// <summary>
        /// Flips the dock between collapsed and not.
        ///
        /// A full rebuild rather than a refresh: which tiles exist on the canvas changes, and the
        /// window resizes around what is left, which is the layout's job and not a refresher's.
        /// </summary>
        internal void ToggleDockCollapsed()
        {
            _dock.Collapsed = !_dock.Collapsed;
            SaveDock();

            RebuildTiles();
        }
    }
}
