using System;
using System.Collections.Generic;

namespace RustPlusDesk.Models
{
    /// <summary>
    /// One tile on the mini-map's command dock.
    ///
    /// <see cref="Kind"/> is a string rather than an enum on purpose: a layout saved by a newer
    /// build can be read by an older one, which drops the tiles it cannot render instead of
    /// failing to parse the file and wiping the whole dock.
    /// </summary>
    public sealed class CommandDockTile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>Clock | Device | TeamChat | ClanChat | Event | Rule</summary>
        public string Kind { get; set; } = CommandDockTileKinds.Clock;

        public int Col { get; set; }
        public int Row { get; set; }
        public int ColSpan { get; set; } = 1;
        public int RowSpan { get; set; } = 1;

        /// <summary>Device tiles: the paired entity this tile controls or reports on.</summary>
        public uint EntityId { get; set; }

        /// <summary>Rule tiles: the Logic Engine rule this tile launches.</summary>
        public string? RuleId { get; set; }

        /// <summary>Event tiles: cargo | deepsea | oilrig | heli | chinook | vendor.</summary>
        public string? EventKey { get; set; }

        /// <summary>Clock tiles: 0 digital, 1 analog, 2 Rust style.</summary>
        public int ClockStyle { get; set; }

        /// <summary>Clock tiles: append the time left until sunrise or sunset.</summary>
        public bool ClockShowDayNight { get; set; } = true;

        /// <summary>Chat tiles: shorten long names so the message still fits on one line.</summary>
        public bool ChatAbbreviateNames { get; set; } = true;
    }

    public static class CommandDockTileKinds
    {
        public const string Clock = "Clock";
        public const string Device = "Device";
        public const string TeamChat = "TeamChat";
        public const string ClanChat = "ClanChat";
        public const string Event = "Event";
        public const string Rule = "Rule";
    }

    /// <summary>
    /// The dock's saved arrangement. Positions are grid cells whose origin is the map tile's
    /// top-left corner, so the dock keeps its shape when the map is resized: only the number of
    /// cells the map covers changes, and the auto-arrange pushes tiles out of the way.
    /// </summary>
    public sealed class CommandDockLayout
    {
        public List<CommandDockTile> Tiles { get; set; } = new();

        /// <summary>Cell size and gap in device-independent pixels.</summary>
        public const double CellSize = 74;
        public const double CellGap = 8;

        public static double CellsToPixels(int cells) =>
            cells <= 0 ? 0 : cells * CellSize + (cells - 1) * CellGap;

        public static double CellOffset(int index) => index * (CellSize + CellGap);

        /// <summary>How many cells a free-size element such as the map tile covers.</summary>
        public static int PixelsToCells(double pixels) =>
            Math.Max(1, (int)Math.Ceiling((pixels + CellGap) / (CellSize + CellGap)));
    }
}
