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
    }

    public sealed class CommandDockPresetStore
    {
        public List<CommandDockPreset> Presets { get; set; } = new();
    }
}
