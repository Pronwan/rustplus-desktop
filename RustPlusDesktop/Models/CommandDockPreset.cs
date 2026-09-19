using System;
using System.Collections.Generic;

namespace RustPlusDesk.Models
{
    /// <summary>
    /// A saved arrangement of the dock.
    ///
    /// Holds the tiles and the map size, because the two are not separable: the map's footprint
    /// is what every other tile is placed around, and restoring the positions without it would
    /// put them somewhere the arrangement was never meant to be.
    ///
    /// Not held: where the dock sits on screen, the appearance defaults, or the lock. Those are
    /// about this desk and this session rather than about the arrangement, and carrying them
    /// along would make loading a layout move the window out from under the pointer.
    /// </summary>
    public sealed class CommandDockPreset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string Name { get; set; } = "";

        public DateTime SavedUtc { get; set; } = DateTime.UtcNow;

        public List<CommandDockTile> Tiles { get; set; } = new();

        public bool GrowRight { get; set; }

        /// <summary>The map's free size when the preset was saved, or null if it had no map.</summary>
        public double? MapSize { get; set; }

        /// <summary>
        /// The map's shape when the preset was saved - 0 circle, 1 square, 2 16:9 - or null if
        /// it had no map, or the preset predates this field.
        ///
        /// Belongs here for the same reason <see cref="MapSize"/> does: at 16:9 the map is a
        /// different height than it is round, so every tile placed under it sits somewhere else.
        /// Restoring the size without the shape puts them back against a footprint the
        /// arrangement was never built around.
        ///
        /// Null is read as "leave the shape alone" rather than as circle. An arrangement saved
        /// before this field existed has no opinion about the shape, and forcing one would undo
        /// a setting the user made somewhere else entirely.
        /// </summary>
        public int? MapShapeIndex { get; set; }

        /// <summary>
        /// The grid zoom this arrangement was built at, or null for one saved before zoom
        /// existed.
        ///
        /// Has to travel with the tiles for the same reason the map's size does: the cells are
        /// a different number of pixels at a different zoom, so an arrangement restored at the
        /// wrong one is the right shape at the wrong scale, and the window around it is sized
        /// for neither. Null means leave the zoom alone.
        /// </summary>
        public double? GridZoom { get; set; }
    }

    public sealed class CommandDockPresetStore
    {
        public List<CommandDockPreset> Presets { get; set; } = new();
    }
}
