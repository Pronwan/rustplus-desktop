using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk
{
    /// <summary>
    /// Named arrangements of the dock: save the one you have, load another, and see what one
    /// looks like before committing to it.
    /// </summary>
    public partial class MiniMapWindow
    {
        private const string PresetCacheKey = "minimap_dock_presets";

        private CommandDockTemplates? _templates;

        public IReadOnlyList<CommandDockPreset> Presets =>
            (StorageService.LoadCache<CommandDockPresetStore>(PresetCacheKey) ?? new CommandDockPresetStore())
                .Presets;

        /// <summary>
        /// The arrangement that is always there and cannot be removed.
        ///
        /// Not stored: it is built on demand, so it cannot be renamed away, deleted by
        /// accident, or quietly diverge from what a fresh install starts with. It is the way
        /// back — one click from any arrangement to the bare mini-map in the corner, which is
        /// where the Mini button used to put it and what people mean by "undo all this".
        /// </summary>
        public const string DefaultPresetId = "builtin-default";

        /// <summary>The mini-map's original size and corner, as the main window first places it.</summary>
        private const double DefaultMapSize = 260;

        private static CommandDockPreset BuiltInDefault() => new()
        {
            Id = DefaultPresetId,
            Name = Helpers.Loc.Text("CommandDockTemplateDefault", "Map only (default)"),
            Tiles = new List<CommandDockTile>
            {
                new() { Id = CommandDockTileKinds.MapTileId, Kind = CommandDockTileKinds.Map, Col = 0, Row = 0 },
            },
            GrowRight = false,
            MapSize = DefaultMapSize,

            // The one preset that does state a shape. It is the way back to the beginning, and a
            // dock still showing a 16:9 strip is not back at the beginning.
            MapShapeIndex = 0,

            // Same reasoning: back to the beginning means back to the original pitch.
            GridZoom = 1.0,
        };

        /// <summary>A saved arrangement, or the built-in one. Null for an id that is neither.</summary>
        private CommandDockPreset? FindPreset(string id) =>
            id == DefaultPresetId ? BuiltInDefault() : Presets.FirstOrDefault(p => p.Id == id);

        private void SavePresets(List<CommandDockPreset> presets) =>
            StorageService.SaveCache(PresetCacheKey, new CommandDockPresetStore { Presets = presets });

        private void BtnTemplates_Click(object sender, RoutedEventArgs e)
        {
            // A closed Window cannot be shown again, so a new one is made rather than reused.
            if (_templates == null)
            {
                _templates = new CommandDockTemplates { Owner = this };
                _templates.Closed += (_, __) => { _templates = null; HidePresetPreview(); };
            }

            _templates.Dock = this;
            _templates.Refresh();
            _templates.Show();
            _templates.Activate();
        }

        // ── Saving and loading ──────────────────────────────────────────────────

        /// <summary>
        /// Stores the arrangement under a name, replacing one of the same name.
        ///
        /// The tiles are copied through a serialisation round trip rather than by reference: a
        /// preset that shared its tile objects with the live dock would change every time a tile
        /// was moved after saving it.
        /// </summary>
        public void SavePreset(string name)
        {
            name = name.Trim();
            if (name.Length == 0) return;

            var presets = Presets.ToList();
            presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            presets.Add(new CommandDockPreset
            {
                Name = name,
                Tiles = CopyTiles(_dock.Tiles),
                GrowRight = _dock.GrowRight,
                MapSize = MapTile != null ? _mapWidth : null,
                MapShapeIndex = MapTile != null ? _shapeIndex : null,
                GridZoom = _dock.GridZoom,
            });

            SavePresets(presets);
        }

        public void RenamePreset(string id, string name)
        {
            // Guarded here rather than only in the window: the built-in is not in the store,
            // so renaming it would silently write a second arrangement under its name.
            if (id == DefaultPresetId) return;

            name = name.Trim();
            if (name.Length == 0) return;

            var presets = Presets.ToList();
            var preset = presets.FirstOrDefault(p => p.Id == id);
            if (preset == null) return;

            preset.Name = name;
            SavePresets(presets);
        }

        public void DeletePreset(string id)
        {
            if (id == DefaultPresetId) return;

            var presets = Presets.ToList();
            presets.RemoveAll(p => p.Id == id);
            SavePresets(presets);
        }

        /// <summary>
        /// Puts an arrangement in place. The dock's position, its appearance defaults and the
        /// lock are left alone — those belong to the desk, not to the layout.
        /// </summary>
        public void ApplyPreset(string id)
        {
            var preset = FindPreset(id);
            if (preset == null) return;

            HidePresetPreview();
            CloseTileSettings();

            _dock.Tiles = CopyTiles(preset.Tiles);
            _dock.GrowRight = preset.GrowRight;
            _dock.MapRemoved = preset.Tiles.All(t => t.Kind != CommandDockTileKinds.Map);

            // The geometry goes in before anything is laid out, and quietly - the public setters
            // each lay the dock out as a side effect, which is the whole problem here.
            //
            // Overlap resolution measures tiles against the map's cell footprint, and that
            // footprint comes from the map's size, its shape and the grid's pitch. Applying the
            // preset's values *after* the first layout meant the tiles were resolved against the
            // previous arrangement's map: a tile this preset puts beside a small map was pushed
            // out from under the big one that was still there. ResolveOverlaps writes to
            // tile.Col and tile.Row, so the push stuck - and loading the same preset again
            // looked like a fix, because by then the geometry already matched.
            if (preset.GridZoom is { } zoom) _dock.GridZoom = CommandDockLayout.ClampZoom(zoom);
            if (preset.MapShapeIndex is { } shape) _shapeIndex = Math.Max(0, Math.Min(2, shape));
            if (preset.MapSize is { } size)
            {
                _mapWidth = Math.Max(160, Math.Min(size, 800));
                _mapHeight = MapHeightFor(_mapWidth, _shapeIndex);
            }

            SaveDock();

            // Every element on the canvas belongs to the tiles that were just replaced, so this
            // is a rebuild rather than a re-layout - and it now measures against the geometry
            // above rather than whatever was on screen a moment ago.
            RebuildTiles();

            // Applies what was set quietly: the map container's size, its corner radius, the
            // clip and the viewbox, and the sliders that show them.
            UpdateSize(_mapWidth, updateSlider: true);

            PersistMapShape(_shapeIndex);
            SettingsOverlay?.SyncShapeSelection(_shapeIndex);
            SettingsOverlay?.SyncGridZoom(DockZoom);
            _overlay?.SetZoom(DockZoom);

            // The one arrangement that moves the window as well.
            //
            // Every other preset deliberately leaves the dock where it is — position belongs
            // to the desk, not the layout. This one is the way back to the beginning, and a
            // dock that has been dragged to the middle of the screen while it held eight
            // widgets is not back at the beginning while it sits there holding one.
            if (id == DefaultPresetId) MoveToDefaultCorner();
        }

        /// <summary>Top-right of the working area, where the Mini button first puts it.</summary>
        private void MoveToDefaultCorner()
        {
            Left = SystemParameters.WorkArea.Right - DefaultMapSize - 20;
            Top = SystemParameters.WorkArea.Top + 20;

            ClampToScreen(pullIntoView: true);
            AnchorOriginToWindow();
            SaveDockPosition();
            FollowAiAnswer();
        }

        private static List<CommandDockTile> CopyTiles(IEnumerable<CommandDockTile> tiles)
        {
            var json = JsonSerializer.Serialize(tiles);
            return JsonSerializer.Deserialize<List<CommandDockTile>>(json) ?? new List<CommandDockTile>();
        }

        // ── Preview ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Fades an outline of the arrangement over the dock.
        ///
        /// Drawn rather than applied: swapping the live layout to show a preview would resize
        /// the window, move the tiles and leave the dock half-changed if the pointer moved on
        /// mid-way. An outline says the same thing and can be dropped at any moment.
        ///
        /// It is laid out with the preset's own map size, not the current one, or a preset saved
        /// around a bigger map would be drawn in the wrong shape.
        /// </summary>
        public void ShowPresetPreview(string id)
        {
            var preset = FindPreset(id);
            if (preset == null) return;

            // The preset's own zoom, not the dock's: the outline is of the arrangement as saved,
            // and drawing it at the live pitch would show the right shape at the wrong scale.
            double previewZoom = CommandDockLayout.ClampZoom(preset.GridZoom ?? DockZoom);

            double mapW = preset.MapSize ?? 0;

            // The preset carries its shape, so the outline can be the footprint the arrangement
            // was actually built around. At 16:9 that is a noticeably shorter map, and the tiles
            // below it sit correspondingly higher.
            int previewShape = preset.MapShapeIndex ?? _shapeIndex;
            double mapH = previewShape == 2 ? mapW * 9.0 / 16.0 : mapW;

            // Uniform, exactly as the live grid is: the map sits over the cells rather than
            // displacing the ones past it. The outline has to agree with what loading the
            // preset will actually produce.
            double X(int col) => CommandDockLayout.CellOffset(col, previewZoom);
            double Y(int row) => CommandDockLayout.CellOffset(row, previewZoom);

            var rects = new List<(Rect Rect, bool IsMap)>();
            foreach (var tile in preset.Tiles)
            {
                bool isMap = tile.Kind == CommandDockTileKinds.Map;
                var rect = isMap
                    ? new Rect(X(tile.Col), Y(tile.Row), mapW, mapH)
                    : new Rect(X(tile.Col), Y(tile.Row),
                        CommandDockLayout.CellsToPixels(tile.ColSpan, previewZoom),
                        CommandDockLayout.CellsToPixels(tile.RowSpan, previewZoom));

                if (rect.Width <= 0 || rect.Height <= 0) continue;
                rects.Add((rect, isMap));
            }

            if (rects.Count == 0) return;

            // Normalised to its own top-left, which is what loading does: NormaliseCells slides
            // every tile so the first occupied cell is (0,0).
            double shiftX = -rects.Min(r => r.Rect.X);
            double shiftY = -rects.Min(r => r.Rect.Y);

            double width = rects.Max(r => r.Rect.Right) + shiftX;
            double height = rects.Max(r => r.Rect.Bottom) + shiftY;

            var corner = PreviewCorner(id, new Size(width, height));

            var overlay = Overlay();
            overlay.ShowPreview(rects.Select(r => (
                new Rect(
                    corner.X + r.Rect.X + shiftX - overlay.Left,
                    corner.Y + r.Rect.Y + shiftY - overlay.Top,
                    r.Rect.Width,
                    r.Rect.Height),
                r.IsMap)));
        }

        /// <summary>
        /// Where on screen the arrangement's top-left corner would end up.
        ///
        /// Every preset but one leaves the dock where it is - position belongs to the desk, not
        /// to the layout - so the corner is simply the dock's. The built-in default is the
        /// exception: it is the way back to the beginning and moves the dock to its original
        /// corner, which used to make its preview a lie, drawn over a dock that was about to
        /// move out from under it.
        ///
        /// The same clamp the load will apply is applied here too, so an arrangement wider than
        /// the screen is outlined where it will actually sit rather than where it would sit if
        /// the screen were bigger.
        /// </summary>
        private Point PreviewCorner(string id, Size size)
        {
            var screen = ScreenBoundsFor(this);

            double x = id == DefaultPresetId
                ? screen.Right - DefaultMapSize - 20
                : (double.IsNaN(Left) ? screen.Left : Left);

            double y = id == DefaultPresetId
                ? screen.Top + 20
                : (double.IsNaN(Top) ? screen.Top : Top);

            if (size.Width <= screen.Width)
                x = Math.Max(screen.Left, Math.Min(x, screen.Right - size.Width));

            y = Math.Max(screen.Top, y);
            if (y + size.Height > screen.Bottom)
                y = Math.Max(screen.Top, screen.Bottom - size.Height);

            return new Point(x, y);
        }

        public void HidePresetPreview() => _overlay?.HidePreview();
    }
}
