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
            });

            SavePresets(presets);
        }

        public void RenamePreset(string id, string name)
        {
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
            var preset = Presets.FirstOrDefault(p => p.Id == id);
            if (preset == null) return;

            HidePresetPreview();
            CloseTileSettings();

            _dock.Tiles = CopyTiles(preset.Tiles);
            _dock.GrowRight = preset.GrowRight;
            _dock.MapRemoved = preset.Tiles.All(t => t.Kind != CommandDockTileKinds.Map);

            SaveDock();

            // The rebuild comes first and unconditionally: every element on the canvas belongs
            // to the tiles that were just replaced, and UpdateSize only repositions what is
            // already there.
            RebuildTiles();

            if (preset.MapSize is { } size) UpdateSize(size, updateSlider: true);
        }

        private static List<CommandDockTile> CopyTiles(IEnumerable<CommandDockTile> tiles)
        {
            var json = JsonSerializer.Serialize(tiles);
            return JsonSerializer.Deserialize<List<CommandDockTile>>(json) ?? new List<CommandDockTile>();
        }

        // ── Preview ─────────────────────────────────────────────────────────────

        private Canvas? _presetPreview;

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
            var preset = Presets.FirstOrDefault(p => p.Id == id);
            if (preset == null) return;

            EnsurePreviewCanvas();
            _presetPreview!.Children.Clear();

            double mapW = preset.MapSize ?? 0;
            double mapH = mapW;   // the shape is an appearance setting; a square is close enough here

            var map = preset.Tiles.FirstOrDefault(t => t.Kind == CommandDockTileKinds.Map);
            int mapCols = mapW > 0 ? CommandDockLayout.PixelsToCells(mapW) : 0;
            int mapRows = mapH > 0 ? CommandDockLayout.PixelsToCells(mapH) : 0;

            double X(int col) => CommandDockLayout.CellOffset(col)
                + (map != null && col >= map.Col + mapCols ? mapW - CommandDockLayout.CellsToPixels(mapCols) : 0);
            double Y(int row) => CommandDockLayout.CellOffset(row)
                + (map != null && row >= map.Row + mapRows ? mapH - CommandDockLayout.CellsToPixels(mapRows) : 0);

            var rects = new List<(Rect Rect, bool IsMap)>();
            foreach (var tile in preset.Tiles)
            {
                bool isMap = tile.Kind == CommandDockTileKinds.Map;
                var rect = isMap
                    ? new Rect(X(tile.Col), Y(tile.Row), mapW, mapH)
                    : new Rect(X(tile.Col), Y(tile.Row),
                        CommandDockLayout.CellsToPixels(tile.ColSpan),
                        CommandDockLayout.CellsToPixels(tile.RowSpan));

                if (rect.Width <= 0 || rect.Height <= 0) continue;
                rects.Add((rect, isMap));
            }

            if (rects.Count == 0) return;

            // Shifted so the arrangement's own top-left corner meets the dock's, which is where
            // it would actually land once loaded.
            double shiftX = -rects.Min(r => r.Rect.X);
            double shiftY = -rects.Min(r => r.Rect.Y);

            foreach (var (rect, isMap) in rects)
            {
                var outline = new System.Windows.Shapes.Rectangle
                {
                    Width = rect.Width,
                    Height = rect.Height,
                    RadiusX = isMap ? Math.Min(rect.Width, rect.Height) / 2 : 10,
                    RadiusY = isMap ? Math.Min(rect.Width, rect.Height) / 2 : 10,
                    Fill = new SolidColorBrush(Color.FromArgb(0x26, 0x3F, 0xD7, 0xFF)),
                    Stroke = new SolidColorBrush(Color.FromArgb(0xCC, 0x3F, 0xD7, 0xFF)),
                    StrokeThickness = isMap ? 2 : 1.5,
                };
                Canvas.SetLeft(outline, rect.X + shiftX);
                Canvas.SetTop(outline, rect.Y + shiftY);
                _presetPreview.Children.Add(outline);
            }

            // Room for an arrangement larger than the dock currently is. Added to the size only,
            // so nothing already on screen moves.
            _previewExtra = new Size(
                Math.Max(0, rects.Max(r => r.Rect.Right) + shiftX - Width),
                Math.Max(0, rects.Max(r => r.Rect.Bottom) + shiftY - Height));
            LayoutDock();

            Fade(_presetPreview, 1, 160);
        }

        public void HidePresetPreview()
        {
            if (_presetPreview == null) return;

            Fade(_presetPreview, 0, 220);

            if (_previewExtra != default)
            {
                _previewExtra = default;
                LayoutDock();
            }
        }

        private Size _previewExtra;

        private void EnsurePreviewCanvas()
        {
            if (_presetPreview != null) return;

            _presetPreview = new Canvas { IsHitTestVisible = false, Opacity = 0 };
            PresetPreviewLayer.Children.Add(_presetPreview);
        }

        private static void Fade(UIElement element, double to, int ms) =>
            element.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { FillBehavior = FillBehavior.HoldEnd });
    }
}
